$ErrorActionPreference = 'Stop'
$models = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CustomCourtTemplateModels.cs"
$runtime = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CustomCourtRuntime.cs"
$codec = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CustomCourtTemplateJsonCodec.cs"
if ($models -notmatch 'CustomCourtTemplateScope') { throw 'missing template scope' }
if ($models -notmatch 'Scope') { throw 'missing scope property' }
if ($runtime -notmatch 'TryApplyCentral') { throw 'missing central apply' }
if ($runtime -notmatch 'TryApplyLocal') { throw 'missing local apply' }
if ($runtime -notmatch 'TryApplyCombined') { throw 'missing combined apply' }
if ($runtime -notmatch 'TryMigrateLocal') { throw 'local migration missing' }
if ($codec -notmatch 'scope|Scope') { throw 'scope is not serialized' }
if ($runtime -match 'TryApplyCentral[\s\S]{0,1800}RequestImmediateReconcile') { throw 'central apply still reconciles local cities' }
Write-Output 'CustomCourtScopeIsolation source guard passed'
