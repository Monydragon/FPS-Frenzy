param(
    [ValidateSet("All", "Stills", "Reels")]
    [string] $Mode = "All",
    [string[]] $Weapon = @(),
    [ValidateSet(30, 60)]
    [int] $FramesPerSecond = 60,
    [ValidateRange(2, 10)]
    [int] $Seconds = 10,
    [ValidateRange(1, 10)]
    [int] $ThreatTier = 1,
    [string[]] $DualWieldPair = @("pulse-sidearm:nova-pistol"),
    [switch] $CaptureTierBoundaries,
    [string] $ExecutablePath = "src/FpsFrenzy.Desktop/bin/Debug/net10.0/FPSFrenzy.dll",
    [string] $CaptureDirectory = "artifacts/item-lab"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$captureRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $CaptureDirectory))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
if (-not $captureRoot.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Item Lab output must remain inside the repository artifacts directory."
}

$allWeapons = @(
    "pulse-sidearm", "ion-repeater", "phase-handcannon", "rift-needler", "nova-pistol",
    "ion-sprayer", "vector-smg", "arc-machine-pistol", "shredder-repeater", "blink-carbine",
    "burst-carbine", "vector-carbine", "railburst-rifle", "suppression-array", "chrono-carbine",
    "scatter-blaster", "drum-scattergun", "slug-driver", "shardcaster", "shockwave-cannon",
    "longshot-rifle", "needle-rail", "viper-marksman", "phase-sniper", "horizon-lance",
    "beam-rifle", "focus-laser", "lancer-rifle", "prism-projector", "solar-lance",
    "plasma-launcher", "comet-projector", "cluster-mortar", "gravity-lobber", "singularity-driver",
    "arc-cannon", "tesla-repeater", "coil-driver", "storm-projector", "tempest-cannon",
    "rotary-cannon", "micro-missile-array", "siege-railgun", "flak-projector", "reactor-cannon",
    "disc-rebounder", "prism-handgun", "swarm-caster", "gravity-bow", "chrono-driver"
)
$selectedWeapons = if ($Weapon.Count -eq 0) { $allWeapons } else { $Weapon }
$unknown = @($selectedWeapons | Where-Object { $_ -notin $allWeapons })
if ($unknown.Count -gt 0) {
    throw "Unknown Item Lab weapon(s): $($unknown -join ', ')."
}

New-Item -ItemType Directory -Path $captureRoot -Force | Out-Null
$captureMotion = Join-Path $PSScriptRoot "Capture-Motion.ps1"
$outputs = [Collections.Generic.List[string]]::new()

function Invoke-WeaponCapture([string] $rightWeapon, [string] $leftWeapon, [int] $tier, [string] $suffix) {
    $name = "item-lab-$rightWeapon$suffix-tier-$tier-$($FramesPerSecond)fps"
    $duration = if ($Mode -eq "Stills") { 1 } else { $Seconds }
    $stillOnly = $Mode -eq "Stills"
    & $captureMotion -Name $name -FramesPerSecond $FramesPerSecond -Seconds $duration `
        -StartingWeapon $rightWeapon -LeftWeapon $leftWeapon -ThreatTier $tier -RunSeed 1337 `
        -GodMode -StillOnly:$stillOnly -Quiet -CaptureDirectory $CaptureDirectory -ExecutablePath $ExecutablePath
    if ($LASTEXITCODE -ne 0) {
        throw "Item Lab capture failed for $rightWeapon."
    }

    $movie = Join-Path $captureRoot "$name.mp4"
    if ($Mode -ne "Reels") {
        $still = Join-Path $captureRoot "$name.png"
        if (-not $stillOnly) {
            $frames = Join-Path $captureRoot $name
            $frameNumber = [Math]::Max(0, [Math]::Min(($duration * $FramesPerSecond) - 1,
                [int]($duration * $FramesPerSecond * 0.65)))
            $source = Join-Path $frames ("frame-{0:D5}.png" -f $frameNumber)
            Copy-Item -LiteralPath $source -Destination $still -Force
        }
        $outputs.Add($still)
    }
    if ($Mode -ne "Stills") {
        $outputs.Add($movie)
    }
}

foreach ($weaponId in $selectedWeapons) {
    Invoke-WeaponCapture $weaponId "" $ThreatTier ""
}

foreach ($pair in $DualWieldPair) {
    $parts = $pair.Split(':', 2, [StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Count -ne 2 -or $parts[0] -notin $allWeapons -or $parts[1] -notin $allWeapons) {
        throw "Dual-wield pairs use right:left weapon IDs. Invalid pair '$pair'."
    }
    Invoke-WeaponCapture $parts[0] $parts[1] $ThreatTier "-dual-$($parts[1])"
}

if ($Mode -ne "Stills") {
    $name = "item-lab-set-swap-pulse-to-longshot-$($FramesPerSecond)fps"
    & $captureMotion -Name $name -FramesPerSecond $FramesPerSecond -Seconds $Seconds `
        -StartingWeapon "pulse-sidearm" -LeftWeapon "ion-sprayer" -SetBWeapon "longshot-rifle" `
        -ThreatTier $ThreatTier -RunSeed 1337 -GodMode -CaptureDirectory $CaptureDirectory `
        -ExecutablePath $ExecutablePath
    if ($LASTEXITCODE -ne 0) {
        throw "Item Lab set-swap capture failed."
    }
    $outputs.Add((Join-Path $captureRoot "$name.mp4"))
}

if ($CaptureTierBoundaries) {
    foreach ($tier in 1..10) {
        Invoke-WeaponCapture "pulse-sidearm" "" $tier "-threat-boundary"
    }
}

$outputs
