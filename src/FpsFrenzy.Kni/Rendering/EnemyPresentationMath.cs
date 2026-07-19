using FpsFrenzy.Core.Simulation;
using System.Numerics;

namespace FpsFrenzy.Kni.Rendering;

public static class EnemyPresentationMath
{
    public const float LocomotionEnterSpeed = 0.2f;
    public const float LocomotionExitSpeed = 0.08f;

    public static float MeasureHorizontalSpeed(Vector3 previous, Vector3 current, float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        Vector2 movement = new(current.X - previous.X, current.Z - previous.Z);
        return movement.Length() / deltaSeconds;
    }

    public static bool UpdateLocomotionLatch(
        bool wasLocomoting,
        EnemyActionState actionState,
        float horizontalSpeed)
    {
        bool requestsLocomotion = actionState is
            EnemyActionState.Locomotion or
            EnemyActionState.Navigating or
            EnemyActionState.Charging;
        if (!requestsLocomotion || !float.IsFinite(horizontalSpeed))
        {
            return false;
        }

        return horizontalSpeed >= (wasLocomoting ? LocomotionExitSpeed : LocomotionEnterSpeed);
    }

    public static float GetLocomotionPlaybackScale(float horizontalSpeed, float authoredMoveSpeed)
    {
        if (!float.IsFinite(horizontalSpeed) || horizontalSpeed < 0f ||
            !float.IsFinite(authoredMoveSpeed) || authoredMoveSpeed <= 0f)
        {
            return 1f;
        }

        return Math.Clamp(horizontalSpeed / authoredMoveSpeed, 0.65f, 1.45f);
    }

    public static float GetAuthoredLocomotionSpeed(
        EnemyActionState actionState,
        float moveSpeed,
        float chargeSpeed)
    {
        if (actionState == EnemyActionState.Charging &&
            float.IsFinite(chargeSpeed) && chargeSpeed > 0f)
        {
            return chargeSpeed;
        }

        return moveSpeed;
    }
}
