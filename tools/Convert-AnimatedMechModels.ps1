param(
    [Parameter(Mandatory = $true)]
    [string] $SourceDirectory,
    [Parameter(Mandatory = $true)]
    [string] $DestinationDirectory,
    [string[]] $Models = @("George", "Leela", "Mike", "Stan")
)

$ErrorActionPreference = "Stop"
$builderVersion = "4.2.9001"
$builderTools = Join-Path $env:USERPROFILE ".nuget/packages/nkast.xna.framework.content.pipeline.builder/$builderVersion/tools"
$assimpNet = Join-Path $builderTools "AssimpNet.dll"
$nativeAssimp = Join-Path $builderTools "assimp.dll"

if (-not (Test-Path -LiteralPath $assimpNet) -or -not (Test-Path -LiteralPath $nativeAssimp)) {
    throw "KNI content-builder $builderVersion with AssimpNet is required. Restore the workspace packages first."
}

$resolvedSource = [IO.Path]::GetFullPath($SourceDirectory)
$resolvedDestination = [IO.Path]::GetFullPath($DestinationDirectory)
New-Item -ItemType Directory -Path $resolvedDestination -Force | Out-Null

function Assert-NumericallyEqual {
    param(
        [double] $Expected,
        [double] $Actual,
        [string] $Label
    )

    if ([Math]::Abs($Expected - $Actual) -gt 0.000001) {
        throw "$Label changed during canonical glTF to assbin conversion: expected $Expected, got $Actual."
    }
}

function Assert-KeySequencePreserved {
    param(
        $ExpectedKeys,
        $ActualKeys,
        [string[]] $Components,
        [string] $Label
    )

    $expected = @($ExpectedKeys)
    $actual = @($ActualKeys)
    if ($expected.Count -ne $actual.Count) {
        throw "$Label key count changed from $($expected.Count) to $($actual.Count)."
    }

    for ($index = 0; $index -lt $expected.Count; $index++) {
        Assert-NumericallyEqual $expected[$index].Time $actual[$index].Time "$Label key $index time"
        foreach ($component in $Components) {
            Assert-NumericallyEqual `
                $expected[$index].Value.$component `
                $actual[$index].Value.$component `
                "$Label key $index $component"
        }
    }
}

function Assert-AnimationTracksPreserved {
    param(
        $ExpectedScene,
        $ActualScene,
        [string] $Model
    )

    $expectedAnimations = @($ExpectedScene.Animations)
    $actualAnimations = @($ActualScene.Animations)
    if ($expectedAnimations.Count -ne $actualAnimations.Count) {
        throw "$Model animation count changed from $($expectedAnimations.Count) to $($actualAnimations.Count)."
    }

    foreach ($expectedAnimation in $expectedAnimations) {
        $actualAnimation = @($actualAnimations | Where-Object Name -eq $expectedAnimation.Name)
        if ($actualAnimation.Count -ne 1) {
            throw "$Model animation '$($expectedAnimation.Name)' was not preserved uniquely."
        }
        $actualAnimation = $actualAnimation[0]
        Assert-NumericallyEqual `
            $expectedAnimation.DurationInTicks `
            $actualAnimation.DurationInTicks `
            "$Model/$($expectedAnimation.Name) duration"
        Assert-NumericallyEqual `
            $expectedAnimation.TicksPerSecond `
            $actualAnimation.TicksPerSecond `
            "$Model/$($expectedAnimation.Name) ticks per second"

        $expectedChannels = @($expectedAnimation.NodeAnimationChannels)
        $actualChannels = @($actualAnimation.NodeAnimationChannels)
        if ($expectedChannels.Count -ne $actualChannels.Count) {
            throw "$Model/$($expectedAnimation.Name) channel count changed."
        }

        foreach ($expectedChannel in $expectedChannels) {
            $actualChannel = @($actualChannels | Where-Object NodeName -eq $expectedChannel.NodeName)
            if ($actualChannel.Count -ne 1) {
                throw "$Model/$($expectedAnimation.Name)/$($expectedChannel.NodeName) was not preserved uniquely."
            }
            $actualChannel = $actualChannel[0]
            $label = "$Model/$($expectedAnimation.Name)/$($expectedChannel.NodeName)"
            Assert-KeySequencePreserved $expectedChannel.PositionKeys $actualChannel.PositionKeys @("X", "Y", "Z") "$label position"
            Assert-KeySequencePreserved $expectedChannel.RotationKeys $actualChannel.RotationKeys @("X", "Y", "Z", "W") "$label rotation"
            Assert-KeySequencePreserved $expectedChannel.ScalingKeys $actualChannel.ScalingKeys @("X", "Y", "Z") "$label scale"
        }
    }
}

function Set-DeterministicAssbinHeader {
    param([string] $Path)

    # Assimp writes only its wall-clock export time into this fixed-width header.
    # Canonicalize that non-content field so rerunning the conversion does not churn
    # the checked-in derived model while leaving the serialized scene untouched.
    $signature = [Text.Encoding]::ASCII.GetBytes("ASSIMP.binary-dump.")
    $timestamp = [Text.Encoding]::ASCII.GetBytes("Thu Jan 01 00:00:00 1970`n")
    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None)
    try {
        $actualSignature = [byte[]]::new($signature.Length)
        if ($stream.Read($actualSignature, 0, $actualSignature.Length) -ne $actualSignature.Length -or
            [Text.Encoding]::ASCII.GetString($actualSignature) -ne "ASSIMP.binary-dump.") {
            throw "$Path does not have the expected Assimp binary header."
        }

        $stream.Position = $signature.Length
        $stream.Write($timestamp, 0, $timestamp.Length)
    }
    finally {
        $stream.Dispose()
    }
}

$previousPath = $env:PATH
try {
    $env:PATH = "$builderTools;$env:PATH"
    Add-Type -Path $assimpNet

    foreach ($model in $Models) {
        $input = Join-Path $resolvedSource "$model.gltf"
        $output = Join-Path $resolvedDestination "$model.assbin"
        if (-not (Test-Path -LiteralPath $input)) {
            throw "Canonical source is missing: $input"
        }

        $context = [Assimp.AssimpContext]::new()
        try {
            $scene = $context.ImportFile($input, [Assimp.PostProcessSteps]::None)
            if ($null -eq $scene -or $null -eq $scene.RootNode -or $scene.MeshCount -lt 1) {
                throw "$model must import as one scene with at least one skinned mesh."
            }

            # KNI's FbxImporter accepts Assimp's binary interchange format, but it
            # expects every weighted bone to belong to one skeleton below the scene
            # root. An identity parent preserves authored local transforms while
            # exposing one common skeleton for both Quaternius robot packs.
            $scene.RootNode.Name = "RootNode"
            $armature = [Assimp.Node]::new("RobotArmature", $scene.RootNode)
            $scene.RootNode.Children.Add($armature)
            $skeletonRoots = @($scene.RootNode.Children | Where-Object {
                $_ -ne $armature -and -not $_.HasMeshes
            })
            foreach ($root in $skeletonRoots) {
                [void] $scene.RootNode.Children.Remove($root)
                $armature.Children.Add($root)
            }

            if ($skeletonRoots.Count -lt 1) {
                throw "$model did not expose any top-level skeleton roots."
            }
            $rootBoneName = "RobotArmature"

            # A non-rendering epsilon influence makes the synthetic parent part of
            # the imported skin. Preserve the original normalized weight sum.
            $epsilon = [single] 0.000001
            foreach ($mesh in $scene.Meshes) {
                if ($mesh.Bones | Where-Object Name -eq $rootBoneName) {
                    continue
                }

                $adjusted = $false
                foreach ($bone in $mesh.Bones) {
                    for ($index = 0; $index -lt $bone.VertexWeights.Count; $index++) {
                        $weight = $bone.VertexWeights[$index]
                        if ($weight.VertexID -eq 0 -and $weight.Weight -gt $epsilon) {
                            $bone.VertexWeights[$index] = [Assimp.VertexWeight]::new(
                                0,
                                [single] ($weight.Weight - $epsilon))
                            $adjusted = $true
                            break
                        }
                    }
                    if ($adjusted) { break }
                }
                if (-not $adjusted) {
                    throw "$model mesh '$($mesh.Name)' has no adjustable weight on vertex zero."
                }

                $rootBone = [Assimp.Bone]::new()
                $rootBone.Name = $rootBoneName
                $rootBone.OffsetMatrix = [Assimp.Matrix4x4]::Identity
                $rootBone.VertexWeights.Add([Assimp.VertexWeight]::new(0, $epsilon))
                $mesh.Bones.Add($rootBone)
            }

            if (-not $context.ExportFile($scene, $output, "assbin", [Assimp.PostProcessSteps]::None)) {
                throw "Assimp did not export $output."
            }
            Set-DeterministicAssbinHeader $output

            $verification = $context.ImportFile($output, [Assimp.PostProcessSteps]::None)
            Assert-AnimationTracksPreserved $scene $verification $model
            $clipNames = @($verification.Animations | ForEach-Object Name)
            $boneNames = @($verification.Meshes | ForEach-Object Bones | ForEach-Object Name |
                Sort-Object -Unique)
            if ($verification.MeshCount -lt 1 -or
                $boneNames.Count -gt 72 -or
                $clipNames.Count -lt 1) {
                throw "$model assbin verification failed."
            }

            [pscustomobject]@{
                Model = $model
                Source = $input
                Output = $output
                Bones = $boneNames.Count
                Clips = $clipNames -join ","
            }
        }
        finally {
            $context.Dispose()
        }
    }
}
finally {
    $env:PATH = $previousPath
}
