param(
    [switch] $Update
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$contentRoot = Join-Path $projectRoot "Content"
$definitionRoot = Join-Path $contentRoot "Data/WeaponBases"
$mgcbPath = Join-Path $contentRoot "FpsFrenzyContent.mgcb"

$bases = [System.Collections.Generic.List[object]]::new()
foreach ($definitionPath in Get-ChildItem -LiteralPath $definitionRoot -Filter "*.json" -File) {
    $root = Get-Content -LiteralPath $definitionPath.FullName -Raw | ConvertFrom-Json
    $definitions = if ($null -ne $root.bases) { @($root.bases) } else { @($root) }
    foreach ($base in $definitions) {
        $bases.Add([pscustomobject]@{
            Id = [string] $base.id
            ModelAsset = [string] $base.modelAsset
            Definition = $definitionPath.FullName
        })
    }
}

$duplicates = @($bases | Group-Object Id | Where-Object Count -gt 1)
if ($duplicates.Count -gt 0) {
    throw "Duplicate weapon base IDs: $($duplicates.Name -join ', ')"
}

$modelAssets = @($bases.ModelAsset | Sort-Object -Unique)
$missingModels = [System.Collections.Generic.List[string]]::new()
foreach ($asset in $modelAssets) {
    $sourcePath = Join-Path $contentRoot ($asset.Replace('/', [IO.Path]::DirectorySeparatorChar) + ".fbx")
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        $missingModels.Add($sourcePath)
    }
}
if ($missingModels.Count -gt 0) {
    throw "Missing weapon source models:`n$($missingModels -join [Environment]::NewLine)"
}

$mgcb = [IO.File]::ReadAllText($mgcbPath)
$missingRegistrations = @($modelAssets | Where-Object {
    $registration = "/build:$_.fbx"
    $mgcb.IndexOf($registration, [StringComparison]::OrdinalIgnoreCase) -lt 0
})

if ($Update -and $missingRegistrations.Count -gt 0) {
    $blocks = foreach ($asset in $missingRegistrations) {
        @"
#begin $asset.fbx
/importer:FbxImporter
/processor:ModelProcessor
/processorParam:ColorKeyEnabled=False
/processorParam:GenerateMipmaps=False
/processorParam:PremultiplyTextureAlpha=True
/build:$asset.fbx

"@
    }
    $marker = "#begin Models/Weapons/Quaternius/AR_1.fbx"
    if ($mgcb.IndexOf($marker, [StringComparison]::Ordinal) -lt 0) {
        throw "Could not find the weapon insertion marker in the MGCB file."
    }
    $mgcb = $mgcb.Replace($marker, (($blocks -join "") + $marker))
    [IO.File]::WriteAllText($mgcbPath, $mgcb)
    $missingRegistrations = @()
}

if ($missingRegistrations.Count -gt 0) {
    throw "Weapon models are not registered in MGCB. Run with -Update:`n$($missingRegistrations -join [Environment]::NewLine)"
}

[pscustomobject]@{
    WeaponBases = $bases.Count
    UniqueModels = $modelAssets.Count
    MissingModels = 0
    MissingRegistrations = 0
    DefinitionRoot = $definitionRoot
}
