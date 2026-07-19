using System.Numerics;

namespace FpsFrenzy.Kni.Input;

public interface IPlatformLookSource
{
    bool IsAvailable { get; }
    Vector2 ConsumeLookDelta(float deltaSeconds);
}

public interface IPlatformMouseCapture
{
    void SetCaptured(bool captured);
    Vector2 ConsumeRelativeLookDelta();
}
