using System.Buffers.Binary;
using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework.Audio;

namespace FpsFrenzy.Kni.Audio;

public sealed class CombatAudio : IDisposable
{
    private const int SampleRate = 22050;
    private readonly Dictionary<string, SoundEffect> _cues = new(StringComparer.OrdinalIgnoreCase);
    private SoundEffectInstance? _ambience;
    private bool _available;
    private bool _disposed;

    public CombatAudio(GameSettings settings)
    {
        try
        {
            Add("pulse-sidearm", 620f, 0.08f, SynthWave.Square, 0.72f);
            Add("burst-carbine", 330f, 0.065f, SynthWave.Noise, 0.65f);
            Add("scatter-blaster", 115f, 0.14f, SynthWave.Noise, 0.9f);
            Add("beam-rifle", 980f, 0.055f, SynthWave.Saw, 0.45f);
            Add("plasma-launcher", 210f, 0.19f, SynthWave.Sine, 0.78f);
            Add("arc-cannon", 440f, 0.22f, SynthWave.Square, 0.7f);
            Add("hit", 880f, 0.04f, SynthWave.Noise, 0.45f);
            Add("kill", 170f, 0.18f, SynthWave.Saw, 0.75f);
            Add("dry", 95f, 0.045f, SynthWave.Square, 0.35f);
            Add("reload", 245f, 0.07f, SynthWave.Noise, 0.38f);
            Add("player-damaged", 82f, 0.16f, SynthWave.Saw, 0.72f);
            Add("pickup", 740f, 0.12f, SynthWave.Sine, 0.52f);
            Add("wave", 390f, 0.28f, SynthWave.Square, 0.45f);
            Add("boss", 68f, 0.48f, SynthWave.Saw, 0.85f);
            Add("enemy-shot", 280f, 0.1f, SynthWave.Sine, 0.4f);
            Add("charge", 125f, 0.25f, SynthWave.Saw, 0.7f);
            Add("support", 520f, 0.24f, SynthWave.Sine, 0.4f);

            SoundEffect ambient = Create(52f, 0.9f, SynthWave.Sine, 0.2f, seed: 77);
            _cues.Add("ambient", ambient);
            _ambience = ambient.CreateInstance();
            _ambience.IsLooped = true;
            ApplySettings(settings);
            _ambience.Play();
            _available = true;
        }
        catch (NoAudioHardwareException)
        {
            DisposeAudioResources();
        }
    }

    public string? Caption { get; private set; }
    public float CaptionRemainingSeconds { get; private set; }

    public void Consume(IReadOnlyList<CombatEvent> events, GameSettings settings)
    {
        if (!_available)
        {
            return;
        }

        bool hitPlayed = false;
        foreach (CombatEvent combatEvent in events)
        {
            switch (combatEvent.Type)
            {
                case CombatEventType.WeaponFired:
                    Play(combatEvent.CueId, settings, pitch: 0f);
                    break;
                case CombatEventType.DryFire:
                    Play("dry", settings);
                    SetCaption("WEAPON EMPTY");
                    break;
                case CombatEventType.ReloadStarted:
                    Play("reload", settings, pitch: -0.12f);
                    SetCaption("RELOADING");
                    break;
                case CombatEventType.ReloadCompleted:
                    Play("reload", settings, pitch: 0.18f);
                    break;
                case CombatEventType.EnemyHit when !hitPlayed:
                    Play("hit", settings);
                    hitPlayed = true;
                    break;
                case CombatEventType.EnemyKilled:
                    Play("kill", settings);
                    SetCaption("TARGET DOWN");
                    break;
                case CombatEventType.PlayerDamaged:
                    Play("player-damaged", settings);
                    SetCaption("IMPACT DETECTED");
                    break;
                case CombatEventType.PickupCollected:
                    Play("pickup", settings);
                    SetCaption($"{combatEvent.CueId?.ToUpperInvariant()} ACQUIRED");
                    break;
                case CombatEventType.WaveStarted:
                    Play(combatEvent.CueId == "boss-wave" ? "boss" : "wave", settings);
                    SetCaption(combatEvent.CueId == "boss-wave" ? "WARNING: MASSIVE HOSTILE" : "INCOMING WAVE");
                    break;
                case CombatEventType.BossPhaseChanged:
                    Play("boss", settings, pitch: 0.12f);
                    SetCaption(combatEvent.CueId ?? "BOSS PHASE CHANGED");
                    break;
                case CombatEventType.EnemyTelegraph:
                    Play("charge", settings);
                    SetCaption("CHARGE INCOMING");
                    break;
                case CombatEventType.EnemyAttack:
                    Play(combatEvent.CueId == "charge" ? "charge" : "enemy-shot", settings, volumeScale: 0.7f);
                    break;
                case CombatEventType.SupportPulse:
                    Play("support", settings, volumeScale: 0.7f);
                    SetCaption("WARDEN SUPPORT PULSE");
                    break;
            }
        }
    }

    public void Update(float deltaSeconds, GameSettings settings)
    {
        ApplySettings(settings);
        CaptionRemainingSeconds = MathF.Max(0f, CaptionRemainingSeconds - deltaSeconds);
        if (CaptionRemainingSeconds <= 0f)
        {
            Caption = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeAudioResources();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void Add(string id, float frequency, float duration, SynthWave wave, float amplitude) =>
        _cues.Add(id, Create(frequency, duration, wave, amplitude, StringComparer.Ordinal.GetHashCode(id)));

    private void Play(
        string? cueId,
        GameSettings settings,
        float pitch = 0f,
        float volumeScale = 1f)
    {
        if (cueId is null || !_cues.TryGetValue(cueId, out SoundEffect? effect))
        {
            return;
        }

        try
        {
            effect.Play(Math.Clamp(settings.SoundEffectsVolume * volumeScale, 0f, 1f), pitch, 0f);
        }
        catch (InstancePlayLimitException)
        {
            // Dense combat can exceed a platform's voice limit; dropping a duplicate cue is intentional.
        }
    }

    private void ApplySettings(GameSettings settings)
    {
        SoundEffect.MasterVolume = settings.MasterVolume;
        if (_ambience is not null)
        {
            _ambience.Volume = Math.Clamp(settings.SoundEffectsVolume * 0.12f, 0f, 1f);
        }
    }

    private void SetCaption(string caption)
    {
        Caption = caption;
        CaptionRemainingSeconds = 1.45f;
    }

    private void DisposeAudioResources()
    {
        _ambience?.Dispose();
        _ambience = null;
        foreach (SoundEffect effect in _cues.Values)
        {
            effect.Dispose();
        }

        _cues.Clear();
        _available = false;
    }

    private static SoundEffect Create(
        float frequency,
        float duration,
        SynthWave wave,
        float amplitude,
        int seed)
    {
        int sampleCount = Math.Max(1, (int)(SampleRate * duration));
        byte[] buffer = new byte[sampleCount * sizeof(short)];
        Random random = new(seed);
        for (int index = 0; index < sampleCount; index++)
        {
            float time = index / (float)SampleRate;
            float phase = time * frequency * MathF.Tau;
            float sample = wave switch
            {
                SynthWave.Sine => MathF.Sin(phase),
                SynthWave.Square => MathF.Sin(phase) >= 0f ? 1f : -1f,
                SynthWave.Saw => 2f * ((time * frequency) - MathF.Floor(0.5f + (time * frequency))),
                SynthWave.Noise => (random.NextSingle() * 2f) - 1f,
                _ => 0f,
            };
            float normalized = index / (float)sampleCount;
            float attack = Math.Clamp(normalized / 0.04f, 0f, 1f);
            float decay = MathF.Pow(1f - normalized, 1.7f);
            short value = (short)(Math.Clamp(sample * amplitude * attack * decay, -1f, 1f) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(index * sizeof(short), sizeof(short)), value);
        }

        return new SoundEffect(buffer, SampleRate, AudioChannels.Mono);
    }

    private enum SynthWave
    {
        Sine,
        Square,
        Saw,
        Noise,
    }
}
