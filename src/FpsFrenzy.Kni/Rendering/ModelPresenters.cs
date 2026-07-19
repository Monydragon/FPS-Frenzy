using FpsFrenzy.Core.Data;
using FpsFrenzy.Kni.Animation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FpsFrenzy.Kni.Rendering;

public sealed class StaticModelPresenter
{
    private readonly Model _model;
    private readonly Matrix[] _boneTransforms;
    private readonly Vector3 _center;
    private readonly Vector3 _bottomCenter;
    private readonly float _maximumSpan;

    public StaticModelPresenter(Model model)
    {
        _model = model;
        _boneTransforms = new Matrix[model.Bones.Count];
        model.CopyAbsoluteBoneTransformsTo(_boneTransforms);
        (_center, _bottomCenter, _maximumSpan) = Measure(model, _boneTransforms);
    }

    public void Draw(
        Vector3 position,
        float targetSpan,
        float yaw,
        float pitch,
        Matrix view,
        Matrix projection,
        Vector3? diffuseTint = null,
        Vector3? emissiveTint = null,
        ArenaDefinition? arena = null,
        bool anchorToGround = false)
    {
        Vector3 anchor = anchorToGround ? _bottomCenter : _center;
        Matrix world = CreateNormalizedWorld(position, targetSpan, yaw, pitch, anchor, _maximumSpan);
        foreach (ModelMesh mesh in _model.Meshes)
        {
            foreach (Effect effect in mesh.Effects)
            {
                if (effect is not BasicEffect basicEffect)
                {
                    continue;
                }

                basicEffect.World = _boneTransforms[mesh.ParentBone.Index] * world;
                basicEffect.View = view;
                basicEffect.Projection = projection;
                basicEffect.EnableDefaultLighting();
                basicEffect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.45f, -1f, -0.25f));
                basicEffect.DirectionalLight0.DiffuseColor = new Vector3(0.82f, 0.84f, 0.92f);
                basicEffect.DirectionalLight0.SpecularColor = new Vector3(0.16f);
                basicEffect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.55f, -0.35f, 0.7f));
                basicEffect.DirectionalLight1.DiffuseColor = new Vector3(0.2f, 0.23f, 0.32f);
                basicEffect.DirectionalLight1.SpecularColor = new Vector3(0.04f);
                basicEffect.DirectionalLight2.Enabled = false;
                basicEffect.DiffuseColor = diffuseTint ?? Vector3.One;
                basicEffect.AmbientLightColor = new Vector3(0.42f);
                basicEffect.EmissiveColor = emissiveTint ?? Vector3.Zero;
                basicEffect.PreferPerPixelLighting = false;
                basicEffect.FogEnabled = true;
                basicEffect.FogStart = arena?.FogStart ?? 34f;
                basicEffect.FogEnd = arena?.FogEnd ?? 88f;
                basicEffect.FogColor = arena?.FogColor.ToXna() ?? new Vector3(0.045f, 0.075f, 0.13f);
                basicEffect.GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            }

            mesh.Draw();
        }
    }

    internal static Matrix CreateNormalizedWorld(
        Vector3 position,
        float targetSpan,
        float yaw,
        float pitch,
        Vector3 anchor,
        float maximumSpan) =>
        Matrix.CreateTranslation(-anchor) *
        Matrix.CreateScale(targetSpan / maximumSpan) *
        Matrix.CreateRotationX(pitch) *
        Matrix.CreateRotationY(yaw) *
        Matrix.CreateTranslation(position);

    internal static (Vector3 Center, Vector3 BottomCenter, float MaximumSpan) Measure(
        Model model,
        Matrix[] boneTransforms)
    {
        Vector3 minimum = new(float.MaxValue);
        Vector3 maximum = new(float.MinValue);
        foreach (ModelMesh mesh in model.Meshes)
        {
            BoundingSphere sphere = mesh.BoundingSphere.Transform(boneTransforms[mesh.ParentBone.Index]);
            Vector3 radius = new(sphere.Radius);
            minimum = Vector3.Min(minimum, sphere.Center - radius);
            maximum = Vector3.Max(maximum, sphere.Center + radius);
        }

        Vector3 span = maximum - minimum;
        Vector3 center = (minimum + maximum) * 0.5f;
        Vector3 bottomCenter = new(center.X, minimum.Y, center.Z);
        return (center, bottomCenter, MathF.Max(0.001f, MathF.Max(span.X, MathF.Max(span.Y, span.Z))));
    }
}

public sealed class SkinnedModelPresenter : IDisposable
{
    private readonly Model _model;
    private readonly SkinningData _skinningData;
    private readonly Vector3 _center;
    private readonly float _maximumSpan;
    private bool _disposed;

    public SkinnedModelPresenter(Model model)
    {
        _model = model;
        _skinningData = model.Tag as SkinningData ??
            throw new InvalidDataException("The alien model did not contain FPS Frenzy skinning data.");
        Matrix[] transforms = new Matrix[model.Bones.Count];
        model.CopyAbsoluteBoneTransformsTo(transforms);
        (_center, _, _maximumSpan) = StaticModelPresenter.Measure(model, transforms);
        _ = model.Meshes
            .SelectMany(mesh => mesh.Effects)
            .OfType<SkinnedEffect>()
            .FirstOrDefault() ?? throw new InvalidDataException("The alien model did not use SkinnedEffect.");
    }

    public EnemyModelInstance CreateInstance() => new(_skinningData);

    public void Draw(
        EnemyModelInstance instance,
        string clipName,
        TimeSpan elapsed,
        Vector3 position,
        float targetSpan,
        float yaw,
        Vector3 tint,
        Matrix view,
        Matrix projection,
        ArenaDefinition? arena = null)
    {
        instance.Play(clipName);
        instance.Player.Update(elapsed, Matrix.Identity);
        Matrix world = StaticModelPresenter.CreateNormalizedWorld(
            position, targetSpan, yaw, 0f, _center, _maximumSpan);
        Matrix[] transforms = instance.Player.GetSkinTransforms();
        Vector3 displayTint = Vector3.Lerp(Vector3.One, tint, 0.28f);
        foreach (ModelMesh mesh in _model.Meshes)
        {
            foreach (Effect effect in mesh.Effects)
            {
                if (effect is not SkinnedEffect skinnedEffect)
                {
                    continue;
                }

                skinnedEffect.SetBoneTransforms(transforms);
                skinnedEffect.World = world;
                skinnedEffect.View = view;
                skinnedEffect.Projection = projection;
                skinnedEffect.EnableDefaultLighting();
                skinnedEffect.DiffuseColor = displayTint;
                skinnedEffect.DirectionalLight0.Enabled = true;
                skinnedEffect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.45f, -1f, -0.25f));
                skinnedEffect.DirectionalLight0.DiffuseColor = new Vector3(0.82f, 0.84f, 0.92f);
                skinnedEffect.DirectionalLight0.SpecularColor = new Vector3(0.12f);
                skinnedEffect.DirectionalLight1.Enabled = true;
                skinnedEffect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.55f, -0.35f, 0.7f));
                skinnedEffect.DirectionalLight1.DiffuseColor = new Vector3(0.2f, 0.23f, 0.32f);
                skinnedEffect.DirectionalLight1.SpecularColor = new Vector3(0.03f);
                skinnedEffect.DirectionalLight2.Enabled = false;
                skinnedEffect.AmbientLightColor = new Vector3(0.42f);
                skinnedEffect.EmissiveColor = tint * 0.07f;
                skinnedEffect.PreferPerPixelLighting = false;
                skinnedEffect.FogEnabled = true;
                skinnedEffect.FogStart = arena?.FogStart ?? 34f;
                skinnedEffect.FogEnd = arena?.FogEnd ?? 88f;
                skinnedEffect.FogColor = arena?.FogColor.ToXna() ?? new Vector3(0.045f, 0.075f, 0.13f);
                skinnedEffect.GraphicsDevice.SamplerStates[0] = SamplerState.LinearClamp;
            }

            mesh.Draw();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public sealed class EnemyModelInstance
{
    private readonly SkinningData _skinningData;
    private string? _clipName;

    internal EnemyModelInstance(SkinningData skinningData)
    {
        _skinningData = skinningData;
        Player = new AnimationPlayer(skinningData);
    }

    internal AnimationPlayer Player { get; }

    internal void Play(string clipName)
    {
        if (string.Equals(_clipName, clipName, StringComparison.Ordinal))
        {
            return;
        }

        if (!_skinningData.AnimationClips.TryGetValue(clipName, out AnimationClip? clip))
        {
            throw new InvalidDataException($"Animation clip '{clipName}' is missing from the alien model.");
        }

        _clipName = clipName;
        Player.StartClip(clip);
    }
}
