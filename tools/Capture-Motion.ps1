param(
    [Parameter(Mandatory = $true)]
    [string] $Name,
    [ValidateSet(30, 60)]
    [int] $FramesPerSecond = 60,
    [string] $ExecutablePath = "",
    [ValidateRange(1, 30)]
    [int] $Seconds = 10,
    [ValidateRange(0, 9)]
    [int] $StartingEncounter = 0,
    [string] $StartingWeapon = "pulse-sidearm",
    [string] $LeftWeapon = "",
    [string] $SetBWeapon = "",
    [ValidateRange(1, 10)]
    [int] $ThreatTier = 1,
    [int] $RunSeed = 1337,
    [switch] $CaptureMenus,
    [switch] $AimDownSights,
    [switch] $DebugLab,
    [switch] $GodMode,
    [switch] $StillOnly,
    [switch] $Quiet,
    [string] $CaptureDirectory = "artifacts/render-captures"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$resolvedCaptureDirectory = Join-Path $projectRoot $CaptureDirectory
$recordingDirectory = Join-Path $resolvedCaptureDirectory $Name
$captureRootFull = [IO.Path]::GetFullPath($resolvedCaptureDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
$recordingFull = [IO.Path]::GetFullPath($recordingDirectory)
if (-not $recordingFull.StartsWith($captureRootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Recording directory must remain inside the capture directory."
}

if (Test-Path -LiteralPath $recordingFull) {
    Remove-Item -LiteralPath $recordingFull -Recurse -Force
}

$env:FPS_FRENZY_AUTOCAPTURE = "1"
if ($StillOnly) {
    Remove-Item Env:FPS_FRENZY_AUTORECORD_SECONDS -ErrorAction SilentlyContinue
    Remove-Item Env:FPS_FRENZY_RECORD_FPS -ErrorAction SilentlyContinue
    Remove-Item Env:FPS_FRENZY_RECORD_NAME -ErrorAction SilentlyContinue
} else {
    $env:FPS_FRENZY_AUTORECORD_SECONDS = $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:FPS_FRENZY_RECORD_FPS = $FramesPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:FPS_FRENZY_RECORD_NAME = $Name
}
$env:FPS_FRENZY_CAPTURE_DIR = $resolvedCaptureDirectory
$env:FPS_FRENZY_START_WAVE = $StartingEncounter.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:FPS_FRENZY_START_WEAPON = $StartingWeapon
$env:FPS_FRENZY_CAPTURE_THREAT_TIER = $ThreatTier.ToString([Globalization.CultureInfo]::InvariantCulture)
if ([string]::IsNullOrWhiteSpace($LeftWeapon)) {
    Remove-Item Env:FPS_FRENZY_CAPTURE_LEFT_WEAPON -ErrorAction SilentlyContinue
} else {
    $env:FPS_FRENZY_CAPTURE_LEFT_WEAPON = $LeftWeapon
}
if ([string]::IsNullOrWhiteSpace($SetBWeapon)) {
    Remove-Item Env:FPS_FRENZY_CAPTURE_SET_B_WEAPON -ErrorAction SilentlyContinue
} else {
    $env:FPS_FRENZY_CAPTURE_SET_B_WEAPON = $SetBWeapon
}
$env:FPS_FRENZY_RUN_SEED = $RunSeed.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:FPS_FRENZY_CAPTURE_GOD_MODE = $GodMode.IsPresent.ToString().ToLowerInvariant()
if ($CaptureMenus) {
    $env:FPS_FRENZY_AUTOCAPTURE_MENUS = "1"
} else {
    Remove-Item Env:FPS_FRENZY_AUTOCAPTURE_MENUS -ErrorAction SilentlyContinue
}
if ($AimDownSights) {
    $env:FPS_FRENZY_CAPTURE_ADS = "1"
} else {
    Remove-Item Env:FPS_FRENZY_CAPTURE_ADS -ErrorAction SilentlyContinue
}
if ($DebugLab) {
    $env:FPS_FRENZY_CAPTURE_DEBUG_LAB = "1"
} else {
    Remove-Item Env:FPS_FRENZY_CAPTURE_DEBUG_LAB -ErrorAction SilentlyContinue
}

$previousErrorAction = $ErrorActionPreference
if ($Quiet) { $ErrorActionPreference = "Continue" }
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    if ($Quiet) {
        dotnet run --project (Join-Path $projectRoot "src/FpsFrenzy.Desktop/FpsFrenzy.Desktop.csproj") -c Debug *> $null
    } else {
        dotnet run --project (Join-Path $projectRoot "src/FpsFrenzy.Desktop/FpsFrenzy.Desktop.csproj") -c Debug
    }
} else {
    $resolvedExecutable = [IO.Path]::GetFullPath((Join-Path $projectRoot $ExecutablePath))
    if ($Quiet) { dotnet $resolvedExecutable *> $null } else { dotnet $resolvedExecutable }
}
$gameExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorAction
if ($gameExitCode -ne 0) {
    throw "FPS Frenzy exited with code $gameExitCode."
}

if ($StillOnly) {
    $sourceStill = Get-ChildItem -LiteralPath $resolvedCaptureDirectory -Filter "0*.png" -File |
        Sort-Object LastWriteTimeUtc, Name -Descending | Select-Object -First 1
    if ($null -eq $sourceStill) {
        throw "FPS Frenzy did not produce the expected Item Lab still."
    }
    $outputStill = Join-Path $resolvedCaptureDirectory "$Name.png"
    Copy-Item -LiteralPath $sourceStill.FullName -Destination $outputStill -Force
    Write-Output $outputStill
    return
}

$frames = Join-Path $recordingFull "frame-%05d.png"
$output = Join-Path $resolvedCaptureDirectory "$Name.mp4"
$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
& $ffmpeg.Source -y -framerate $FramesPerSecond -i $frames -c:v libx264 -pix_fmt yuv420p -movflags +faststart $output
if ($LASTEXITCODE -ne 0) {
    throw "ffmpeg exited with code $LASTEXITCODE."
}

Write-Output $output
