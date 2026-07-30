$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        $failures.Add("missing source file: $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$source, [string]$needle, [string]$label) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${label}: missing '$needle'")
    }
}

$scope = Read-Source `
    'Code/core/lineage/WarParticipantEntrySourceScope.cs'
$service = Read-Source `
    'Code/core/lineage/WarParticipantEntrySourceService.cs'
$warPatch = Read-Source 'Code/patch/AW_WarPatch.cs'
$coalition = Read-Source `
    'Code/core/lineage/DiplomaticCoalitionService.cs'
$proposal = Read-Source `
    'Code/core/lineage/DiplomacyProposalService.cs'
$vassal = Read-Source 'Code/core/lineage/VassalService.cs'
$mandate = Read-Source 'Code/core/lineage/MandateRebelService.cs'
$jingnan = Read-Source 'Code/core/lineage/FeudatoryJingnanService.cs'

foreach ($needle in @('public static WarParticipantEntrySourceScope Open(',
        'TryCurrent(', 'WarParticipantEntrySourceKind')) {
    Require-Text $scope $needle "join provenance scope $needle"
}

foreach ($needle in @('TryCanJoinWar(', 'TryReadActiveSourceFingerprint(',
        'TryEndAllActiveSources(', 'TryEndAllActiveSourcesForWar(',
        'QueuePendingClosure(', 'QueuePendingWarClosure(',
        'CREATED_TIME<=@ended')) {
    Require-Text $service $needle "entry-source service $needle"
}

foreach ($needle in @('RecordMainBelligerents(',
        'WarParticipantEntrySourceKind.MainBelligerent',
        'WarParticipantEntrySourceScope.TryCurrent(',
        'WarParticipantEntrySourceService.Instance.TryCanJoinWar(',
        'WarParticipantEntrySourceService.Instance.TryRecordSource(',
        'WarParticipantLifecycleRules.ShouldRollbackJoin(',
        'WarParticipantLifecycleRules.RequiresDurableJoinSource(',
        'private static bool TryRollbackJoin(',
        'return !pWar.hasKingdom(pKingdom);',
        'private static void QueueRollbackJoin(',
        'DeferredWorkClass.CriticalRuntime',
        'WarParticipantLifecycleRules.ShouldQueueRollbackRepair(',
        'WarParticipantLifecycleRules.ShouldNotifyRollbackDeparture(',
        'private static bool TryIsKingdomInWar(',
        'QueueRollbackJoin(pWarId, pKingdomId, pDefender,',
        'remainsOnSideAfterRemove:',
        'WarParticipantEntrySourceService.Instance.TryEndAllActiveSources(',
        'WarParticipantEntrySourceService.Instance.' +
            'TryEndAllActiveSourcesForWar(')) {
    Require-Text $warPatch $needle "native war boundary $needle"
}

if ($warPatch.Contains('throw new InvalidOperationException(' +
        '"War join provenance rollback still active: war="')) {
    $failures.Add('critical rollback repair must requeue itself instead of ' +
        'depending on the generic two-attempt exception retry')
}

Require-Text $coalition 'WarParticipantEntrySourceScope.Open(' `
    'coalition alliance join carries provenance'
Require-Text $coalition 'WarParticipantEntrySourceKind.AllianceCall' `
    'coalition join is recorded as an alliance call'

Require-Text $proposal 'WarParticipantEntrySourceScope.Open(' `
    'accepted join-war proposal carries provenance'
Require-Text $proposal 'WarParticipantEntrySourceKind.AllianceCall' `
    'accepted join-war proposal is recorded as an alliance call'

Require-Text $vassal 'WarParticipantEntrySourceKind.FormalVassalObligation' `
    'vassal obligation join carries its direct source kind'
Require-Text $vassal 'WarParticipantEntrySourceScope.Open(' `
    'vassal join enters through the shared provenance scope'

foreach ($source in @($mandate, $jingnan)) {
    Require-Text $source 'WarParticipantEntrySourceKind.ScriptedJoin' `
        'scripted rebellion join carries provenance'
    Require-Text $source 'WarParticipantEntrySourceScope.Open(' `
        'scripted rebellion uses the shared join scope'
}
Require-Text $mandate 'WarParticipantEntrySourceKind.AllianceCall' `
    'mandate rebel ally join is recorded as an alliance call'

if ($failures.Count -gt 0) {
    throw "War participant entry-source guard failures:`n - " +
          ($failures -join "`n - ")
}

Write-Output 'War participant entry-source guards passed.'
