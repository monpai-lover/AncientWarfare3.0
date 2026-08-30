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
        'private static readonly bool EnableOfficialBodySkinSwitch = true;',
        'if (EnableOfficialBodySkinSwitch)',
        'XiaActorTextureRules.ResolveOfficialBodyDirectory(',
        'LineageKeys.COURT_OFFICE_ID',
        'OfficialCareerStateService.OfficeGradeForOffice(',
        'XiaRace.TEXTURE_PATH + officialBody')
    TextureBinding = @(
        'tex.texture_path_leader = pBasePath + "leader_1";',
        'tex.texture_head_king =',
        'pBasePath + "heads_special/head_king";',
        'tex.texture_head_warrior =',
        'pBasePath + "heads_special/head_warrior";',
        'tex.texture_heads_old_male =',
        'heads_special/head_old_male',
        'tex.texture_heads_old_female =',
        'heads_special/head_old_female',
        'XiaActorTextureRules.ExpandSkins(maleDirs, count)',
        'XiaActorTextureRules.ExpandSkins(femaleDirs, count)',
        'XiaActorTextureRules.ExpandSkins(warriorDirs, count)')
    VisualPatch = @(
        'TryApplyXiaSpecialHead(__instance)',
        'XiaActorTextureRules.ResolveOfficialBodyDirectory(',
        'pPath = textureAsset.texture_path_base + officialBody',
        'OfficialCareerStateService.OfficeGradeForOffice(',
        'LineageKeys.COURT_LAYER',
        'XiaActorTextureRules.ResolveOfficialHeadPath(',
        'XiaActorTextureRules.ResolveWarriorHeadPath(',
        'SpriteTextureLoader.getSprite(',
        'heads_special/head_king', 'heads_heir/head_0')
    AvatarHead = @(
        'XiaActorTextureRules.ResolveOfficialHeadPath(',
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
if (-not $rules.Contains('"heads_warrior/head_"')) {
    $failures.Add('Warriors must retain the two custom head variants')
}
$textureBinding = $sources.TextureBinding
if ($textureBinding.Contains('tex.texture_head_king = pBasePath + "heads_king/')) {
    $failures.Add('Vanilla king head must not use the custom heads_king path')
}
if ($textureBinding.Contains('tex.texture_head_warrior = pBasePath + "heads_warrior/')) {
    $failures.Add('Vanilla warrior head must not use the custom heads_warrior path')
}

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    $failures.Add("Updated actor texture source is missing: $sourceRoot")
}
else {
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relative = $sourceFile.FullName.Substring($sourceRoot.Length).
            TrimStart('\', '/')
        if ($relative -like 'heads_king\*') {
            continue
        }
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
# directory. Keep compatibility copies for warrior heads still loaded by
# vanilla code; king now uses the special head path directly.
foreach ($specialHead in @(
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

if (Test-Path -LiteralPath (Join-Path $xiaRoot 'heads_king')) {
    $failures.Add('Obsolete custom heads_king directory must be absent')
}
if (-not (Test-Path -LiteralPath (Join-Path $xiaRoot 'heads_special\head_king\head_king.png'))) {
    $failures.Add('King head must remain at heads_special/head_king')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Xia official actor texture overlay guard passed.'
