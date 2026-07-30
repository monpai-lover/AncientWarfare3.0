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

$defs = Read-Source 'Code/content/policies/KingdomPolicyDefs.cs'
$order = Read-Source 'Code/core/policy/KingdomPolicyTechOrderRules.cs'
$ai = Read-Source 'Code/core/policy/KingdomPolicyAI.cs'
$query = Read-Source 'Code/core/court/CivilServiceExamCandidateQuery.cs'
$locale = Read-Source 'Locales/aw3_court.csv'

foreach ($required in @(
        'Id = "aw_tech_civil_service_examination"',
        'NameKey = "aw_tech_civil_service_examination"',
        'DescKey = "aw_tech_civil_service_examination_desc"',
        'Cost = 90f',
        'RequiredTechs = new[] { "aw_tech_nine_rank_system" }',
        'Column = 3',
        'Row = 3')) {
    Require-Text $defs $required "examination technology $required"
}

$nineRankIndex = $order.IndexOf('"aw_tech_nine_rank_system"')
$examIndex = $order.IndexOf('"aw_tech_civil_service_examination"')
$departmentsIndex = $order.IndexOf('"aw_tech_three_departments"')
if ($nineRankIndex -lt 0 -or $examIndex -le $nineRankIndex -or
    $departmentsIndex -le $examIndex) {
    $failures.Add('technology order must place examination after Nine-Rank and before Three Departments')
}
Require-Text $order 'if (pId == "aw_tech_civil_service_examination") return pNineRankCompleted;' `
    'AI only considers examination after Nine-Rank'

$departmentMatch = [regex]::Match($defs,
    'Id = "aw_tech_three_departments"(?<block>.*?)(?=new KingdomPolicyDef|\z)',
    [Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $departmentMatch.Success) {
    $failures.Add('Three Departments technology definition is missing')
} elseif ($departmentMatch.Groups['block'].Value.Contains(
        'aw_tech_civil_service_examination')) {
    $failures.Add('Three Departments must continue to depend on Nine-Rank, not examinations')
}

foreach ($required in @(
        'case "aw_tech_civil_service_examination":',
        'CountCivilServiceVacancies(',
        'CivilServiceExamCandidateQuery.',
        'CountEducatedWithoutQualification(pKingdom, 32)',
        'KingdomPolicyTechOrderRules.',
        'CivilServiceExaminationContextScore(vacancies,',
        'KingdomTitle.Emperor',
        'MandateService.IsMandateKingdom(')) {
    Require-Text $ai $required "context-aware examination AI $required"
}
Reject-Text $ai 'return int.MaxValue' 'examination AI cannot force absolute priority'
Reject-Text $ai 'World.world.units' 'examination AI cannot scan every actor'

foreach ($required in @(
        'public static int CivilServiceExaminationContextScore(',
        'Math.Min(',
        'CountEducatedWithoutQualification(',
        'SELECT DISTINCT M.ACTOR_ID',
        'LIMIT @limit',
        'SchoolMembershipTableItem.GetTableName()',
        'CivilServiceExamCandidateTableItem.',
        'GetTableName();')) {
    $target = if ($required -like '*ContextScore*' -or $required -eq 'Math.Min(') {
        $order
    } else {
        $query
    }
    Require-Text $target $required "bounded examination research pressure $required"
}

$simplifiedName = ([char]0x8D21) + ([char]0x4E3E) +
    ([char]0x5236) + ([char]0x5EA6)
$traditionalName = ([char]0x8CA2) + ([char]0x8209) +
    ([char]0x5236) + ([char]0x5EA6)
$localizedRow = 'aw_tech_civil_service_examination,' + $simplifiedName +
    ',Civil Service Examinations,' + $traditionalName
Require-Text $locale $localizedRow `
    'examination technology localization'
Require-Text $locale 'aw_tech_civil_service_examination_desc,' `
    'examination technology description localization'

if ($failures.Count -gt 0) {
    Write-Host "Civil service exam policy guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Civil service exam policy source guard passed.'
