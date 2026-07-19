using FpsFrenzy.Core.Data;
using FpsFrenzy.Kni.Development;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FpsFrenzy.Kni.Rendering;

public sealed class CharacterLabRenderer : IDisposable
{
    private const float AuthoredEnemySpawnHeight = 0.7f;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _pixel;
    private readonly PixelFont _font;
    private bool _disposed;

    public CharacterLabRenderer(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        _graphicsDevice = graphicsDevice;
        _spriteBatch = new SpriteBatch(graphicsDevice);
        _pixel = new Texture2D(graphicsDevice, 1, 1, false, SurfaceFormat.Color);
        _pixel.SetData([Color.White]);
        _font = new PixelFont(_pixel);
    }

    public void Draw(
        PrimitiveRenderer primitives,
        SkinnedModelPresenter presenter,
        EnemyModelInstance instance,
        EnemyDefinition enemy,
        CharacterLabController controller,
        ArenaDefinition arena)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(enemy);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(arena);

        _graphicsDevice.Clear(new Color(7, 13, 27));
        float distance = controller.CameraDistanceMeters;
        // Release portal positions use the authored 0.7 m simulation baseline. Reproduce it here
        // before applying the same ground/hover offsets as integrated gameplay, otherwise every
        // ground robot is presented 0.7 m below the Character Lab floor.
        float visualBaseY = AuthoredEnemySpawnHeight +
            enemy.Visual.GroundOffset + enemy.Visual.HoverOffset;
        Vector3 target = new(0f, visualBaseY + (enemy.Visual.TargetHeight * 0.5f), 0f);
        Vector3 camera = new(distance * 0.13f, target.Y + MathF.Max(0.12f, enemy.Visual.TargetHeight * 0.06f), distance);
        Matrix view = Matrix.CreateLookAt(camera, target, Vector3.Up);
        Matrix projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(68f),
            _graphicsDevice.Viewport.AspectRatio,
            0.08f,
            80f);

        primitives.Begin(view, projection, arena);
        DrawStage(primitives, enemy.Visual.TargetHeight, enemy.Visual.EmissiveAccent.ToXna());

        Vector3 emissive = enemy.Visual.EmissiveAccent == System.Numerics.Vector3.Zero
            ? enemy.Tint.ToXna()
            : enemy.Visual.EmissiveAccent.ToXna();
        presenter.Draw(
            instance,
            new Vector3(0f, visualBaseY, 0f),
            enemy.Visual.TargetHeight,
            MathHelper.ToRadians(enemy.Visual.ForwardYawDegrees),
            emissive,
            controller.CurrentPose == CharacterLabPose.Hit ? 0.72f : 0f,
            view,
            projection,
            arena);

        DrawOverlay(enemy, controller, emissive);
    }

    private static void DrawStage(PrimitiveRenderer primitives, float enemyHeight, Vector3 accent)
    {
        Color accentColor = new(accent.X, accent.Y, accent.Z);
        primitives.DrawCube(new Vector3(0f, -0.09f, 0f), new Vector3(30f, 0.18f, 30f),
            new Color(17, 28, 48));
        primitives.DrawCube(new Vector3(0f, 4f, -4.2f), new Vector3(13f, 8f, 0.16f),
            new Color(13, 22, 40));

        for (int grid = -10; grid <= 10; grid += 2)
        {
            Color gridColor = grid == 0 ? accentColor : new Color(35, 68, 92);
            float thickness = grid == 0 ? 0.025f : 0.012f;
            primitives.DrawBeam(new Vector3(grid, 0.015f, -4f), new Vector3(grid, 0.015f, 10f),
                thickness, gridColor);
            primitives.DrawBeam(new Vector3(-10f, 0.018f, grid), new Vector3(10f, 0.018f, grid),
                thickness, gridColor);
        }

        float markerHeight = MathF.Max(2f, MathF.Ceiling(enemyHeight));
        for (float side = -1f; side <= 1f; side += 2f)
        {
            float markerX = side * MathF.Max(1.35f, enemyHeight * 0.42f);
            primitives.DrawBeam(new Vector3(markerX, 0.02f, -0.8f),
                new Vector3(markerX, markerHeight, -0.8f), 0.018f, accentColor);
            for (int meter = 1; meter <= markerHeight; meter++)
            {
                primitives.DrawBeam(new Vector3(markerX - 0.12f, meter, -0.8f),
                    new Vector3(markerX + 0.12f, meter, -0.8f), 0.018f, accentColor);
            }
        }

        float ringRadius = MathF.Max(0.72f, enemyHeight * 0.34f);
        primitives.DrawBeam(new Vector3(-ringRadius, 0.035f, 0f),
            new Vector3(ringRadius, 0.035f, 0f), 0.024f, accentColor);
        primitives.DrawBeam(new Vector3(0f, 0.035f, -ringRadius),
            new Vector3(0f, 0.035f, ringRadius), 0.024f, accentColor);
    }

    private void DrawOverlay(EnemyDefinition enemy, CharacterLabController controller, Vector3 accent)
    {
        Rectangle safe = _graphicsDevice.Viewport.TitleSafeArea;
        Color accentColor = new(accent.X, accent.Y, accent.Z);
        Rectangle panel = new(safe.Left + 22, safe.Top + 22, 390, 100);
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        _spriteBatch.Draw(_pixel, panel, new Color(4, 9, 19, 218));
        _spriteBatch.Draw(_pixel, new Rectangle(panel.X, panel.Y, 5, panel.Height), accentColor);
        _font.Draw(_spriteBatch, "CHARACTER LAB", new Vector2(panel.X + 20, panel.Y + 15), accentColor, 2);
        _font.Draw(_spriteBatch, enemy.DisplayName, new Vector2(panel.X + 20, panel.Y + 42), Color.White, 2);
        string status = controller.Mode == CharacterLabCaptureMode.Reel
            ? $"{controller.CurrentPose}  {controller.FramesPerSecond} FPS"
            : $"{controller.CurrentPose}  {controller.CurrentDistance}";
        _font.Draw(_spriteBatch, status, new Vector2(panel.X + 20, panel.Y + 70), new Color(180, 211, 235), 1);

        if (controller.Mode == CharacterLabCaptureMode.Reel)
        {
            Vector2 framePosition = new(panel.Right - 120, panel.Y + 70);
            _font.Draw(_spriteBatch, "FRAME", framePosition, new Color(120, 158, 188), 1);
            _font.DrawNumber(_spriteBatch, controller.CapturedReelFrames,
                framePosition + new Vector2(40, 0), Color.White, 1);
        }

        _spriteBatch.End();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _spriteBatch.Dispose();
        _pixel.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
