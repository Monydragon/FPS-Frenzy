using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using FpsFrenzy.Core.Data;

namespace FpsFrenzy.Core.Simulation;

using BepuSimulation = BepuPhysics.Simulation;

internal sealed class BepuPlayerController : IDisposable
{
    private const float CapsuleRadius = 0.35f;
    private const float CapsuleLength = 1.1f;
    private const float CapsuleCenterToEye = 0.75f;
    private readonly BufferPool _bufferPool = new();
    private readonly BepuSimulation _simulation;
    private readonly BodyHandle _bodyHandle;

    public BepuPlayerController(ArenaDefinition arena)
    {
        _simulation = BepuSimulation.Create(
            _bufferPool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks(new Vector3(0f, -24f, 0f)),
            new SolveDescription(8, 1));

        foreach (ArenaPrimitiveDefinition primitive in arena.Primitives)
        {
            if (!primitive.HasCollision)
            {
                continue;
            }

            TypedIndex shape = _simulation.Shapes.Add(new Box(
                primitive.Size.X,
                primitive.Size.Y,
                primitive.Size.Z));
            Vector3 radians = primitive.RotationDegrees * (MathF.PI / 180f);
            Quaternion orientation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
            _simulation.Statics.Add(new StaticDescription(primitive.Position, orientation, shape));
        }

        Capsule capsule = new(CapsuleRadius, CapsuleLength);
        BodyInertia inertia = capsule.ComputeInertia(80f);
        inertia.InverseInertiaTensor = default;
        TypedIndex capsuleShape = _simulation.Shapes.Add(capsule);
        Vector3 center = arena.PlayerSpawn - new Vector3(0f, CapsuleCenterToEye, 0f);
        _bodyHandle = _simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(center),
            inertia,
            new CollidableDescription(capsuleShape, 0.1f),
            new BodyActivityDescription(0.01f)));
    }

    public PlayerPhysicsResult Step(Vector3 desiredVelocity, bool jump, bool wasGrounded, float deltaSeconds)
    {
        BodyReference body = _simulation.Bodies.GetBodyReference(_bodyHandle);
        Vector3 velocity = body.Velocity.Linear;
        float acceleration = wasGrounded ? 32f : 9f;
        float blend = 1f - MathF.Exp(-acceleration * deltaSeconds);
        velocity.X = float.Lerp(velocity.X, desiredVelocity.X, blend);
        velocity.Z = float.Lerp(velocity.Z, desiredVelocity.Z, blend);
        if (jump && wasGrounded)
        {
            velocity.Y = 7.6f;
        }

        body.Velocity.Linear = velocity;
        body.Velocity.Angular = Vector3.Zero;
        body.Pose.Orientation = Quaternion.Identity;
        body.Awake = true;
        _simulation.Timestep(deltaSeconds);

        body = _simulation.Bodies.GetBodyReference(_bodyHandle);
        body.Pose.Orientation = Quaternion.Identity;
        GroundRayHitHandler groundHit = new();
        _simulation.RayCast(body.Pose.Position, -Vector3.UnitY, 1.02f, ref groundHit);
        bool grounded = groundHit.Hit && body.Velocity.Linear.Y <= 0.5f;
        if (grounded && body.Velocity.Linear.Y < 0f)
        {
            body.Velocity.Linear.Y = 0f;
        }

        return new PlayerPhysicsResult(
            body.Pose.Position + new Vector3(0f, CapsuleCenterToEye, 0f),
            body.Velocity.Linear.Y,
            grounded);
    }

    public void Teleport(Vector3 eyePosition)
    {
        BodyReference body = _simulation.Bodies.GetBodyReference(_bodyHandle);
        body.Pose.Position = eyePosition - new Vector3(0f, CapsuleCenterToEye, 0f);
        body.Pose.Orientation = Quaternion.Identity;
        body.Velocity.Linear = Vector3.Zero;
        body.Velocity.Angular = Vector3.Zero;
        body.Awake = true;
    }

    public void Dispose()
    {
        _simulation.Dispose();
        _bufferPool.Clear();
    }

    private struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(BepuSimulation simulation) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool AllowContactGeneration(
            int workerIndex,
            CollidableReference a,
            CollidableReference b,
            ref float speculativeMargin) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool AllowContactGeneration(
            int workerIndex,
            CollidablePair pair,
            int childIndexA,
            int childIndexB) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ConfigureContactManifold<TManifold>(
            int workerIndex,
            CollidablePair pair,
            ref TManifold manifold,
            out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial = new PairMaterialProperties
            {
                FrictionCoefficient = 0.8f,
                MaximumRecoveryVelocity = 2f,
                SpringSettings = new SpringSettings(30f, 1f),
            };
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ConfigureContactManifold(
            int workerIndex,
            CollidablePair pair,
            int childIndexA,
            int childIndexB,
            ref ConvexContactManifold manifold) => true;

        public readonly void Dispose() { }
    }

    private struct PoseIntegratorCallbacks(Vector3 gravity) : IPoseIntegratorCallbacks
    {
        private Vector3Wide _gravityWideDt;

        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        public readonly void Initialize(BepuSimulation simulation) { }

        public void PrepareForIntegration(float deltaSeconds) =>
            Vector3Wide.Broadcast(gravity * deltaSeconds, out _gravityWideDt);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void IntegrateVelocity(
            Vector<int> bodyIndices,
            Vector3Wide position,
            QuaternionWide orientation,
            BodyInertiaWide localInertia,
            Vector<int> integrationMask,
            int workerIndex,
            Vector<float> deltaSeconds,
            ref BodyVelocityWide velocity) =>
            velocity.Linear += _gravityWideDt;
    }

    private struct GroundRayHitHandler : IRayHitHandler
    {
        public bool Hit { get; private set; }

        public readonly bool AllowTest(CollidableReference collidable) =>
            collidable.Mobility == CollidableMobility.Static;

        public readonly bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnRayHit(
            in RayData ray,
            ref float maximumT,
            float t,
            in Vector3 normal,
            CollidableReference collidable,
            int childIndex)
        {
            if (normal.Y <= 0.55f)
            {
                return;
            }

            Hit = true;
            maximumT = t;
        }
    }
}

internal readonly record struct PlayerPhysicsResult(Vector3 Position, float VerticalVelocity, bool IsGrounded);
