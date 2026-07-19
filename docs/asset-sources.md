# Asset sources and licenses

Binary source art is tracked by Git LFS. Only files selected for the release roster are stored; asset archives are not committed. Machine-readable provenance is in `Content/ThirdParty/asset-manifest.json`.

## Kenney Blaster Kit 2.1

- Source: https://kenney.nl/assets/blaster-kit
- License: CC0 1.0 Universal
- Downloaded: 2026-07-16
- Selected originals: `blaster-a.fbx`, `blaster-c.fbx`, `blaster-f.fbx`, `blaster-j.fbx`, `blaster-n.fbx`, `blaster-r.fbx`, and the shared `Textures/colormap.png`
- Local root: `Content/Models/Weapons`
- Modification: selected files only; no mesh edits. The shared texture was retained at the relative path expected by the FBX files.
- Intended mapping: Pulse Sidearm (a), Burst Carbine (c), Scatter Blaster (f), Beam Rifle (j), Plasma Launcher (n), Arc Cannon (r).

## Quaternius Ultimate Monsters

- Source: https://quaternius.com/packs/ultimatemonsters.html
- License: CC0 1.0 Universal
- Downloaded: 2026-07-17
- Selected originals: Blob `Alien.fbx`, Flying `Armabee_Evolved.fbx`, Big `Orc.fbx`, Blob `GreenSpikyBlob.fbx`, Big `MushroomKing.fbx`, Big `BlueDemon.fbx`, and `Atlas_Monsters.png`
- Local root: `Content/Models/Enemies`
- Modification: selected files only; no mesh, rig, animation, or texture edits.
- Roster mapping: Alien (grunt), Armabee Evolved (skirmisher), Orc (brute), Green Spiky Blob (spitter), Mushroom King (warden), and Blue Demon (boss).
- Animation stacks are validated per model by the content processor and mapped to the shared idle/walk/attack/hit/death runtime aliases.

## Kenney Survival Kit 2.0

- Source: https://kenney.nl/assets/survival-kit
- License: CC0 1.0 Universal
- Downloaded: 2026-07-17
- Selected originals: `box-large.fbx`, `barrel.fbx`, and `Textures/colormap.png`
- Local root: `Content/Models/Pickups`
- Modification: selected files only; the box and barrel were renamed to `health-crate.fbx` and `ammo-cache.fbx` to document their gameplay roles.

## Kenney Modular Space Kit 1.0

- Source: https://kenney.nl/assets/modular-space-kit
- License: CC0 1.0 Universal
- Downloaded: 2026-07-17
- Selected originals: `gate.fbx`, `gate-door-window.fbx`, `template-wall-detail-a.fbx`, `cables.fbx`, `stairs-wide.fbx`, and `Textures/colormap.png`
- Local root: `Content/Models/Arenas/OrbitalDepot`
- Modification: selected files only; the models are non-colliding visual dressing placed over the existing authored collision primitives.

## Kenney Space Station Kit 1.0

- Source: https://kenney.nl/assets/space-station-kit
- License: CC0 1.0 Universal
- Downloaded: 2026-07-18
- Selected originals: balcony floor/rail, computer, container, display, floor-detail, pipe, rail, barrier, command-table, door, pillar, and the shared `Textures/colormap.png` files listed in the machine-readable manifest
- Local root: `Content/Models/Arenas/OrbitalDepot/Station`
- Modification: selected files only; all modules are data-authored decorative props with separate project-authored collision primitives.

## Kenney Space Kit 2.0

- Source: https://kenney.nl/assets/space-kit
- License: CC0 1.0 Universal
- Downloaded: 2026-07-18
- Selected originals: `craft_cargoA.fbx`, `machine_generatorLarge.fbx`, `machine_wirelessCable.fbx`, and `satelliteDish_detailed.fbx`
- Local root: `Content/Models/Arenas/OrbitalDepot/SpaceKit`
- Modification: selected files only; stock FBX material colors were retained. The cargo craft, generator, wireless relay, and dish are non-colliding elevated silhouette and landmark dressing.
- Version note: the downloaded archive's included `License.txt` identifies Space Kit 2.0 (created 2020-08-27), although the current asset landing page displays 1.0.

## Kenney Prototype Textures 1.0

- Source: https://kenney.nl/assets/prototype-textures
- License: CC0 1.0 Universal
- Downloaded: 2026-07-18
- Selected originals: `PNG/Dark/texture_03.png`, `PNG/Dark/texture_05.png`, and `PNG/Dark/texture_09.png`
- Local root: `Content/Textures/Arenas/OrbitalDepot`
- Modification: renamed to `floor-grid.png`, `wall-diagonal.png`, and `platform-markers.png`; no pixel edits.

## Kenney UI Pack - Sci-Fi 2.0

- Source: https://kenney.nl/assets/ui-pack-sci-fi
- License: CC0 1.0 Universal
- Downloaded: 2026-07-18
- Selected originals: blue crosshair emblem and blue/green notched rectangular button panels listed in the machine-readable manifest
- Local root: `Content/Textures/UI`
- Modification: renamed to `menu-emblem.png`, `menu-button.png`, and `menu-button-selected.png`; runtime tinting ties the interface to the arena sector palette.

## Arena construction

Orbital Depot uses project-authored JSON for collision, navigation, tiled material assignment, and non-colliding prop placement. Training Ring remains primitive-only. Third-party packs supply selected art assets, not a prebuilt arena map.

## Code sample attribution

The build-time skinned model processor and runtime animation data/player are adapted for KNI/.NET 10 from the Microsoft XNA Skinned Model sample distributed in the KNI XNAGameStudio repository:

- https://github.com/kniEngine/XNAGameStudio/tree/main/Samples/Skinned-Model
- License: Microsoft Permissive License / MIT-compatible sample distribution as documented by KNI.
