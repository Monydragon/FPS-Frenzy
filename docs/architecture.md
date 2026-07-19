# FPS Frenzy architecture

## Platform and dependency baseline

FPS Frenzy targets desktop (Windows, Linux, macOS) and Android only. The repository pins .NET SDK 10.0.203, C# 14, KNI 4.2.9001/4.2.9001.1, and BepuPhysics 2.4.0. Desktop runs through KNI SDL2/OpenGL. Android runs through KNI OpenGL ES with a minimum API of 23, target API 36, application ID `com.kni.fpsfrenzy`, and ARM64 release output.

All projects enable nullable reference types, deterministic builds, recommended analyzers, and warnings as errors. Package versions are managed centrally in `Directory.Packages.props`.

## Project boundaries

- `FpsFrenzy.Core` owns simulation state, stable entity IDs, ticked player commands, Bepu physics, six weapon behaviors, swept projectiles, damage, pickups, A* navigation, enemy and boss AI, wave rules, and scoring. It uses `System.Numerics` and has no KNI dependency.
- `FpsFrenzy.Kni` owns rendering, XNA/KNI type conversion, model animation playback, combat audio/feedback, the main/pause/settings/accessibility/results flow, settings persistence, content loading, HUD presentation, keyboard/mouse/gamepad/touch mapping, render capture, and the variable-rate game loop.
- `FpsFrenzy.Desktop` is the SDL2/OpenGL entry point.
- `FpsFrenzy.Android` is the Android activity plus touch/sensor integration. Gyro is optional and never required for installation.
- `FpsFrenzy.Content.Pipeline` is a .NET Standard 2.0 build-time extension adapted from the MIT-licensed KNI/XNA Skinned Model sample. It validates required alien clips and the 72-bone Reach/mobile limit, emits `SkinnedEffect` content, and stores animation data in the model XNB.
- `FpsFrenzy.Core.Tests` covers simulation and game rules. `FpsFrenzy.Content.Tests` covers JSON, asset provenance, release wave shape, animation aliases, compass projection, ADS, reticle, and safe-area math.

## Frame and simulation flow

The application opens on a paused main-menu presentation. Starting or replaying creates a fresh Standard run; victory and defeat open a results screen with replay, main-menu, and quit paths. Losing focus pauses active gameplay. Menu transitions suppress held activation input until it returns to neutral, preventing a click, Space, or gamepad A press from leaking into combat.

KNI samples a platform-neutral `PlayerCommand`, identified by simulation tick and player `EntityId`. Rendering may run at 30 or 60 FPS, while an accumulator advances `GameSimulation.Step` at a fixed 60 Hz. If multiple simulation ticks are needed, accumulated look input is divided over those ticks. Previous/current transforms are retained for interpolated rendering.

The update order is:

1. Pause and command edge handling.
2. Bepu capsule movement and grounded jump.
3. Weapon resource, reload, heat/energy, and shot processing.
4. Wave spawning and completion.
5. A* path refresh, separation, safe collision sliding, behavior-specific movement, melee/ranged/support attacks, and boss phases.
6. Projectile movement and damage.
7. Pickup collection/respawn and enemy death cleanup.
8. Defeat/result state.

The command and simulation boundaries are suitable for a future authoritative server. No deterministic-lockstep guarantee or networking package is present.

## Physics and navigation

Orbital Depot JSON generates a 72-by-56-meter production floor, bulkheads, low cover, cargo, ramps, sector lighting, tiled material assignments, overhead dock silhouettes, and non-colliding visual props. Collision, visibility, and navigation flags are independent, so decorative dressing never creates invisible walls and model-shaped cover retains simple predictable collision. The player is an upright 1.8 m Bepu capsule with a 7.5 m/s target run speed, moderate air acceleration, and approximately 1.2 m jump height. A 0.75 m grid supports four-direction A*. Player/enemy positions are clamped to arena bounds, enemy collision uses safe slide resolution and separation, and projectiles use swept collision to prevent tunneling.

## Content and validation

Definitions are versioned JSON loaded with `System.Text.Json`. Startup and tests validate IDs, cross-references, weapon modes and effective output bands, enemy health/pressure bands, descending boss thresholds, summon references, pickup amount/respawn bands, arena bounds, spawn/pickup positions, animation aliases, Standard wave shape, and navigation settings. Orbital Depot supplies ten authored mixed waves plus a Big Alien boss wave and placed access to all six weapons. Training Ring remains a short development arena. Weapon pads apply their authored refill amounts and stay available when the selected resource is already full.

God Mode is a persisted presentation setting that routes to `GameSimulation.SetPlayerInvulnerable`. It defaults off, blocks only incoming player damage, never heals the player, and leaves weapons, AI, pickups, wave progression, score, and difficulty data unchanged.

KNI MGCB copies JSON and provenance metadata, builds all six selected Kenney blasters, pickup and pedestal models, Space Station/Space Kit arena dressing, and Sci-Fi UI textures through the standard processors, and builds all six Quaternius monsters through the custom skinned processor. The processor repairs the source FBX armature's local scale/translation unit conversion before emitting animation matrices, avoiding displaced bones and exploding geometry. The runtime fails fast if skinning metadata or a requested animation clip is missing.

## Rendering and HUD

The renderer uses the Reach profile, `BasicEffect`, `SkinnedEffect`, shared key/fill directional lighting, authored arena fog, shared model atlases, and world-scale tiled arena textures. Arena primitives, data-driven station props, pickups, projectiles, trimmed tracers, impact sparks, and animated/tinted enemies render in the world pass. Enemy role tints multiply their authored monster atlas instead of replacing it, preserving material detail while the existing ground and compass markers maintain combat visibility. The active weapon renders in a second pass after clearing depth, preventing wall clipping. Hip and ADS FOVs interpolate between the data-defined defaults; camera bob, recoil, damage shake, flash intensity, HUD scale, reticle contrast, and color-vision mapping respect persisted settings.

The safe-area HUD shows health, weapon/resource ammo, wave/enemy count, score, boss phase/health, reticle/hit confirmation, subtitles, and a compass. Every living spawned enemy is projected regardless of distance or visibility. Behind markers pin to compass edges, height differences receive chevrons, and overlapping bearings cluster with counts. Numeric glyph formatting and compass bins reuse stack/fixed storage to avoid managed allocation in steady HUD rendering. F12 captures the final backbuffer to PNG for presentation regression checks.

## Performance budgets

- Simulation: fixed 60 Hz.
- Desktop presentation: 60 FPS.
- Android presentation: 60 FPS with an opt-in 30 FPS fallback.
- Graphics profile: Reach/mobile-friendly effects and geometry.
- Hot paths: reusable physics/navigation/HUD structures; gameplay-event allocations remain acceptable for spawning and content/state transitions.

## Future co-op seam

A future authoritative server can receive tick-numbered `PlayerCommand` values and own `GameSimulation.Step`. Clients can interpolate entity snapshots using stable IDs. Before networking work begins, random events must move behind an injectable random source and state snapshots need an explicit protocol/version. This milestone intentionally contains no accounts, lobbies, cloud saves, analytics, ads, or purchases.
