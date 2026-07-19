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
    private string? _recordingName;
    private int _recordingFrameIndex;
    private int _manualRecordingIndex;
    private double _recordingAccumulatorSeconds;
    private int _recordingTargetFrames;
    private int _recordingFramesPerSecond = 60;
    private bool _recordEveryDraw;

    public RenderCaptureService()
    {
        string? configured = Environment.GetEnvironmentVariable("FPS_FRENZY_CAPTURE_DIR");
        _directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.CurrentDirectory, "artifacts", "render-captures")
            : Path.GetFullPath(configured);

        if (double.TryParse(
                Environment.GetEnvironmentVariable("FPS_FRENZY_AUTORECORD_SECONDS"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double automaticSeconds) && automaticSeconds > 0d)
        {
            int automaticFps = int.TryParse(
                Environment.GetEnvironmentVariable("FPS_FRENZY_RECORD_FPS"),
                out int configuredFps)
                ? configuredFps
                : 60;
            StartRecording(
                Environment.GetEnvironmentVariable("FPS_FRENZY_RECORD_NAME") ?? "automatic-motion",
                automaticSeconds,
                automaticFps,
                captureEveryDraw: true);
        }
    }

    public string DirectoryPath => _directory;

    public bool HasPendingCapture => _pendingName is not null;
    public bool IsRecording => _recordingName is not null;
    public int RecordingFramesPerSecond => _recordingFramesPerSecond;

    public void UpdateInput()
    {
        KeyboardState keyboard = Keyboard.GetState();
        if (keyboard.IsKeyDown(Keys.F12) && !_previousKeyboard.IsKeyDown(Keys.F12))
        {
            Queue($"manual-{++_manualCaptureIndex:00}");
        }

        if (keyboard.IsKeyDown(Keys.F11) && !_previousKeyboard.IsKeyDown(Keys.F11))
        {
            if (IsRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording($"manual-motion-{++_manualRecordingIndex:00}", 15d, 60);
            }
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

    public void StartRecording(
        string name,
        double durationSeconds,
        int framesPerSecond = 60,
        bool captureEveryDraw = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(durationSeconds, 0d);

        if (framesPerSecond is not (30 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Recordings support 30 or 60 FPS.");
        }

        _recordingName = Sanitize(name);
        _recordingFrameIndex = 0;
        _recordingAccumulatorSeconds = 0d;
        _recordingFramesPerSecond = framesPerSecond;
        _recordingTargetFrames = Math.Max(1, (int)Math.Ceiling(durationSeconds * framesPerSecond));
        _recordEveryDraw = captureEveryDraw;
    }

    public void StopRecording()
    {
        _recordingName = null;
        _recordingAccumulatorSeconds = 0d;
        _recordingTargetFrames = 0;
        _recordEveryDraw = false;
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

    public string? CaptureRecordingFrame(GraphicsDevice graphicsDevice, TimeSpan elapsed)
    {
        if (_recordingName is null)
        {
            return null;
        }

        if (!_recordEveryDraw)
        {
            _recordingAccumulatorSeconds += Math.Max(0d, elapsed.TotalSeconds);
            double frameInterval = 1d / _recordingFramesPerSecond;
            if (_recordingAccumulatorSeconds + 0.000001d < frameInterval)
            {
                return null;
            }

            _recordingAccumulatorSeconds %= frameInterval;
        }

        string recordingDirectory = Path.Combine(_directory, _recordingName);
        Directory.CreateDirectory(recordingDirectory);
        string path = Path.Combine(recordingDirectory, $"frame-{_recordingFrameIndex++:00000}.png");
        SaveBackBuffer(graphicsDevice, path);
        if (_recordingFrameIndex >= _recordingTargetFrames)
        {
            StopRecording();
        }

        return path;
    }

    private static void SaveBackBuffer(GraphicsDevice graphicsDevice, string path)
    {
        int width = graphicsDevice.PresentationParameters.BackBufferWidth;
        int height = graphicsDevice.PresentationParameters.BackBufferHeight;
        Color[] pixels = new Color[width * height];
        graphicsDevice.GetBackBufferData(pixels);
        using Texture2D texture = new(graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        using FileStream stream = File.Create(path);
        texture.SaveAsPng(stream, width, height);
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
