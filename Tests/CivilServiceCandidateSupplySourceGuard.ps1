$ErrorActionPreference = 'Stop'

function Read-Source([string]$Path) {
    $fullPath = [System.IO.Path]::Combine($PSScriptRoot, '..', $Path)
    return [System.IO.File]::ReadAllText($fullPath)
}

function Require-Text([string]$Text, [string]$Needle, [string]$Message) {
    if (-not $Text.Contains($Needle)) { throw "Missing $Message" }
}

function Reject-Text([string]$Text, [string]$Needle, [string]$Message) {
    if ($Text.Contains($Needle)) { throw "Found forbidden $Message" }
}

function Require-Order([string]$Text, [string]$First, [string]$Second,
    [string]$Message) {
    $firstIndex = $Text.IndexOf($First)
    $secondIndex = $Text.IndexOf($Second, [Math]::Max(0, $firstIndex + 1))
    if ($firstIndex -lt 0 -or $secondIndex -lt 0 -or
        $firstIndex -ge $secondIndex) { throw "Invalid order for $Message" }
}

function Require-Regex([string]$Text, [string]$Pattern, [string]$Message) {
    if ($Text -notmatch $Pattern) { throw "Missing $Message" }
}

$candidate = Read-Source 'Code/core/court/CivilServiceExamCandidateQuery.cs'
$examService = Read-Source 'Code/core/court/CivilServiceExamService.cs'
$reader = Read-Source 'Code/core/lineage/LineageArchiveReader.cs'
$enrollment = Read-Source 'Code/core/schools/HistoricalSchoolEliteEnrollmentService.cs'
$keys = Read-Source 'Code/core/lineage/LineageKeys.cs'
$court = Read-Source 'Code/core/court/CourtService.cs'
$cityLeader = Read-Source 'Code/patch/AW_CityLeaderPatch.cs'

Reject-Text $candidate 'if (localSelected.Count < CivilServiceExamRules.CandidateLimit)' `
    'local-full gate that starves foreign examinees'
Require-Text $candidate 'CivilServiceExamCandidatePoolQuery.LoadForeignResidents(' `
    'bounded foreign candidate query'
Require-Text $reader 'LoadDeclinedNobles(db,' `
    'bounded declined-noble archive query'
Require-Text $enrollment 'ReadLivingDeclinedNobleActorIds(' `
    'declined-noble annual education source'
Require-Text $enrollment 'HistoricalSchoolElitePriority.DeclinedNoble' `
    'declined-noble education priority'
Require-Text $keys 'SCHOOL_DECLINED_NOBLE_EDUCATION_CURSOR' `
    'independent declined-noble enrollment cursor'
Require-Regex $court `
    '(?s)SchoolGuestOfficeService\.FillVacanciesAfterCivilServiceExam\(\s*pKingdom, pAllowActing: false\);\s*SchoolGuestOfficeService\.FillVacanciesAfterCivilServiceExam\(\s*pKingdom, pAllowActing: true\);[\s\S]*?EnsureMinimumCourt\([^;]+pAllowActing: true\);' `
    'foreign graduates before domestic acting fallback'
Require-Text $court 'AW_CityLeaderPatch.FillVacanciesAfterCivilServiceExam(' `
    'immediate post-ranking city vacancy fill'
Require-Text $cityLeader 'CivilServiceExamRules.CityVacancyFillBudget' `
    'bounded post-ranking city vacancy scan'
Require-Text $examService 'internal static int CandidateTargetForRealm(' `
    'single runtime candidate-target adapter'
Require-Text $examService 'CivilServiceExamRules.CandidateTarget(' `
    'population and vacancy target calculation'
Require-Text $examService 'int candidateTarget = CandidateTargetForRealm(pKingdom);' `
    'foreign invitation dynamic candidate target'
Reject-Text $candidate `
    'int limit = Math.Min(CivilServiceExamRules.SuggestedCandidateTarget,' `
    'fixed twenty-four eligible-candidate count cap'
Require-Text $enrollment `
    'CivilServiceExamService.CandidateTargetForRealm(pKingdom)' `
    'annual education dynamic candidate target'
Require-Regex $enrollment `
    'RealmSuccessfulJoinLimitForExamPipeline\([\s\S]*?eligibleLocalCandidates,\s*candidateTarget\);' `
    'annual education deficit against dynamic target'

Write-Output 'Civil-service candidate supply source guard passed.'
