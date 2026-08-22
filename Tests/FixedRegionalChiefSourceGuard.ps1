$ErrorActionPreference = 'Stop'
$models = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CustomCourtTemplateModels.cs"
$rules = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CustomCourtTemplateRules.cs"
$read = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CourtReadModelService.cs"
$appointment = Get-Content -Raw "$PSScriptRoot/../Code/core/court/LocalCourtAppointmentService.cs"
if ($models -notmatch 'CustomCourtOfficeRoleKind') { throw 'missing fixed role kind' }
if ($models -notmatch 'RoleKind') { throw 'missing fixed role field' }
if ($rules -notmatch 'RegionalChief|CommanderyChief') { throw 'missing fixed-role validation' }
if ($appointment -notmatch 'RegionalChief|CommanderyChief|fixed') { throw 'appointment does not protect fixed roles' }
if ($appointment -notmatch 'IsFixedOffice') { throw 'fixed offices are not retained' }
if ($read -notmatch 'commandery-chief|regional-chief') { throw 'missing stable chief node ids' }
Write-Output 'FixedRegionalChief source guard passed'
