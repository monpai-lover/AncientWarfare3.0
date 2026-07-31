$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$conversation = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/DiplomacyConversationService.cs'))
$window = [IO.File]::ReadAllText((Join-Path $root `
    'Code/ui/windows/DiplomacyConversationWindow.cs'))
$rows = Import-Csv -Encoding UTF8 (Join-Path $root `
    'Locales/aw3_diplomacy.csv')

$requiredKeys = @(
    'aw_diplomacy_detail_vassalize_demand',
    'aw_diplomacy_detail_vassalize_seek',
    'aw_diplomacy_detail_vassalize_internalize',
    'aw_diplomacy_detail_end_vassal_release',
    'aw_diplomacy_detail_end_vassal_request',
    'aw_diplomacy_failure_join_war_stale',
    'aw_diplomacy_failure_protector_war_conflict',
    'aw_diplomacy_failure_protector_too_weak',
    'aw_diplomacy_failure_protector_relations_low',
    'aw_diplomacy_failure_protection_threat_stale',
    'aw_diplomacy_failure_protection_war_entry',
    'aw_diplomacy_failure_internalization_target',
    'aw_diplomacy_failure_internalization_write',
    'aw_diplomacy_failure_internalization_title',
    'aw_diplomacy_failure_internalization_rebel',
    'aw_diplomacy_failure_alliance_truce_write',
    'aw_diplomacy_failure_invalid_vassalize_direction',
    'aw_diplomacy_failure_invalid_end_vassal_direction'
)

foreach ($key in $requiredKeys) {
    $matches = @($rows | Where-Object { $_.key -eq $key })
    if ($matches.Count -ne 1) {
        throw "localization key '$key' must occur exactly once."
    }
    $row = $matches[0]
    foreach ($language in @('cz', 'en', 'ch')) {
        if ([string]::IsNullOrWhiteSpace($row.$language) -or
            $row.$language -eq $key) {
            throw "localization key '$key' is missing '$language'."
        }
    }
}

foreach ($detail in @(
    'VassalizeDemandDetail',
    'VassalizeSeekDetail',
    'VassalizeInternalizeDetail',
    'EndVassalReleaseDetail',
    'EndVassalRequestDetail')) {
    if (-not $conversation.Contains($detail)) {
        throw "directional conversation routing missing '$detail'."
    }
}
if (-not $conversation.Contains('ShouldAppendProposalSubject')) {
    throw 'proposal replies do not preserve their directional subject.'
}

foreach ($reason in @(
    'join_war_stale',
    'protector_war_conflict',
    'protector_too_weak',
    'protector_relations_low',
    'protection_threat_stale',
    'protection_war_entry_failed',
    'internalization_target',
    'source_relation_missing',
    'source_relation_ambiguous',
    'conversion_database_unavailable',
    'conversion_table_invalid',
    'conversion_target_invalid',
    'conversion_write_failed',
    'target_title_too_high',
    'vassal_cycle',
    'rebel_blocked',
    'alliance_truce_write_failed',
    'invalid_vassalize_direction',
    'invalid_end_vassal_direction')) {
    if (-not $window.Contains('"' + $reason + '"')) {
        throw "proposal failure mapping missing '$reason'."
    }
}

Write-Output 'AI diplomacy proposal localization source guards passed.'
