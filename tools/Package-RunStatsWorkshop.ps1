[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace = Join-Path $projectRoot 'workshop\RunStats'
$content = Join-Path $workspace 'content'
$releaseRoot = Join-Path $projectRoot 'src\RunStats\bin\Release\net9.0'
$sources = [ordered]@{
    'RunStats.dll' = Join-Path $releaseRoot 'RunStats.dll'
    'runstats.pck' = Join-Path $projectRoot 'src\RunStats\runstats.pck'
    'mod_manifest.json' = Join-Path $projectRoot 'src\RunStats\mod_manifest.json'
}

foreach ($source in $sources.Values) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required release artifact not found: $source"
    }
}

New-Item -ItemType Directory -Path $content -Force | Out-Null
$unexpected = Get-ChildItem -LiteralPath $content -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notin $sources.Keys }
if ($unexpected) {
    $names = ($unexpected.Name | Sort-Object) -join ', '
    throw "Refusing to package over unexpected Workshop content: $names"
}

foreach ($pair in $sources.GetEnumerator()) {
    Copy-Item -LiteralPath $pair.Value -Destination (Join-Path $content $pair.Key) -Force
}

$packaged = Get-ChildItem -LiteralPath $content -File | Sort-Object Name
if ($packaged.Count -ne $sources.Count) {
    throw "Workshop package contains $($packaged.Count) files; expected $($sources.Count)."
}

$packaged | Select-Object Name, Length,
    @{ Name = 'Sha256'; Expression = { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash } }
