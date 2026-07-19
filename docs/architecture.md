# FPS Frenzy architecture

## Platform and dependency baseline

FPS Frenzy targets desktop (Windows, Linux, and macOS) and Android. The repository pins .NET SDK 10.0.203, C# 14, KNI 4.2.9001/4.2.9001.1, and BepuPhysics 2.4.0. Desktop runs through KNI SDL2/OpenGL. Android runs through KNI OpenGL ES with minimum API 23, target API 36, application ID `com.kni.fpsfrenzy`, debug APK output, and ARM64 release output.

All projects enable nullable reference types, deterministic builds, recommended analyzers, and warnings as errors. Package versions are managed centrally in `Directory.Packages.props`. Binary art and audio use Git LFS.

## Project boundaries

- `FpsFrenzy.Core` owns the platform-neutral simulation: stable entity IDs, ticked player commands, Bepu physics, weapons, projectiles, damage, pickups, A* navigation, robot AI, attack timing, objectives, sectors, deterministic run generation, upgrade effects, checkpoints as data, boss phases, and scoring. It uses `System.Numerics` and has no KNI dependency.
- `FpsFrenzy.Kni` owns the variable-rate game loop, rendering, XNA/KNI type conversion, animation playback, authored audio, combat feedback, HUD, menu and reward presentation, input mapping, settings, profile/checkpoint file stores, and render capture.
- `FpsFrenzy.Desktop` is the SDL2/OpenGL entry point and desktop mouse-capture adapter.
- `FpsFrenzy.Android` is the Android activity plus touch and sensor integration. Gyro is optional and never required for installation.
- `FpsFrenzy.Content.Pipeline` is a .NET Standard 2.0 KNI content extension derived from the MIT-licensed KNI/XNA Skinned Model sample. It validates Assimp-imported animated model content and converts it into the runtime skinning format.
- `FpsFrenzy.Core.Tests` covers simulation, weapons, objectives, deterministic run generation, upgrades, checkpoints, and God Mode. `FpsFrenzy.Content.Tests` covers content validation, animation interpolation/playback, visual calibration, settings, menus, profile/checkpoint persistence, compass projection, ADS, reticle, and safe-area math.

## Application and run lifecycle

The application opens on a live, slowly orbiting arena view with the simulation paused. Main-menu choices include Continue when a valid checkpoint exists, Start New Run, Loadout, Records, Settings, Accessibility, and Quit. The first new run opens a compact controls/objective card. Losing focus or opening the pause menu freezes both simulation and enemy animation.

`GameSimulation(ContentCatalog, RunConfiguration)` is the campaign entry point. `RunConfiguration` carries arena ID, seed, Standard difficulty, starting weapon, God Mode, first-run status, unlocked upgrade IDs, and an optional checkpoint. The legacy constructor and Training Ring wave data remain available as development fixtures.

For a release run, `RunDirector` deterministically selects three authored sectors without replacement. The first profile run uses the authored onboarding order; later runs derive sector and encounter order from the seed. Each sector schedules one Purge, one Relay Defense, and one Elite Hunt, for nine encounters total. The first encounter of the onboarding run is always Purge. After the ninth reward, the director switches to the central three-phase Breach Walker encounter.

The high-level state flow is:

```text
Main/Loadout
  -> EncounterActive
  -> RewardSelection -> checkpoint
  -> ... nine encounters/rewards
  -> BossActive
  -> Victory or Defeat
  -> Results
```

`RunPhase`, `EncounterObjectiveType`, `RunSnapshot`, `UpgradeOffer`, and combat events expose this state without coupling Core to the HUD. Reward selection calls `ChooseUpgrade(string upgradeId)`; simulation is held while the choice menu is open. Checkpoints represent the next encounter rather than a mid-fight snapshot.

## Fixed simulation and presentation flow

KNI samples a platform-neutral `PlayerCommand`, identified by simulation tick and player `EntityId`. Presentation can run at 30 or 60 FPS, while an accumulator advances `GameSimulation.Step` at a fixed 60 Hz. When multiple simulation ticks are needed, accumulated look input is divided over those ticks. Previous/current transforms are retained for interpolated rendering.

The main simulation order is:

1. Pause, command-edge, and run-phase handling.
2. Bepu capsule movement, grounded jump, and player/enemy separation.
3. Weapon resource, reload, heat/energy, upgrade-modified fire, and shot processing.
4. Sector objective setup, portal telegraphs, threat-budget spawning, and encounter completion.
5. A* path refresh, separation, collision sliding, behavior-specific movement, and state-driven attack windup/impact/recovery.
6. Swept projectile movement, splash/chaining, and synchronized damage events.
7. Relay damage, pickup collection, armory activation, drops, stagger/death, and enemy cleanup.
8. Reward, boss phase, victory, defeat, score, and run-stat transitions.

Attack damage and projectile creation occur on `EnemyAttackImpact`, not when an animation starts. Animation consumes the same action state and impact events, so combat timing remains authoritative in the fixed simulation.

## Arena, objectives, and spawning

Orbital Depot is a 72-by-56-meter arena built from four readable station quadrants around a recovery hub and boss floor. Release schema data gives every sector bounds, an entry point, objective anchor, spawn portals, and energy-gate IDs. Closing gates focus the active encounter without changing the map's geometry or the 7.5 m/s run-and-jump movement model.

Sector pressure is data-driven but uses the Standard campaign defaults:

| Sector | Threat budget | Maximum active | Relay duration |
|---|---:|---:|---:|
| 1 | 10 | 4 | 60 seconds |
| 2 | 16 | 6 | 75 seconds |
| 3 | 24 | 8 | 90 seconds |

Purge completes after the generated roster is cleared. Relay Defense protects a 450-health objective for the sector duration; enemy attacks deal half damage to the relay. Elite Hunt marks the highest-pressure generated enemy, grants it 40% more health, and leaves its damage unchanged.

Spawn selection rejects occupied, blocked, or non-navigable portals and enforces a 12-to-32-meter legal range. It prefers 14 to 28 meters and off-camera positions. Every accepted spawn receives at least a 0.75-second light/event telegraph before the enemy is created. The generated roster respects the active cap and guarantees all non-boss behaviors are eligible by Sector 3.

Collision, visibility, and navigation flags remain independent, so decorative station dressing never creates invisible walls. The player is an upright 1.8-meter Bepu capsule with a 7.5 m/s target speed, moderate air acceleration, and approximately 1.2-meter jump height. A 0.75-meter grid supports four-direction A*. Swept projectile tests prevent tunneling.

## Weapons, upgrades, and persistence

All six weapon slots remain available: Pulse Sidearm, Burst Carbine, Scatter Blaster, Beam Rifle, Plasma Launcher, and Arc Cannon. The run begins with the loadout weapon. After encounters one through five, a seeded unowned weapon is activated on the hub armory pad. Collecting it adds it to the current inventory and permanently unlocks it as a future starting option.

The 18 one-rank upgrades are immutable definitions. `RunModifiers` owns only the IDs selected for the current run and computes weapon-specific and general effects on demand; shared `WeaponDefinition` instances are never mutated. Offers contain three unique unlocked, unowned choices, generated deterministically from the run seed and encounter index.

The profile starts with all six weapon-signature upgrades plus Calibrated Cells, Expanded Stores, Field Loader, Reinforced Shell, Salvage Repair, and Magnetic Salvage. The remaining six enter the offer pool through challenge completion. Persistent progression unlocks options and starting weapons only; it never grants permanent combat stats.

Three independent stores live under the platform's local application-data `FPSFrenzy` directory:

- `settings.json` preserves existing settings and adds a defaulted `MusicVolume` plus God Mode.
- `profile-v1.json` stores unlock sets, selected starting weapon, challenges, tutorial state, lifetime totals, and best/recent run records.
- `run-checkpoint-v2.json` stores the seed, onboarding-order marker, next encounter index, cumulative run/player state, starting/selected weapon state, collected weapons, owned upgrades, active armory offers, and whether God Mode was used.

Profile and checkpoint stores write to a temporary file and atomically replace the destination. Missing, corrupt, inaccessible, or unsupported files fall back without damaging settings. A checkpoint is saved only after reward selection and is cleared on death, victory, or explicit abandonment.

God Mode is a persisted setting routed to `GameSimulation.SetPlayerInvulnerable`. It defaults off, blocks only incoming player damage, never heals the player, and leaves enemies, weapons, objectives, pickups, score, and unlocks unchanged. Once enabled during a run, `RunDirector` retains the marker even if it is later disabled. God Mode results remain visible but cannot replace the best unassisted record.

## Robot content and animation pipeline

The production faction maps the Quaternius Animated Mech and Sci-Fi Essentials assets to six silhouettes: Leela/Striker, Stan/Interceptor, George/Juggernaut, Eye Drone/Wasp, Mike/Warden, and Trilobite/Breach Walker. Enemy definitions author their target height, ground or hover offset, forward correction, corpse lifetime, albedo/emissive assets, emissive accent, health-bar anchor, texture sampling mode, attack timing, stagger policy, and state-to-clip bindings.

All six production robots use their official canonical glTF transforms. A reproducible AssimpNet 5.0.0 conversion writes KNI-readable `.assbin` without rewriting animation keys. Each robot receives one identity common skeleton parent and one epsilon root influence per mesh solely so KNI recognizes a single skeleton; authored local transforms remain unchanged. The conversion rejects any numerical change to animation TRS key times or values. The retained glTF files, external buffers, referenced images, and exact conversion metadata live with the asset provenance. Runtime content blocks set `NormalizeImportedBoneBasis=False`. For other imported formats, the custom processor applies any required source-unit conversion once at the scene root, never per keyframe. It then:

1. validates finite, invertible bind and animation transforms;
2. enforces the 72-bone Reach/mobile limit and required clip/material bindings;
3. resamples every bone channel to 30 Hz;
4. decomposes samples into translation, quaternion rotation, and scale;
5. strips configured root translation so simulation remains authoritative; and
6. writes per-bone tracks, bind pose, inverse bind pose, and hierarchy into the model XNB.

At runtime, `AnimationPlayer` linearly interpolates translation and scale, uses quaternion slerp for rotation, and crossfades state changes (normally 0.12 seconds). Idle and locomotion loop. Windup, active attack, recovery, hit reaction, and death clamp as one-shots; death holds its final pose. Animation advances from `Game.Update`, not `Draw`, so pause freezes it and render frame rate cannot alter playback speed.

## Rendering and HUD

The renderer remains Reach-compatible and non-PBR. Arena and prop passes use `BasicEffect`; robots use the stock `SkinnedEffect` with explicitly bound albedo, directional key/fill light, fog, a restrained emissive accent, and hit flash. Two deterministic stock-effect passes avoid introducing a second, platform-specific skinning shader: an expanded back-face shell adds a narrow additive silhouette rim, and an optional additive emissive-mask pass lights authored cores and panels. Both auxiliary passes use black fog so they fade out without adding a second copy of the arena fog color. This shell is a mobile-safe rim approximation rather than a per-pixel Fresnel calculation; it preserves the tested Reach 72-bone path on desktop and Android. Detailed robot textures build mipmaps and use linear sampling. Palette-style assets, when declared, use point sampling without mipmaps. Whole-model role tinting is not used as a material replacement.

Visual calibration creates world transforms from authored height and ground anchors rather than animated bounding spheres. Ground and hover offsets prevent foot penetration and floating, movement direction drives facing, and camera-right health bars stay billboarded. The weapon renders after a depth clear to avoid wall clipping and adds procedural equip, fire recoil, reload arc, overheat shake, recovery, movement bob, and idle sway.

The safe-area HUD shows health, weapon resource state, sector/encounter/objective progress, score, boss phase/health, reward choices, reticle and hit confirmation, subtitles, God Mode state, and the enemy compass. Enemy bearings behind the camera pin to compass edges; height differences receive chevrons, and overlapping bearings cluster.

## Audio and menus

Runtime audio is compiled from selected CC0 source files rather than generated oscillators:

- Kenney Sci-Fi Sounds supplies portals, gates, impacts, machinery, explosions, and destruction cues.
- Kenney Digital Audio supplies energy weapon, charge, and upgrade cues.
- Kenney UI Audio supplies hover, confirm, back, toggle, and results interaction cues.
- Dark Sci-Fi Audio supplies menu, intermission, combat, boss, results, and victory music.

World enemy/impact cues receive distance attenuation and stereo pan from the listener transform. First-person weapon and UI cues remain non-spatial. Master, Music, and SFX volumes are independent; subtitle captions remain available. The audio layer tolerates missing audio hardware and platform voice-limit drops so headless capture and dense combat do not fail the run.

Menu input supports keyboard, gamepad, pointer, and touch. Transitions wait for activation controls to return to neutral, preventing a click, Space, or gamepad A press from leaking into gameplay. Settings cover volume, sensitivities, FOV, 30/60 FPS, and God Mode. Accessibility covers reduced flash, screen shake, camera bob, high-contrast reticle, large HUD text, subtitles, toggle ADS, and color-vision mapping.

## Capture and visual verification

`RenderCaptureService` reads the final backbuffer after HUD rendering:

- F12 queues a PNG still in `artifacts/render-captures`.
- F11 starts or stops a 15-second, 60 FPS recording and writes `frame-00000.png`-style images under a capture subdirectory.
- `FPS_FRENZY_CAPTURE_DIR` overrides the output root.
- `FPS_FRENZY_AUTORECORD_SECONDS`, `FPS_FRENZY_RECORD_FPS`, and `FPS_FRENZY_RECORD_NAME` support deterministic automated capture runs.

`tools/Capture-Motion.ps1` sets the automation variables, runs the desktop project, and invokes ffmpeg to encode the sequence as H.264/yuv420p MP4. It accepts 30 or 60 FPS and 1-to-30-second durations, plus deterministic encounter, weapon, seed, menu, and optional God Mode controls. Standard and God Mode off remain the defaults. ffmpeg is a development dependency, never a game runtime dependency.

`tools/Capture-CharacterLab.ps1` drives the opt-in `FPS_FRENZY_CHARACTER_LAB` path. The pure `CharacterLabController` schedules each release robot across five authored animation states and three fixed camera distances. Stills freeze the sampled pose while the camera advances; state reels advance from the count of frames actually written, not wall-clock time, so the 30 and 60 FPS variants both represent exactly ten seconds. The lab uses deterministic settings and capture-local profile/checkpoint paths, never the player's files. With no Character Lab environment flag, startup follows the normal game path unchanged.

## Content validation and performance budgets

Definitions and provenance are versioned JSON loaded with `System.Text.Json`. Startup and tests validate IDs and cross-references, weapon modes and output bands, enemy combat ranges and attack timing, sector bounds, portals, objectives, boss thresholds/summons, upgrade effects, animation bindings, visual offsets, texture sampling metadata, and arena navigation. The content processor rejects missing clips or albedo, invalid transforms, and skeletons above 72 bones. The machine-readable asset manifest records source URL, version, license, local name, and modifications for selected files.

Training Ring and its legacy wave schema remain raw repository development fixtures and are not copied into the shipping MGCB catalog. The release Orbital Depot campaign uses sectors, generated encounters, reward progression, and the robot roster exclusively.

Performance targets are fixed 60 Hz simulation, 60 FPS desktop presentation, and 60 FPS Android presentation with the existing 30 FPS fallback. Reach-compatible effects, bounded active enemies, reusable physics/navigation/HUD structures, and event allocation limited to content/state transitions keep the mobile path predictable.

## Future co-op seam

A future authoritative server could receive tick-numbered `PlayerCommand` values and own `GameSimulation.Step`; clients could interpolate stable-ID snapshots. Before networking work begins, all random events would need an injectable source and snapshots would need a protocol version. The current project intentionally contains no networking, accounts, lobbies, cloud saves, analytics, ads, or purchases.
