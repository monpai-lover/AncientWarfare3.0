$ErrorActionPreference = 'Stop'
$models = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CustomCourtTemplateModels.cs"
$rules = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CustomLocalCourtTemplateRules.cs"
$read = Get-Content -Raw "$PSScriptRoot/../Code/core/court/CourtReadModelService.cs"
$resolver = Get-Content -Raw "$PSScriptRoot/../Code/core/court/LocalChiefOfficeResolver.cs"
$appointment = Get-Content -Raw "$PSScriptRoot/../Code/core/court/LocalCourtAppointmentService.cs"
if ($models -notmatch 'public string ChiefOfficeId') { throw 'missing durable local chief identity' }
if ($rules -notmatch 'EnsureChiefOfficeId' -or $rules -notmatch 'ResolveChiefOfficeId') { throw 'missing chief identity validation and migration' }
if ($resolver -notmatch 'string.Equals\(office.Id, chiefOfficeId' -or $resolver -notmatch 'ResolveChiefOfficeId') { throw 'chief office must precede subordinate seats' }
if ($appointment -notmatch 'LocalChiefOfficeResolver.ResolveOrderedSeats' -or $appointment -notmatch 'pVacancy.IsLocalChief') { throw 'appointments must preserve authoritative chief roles' }
if ($read -notmatch 'regional-folder:' -or $read -notmatch 'IsFixedRole = true' -or $read -notmatch 'HistoryOfficeLayer = CourtOfficeLayer.City') { throw 'regional chief must have a stable node backed by the real city office' }
Write-Output 'FixedRegionalChief source guard passed'
