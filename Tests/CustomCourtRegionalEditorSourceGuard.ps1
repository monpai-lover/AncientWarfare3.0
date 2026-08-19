$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$workflow = Get-Content -Raw (Join-Path $root 'Code\ui\windows\CustomCourtWorkflowWindow.cs')
$card = Get-Content -Raw (Join-Path $root 'Code\ui\components\CourtWorkflowVacancyCard.cs')
foreach ($token in @('regional_government_layer','RegionTitleInput','GovernorTitleInput','ManagementOfficeIds','IsRegionalLayerCard')) {
    if (-not ($workflow.Contains($token) -or $card.Contains($token))) { throw "Missing editor token: $token" }
}
if (-not $workflow.Contains('_root.anchoredPosition = new Vector2(-530f, 0f)')) { throw 'Local editor offset missing' }
if (-not $workflow.Contains('_root.anchoredPosition = new Vector2(-510f, 0f)')) { throw 'Central-only +20 px offset missing' }
Write-Output 'PASS: custom court regional editor'
