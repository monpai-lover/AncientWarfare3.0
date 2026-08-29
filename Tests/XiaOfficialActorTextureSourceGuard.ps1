$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$texturePatchPath = Join-Path $root 'Code\content\XiaTexturePatch.cs'
$textureBindingPath = Join-Path $root 'Code\content\XiaTextures.cs'
$visualPatchPath = Join-Path $root 'Code\patch\AW_ActorVisualRolePatch.cs'
$avatarHeadPath = Join-Path $root 'Code\patch\AW_AvatarHeadPatch.cs'
$careerPath = Join-Path $root 'Code\core\court\OfficialCareerStateService.cs'

$texturePatch = [IO.File]::ReadAllText($texturePatchPath)
$textureBinding = [IO.File]::ReadAllText($textureBindingPath)
$visualPatch = [IO.File]::ReadAllText($visualPatchPath)
$avatarHead = [IO.File]::ReadAllText($avatarHeadPath)
$career = [IO.File]::ReadAllText($careerPath)
$failures = [Collections.Generic.List[string]]::new()

foreach ($needle in @(
    'OfficialCareerRankRules.Unranked',
    'XiaActorTextureRules.ResolveOfficialBodyDirectory(rank)',
    'XiaRace.TEXTURE_PATH + officialBody',
    '__instance.isKing()')) {
    if (-not $texturePatch.Contains($needle)) {
        $failures.Add("Xia body selection is missing: $needle")
    }
}

foreach ($needle in @(
    'OfficialCareerRankRules.Unranked',
    'XiaActorTextureRules.ResolveOfficialHeadPath(rank)',
    'heads_heir/head_0',
    'actor.isCityLeader()',
    'OfficialCareerRankRules.MinimumRank',
    'ActorAnimationLoader.getHeadSpecial(')) {
    if (-not $avatarHead.Contains($needle)) {
        $failures.Add("Xia avatar official head selection is missing: $needle")
    }
}

foreach ($needle in @(
    'TryApplyXiaSpecialHead(__instance)',
    'XiaActorTextureRules.ResolveOfficialHeadPath(rank)',
    'heads_king/head_0',
    'heads_heir/head_0',
    'heads_warrior/head_')) {
    if (-not $visualPatch.Contains($needle)) {
        $failures.Add("Xia head selection is missing: $needle")
    }
}

foreach ($needle in @(
    'tex.texture_path_leader = pBasePath + "leader_1";',
    'XiaActorTextureRules.ExpandSkins(maleDirs, count)',
    'XiaActorTextureRules.ExpandSkins(femaleDirs, count)',
    'XiaActorTextureRules.ExpandSkins(warriorDirs, count)')) {
    if (-not $textureBinding.Contains($needle)) {
        $failures.Add("Xia texture binding is missing: $needle")
    }
}

if ($textureBinding.Contains(
        'tex.texture_path_leader = pBasePath + "leader";')) {
    $failures.Add('Legacy Xia leader directory is still configured.')
}

foreach ($needle in @(
    'out int previousRank, OfficialCareerRankRules.Unranked',
    'int nextRank = OfficialCareerRankRules.ClampRank(pRank);',
    'XiaActorTextureRules.ResolveOfficialTier(previousRank) !=',
    'XiaActorTextureRules.ResolveOfficialTier(nextRank)',
    'LineageService.IsXia(pActor)',
    'pActor.dirty_sprite_head = true;',
    'pActor.clearGraphicsFully();')) {
    if (-not $career.Contains($needle)) {
        $failures.Add("Xia rank-tier appearance invalidation is missing: $needle")
    }
}

$xiaRoot = Join-Path $root 'GameResources\actors\species\civs\Xia'
function Test-NumberedDirectories([string] $prefix, [string[]] $expected) {
    $actual = @(Get-ChildItem -LiteralPath $xiaRoot -Directory |
        Where-Object { $_.Name -match ('^' + [regex]::Escape($prefix) + '\d+$') } |
        Sort-Object Name | ForEach-Object Name)
    if (($actual -join ',') -ne ($expected -join ',')) {
        $failures.Add("Unexpected $prefix directories: $($actual -join ',')")
    }
}

Test-NumberedDirectories 'male_' @('male_1', 'male_2', 'male_3')
Test-NumberedDirectories 'female_' @('female_1', 'female_2')
Test-NumberedDirectories 'warrior_' @('warrior_1', 'warrior_2')
Test-NumberedDirectories 'leader_' @('leader_1', 'leader_2', 'leader_3')

$requiredDirectories = @(
    'king', 'heir', 'heads_male', 'heads_female', 'heads_king',
    'heads_heir', 'heads_leader', 'heads_warrior', 'child', 'slave',
    'bandit_male', 'bandit_female', 'bandit_general', 'heads_bandit',
    'heads_special', 'clans', 'special')
foreach ($directory in $requiredDirectories) {
    $requiredPath = Join-Path $xiaRoot $directory
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Container)) {
        $failures.Add("Required Xia actor resource is missing: $directory")
    }
}
if (Test-Path -LiteralPath (Join-Path $xiaRoot 'leader')) {
    $failures.Add('Legacy Xia leader resource directory is still present.')
}

$expectedFileCounts = @{
    'male_1' = 21; 'male_2' = 21; 'male_3' = 21
    'female_1' = 21; 'female_2' = 21
    'warrior_1' = 21; 'warrior_2' = 21
    'king' = 21; 'heir' = 21
    'leader_1' = 21; 'leader_2' = 21; 'leader_3' = 21
    'heads_male' = 7; 'heads_female' = 3
    'heads_king' = 1; 'heads_heir' = 1
    'heads_leader' = 3; 'heads_warrior' = 2
}
foreach ($entry in $expectedFileCounts.GetEnumerator()) {
    $directoryPath = Join-Path $xiaRoot $entry.Key
    if (-not (Test-Path -LiteralPath $directoryPath -PathType Container)) {
        continue
    }
    $count = @(Get-ChildItem -LiteralPath $directoryPath -File).Count
    if ($count -ne $entry.Value) {
        $failures.Add(
            "Unexpected file count for $($entry.Key): $count")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Xia official actor texture source guard passed.'
