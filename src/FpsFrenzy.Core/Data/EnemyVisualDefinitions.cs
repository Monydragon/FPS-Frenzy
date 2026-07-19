using System.Numerics;
using System.Text.Json.Serialization;

namespace FpsFrenzy.Core.Data;

[JsonConverter(typeof(JsonStringEnumConverter<EnemyAnimationLoopMode>))]
public enum EnemyAnimationLoopMode
{
    Loop,
    PlayOnce,
    PlayOnceHold,
}

[JsonConverter(typeof(JsonStringEnumConverter<TextureSamplingMode>))]
public enum TextureSamplingMode
{
    LinearMipmapped,
    PointNoMipmaps,
}

public sealed record AnimationClipBinding
{
    public required string ClipName { get; init; }
    public EnemyAnimationLoopMode LoopMode { get; init; } = EnemyAnimationLoopMode.Loop;
    public float PlaybackRate { get; init; } = 1f;
    public float TransitionSeconds { get; init; } = 0.12f;
    public float StartNormalized { get; init; }
    public float EndNormalized { get; init; } = 1f;
    public bool Interruptible { get; init; } = true;
    public bool StripRootMotion { get; init; } = true;
}

public sealed record EnemyVisualDefinition
{
    public float TargetHeight { get; init; } = 1.8f;
    public float GroundOffset { get; init; }
    public float HoverOffset { get; init; }
    public float ForwardYawDegrees { get; init; }
    public float CorpseLifetimeSeconds { get; init; } = 0.8f;
    public string? AlbedoAsset { get; init; }
    public string? EmissiveAsset { get; init; }
    public Vector3 EmissiveAccent { get; init; }
    public Vector3 HealthBarAnchor { get; init; } = new(0f, 2f, 0f);
    public float SourceUnitScale { get; init; } = 1f;
    public TextureSamplingMode TextureSampling { get; init; } = TextureSamplingMode.LinearMipmapped;
    public Dictionary<string, AnimationClipBinding> AnimationBindings { get; init; } = [];
}
