[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [string] $GameRoot = 'D:\Steam\steamapps\common\Slay the Spire 2',

    [switch] $ApproveFirstDeployment
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRootFull = [System.IO.Path]::GetFullPath($GameRoot)
$releaseInfo = Join-Path $gameRootFull 'release_info.json'

if (-not (Test-Path -LiteralPath $releaseInfo -PathType Leaf)) {
    throw "The selected game root is not a verified Slay the Spire 2 installation: $gameRootFull"
}

if (Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue) {
    throw 'Slay the Spire 2 is running. Exit the game before deploying RunStats.'
}

$buildRoot = Join-Path $projectRoot "src\RunStats\bin\$Configuration\net9.0"
$manifestPath = Join-Path $projectRoot 'src\RunStats\mod_manifest.json'
$modsRoot = Join-Path $gameRootFull 'mods'
$destination = Join-Path $modsRoot 'RunStats'
$allowedNames = @(
    'RunStats.dll',
    'RunStats.pdb',
    'runstats.pck',
    'mod_manifest.json'
)

$sources = @{}
foreach ($name in $allowedNames) {
    $candidate = if ($name -eq 'mod_manifest.json') {
        $manifestPath
    } elseif ($name -eq 'runstats.pck') {
        Join-Path $projectRoot 'src\RunStats\runstats.pck'
    } else {
        Join-Path $buildRoot $name
    }

    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $sources[$name] = $candidate
    }
}

if (-not $sources.ContainsKey('RunStats.dll')) {
    throw "RunStats.dll was not found. Build the $Configuration configuration first."
}

if (-not $sources.ContainsKey('mod_manifest.json')) {
    throw 'mod_manifest.json was not found.'
}

if (-not $sources.ContainsKey('runstats.pck')) {
    throw 'runstats.pck was not found. Build the resource pack first.'
}

if ((-not (Test-Path -LiteralPath $destination)) -and (-not $ApproveFirstDeployment)) {
    throw 'First deployment requires the explicit -ApproveFirstDeployment switch after plan approval.'
}

if (Test-Path -LiteralPath $destination -PathType Container) {
    $unexpected = Get-ChildItem -LiteralPath $destination -File |
        Where-Object { $_.Name -notin $allowedNames }

    if ($unexpected) {
        $names = ($unexpected.Name | Sort-Object) -join ', '
        throw "Refusing to deploy over unexpected files in the RunStats directory: $names"
    }
}

if ($PSCmdlet.ShouldProcess($destination, "Copy RunStats-owned $Configuration artifacts")) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($name in ($sources.Keys | Sort-Object)) {
        Copy-Item -LiteralPath $sources[$name] -Destination (Join-Path $destination $name) -Force
    }
}
