$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $repo $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$source, [string]$needle, [string]$name) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${name}: missing '$needle'")
    }
}

function Reject-Text([string]$source, [string]$needle, [string]$name) {
    if ($source.Contains($needle)) {
        $failures.Add("${name}: forbidden '$needle'")
    }
}

function Reject-Pattern([string]$source, [string]$pattern, [string]$name) {
    if ([Text.RegularExpressions.Regex]::IsMatch(
            $source, $pattern,
            [Text.RegularExpressions.RegexOptions]::Singleline)) {
        $failures.Add("${name}: forbidden pattern '$pattern'")
    }
}

function Require-Pattern([string]$source, [string]$pattern, [string]$name) {
    if (-not [Text.RegularExpressions.Regex]::IsMatch(
            $source, $pattern,
            [Text.RegularExpressions.RegexOptions]::Singleline)) {
        $failures.Add("${name}: missing pattern '$pattern'")
    }
}

$query = Read-Source 'Code/core/court/CivilServiceExamCandidateQuery.cs'
$pool = Read-Source 'Code/core/court/CivilServiceExamCandidatePoolQuery.cs'
$rules = Read-Source 'Code/core/court/CivilServiceExamRules.cs'
$service = Read-Source 'Code/core/court/CivilServiceExamService.cs'
$schoolRuntime = Read-Source 'Code/core/schools/HistoricalSchoolRuntime.cs'
$restorePipeline = Read-Source 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'
$annual = Read-Source 'Code/core/policy/KingdomAnnualWorkService.cs'
$authority = Read-Source 'Code/core/performance/AWAuthorityCycleService.cs'
$restore = Read-Source 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'
$recent = Read-Source 'Code/core/policy/RecentFeatureBenchmarkRules.cs'
$age = Read-Source 'Code/core/policy/UpdateAgeBenchmarkRules.cs'
$chroniclePatch = Read-Source 'Code/patch/AW_ChroniclePatch.cs'

foreach ($required in @(
        'SchoolMembershipTableItem.GetTableName()',
        'ActorArchiveTableItem.GetTableName()',
        'CourtOfficerTableItem.GetTableName()',
        'OfficialCareerStateTableItem.GetTableName()',
        'SchoolInstitutionTableItem.GetTableName()',
        'CivilServiceExamRules.CandidateLimit',
        'CivilServiceExamRules.CandidateSourceLimit',
        'World.world?.units?.get(actorId)',
        'HistoricalSchoolEducationService.IsEducated(',
        'SchoolMembershipService.GetActive(',
        'CivilServiceExamCandidatePoolQuery.LoadLocal(',
        'CivilServiceExamCandidatePoolQuery.LoadForeignResidents(',
        'new CivilServiceExamCandidateFacts(',
        'EducationScore(membership, pYear)',
        'SafeStat(actor, "intelligence")',
        'CivilServiceExamRules.AgeFitness(',
        'pActor.data.get(LineageKeys.LINEAGE_STATUS,',
        'CivilServiceExamRules.ResolveSocialOrigin(',
        'CivilServiceExamRules.SelectCandidatesWithLocalPriority(')) {
    Require-Text $query $required "bounded candidate query $required"
}
foreach ($required in @(
        'CASE WHEN A.STATUS=''noble''',
        'THEN ''declined_noble'' ELSE ''commoner''',
        '@socialOrigin',
        'M.START_YEAR<@year',
        'command.Parameters.AddWithValue("@year",',
        'ORDER BY COALESCE((SELECT',
        'MAX(SR.CYCLE_YEAR)',
        'command.Parameters.AddWithValue("@limit",')) {
    Require-Text $pool $required "bounded candidate pool SQL $required"
}
foreach ($origin in @('NobleOrigin', 'DeclinedNobleOrigin',
        'CommonerOrigin')) {
    Require-Pattern $query `
        "LoadIndexedActorIds\(pKingdomId,\s*CivilServiceExamRules\.${origin},\s*pYear,\s*pMode\)" `
        "bounded candidate query stratified $origin load"
}
Require-Text $query 'CivilServiceExamRules.InterleaveCandidateSources(sources,' `
    'bounded candidate query interleaves all social sources'
Require-Text $rules `
    'CandidateSourceLimit = CandidateLimit * 3' `
    'candidate source limit covers three bounded strata'
Reject-Text $query 'foreach (Actor actor in World.world.units' `
    'candidate query world actor enumeration'
Reject-Text $query 'World.world.units.ToList' `
    'candidate query world actor materialization'
Reject-Text $query 'ChronicleGate.IsNobleActor(pActor)' `
    'candidate query lineage membership is not current noble status'
Reject-Text $query 'LoadIndexedActorIds(pKingdom.id);' `
    'candidate query old unstratified source load'
Reject-Pattern $query `
    "ORDER\s+BY\s+CASE\s+WHEN\s+A\.STATUS\s*=\s*'noble'\s+THEN\s+0" `
    'candidate query old noble-first global ordering'
Reject-Pattern $query `
    'AddWithValue\(\s*"@limit"\s*,\s*CivilServiceExamRules\.CandidateLimit\s*\)' `
    'candidate query old CandidateLimit global truncation binding'

foreach ($required in @(
        'CivilServiceExamCandidateTableItem.GetTableName()',
        'CivilServiceExamSessionTableItem.GetTableName()')) {
    Require-Text $query $required `
        "candidate query supplies qualification history table $required"
}
foreach ($required in @(
        'S.STATUS=''completed''',
        'E.QUALIFICATION=''jinshi''',
        '@tribute',
        'command.Parameters.AddWithValue("@tribute",')) {
    Require-Text $pool $required `
        "candidate source excludes equivalent host qualification before limit $required"
}
Require-Pattern $pool `
    "NOT EXISTS\s*\(SELECT 1 FROM\s*" `
    'candidate source qualification exclusion is applied inside SQL'

Require-Pattern $schoolRuntime `
    'public static void LoadState\(\)\s*\{\s*SchoolMembershipService\.LoadIndexes\(\);' `
    'school runtime load atomically rebuilds membership indexes'
Reject-Text $restorePipeline 'new AW3RestoreStage("school_indexes"' `
    'school runtime membership indexes are not rebuilt by a duplicate restore stage'

foreach ($required in @(
        'public static void OnKingdomYear(Kingdom pKingdom)',
        'public static void ProcessAuthorityCycle()',
        'CivilServiceExamRules.AuthorityCandidateBudget',
        'CivilServiceExamRules.ShouldOpenCandidateRoll(candidates.Count)',
        'CivilServiceExamRules.EmptyCandidateRetryDays',
        'CivilServiceExamRules.FinalAdmissionQuota(',
        'CivilServiceExamRules.StageCapacity(',
        'CivilServiceExamRules.AdmissionQuotaForStage(',
        'CivilServiceExamRules.BuildAiRanking(',
        'LineageKeys.COURT_DOMINANT_SCHOOL',
        'RulerExamAbility(pKingdom.king)',
        'CivilServiceExamPersistence.LoadFinalRankingCandidates(',
        'CourtService.FillVacanciesAfterCivilServiceExam(',
        'CivilServiceExamPersistence.LoadDueSession(',
        'CivilServiceExamCandidateQuery.Build(',
        'public static void ClearRuntime()',
        'public static void RebuildRuntime()')) {
    Require-Text $service $required "exam runtime $required"
}
Reject-Text $service 'foreach (Actor actor in World.world.units' `
    'exam runtime world actor enumeration'
Reject-Text $service 'update.Qualification = "jinshi"' `
    'palace scoring cannot bypass vacancy-limited ranking'

$rankingStart = $service.IndexOf(
    'public static bool TrySubmitPlayerRanking(',
    [System.StringComparison]::Ordinal)
$rankingEnd = if ($rankingStart -ge 0) {
    $service.IndexOf('private static void ProcessScheduled(', $rankingStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$rankingSource = if ($rankingStart -ge 0 -and $rankingEnd -gt $rankingStart) {
    $service.Substring($rankingStart, $rankingEnd - $rankingStart)
} else { '' }
Require-Text $rankingSource 'session.Mode != "imperial_exam"' `
    'player ranking validates the frozen session mode'
Reject-Text $rankingSource 'MandateService.IsMandateKingdom(kingdom)' `
    'player ranking must not reclassify a frozen sitting from current mandate'
Reject-Text $rankingSource 'KingdomTitleService.IsEmperor(kingdom)' `
    'player ranking must not reclassify a frozen sitting from current title'

Require-Text $annual 'CivilServiceExamService.OnKingdomYear(pKingdom);' `
    'annual examination scheduling'
Require-Text $authority 'CivilServiceExamService.ProcessAuthorityCycle' `
    'authority examination progression'
Require-Text $authority 'CivilServiceExamService.ClearRuntime();' `
    'authority examination reset'
Require-Text $restore 'CivilServiceExamService.RebuildRuntime' `
    'save-load active examination recovery'
Require-Text $restore 'CivilServiceQualificationService.RebuildRuntimeProjections' `
    'save-load qualification projection recovery'
Require-Text $service 'public static void OnKingdomDestroying(Kingdom pKingdom)' `
    'kingdom destruction cancels active examination sessions immediately'
Require-Text $service 'CancelActiveSessionForKingdom' `
    'kingdom destruction uses a kingdom-scoped cancellation transaction'
Require-Text $chroniclePatch 'CivilServiceExamService.OnKingdomDestroying(pKingdom);' `
    'kingdom destruction hook reaches the examination service'

foreach ($required in @(
        'CivilServiceExamAnnualIndex',
        'CivilServiceExamRuntimeIndex',
        'aw3_year_civil_service_exam',
        'aw3_runtime_civil_service_exam')) {
    Require-Text $recent $required "recent benchmark $required"
}
foreach ($required in @(
        'KingdomCivilServiceExamIndex',
        'aw3_kingdom_civil_service_exam')) {
    Require-Text $age $required "annual benchmark $required"
}

$forbiddenFiles = @(
    'Code/patch/AW_DeferredRuntimeWorkPatch.cs',
    'Code/patch/AW_AgePatch.cs',
    'Code/ui/windows/CourtWindow.cs')
foreach ($relativePath in $forbiddenFiles) {
    $source = Read-Source $relativePath
    Reject-Text $source 'CivilServiceExamService.ProcessAuthorityCycle' `
        "$relativePath direct exam progression"
    Reject-Text $source 'CivilServiceExamCandidateQuery.Build' `
        "$relativePath candidate discovery"
}

if ($failures.Count -gt 0) {
    Write-Host "Civil service exam runtime guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Civil service exam runtime source guard passed.'
