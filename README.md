# FPS Frenzy

FPS Frenzy is an offline, single-player robot arena roguelite built with C# 14, .NET 10, and KNI (the MonoGame/XNA-compatible fork). It does not use Unity.

Each Standard run sends the player through three seeded sectors of the 72-by-56-meter Orbital Depot. Every sector contains a Purge, Relay Defense, and Elite Hunt encounter. Combat closes the sector gates, robot reinforcements arrive through telegraphed portals, and each completed encounter pauses for one of three non-stacking upgrade choices. After nine encounters, the run returns to the central arena for the three-phase Breach Walker fight.

The player keeps the classic 7.5 m/s run-and-jump moveset and can carry all six weapons. A chosen starting weapon opens the run; a seeded central armory offer appears after each of the first five encounters, allowing the full arsenal to be assembled during the run. Collected weapons become future loadout options, while challenge unlocks add new upgrades to the offer pool without granting permanent health, damage, or defense.

The release enemy faction uses textured Quaternius robots: Striker, Interceptor, Juggernaut, Wasp, Warden, and the Breach Walker boss. Their authored albedo textures, calibrated placement, telegraphed attacks, state-driven animation, hit reactions, held death poses, emissive accents, and camera-facing health bars replace the former flat-tinted monster presentation. Authored CC0 sound effects and music replace generated tones.

## Requirements

- .NET SDK 10.0.203, selected by `global.json`
- Git LFS for binary models, textures, and audio
- Android workload for Android builds: `dotnet workload install android`
- Optional: `ffmpeg` on `PATH` for MP4 motion captures

## Build and run

From the repository root:

```powershell
dotnet restore FPSFrenzy.slnx
dotnet build FPSFrenzy.slnx -c Debug
dotnet test FPSFrenzy.slnx -c Debug
dotnet run --project src/FpsFrenzy.Desktop/FpsFrenzy.Desktop.csproj
```

Build an Android debug APK with:

```powershell
dotnet build src/FpsFrenzy.Android/FpsFrenzy.Android.csproj -c Debug
```

Desktop uses SDL2/OpenGL. Android uses OpenGL ES, landscape touch controls, and optional gyro fine aim.

## Gameplay loop

The main menu provides Continue Run when a checkpoint exists, Start New Run, Loadout, Records, Settings, Accessibility, and Quit. A first-run card explains the controls and objectives.

The complete loop is:

```text
Main Menu -> Loadout -> New/Continue Run -> 3 sectors / 9 encounters
          -> 9 upgrade rewards -> Breach Walker -> Results -> Retry/Menu
```

Checkpoints are written only after an upgrade is selected, so Continue reconstructs the next encounter from the saved run seed. Victory, defeat, or abandoning the run clears the checkpoint. Results record the seed, time, score, kills, damage taken, sectors completed, upgrades, unlocks, and whether God Mode was used.

## Controls

- Move: WASD, left stick, or Android left touch stick
- Look: mouse, right stick, or Android right look region; optional Android gyro adds fine aim
- Fire: left mouse, right trigger, or FIRE touch button
- ADS: hold right mouse/left trigger; Android ADS toggles
- Reload: R, gamepad X, or reload touch button
- Jump: Space, gamepad A, or JUMP touch button
- Weapons: number keys, mouse wheel, gamepad shoulders, or PREV/NEXT touch buttons
- Menus: arrow keys/D-pad or pointer/touch; Enter/Space/A selects; Escape/Back/B returns
- Pause: Escape/Back, gamepad Start, or the Android PAUSE button
- Settings: F2
- Accessibility: F3
- God Mode: toggle in Settings; the HUD displays a badge while enabled
- Debug test sandbox: F1 toggles the diagnostic HUD without writing records or checkpoints
- Debug collision view: F4 while the debug sandbox is enabled
- Debug stage controls: F5 restarts, F6/F7 move between stages, F8 toggles test invulnerability, and F9 completes the current encounter, reward, or boss
- Still capture: F12
- Motion-frame capture: F11 starts or stops a 15-second, 60 FPS PNG sequence
- Exit desktop: F10

God Mode defaults off and affects only incoming player damage. It does not change enemy AI, weapons, objectives, pickups, score, challenge unlocks, or starting-weapon unlocks. Enabling it at any point marks that run's results, and God Mode runs do not replace the best unassisted record.

For automated developer runs, set `FPS_FRENZY_DEBUG=1`. Optional `FPS_FRENZY_DEBUG_COLLISION=1` and `FPS_FRENZY_DEBUG_GOD_MODE=1` start with those debug overlays enabled. Debug runs are sandboxed from profile records, unlocks, and checkpoint writes.

## Local saves

Settings, profile progression, and the active checkpoint are separate versioned files in the platform's local application-data `FPSFrenzy` directory:

- `settings.json` stores controls, graphics, accessibility, Master/Music/SFX volumes, and God Mode.
- `profile-v1.json` stores starting-weapon and upgrade unlocks, challenge progress, tutorial state, lifetime totals, and run records.
- `run-checkpoint-v2.json` stores the seed, onboarding-order marker, next encounter, cumulative run/player state, owned upgrades, collected weapons, selected weapon, armory state, and God Mode marker.

Profile and checkpoint writes use a temporary file followed by an atomic replacement. Missing, corrupt, or unsupported versions fall back safely without overwriting settings.

## Render and motion captures

F12 writes the final backbuffer to `artifacts/render-captures`. F11 records numbered PNG frames under a named subdirectory in the same location; it does not require ffmpeg at runtime.

For an automated recording and MP4 encode, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Capture-Motion.ps1 `
  -Name robot-arena-reel -FramesPerSecond 60 -Seconds 10
```

The helper launches the desktop game in capture mode, writes the frame sequence, and invokes ffmpeg to create `artifacts/render-captures/robot-arena-reel.mp4`. `-FramesPerSecond` accepts 30 or 60, `-Seconds` accepts 1 through 30, and `-StartingEncounter`, `-StartingWeapon`, and `-RunSeed` make the scenario repeatable. Captures use Standard with God Mode off by default; `-GodMode` is available for uninterrupted presentation reels and remains visibly marked in any results screen. ffmpeg is a development dependency only.

The deterministic Character Lab captures the six schema-v2 robots independently of gameplay saves and settings. This command writes 15 stills per robot (Idle, Locomotion, Attack, Hit, and Death at near, medium, and far distance), then exact ten-second 30 and 60 FPS state reels:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Capture-CharacterLab.ps1
```

Use `-Enemy robot-striker`, `-Mode Stills`, `-Mode Reels`, or `-FrameRates 60` to narrow a run. Character Lab always renders at 1280x720; its numbered frames remain available beside the ffmpeg-encoded MP4 files under `artifacts/character-lab`.

See [architecture.md](docs/architecture.md) for system boundaries and [asset-sources.md](docs/asset-sources.md) for asset provenance and licenses.
