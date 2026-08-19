$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$codec = Get-Content -Raw (Join-Path $root 'Code\core\court\CustomCourtTemplateJsonCodec.cs')
$models = Get-Content -Raw (Join-Path $root 'Code\core\court\CustomCourtTemplateModels.cs')
$runtime = Get-Content -Raw (Join-Path $root 'Code\core\court\CustomCourtRuntime.cs')
$document = Get-Content -Raw (Join-Path $root 'Code\core\court\CustomCourtTemplateDocumentRules.cs')
foreach ($pair in @(
    @($models, 'LocalLevelTitle'),
    @($codec, 'EnsureRegionalLayer'),
    @($codec, 'Prefecture'),
    @($codec, 'ManagementOfficeIds'),
    @($runtime, 'RegionalTitles'),
    @($document, 'RegionalGovernmentLayer = null')
)) {
    if (-not $pair[0].Contains($pair[1])) {
        throw "Regional template guard missing: $($pair[1])"
    }
}
if ($codec -match 'LocalLevelTitle\.Chinese\s*=\s*pLayer\.LocalLevelTitle\.English') {
    throw 'Legacy normalization must preserve independent language values'
}
Write-Output 'Regional government template source guard PASS'
