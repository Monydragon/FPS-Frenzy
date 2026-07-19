using FpsFrenzy.Core.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FpsFrenzy.Kni.Rendering;

public sealed class PrimitiveRenderer : IDisposable
{
    private static readonly VertexPositionNormalTexture[] CubeVertices = CreateCubeVertices();
    private static readonly short[] CubeIndices = CreateCubeIndices();
    private readonly GraphicsDevice _graphicsDevice;
    private readonly BasicEffect _effect;
    private readonly VertexPositionNormalTexture[] _texturedVertices = [.. CubeVertices];
    private bool _disposed;

    public PrimitiveRenderer(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _effect = new BasicEffect(graphicsDevice)
        {
            TextureEnabled = false,
            LightingEnabled = true,
            AmbientLightColor = new Vector3(0.42f),
            FogEnabled = true,
            FogColor = new Vector3(0.055f, 0.075f, 0.12f),
            FogStart = 22f,
            FogEnd = 62f,
        };
        _effect.PreferPerPixelLighting = false;
        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.45f, -1f, -0.25f));
        _effect.DirectionalLight0.DiffuseColor = new Vector3(0.82f, 0.84f, 0.92f);
        _effect.DirectionalLight0.SpecularColor = new Vector3(0.16f);
        _effect.DirectionalLight1.Enabled = true;
        _effect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.55f, -0.35f, 0.7f));
        _effect.DirectionalLight1.DiffuseColor = new Vector3(0.2f, 0.23f, 0.32f);
        _effect.DirectionalLight1.SpecularColor = new Vector3(0.04f);
        _effect.DirectionalLight2.Enabled = false;
    }

    public void Begin(Matrix view, Matrix projection, ArenaDefinition arena)
    {
        _effect.View = view;
        _effect.Projection = projection;
        _effect.FogColor = arena.FogColor.ToXna();
        _effect.FogStart = arena.FogStart;
        _effect.FogEnd = arena.FogEnd;
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
        _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    public void Draw(ArenaPrimitiveDefinition primitive, Texture2D? texture = null)
    {
        Vector3 rotation = primitive.RotationDegrees.ToXna() * (MathF.PI / 180f);
        _effect.World = Matrix.CreateScale(primitive.Size.ToXna()) *
            Matrix.CreateRotationX(rotation.X) *
            Matrix.CreateRotationY(rotation.Y) *
            Matrix.CreateRotationZ(rotation.Z) *
            Matrix.CreateTranslation(primitive.Position.ToXna());
        if (texture is not null)
        {
            ConfigureTextureCoordinates(primitive.Size.ToXna(), primitive.TextureMetersPerTile);
        }

        DrawCurrentWorld(new Color(primitive.Color.X, primitive.Color.Y, primitive.Color.Z), primitive.IsEmissive, texture);
    }

    public void DrawCube(Vector3 position, Vector3 scale, Color color, float yaw = 0f, bool emissive = false)
    {
        _effect.World = Matrix.CreateScale(scale) * Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(position);
        DrawCurrentWorld(color, emissive, texture: null);
    }

    public void DrawBeam(Vector3 start, Vector3 end, float thickness, Color color)
    {
        Vector3 delta = end - start;
        float length = delta.Length();
        if (length <= 0.001f)
        {
            return;
        }

        Vector3 direction = delta / length;
        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up;
        _effect.World = Matrix.CreateScale(thickness, thickness, length) *
            Matrix.CreateWorld((start + end) * 0.5f, direction, up);
        DrawCurrentWorld(color, emissive: true, texture: null);
    }

    private void DrawCurrentWorld(Color color, bool emissive, Texture2D? texture)
    {
        _effect.LightingEnabled = !emissive;
        _effect.TextureEnabled = texture is not null;
        _effect.Texture = texture;
        _effect.DiffuseColor = color.ToVector3();
        _graphicsDevice.SamplerStates[0] = texture is null ? SamplerState.LinearClamp : SamplerState.LinearWrap;
        VertexPositionNormalTexture[] vertices = texture is null ? CubeVertices : _texturedVertices;
        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                vertices,
                0,
                vertices.Length,
                CubeIndices,
                0,
                CubeIndices.Length / 3);
        }
    }

    private void ConfigureTextureCoordinates(Vector3 size, float metersPerTile)
    {
        SetFaceTextureCoordinates(0, size.X / metersPerTile, size.Y / metersPerTile);
        SetFaceTextureCoordinates(1, size.X / metersPerTile, size.Y / metersPerTile);
        SetFaceTextureCoordinates(2, size.Z / metersPerTile, size.Y / metersPerTile);
        SetFaceTextureCoordinates(3, size.Z / metersPerTile, size.Y / metersPerTile);
        SetFaceTextureCoordinates(4, size.X / metersPerTile, size.Z / metersPerTile);
        SetFaceTextureCoordinates(5, size.X / metersPerTile, size.Z / metersPerTile);
    }

    private void SetFaceTextureCoordinates(int face, float uSpan, float vSpan)
    {
        int vertex = face * 4;
        _texturedVertices[vertex].TextureCoordinate = new Vector2(0f, vSpan);
        _texturedVertices[vertex + 1].TextureCoordinate = Vector2.Zero;
        _texturedVertices[vertex + 2].TextureCoordinate = new Vector2(uSpan, 0f);
        _texturedVertices[vertex + 3].TextureCoordinate = new Vector2(uSpan, vSpan);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _effect.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static VertexPositionNormalTexture[] CreateCubeVertices()
    {
        List<VertexPositionNormalTexture> vertices = [];
        AddFace(vertices, Vector3.Forward, Vector3.Up, Vector3.Right);
        AddFace(vertices, Vector3.Backward, Vector3.Up, Vector3.Left);
        AddFace(vertices, Vector3.Left, Vector3.Up, Vector3.Forward);
        AddFace(vertices, Vector3.Right, Vector3.Up, Vector3.Backward);
        AddFace(vertices, Vector3.Up, Vector3.Backward, Vector3.Right);
        AddFace(vertices, Vector3.Down, Vector3.Forward, Vector3.Right);
        return [.. vertices];
    }

    private static void AddFace(List<VertexPositionNormalTexture> vertices, Vector3 normal, Vector3 up, Vector3 right)
    {
        Vector3 center = normal * 0.5f;
        up *= 0.5f;
        right *= 0.5f;
        vertices.Add(new VertexPositionNormalTexture(center - right - up, normal, new Vector2(0f, 1f)));
        vertices.Add(new VertexPositionNormalTexture(center - right + up, normal, new Vector2(0f, 0f)));
        vertices.Add(new VertexPositionNormalTexture(center + right + up, normal, new Vector2(1f, 0f)));
        vertices.Add(new VertexPositionNormalTexture(center + right - up, normal, new Vector2(1f, 1f)));
    }

    private static short[] CreateCubeIndices()
    {
        short[] indices = new short[36];
        for (short face = 0; face < 6; face++)
        {
            short vertex = (short)(face * 4);
            int offset = face * 6;
            indices[offset] = vertex;
            indices[offset + 1] = (short)(vertex + 1);
            indices[offset + 2] = (short)(vertex + 2);
            indices[offset + 3] = vertex;
            indices[offset + 4] = (short)(vertex + 2);
            indices[offset + 5] = (short)(vertex + 3);
        }

        return indices;
    }
}
