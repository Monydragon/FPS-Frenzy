# Asset sources and licenses

Binary source art is tracked by Git LFS. Only files selected for the release roster are stored; asset archives are not committed. Machine-readable provenance is in `Content/ThirdParty/asset-manifest.json`.

## Kenney Blaster Kit 2.1

- Source: https://kenney.nl/assets/blaster-kit
- License: CC0 1.0 Universal
- Downloaded: 2026-07-19
- Selected assembled originals: `blaster-a.fbx` through `blaster-r.fbx`
- Retained modular sources: `clip-large.fbx`, `clip-small.fbx`, `grenade-a.fbx`, `grenade-b.fbx`, `scope-large-a.fbx`, `scope-large-b.fbx`, `scope-small.fbx`, `silencer-larger.fbx`, and `silencer-small.fbx`
- Canonical source root: `Content/ThirdParty/Kenney-Blaster-Kit-2.1/FBX`
- Local root: `Content/Models/Weapons`
- Modification: all 18 assembled blasters are optimized runtime models; nine modular parts are retained as provenance/source material but are not composed at runtime. JSON supplies canonical orientation, target span, muzzle/grip anchors, hip/ADS placement, color treatment, and procedural animation calibration. Stock meshes and material colors are unchanged.

## Quaternius Sci-Fi Modular Gun Pack

- Source: https://quaternius.com/packs/scifimodularguns.html
- License: CC0 1.0 Universal
- Pack release: November 2021
- Downloaded: 2026-07-19 from the publisher's public Google Drive folder
- Selected originals: all 20 assembled FBX files—six assault rifles, two crossbows, five grenade/projector forms, three pistols, two SMGs, and three sniper silhouettes
- Canonical source root: `Content/ThirdParty/Quaternius-Sci-Fi-Modular-Gun-Pack/FBX`
- Runtime root: `Content/Models/Weapons/Quaternius`
- Modification: selected assembled models only; meshes and materials are unchanged. The 50 JSON weapon bases share the 38 retained runtime silhouettes where appropriate and author behavioral sidegrades independently of visual geometry.

## Quaternius Animated Robot Pack

- Source: https://quaternius.com/packs/animatedrobot.html
- License: CC0 1.0 Universal
- Pack release: October 2018
- Downloaded: 2026-07-19 from the publisher's public Google Drive folder
- Selected original: `Robot.fbx`
- Canonical source root: `Content/ThirdParty/Quaternius-Animated-Robot-Pack`
- Runtime root: `Content/Models/Player`
- Modification: the project conversion script creates the static loadout mannequin used by Character/Loadout presentation; the retained publisher source and generated output names are listed in the machine-readable manifest.

## Quaternius Ultimate Monsters

- Source: https://quaternius.com/packs/ultimatemonsters.html
- License: CC0 1.0 Universal
- Downloaded: 2026-07-17
- Selected originals: Blob `Alien.fbx`, Flying `Armabee_Evolved.fbx`, Big `Orc.fbx`, Blob `GreenSpikyBlob.fbx`, Big `MushroomKing.fbx`, Big `BlueDemon.fbx`, and `Atlas_Monsters.png`
- Local root: `Content/Models/Enemies`
- Modification: selected files only; no mesh, rig, animation, or texture edits.
- Roster mapping: Alien (grunt), Armabee Evolved (skirmisher), Orc (brute), Green Spiky Blob (spitter), Mushroom King (warden), and Blue Demon (boss).
- Animation stacks are validated per model by the content processor and mapped to the shared idle/walk/attack/hit/death runtime aliases.
- Migration note: these sources remain in the repository for the raw Training Ring development fixture and rollback reference. After the robot roster passed Character Lab and integrated capture gates, the monster definitions, models, and legacy wave data were removed from the shipping MGCB catalog.

## Quaternius Animated Mech Pack

- Source: https://quaternius.com/packs/animatedmech.html
- License: CC0 1.0 Universal
- Pack release: March 2021
- Downloaded: 2026-07-19 from the publisher's public Google Drive files linked by the source page
- Selected originals: the self-contained official `George.gltf`, `Leela.gltf`, `Mike.gltf`, and `Stan.gltf`, plus each model's default `_Texture.png` albedo
- Retained canonical sources: `Content/ThirdParty/Quaternius-Animated-Mech-Pack/CanonicalGLTF`; the exact publisher file URLs are recorded in the machine-readable manifest.
- Local root: `Content/Models/Enemies/Robots`
- Production conversion: from PowerShell, run `./tools/Convert-AnimatedMechModels.ps1 -SourceDirectory Content/ThirdParty/Quaternius-Animated-Mech-Pack/CanonicalGLTF -DestinationDirectory Content/Models/Enemies/Robots`. The script uses AssimpNet 5.0.0 from KNI content-builder 4.2.9001 with `PostProcessSteps.None` to write `.assbin` files.
- Modification: an identity `RobotArmature` parent joins the canonical glTF's separate Body/Foot roots into the single skeleton required by KNI. One `0.000001` non-rendering root influence is added while preserving the vertex's total weight. The non-content Assimp export timestamp is fixed so repeated conversions produce stable derived binaries. Meshes, UVs, authored animation TRS tracks, and albedo pixels are otherwise unchanged; external albedos are bound explicitly. Resulting skins contain George 48, Leela 18, Mike 44, and Stan 44 bones.
- Roster mapping: Leela (Striker), Stan (Interceptor), George (Juggernaut), and Mike (Warden).

## Quaternius Sci-Fi Essentials Kit - Standard

- Source: https://quaternius.com/packs/scifiessentialskit.html
- Download source: https://quaternius.itch.io/sci-fi-essentials-kit
- License: CC0 1.0 Universal
- Version: Standard free edition dated 2024-11-17 by itch.io
- Downloaded: 2026-07-19
- Selected originals: the official `glTF/Enemy_EyeDrone.gltf` and `glTF/Enemy_Trilobite.gltf`, their external `.bin` buffers and six referenced base-color/normal/ORM images, plus the pack's regular/large emissive masks
- Retained canonical sources: `Content/ThirdParty/Quaternius-Sci-Fi-Essentials-Standard/CanonicalGLTF`; all ten glTF dependencies are byte-for-byte copies of the publisher files.
- Local root: `Content/Models/Enemies/Robots`
- Production conversion: from PowerShell, run `./tools/Convert-AnimatedMechModels.ps1 -SourceDirectory Content/ThirdParty/Quaternius-Sci-Fi-Essentials-Standard/CanonicalGLTF -DestinationDirectory Content/Models/Enemies/Robots -Models Enemy_EyeDrone,Enemy_Trilobite`. AssimpNet 5.0.0 imports and exports with `PostProcessSteps.None`.
- Modification: an identity `RobotArmature` parent wraps the canonical `Root` skeleton branch without changing any authored local transforms. One `0.000001` non-rendering `RobotArmature` influence is added to each mesh while preserving the affected vertex's total weight, allowing KNI to identify one common skeleton root. The converter fixes Assimp's non-content export timestamp and rejects any numerical change to animation TRS key times or values. Runtime processing disables imported-basis normalization, performs no per-keyframe/source-unit rewrite, and explicitly binds repository-relative albedos. Resulting skins contain Eye Drone 9 and Trilobite 21 bones.
- Roster mapping: Eye Drone (Wasp) and Trilobite (Breach Walker boss).

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

## FPS Frenzy title-menu key art

- Source: OpenAI built-in image generation, generated 2026-07-21 for this project without source-image inputs
- Local file: `Content/Textures/UI/title-menu-background.png`
- Use: text-free title-menu background; the localized Oxanium title and interactive menu remain runtime-rendered UI
- Prompt direction: a wide industrial orbital depot and mysterious null-signal core, viewed behind an original robot operative with a lowered pulse pistol; dark crop-safe title space; restrained cyan, amber, and coral lighting; no text, logos, UI, or watermark
- Modification: imported unchanged; the renderer aspect-crops it to the platform safe area and applies a dark readability veil

## Kenney Sci-Fi Sounds 1.0

- Source: https://kenney.nl/assets/sci-fi-sounds
- License: CC0 1.0 Universal
- Downloaded: 2026-07-18
- Selected originals: twelve door, explosion, force-field, impact, laser, and low-frequency explosion OGG files listed in the machine-readable manifest
- Local root: `Content/Audio/Sfx`
- Modification: renamed to stable gameplay cue IDs for gates, reload, robot destruction, support, portals, impacts, plasma/scatter/enemy fire, player damage, and boss warnings; encoded audio is unchanged.

## Kenney Digital Audio 1.0

- Source: https://kenney.nl/assets/digital-audio
- License: CC0 1.0 Universal
- Downloaded: 2026-07-18
- Selected originals: `laser1.ogg`, `laser5.ogg`, `laser9.ogg`, `phaserUp6.ogg`, `powerUp7.ogg`, `powerUp11.ogg`, `threeTone2.ogg`, and `zapThreeToneUp.ogg`
- Local root: `Content/Audio/Sfx`
- Modification: renamed to the Pulse Sidearm, Burst Carbine, Beam Rifle, charge, pickup, upgrade, wave, and Arc Cannon cue IDs; encoded audio is unchanged.

## Kenney UI Audio 1.0

- Source: https://kenney.nl/assets/ui-audio
- License: CC0 1.0 Universal
- Downloaded: 2026-07-18
- Selected originals: `click1.ogg`, `click3.ogg`, `rollover2.ogg`, `switch2.ogg`, and `switch4.ogg`
- Local root: `Content/Audio/Sfx`
- Modification: renamed to dry-fire, confirm, hover, toggle, and back cue IDs; encoded audio is unchanged.

## Dark Sci-Fi Audio Pack

- Source: https://opengameart.org/content/dark-sci-fi-audio-pack
- Author: SRG774
- License: CC0 1.0 Universal
- Version: published 2026-05-17; OGG archive updated 2026-07-09
- Downloaded: 2026-07-21
- Selected originals: `title.ogg`, `airy.ogg`, `sector.ogg`, `pulse.ogg`, `urgent.ogg`, `transmission.ogg`, and `victory.ogg`
- Local root: `Content/Audio/Music`
- Modification: selected files only; filenames and encoded audio are unchanged.
- Runtime mapping: menu, recovery/upgrade, Adventure exploration, combat, boss, defeat/results, and victory stinger respectively. Music changes use a 0.35-second fade-out and 0.45-second fade-in.

## Oxanium

- Source: https://github.com/sevmeyer/oxanium/tree/master/fonts/ttf
- License: SIL Open Font License 1.1 (`Content/Fonts/OFL-Oxanium.txt`)
- Downloaded: 2026-07-21
- Selected originals: `Oxanium-Regular.ttf` and `Oxanium-SemiBold.ttf`
- Local root: `Content/Fonts`
- Modification: unchanged TTFs compiled into 20 pt HUD, 22 pt body, and 44 pt heading SpriteFonts. The runtime bitmap glyph table has been removed.

## Arena construction

Orbital Depot uses project-authored JSON for collision, navigation, tiled material assignment, and non-colliding prop placement. Training Ring remains primitive-only. Third-party packs supply selected art assets, not a prebuilt arena map.

## Code sample attribution

The build-time skinned model processor and runtime animation data/player are adapted for KNI/.NET 10 from the Microsoft XNA Skinned Model sample distributed in the KNI XNAGameStudio repository:

- https://github.com/kniEngine/XNAGameStudio/tree/main/Samples/Skinned-Model
- License: Microsoft Permissive License / MIT-compatible sample distribution as documented by KNI.
