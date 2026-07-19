param(
    [Parameter(Mandatory = $true)]
    [string] $Name,
    [ValidateSet(30, 60)]
    [int] $FramesPerSecond = 60,
    [ValidateRange(1, 30)]
    [int] $Seconds = 10,
    [ValidateRange(0, 9)]
    [int] $StartingEncounter = 0,
    [string] $StartingWeapon = "pulse-sidearm",
    [int] $RunSeed = 1337,
    [switch] $CaptureMenus,
    [switch] $GodMode,
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
$env:FPS_FRENZY_AUTORECORD_SECONDS = $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:FPS_FRENZY_RECORD_FPS = $FramesPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:FPS_FRENZY_RECORD_NAME = $Name
$env:FPS_FRENZY_CAPTURE_DIR = $resolvedCaptureDirectory
$env:FPS_FRENZY_START_WAVE = $StartingEncounter.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:FPS_FRENZY_START_WEAPON = $StartingWeapon
$env:FPS_FRENZY_RUN_SEED = $RunSeed.ToString([Globalization.CultureInfo]::InvariantCulture)
$env:FPS_FRENZY_CAPTURE_GOD_MODE = $GodMode.IsPresent.ToString().ToLowerInvariant()
if ($CaptureMenus) {
    $env:FPS_FRENZY_AUTOCAPTURE_MENUS = "1"
} else {
    Remove-Item Env:FPS_FRENZY_AUTOCAPTURE_MENUS -ErrorAction SilentlyContinue
}

dotnet run --project (Join-Path $projectRoot "src/FpsFrenzy.Desktop/FpsFrenzy.Desktop.csproj") -c Debug
if ($LASTEXITCODE -ne 0) {
    throw "FPS Frenzy exited with code $LASTEXITCODE."
}

$frames = Join-Path $recordingFull "frame-%05d.png"
$output = Join-Path $resolvedCaptureDirectory "$Name.mp4"
$ffmpeg = Get-Command ffmpeg -ErrorAction Stop
& $ffmpeg.Source -y -framerate $FramesPerSecond -i $frames -c:v libx264 -pix_fmt yuv420p -movflags +faststart $output
if ($LASTEXITCODE -ne 0) {
    throw "ffmpeg exited with code $LASTEXITCODE."
}

Write-Output $output
