$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $RelativePath) {
    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required source: $RelativePath"
    }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8
}

function Remove-CSharpCommentsAndLiterals([string] $Source) {
    $tokenPattern = @'
(?ms)(@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|//[^\r\n]*|/\*.*?\*/)
'@.Trim()
    return [regex]::Replace($Source, $tokenPattern, ' ')
}

function Assert-PositiveTemplateWeight($Template, [string] $Label) {
    if ($null -eq $Template -or $null -eq $Template.weight) {
        throw "$Label must define a positive numeric weight."
    }

    $weightText = [Convert]::ToString($Template.weight,
        [Globalization.CultureInfo]::InvariantCulture)
    [double] $weight = 0.0
    $parsed = [double]::TryParse($weightText,
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref] $weight)
    if (-not $parsed -or $weight -le 0.0 -or
        [double]::IsNaN($weight) -or [double]::IsInfinity($weight)) {
        throw "$Label must define a positive numeric weight."
    }
}

$clanGenerators = Read-Source `
    'name_generators/default/clans.json' | ConvertFrom-Json
$rawGeneratorId = 'orc_nomadic_family_stem'
$rawGenerators = @($clanGenerators |
    Where-Object { [string]$_.id -eq $rawGeneratorId })
if ($rawGenerators.Count -ne 1) {
    throw "$rawGeneratorId must be defined exactly once."
}

$rawGenerator = $rawGenerators[0]
$templates = @($rawGenerator.templates)
if ($templates.Count -eq 0) {
    throw "$rawGeneratorId must provide at least one primary template."
}
$fallbackTemplate = $rawGenerator.default_template
$fallbackFormat = [string]$fallbackTemplate.format
$orcFamilyFormat = '{' + ([string][char]0x517D) +
    ([string][char]0x4EBA) + ([string][char]0x59D3) +
    ([string][char]0x6C0F) + ':family_name}'
$nomadicFamilyFormat = '{' + ([string][char]0x6E38) +
    ([string][char]0x7267) + ([string][char]0x59D3) +
    ([string][char]0x6C0F) + ':family_name}'
if (-not [string]::Equals($fallbackFormat, $orcFamilyFormat,
        [System.StringComparison]::Ordinal)) {
    throw "$rawGeneratorId must keep the independent orc family fallback."
}
Assert-PositiveTemplateWeight $fallbackTemplate `
    "$rawGeneratorId default template"

$allTemplates = @($fallbackTemplate) + $templates
$hasNomadicPrimary = $false
for ($index = 0; $index -lt $allTemplates.Count; $index++) {
    $template = $allTemplates[$index]
    $format = [string]$template.format
    if ([string]::IsNullOrWhiteSpace($format)) {
        throw "$rawGeneratorId contains an empty format."
    }
    foreach ($forbidden in @(
        ([string][char]0x90E8) + ([string][char]0x843D),
        ([string][char]0x5BB6) + ([string][char]0x65CF),
        '#' + ([string][char]0x90E8) + ([string][char]0x843D) + '#')) {
        if ($format.Contains($forbidden)) {
            throw "$rawGeneratorId must stay suffix-free: $format"
        }
    }
    if ($format -notmatch '^\{[^{}:]+:family_name\}$') {
        throw "$rawGeneratorId must emit one raw family-name component: $format"
    }
    if ($index -gt 0) {
        Assert-PositiveTemplateWeight $template `
            "$rawGeneratorId primary template $index"
    }
    if ($index -gt 0 -and [string]::Equals($format,
            $nomadicFamilyFormat,
            [System.StringComparison]::Ordinal)) {
        $hasNomadicPrimary = $true
    }
}
if (-not $hasNomadicPrimary) {
    throw "$rawGeneratorId must provide a positive nomadic family-name primary template."
}

$completeGenerators = @($clanGenerators |
    Where-Object { [string]$_.id -eq 'orc_nomadic_clan' })
if ($completeGenerators.Count -ne 1) {
    throw 'The complete orc_nomadic_clan heading generator must remain defined.'
}

$orcRules = Read-Source 'Code/core/naming/AWOrcNomadicNamingRules.cs'
if ($orcRules -notmatch
        'FamilyStemGeneratorId\s*=\s*"orc_nomadic_family_stem"') {
    throw 'AWOrcNomadicNamingRules must expose the stable raw-stem generator id.'
}
if ($orcRules -notmatch
        'AWNamingObjectKind\.Clan\s*=>\s*"orc_nomadic_clan"') {
    throw 'The existing complete orc clan heading route must remain unchanged.'
}
foreach ($reverseParsing in @(
    'Replace\s*\(', 'Remove\s*\(', 'Substring\s*\(',
    'Regex', 'TrimEnd\s*\(')) {
    if ($orcRules -match $reverseParsing) {
        throw "Orc raw stems must not be recovered from headings: $reverseParsing"
    }
}

$admissionRules = Read-Source `
    'Code/core/lineage/WesternLineageAdmissionRules.cs'
$executableAdmissionRules =
    Remove-CSharpCommentsAndLiterals $admissionRules

$allowedUsingPattern =
    '(?m)^\s*using\s+AncientWarfare3\.core\.naming\s*;\s*$'
$withoutAllowedUsing = [regex]::Replace($executableAdmissionRules,
    $allowedUsingPattern, ' ')
if ([regex]::IsMatch($withoutAllowedUsing, '\busing\b')) {
    throw 'Western admission rules may only import the naming namespace.'
}

$allowedStaticDeclarationPattern =
    '\b(?:public|internal|private|protected)\s+static\s+(?:class\s+[A-Za-z_][A-Za-z0-9_]*|[A-Za-z_][A-Za-z0-9_.<>,?\[\]\s]*\s+[A-Za-z_][A-Za-z0-9_]*\s*\()'
$withoutAllowedStaticDeclarations = [regex]::Replace(
    $executableAdmissionRules, $allowedStaticDeclarationPattern, ' ')
if ([regex]::IsMatch($withoutAllowedStaticDeclarations, '\bstatic\b') -or
    [regex]::IsMatch($executableAdmissionRules,
        '(?m)^\s*(?:public|internal|private|protected)\s+const\b')) {
    throw 'Western admission rules must not declare static runtime fields.'
}

foreach ($forbidden in @(
    '\bActor\b', '\bKingdom\b', '\bCity\b', '\bUnity\w*\b',
    '\bDB\w*\b', '\bSystem\b', '\bIO\b', '\bDateTime\b',
    '\bRandom\b', '\bEnvironment\b', '\bAssetManager\b',
    '\bLineageService\b', '\bWorld\b',
    '\b(?:Thread|thread)\w*\b', '\b(?:Task|task)\w*\b',
    '\block\b', '\bUsesAwLineageSystem\b')) {
    if ([regex]::IsMatch($executableAdmissionRules, $forbidden)) {
        throw "Western admission rules must stay pure: $forbidden"
    }
}

$persistence = Read-Source `
    'Code/core/lineage/WesternLineageAdmissionPersistence.cs'
$service = Read-Source `
    'Code/core/lineage/WesternLineageAdmissionService.cs'
$lineageService = Read-Source 'Code/core/lineage/LineageService.cs'
$promotionPatch = Read-Source 'Code/patch/AW_PromotionPatch.cs'
$chroniclePatch = Read-Source 'Code/patch/AW_ChroniclePatch.cs'
$familyTreeWindow = Read-Source 'Code/ui/windows/FamilyTreeWindow.cs'

foreach ($token in @(
    'BeginTransaction'
    'INSERT INTO " + LineageGroup'
    'INSERT INTO " + ShiBranch'
    'UpsertActor'
    'transaction.Commit()'
    'transaction?.Rollback()')) {
    if ($persistence -notmatch [regex]::Escape($token)) {
        throw "Western admission persistence is missing: $token"
    }
}
foreach ($token in @(
    'WesternLineageAdmissionRules.Resolve'
    'WesternLineageAdmissionPersistence.TryCommit'
    'AWCultureNamingTraditionService.ResolveForActor'
    'WesternFamilyIdentityRules.ProjectBranch'
    'SynchronizeOriginalClan'
    'WesternFamilyIdentityRules.BuildHeading'
    'World.world?.clans?.newClan'
    'pActor.setClan'
    'clan.setName'
    'AWLocalizedNameService.CommitChineseName'
    'LineageService.ArchiveActor')) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Western admission runtime service is missing: $token"
    }
}
if ($service -notmatch
        'private\s+static\s+bool\s+SynchronizeOriginalClan\s*\(') {
    throw 'Vanilla-clan synchronization must report a verified result.'
}
if ($service -notmatch
        'ResolveOriginalClanSync\s*\(\s*pIdentity\.Profile\s*,\s*pRuler') {
    throw 'Vanilla-clan synchronization must pass the ruler boundary into the pure rule.'
}
foreach ($verification in @(
    'ReferenceEquals\s*\(\s*pActor\.clan\s*,\s*clan\s*\)'
    'string\.Equals\s*\(\s*clan\.data\.name\s*,\s*heading')) {
    if ($service -notmatch $verification) {
        throw "Vanilla-clan synchronization is missing postcondition verification: $verification"
    }
}
$syncMatch = [regex]::Match($service,
    '(?s)private\s+static\s+bool\s+SynchronizeOriginalClan\s*\(.*?(?=\r?\n\s*private\s+static\s+Clan\s+FindParentClan)')
if (-not $syncMatch.Success) {
    throw 'SynchronizeOriginalClan must remain an inspectable runtime boundary.'
}
$syncSource = $syncMatch.Value
if ($syncSource -notmatch
        '(?s)WesternOriginalClanSyncAction\.CreateClan.*?CaptureValidClanIds\s*\(\s*\).*?newClan\s*\(.*?catch\s*\(\s*Exception\s+[A-Za-z_][A-Za-z0-9_]*\s*\).*?ModClass\.LogWarning.*?\.Message.*?FindUniqueNewFounderClan\s*\(\s*pActor\s*,\s*clanIdsBefore') {
    throw 'Clan creation must snapshot valid ids before newClan and recover only from the post-exception delta.'
}
if ($syncSource -notmatch
        '(?s)bool\s+createdForActor\s*=.*?!clanIdsBefore\.Contains\s*\(\s*clan\.data\.id\s*\).*?clan\.data\.founder_actor_id\s*==\s*pActor\.data\.id.*?if\s*\(\s*!createdForActor\s*\)\s*return false') {
    throw 'The returned or recovered Clan must itself verify as a new id founded by this actor.'
}
if ($syncSource -notmatch
        '(?s)ReferenceEquals\s*\(\s*pActor\.kingdom\?\.king\s*,\s*pActor\s*\).*?trySetRoyalClan\s*\(\s*\).*?royal_clan_id') {
    throw 'Successful synchronization must refresh and verify the actual king royal clan id.'
}
$snapshotMatch = [regex]::Match($service,
    '(?s)private\s+static\s+HashSet<long>\s+CaptureValidClanIds\s*\(.*?(?=\r?\n\s*private\s+static\s+Clan\s+FindUniqueNewFounderClan)')
if (-not $snapshotMatch.Success -or
    $snapshotMatch.Value -notmatch 'candidate\?\.data\s*!=\s*null' -or
    $snapshotMatch.Value -notmatch '\.Add\s*\(\s*candidate\.data\.id\s*\)') {
    throw 'The pre-call snapshot must contain ids from valid vanilla Clan objects only.'
}
$recoverableMatch = [regex]::Match($service,
    '(?s)private\s+static\s+Clan\s+FindUniqueNewFounderClan\s*\(.*?(?=\r?\n\s*private\s+static\s+Clan\s+FindParentClan)')
if (-not $recoverableMatch.Success -or
    $recoverableMatch.Value -notmatch
        '!pClanIdsBefore\.Contains\s*\(\s*candidate\.data\.id\s*\)' -or
    $recoverableMatch.Value -notmatch
        'founder_actor_id\s*==\s*pActor\.data\.id' -or
    $recoverableMatch.Value -notmatch
        'candidateCount\s*==\s*1\s*\?\s*match\s*:\s*null') {
    throw 'Exception recovery must require exactly one new-id founder match.'
}
if ($service -match 'FindRecoverableFounderClan' -or
    $recoverableMatch.Value -match
        '\.First\s*\(|FirstOrDefault\s*\(|return\s+candidate\s*;' -or
    $recoverableMatch.Value -match
        'removeObject\s*\(|Dispose\s*\(|delete\w*\s*\(') {
    throw 'Founder recovery must not reuse an old/first match, guess, or delete Clan objects.'
}
if ($service -notmatch
        '(?s)SynchronizeOriginalClan\s*\(.*?ModClass\.LogWarning') {
    throw 'A failed vanilla-clan synchronization must emit a warning without rolling back admission.'
}
$parentClanMatch = [regex]::Match($service,
    '(?s)private\s+static\s+Clan\s+FindParentClan\s*\(.*?(?=\r?\n\s*private\s+static\s+FamilyBranchIdentityProjection)')
if (-not $parentClanMatch.Success) {
    throw 'FindParentClan must remain an inspectable runtime-object boundary.'
}
if ($parentClanMatch.Value -match '\.isRekt\s*\(') {
    throw 'A dead parent with a retained hot Actor and valid Clan must remain reusable.'
}
if ($parentClanMatch.Value -match 'LineageArchiveReader|ActorArchive') {
    throw 'Archive-only parent ids cannot be guessed back into runtime Clan objects.'
}
if ($service -notmatch '(?s)TryCommit\s*\(.*?if\s*\(\s*!result\.Success\s*\)\s*return false;.*?pActor\.data\.set') {
    throw 'Actor hot identity must be published only after persistence succeeds.'
}
foreach ($entry in @(
    'OnActorPromoted'
    'EnsureRoyalHeirLineage'
    'EnsureOfficialShiAndClan')) {
    if ($lineageService -notmatch ("(?s)" + $entry +
            ".*?WesternLineageAdmissionService\.TryEnsure")) {
        throw "$entry does not admit western lineage roles."
    }
}
if ($lineageService -notmatch '(?s)ApplyDisplayName.*?WesternFamilyIdentityRules\.BuildActor') {
    throw 'Live display projection does not consume persisted western family identity.'
}
if ($promotionPatch -notmatch
        'WesternLineageAdmissionRules\.ShouldRunKingAdmission') {
    throw 'Western setKing admission must include generated/load-style kings.'
}
if ($promotionPatch -notmatch
        '(?s)SetKing_Postfix.*?WesternLineageMigrationService\.Request\(__instance\)') {
    throw 'Every actual accession must queue a stable-state western lineage reconciliation.'
}
if ($chroniclePatch -notmatch
        '(?s)MakeNewCivKingdom_Postfix.*?WesternLineageMigrationService\.Request\(__result\)') {
    throw 'Each newly founded civilization kingdom must refresh western lineage migration.'
}
if ($familyTreeWindow -notmatch
        'FamilyTreeMaterializationRules\s*\.ShouldUseSynchronousFallback') {
    throw 'Family-tree loading must fall back when the historical reader never becomes ready.'
}

Write-Output 'Western lineage admission source guard passed.'
