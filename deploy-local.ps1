[CmdletBinding()]
param(
    [string]$SourceRoot = 'F:\WorldBox New Mod\AncientWarfare3.0',
    [string]$DestinationRoot = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
)

$ErrorActionPreference = 'Stop'

$productionDirectories = @(
    'Code', 'Assemblies', 'ABPackages', 'EmbededResources', 'fonts',
    'GameResources', 'Locales', 'name_generators', 'word_libraries',
    'THIRD_PARTY_NOTICES'
)
$productionRootFiles = @(
    'AncientWarfare3.csproj', 'mod.json', 'default_config.json', 'icon.png',
    'README.md', 'sponsor_qr.jpg', 'supporters.csv', 'THIRD_PARTY_NOTICES.md'
)

if (-not (Test-Path -LiteralPath $DestinationRoot)) {
    throw "Destination does not exist: $DestinationRoot"
}

# 1. Timestamped safety backup of the current Code/ + mod.json
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $DestinationRoot ".aw3-deploy-backups\$stamp"
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $DestinationRoot 'Code')) {
    robocopy (Join-Path $DestinationRoot 'Code') (Join-Path $backupDir 'Code') /E /NFL /NDL /NJH /NJS /NP | Out-Null
}
Copy-Item -LiteralPath (Join-Path $DestinationRoot 'mod.json') -Destination $backupDir -Force -ErrorAction SilentlyContinue
Write-Output "Backup written: $backupDir"

# 2. Mirror each production directory (deletes stale files in destination)
foreach ($dir in $productionDirectories) {
    $src = Join-Path $SourceRoot $dir
    if (-not (Test-Path -LiteralPath $src)) {
        Write-Output "SKIP missing source dir: $dir"
        continue
    }
    $dst = Join-Path $DestinationRoot $dir
    robocopy $src $dst /MIR /XD bin obj /XF *.pdb /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $dir (exit $LASTEXITCODE)" }
    Write-Output "Mirrored: $dir"
}

# 3. Copy production root files
foreach ($file in $productionRootFiles) {
    $src = Join-Path $SourceRoot $file
    if (-not (Test-Path -LiteralPath $src)) {
        Write-Output "SKIP missing root file: $file"
        continue
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $DestinationRoot $file) -Force
}
Write-Output "Root files copied."
Write-Output "DEPLOY-DONE"
