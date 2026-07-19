using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FpsFrenzy.Kni.Rendering;

public sealed class RenderCaptureService
{
    private readonly string _directory;
    private KeyboardState _previousKeyboard;
    private string? _pendingName;
    private int _pendingRenderFrames;
    private int _manualCaptureIndex;

    public RenderCaptureService()
    {
        string? configured = Environment.GetEnvironmentVariable("FPS_FRENZY_CAPTURE_DIR");
        _directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.CurrentDirectory, "artifacts", "render-captures")
            : Path.GetFullPath(configured);
    }

    public string DirectoryPath => _directory;

    public bool HasPendingCapture => _pendingName is not null;

    public void UpdateInput()
    {
        KeyboardState keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.F12) && !_previousKeyboard.IsKeyDown(Keys.F12))
        {
            Queue($"manual-{++_manualCaptureIndex:00}");
        }

        _previousKeyboard = keyboard;
    }

    public void Queue(string name)
    {
        if (_pendingName is null)
        {
            _pendingName = Sanitize(name);
            _pendingRenderFrames = 2;
        }
    }

    public string? CaptureIfRequested(GraphicsDevice graphicsDevice)
    {
        if (_pendingName is null)
        {
            return null;
        }

        if (_pendingRenderFrames > 0)
        {
            _pendingRenderFrames--;
            return null;
        }

        int width = graphicsDevice.PresentationParameters.BackBufferWidth;
        int height = graphicsDevice.PresentationParameters.BackBufferHeight;
        Color[] pixels = new Color[width * height];
        graphicsDevice.GetBackBufferData(pixels);
        using Texture2D texture = new(graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, $"{_pendingName}.png");
        using FileStream stream = File.Create(path);
        texture.SaveAsPng(stream, width, height);
        _pendingName = null;
        return path;
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return value;
    }
}
