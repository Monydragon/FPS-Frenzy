param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath,
    [Parameter(Mandatory = $true)]
    [string] $DestinationPath
)

$ErrorActionPreference = "Stop"
$builderVersion = "4.2.9001"
$builderTools = Join-Path $env:USERPROFILE ".nuget/packages/nkast.xna.framework.content.pipeline.builder/$builderVersion/tools"
$assimpNet = Join-Path $builderTools "AssimpNet.dll"
$nativeAssimp = Join-Path $builderTools "assimp.dll"
if (-not (Test-Path -LiteralPath $assimpNet) -or -not (Test-Path -LiteralPath $nativeAssimp)) {
    throw "KNI content-builder $builderVersion with AssimpNet is required."
}

$resolvedSource = [IO.Path]::GetFullPath($SourcePath)
$resolvedDestination = [IO.Path]::GetFullPath($DestinationPath)
$destinationDirectory = Split-Path -Parent $resolvedDestination
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

$previousPath = $env:PATH
try {
    $env:PATH = "$builderTools;$env:PATH"
    Add-Type -Path $assimpNet
    $context = [Assimp.AssimpContext]::new()
    try {
        # The menu mannequin never animates. Bake its authored bind pose once so the
        # stock Reach model processor sees a static scene instead of several FBX roots.
        $scene = $context.ImportFile(
            $resolvedSource,
            [Assimp.PostProcessSteps]::PreTransformVertices)
        if ($null -eq $scene -or $scene.MeshCount -lt 1) {
            throw "The mannequin source did not contain renderable meshes."
        }

        foreach ($mesh in $scene.Meshes) {
            $mesh.Bones.Clear()
        }
        $scene.Animations.Clear()
        $scene.RootNode.Name = "OperatorMannequin"
        if (-not $context.ExportFile($scene, $resolvedDestination, "assbin", [Assimp.PostProcessSteps]::None)) {
            throw "Assimp did not export the static mannequin."
        }

        $verification = $context.ImportFile($resolvedDestination, [Assimp.PostProcessSteps]::None)
        $weightedMeshes = @($verification.Meshes | Where-Object BoneCount -gt 0)
        if ($verification.MeshCount -lt 1 -or $weightedMeshes.Count -gt 0) {
            throw "Static mannequin verification failed."
        }

        [pscustomobject]@{
            Source = $resolvedSource
            Output = $resolvedDestination
            Meshes = $verification.MeshCount
            Bones = 0
        }
    }
    finally {
        $context.Dispose()
    }
}
finally {
    $env:PATH = $previousPath
}
