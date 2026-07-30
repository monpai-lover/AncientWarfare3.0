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

function Require-Pattern([string]$source, [string]$pattern, [string]$label) {
    if (-not [regex]::IsMatch($source, $pattern,
            [Text.RegularExpressions.RegexOptions]::Singleline)) {
        $failures.Add("${label}: missing pattern '$pattern'")
    }
}

$proposal = Read-Source `
    'Code/core/db/WarPeaceSettlementProposalTableItem.cs'
$participant = Read-Source `
    'Code/core/db/WarPeaceSettlementParticipantTableItem.cs'
$entry = Read-Source `
    'Code/core/db/WarParticipantEntrySourceTableItem.cs'
$indexes = Read-Source 'Code/core/db/LineageArchiveIndexRules.cs'
$store = Read-Source 'Code/core/lineage/WarPeaceSettlementStore.cs'
$models = Read-Source 'Code/core/lineage/WarPeaceSettlementModels.cs'
$entryService = Read-Source `
    'Code/core/lineage/WarParticipantEntrySourceService.cs'
$territory = Read-Source 'Code/core/lineage/WarTerritoryService.cs'

foreach ($field in @('scope_kind', 'exit_root_kingdom_id')) {
    Require-Text $proposal $field "settlement proposal $field"
}
Require-Text $proposal '[TableItemDef(pDefaultValue: "coalition")]' `
    'legacy proposals default to coalition scope'
Require-Text $proposal '[TableItemDef(pDefaultValue: "-1")]' `
    'legacy proposals default to no exit root'

foreach ($field in @('proposal_id', 'kingdom_id', 'side_kind',
        'participant_role', 'exit_parent_id', 'vassal_relation_id',
        'entry_source_kind', 'entry_source_fingerprint',
        'included_in_exit_group')) {
    Require-Text $participant $field "settlement participant $field"
}

foreach ($field in @('entry_id', 'war_id', 'kingdom_id', 'source_kind',
        'source_kingdom_id', 'active', 'created_time', 'ended_time')) {
    Require-Text $entry $field "war entry source $field"
}

foreach ($index in @('idx_WarParticipantEntry_war_kingdom_active',
        'uq_WarParticipantEntry_active_source',
        'idx_WarPeaceParticipant_proposal_included',
        'uq_WarPeaceParticipant_proposal_kingdom',
        'idx_WarPeaceTerm_proposal_position')) {
    Require-Text $indexes $index "participant persistence index $index"
}

Require-Text $models 'WarPeaceSettlementScopeKind Scope' `
    'draft and proposal carry settlement scope'
Require-Text $models 'long ExitRootKingdomId' `
    'draft and proposal carry exit root'
Require-Text $models 'List<WarPeaceSettlementParticipantSnapshot>' `
    'proposal carries participant snapshot'
Require-Text $models 'string EntrySourceFingerprint' `
    'participant snapshot carries the complete source-set fingerprint'
Require-Text $models 'WarPeaceSettlementTerm term = terms[i].Clone();' `
    'proposal owns independent settlement term copies'

foreach ($required in @('TryRecordSource(', 'INSERT OR IGNORE INTO ',
        'TryEndSource(', 'SET ACTIVE=0,ENDED_TIME=@ended',
        'ReadActiveSources(', 'LIMIT @limit',
        'TryMarkSeparatePeaceExit(', 'TryHasSeparatePeaceExit(',
        'TryCanJoinWar(', 'TryReadActiveSourceFingerprint(',
        'TryReadSeparatePeaceExit(',
        'HasSeparatePeaceExit(', 'separate_peace_exit')) {
    Require-Text $entryService $required `
        "war participant entry source service $required"
}
Require-Text $entryService 'BeginTransaction(IsolationLevel.Serializable)' `
    'entry source id allocation uses an immediate transaction'
Require-Text $entryService 'TableIdAllocator.Next(db, transaction,' `
    'entry source id allocation is transaction-bound'
Require-Text $models 'TryHasExecutedCoalitionSettlement(' `
    'store exposes fallible coalition completion lookup'
Require-Text $models 'TryReadExecutedCoalitionTerms(' `
    'store exposes fallible coalition term read'
Require-Text $store 'TryHasExecutedCoalitionSettlement(' `
    'coalition completion lookup is try-style'
Require-Text $store 'TryReadExecutedCoalitionTerms(' `
    'coalition terms lookup is try-style'
Require-Text $store 'BeginTransaction(IsolationLevel.Serializable)' `
    'settlement id allocation uses an immediate transaction'
Require-Text $store 'TableIdAllocator.Next(db, transaction,' `
    'settlement id allocation is transaction-bound'
Require-Pattern $store `
    'private static bool ReadParticipants[\s\S]*?WarPeaceSettlementParticipantTableItem\.GetTableName\(\)[\s\S]*?LIMIT @limit[\s\S]*?MaximumParticipantsPerProposal \+ 1' `
    'settlement participant reads use an overflow probe'
Require-Text $store 'draft.Participants.Count > MaximumParticipantsPerProposal' `
    'settlement creation rejects excessive participant snapshots'
Require-Pattern $entryService `
    'public bool HasSeparatePeaceExit[\s\S]*?return !TryHasSeparatePeaceExit\(pWarId, pKingdomId,[\s\S]*?out bool exited\) \|\| exited;' `
    'legacy separate-peace query fails closed'
Require-Text $store 'TryReadExecutedCoalitionProposalId(' `
    'coalition term reads resolve one authoritative proposal'
Require-Text $store 'WHERE PROPOSAL_ID=@proposal' `
    'coalition terms are restricted to the authoritative proposal'
Require-Text $store 'MaximumExecutedCoalitionTerms + 1' `
    'coalition term reads use an overflow probe'
Require-Text $territory 'TryHasExecutedCoalitionSettlement(' `
    'war goal completion observes coalition lookup failure'
Require-Text $territory 'TryReadExecutedCoalitionTerms(' `
    'war goal completion observes coalition term read failure'
Require-Text $territory 'out bool hasExecutedSettlement)) return;' `
    'war goal completion defers on coalition lookup failure'
Require-Text $territory 'out IReadOnlyList<WarPeaceSettlementTerm>' `
    'war goal completion captures coalition terms'
Require-Text $territory 'executedTerms)) return;' `
    'war goal completion defers on coalition term read failure'

Require-Text $store 'InsertParticipant(' `
    'store inserts participant snapshots'
Require-Text $store 'ReadParticipants(' `
    'store restores participant snapshots'
Require-Text $store 'SCOPE_KIND' `
    'store persists settlement scope'
Require-Text $store 'EXIT_ROOT_KINGDOM_ID' `
    'store persists exit root'
Require-Text $store 'HasExecutedCoalitionSettlement(' `
    'coalition completion lookup ignores separate peace'
Require-Text $store 'ReadExecutedCoalitionTerms(' `
    'final war goals read only coalition terms'

if ($failures.Count -gt 0) {
    throw "War peace participant persistence guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'War peace participant persistence source guards passed.'
