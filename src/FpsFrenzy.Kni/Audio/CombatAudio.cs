using FpsFrenzy.Core.Simulation;
using FpsFrenzy.Kni.Settings;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Media;
using Vector3 = System.Numerics.Vector3;

namespace FpsFrenzy.Kni.Audio;

public enum AudioMusicState
{
    None,
    Menu,
    Intermission,
    AdventureExplore,
    Combat,
    Boss,
    Victory,
    Results,
    Defeat,
}

public sealed class CombatAudio : IDisposable
{
    private const int MaximumSpatialVoices = 12;
    private static readonly string[] CueIds =
    [
        "pulse-sidearm",
        "burst-carbine",
        "scatter-blaster",
        "beam-rifle",
        "plasma-launcher",
        "arc-cannon",
        "hit",
        "kill",
        "dry",
        "reload",
        "player-damaged",
        "pickup",
        "wave",
        "boss",
        "enemy-shot",
        "charge",
        "support",
        "portal",
        "gate-open",
        "gate-close",
        "ui-hover",
        "ui-confirm",
        "ui-back",
        "ui-toggle",
        "upgrade",
    ];

    private readonly Dictionary<string, SoundEffect> _cues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AudioMusicState, Song> _music = [];
    private readonly List<SoundEffectInstance> _spatialVoices = [];
    private bool _available;
    private bool _disposed;
    private AudioMusicState _musicState;
    private AudioMusicState _requestedMusicState;
    private MusicTransitionPhase _musicTransitionPhase;
    private float _musicTransitionSeconds;
    private bool _victoryStingerActive;
    private bool _victoryStingerObservedPlaying;

    public CombatAudio(ContentManager content, GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            foreach (string cueId in CueIds)
            {
                _cues.Add(cueId, content.Load<SoundEffect>($"Audio/Sfx/{cueId}"));
            }

            _music.Add(AudioMusicState.Menu, LoadMusic(content, AudioMusicState.Menu));
            _music.Add(AudioMusicState.Intermission, LoadMusic(content, AudioMusicState.Intermission));
            _music.Add(AudioMusicState.AdventureExplore, LoadMusic(content, AudioMusicState.AdventureExplore));
            _music.Add(AudioMusicState.Combat, LoadMusic(content, AudioMusicState.Combat));
            _music.Add(AudioMusicState.Boss, LoadMusic(content, AudioMusicState.Boss));
            _music.Add(AudioMusicState.Victory, LoadMusic(content, AudioMusicState.Victory));
            Song resultsMusic = LoadMusic(content, AudioMusicState.Results);
            _music.Add(AudioMusicState.Results, resultsMusic);
            _music.Add(AudioMusicState.Defeat, resultsMusic);
            _available = true;
            ApplySettings(settings);
        }
        catch (ContentLoadException)
        {
            _cues.Clear();
            _music.Clear();
        }
        catch (NoAudioHardwareException)
        {
            _cues.Clear();
            _music.Clear();
        }
    }

    public string? Caption { get; private set; }
    public float CaptionRemainingSeconds { get; private set; }

    public void Consume(
        IReadOnlyList<CombatEvent> events,
        GameSettings settings,
        Vector3 listenerPosition,
        Vector3 listenerForward)
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
                    Play(combatEvent.CueId, settings);
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
                case CombatEventType.WorldImpact:
                    PlaySpatial("hit", combatEvent.Position, listenerPosition, listenerForward, settings, 0.28f);
                    break;
                case CombatEventType.EnemyHit when !hitPlayed:
                    PlaySpatial("hit", combatEvent.Position, listenerPosition, listenerForward, settings, 0.7f);
                    hitPlayed = true;
                    break;
                case CombatEventType.EnemyKilled:
                    PlaySpatial("kill", combatEvent.Position, listenerPosition, listenerForward, settings);
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
                    SetCaption(combatEvent.CueId == "boss-wave" ? "WARNING: BREACH WALKER" : "HOSTILES INBOUND");
                    break;
                case CombatEventType.BossPhaseChanged:
                    Play("boss", settings, pitch: 0.12f);
                    SetCaption(combatEvent.CueId ?? "BOSS PHASE CHANGED");
                    break;
                case CombatEventType.EnemyTelegraph:
                    PlaySpatial("charge", combatEvent.Position, listenerPosition, listenerForward, settings);
                    SetCaption("CHARGE INCOMING");
                    break;
                case CombatEventType.EnemyAttackStarted:
                    PlaySpatial("charge", combatEvent.Position, listenerPosition, listenerForward, settings, 0.72f);
                    break;
                case CombatEventType.EnemyAttack:
                    PlaySpatial(
                        combatEvent.CueId == "charge" ? "charge" : "enemy-shot",
                        combatEvent.Position,
                        listenerPosition,
                        listenerForward,
                        settings,
                        0.8f);
                    break;
                case CombatEventType.SupportPulse:
                    PlaySpatial("support", combatEvent.Position, listenerPosition, listenerForward, settings, 0.8f);
                    SetCaption("WARDEN SUPPORT PULSE");
                    break;
                case CombatEventType.EnemySpawnTelegraph:
                    PlaySpatial("portal", combatEvent.Position, listenerPosition, listenerForward, settings, 0.9f);
                    SetCaption("PORTAL CHARGING");
                    break;
                case CombatEventType.SectorActivated:
                    Play("gate-close", settings, volumeScale: 0.85f);
                    SetCaption($"{combatEvent.CueId?.Replace('-', ' ').ToUpperInvariant()} SEALED");
                    break;
                case CombatEventType.EncounterStarted:
                    Play("wave", settings, pitch: -0.06f, volumeScale: 0.8f);
                    SetCaption($"OBJECTIVE {combatEvent.CueId?.ToUpperInvariant()}");
                    break;
                case CombatEventType.EncounterCompleted:
                    Play("gate-open", settings, volumeScale: 0.9f);
                    SetCaption("SECTOR OBJECTIVE COMPLETE");
                    break;
                case CombatEventType.EncounterFailed:
                    Play("boss", settings, pitch: -0.18f);
                    SetCaption("OBJECTIVE FAILED");
                    break;
                case CombatEventType.ArmoryActivated:
                    Play("upgrade", settings, pitch: 0.12f);
                    SetCaption($"ARMORY ONLINE {combatEvent.CueId?.Replace('-', ' ').ToUpperInvariant()}");
                    break;
                case CombatEventType.UpgradeApplied:
                    Play("upgrade", settings);
                    SetCaption($"{combatEvent.CueId?.Replace('-', ' ').ToUpperInvariant()} INSTALLED");
                    break;
                case CombatEventType.RelayDamaged:
                    PlaySpatial("hit", combatEvent.Position, listenerPosition, listenerForward, settings, 0.34f);
                    SetCaption("RELAY UNDER ATTACK");
                    break;
            }
        }
    }

    public void PlayInterfaceCue(string cueId, GameSettings settings) => Play(cueId, settings, volumeScale: 0.7f);

    public void SetMusicState(AudioMusicState state, GameSettings settings)
    {
        ApplySettings(settings, MusicVolumeScale());
        if (!_available || state == _requestedMusicState)
        {
            return;
        }

        _requestedMusicState = state;
        _victoryStingerActive = false;
        _victoryStingerObservedPlaying = false;
        if (_musicState == AudioMusicState.None)
        {
            StartRequestedMusic();
            return;
        }
        _musicTransitionPhase = MusicTransitionPhase.FadingOut;
        _musicTransitionSeconds = 0f;
    }

    public void Update(float deltaSeconds, GameSettings settings)
    {
        UpdateMusicTransition(deltaSeconds, settings);
        TryAdvanceVictorySequence();
        for (int index = _spatialVoices.Count - 1; index >= 0; index--)
        {
            if (_spatialVoices[index].State != SoundState.Stopped)
            {
                continue;
            }

            _spatialVoices[index].Dispose();
            _spatialVoices.RemoveAt(index);
        }

        CaptionRemainingSeconds = MathF.Max(0f, CaptionRemainingSeconds - deltaSeconds);
        if (CaptionRemainingSeconds <= 0f)
        {
            Caption = null;
        }
    }

    internal static AudioMusicState? ResolveVictoryFollowUp(
        AudioMusicState requestedState,
        bool stingerActive,
        bool observedPlaying,
        MediaState playbackState) =>
        requestedState == AudioMusicState.Victory && stingerActive && observedPlaying &&
        playbackState == MediaState.Stopped
            ? AudioMusicState.Results
            : null;

    internal static string GetMusicAssetName(AudioMusicState state) => state switch
    {
        AudioMusicState.Menu => "title",
        AudioMusicState.Intermission => "airy",
        AudioMusicState.AdventureExplore => "sector",
        AudioMusicState.Combat => "pulse",
        AudioMusicState.Boss => "urgent",
        AudioMusicState.Victory => "victory",
        AudioMusicState.Results or AudioMusicState.Defeat => "transmission",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private void TryAdvanceVictorySequence()
    {
        if (!_available || !_victoryStingerActive)
        {
            return;
        }

        try
        {
            MediaState playbackState = MediaPlayer.State;
            if (playbackState == MediaState.Playing)
            {
                _victoryStingerObservedPlaying = true;
                return;
            }

            AudioMusicState? followUp = ResolveVictoryFollowUp(
                _musicState,
                _victoryStingerActive,
                _victoryStingerObservedPlaying,
                playbackState);
            if (followUp is not AudioMusicState resultsState ||
                !_music.TryGetValue(resultsState, out Song? resultsMusic))
            {
                return;
            }

            MediaPlayer.IsRepeating = true;
            MediaPlayer.Play(resultsMusic);
            _victoryStingerActive = false;
            _victoryStingerObservedPlaying = false;
        }
        catch (InvalidOperationException)
        {
            // Media playback is optional on headless test/capture machines.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            MediaPlayer.Stop();
        }
        catch (InvalidOperationException)
        {
        }

        _cues.Clear();
        _music.Clear();
        foreach (SoundEffectInstance voice in _spatialVoices)
        {
            voice.Stop();
            voice.Dispose();
        }
        _spatialVoices.Clear();
        _available = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void PlaySpatial(
        string cueId,
        Vector3 sourcePosition,
        Vector3 listenerPosition,
        Vector3 listenerForward,
        GameSettings settings,
        float volumeScale = 1f)
    {
        Vector3 offset = sourcePosition - listenerPosition;
        float distance = offset.Length();
        float attenuation = Math.Clamp(1f - (distance / 55f), 0.14f, 1f);
        float pan = 0f;
        if (distance > 0.001f)
        {
            Vector3 planarForward = new(listenerForward.X, 0f, listenerForward.Z);
            if (planarForward.LengthSquared() > 0.001f)
            {
                planarForward = Vector3.Normalize(planarForward);
                Vector3 right = Vector3.Normalize(Vector3.Cross(planarForward, Vector3.UnitY));
                pan = Math.Clamp(Vector3.Dot(Vector3.Normalize(offset), right), -1f, 1f);
            }
        }

        PlaySpatialInstance(cueId, settings, volumeScale * attenuation, pan);
    }

    private void PlaySpatialInstance(string cueId, GameSettings settings, float volumeScale, float pan)
    {
        if (!_cues.TryGetValue(cueId, out SoundEffect? effect))
        {
            return;
        }

        try
        {
            while (_spatialVoices.Count >= MaximumSpatialVoices)
            {
                SoundEffectInstance oldest = _spatialVoices[0];
                oldest.Stop();
                oldest.Dispose();
                _spatialVoices.RemoveAt(0);
            }

            SoundEffectInstance instance = effect.CreateInstance();
            instance.Volume = Math.Clamp(settings.SoundEffectsVolume * volumeScale, 0f, 1f);
            instance.Pan = Math.Clamp(pan, -1f, 1f);
            instance.Play();
            _spatialVoices.Add(instance);
        }
        catch (InstancePlayLimitException)
        {
        }
    }

    private void Play(
        string? cueId,
        GameSettings settings,
        float pitch = 0f,
        float volumeScale = 1f,
        float pan = 0f)
    {
        if (cueId is null || !_cues.TryGetValue(cueId, out SoundEffect? effect))
        {
            return;
        }

        try
        {
            effect.Play(
                Math.Clamp(settings.SoundEffectsVolume * volumeScale, 0f, 1f),
                Math.Clamp(pitch, -1f, 1f),
                Math.Clamp(pan, -1f, 1f));
        }
        catch (InstancePlayLimitException)
        {
            // Dense combat can exceed a platform's voice limit; duplicate cues may drop.
        }
    }

    private static void ApplySettings(GameSettings settings, float musicVolumeScale = 1f)
    {
        SoundEffect.MasterVolume = settings.MasterVolume;
        MediaPlayer.Volume = Math.Clamp(
            settings.MasterVolume * settings.MusicVolume * musicVolumeScale, 0f, 1f);
    }

    private void UpdateMusicTransition(float deltaSeconds, GameSettings settings)
    {
        _musicTransitionSeconds += MathF.Max(0f, deltaSeconds);
        if (_musicTransitionPhase == MusicTransitionPhase.FadingOut && _musicTransitionSeconds >= 0.35f)
        {
            StartRequestedMusic();
        }
        else if (_musicTransitionPhase == MusicTransitionPhase.FadingIn && _musicTransitionSeconds >= 0.45f)
        {
            _musicTransitionPhase = MusicTransitionPhase.None;
            _musicTransitionSeconds = 0f;
        }
        ApplySettings(settings, MusicVolumeScale());
    }

    private float MusicVolumeScale() => _musicTransitionPhase switch
    {
        MusicTransitionPhase.FadingOut => 1f - Math.Clamp(_musicTransitionSeconds / 0.35f, 0f, 1f),
        MusicTransitionPhase.FadingIn => Math.Clamp(_musicTransitionSeconds / 0.45f, 0f, 1f),
        _ => 1f,
    };

    private void StartRequestedMusic()
    {
        try
        {
            if (_requestedMusicState == AudioMusicState.None ||
                !_music.TryGetValue(_requestedMusicState, out Song? song))
            {
                MediaPlayer.Stop();
                _musicState = AudioMusicState.None;
                _musicTransitionPhase = MusicTransitionPhase.None;
                _musicTransitionSeconds = 0f;
                return;
            }

            _musicState = _requestedMusicState;
            MediaPlayer.IsRepeating = _musicState != AudioMusicState.Victory;
            MediaPlayer.Play(song);
            _victoryStingerActive = _musicState == AudioMusicState.Victory;
            _musicTransitionPhase = MusicTransitionPhase.FadingIn;
            _musicTransitionSeconds = 0f;
        }
        catch (InvalidOperationException)
        {
            _musicTransitionPhase = MusicTransitionPhase.None;
            _musicTransitionSeconds = 0f;
        }
    }

    private enum MusicTransitionPhase
    {
        None,
        FadingOut,
        FadingIn,
    }

    private static Song LoadMusic(ContentManager content, AudioMusicState state) =>
        content.Load<Song>($"Audio/Music/{GetMusicAssetName(state)}");

    private void SetCaption(string caption)
    {
        Caption = caption;
        CaptionRemainingSeconds = 1.45f;
    }
}
