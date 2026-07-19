using System.Runtime.InteropServices;
using System.Numerics;
using FpsFrenzy.Kni.Input;

namespace FpsFrenzy.Desktop;

public sealed class SdlMouseCapture : IPlatformMouseCapture
{
    private bool _captured;

    public void SetCaptured(bool captured)
    {
        if (_captured == captured)
        {
            return;
        }

        if (SDL_SetRelativeMouseMode(captured ? SdlBool.True : SdlBool.False) < 0)
        {
            string message = Marshal.PtrToStringUTF8(SDL_GetError()) ?? "Unknown SDL error.";
            throw new InvalidOperationException($"SDL could not change relative mouse mode: {message}");
        }

        _captured = captured;
    }

    public Vector2 ConsumeRelativeLookDelta()
    {
        if (!_captured)
        {
            return Vector2.Zero;
        }

        _ = SDL_GetRelativeMouseState(out int x, out int y);
        return new Vector2(x, y);
    }

    private enum SdlBool
    {
        False,
        True,
    }

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_SetRelativeMouseMode(SdlBool enabled);

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetError();

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint SDL_GetRelativeMouseState(out int x, out int y);
}
