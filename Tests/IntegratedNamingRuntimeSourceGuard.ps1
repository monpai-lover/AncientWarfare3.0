$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$required = @(
    'Code/core/naming/AWLocalizedNameService.cs'
    'Code/core/naming/AWLocalizedMottoService.cs'
    'Code/core/naming/AWLocalizedNameRefreshService.cs'
    'Code/patch/naming/AW_ActorLocalizedNamePatch.cs'
    'Code/patch/naming/AW_WorldLocalizedNamePatches.cs'
    'Code/patch/naming/AW_LanguageChangeNamePatch.cs'
    'Code/core/naming/AWActorInitialNameRules.cs'
)

foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $relative))) {
        throw "Missing integrated naming runtime source: $relative"
    }
}

$service = Get-Content -LiteralPath (Join-Path $repoRoot $required[0]) -Raw
$mottoService = Get-Content -LiteralPath (Join-Path $repoRoot $required[1]) -Raw
$localizedNameService = Get-Content -LiteralPath (Join-Path $repoRoot `
    'Code/core/naming/AWLocalizedNameService.cs') -Raw
$actorInitialNameRules = Get-Content -LiteralPath (Join-Path $repoRoot `
    'Code/core/naming/AWActorInitialNameRules.cs') -Raw
$actorNamePatch = Get-Content -LiteralPath (Join-Path $repoRoot `
    'Code/patch/naming/AW_ActorLocalizedNamePatch.cs') -Raw
$parameterGetters = Get-Content -LiteralPath (Join-Path $repoRoot `
    'Code/core/naming/AWNameParameterGetters.cs') -Raw
$migration = Get-Content -LiteralPath (Join-Path $repoRoot `
    'Code/core/naming/AWLocalizedNameMigrationService.cs') -Raw
$xiaRepair = Get-Content -LiteralPath (Join-Path $repoRoot `
    'Code/content/XiaNamingRepair.cs') -Raw
$stateNames = Get-Content -LiteralPath (Join-Path $repoRoot `
    'Code/core/lineage/StateNameService.cs') -Raw
foreach ($token in @(
    'AWNameDataKeys.NativeName',
    'AWNameDataKeys.ChineseName',
    'AWNamingSeedRules.Combine',
    'AWLocalizedNameProjectionRules.Select',
    'AWNameGeneratorLibrary.Get'
)) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Localized name service is missing required behavior: $token"
    }
}
if ($localizedNameService -notmatch
    'HistoricalFigureNameRules\.ShouldProtect\s*\(') {
    throw 'Integrated actor naming must bypass localized generation for historical figures.'
}
if ($localizedNameService -notmatch
    'AWActorInitialNameRules\.ResolveGeneratedName\s*\(' -or
    $actorInitialNameRules -notmatch '"given_name"' -or
    $actorInitialNameRules -notmatch '"family_name"' -or
    $actorInitialNameRules -notmatch '"middle_name"') {
    throw 'Actor creation must reduce generated identities to one given name.'
}
$actorCaptureStart = $localizedNameService.IndexOf(
    'private static void PersistActorGeneratedComponents')
$generateValueStart = $localizedNameService.IndexOf(
    'internal static string GenerateValue', $actorCaptureStart)
if ($actorCaptureStart -lt 0 -or $generateValueStart -le $actorCaptureStart) {
    throw 'Actor generated-component capture boundaries are unavailable.'
}
$actorCapture = $localizedNameService.Substring($actorCaptureStart,
    $generateValueStart - $actorCaptureStart)
if ($actorCapture -match 'CHINESE_FAMILY_NAME|FamilyComponent') {
    throw 'Actor creation must not persist a template surname before AW3 creates a family branch.'
}
if ($actorCapture -notmatch 'pSelectedName' -or
    $actorCapture -notmatch 'LineageKeys\.GIVEN_NAME') {
    throw 'Actor creation must persist the selected single name for later family projection.'
}
if ($actorNamePatch -notmatch 'UsesAwLineageSystem\s*\(' -or
    $actorNamePatch -notmatch 'HistoricalFigureService\.TRAIT_FIGURE' -or
    $actorNamePatch -notmatch 'data\.name') {
    throw 'Lineage-managed actors must keep the structured family/shi display name.'
}
$getNameStart = $actorNamePatch.IndexOf('private static void GetName_Postfix')
$historicalStart = $actorNamePatch.IndexOf(
    'private static bool IsHistoricalFigure', $getNameStart)
if ($getNameStart -lt 0 -or $historicalStart -le $getNameStart) {
    throw 'Actor getName projection boundaries are unavailable.'
}
$getNameBody = $actorNamePatch.Substring($getNameStart,
    $historicalStart - $getNameStart)
foreach ($forbiddenWrite in @(
    'LineageService.ApplyDisplayName'
    'AWLocalizedNameService.ProjectActor'
    '.setName('
    '.data.set('
)) {
    if ($getNameBody.Contains($forbiddenWrite)) {
        throw "Actor.getName must be a read-only projection; found $forbiddenWrite"
    }
}
if ($localizedNameService -notmatch
    'LineageKeys\.CHINESE_FAMILY_NAME[\s\S]*LineageKeys\.FAMILY_NAME') {
    throw 'Actor template surnames must preserve chinese_family_name before lineage fallback.'
}
if ($parameterGetters -notmatch
    'founder_family_name[\s\S]*ActorLocalizedNameBoundaryRules\.ResolveTemplateFamily' -and
    $parameterGetters -notmatch
    'ActorLocalizedNameBoundaryRules\.ResolveTemplateFamily[\s\S]*founder_family_name') {
    throw 'Clan template surnames must use the old Chinese-name family boundary instead of preferring AW3 lineage fields.'
}

if ($service -notmatch `
    'bool\s+CommitChineseName\s*\(\s*BaseSystemData\s+pData,\s*' +
    'string\s+pChineseName,\s*string\s+pMetaType,\s*long\s+pObjectId\s*\)' -or
    $service -notmatch `
    'CommitChineseName[\s\S]*return\s+' +
    'AWLocalizedNameMigrationService\.Enqueue\s*\(\s*' +
    'pMetaType,\s*pObjectId,\s*pData\s*\)') {
    throw 'Localized Chinese-name commits must enqueue an exact identity write.'
}
if ($migration -notmatch `
    'bool\s+Enqueue\s*\(\s*string\s+pMetaType,\s*long\s+pObjectId') {
    throw 'The bounded identity write gate must report whether it accepted a commit.'
}
if ($migration -notmatch `
    'while\s*\(remaining\s*>\s*0\)[\s\S]*PendingWrites\.Count\s*>\s*0' -or
    $migration -notmatch `
    'PendingWrites\.Flush[\s\S]*if\s*\(!?_pending\)') {
    throw 'Targeted identity writes must flush before migration reads old rows.'
}
foreach ($method in @('TryRenameKingdom',
        'TryApplyFullyXiaizedKingdomName')) {
    if ($xiaRepair -notmatch ($method +
            '\s*\([^)]*\)\s*\{(?s:.*?)' +
            'AWLocalizedNameService\.CommitChineseName\s*\(')) {
        throw "$method does not commit its localized kingdom identity."
    }
}
if ($xiaRepair -notmatch `
    'TryApplyFullyXiaizedKingdomName\s*\([^)]*\)\s*\{(?s:.*?)' +
    'if\s*\(\s*!AWLocalizedNameService\.CommitChineseName\s*\(' -or
    $xiaRepair -notmatch `
    'CommitChineseName[\s\S]*LineageKeys\.XIA_FULL_NAME_APPLIED') {
    throw 'The level-five applied marker must follow an accepted identity write.'
}
if ($stateNames -match 'LineageKeys\.XIA_FULL_NAME_APPLIED') {
    throw 'State-name projection must not mark full naming before identity persistence accepts it.'
}
$fullRenameStart = $xiaRepair.IndexOf(
    'internal static bool TryApplyFullyXiaizedKingdomName')
$stateApplyStart = $xiaRepair.IndexOf(
    'private static bool TryApplyFullyXiaizedStateName', $fullRenameStart)
if ($fullRenameStart -lt 0 -or $stateApplyStart -le $fullRenameStart) {
    throw 'The full-Xia rename method boundaries are unavailable.'
}
$fullRename = $xiaRepair.Substring($fullRenameStart,
    $stateApplyStart - $fullRenameStart)
$captureIndex = $fullRename.IndexOf(
    'AWLocalizedNameService.CaptureNative(pKingdom.data)')
$stateProjectionIndex = $fullRename.IndexOf(
    'TryApplyFullyXiaizedStateName(pKingdom, preferredName)')
$commitIndex = $fullRename.IndexOf(
    'AWLocalizedNameService.CommitChineseName')
if ($captureIndex -lt 0 -or $stateProjectionIndex -le $captureIndex -or
    $commitIndex -le $stateProjectionIndex) {
    throw 'Full-Xia rename must capture the old native name before state-name projection.'
}

$actorPatch = Get-Content -LiteralPath (Join-Path $repoRoot $required[3]) -Raw
foreach ($token in @('Actor', 'getName')) {
    if ($actorPatch -notmatch [regex]::Escape($token)) {
        throw "Actor localized-name patch is missing hook: $token"
    }
}
if ($actorPatch -match 'generateNewName' -or
    $actorPatch -notmatch '!string\.IsNullOrWhiteSpace\(__instance\.data\.name\)') {
    throw 'Integrated actor naming must retain the old empty-name-only generation boundary.'
}

$worldPatch = Get-Content -LiteralPath (Join-Path $repoRoot $required[4]) -Raw
foreach ($token in @(
    'City', 'Clan', 'Kingdom', 'Culture', 'Language', 'Religion',
    'Subspecies', 'WorldLog', 'WarManager', 'Book', 'ItemManager'
)) {
    if ($worldPatch -notmatch "\b$token\b") {
        throw "World localized-name patches are missing category: $token"
    }
}

foreach ($token in @(
    'AWNameDataKeys.NativeMotto'
    'AWNameDataKeys.ChineseMotto'
    'AWLocalizedMottoProjectionRules.Resolve'
    'AWNameGeneratorLibrary.Get'
    'AWLocalizedNameService.GenerateValue'
    'kingdom_mottos'
    'clan_mottos'
    'alliance_mottos'
)) {
    if ($mottoService -notmatch [regex]::Escape($token)) {
        throw "Localized motto service is missing required behavior: $token"
    }
}
if ($mottoService -notmatch `
    'void\s+CommitEdit\s*\(\s*BaseSystemData\s+pData,\s*' +
    'string\s+pEditedMotto\s*\)[\s\S]*' +
    'AWLocalizedMottoProjectionRules\.ResolveEdit\s*\(') {
    throw 'Localized motto edits must commit through the explicit language slot.'
}
foreach ($hook in @(
    'typeof(Kingdom), nameof(Kingdom.getMotto)'
    'typeof(Clan), nameof(Clan.getMotto)'
    'typeof(Alliance), nameof(Alliance.getMotto)'
)) {
    if ($worldPatch -notmatch [regex]::Escape($hook)) {
        throw "World localized-name patches are missing motto hook: $hook"
    }
}

$mottoEditPatchPath = Join-Path $repoRoot `
    'Code/patch/naming/AW_MottoEditPatch.cs'
if (-not (Test-Path -LiteralPath $mottoEditPatchPath)) {
    throw 'Missing localized motto edit patch.'
}
$mottoEditPatch = Get-Content -LiteralPath $mottoEditPatchPath -Raw
foreach ($hook in @(
    'typeof(KingdomWindow), "applyInputMotto"'
    'typeof(ClanWindow), "applyInputMotto"'
    'typeof(AllianceWindow), "applyInputMotto"'
    'AWLocalizedMottoService.CommitEdit'
)) {
    if ($mottoEditPatch -notmatch [regex]::Escape($hook)) {
        throw "Localized motto edit patch is missing hook: $hook"
    }
}

$refresh = Get-Content -LiteralPath (Join-Path $repoRoot $required[2]) -Raw
foreach ($projection in @(
    'AWLocalizedMottoService.ProjectKingdom'
    'AWLocalizedMottoService.ProjectClan'
    'AWLocalizedMottoService.ProjectAlliance'
)) {
    if ($refresh -notmatch [regex]::Escape($projection)) {
        throw "Bounded language refresh is missing motto projection: $projection"
    }
}

$continuity = Get-Content -LiteralPath (Join-Path $repoRoot `
    'Code/core/lineage/KingdomIdentityContinuityService.cs') -Raw
if ($continuity -notmatch 'AWLocalizedMottoService\.CopyIdentity\s*\(') {
    throw 'Kingdom identity continuity must copy both localized motto slots.'
}
if ($mottoService -notmatch `
    'CopyIdentity[\s\S]*AWNameDataKeys\.NativeMotto[\s\S]*' +
    'AWNameDataKeys\.ChineseMotto[\s\S]*removeString\s*\(') {
    throw 'Localized motto continuity must clear absent source slots exactly.'
}

$languagePatch = Get-Content -LiteralPath (Join-Path $repoRoot $required[5]) -Raw
if ($languagePatch -notmatch 'LocalizedTextManager' -or
    $languagePatch -notmatch 'setLanguage' -or
    $languagePatch -notmatch 'AWLocalizedNameRefreshService\.Request') {
    throw 'Language-change patch does not enqueue a bounded refresh.'
}
if ($languagePatch -match 'World\.world\.(units|cities|kingdoms)') {
    throw 'Language-change patch must not synchronously scan world objects.'
}

$deferredPath = Join-Path $repoRoot 'Code/patch/AW_DeferredRuntimeWorkPatch.cs'
$deferred = Get-Content -LiteralPath $deferredPath -Raw
if ($deferred -notmatch 'AWLocalizedNameRefreshService\.ProcessFrame' -or
    $deferred -notmatch 'AWLocalizedNameRefreshService\.Clear') {
    throw 'Deferred runtime patch must process and clear the naming refresh queue.'
}

Write-Output 'Integrated naming runtime source guard passed.'
