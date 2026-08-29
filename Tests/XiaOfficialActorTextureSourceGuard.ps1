$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$actorSourceName = -join ([char]0x4EBA, [char]0x7269)
$sourceRoot = Join-Path (Split-Path -Parent $root) $actorSourceName
$xiaRoot = Join-Path $root 'GameResources\actors\species\civs\Xia'
$failures = [Collections.Generic.List[string]]::new()

$sources = @{
    TexturePatch = [IO.File]::ReadAllText(
        (Join-Path $root 'Code\content\XiaTexturePatch.cs'))
    TextureBinding = [IO.File]::ReadAllText(
        (Join-Path $root 'Code\content\XiaTextures.cs'))
    VisualPatch = [IO.File]::ReadAllText(
        (Join-Path $root 'Code\patch\AW_ActorVisualRolePatch.cs'))
    AvatarHead = [IO.File]::ReadAllText(
        (Join-Path $root 'Code\patch\AW_AvatarHeadPatch.cs'))
    Career = [IO.File]::ReadAllText(
        (Join-Path $root 'Code\core\court\OfficialCareerStateService.cs'))
}

$requiredFragments = @{
    TexturePatch = @(
        'XiaActorTextureRules.ResolveOfficialBodyDirectory(rank)',
        'XiaRace.TEXTURE_PATH + officialBody')
    TextureBinding = @(
        'tex.texture_path_leader = pBasePath + "leader_1";',
        'XiaActorTextureRules.ExpandSkins(maleDirs, count)',
        'XiaActorTextureRules.ExpandSkins(femaleDirs, count)',
        'XiaActorTextureRules.ExpandSkins(warriorDirs, count)')
    VisualPatch = @(
        'TryApplyXiaSpecialHead(__instance)',
        'XiaActorTextureRules.ResolveOfficialHeadPath(rank)',
        'SpriteTextureLoader.getSprite(',
        'heads_king/head_0', 'heads_heir/head_0',
        'heads_warrior/head_')
    AvatarHead = @(
        'XiaActorTextureRules.ResolveOfficialHeadPath(rank)',
        'SpriteTextureLoader.getSprite(',
        'heads_heir/head_0', 'actor.isCityLeader()')
    Career = @(
        'XiaActorTextureRules.ResolveOfficialTier(previousRank) !=',
        'XiaActorTextureRules.ResolveOfficialTier(nextRank)',
        'pActor.dirty_sprite_head = true;',
        'pActor.clearGraphicsFully();')
}

foreach ($entry in $requiredFragments.GetEnumerator()) {
    foreach ($fragment in $entry.Value) {
        if (-not $sources[$entry.Key].Contains($fragment)) {
            $failures.Add("$($entry.Key) is missing: $fragment")
        }
    }
}

$rules = [IO.File]::ReadAllText(
    (Join-Path $root 'Code\core\presentation\XiaActorTextureRules.cs'))
if (-not $rules.Contains('"heads_leader/head_"')) {
    $failures.Add('Official heads must use the heads_leader directory')
}
if ($rules.Contains('heads_leaders/')) {
    $failures.Add('The invalid plural heads_leaders path must not be used')
}

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    $failures.Add("Updated actor texture source is missing: $sourceRoot")
}
else {
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relative = $sourceFile.FullName.Substring($sourceRoot.Length).
            TrimStart('\', '/')
        $destination = Join-Path $xiaRoot $relative
        if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
            $failures.Add("Updated Xia actor texture was not copied: $relative")
            continue
        }
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            $failures.Add("Updated Xia actor texture differs: $relative")
        }
    }
}

# These files are intentionally absent from the update package. Their presence
# proves that overlay copying did not mirror-delete existing Xia resources.
foreach ($preserved in @(
    'leader\sprites.json',
    'male_4\sprites.json',
    'female_10\sprites.json',
    'warrior_10\sprites.json',
    'heads_special\head_old_male\head_old_male.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $xiaRoot $preserved))) {
        $failures.Add("Existing Xia actor resource was deleted: $preserved")
    }
}

# ActorAnimationLoader.getHeadSpecial treats its final path segment as a
# directory. Keep compatibility copies for heads still loaded by vanilla code.
foreach ($specialHead in @(
    'heads_king\head_0',
    'heads_warrior\head_0',
    'heads_warrior\head_1')) {
    $flat = Join-Path $xiaRoot ($specialHead + '.png')
    $nested = Join-Path $xiaRoot (Join-Path $specialHead `
        ((Split-Path $specialHead -Leaf) + '.png'))
    if (-not (Test-Path -LiteralPath $flat -PathType Leaf) -or
        -not (Test-Path -LiteralPath $nested -PathType Leaf)) {
        $failures.Add("Missing special-head compatibility copy: $specialHead")
        continue
    }
    if ((Get-FileHash -LiteralPath $flat -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $nested -Algorithm SHA256).Hash) {
        $failures.Add("Special-head compatibility copy differs: $specialHead")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Xia official actor texture overlay guard passed.'
