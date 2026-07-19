# FPS Frenzy

FPS Frenzy is an offline-first, colorful low-poly arena PvM FPS built with C# 14, .NET 10, and KNI 4.2.9001. It does not use Unity.

Milestone M1 makes the enlarged Orbital Depot Arena the first production map: ten mixed waves followed by the three-phase Big Alien boss. A main menu, pause flow, results screen, replay path, and persistent settings wrap the complete Standard run. Its four color-coded sectors, wide combat lanes, tiled materials, modular station dressing, elevated dock machinery, tactical displays, and expanded health/ammo coverage emphasize readable arena combat. The run also includes six distinct weapons, five regular alien AI roles, procedural combat audio, muzzle/impact/damage feedback, an animated Quaternius enemy roster, and a safe-area HUD with boss health, reticle, subtitles, and enemy compass. Optional God Mode is off by default and only prevents incoming player damage; it does not alter enemy AI, weapons, pickups, waves, score, or progression. Desktop uses SDL2/OpenGL; Android uses OpenGL ES and landscape touch controls with optional gyro fine aim.

## Requirements

- .NET SDK 10.0.203 (selected by `global.json`)
- Android workload for Android builds: `dotnet workload install android`
- Git LFS for binary art sources

## Build and run

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
- Render capture: F12 (writes PNGs to `artifacts/render-captures`)
- Exit desktop: F10

See [architecture.md](docs/architecture.md) for system boundaries and [asset-sources.md](docs/asset-sources.md) for asset provenance.
