# FPS Frenzy

FPS Frenzy is an offline, single-player robot action roguelite with Arena and Adventure modes, built with C# 14, .NET 10, and KNI (the MonoGame/XNA-compatible fork). It does not use Unity.

Each run sends the player through three seeded sectors of the continuously accessible 72-by-56-meter Orbital Depot. Every sector contains a Purge, Relay Defense, and Elite Hunt encounter. The sectors provide visual identity and objective direction without invisible barriers: players and enemies can cross the complete unobstructed floor during every encounter and the boss fight. Robot reinforcements arrive through telegraphed portals, and each completed encounter pauses for one of three non-stacking upgrade choices. After nine encounters, the run returns to the central arena for the three-phase Breach Walker fight.

Adventure Mode, **The Null Signal**, generates three deterministic derelict-station floors from a player-visible decimal seed. Maintenance Deck, Fabrication Ring, and Signal Core use exploration objectives, story transmissions, patrols, energy gates, floor-specific hazards, two or three seeded loot chests, persistent lore, secrets, and one three-choice boon per floor before the shield-locked Core Warden fight. Chests guarantee weapons from missing quickbar families before applying an elevated general weapon chance, encouraging experimentation throughout a run. The same seed, generator version, and floor always reconstruct the same room graph, geometry, roles, objectives, dressing, and enemy roster.

The player starts with a Pulse Sidearm pistol and keeps the classic 7.5 m/s run-and-jump moveset while using slot-authentic persistent equipment: armor, two accessories, two rings, and two swappable weapon sets with independent right/left hands. One-handed weapons can be dual-wielded and two-handed weapons reserve both hands. All 50 behavioral weapon bases across ten proficiency families are available as Common armory issues on a fresh profile, including scoped Precision rifles, SMGs, Heavy weapons, and Experimental weapons. Item Power, rarity, affixes, talents, learned equipment abilities, and ten selectable threat tiers provide persistent RPG progression. Casual through Extreme tune combat behavior and supply scarcity; harder difficulties add stronger rarity luck. The nine three-choice boons remain temporary run modifiers.

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

The industrial-holographic main menu presents original orbital-station key art and groups Play, Operative, Arsenal, Records, Settings, and Quit. Play exposes independent Continue/New actions for Adventure and Arena plus Debug Lab; Records has separate tabs for both modes. Operative contains Character, Abilities, Proficiencies, and Stats, while Arsenal contains Inventory, Loadout, Crafting, and Armory. Crowded pages use clipped seven-row viewports with scroll rails, mouse-wheel and touch-drag scrolling, clickable arrows, and automatic controller focus tracking. The quickbar has one canonical slot for each family: Pulse, SMG, Burst, Scatter, Precision, Beam, Plasma, Arc, Heavy, and Experimental. Missing families auto-fill from ground drops without forcing a switch; competing drops pause for Replace, Dismantle, or Leave. Debug Lab is a non-persistent, invulnerable arena sandbox with all ten family slots populated, every release enemy, and one equipment drop of each rarity; J/K or `[`/`]` cycles all 50 weapons without restarting the arena. Inventory/loadout changes and free talent respecs are between-run or recovery-hub actions. A first-run card explains the controls and objectives.

The complete loop is:

```text
Arena:     Main Menu -> Operative/Arsenal -> New/Continue Arena
          -> encounter -> recovery loot/cache -> boon/checkpoint (x9)
          -> Breach Walker -> Results/unlocks -> Retry/Menu

Adventure: Main Menu -> seed setup/New/Continue Adventure
          -> explore/objectives/chests -> exit lift -> boon/commit/checkpoint (x3)
          -> Core Warden -> Results/lore -> Retry/Menu
```

Arena checkpoints are written after recovery and boon selection. Adventure checkpoints are written at run entry and only after each floor boon; defeat or quitting mid-floor restores that stage entrance and discards uncommitted floor rewards. Results include mode, difficulty and threat tier, seed, generator version/hash where applicable, floor/sector progress, both starting weapon sets, progression deltas, loot, and the God Mode marker.

## Controls

- Move: WASD, left stick, or Android left touch stick
- Look: mouse, right stick, or Android right look region; optional Android gyro adds fine aim
- Fire Right: left mouse, right trigger, or right FIRE touch button
- Fire Left: right mouse, left trigger, or left FIRE touch button
- ADS/focus: Shift, gamepad LT for a single weapon, gamepad RS while dual-wielding, or the Android ADS toggle
- Activate/use/loot: E, Xbox X / PlayStation Square, or the context touch prompt
- Abilities: Q/F, gamepad LB/RB, or the two ability touch buttons
- Reload both eligible hands: R, gamepad D-pad Down, or reload touch button; either hand may keep firing while the other reloads
- Select weapon slot: 1-0 or mouse wheel; gamepad Y/B or D-pad Right/Left cycles next/previous; touch has next/previous controls
- Jump: Space, gamepad A / PlayStation Cross, or JUMP touch button
- Controller rebinding: Settings -> Controller Bindings; select an action and press a new button. Conflicts swap automatically, and bindings persist in `settings.json`.
- Menus: arrow keys/D-pad or pointer/touch; Enter/Space/A selects; Escape/Back/B returns
- Pause: Escape/Back, gamepad Start, or the Android PAUSE button
- God Mode: toggle in Settings; the HUD displays a badge while enabled
- Debug test sandbox: F1 toggles the diagnostic HUD without writing records or checkpoints
- Debug RPG grant: F2 while the debug sandbox is enabled
- Debug rarity/loot showcase: F3 while the debug sandbox is enabled
- Debug collision view: F4 while the debug sandbox is enabled
- Debug stage controls: F5 restarts, F6/F7 move between stages, F8 toggles test invulnerability, and F9 completes the current encounter, reward, or boss
- Weapon/Arena Lab: select Debug Lab from the main menu or press F11 during a run; gamepad Y/B cycles all weapons, LB/RB activates the equipped test abilities, D-pad Up/Down cycles the LB/RB ability slots, and D-pad Right/Left spawns an enemy/freezes AI. Keyboard uses J/K or `[`/`]` for weapons, `-`/`+` for difficulty, Page Down/Page Up for Threat Tier, I to spawn, O to freeze AI, T to teleport sectors, and F12 to hot-reload numeric weapon JSON
- Still capture: Shift+F12
- Motion-frame capture: Shift+F11 starts or stops a 15-second, 60 FPS PNG sequence
- Exit desktop: F10

God Mode defaults off and affects only incoming player damage. It does not change enemy AI, weapons, objectives, pickups, score, challenge unlocks, or starting-weapon unlocks. Enabling it at any point marks that run's results, and God Mode runs do not replace the best unassisted record.

For automated developer runs, set `FPS_FRENZY_DEBUG=1`. Optional `FPS_FRENZY_DEBUG_COLLISION=1` and `FPS_FRENZY_DEBUG_GOD_MODE=1` start with those debug overlays enabled. Debug runs are sandboxed from profile records, unlocks, and checkpoint writes.

## Local saves

Settings, shared profile progression, and the independent mode checkpoints are separate versioned files in the platform's local application-data `FPSFrenzy` directory:

- `settings.json` stores controls, graphics, accessibility, Master/Music/SFX volumes, and God Mode.
- `profile-v2.json` (schema 6) and its generation-matched `.stash` store shared level/XP, talents, salvage, proficiency, ability mastery/loadout, difficulty, both starter weapon sets, threat unlocks, equipped item IDs, unlimited stash, discovered lore, completion state, and separate Arena/Adventure records.
- `run-checkpoint-v4.json` stores deterministic encounter state, difficulty, both weapon sets and independent states, issued items, pending progression and loot, recovery cache, ability cooldowns, drop serial, and run-result deltas.
- `adventure-checkpoint-v1.json` stores Adventure ID, seed, generator version/hash, next stage, story position, boons, stats, loadout/weapon state, and committed floor rewards; live enemies, gates, and visited-map state are reconstructed at stage entry.

Profile and stash writes use matching atomic generations and paired backups. A mismatch, corruption, or unsupported version loads the newest matching backup without overwriting settings. Schema-v1 profiles migrate to starter Scout armor and a Common Item Power 1 Pulse Sidearm; schema-v5 records migrate as Arena records.

## Render and motion captures

Shift+F12 writes the final backbuffer to `artifacts/render-captures`. Shift+F11 records numbered PNG frames under a named subdirectory in the same location; it does not require ffmpeg at runtime.

For an automated recording and MP4 encode, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Capture-Motion.ps1 `
  -Name robot-arena-reel -FramesPerSecond 60 -Seconds 10
```

The helper launches the desktop game in capture mode, writes the frame sequence, and invokes ffmpeg to create `artifacts/render-captures/robot-arena-reel.mp4`. `-FramesPerSecond` accepts 30 or 60; `-StartingEncounter`, `-StartingWeapon`, `-LeftWeapon`, `-SetBWeapon`, `-ThreatTier`, and `-RunSeed` make weapon, set-swap, and threat scenarios repeatable. `-AimDownSights` records the family focus/scope presentation and `-DebugLab` records the populated sandbox. Captures use Normal with God Mode off by default; `-GodMode` is available for uninterrupted presentation reels. ffmpeg is a development dependency only.

The deterministic Character Lab captures the six schema-v2 robots independently of gameplay saves and settings. This command writes 15 stills per robot (Idle, Locomotion, Attack, Hit, and Death at near, medium, and far distance), then exact ten-second 30 and 60 FPS state reels:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Capture-CharacterLab.ps1
```

Use `-Enemy robot-striker`, `-Mode Stills`, `-Mode Reels`, or `-FrameRates 60` to narrow a run. Character Lab always renders at 1280x720; its numbered frames remain available beside the ffmpeg-encoded MP4 files under `artifacts/character-lab`.

The Item Lab runner covers all 50 weapon bases, a representative dual-wield pair, two-handed handling, automatic Set A-to-Set B footage, scoped Precision ADS/reload presentation, still extraction, 30/60 FPS reels, and optional Tier I-X boundary captures:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Capture-ItemLab.ps1 `
  -Mode All -FramesPerSecond 60 -CaptureTierBoundaries
```

Use `-Weapon pulse-sidearm,gravity-lobber`, `-DualWieldPair pulse-sidearm:nova-pistol`, or `-Mode Stills` for a smaller deterministic capture set. Output is written under `artifacts/item-lab`.

Weapon bases and archetypes are JSON-driven. To validate that every declared model exists and is registered with the KNI content pipeline, run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Sync-WeaponContent.ps1
```

Pass `-Update` to register newly declared FBX model assets automatically. Numeric JSON changes can be reloaded from the desktop F11 lab; new or changed models still require a content rebuild.

See [architecture.md](docs/architecture.md) for system boundaries and [asset-sources.md](docs/asset-sources.md) for asset provenance and licenses.
