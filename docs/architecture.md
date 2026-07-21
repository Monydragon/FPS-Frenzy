# FPS Frenzy architecture

## Platform and dependency baseline

FPS Frenzy targets desktop (Windows, Linux, and macOS) and Android. The repository pins .NET SDK 10.0.203, C# 14, KNI 4.2.9001/4.2.9001.1, and BepuPhysics 2.4.0. Desktop runs through KNI SDL2/OpenGL. Android runs through KNI OpenGL ES with minimum API 23, target API 36, application ID `com.kni.fpsfrenzy`, debug APK output, and ARM64 release output.

All projects enable nullable reference types, deterministic builds, recommended analyzers, and warnings as errors. Package versions are managed centrally in `Directory.Packages.props`. Binary art and audio use Git LFS.

## Project boundaries

- `FpsFrenzy.Core` owns the platform-neutral simulation: stable entity IDs, ticked player commands, Bepu physics, weapons, projectiles, damage, pickups, A* navigation, robot AI, attack timing, Arena objectives, deterministic dungeon generation, Adventure state, upgrade effects, checkpoints as data, boss phases, and scoring. It uses `System.Numerics` and has no KNI dependency.
- `FpsFrenzy.Kni` owns the variable-rate game loop, rendering, XNA/KNI type conversion, animation playback, authored audio, combat feedback, HUD, menu and reward presentation, input mapping, settings, profile/checkpoint file stores, and render capture.
- `FpsFrenzy.Desktop` is the SDL2/OpenGL entry point and desktop mouse-capture adapter.
- `FpsFrenzy.Android` is the Android activity plus touch and sensor integration. Gyro is optional and never required for installation.
- `FpsFrenzy.Content.Pipeline` is a .NET Standard 2.0 KNI content extension derived from the MIT-licensed KNI/XNA Skinned Model sample. It validates Assimp-imported animated model content and converts it into the runtime skinning format.
- `FpsFrenzy.Core.Tests` covers simulation, weapons, objectives, deterministic run generation, upgrades, checkpoints, and God Mode. `FpsFrenzy.Content.Tests` covers content validation, animation interpolation/playback, visual calibration, settings, menus, profile/checkpoint persistence, compass projection, ADS, reticle, and safe-area math.

## Application and run lifecycle

The application opens on generated, text-free orbital-station title key art while the simulation remains paused behind it. The main menu groups Play, Operative, Arsenal, mode-tabbed Records, Settings, and Quit. Play exposes independent Continue/New actions for Arena and Adventure plus Debug Lab. Starting over when that mode has a checkpoint requires explicit confirmation. Losing focus or opening the pause menu freezes both simulation and enemy animation.

`GameSimulation(ContentCatalog, RunConfiguration)` is the campaign entry point. `RunConfiguration` carries `GameMode`, arena/Adventure ID, seed, one of six combat difficulties, Threat Tier, two starting weapon sets, persistent progression, God Mode, first-run status, unlocked upgrade IDs, and the applicable optional checkpoint. The legacy constructor and Training Ring wave data remain available as development fixtures.

For a release run, `RunDirector` deterministically selects three authored sectors without replacement. The first profile run uses the authored onboarding order; later runs derive sector and encounter order from the seed. Each sector schedules one Purge, one Relay Defense, and one Elite Hunt, for nine encounters total. The first encounter of the onboarding run is always Purge. After the ninth reward, the director switches to the central three-phase Breach Walker encounter.

The high-level Arena state flow is:

```text
Main/Loadout
  -> EncounterActive
  -> RecoveryLoot -> RewardSelection -> progression commit/checkpoint
  -> ... nine encounters/rewards
  -> BossActive
  -> Victory or Defeat
  -> Results
```

`RunPhase`, `EncounterObjectiveType`, `RunSnapshot`, `UpgradeOffer`, and combat events expose this state without coupling Core to the HUD. Reward selection calls `ChooseUpgrade(string upgradeId)`; simulation is held while the choice menu is open. Checkpoints represent the next encounter rather than a mid-fight snapshot.

Adventure uses a separate `AdventureDirector` and `AdventureSnapshot`: stage entry leads through free exploration, objective interaction, optional chest/lore branches, an explicitly unlocked exit lift, a floor boon and committed checkpoint, then the next generated floor. Transmissions do not consume or block a nearby world interaction. After three floors, the authored Core Chamber starts with two shield controls before the Core Warden becomes vulnerable. Adventure resumes reconstruct the current stage entrance rather than serializing live enemies, gates, or visited-map state.

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

Orbital Depot is a continuously traversable 72-by-56-meter arena built from four readable station quadrants around a recovery hub and boss floor. Release schema 3 uses `OpenArena`: sector bounds remain objective, lighting, portal-weighting, floor-marking, and HUD metadata only. No player, enemy, or boss clamp uses them. Its visible floor is partitioned into non-overlapping regions, outer bulkheads meet without positive-volume intersections, and dressing is restricted to bounded flush wall mounts. Validation rejects visible overlap, out-of-bounds placement, floor penetration, floating props, missing mount metadata, and player-clearance violations.

Sector pressure is data-driven and keeps the same encounter population at every named difficulty:

| Sector | Threat budget | Maximum active | Relay duration |
|---|---:|---:|---:|
| 1 | 10 | 4 | 60 seconds |
| 2 | 16 | 6 | 75 seconds |
| 3 | 24 | 8 | 90 seconds |

Purge completes after the generated roster is cleared. Relay Defense protects a 450-health objective for the sector duration; enemy attacks deal half damage to the relay. Elite Hunt marks the highest-pressure generated enemy, grants it 40% more health, and leaves its damage unchanged.

Spawn selection considers every arena portal, prefers the active sector, rejects occupied, blocked, or non-navigable points, and enforces a 12-to-32-meter legal range. It prefers 14 to 28 meters and off-camera positions. Every accepted spawn receives at least a 0.75-second light/event telegraph before the enemy is created. The generated roster respects the active cap and guarantees all non-boss behaviors are eligible by Sector 3.

Collision, visibility, and navigation flags remain independent, so decorative station dressing never creates invisible walls. The player is an upright 1.8-meter Bepu capsule with a 7.5 m/s target speed, moderate air acceleration, and approximately 1.2-meter jump height. A 0.75-meter grid supports four-direction A*. Swept projectile tests prevent tunneling.

## Adventure generation and objectives

`DungeonGenerator` uses a versioned PCG integer stream and independent per-floor sub-seeds. A 48-by-40 integer grid at two meters per cell constrains floors to 96 by 80 meters. Non-overlapping rectangular rooms are joined by four-meter-wide axis-aligned corridors; a deterministic spanning path is augmented with bounded loops and optional branches, then the walkable-cell union is merged into floor, wall, and flush-ceiling primitives. The same Adventure ID, positive 32-bit seed, generator version, and floor index reproduce the room graph, geometry, roles, objectives, decoration sockets, and enemy composition across desktop and Android.

Generation validates occupancy, bounds, connectivity, objective order, spawns, 3.8-meter vertical and 1.2-meter lateral clearance, decoration mounts, and positive-volume geometry intersections. It retries with up to 32 deterministic salts before selecting a validated fixed fallback. Maintenance Deck has eight rooms and two chests, Fabrication Ring ten rooms and three chests, and Signal Core nine rooms and three chests; every floor also guarantees a persistent lore terminal. Chest loot guarantees up to two missing quickbar weapon families per opening and otherwise uses a higher weapon roll so the ten-family arsenal can be explored during Adventure. Toggleable energy-gate colliders feed the same enabled-state query used by player/enemy movement, A*, hitscan, projectiles, and line of sight. Patrol groups are dormant until line-of-sight or objective activation and no more than eight enemies may be alerted.

The floors use arcing conduits, timed fabrication lasers, and signal-surge pads. Story beats are skippable radio text and terminal panels, while discovered lore persists in the shared profile. The north-up HUD map reveals visited room and corridor cells; the expanded pause map adds player, objective, locked-door, exit, cache, and lore markers without revealing undiscovered secrets.

## Weapons, difficulty, upgrades, and persistence

Ten `WeaponArchetypeDefinition` files and 50 `WeaponBaseDefinition` records resolve into immutable runtime definitions. The families are Pulse, SMG, Burst, Scatter, Precision, Beam, Plasma, Arc, Heavy, and Experimental. Typed delivery, trigger, motion, and effect definitions cover hitscan/projectile/beam weapons, semi/automatic/burst/charge/continuous/spool fire, straight/ballistic/homing/returning motion, and pierce, ricochet, splash, chain, cluster, split, pull, knockback, damage-over-time, stun, ramping, and weak-point bonuses. `tools/Sync-WeaponContent.ps1` validates source models and MGCB registration without C# catalog edits.

All 50 bases are selectable as Common, affix-free armory issues on a fresh profile. A run owns Set A and Set B; each set accepts one two-handed weapon, one one-handed weapon, or two independently fired one-handed weapons. A 0.35-second swap cancels charge/fire/reload and holstered heat or energy recovers at half speed. Only the active set contributes weapon stats and effects. Issued instances are distinct, run-bound, use the selected Threat Tier's minimum Item Power, and never persist or salvage.

Precision weapons use a true 4x camera FOV, 42% scoped look sensitivity, low magazines, slow cadence, and authored direct-hit weak-point multipliers. Enemy head/core volumes are data-authored; splash and chain damage deliberately bypass weak-point bonuses.

`DifficultyDefinition` supplies Casual, Easy, Normal, Hard, Very Hard, and Extreme packages. Difficulty scales enemy health/damage, attack interval, projectile speed, and tells while preserving minimum tells. It also makes health/ammo drops and their refill amounts scarcer as difficulty rises, while adding progressively stronger rarity luck. Threat Tier independently controls Item Power, its base rarity distribution, XP/AP, and its existing health/damage scale. The two packages compose multiplicatively without changing encounter population or the seed-authored roster.

The 18 one-rank run boons are immutable definitions. `RunModifiers` owns only the IDs selected for the current run and computes effects on demand; shared weapon definitions are never mutated. Persistent level/talents, family proficiency, equipment ability mastery, stash loot, and threat unlocks commit once at recovery/reward checkpoints.

Four independent stores live under the platform's local application-data `FPSFrenzy` directory:

- `settings.json` preserves controls, audio, accessibility, and God Mode.
- `profile-v2.json` (schema 6) plus its generation-matched stash store shared RPG progression, difficulty, both starter sets, mode-specific records, lore, completion state, and unlimited inventory.
- `run-checkpoint-v4.json` stores deterministic campaign state, difficulty, both sets and independent hand states, issued instances, recovery loot, and pending progression.
- `adventure-checkpoint-v1.json` stores committed Adventure stage-entry state independently of the Arena checkpoint.

Profile/stash generations and checkpoints use atomic replacement with backup recovery. Missing, corrupt, generation-mismatched, inaccessible, or unsupported data falls back safely without damaging settings. Legacy profiles through schema 5 and supported Arena checkpoints migrate in place. Arena saves after recovery and reward selection; Adventure saves at run entry and after each floor boon, so a mid-floor retry reconstructs the committed stage entrance and discards uncommitted floor rewards.

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

Visual calibration creates world transforms from authored height and ground anchors rather than animated bounding spheres. Ground and hover offsets prevent foot penetration and floating, movement direction drives facing, and camera-right health bars stay billboarded. Weapons use normalized canonical forward/up axes, explicit muzzle/grip anchors, and validated camera-away muzzle orientation. They render model-only after a depth clear to avoid wall clipping—no placeholder primitive hands or calibration cubes are attached—and add procedural equip, fire recoil, reload arc, charge, overheat/vent, recovery, holster/swap, movement bob, and idle sway. Every family has an authored ADS FOV and focus frame; Precision ADS adds its dedicated 4x scope and weak-point reticle.

The safe-area HUD shows separate armor and health meters, recharge/recovery state, weapon resource state, sector/floor objectives, score, boss phase/health, reward choices, Adventure transmissions and minimap, seed/version/hash diagnostics, reticle and hit confirmation, subtitles, God Mode state, and the enemy compass. Incoming damage is reduced by the existing equipment/talent armor rating, consumes the 100-point rechargeable armor layer first, and spills into health. Armor recharge begins after four damage-free seconds; slower health recovery begins after seven seconds and only once armor is full. Adventure additionally shows explored rooms, opened/total chests, lore, exit lock/readiness and distance, plus contextual chest, terminal, objective, and lift prompts. Stage entry uses a reduced-motion-aware descent transition. Enemy bearings behind the camera pin to compass edges; height differences receive chevrons, and overlapping bearings cluster.

## Audio and menus

Runtime audio is compiled from selected CC0 source files rather than generated oscillators:

- Kenney Sci-Fi Sounds supplies portals, gates, impacts, machinery, explosions, and destruction cues.
- Kenney Digital Audio supplies energy weapon, charge, and upgrade cues.
- Kenney UI Audio supplies hover, confirm, back, toggle, and results interaction cues.
- Dark Sci-Fi Audio supplies menu, Adventure exploration (`sector`), intermission, combat, boss, results, and victory music.

World enemy/impact cues receive distance attenuation and stereo pan from the listener transform. First-person weapon and UI cues remain non-spatial. Master, Music, and SFX volumes are independent; subtitle captions remain available. Adventure exploration maps to `sector`, alert combat to `pulse`, floor rewards to `airy`, and Core Warden to `urgent`. Music changes use a 0.35-second fade-out, 0.45-second fade-in, and 1.5-second combat-clear grace period. The audio layer tolerates missing audio hardware and platform voice-limit drops so headless capture and dense combat do not fail the run.

Menus and overlays use Oxanium SpriteFonts over scaled dark-glass panels with cyan focus, amber warnings, teal success, coral alerts, and redundant color-vision cues. Layout derives from a 1280-by-720 reference using the safe-area minimum axis; large text adds 1.25x scaling, with wrapping, ellipsis, detail panes, and clipped row viewports for dense pages. Viewports expose a proportional scroll thumb, arrow buttons, current range, mouse-wheel input, touch dragging, pointer selection, and focus-following keyboard/gamepad navigation. Selection brackets fill over 160 ms; Reduced UI Motion resolves immediately and Reduced Flash removes pulsing/glow. Keyboard, gamepad, pointer, and touch share focus-retaining, input-neutral transitions. Controller actions persist in `settings.json`; the rebinding page captures a replacement button and swaps conflicts. Defaults are A/Cross for Jump, X/Square for Activate/Use, and D-pad Down for Reload. The Adventure seed editor accepts decimal 1 through 2,147,483,647 by direct typing or a controller/touch keypad.

## Capture and visual verification

`RenderCaptureService` reads the final backbuffer after HUD rendering:

- Shift+F12 queues a PNG still in `artifacts/render-captures`.
- Shift+F11 starts or stops a 15-second, 60 FPS recording and writes `frame-00000.png`-style images under a capture subdirectory. Unmodified F11 opens the Weapon/Arena Lab.
- `FPS_FRENZY_CAPTURE_DIR` overrides the output root.
- `FPS_FRENZY_AUTORECORD_SECONDS`, `FPS_FRENZY_RECORD_FPS`, and `FPS_FRENZY_RECORD_NAME` support deterministic automated capture runs.

`tools/Capture-Motion.ps1` sets the automation variables, runs the desktop project, and invokes ffmpeg to encode the sequence as H.264/yuv420p MP4. It accepts 30 or 60 FPS and 1-to-30-second durations, plus deterministic encounter, Set A/Set B weapon, seed, menu, and optional God Mode controls. Normal and God Mode off remain the defaults. `Capture-ItemLab.ps1` enumerates all 50 bases and includes dual-wield, scoped Precision, and set-swap scenarios. ffmpeg is a development dependency, never a game runtime dependency.

`tools/Capture-CharacterLab.ps1` drives the opt-in `FPS_FRENZY_CHARACTER_LAB` path. The pure `CharacterLabController` schedules each release robot across five authored animation states and three fixed camera distances. Stills freeze the sampled pose while the camera advances; state reels advance from the count of frames actually written, not wall-clock time, so the 30 and 60 FPS variants both represent exactly ten seconds. The lab uses deterministic settings and capture-local profile/checkpoint paths, never the player's files. With no Character Lab environment flag, startup follows the normal game path unchanged.

## Content validation and performance budgets

Definitions and provenance are versioned JSON loaded with `System.Text.Json`. Startup and tests validate IDs and cross-references, all 50 weapon bases, typed behaviors/effects, handedness, scopes/grips, canonical axes and camera-away muzzles, enemy weak points, Adventure recipes/objective order, canonical layout hashes, thousands of generated floors, Orbital Depot visible placement, animation bindings, texture sampling, and collision/navigation. The content processor rejects missing clips or albedo, invalid transforms, and skeletons above 72 bones. The machine-readable asset manifest records source URL, version, license, retained source files, generated runtime names, and orientation/assembly modifications.

Training Ring and its legacy wave schema remain raw repository development fixtures and are not copied into the shipping MGCB catalog. The release Orbital Depot campaign uses sectors, generated encounters, reward progression, and the robot roster exclusively.

Performance targets are fixed 60 Hz simulation, 60 FPS desktop presentation, and 60 FPS Android presentation with the existing 30 FPS fallback. Reach-compatible effects, bounded active enemies, reusable physics/navigation/HUD structures, and event allocation limited to content/state transitions keep the mobile path predictable.

## Future co-op seam

A future authoritative server could receive tick-numbered `PlayerCommand` values and own `GameSimulation.Step`; clients could interpolate stable-ID snapshots. Before networking work begins, all random events would need an injectable source and snapshots would need a protocol version. The current project intentionally contains no networking, accounts, lobbies, cloud saves, analytics, ads, or purchases.
