param(
    [ValidateSet(
        "all",
        "robot-striker",
        "robot-interceptor",
        "robot-juggernaut",
        "robot-wasp",
        "robot-warden",
        "breach-walker")]
    [string[]] $Enemy = @("all"),
    [ValidateSet("All", "Stills", "Reels")]
    [string] $Mode = "All",
    [ValidateSet(30, 60)]
    [int[]] $FrameRates = @(30, 60),
    [string] $CaptureDirectory = "artifacts/character-lab",
    [switch] $SkipBuild,
    [switch] $SkipEncode
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$desktopProject = Join-Path $projectRoot "src/FpsFrenzy.Desktop/FpsFrenzy.Desktop.csproj"
$resolvedCaptureDirectory = [IO.Path]::GetFullPath((Join-Path $projectRoot $CaptureDirectory))
$roster = @(
    "robot-striker",
    "robot-interceptor",
    "robot-juggernaut",
    "robot-wasp",
    "robot-warden",
    "breach-walker"
)
$selectedEnemies = if ($Enemy -contains "all") { $roster } else { $Enemy }
$states = @("idle", "locomotion", "attack", "hit", "death")
$distances = @("near", "medium", "far")
$managedVariables = @(
    "FPS_FRENZY_CHARACTER_LAB",
    "FPS_FRENZY_LAB_ENEMY",
    "FPS_FRENZY_LAB_MODE",
    "FPS_FRENZY_LAB_FPS",
    "FPS_FRENZY_CAPTURE_DIR",
    "FPS_FRENZY_AUTOCAPTURE",
    "FPS_FRENZY_AUTOCAPTURE_MENUS",
    "FPS_FRENZY_AUTORECORD_SECONDS",
    "FPS_FRENZY_RECORD_FPS",
    "FPS_FRENZY_RECORD_NAME",
    "FPS_FRENZY_CAPTURE_PREFIX",
    "FPS_FRENZY_CAPTURE_GOD_MODE"
)
$previousEnvironment = @{}
foreach ($name in $managedVariables) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

function Set-LabEnvironment([string] $enemyId, [string] $captureMode, [int] $framesPerSecond) {
    $env:FPS_FRENZY_CHARACTER_LAB = "1"
    $env:FPS_FRENZY_LAB_ENEMY = $enemyId
    $env:FPS_FRENZY_LAB_MODE = $captureMode
    $env:FPS_FRENZY_LAB_FPS = $framesPerSecond.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:FPS_FRENZY_CAPTURE_DIR = $resolvedCaptureDirectory
    foreach ($name in @(
        "FPS_FRENZY_AUTOCAPTURE",
        "FPS_FRENZY_AUTOCAPTURE_MENUS",
        "FPS_FRENZY_AUTORECORD_SECONDS",
        "FPS_FRENZY_RECORD_FPS",
        "FPS_FRENZY_RECORD_NAME",
        "FPS_FRENZY_CAPTURE_PREFIX",
        "FPS_FRENZY_CAPTURE_GOD_MODE")) {
        [Environment]::SetEnvironmentVariable($name, $null, "Process")
    }
}

function Assert-InCaptureRoot([string] $path) {
    $root = $resolvedCaptureDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Character Lab output must remain inside the capture directory: $full"
    }
    return $full
}

function Invoke-LabGame {
    dotnet run --project $desktopProject -c Debug --no-build --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "FPS Frenzy Character Lab exited with code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $resolvedCaptureDirectory -Force | Out-Null
$outputs = [Collections.Generic.List[string]]::new()

try {
    if (-not $SkipBuild) {
        dotnet build $desktopProject -c Debug --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "FPS Frenzy desktop build exited with code $LASTEXITCODE."
        }
    }

    foreach ($enemyId in $selectedEnemies) {
        if ($Mode -in @("All", "Stills")) {
            foreach ($state in $states) {
                foreach ($distance in $distances) {
                    $still = Assert-InCaptureRoot (
                        Join-Path $resolvedCaptureDirectory "character-lab-$enemyId-$state-$distance.png")
                    if (Test-Path -LiteralPath $still) {
                        Remove-Item -LiteralPath $still -Force
                    }
                }
            }

            Set-LabEnvironment $enemyId "stills" 60
            Invoke-LabGame
            $stillFiles = Get-ChildItem -LiteralPath $resolvedCaptureDirectory `
                -Filter "character-lab-$enemyId-*.png" -File
            if ($stillFiles.Count -ne 15) {
                throw "Expected 15 Character Lab stills for $enemyId, found $($stillFiles.Count)."
            }
            foreach ($stillFile in $stillFiles) {
                $outputs.Add($stillFile.FullName)
            }
        }

        if ($Mode -in @("All", "Reels")) {
            foreach ($framesPerSecond in $FrameRates) {
                $name = "character-lab-$enemyId-reel-$($framesPerSecond)fps"
                $recordingDirectory = Assert-InCaptureRoot (Join-Path $resolvedCaptureDirectory $name)
                if (Test-Path -LiteralPath $recordingDirectory) {
                    Remove-Item -LiteralPath $recordingDirectory -Recurse -Force
                }

                Set-LabEnvironment $enemyId "reel" $framesPerSecond
                Invoke-LabGame
                $expectedFrames = 10 * $framesPerSecond
                $actualFrames = @(Get-ChildItem -LiteralPath $recordingDirectory -Filter "frame-*.png" -File).Count
                if ($actualFrames -ne $expectedFrames) {
                    throw "Expected $expectedFrames frames for $name, found $actualFrames."
                }

                if (-not $SkipEncode) {
                    $ffmpeg = Get-Command ffmpeg -ErrorAction Stop
                    $inputPattern = Join-Path $recordingDirectory "frame-%05d.png"
                    $output = Assert-InCaptureRoot (Join-Path $resolvedCaptureDirectory "$name.mp4")
                    & $ffmpeg.Source -y -framerate $framesPerSecond -i $inputPattern `
                        -c:v libx264 -pix_fmt yuv420p -movflags +faststart $output
                    if ($LASTEXITCODE -ne 0) {
                        throw "ffmpeg exited with code $LASTEXITCODE while encoding $name."
                    }
                    $outputs.Add($output)
                } else {
                    $outputs.Add($recordingDirectory)
                }
            }
        }
    }
}
finally {
    foreach ($name in $managedVariables) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], "Process")
    }
}

$outputs
