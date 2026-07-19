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
    private readonly ModelTextureSampling _textureSampling;

    public StaticModelPresenter(
        Model model,
        ModelTextureSampling textureSampling = ModelTextureSampling.Detailed)
    {
        _model = model;
        _textureSampling = textureSampling;
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
                basicEffect.GraphicsDevice.SamplerStates[0] = _textureSampling == ModelTextureSampling.Palette
                    ? SamplerState.PointClamp
                    : SamplerState.LinearClamp;
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

public enum ModelTextureSampling
{
    Detailed,
    Palette,
}

// Reach does not expose the normals/bone palette needed to bolt a Fresnel term onto the
// stock SkinnedEffect. This deterministic back-face shell supplies a restrained silhouette
// rim while keeping the same tested Reach/Android skinning shader and 72-bone contract.
internal static class RobotMaterialFallback
{
    internal const float RimShellScale = 1.025f;

    private static readonly Vector3 NeutralRimColor = new(0.08f, 0.2f, 0.32f);

    internal static Vector3 CalculateBaseEmissive(
        Vector3 emissiveAccent,
        float hitFlash,
        bool hasEmissiveMask)
    {
        Vector3 accent = SanitizeColor(emissiveAccent);
        float flash = SanitizeUnit(hitFlash);
        return Vector3.Clamp(
            (hasEmissiveMask ? Vector3.Zero : accent * 0.12f) + (Vector3.One * flash * 0.7f),
            Vector3.Zero,
            Vector3.One);
    }

    internal static Vector3 CalculateRimColor(Vector3 emissiveAccent, float hitFlash)
    {
        Vector3 accent = SanitizeColor(emissiveAccent);
        Vector3 tint = accent.LengthSquared() > 0.000001f
            ? Vector3.Lerp(NeutralRimColor, accent, 0.45f)
            : NeutralRimColor;
        return Vector3.Clamp(
            tint + (Vector3.One * SanitizeUnit(hitFlash) * 0.38f),
            Vector3.Zero,
            Vector3.One);
    }

    internal static Vector3 CalculateEmissiveMaskColor(Vector3 emissiveAccent) =>
        SanitizeColor(emissiveAccent);

    internal static Matrix CreateRimWorld(Matrix world, Vector3 sourceAnchor)
    {
        if (!IsFinite(sourceAnchor))
        {
            throw new ArgumentException("The rim source anchor must be finite.", nameof(sourceAnchor));
        }

        return Matrix.CreateTranslation(-sourceAnchor) *
            Matrix.CreateScale(RimShellScale) *
            Matrix.CreateTranslation(sourceAnchor) *
            world;
    }

    private static float SanitizeUnit(float value) => float.IsFinite(value)
        ? Math.Clamp(value, 0f, 1f)
        : 0f;

    private static Vector3 SanitizeColor(Vector3 value) => IsFinite(value)
        ? Vector3.Clamp(value, Vector3.Zero, Vector3.One)
        : Vector3.Zero;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public sealed record EnemyVisualCalibration
{
    public float SourceHeight { get; init; } = 1f;
    public Vector3 SourceGroundAnchor { get; init; }
    public float GroundOffset { get; init; }
    public float ForwardYaw { get; init; }
    public Vector3 HealthBarAnchor { get; init; } = new(0f, 1.15f, 0f);

    public Matrix CreateWorld(Vector3 groundPosition, float targetHeight, float facingYaw)
    {
        Validate();
        if (!float.IsFinite(targetHeight) || targetHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight));
        }

        return StaticModelPresenter.CreateNormalizedWorld(
            groundPosition + new Vector3(0f, GroundOffset, 0f),
            targetHeight,
            facingYaw + ForwardYaw,
            0f,
            SourceGroundAnchor,
            SourceHeight);
    }

    public Vector3 GetHealthBarWorldPosition(Vector3 groundPosition, float targetHeight, float facingYaw) =>
        Vector3.Transform(HealthBarAnchor, CreateWorld(groundPosition, targetHeight, facingYaw));

    internal void Validate()
    {
        if (!float.IsFinite(SourceHeight) || SourceHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(SourceHeight), "SourceHeight must be finite and greater than zero.");
        }

        if (!IsFinite(SourceGroundAnchor) || !float.IsFinite(GroundOffset) ||
            !float.IsFinite(ForwardYaw) || !IsFinite(HealthBarAnchor))
        {
            throw new ArgumentException("Enemy visual calibration values must be finite.");
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public readonly record struct AnimationClipBinding(
    string ClipName,
    AnimationLoopMode LoopMode = AnimationLoopMode.Loop,
    float PlaybackRate = 1f,
    float TransitionSeconds = 0.12f,
    float StartNormalized = 0f,
    float EndNormalized = 1f)
{
    internal AnimationPlaybackOptions ToPlaybackOptions()
    {
        if (string.IsNullOrWhiteSpace(ClipName))
        {
            throw new ArgumentException("Animation clip name cannot be empty.", nameof(ClipName));
        }

        if (!float.IsFinite(TransitionSeconds) || TransitionSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(TransitionSeconds));
        }

        return new AnimationPlaybackOptions(
            LoopMode,
            TimeSpan.FromSeconds(TransitionSeconds),
            PlaybackRate,
            StartNormalized,
            EndNormalized);
    }
}

public sealed class SkinnedModelPresenter : IDisposable
{
    private readonly Model _model;
    private readonly SkinningData _skinningData;
    private readonly Vector3 _center;
    private readonly Vector3 _bottomCenter;
    private readonly float _maximumSpan;
    private readonly EnemyVisualCalibration _calibration;
    private readonly ModelTextureSampling _textureSampling;
    private readonly Texture2D? _emissiveMask;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Dictionary<SkinnedEffect, Texture2D> _albedoTextures = new();
    private readonly Texture2D _whiteTexture;
    private readonly RasterizerState _rimRasterizerState;
    private bool _diagnosticsLoggedDraw;
    private bool _disposed;

    public SkinnedModelPresenter(
        Model model,
        Texture2D? albedo = null,
        EnemyVisualCalibration? calibration = null,
        ModelTextureSampling textureSampling = ModelTextureSampling.Detailed,
        Texture2D? emissiveMask = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _skinningData = model.Tag as SkinningData ??
            throw new InvalidDataException("The enemy model did not contain FPS Frenzy skinning data.");
        Matrix[] transforms = new Matrix[model.Bones.Count];
        model.CopyAbsoluteBoneTransformsTo(transforms);
        if (_skinningData.HasSourceGeometryBounds)
        {
            Vector3 minimum = _skinningData.SourceBoundsMinimum;
            Vector3 maximum = _skinningData.SourceBoundsMaximum;
            Vector3 span = maximum - minimum;
            _center = (minimum + maximum) * 0.5f;
            _bottomCenter = new Vector3(_center.X, minimum.Y, _center.Z);
            _maximumSpan = MathF.Max(0.001f, MathF.Max(span.X, MathF.Max(span.Y, span.Z)));
        }
        else
        {
            // Compatibility for pre-v3 XNBs. New content carries weighted-geometry bounds because
            // KNI's generated skinned bounding spheres can be two orders of magnitude off.
            (_center, _bottomCenter, _maximumSpan) = StaticModelPresenter.Measure(model, transforms);
        }
        if (string.Equals(
                Environment.GetEnvironmentVariable("FPS_FRENZY_SKIN_DIAGNOSTICS"),
                "1",
                StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"SKIN_RUNTIME bounds center={_center} bottom={_bottomCenter} span={_maximumSpan} " +
                $"meshes={model.Meshes.Count} bones={model.Bones.Count}");
            for (int index = 0; index < Math.Min(_skinningData.BindPose.Count, 8); index++)
            {
                Console.WriteLine($"SKIN_RUNTIME bind[{index}]={_skinningData.BindPose[index]}");
            }
            foreach (ModelMesh mesh in model.Meshes)
            {
                Console.WriteLine(
                    $"SKIN_RUNTIME mesh={mesh.Name} sphereCenter={mesh.BoundingSphere.Center} " +
                    $"sphereRadius={mesh.BoundingSphere.Radius} parentBone={mesh.ParentBone.Index} " +
                    $"parentAbsolute={transforms[mesh.ParentBone.Index]}");
            }

            LogVertexBufferBounds(model);
        }
        _calibration = calibration ?? new EnemyVisualCalibration
        {
            SourceHeight = _maximumSpan,
            SourceGroundAnchor = _bottomCenter,
            HealthBarAnchor = _bottomCenter + new Vector3(0f, _maximumSpan * 1.08f, 0f),
        };
        _calibration.Validate();
        _textureSampling = textureSampling;
        _emissiveMask = emissiveMask;

        SkinnedEffect[] effects = model.Meshes
            .SelectMany(mesh => mesh.Effects)
            .OfType<SkinnedEffect>()
            .Distinct()
            .ToArray();
        if (effects.Length == 0)
        {
            throw new InvalidDataException("The enemy model did not use SkinnedEffect.");
        }

        _graphicsDevice = effects[0].GraphicsDevice;

        foreach (SkinnedEffect effect in effects)
        {
            if (albedo is not null)
            {
                effect.Texture = albedo;
            }

            if (effect.Texture is null)
            {
                throw new InvalidDataException(
                    "Every enemy material must bind an albedo texture explicitly or through the compiled model material.");
            }

            _albedoTextures.Add(effect, effect.Texture);
        }

        _whiteTexture = new Texture2D(_graphicsDevice, 1, 1, false, SurfaceFormat.Color);
        _whiteTexture.SetData(new[] { Color.White });
        _rimRasterizerState = new RasterizerState
        {
            CullMode = CullMode.CullClockwiseFace,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = true,
        };
    }

    public EnemyVisualCalibration Calibration => _calibration;

    private static void LogVertexBufferBounds(Model model)
    {
        Vector3 minimum = new(float.MaxValue);
        Vector3 maximum = new(float.MinValue);
        int count = 0;
        foreach (ModelMeshPart part in model.Meshes.SelectMany(mesh => mesh.MeshParts))
        {
            VertexDeclaration declaration = part.VertexBuffer.VertexDeclaration;
            VertexElement? positionElement = declaration.GetVertexElements()
                .FirstOrDefault(element => element.VertexElementUsage == VertexElementUsage.Position);
            if (positionElement is null || positionElement.Value.VertexElementFormat != VertexElementFormat.Vector3)
            {
                continue;
            }

            int stride = declaration.VertexStride;
            byte[] data = new byte[part.VertexBuffer.VertexCount * stride];
            part.VertexBuffer.GetData(data);
            int offset = positionElement.Value.Offset;
            for (int vertex = part.VertexOffset; vertex < part.VertexOffset + part.NumVertices; vertex++)
            {
                int start = (vertex * stride) + offset;
                Vector3 position = new(
                    BitConverter.ToSingle(data, start),
                    BitConverter.ToSingle(data, start + sizeof(float)),
                    BitConverter.ToSingle(data, start + (sizeof(float) * 2)));
                minimum = Vector3.Min(minimum, position);
                maximum = Vector3.Max(maximum, position);
                count++;
            }
        }

        Console.WriteLine($"SKIN_RUNTIME vertexBufferBounds min={minimum} max={maximum} count={count}");
    }

    public EnemyModelInstance CreateInstance() => new(_skinningData);

    public void Draw(
        EnemyModelInstance instance,
        Vector3 groundPosition,
        float targetHeight,
        float yaw,
        Vector3 emissiveAccent,
        float hitFlash,
        Matrix view,
        Matrix projection,
        ArenaDefinition? arena = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Matrix world = _calibration.CreateWorld(groundPosition, targetHeight, yaw);
        DrawModel(
            instance,
            world,
            _calibration.SourceGroundAnchor,
            emissiveAccent,
            hitFlash,
            view,
            projection,
            arena);
    }

    // Compatibility path for the current game loop. New integration should call instance.Play/Update
    // from Game.Update and use the calibrated, render-only overload above.
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
        instance.Play(new AnimationClipBinding(clipName));
        instance.Update(elapsed, paused: false);
        Matrix world = StaticModelPresenter.CreateNormalizedWorld(
            position, targetSpan, yaw, 0f, _center, _maximumSpan);
        DrawModel(instance, world, _center, tint, 0f, view, projection, arena);
    }

    private void DrawModel(
        EnemyModelInstance instance,
        Matrix world,
        Vector3 sourceAnchor,
        Vector3 emissiveAccent,
        float hitFlash,
        Matrix view,
        Matrix projection,
        ArenaDefinition? arena)
    {
        Matrix[] transforms = instance.Player.GetSkinTransforms();
        if (!_diagnosticsLoggedDraw && string.Equals(
                Environment.GetEnvironmentVariable("FPS_FRENZY_SKIN_DIAGNOSTICS"),
                "1",
                StringComparison.Ordinal))
        {
            _diagnosticsLoggedDraw = true;
            Console.WriteLine($"SKIN_RUNTIME draw world={world}");
            for (int index = 0; index < Math.Min(transforms.Length, 8); index++)
            {
                Console.WriteLine($"SKIN_RUNTIME skin[{index}]={transforms[index]}");
            }
        }

        Vector3 baseEmissive = RobotMaterialFallback.CalculateBaseEmissive(
            emissiveAccent,
            hitFlash,
            _emissiveMask is not null);
        foreach (ModelMesh mesh in _model.Meshes)
        {
            foreach (Effect effect in mesh.Effects)
            {
                if (effect is not SkinnedEffect skinnedEffect)
                {
                    continue;
                }

                skinnedEffect.SetBoneTransforms(transforms);
                skinnedEffect.Texture = _albedoTextures[skinnedEffect];
                skinnedEffect.World = world;
                skinnedEffect.View = view;
                skinnedEffect.Projection = projection;
                skinnedEffect.EnableDefaultLighting();
                skinnedEffect.DiffuseColor = Vector3.One;
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
                skinnedEffect.EmissiveColor = baseEmissive;
                skinnedEffect.Alpha = 1f;
                skinnedEffect.PreferPerPixelLighting = false;
                skinnedEffect.FogEnabled = true;
                skinnedEffect.FogStart = arena?.FogStart ?? 34f;
                skinnedEffect.FogEnd = arena?.FogEnd ?? 88f;
                skinnedEffect.FogColor = arena?.FogColor.ToXna() ?? new Vector3(0.045f, 0.075f, 0.13f);
                skinnedEffect.GraphicsDevice.SamplerStates[0] = _textureSampling == ModelTextureSampling.Palette
                    ? SamplerState.PointClamp
                    : SamplerState.LinearClamp;
            }

            mesh.Draw();
        }

        DrawRimPass(
            transforms,
            RobotMaterialFallback.CreateRimWorld(world, sourceAnchor),
            RobotMaterialFallback.CalculateRimColor(emissiveAccent, hitFlash),
            view,
            projection,
            arena);

        Vector3 emissiveMaskColor = RobotMaterialFallback.CalculateEmissiveMaskColor(emissiveAccent);
        if (_emissiveMask is not null && emissiveMaskColor.LengthSquared() > 0.000001f)
        {
            DrawEmissiveMaskPass(transforms, world, emissiveMaskColor, view, projection, arena);
        }
    }

    private void DrawRimPass(
        Matrix[] transforms,
        Matrix world,
        Vector3 rimColor,
        Matrix view,
        Matrix projection,
        ArenaDefinition? arena)
    {
        BlendState previousBlendState = _graphicsDevice.BlendState;
        DepthStencilState previousDepthStencilState = _graphicsDevice.DepthStencilState;
        RasterizerState previousRasterizerState = _graphicsDevice.RasterizerState;
        SamplerState previousSamplerState = _graphicsDevice.SamplerStates[0];
        try
        {
            _graphicsDevice.BlendState = BlendState.Additive;
            _graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
            _graphicsDevice.RasterizerState = _rimRasterizerState;
            _graphicsDevice.SamplerStates[0] = SamplerState.PointClamp;

            foreach (ModelMesh mesh in _model.Meshes)
            {
                foreach (Effect effect in mesh.Effects)
                {
                    if (effect is not SkinnedEffect skinnedEffect)
                    {
                        continue;
                    }

                    skinnedEffect.SetBoneTransforms(transforms);
                    skinnedEffect.Texture = _whiteTexture;
                    skinnedEffect.World = world;
                    skinnedEffect.View = view;
                    skinnedEffect.Projection = projection;
                    skinnedEffect.DiffuseColor = Vector3.Zero;
                    skinnedEffect.AmbientLightColor = Vector3.Zero;
                    skinnedEffect.EmissiveColor = rimColor;
                    skinnedEffect.Alpha = 1f;
                    skinnedEffect.DirectionalLight0.Enabled = false;
                    skinnedEffect.DirectionalLight1.Enabled = false;
                    skinnedEffect.DirectionalLight2.Enabled = false;
                    skinnedEffect.PreferPerPixelLighting = false;
                    skinnedEffect.FogEnabled = true;
                    skinnedEffect.FogStart = arena?.FogStart ?? 34f;
                    skinnedEffect.FogEnd = arena?.FogEnd ?? 88f;
                    // Additive auxiliary passes must fade to black. Using the arena fog color
                    // here would add a second copy of that color over the already-fogged base.
                    skinnedEffect.FogColor = Vector3.Zero;
                }

                mesh.Draw();
            }
        }
        finally
        {
            RestoreAlbedoTextures();
            _graphicsDevice.BlendState = previousBlendState;
            _graphicsDevice.DepthStencilState = previousDepthStencilState;
            _graphicsDevice.RasterizerState = previousRasterizerState;
            _graphicsDevice.SamplerStates[0] = previousSamplerState;
        }
    }

    private void DrawEmissiveMaskPass(
        Matrix[] transforms,
        Matrix world,
        Vector3 emissiveAccent,
        Matrix view,
        Matrix projection,
        ArenaDefinition? arena)
    {
        BlendState previousBlendState = _graphicsDevice.BlendState;
        DepthStencilState previousDepthStencilState = _graphicsDevice.DepthStencilState;
        SamplerState previousSamplerState = _graphicsDevice.SamplerStates[0];
        try
        {
            _graphicsDevice.BlendState = BlendState.Additive;
            _graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
            _graphicsDevice.SamplerStates[0] = _textureSampling == ModelTextureSampling.Palette
                ? SamplerState.PointClamp
                : SamplerState.LinearClamp;

            foreach (ModelMesh mesh in _model.Meshes)
            {
                foreach (Effect effect in mesh.Effects)
                {
                    if (effect is not SkinnedEffect skinnedEffect)
                    {
                        continue;
                    }

                    skinnedEffect.SetBoneTransforms(transforms);
                    skinnedEffect.Texture = _emissiveMask;
                    skinnedEffect.World = world;
                    skinnedEffect.View = view;
                    skinnedEffect.Projection = projection;
                    skinnedEffect.DiffuseColor = Vector3.Zero;
                    skinnedEffect.AmbientLightColor = Vector3.Zero;
                    skinnedEffect.EmissiveColor = emissiveAccent;
                    skinnedEffect.Alpha = 1f;
                    skinnedEffect.DirectionalLight0.Enabled = false;
                    skinnedEffect.DirectionalLight1.Enabled = false;
                    skinnedEffect.DirectionalLight2.Enabled = false;
                    skinnedEffect.PreferPerPixelLighting = false;
                    skinnedEffect.FogEnabled = true;
                    skinnedEffect.FogStart = arena?.FogStart ?? 34f;
                    skinnedEffect.FogEnd = arena?.FogEnd ?? 88f;
                    skinnedEffect.FogColor = Vector3.Zero;
                }

                mesh.Draw();
            }
        }
        finally
        {
            RestoreAlbedoTextures();
            _graphicsDevice.BlendState = previousBlendState;
            _graphicsDevice.DepthStencilState = previousDepthStencilState;
            _graphicsDevice.SamplerStates[0] = previousSamplerState;
        }
    }

    private void RestoreAlbedoTextures()
    {
        foreach ((SkinnedEffect effect, Texture2D texture) in _albedoTextures)
        {
            effect.Texture = texture;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rimRasterizerState.Dispose();
        _whiteTexture.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed class EnemyModelInstance
{
    private readonly SkinningData _skinningData;
    private AnimationClipBinding? _binding;
    private float _playbackScale = 1f;
    private float _targetPlaybackScale = 1f;

    internal EnemyModelInstance(SkinningData skinningData)
    {
        _skinningData = skinningData;
        Player = new AnimationPlayer(skinningData);
    }

    internal AnimationPlayer Player { get; }
    public string? CurrentClipName => _binding?.ClipName;
    public bool IsAnimationComplete => Player.IsComplete;
    internal bool IsLocomoting { get; set; }

    public void Play(AnimationClipBinding binding, float playbackScale = 1f)
    {
        if (!float.IsFinite(playbackScale) || playbackScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(playbackScale));
        }

        _targetPlaybackScale = playbackScale;
        AnimationPlaybackOptions options = binding.ToPlaybackOptions();
        if (_binding == binding)
        {
            return;
        }

        if (!_skinningData.AnimationClips.TryGetValue(binding.ClipName, out AnimationClip? clip))
        {
            throw new InvalidDataException($"Animation clip '{binding.ClipName}' is missing from the enemy model.");
        }

        _binding = binding;
        _playbackScale = playbackScale;
        options = options with { PlaybackRate = options.PlaybackRate * _playbackScale };
        if (Player.CurrentClip is null)
        {
            Player.StartClip(clip, options with { TransitionDuration = TimeSpan.Zero });
        }
        else
        {
            Player.TransitionTo(clip, options);
        }
    }

    public void Update(TimeSpan elapsed, bool paused)
    {
        if (Player.CurrentClip is null)
        {
            return;
        }

        Player.IsPaused = paused;
        if (!paused && _binding is AnimationClipBinding binding)
        {
            float blend = 1f - MathF.Exp(-10f * (float)elapsed.TotalSeconds);
            _playbackScale = float.Lerp(_playbackScale, _targetPlaybackScale, blend);
            Player.SetPlaybackRate(binding.PlaybackRate * _playbackScale);
        }

        Player.Update(elapsed, Matrix.Identity);
    }
}
