$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/DiplomacyProposalService.cs'))

function Require([string]$name, [string[]]$needles) {
    foreach ($needle in $needles) {
        if (-not $service.Contains($needle)) {
            throw "$name missing '$needle'."
        }
    }
}

Require 'prepared candidates carry war identity' @(
    'private sealed class PreparedAiProposal',
    'public long WarId = -1L;')
Require 'ordinary join-war candidate exists' @(
    'TryBuildOrdinaryAiProposals',
    'TryPrepareJoinWarCandidate')
Require 'readonly join-war candidate exists' @(
    'TryBuildOrdinaryAiProposalsReadOnly',
    'TryPrepareJoinWarCandidateReadOnly')
Require 'prepared creation uses selected war' @(
    'TryCreatePreparedOrdinary',
    'prepared.WarId')
Require 'async commit uses selected war' @(
    'TryCommitAsyncProposal',
    'currentSelected.WarId')

Write-Output 'Ordinary diplomacy proposal source guards passed.'
