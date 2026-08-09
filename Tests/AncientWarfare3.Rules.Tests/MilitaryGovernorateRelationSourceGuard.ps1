$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\VassalService.cs')

foreach ($token in @(
    'SetMilitaryGovernorate(Kingdom pSubject,',
    'VassalSubjectKind pSubjectKind',
    'ColumnVal.Create("SUBJECT_KIND", (int)pSubjectKind)',
    'public VassalSubjectKind SubjectKind { get; }',
    'subject_kind = VassalSubjectKind.Ordinary',
    'LineageKeys.MILITARY_GOVERNORATE_SUBJECT_KIND'
)) {
    if (-not $service.Contains($token)) {
        throw "Missing military governorate relation integration: $token"
    }
}

$subjectKindReads = ([regex]::Matches($service, 'SELECT[^;]+SUBJECT_KIND',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)).Count
if ($subjectKindReads -lt 4) {
    throw 'Not all active relation read paths restore SUBJECT_KIND.'
}

Write-Output 'Military governorate relation source guard passed.'
