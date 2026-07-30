param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

& (Join-Path $PSScriptRoot 'WarGoalSettlementPersistenceSourceGuard.ps1')
& (Join-Path $PSScriptRoot 'WarGoalCreationLifecycleSourceGuard.ps1')
& (Join-Path $PSScriptRoot 'WarGoalRuntimeSettlementSourceGuard.ps1')
& (Join-Path $PSScriptRoot 'VassalOccupationAttributionSourceGuard.ps1')
& (Join-Path $PSScriptRoot 'WarExhaustionSettlementSourceGuard.ps1')
& (Join-Path $PSScriptRoot 'EnclosedUnownedZoneSourceGuard.ps1')
& (Join-Path $PSScriptRoot 'EmptyCitySurvivalSourceGuard.ps1')
& (Join-Path $PSScriptRoot 'EmptyCitySurvivalRulesTests.ps1')
& (Join-Path $PSScriptRoot 'ArmyMapInformationMinimapSourceGuardTests.ps1')
& (Join-Path $PSScriptRoot 'CityReservePoolLifecycleSourceGuardTests.ps1')
& (Join-Path $PSScriptRoot 'CityReserveRecruitmentSourceGuardTests.ps1')
& (Join-Path $PSScriptRoot 'ReplacementArmyCommandSourceGuardTests.ps1')
& (Join-Path $PSScriptRoot 'ReserveExhaustionPersistenceSourceGuardTests.ps1')
& (Join-Path $PSScriptRoot 'ConscriptionLawSourceGuardTests.ps1')
& (Join-Path $PSScriptRoot `
    'ArmyReplenishmentOperationPersistenceSourceGuardTests.ps1')
& (Join-Path $PSScriptRoot `
    'ArmyReplenishmentOperationSourceGuardTests.ps1')

$enclosedZoneRulesProject = Join-Path $PSScriptRoot `
    'EnclosedUnownedZoneRulesTests.csproj'
& dotnet run --project $enclosedZoneRulesProject
if ($LASTEXITCODE -ne 0) {
    throw "Enclosed unowned Zone rule tests failed with exit code " +
        $LASTEXITCODE + "."
}

$warGoalLifecycleProject = if ([string]::IsNullOrWhiteSpace(
        $env:AW3_WAR_GOAL_LIFECYCLE_TEST_PROJECT)) {
    Join-Path $PSScriptRoot 'WarGoalCreationLifecycleTests.csproj'
} else {
    $env:AW3_WAR_GOAL_LIFECYCLE_TEST_PROJECT
}
& dotnet run --project $warGoalLifecycleProject
if ($LASTEXITCODE -ne 0) {
    throw "War goal creation lifecycle tests failed with exit code " +
        $LASTEXITCODE + "."
}

$sqliteMigrationProject = if ([string]::IsNullOrWhiteSpace(
        $env:AW3_SQLITE_MIGRATION_TEST_PROJECT)) {
    Join-Path $PSScriptRoot 'SQLiteHelperSchemaMigrationTests.csproj'
} else {
    $env:AW3_SQLITE_MIGRATION_TEST_PROJECT
}
& dotnet run --project $sqliteMigrationProject
if ($LASTEXITCODE -ne 0) {
    throw "SQLite helper schema migration tests failed with exit code " +
        $LASTEXITCODE + "."
}

function Read-Source([string]$relativePath) {
    return [System.IO.File]::ReadAllText((Join-Path $root $relativePath))
}

function Require-Absent([string]$name, [string]$relativePath, [string]$needle) {
    $fullPath = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        return
    }
    $text = [System.IO.File]::ReadAllText($fullPath)
    if ($text.Contains($needle)) {
        $failures.Add("${name}: found forbidden text '$needle' in $relativePath")
    }
}

function Require-Present([string]$name, [string]$relativePath, [string]$needle) {
    $fullPath = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        $failures.Add("${name}: missing source file $relativePath")
        return
    }
    $text = [System.IO.File]::ReadAllText($fullPath)
    if (-not $text.Contains($needle)) {
        $failures.Add("${name}: missing required text '$needle' in $relativePath")
    }
}

function Require-OccurrenceCount([string]$name, [string]$relativePath,
    [string]$needle, [int]$expectedCount) {
    $fullPath = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        $failures.Add("${name}: missing source file $relativePath")
        return
    }
    $text = [System.IO.File]::ReadAllText($fullPath)
    $count = 0
    $offset = 0
    while (($offset = $text.IndexOf($needle, $offset,
            [System.StringComparison]::Ordinal)) -ge 0) {
        $count++
        $offset += $needle.Length
    }
    if ($count -ne $expectedCount) {
        $failures.Add("${name}: expected $expectedCount occurrences of '$needle' in $relativePath, found $count")
    }
}

function Require-FileAbsent([string]$name, [string]$relativePath) {
    $fullPath = Join-Path $root $relativePath
    if ([System.IO.File]::Exists($fullPath)) {
        $failures.Add("${name}: obsolete source file still exists: $relativePath")
    }
}

function Require-CsvHeader([string]$name, [string]$relativePath,
    [string]$expectedHeader) {
    $fullPath = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        $failures.Add("${name}: missing CSV file $relativePath")
        return
    }
    $text = [System.IO.File]::ReadAllText($fullPath)
    $firstLine = ($text -split "`r?`n", 2)[0].TrimStart([char]0xFEFF)
    if ($firstLine -cne $expectedHeader) {
        $failures.Add("${name}: expected header '$expectedHeader' in $relativePath, found '$firstLine'")
    }
}

Get-ChildItem (Join-Path $root 'Locales') -Filter 'aw3_*.csv' -File |
    ForEach-Object {
        $relativeLocale = 'Locales/' + $_.Name
        Require-CsvHeader "AW3 locale CSV $($_.Name)" $relativeLocale `
            'key,cz,en,ch'
    }

$nmlVisibleTestSources = @(
    git -C $root -c core.quotepath=false ls-files -- Tests |
        Where-Object {
            $_.EndsWith('.cs', [System.StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)
        }
)
if ($nmlVisibleTestSources.Count -gt 0) {
    $failures.Add('NML-visible test sources must use .cs.txt: ' + ($nmlVisibleTestSources -join ', '))
}

$productionSources = Get-ChildItem (Join-Path $root 'Code') -Recurse -Filter '*.cs' -File
foreach ($sourceFile in $productionSources) {
    $sourceText = [System.IO.File]::ReadAllText($sourceFile.FullName)
    $relativeSource = $sourceFile.FullName.Substring($root.Length + 1).Replace('\', '/')
    if ([regex]::IsMatch($sourceText,
            '(?i)(?:\.data\.name\s*=|\.setName\s*\()\s*"name"')) {
        $failures.Add("meta objects cannot receive the literal Name placeholder: $relativeSource")
    }
    if ($sourceText.Contains('EnsureWorldNames')) {
        $failures.Add("production source cannot retain a whole-world name repair: $relativeSource")
    }
    if ($sourceText.Contains('MandateRulerTitle')) {
        $failures.Add("production source cannot retain the legacy Mandate ruler-title authority: $relativeSource")
    }
    if ($sourceText.Contains('RepairMissingTempleTitles')) {
        $failures.Add("production source cannot repair missing temple titles while reading: $relativeSource")
    }
    if ($sourceText.Contains('RepairFirstOrdinaryEmperorDisplayTitle')) {
        $failures.Add("production source cannot repair committed ruler titles while displaying: $relativeSource")
    }
}

Require-Absent 'generic meta-window name patch' 'Code/patch/AW_WorldLogGuardPatch.cs' 'WindowMetaGeneric<War'
Require-Absent 'generic meta-window helper' 'Code/patch/AW_WorldLogGuardPatch.cs' 'MetaWindowSafetyRules'
Require-Absent 'kingdom display-time name repair' 'Code/patch/AW_KingdomWindowPatch.cs' 'nameInput.setText(dataName)'
Require-Absent 'load-time world name scan' 'Code/patch/AW_SavePatch.cs' 'XiaNamingRepair.EnsureWorldNames()'
Require-Present 'new Xia kingdoms reject vanilla names before first-ruler binding' `
    'Code/content/XiaNamingRepair.cs' 'XiaPreQinKingdomNameRules.IsKnown(pKingdom.data.name)'
Require-Present 'Xia kingdom creation refreshes appellations after direct Chinese Name writes' `
    'Code/patch/AW_XiaNamingPatch.cs' 'RulerAppellationService.RefreshLivingProjection(__instance);'
Require-Present 'central power resize repositions the close control' `
    'Code/ui/windows/CentralPowerWindow.cs' 'BackgroundTransform?.parent?.Find("CloseBackground")'
Require-Present 'central power resize repositions the title background' `
    'Code/ui/windows/CentralPowerWindow.cs' 'BackgroundTransform?.Find("TitleBackground")'
Require-Present 'central power resize updates the native scroll viewport' `
    'Code/ui/windows/CentralPowerWindow.cs' 'BackgroundTransform?.Find("Scroll View")'
Require-Present 'central power title does not block window dragging' `
    'Code/ui/windows/CentralPowerWindow.cs' 'window.titleText.raycastTarget = false'
Require-Present 'central power window has a localized native title' `
    'Locales/aw3_centralization.csv' 'aw_central_power Title,'
Require-Present 'feudatory window has a localized native title' `
    'Locales/aw3_mandate.csv' 'aw_feudatories Title,'
Require-Present 'Mandate cycle owns a dedicated window id' `
    'Code/ui/AW_LineageWindowIds.cs' 'MANDATE_CYCLE = "aw_mandate_cycle"'
Require-Present 'Mandate history exposes the cycle entry' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'filter_key = "mandate_cycle"'
Require-Present 'Mandate history opens the dedicated cycle window' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'MandateCycleWindow.Open();'
Require-Present 'Mandate cycle uses the shared resizable chrome' `
    'Code/ui/windows/MandateCycleWindow.cs' 'WideWindowChrome.Attach('
Require-Present 'Mandate cycle uses the requested default size' `
    'Code/ui/windows/MandateCycleWindow.cs' 'new Vector2(580f, 360f)'
Require-Present 'Mandate cycle uses the requested minimum size' `
    'Code/ui/windows/MandateCycleWindow.cs' 'new Vector2(420f, 280f)'
Require-Present 'Mandate cycle native title is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_cycle Title,'
$phasePoliticalClarity = 'aw_mandate_phase_golden,' +
    [char]0x653F + [char]0x6CBB + [char]0x6E05 + [char]0x660E + ','
$phaseTerritorialExpansion = 'aw_mandate_phase_renewal,' +
    [char]0x5F00 + [char]0x7586 + [char]0x62D3 + [char]0x571F + ','
$phasePoliticalTension = 'aw_mandate_phase_decline,' +
    [char]0x5C40 + [char]0x52BF + [char]0x7D27 + [char]0x5F20 + ','
$phaseWarringContenders = 'aw_mandate_phase_chaos,' +
    [char]0x7FA4 + [char]0x96C4 + [char]0x5272 + [char]0x636E + ','
Require-Present 'political clarity phase is localized' `
    'Locales/aw3_mandate.csv' $phasePoliticalClarity
Require-Present 'territorial expansion phase is localized' `
    'Locales/aw3_mandate.csv' $phaseTerritorialExpansion
Require-Present 'political tension phase is localized' `
    'Locales/aw3_mandate.csv' $phasePoliticalTension
Require-Present 'warring contenders phase is localized' `
    'Locales/aw3_mandate.csv' $phaseWarringContenders
Require-Present 'manual appointment window has a localized native title' `
    'Locales/aw3_court.csv' 'aw_court_appointment Title,'
Require-Absent 'custom tab native sprite overwrite' 'Code/ui/AW_LineageTab.cs' 'ApplyNativeTabSprites'
Require-Absent 'custom tab selected sprite overwrite' 'Code/ui/AW_LineageTab.cs' 'tab_main.image_selected'
Require-Absent 'custom tab keeps the selected skin cloned by NML' `
    'Code/ui/AW_LineageTab.cs' 'tab.image_selected ='
Require-Present 'custom tab dividers retain the native line prefix' `
    'Code/ui/AW_LineageTab.cs' 'divider.name = "_line_aw3_" + pFollowingGroupId;'
Require-Present 'custom tab layout delegates final placement to vanilla' `
    'Code/ui/AW_LineageTab.cs' 'pTab.sortButtons();'
Require-Absent 'custom tab does not recalculate width before PowersTab Start' `
    'Code/ui/AW_LineageTab.cs' 'pTab.recalc();'
Require-Absent 'custom tab does not place groups with NML-relative manual coordinates' `
    'Code/ui/AW_LineageTab.cs' 'pTab.PutElement('
Require-Present 'diplomacy UI and AI share one assessment entry point' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'public static DiplomacyActionAssessment Assess('
Require-Present 'diplomacy proposals persist a response due timestamp' `
    'Code/core/db/DiplomacyProposalTableItem.cs' 'public double response_due_time = -1;'
Require-Present 'diplomacy proposal writes include the response due timestamp' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'ColumnVal.Create("RESPONSE_DUE_TIME", responseDueTime)'
Require-Absent 'player diplomacy cannot enqueue an immediate same-frame AI reply' `
    'Code/core/lineage/DiplomacyProposalService.cs' '() => EvaluateAndRespond(proposalId)'
Require-Present 'diplomacy due responses use a one-row bounded query' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'private static long FindDuePendingProposal(double pNow)'
Require-Present 'the bounded frame dispatcher processes delayed diplomacy replies' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'DiplomacyProposalService.ProcessFrame'
Require-Present 'AI response consumes the shared diplomacy assessment' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'DiplomacyActionAssessment assessment = Assess('
Require-Present 'diplomacy window uses the requested default size' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' 'new Vector2(580f, 360f)'
Require-Present 'diplomacy action list exposes a visible scrollbar' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' 'CreateVerticalScrollbar('
Require-Present 'diplomacy country entries render the real kingdom flag' `
    'Code/ui/items/DiplomacyKingdomListItem.cs' 'KingdomFlagBuilder.Build('
Require-Present 'completed diplomacy proposals synthesize a separate response event' `
    'Code/core/lineage/DiplomacyConversationService.cs' 'EventType = "proposal_response"'
Require-Present 'proposal response timestamps use the responder snapshot' `
    'Code/core/lineage/DiplomacyConversationService.cs' 'pEvent.IsProposalResponse ? pEvent.Proposal?.ResponseYearPrefix'
Require-Present 'diplomacy bubbles render the real speaker kingdom flag' `
    'Code/ui/items/DiplomacyBubbleItem.cs' 'KingdomFlagBuilder.Build('
Require-Present 'diplomacy bubble flag rebinding is cached by speaker signature' `
    'Code/ui/items/DiplomacyBubbleItem.cs' 'if (_boundFlagSignature != signature)'
Require-Present 'diplomacy conversations remain bounded after response expansion' `
    'Code/core/lineage/DiplomacyConversationService.cs' 'TrimExpandedEvents(result, eventLimit);'
Require-Present 'diplomacy native window title is localized before first frame' `
    'Locales/aw3_diplomacy.csv' 'aw_diplomacy_conversations Title,'
Require-Present 'diplomacy locale uses WorldBox simplified and traditional language ids' `
    'Locales/aw3_diplomacy.csv' 'key,cz,en,ch'
Require-Present 'diplomacy exposes one normalized opinion service' `
    'Code/core/lineage/DiplomacyOpinionService.cs' 'public static int Read(Kingdom pMain, Kingdom pTarget)'
Require-Present 'diplomacy list displays normalized opinion' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' 'DiplomacyOpinionService.Read(pBase, pOther)'
Require-Present 'diplomacy AI scores normalized opinion' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'DiplomacyOpinionService.Read(pResponder, pRequester)'
Require-Present 'diplomatic letters use normalized opinion tone' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'DiplomacyOpinionService.Read(pSpeaker, pRecipient)'
Require-Present 'diplomacy availability uses precise pure-rule reasons' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'DiplomacyProposalRules.UnavailableReason(pType, context.Availability)'
Require-Present 'diplomatic subject requests reuse the live vassal preflight' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'VassalService.CanSetVassal(pResponder, pRequester, out subjectFailure)'
Require-Present 'diplomatic alliances preflight vanilla alliance membership rules' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'AllianceExecutionFailure(pRequester, pResponder)'
Require-Present 'failed diplomatic effects record the proposal and exact stage' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'Diplomacy response execution rejected:'
Require-Present 'reserved diplomacy blocks duplicate pair proposals' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    "STATUS IN ('pending','processing')"
Require-Present 'reserved diplomacy records durable recovery time' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    "SET STATUS='processing',RESPONSE_YEAR=@year,"
Require-Present 'reserved diplomacy has a bounded recovery loop' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'private static bool RecoverProcessingProposal(long pProposalId)'
Require-Present 'successful diplomacy defers finalization without false failure' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'Diplomacy proposal finalization deferred:'
Require-Present 'diplomatic response persistence catches and records close failures' `
    'Code/core/lineage/DiplomacyProposalService.cs' 'Diplomacy proposal close failed:'
Require-Present 'non-aggression relationship label is localized' `
    'Locales/aw3_diplomacy.csv' 'aw_diplomacy_relation_non_aggression,'
Require-Present 'non-Mandate tribute failure is localized' `
    'Locales/aw3_diplomacy.csv' 'aw_diplomacy_failure_requires_mandate,'
Require-Present 'duplicate non-aggression failure is localized' `
    'Locales/aw3_diplomacy.csv' 'aw_diplomacy_failure_active_non_aggression,'
Require-Present 'diplomacy declaration uses the dedicated war window' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' 'DiplomaticWarDeclarationWindow.Open('
Require-Present 'war-reason window projects existing target status rows' `
    'Code/ui/windows/WarDecisionTargetWindow.cs' `
    'stats = BuildStats(report, "");'
Require-Present 'dedicated war declaration submits an authoritative multiplayer command' `
    'Code/ui/windows/DiplomaticWarDeclarationWindow.cs' 'AW3CommandRequest.DeclareWar(attacker.id,'
Require-Present 'authoritative war command executes through the declaration service' `
    'Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs' `
    'DiplomaticWarDeclarationService.Issue(attacker,'
Require-Absent 'court appointment history does not store a bare rank connector key' `
    'Code/core/lineage/ChronicleEvents.cs' 'H("aw_hist_court_rank_mid")'
Require-Present 'court appointment history supplies a localized rank connector fallback' `
    'Code/core/lineage/ChronicleEvents.cs' '", at official rank ")'
Require-Present 'court appointment history resolves the rank name through locale' `
    'Code/core/lineage/ChronicleEvents.cs' 'AW_L10n.Text(rankKey,'
Require-Present 'wide window resize handle uses an icon sprite' `
    'Code/ui/components/WideWindowChrome.cs' 'ui/icons/iconArrowMetaRight'
Require-Present 'AW3 lazy window creation captures the visible current window' `
    'Code/patch/AW_WindowCreationPatch.cs' 'ScrollWindow.getCurrentWindow()'
Require-Present 'AW3 lazy window creation restores only when the registry was cleared' `
    'Code/patch/AW_WindowCreationPatch.cs' 'AWWindowCreationRules.ShouldRestoreCurrent('
Require-Present 'AW3 lazy window repair restores without adding window history' `
    'Code/patch/AW_WindowCreationPatch.cs' '__state.setActive(true, pSkipAnimation: true);'
Require-Present 'wide window resize target is twenty-six pixels' `
    'Code/ui/components/WideWindowChrome.cs' '_resizeHandle.sizeDelta = new Vector2(26f, 26f);'
Require-Present 'wide window resize affordance has an inset icon' `
    'Code/ui/components/WideWindowChrome.cs' '"WideWindowResizeIcon"'
Require-Present 'wide window resize tooltip is localized' `
    'Locales/aw3_diplomacy.csv' 'aw_window_resize_desc,'
Require-Absent 'wide window resize handle is not a generated orange rectangle' `
    'Code/ui/components/WideWindowChrome.cs' 'image.sprite = WhiteSprite();'
Require-Present 'official rank snapshots begin with the Nine-rank technology' `
    'Code/core/court/OfficialCareerService.cs' 'RankAtAppointment = CourtService.HasNineRankSystem(pKingdom)'
Require-Present 'annual rank evaluation is gated by the Nine-rank technology' `
    'Code/core/court/OfficialCareerStateService.cs' 'if (termDue && nineRankSystem &&'
Require-Present 'centralization nominal level key' `
    'Code/core/lineage/LineageKeys.cs' 'CENTRALIZATION_LEVEL'
Require-Present 'centralization reform cooldown key' `
    'Code/core/lineage/LineageKeys.cs' 'CENTRALIZATION_REFORM_READY_YEAR'
Require-Present 'centralization chaos epoch key' `
    'Code/core/lineage/LineageKeys.cs' 'CENTRALIZATION_LAST_CHAOS_EPOCH'
Require-Present 'annual vassal tribute marker key' `
    'Code/core/lineage/LineageKeys.cs' 'VASSAL_TRIBUTE_LAST_YEAR'
Require-Present 'direct vassal count cache key' `
    'Code/core/lineage/LineageKeys.cs' 'VASSAL_DIRECT_COUNT'
Require-Present 'war obligation decision key' `
    'Code/core/lineage/LineageKeys.cs' 'VASSAL_OBLIGATION_DECISIONS'
Require-Present 'centralization has one Mandate-decision completion entry' `
    'Code/core/lineage/CentralizationService.cs' 'public static bool TryCompleteMandateReform('
Require-Present 'Mandate decisions define centralization reform' `
    'Code/core/lineage/MandateDecisionService.cs' 'aw_mandate_decision_centralize_1'
Require-Present 'Mandate decision completion delegates to centralization service' `
    'Code/core/lineage/MandateDecisionService.cs' 'CentralizationService.TryCompleteMandateReform('
Require-Absent 'centralization reform cannot spend ordinary political points' `
    'Code/core/lineage/CentralizationService.cs' 'TrySpendPoliticalPoints('
Require-Present 'phase transition notifies centralization' `
    'Code/core/lineage/MandatePhaseService.cs' 'CentralizationService.OnPhaseChanged(previous, pPhase, pYear);'
Require-Absent 'chaos downgrade does not scan actors' `
    'Code/core/lineage/CentralizationService.cs' 'getUnits()'
Require-Present 'policy points have one bounded spend helper' `
    'Code/core/policy/KingdomPolicyService.cs' 'public static bool TrySpendPoliticalPoints('
Require-Present 'policy points have one bounded transfer helper' `
    'Code/core/policy/KingdomPolicyService.cs' 'public static float TransferPoliticalPoints('
Require-Present 'Mandate declarations consume the shared rites gate' `
    'Code/core/lineage/MandateService.cs' 'MandateRitesService.CanDeclare('
Require-Absent 'legacy policy-only Mandate declaration gate is removed' `
    'Code/core/lineage/MandateDeclarationRules.cs' 'CanStartOrdinaryDeclaration('
Require-Present 'Mandate sacrifice reuses the capital temple check' `
    'Code/core/lineage/MandateSacrificeService.cs' 'MandateRitesService.HasUsableCapitalTemple('
Require-Present 'King-to-Emperor promotion consumes the shared rites gate' `
    'Code/core/policy/KingdomPolicyService.cs' 'MandateRitesService.CanPromoteToEmperor('
Require-Present 'policy tooltip reads one rites snapshot' `
    'Code/ui/windows/KingdomPolicyWindow.cs' 'MandateRitesService.ReadSnapshot('
Require-Absent 'policy tooltip cannot read raw permanent rites points' `
    'Code/ui/windows/KingdomPolicyWindow.cs' 'MANDATE_RITUAL_COMPLETENESS'
Require-Present 'Mandate dynasty UI reads one rites snapshot' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'MandateRitesService.ReadSnapshot('
Require-Absent 'Mandate dynasty UI cannot read raw permanent rites points' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'MANDATE_RITUAL_COMPLETENESS'
Require-Present 'pathfinder exposes one-lookup ready cursor' `
    'Code/core/pathfinding/AWPathFinder.cs' 'public readonly struct ReadyPathCursor'
Require-Present 'path lifecycle rules centralize request and retry decisions' `
    'Code/core/pathfinding/AWPathLifecycleRules.cs' 'public readonly struct AWPathRequestKey'
Require-Absent 'path stream no longer locks every state read and step write' `
    'Code/core/pathfinding/AWPathStream.cs' '_stateGate'
Require-Present 'pathfinder opens ready cursor once' `
    'Code/core/pathfinding/AWPathFinder.cs' 'public AWPathPollResult OpenReadyCursor('
Require-Present 'pathfinder exposes Cultiway-style allocation-free active reuse' `
    'Code/core/pathfinding/AWPathFinder.cs' 'public bool TryReuse(long pActorId,'
Require-Absent 'pathfinder reuse does not count an entire concurrent path queue' `
    'Code/core/pathfinding/AWPathFinder.cs' 'existing.Request.Stream.Count > 0'
Require-Present 'smooth movement retains one ready cursor' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'AWPathFinder.ReadyPathCursor customPathCursor = default;'
Require-Present 'smooth movement continues through retained cursor' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'ContinuePathMovementFromSmooth(pActor, ref customPathCursor);'
Require-Present 'path movement classifies fast-step blockers' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'GetFastMoveBlockReason('
Require-Present 'ordinary ground movement uses Cultiway fast step' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'FastMoveTo(pActor, tile, adjacentStep);'
Require-Present 'special ground movement replays vanilla side effects' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'FastMoveToWithMoveToSideEffects(pActor, tile, adjacentStep);'
Require-Present 'fast movement maintains actor movement batch' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'SetMoveStepTile('
Require-Present 'fast movement preserves tile step actions' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'ApplyStepActionForCurrentTile('
$pathMovementBridge = Read-Source 'Code/core/pathfinding/AWPathMovementBridge.cs'
$pathSubmit = ''
$pathSubmitStart = $pathMovementBridge.IndexOf(
    'private static ExecuteEvent SubmitCore(', [System.StringComparison]::Ordinal)
$pathSubmitEnd = $pathMovementBridge.IndexOf(
    'public static void Update(', $pathSubmitStart,
    [System.StringComparison]::Ordinal)
if ($pathSubmitStart -lt 0 -or $pathSubmitEnd -lt 0) {
    $failures.Add('path submit transaction source boundaries are missing')
}
else {
    $pathSubmit = $pathMovementBridge.Substring($pathSubmitStart,
        $pathSubmitEnd - $pathSubmitStart)
    $acceptIndex = $pathSubmit.IndexOf(
        'accepted = finder.Request(request, out reused);',
        [System.StringComparison]::Ordinal)
    $mutationIndex = if ($acceptIndex -ge 0) {
        $pathSubmit.IndexOf('pActor.clearOldPath();', $acceptIndex,
            [System.StringComparison]::Ordinal)
    } else { -1 }
    if ($acceptIndex -lt 0 -or $mutationIndex -lt 0 -or
        $acceptIndex -gt $mutationIndex) {
        $failures.Add('path requests must be accepted before actor movement state is mutated')
    }
}
Require-Present 'lost custom requests release stale movement ownership' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'ReleaseStaleOwnership(pActor)'
$pathReleaseStart = $pathMovementBridge.IndexOf(
    'private static void ReleaseStaleOwnership(',
    [System.StringComparison]::Ordinal)
$pathReleaseEnd = $pathMovementBridge.IndexOf(
    'private static void MarkRetryProgress(', $pathReleaseStart,
    [System.StringComparison]::Ordinal)
if ($pathReleaseStart -lt 0 -or $pathReleaseEnd -lt 0) {
    $failures.Add('path ownership release source boundaries are missing')
}
elseif ($pathMovementBridge.Substring($pathReleaseStart,
        $pathReleaseEnd - $pathReleaseStart).Contains(
        'pActor.beh_tile_target = null;')) {
    $failures.Add('completed custom paths must preserve the behaviour tile target for follow-up work')
}
Require-Absent 'lost custom requests are never resubmitted from ordinary request metadata' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'TryRecoverMissingRequest(pActor)'
Require-Present 'path retries resolve only the live actor target' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'WorldTile target = pActor.tile_target;'
Require-Present 'only bounded pending retries retain missing-request ownership' `
    'Code/core/pathfinding/AWPathLifecycleRules.cs' `
    'return hasRetryContext && retryPending;'
Require-Absent 'path recovery cannot resurrect a cleared cached target' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'target = tiles[context.TargetTileId];'
Require-Present 'administrative city transfers retire the previous local army' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'CityOwnershipTransferRules.ShouldDisbandLocalArmy('
Require-Present 'administrative city cleanup invokes vanilla soldier retirement' `
    'Code/patch/AW_ChroniclePatch.cs' 'RemoveCitySoldiers.Invoke('
Require-Absent 'peace settlement city transfer cannot bypass city membership cleanup' `
    'Code/core/lineage/WarTerritoryService.cs' `
    'targetCity.setKingdom(attacker, false);'
Require-Absent 'war-goal transfer helper cannot bypass city membership cleanup' `
    'Code/core/lineage/WarTerritoryService.cs' `
    'pTargetCity.setKingdom(pAttacker, false);'
$fastReuseIndex = $pathSubmit.IndexOf('finder.TryReuse(',
    [System.StringComparison]::Ordinal)
$profileCaptureIndex = $pathSubmit.IndexOf('CaptureProfile(pActor)',
    [System.StringComparison]::Ordinal)
if ($fastReuseIndex -lt 0 -or $profileCaptureIndex -lt 0 -or
    $fastReuseIndex -gt $profileCaptureIndex) {
    $failures.Add('same-target paths must be reused before actor profile capture')
}
Require-Present 'reused path requests preserve their original timeout clock' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'if (!reused)'
Require-Present 'smooth movement transpiler resolves updateMovement' `
    'Code/patch/AW_GlobalPathfindingPatch.cs' 'var updateMovement = AccessTools.Method(typeof(Actor), "updateMovement"'
Require-Present 'smooth movement transpiler uses direct optimized call' `
    'Code/patch/AW_GlobalPathfindingPatch.cs' 'nameof(UpdateMovementDirect)'
Require-Present 'AW3 smooth transpiler runs before real Cultiway' `
    'Code/patch/AW_GlobalPathfindingPatch.cs' '[HarmonyBefore("inmny.cultiway")]'
$pathFailureStart = $pathMovementBridge.IndexOf('private static void HandleFailure(')
$pathFailureEnd = $pathMovementBridge.IndexOf('private static bool TryStartDueRetry(',
    $pathFailureStart)
if ($pathFailureStart -lt 0 -or $pathFailureEnd -lt 0 -or
    -not $pathMovementBridge.Substring($pathFailureStart,
        $pathFailureEnd - $pathFailureStart).Contains('pActor.setNotMoving();')) {
    $failures.Add('path failure must leave the smooth movement batch before scheduling recovery')
}
$vassalService = Read-Source 'Code/core/lineage/VassalService.cs'
$getSuzerainStart = $vassalService.IndexOf('public static long GetSuzerainId(')
$getSuzerainEnd = $vassalService.IndexOf('public static Kingdom GetSuzerain(', $getSuzerainStart)
if ($getSuzerainStart -lt 0 -or $getSuzerainEnd -lt 0 -or
    $vassalService.Substring($getSuzerainStart,
        $getSuzerainEnd - $getSuzerainStart).Contains('ReadActiveSuzerainId(')) {
    $failures.Add('runtime suzerain lookup must not query archived vassal relations')
}
Require-Absent 'stable vassal plate component lookup' `
    'Code/ui/components/VassalNameplateSuzerainFlag.cs' '.GetComponent<'
Require-Present 'vassal flag resolves the visible name without private-field reflection' `
    'Code/ui/components/VassalNameplateSuzerainFlag.cs' `
    '_nameText = FindNameText(pNameplate);'
Require-Present 'missing name binding keeps the optional flag at a stable sibling' `
    'Code/ui/components/VassalNameplateSuzerainFlag.cs' `
    '_root.transform.SetAsFirstSibling();'
Require-Present 'nameplates receive one cached vassal flag component' `
    'Code/patch/AW_VassalNameplatePatch.cs' 'VassalNameplateSuzerainFlag.Attach(__instance);'
Require-Present 'kingdom suffix uses native nameplate string generation' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'getStringForNameplate'
Require-Absent 'kingdom suffix does not recalculate population' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'getPopulationPeople'
Require-Absent 'kingdom suffix does not assign nameplate text twice' `
    'Code/patch/AW_NameplateTitlePatch.cs' '.setText('
Require-Absent 'mandate marker service has no field reflection' `
    'Code/core/policy/MandateMapMarkerService.cs' 'FieldInfo'
Require-Absent 'mandate marker service does not mutate nameplate fields' `
    'Code/core/policy/MandateMapMarkerService.cs' '.SetValue('
Require-Absent 'mandate marker render does not rebuild database report' `
    'Code/core/policy/MandateMapMarkerService.cs' 'ReadReport('
Require-Present 'kingdom mandate marker patches the native Sprite overload' `
    'Code/patch/AW_MandateMapModePatch.cs' `
    '"showSpecies", new[] { typeof(Sprite) }'
Require-Absent 'kingdom mandate marker cannot patch the unused string overload' `
    'Code/patch/AW_MandateMapModePatch.cs' `
    '"showSpecies", new[] { typeof(string) }'
Require-Present 'kingdom mandate marker caches resolved Sprite instances' `
    'Code/patch/AW_MandateMapModePatch.cs' `
    'Dictionary<string, Sprite>'
Require-Present 'kingdom nameplates use the self-validating state-name projection' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'RulerAppellationService.GetProjectedStateName('
Require-Absent 'kingdom nameplates never query SQLite' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'SQLite'
Require-Absent 'kingdom nameplates do not inspect rebel state during redraw' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'MandateRebelService'
Require-Absent 'kingdom nameplates do not inspect government during redraw' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'RepublicGovernmentService'
Require-Absent 'kingdom nameplates do not inspect species during redraw' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'LineageService'
Require-Present 'wide windows share one drag and resize component' `
    'Code/ui/components/WideWindowChrome.cs' 'internal sealed class WideWindowChrome'
Require-Present 'court uses shared wide-window chrome' `
    'Code/ui/windows/CourtWindow.cs' 'WideWindowChrome.Attach('
Require-Present 'era window has the exact minimum size' `
    'Code/ui/windows/NameDecisionWindow.cs' 'new Vector2(420f, 280f)'
Require-Present 'era window submits through the multiplayer command boundary' `
    'Code/ui/windows/NameDecisionWindow.cs' 'AW3CommandRequest.ChangeEra('
Require-Present 'authoritative records handler commits through the atomic era service' `
    'Code/core/multiplayer/commands/AW3RecordsCommandHandler.cs' `
    'YearNameService.TryChangeEra('
Require-Present 'second era-window click focuses without resetting input' `
    'Code/ui/windows/NameDecisionWindow.cs' 'FocusExisting()'
Require-Present 'policy era click opens the era-only window' `
    'Code/ui/windows/KingdomPolicyWindow.cs' 'NameDecisionWindow.Open('
Require-Present 'unit window projects a ritual appellation without replacing the actor name' `
    'Code/patch/AW_UnitWindowPatch.cs' 'RulerAppellationService.GetFullLivingAppellation('
Require-Present 'kingdom window projects a ritual appellation and keeps the real ruler name' `
    'Code/patch/AW_KingdomWindowPatch.cs' 'RulerAppellationService.GetFullLivingAppellation('
Require-Present 'family nodes receive cached appellation details before rendering' `
    'Code/core/lineage/LineageQuery.cs' 'RulerAppellationService.EnrichFamilyTreeNode('
Require-Present 'family tooltip shows the ritual appellation snapshot' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'pNode.ritual_appellation'
Require-Present 'family tooltip shows the direct parent Shi' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'pNode.parent_shi_display'
Require-Present 'family tooltip shows the root Shi' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'pNode.root_shi_display'
Require-Present 'family tooltip shows the Shi origin city' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'pNode.origin_city_name'
Require-Present 'family tooltip shows the bound state name' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'pNode.state_name'
Require-Present 'family tooltip shows retrospective relation' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'pNode.retrospective_relation'
Require-Present 'family tree biography button is pooled on the node' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'BuildBiographyButton()'
Require-Present 'family tree biography listener is reset on rebind' `
    'Code/ui/items/FamilyTreeNodeView.cs' '_biographyButton.onClick.RemoveAllListeners()'
Require-Present 'family tree biography uses the authoritative person history window' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'HistoryListWindow.OpenPerson(actorId)'
Require-Present 'family tree biography uses the document icon' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'ui/icons/iconDocument'
Require-Present 'family tree identity title uses the bounded ritual-first projection' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'BuildIdentityTitleBlock(pNode.ritual_appellation,'
Require-Present 'family tree branch badges reserve the second social-title line' `
    'Code/ui/items/FamilyTreeNodeView.cs' 'CompactSocialTitleHeight'
Require-Present 'family tree social title wraps inside its fixed card' `
    'Code/ui/items/FamilyTreeNodeView.cs' '_socialText.horizontalOverflow = HorizontalWrapMode.Wrap'
Require-Absent 'family tree social title cannot overflow into adjacent nodes' `
    'Code/ui/items/FamilyTreeNodeView.cs' '_socialText.horizontalOverflow = HorizontalWrapMode.Overflow'
Require-Present 'family tree biography description locale' `
    'Locales/others.csv' 'aw_view_person_biography_desc,'
Require-Present 'manual rank change confirms one state row' `
    'Code/core/court/OfficialCareerStateService.cs' 'TryApplyManualRankChange('
Require-Present 'manual dismissal exposes a confirmed result' `
    'Code/core/court/CourtService.cs' 'TryDismissOfficer('
Require-Present 'manual noble title closure is confirmed' `
    'Code/core/lineage/NobleRankService.cs' 'TryRevoke('
Require-Present 'manual fief revocation is confirmed' `
    'Code/core/lineage/FiefService.cs' 'TryRevokeActorFief('
Require-Present 'fief disposition preview reuses domain eligibility' `
    'Code/core/lineage/FiefService.cs' 'CanGrantFief('
Require-Present 'feudatory disposition reports typed resistance result' `
    'Code/core/lineage/FeudatoryService.cs' 'TryRelocateFeudatoryDisposition('
Require-Present 'other punishments can trigger feudatory resistance' `
    'Code/core/lineage/FeudatoryService.cs' 'TryStartDispositionResistance('
Require-Present 'general disposition has an immediate rebellion entry' `
    'Code/core/lineage/GeneralRebellionService.cs' 'TryStartDispositionRebellion('
Require-Present 'chief minister disposition has an immediate coup entry' `
    'Code/core/court/MinisterialPowerService.cs' 'TryStartDispositionCoup('
Require-Present 'court disposition centralizes resistance routing' `
    'Code/core/court/CourtDispositionResistanceService.cs' 'CourtDispositionRules.ResistanceRoute('
Require-Present 'court disposition has an authoritative preview' `
    'Code/core/court/CourtDispositionService.cs' 'public static CourtDispositionPreview Preview('
Require-Present 'court disposition has one authoritative executor' `
    'Code/core/court/CourtDispositionService.cs' 'public static CourtDispositionResult Execute('
Require-Present 'court disposition uses the idempotency ledger' `
    'Code/core/court/CourtDispositionService.cs' 'ReadByOperationKey('
Require-Present 'court disposition routes resistance before mutation' `
    'Code/core/court/CourtDispositionService.cs' 'CourtDispositionResistanceService.Resolve('
Require-Present 'court disposition spends only through policy service' `
    'Code/core/court/CourtDispositionService.cs' 'KingdomPolicyService.TrySpendPoliticalPoints('
Require-Present 'court disposition finalizes its ledger result' `
    'Code/core/court/CourtDispositionService.cs' 'CourtDispositionPersistence.Finalize('
Require-Present 'surname disposition writes shared history' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnCourtSurnameGranted('
Require-Present 'lineage expulsion writes shared history' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnCourtLineageExpelled('
foreach ($courtDispositionLocaleKey in @(
    'aw_court_disposition_window_title,',
    'aw_court_disposition_window_desc,',
    'aw_court_disposition_action_promote_rank,',
    'aw_court_disposition_action_demote_rank,',
    'aw_court_disposition_action_dismiss_office,',
    'aw_court_disposition_action_grant_noble_rank,',
    'aw_court_disposition_action_grant_fief,',
    'aw_court_disposition_action_revoke_fief,',
    'aw_court_disposition_action_grant_surname,',
    'aw_court_disposition_action_expel_lineage,',
    'aw_court_disposition_action_relocate_feudatory,',
    'aw_court_disposition_action_reclaim_feudatory_city,',
    'aw_court_disposition_reason_invalid_command,',
    'aw_court_disposition_reason_invalid_ruler,',
    'aw_court_disposition_reason_invalid_target,',
    'aw_court_disposition_reason_invalid_parameter,',
    'aw_court_disposition_reason_ineligible_action,',
    'aw_court_disposition_reason_insufficient_political_points,',
    'aw_court_disposition_reason_persistence_failed,',
    'aw_court_disposition_reason_political_spend_failed,',
    'aw_court_disposition_reason_resistance_failed,',
    'aw_court_disposition_outcome_rejected,',
    'aw_court_disposition_outcome_committed,',
    'aw_court_disposition_outcome_rebelled,',
    'aw_court_disposition_outcome_clean_failure,',
    'aw_court_disposition_outcome_unknown,',
    'aw_court_disposition_cost,',
    'aw_hist_event_court_surname_granted,',
    'aw_hist_event_court_lineage_expelled,',
    'aw_hist_court_surname_edict,',
    'aw_hist_court_expulsion_edict,'
)) {
    Require-Present "court disposition locale $courtDispositionLocaleKey" `
        'Locales/aw3_court.csv' $courtDispositionLocaleKey
}
Require-Present 'disposition window default width is bounded' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'DefaultWidth = 420f'
Require-Present 'disposition window default height is bounded' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'DefaultHeight = 280f'
Require-Present 'disposition window reuses wide window chrome' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'WideWindowChrome.Attach('
Require-Present 'disposition window previews commands through service' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'CourtDispositionService.Preview('
Require-Present 'disposition window submits through the multiplayer command facade' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'AW3MultiplayerCommandFacade.DispatchFromUi('
Require-Present 'authoritative court handler executes disposition commands through service' `
    'Code/core/multiplayer/commands/AW3CourtCommandHandler.cs' `
    'CourtDispositionService.Execute(command);'
Require-Present 'successful disposition refreshes the court' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'CourtWindow.OpenAndRefresh('
Require-Present 'disposition city choices are bounded' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'MaximumCityChoices = 32'
Require-Present 'court cards expose the disposition console' `
    'Code/ui/items/CourtActorNodeView.cs' 'CourtDispositionWindow.Open('
Require-Absent 'disposition UI cannot write political points directly' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'LineageKeys.POLICY_POINTS'
Require-Absent 'disposition UI cannot insert database rows' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'DB.Insert'
Require-Absent 'disposition UI cannot update database rows' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'DB.UpdateValue'
Require-Absent 'disposition UI cannot mutate actor data' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'actor.data.set'
Require-Absent 'disposition UI cannot transfer city sovereignty' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'city.joinAnotherKingdom'
Require-Present 'lineage disposition migration is hard bounded' `
    'Code/core/lineage/LineageDispositionRules.cs' 'MaximumMigrants = 128'
Require-Present 'lineage disposition queries indexed parent edges' `
    'Code/core/lineage/LineageDispositionService.cs' 'WHERE E.PARENT_ID=@parent'
Require-Present 'lineage disposition limits each child query' `
    'Code/core/lineage/LineageDispositionService.cs' 'LIMIT @limit'
Require-Absent 'lineage disposition cannot scan kingdom units' `
    'Code/core/lineage/LineageDispositionService.cs' 'kingdom.getUnits'
Require-Absent 'lineage disposition cannot scan world unit lists' `
    'Code/core/lineage/LineageDispositionService.cs' 'getSimpleList'
Require-Absent 'lineage disposition cannot use recovering child scans' `
    'Code/core/lineage/LineageDispositionService.cs' 'LineageQuery.GetChildIds'
Require-Absent 'disposition window cannot scan kingdom actors' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'getUnits('
Require-Absent 'disposition window cannot scan world actors' `
    'Code/ui/windows/CourtDispositionWindow.cs' 'World.world.units'
Require-Present 'recent benchmark command has an explicit entry' `
    'Tests/AncientWarfare3.Rules.Tests/Program.cs.txt' 'CourtDispositionPerformanceTests.Run()'
Require-Present 'lineage disposition grants surnames through one service' `
    'Code/core/lineage/LineageDispositionService.cs' 'TryGrantSurname('
Require-Present 'lineage disposition expels through one service' `
    'Code/core/lineage/LineageDispositionService.cs' 'TryExpel('
Require-Present 'lineage disposition reads the indexed parent edge' `
    'Code/core/lineage/LineageDispositionService.cs' 'WHERE E.PARENT_ID=@parent'
Require-Absent 'lineage disposition cannot use fallback world child scans' `
    'Code/core/lineage/LineageDispositionService.cs' 'LineageQuery.GetChildIds('
Require-Absent 'lineage disposition cannot scan kingdom units' `
    'Code/core/lineage/LineageDispositionService.cs' 'getUnits('
Require-Absent 'lineage disposition cannot rename actor real names' `
    'Code/core/lineage/LineageDispositionService.cs' '.setName('
Require-Absent 'appellation projection service cannot write SQLite' `
    'Code/core/lineage/RulerAppellationService.cs' 'ExecuteNonQuery('
Require-Present 'republic transitions refresh cached appellations' `
    'Code/core/lineage/RepublicGovernmentService.cs' 'RulerAppellationService.RefreshLivingProjection('
Require-Present 'rebel transitions refresh cached appellations' `
    'Code/core/lineage/MandateRebelService.cs' 'RulerAppellationService.RefreshLivingProjection('
Require-Present 'Mandate transitions refresh cached appellations' `
    'Code/core/lineage/MandateService.cs' 'RulerAppellationService.RefreshLivingProjection('
Require-Present 'kingdom renames refresh cached appellations' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'RulerAppellationService.RefreshLivingProjection('
Require-Present 'Xia institution adoption refreshes cached appellations' `
    'Code/core/lineage/XiaizationService.cs' 'RulerAppellationService.RefreshLivingProjection('
Require-Present 'title UI has a dedicated three-language locale file' `
    'Locales/aw3_titles.csv' 'aw_title_era_window,'
Require-Present 'title history fallbacks include accession eras' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_title_accession_era'
Require-Present 'title history fallbacks include voluntary eras' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_title_voluntary_era'
Require-Present 'title history fallbacks include first state names' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_title_state_name'
Require-Present 'title history fallbacks include retrospective awards' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_title_retrospective'
Require-Present 'title history fallbacks include Shi cadet branches' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_title_shi_branch'
Require-Present 'era commits use the accession edict template' `
    'Code/core/lineage/YearNameService.cs' 'aw_hist_edict_accession_era'
Require-Present 'era commits use the voluntary era edict template' `
    'Code/core/lineage/YearNameService.cs' 'aw_hist_edict_voluntary_era'
Require-Present 'state-name commits use the first-name title template' `
    'Code/core/lineage/StateNameService.cs' 'aw_hist_title_state_name'
Require-Present 'reign-end title commits use the posthumous edict template' `
    'Code/core/lineage/PosthumousTitleService.cs' 'aw_hist_edict_posthumous'
Require-Present 'retrospective commits use the retrospective template' `
    'Code/core/lineage/RetrospectiveTitleService.cs' 'aw_hist_title_retrospective'
Require-Present 'cadet branch creation writes a biography snapshot' `
    'Code/core/lineage/LineageService.cs' 'aw_hist_title_shi_branch'
Require-FileAbsent 'legacy Mandate title table is deleted' `
    'Code/core/db/MandateRulerTitleTableItem.cs'
Require-FileAbsent 'legacy Mandate title definitions are deleted' `
    'Code/core/lineage/MandateRulerTitleDefs.cs'
Require-FileAbsent 'legacy Mandate title rules are deleted' `
    'Code/core/lineage/MandateRulerTitleRules.cs'
Require-FileAbsent 'legacy Mandate title service is deleted' `
    'Code/core/lineage/MandateRulerTitleService.cs'
Require-Present 'kingdom history reads authoritative posthumous title rows' `
    'Code/core/lineage/HistoryQuery.cs' 'PosthumousTitleTableItem.GetTableName()'
Require-Present 'Mandate history reads authoritative posthumous title rows' `
    'Code/core/lineage/MandateHistoryQuery.cs' 'PosthumousTitleTableItem.GetTableName()'
Require-Present 'posthumous titles have a Mandate-period directed index' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'idx_PosthumousTitle_mandate_period_reign'
Require-Absent 'reign rows cannot own duplicate posthumous title fields' `
    'Code/core/db/KingdomReignTableItem.cs' 'posthumous_title'
Require-Absent 'reign rows cannot own duplicate posthumous color fields' `
    'Code/core/db/KingdomReignTableItem.cs' 'posthumous_color'
Require-Absent 'reign writer cannot initialize or update duplicate title columns' `
    'Code/core/lineage/ReignRecordWriter.cs' 'POSTHUMOUS_TITLE'
Require-Absent 'title commits cannot dual-write the reign row' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'POSTHUMOUS_TITLE'
Require-Present 'untitled reign lookup excludes authoritative title rows' `
    'Code/core/lineage/ReignRecordWriter.cs' 'NOT EXISTS (SELECT 1 FROM'
Require-Present 'untitled reign lookup reads the authoritative title table' `
    'Code/core/lineage/ReignRecordWriter.cs' 'PosthumousTitleTableItem.GetTableName()'
Require-Absent 'ancestry analysis cannot fall back to reign title snapshots' `
    'Code/core/lineage/AncestryAnalysisService.cs' 'POSTHUMOUS_TITLE'
Require-Absent 'history windows cannot repair first-emperor titles while reading' `
    'Code/core/lineage/HistoryQuery.cs' 'RepairOrdinaryFirstEmperorDisplayTitles'
Require-Absent 'history queries cannot invoke compact-title repair rules' `
    'Code/core/lineage/HistoryQuery.cs' 'RepairFirstOrdinaryEmperorDisplayTitle'
Require-Present 'mandate marker uses native species loader' `
    'Code/patch/AW_MandateMapModePatch.cs' '"showSpecies"'
Require-Absent 'mandate marker no longer post-processes kingdom plates' `
    'Code/patch/AW_MandateMapModePatch.cs' 'ApplyNameplate('
Require-Present 'archive switch rebuilds mandate marker projection' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'MandateService.RebuildRuntimeMarkerProjection'
$godPowerLibrary = Read-Source 'Code/content/GodPowerLibrary.cs'
$schoolDrawStart = $godPowerLibrary.IndexOf('private static void DrawSchoolNameplates(')
$schoolDrawEnd = $godPowerLibrary.IndexOf('private static void ConfigureMapModePower(', $schoolDrawStart)
if ($schoolDrawStart -lt 0 -or $schoolDrawEnd -lt 0) {
    $failures.Add('school nameplate draw method could not be inspected')
} else {
    $schoolDrawRegion = $godPowerLibrary.Substring($schoolDrawStart,
        $schoolDrawEnd - $schoolDrawStart)
    if (-not $schoolDrawRegion.Contains('zone_camera.getVisibleZones()')) {
        $failures.Add('school nameplates must enumerate visible zones')
    }
    if ($schoolDrawRegion.Contains('foreach (City city in World.world.cities)')) {
        $failures.Add('school nameplates must not scan every world city')
    }
    if ([regex]::Matches($schoolDrawRegion,
            'CitySchoolSnapshotService\.GetSnapshot\(').Count -ne 1) {
        $failures.Add('school nameplate candidate must read exactly one snapshot')
    }
    if (-not $schoolDrawRegion.Contains(
            'GetSchoolIdentityMetaForCity(city, snapshot)')) {
        $failures.Add('school nameplate identity and icon must share one snapshot')
    }
}
Require-Present 'school identity accepts cached snapshot' `
    'Code/core/policy/AWMapModeMetaLibrary.cs' `
    'GetSchoolIdentityMetaForCity(City pCity, CitySchoolSnapshot pSnapshot)'
Require-Present 'school actor navigation finishes window animation' 'Code/ui/SchoolActorNavigation.cs' 'ScrollWindow.finishAnimations();'
Require-Present 'school actor navigation resolves unit meta selection' 'Code/ui/SchoolActorNavigation.cs' 'MetaType.Unit.getAsset()'
Require-Present 'school actor navigation uses unit meta selection' 'Code/ui/SchoolActorNavigation.cs' 'unitMeta.selectAndInspect('
Require-Absent 'school card avoids legacy unit navigation' 'Code/ui/items/SchoolActorCardView.cs' 'ActionLibrary.openUnitWindow'
Require-Absent 'school master card avoids legacy unit navigation' 'Code/ui/items/SchoolMasterCardView.cs' 'ActionLibrary.openUnitWindow'
Require-Absent 'school roster avoids legacy unit navigation' 'Code/ui/items/SchoolRosterNodeView.cs' 'ActionLibrary.openUnitWindow'
Require-Absent 'anonymous Xia clan placeholder' 'Code/content/XiaNaming.cs' '"无名"'
Require-Absent 'historical master must not force player favorite marker' 'Code/core/schools/HistoricalMasterIdentityProjection.cs' 'pActor.data.favorite = true;'
Require-Absent 'marriage must not copy spouse blood lineage' 'Code/core/lineage/LineageService.cs' 'TryInheritLineageFromSource(pHusband, pWife'
Require-Absent 'lover patch must not mutate blood lineage' 'Code/patch/AW_LoversPatch.cs' 'LineageService.OnBecameLovers'
Require-Absent 'occupation protection must not wait for capture-owner registration' 'Code/core/lineage/ArmyRetreatService.cs' 'pTargetCity.being_captured_by == pAttacker'
Require-Present 'occupation protection reads current capture forces' 'Code/core/lineage/ArmyRetreatService.cs' 'CityOccupationAccelerationService.DescribeCaptureFor('
Require-Absent 'army retreat cannot enumerate the whole roster synchronously' 'Code/core/lineage/ArmyRetreatService.cs' 'SafeUnits('
Require-Absent 'army retreat cannot scan every kingdom city' 'Code/core/lineage/ArmyRetreatService.cs' 'kingdom.getCities()'
Require-Present 'army retreat uses direct roster count' 'Code/core/lineage/ArmyRetreatService.cs' 'pArmy.countUnits()'
Require-Present 'army retreat mutates only one captain per item' 'Code/core/lineage/ArmyRetreatService.cs' 'CaptainMutationBudget = 1'
Require-Present 'army retreat batches use the deferred queue' 'Code/core/lineage/ArmyRetreatService.cs' 'CoalescingKey("army_retreat", pArmyId)'
Require-Present 'army retreat uses the existing captain path request' 'Code/core/lineage/ArmyRetreatService.cs' 'captain.goTo('
Require-Absent 'army retreat cannot iterate direct army members' 'Code/core/lineage/ArmyRetreatService.cs' 'army.units'
Require-Absent 'one retreating army cannot clear its city shared attack target' 'Code/core/lineage/ArmyRetreatService.cs' 'pSourceCity.target_attack_city = null'
Require-Present 'army retreat runtime state has world reset' 'Code/core/lineage/ArmyRetreatService.cs' 'public static void ClearRuntime()'
Require-Present 'archive switch clears army retreat runtime state' 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'ArmyRetreatService.ClearRuntime'
$armySafetyPatch = Read-Source 'Code/patch/AW_ArmySafetyPatch.cs'
$retreatGate = $armySafetyPatch.IndexOf('ArmyRetreatService.ShouldStopAttack(pActor)',
    [System.StringComparison]::Ordinal)
$vanguardGate = $armySafetyPatch.IndexOf(
    'TemporarySlaveVanguardService.ShouldDelayBehindVanguard(',
    [System.StringComparison]::Ordinal)
if ($retreatGate -lt 0 -or $vanguardGate -lt 0 -or $retreatGate -gt $vanguardGate) {
    $failures.Add('an active or newly triggered retreat must precede vanguard assault holding')
}
Require-Present 'capture prefix can stop stale post-transfer update' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'public static bool UpdateCapture_Prefix(City __instance, float pElapsed)'
Require-Absent 'capture prefix cannot complete occupation after defender defeat' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'TryCompleteAfterDefenderDefeat'
Require-Absent 'occupation service cannot retain an early-completion entry' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'TryCompleteAfterDefenderDefeat'
Require-Absent 'occupation prefix cannot add AW3 capture ticks' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'BeforeUpdateCapture('
Require-Absent 'occupation service cannot expose an acceleration path' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'AddCaptureTicks('
Require-Absent 'occupation service cannot write private capture progress' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'CaptureTicksField.SetValue'
Require-Absent 'occupation rules cannot modify vanilla capture speed' 'Code/core/lineage/CityOccupationAccelerationRules.cs' 'ExtraCapturePoints('
Require-Absent 'occupation patch cannot suppress vanilla capture points' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'ShouldApplyCapturePointContribution('
Require-Present 'zone capture scan records actual warriors' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'RecordActiveMilitaryPresence(__instance, pObject);'
Require-Present 'zone capture reset clears military presence' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'ClearActiveMilitaryPresence(__instance);'
Require-Present 'active defender check uses military presence index' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'HasActiveMilitaryPresence(pCity, pCity.kingdom)'
Require-Absent 'occupation service no longer retains defender engagement state' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'EngagementByCity'
Require-Present 'finish capture requires the natural one-hundred-percent limit' `
    'Code/patch/AW_CityOccupationAccelerationPatch.cs' `
    'CityOccupationAccelerationService.HasReachedNaturalCaptureLimit(__instance)'
Require-Present 'locked city completion is deferred through the shared frame queue' `
    'Code/core/lineage/CityOccupationAccelerationService.cs' '"occupation_complete", pCity.id'
Require-Present 'vassal capitals are intercepted before vanilla ownership transfer' `
    'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'TryQueueNonTerritorialSettlementAtCaptureLimit'
Require-Present 'controlled vassal goals settle without city transfer' `
    'Code/core/lineage/WarTerritoryService.cs' 'TryResolveControlledSettlementGoal'
Require-Present 'tributary victories use a dedicated peace action' `
    'Code/core/lineage/PeaceSettlementRules.cs' 'case WarTerritoryService.GOAL_FORCE_TRIBUTARY:'
Require-Present 'explicit war goals prevent duplicate vassal settlement' `
    'Code/core/lineage/VassalService.cs' 'bool hasExplicitGoal = WarTerritoryService.HasWarGoal(pWar.data.id);'
Require-Absent 'war end no longer maintains removed city engagement evidence' 'Code/patch/AW_WarPatch.cs' 'CityOccupationAccelerationService.OnWarEnded(pWar);'
Require-Present 'city occupation runtime cache has world reset' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'public static void ClearRuntime()'
Require-Present 'archive switch clears city occupation runtime cache' 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'CityOccupationAccelerationService.ClearRuntime'
Require-Present 'royal asylum active key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_ACTIVE = "aw_royal_asylum_active"'
Require-Present 'royal asylum home kingdom key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_HOME_KINGDOM_ID = "aw_royal_asylum_home_kingdom_id"'
Require-Present 'royal asylum former city key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_FORMER_CITY_ID = "aw_royal_asylum_former_city_id"'
Require-Present 'royal asylum host city key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_HOST_CITY_ID = "aw_royal_asylum_host_city_id"'
Require-Present 'royal asylum roster key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_ROSTER_IDS = "aw_royal_asylum_roster_ids"'
Require-Present 'royal asylum content registration' 'Code/content/XiaContent.cs' 'RoyalAsylumContent.Init();'
Require-Present 'royal asylum dedicated actor job' 'Code/content/RoyalAsylumContent.cs' 'ActorJobId = "aw_royal_asylum_job"'
Require-Present 'royal asylum dedicated roam task' 'Code/content/RoyalAsylumContent.cs' 'RoamTaskId = "aw_royal_asylum_roam"'
Require-Present 'royal asylum status asset' 'Code/content/RoyalAsylumContent.cs' 'StatusId = "aw_royal_asylum"'
Require-Present 'royal asylum roam behavior' 'Code/ai/behaviours/actor/BehRoyalAsylumRoam.cs' 'class BehRoyalAsylumRoam'
Require-Present 'royal asylum roam uses logical host' 'Code/ai/behaviours/actor/BehRoyalAsylumRoam.cs' 'RoyalAsylumService.TryGetRoamTile'
Require-Present 'royal asylum status title locale' 'Locales/others.csv' 'status_title_aw_royal_asylum,'
Require-Present 'royal asylum status description locale' 'Locales/others.csv' 'status_description_aw_royal_asylum,'
Require-Present 'royal asylum roam task locale' 'Locales/others.csv' 'task_unit_aw_royal_asylum_roam,'
Require-Present 'royal asylum host row locale' 'Locales/others.csv' 'aw_royal_asylum_host,'
Require-Present 'royal asylum roster is bounded' 'Code/core/lineage/RoyalAsylumService.cs' 'MaxRosterSize = 64'
Require-Present 'royal asylum reacts to war start' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void OnWarStarted(War pWar)'
Require-Present 'royal asylum reconciles by home kingdom year' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void OnKingdomYear(Kingdom pHome)'
Require-Present 'peaceful kingdoms without refugees skip royal genealogy work' `
    'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumRules.NeedsAnnualProcessing('
Require-Present 'royal asylum annual roster is read once into a local snapshot' `
    'Code/core/lineage/RoyalAsylumService.cs' 'List<long> roster = ReadRoster(pHome);'
Require-Present 'royal asylum host candidates are shared by one annual pass' `
    'Code/core/lineage/RoyalAsylumService.cs' 'List<HostCandidate> hostCandidates = null;'
Require-Present 'royal asylum annual evacuation appends to the retained roster' `
    'Code/core/lineage/RoyalAsylumService.cs' 'TryEvacuate(actor, pHome, retained, hostCandidates,'
Require-Absent 'royal asylum evacuation cannot rewrite the roster per actor' `
    'Code/core/lineage/RoyalAsylumService.cs' 'AddToRoster(pHome, pActor.data.id);'
Require-Present 'royal asylum presentation avoids duplicate status writes' `
    'Code/core/lineage/RoyalAsylumService.cs' '!pActor.hasStatus(RoyalAsylumContent.StatusId)'
Require-Present 'royal asylum presentation avoids duplicate job resets' `
    'Code/core/lineage/RoyalAsylumService.cs' 'pActor.ai.job?.id != RoyalAsylumContent.ActorJobId'
Require-Present 'royal asylum runtime reload exists' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void LoadRuntimeState()'
Require-Present 'royal asylum runtime reset exists' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void ClearRuntime()'
Require-Present 'royal asylum evacuation removes formal city only' 'Code/core/lineage/RoyalAsylumService.cs' 'pActor.setCity(null);'
Require-Present 'royal asylum evacuation verifies retained nationality' 'Code/core/lineage/RoyalAsylumService.cs' 'pActor.kingdom != pHome'
Require-Absent 'royal asylum cannot set a foreign formal city' 'Code/core/lineage/RoyalAsylumService.cs' 'setCity(pHost'
Require-Absent 'royal asylum cannot scan every actor' 'Code/core/lineage/RoyalAsylumService.cs' 'foreach (Actor actor in World.world.units)'
Require-Present 'war start invokes royal asylum' 'Code/patch/AW_WarPatch.cs' 'RoyalAsylumService.OnWarStarted(__result);'
Require-Present 'kingdom year invokes royal asylum' 'Code/core/policy/KingdomAnnualWorkService.cs' 'RoyalAsylumService.OnKingdomYear(pKingdom)'
Require-Present 'archive load rebuilds royal asylum runtime' 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'RoyalAsylumService.LoadRuntimeState'
Require-Present 'archive switch clears royal asylum runtime' 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'RoyalAsylumService.ClearRuntime'
Require-Present 'royal asylum extinction naturalization exists' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void NaturalizeBeforeExtinction(Kingdom pHome)'
Require-Present 'royal asylum extinction uses formal host join' 'Code/core/lineage/RoyalAsylumService.cs' 'actor.joinCity(hostCity);'
Require-Present 'naturalized refugee leaves extinct kingdom unit cache immediately' 'Code/core/lineage/RoyalAsylumService.cs' 'pHome.units.Remove(actor);'
Require-Present 'failed extinction asylum cannot survive nomad conversion' 'Code/core/lineage/RoyalAsylumService.cs' 'CloseBeforeNomadFallback(actor, pHome);'
Require-Present 'kingdom destruction invokes asylum naturalization before vanilla removal' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'RoyalAsylumService.NaturalizeBeforeExtinction(pKingdom);'
Require-Present 'ordinary enlistment rejects royal refugees' 'Code/patch/AW_EnlistPatch.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'direct warrior promotion rejects royal refugees' 'Code/patch/AW_EnlistPatch.cs' 'SetProfession_Asylum_Prefix'
Require-Present 'controlled recruitment uses thread-static scope' 'Code/core/lineage/MilitaryRecruitmentScope.cs' '[ThreadStatic]'
Require-Present 'vanilla random enlistment is intercepted' 'Code/patch/AW_StandingArmyPatch.cs' '[HarmonyPatch(typeof(City), "tryToMakeWarrior")]'
Require-Present 'standing army candidate scan is bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxCandidateScan = 64'
Require-Present 'standing army roster scan is bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxStandingScanPerPass = 64'
Require-Present 'standing army appointments are bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxAppointmentsPerPass = 2'
Require-Present 'standing army reductions are bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxReductionsPerPass = 2'
Require-Present 'standing army replacements are bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxReplacementsPerPass = 1'
Require-Present 'standing army candidate cursor indexes residents directly' 'Code/core/lineage/StandingArmyService.cs' 'pCity.units'
Require-Present 'standing army roster cursor indexes members directly' 'Code/core/lineage/StandingArmyService.cs' 'army.units'
Require-Present 'standing army maintenance pauses during mobilization' 'Code/core/lineage/StandingArmyService.cs' 'StandingArmyRules.ShouldMaintainPeacetime('
Require-Absent 'standing army candidate cursor cannot reskip prior residents' 'Code/core/lineage/StandingArmyService.cs' 'skipped++ < cursor'
Require-Absent 'standing army maintenance cannot enumerate the full roster' 'Code/core/lineage/StandingArmyService.cs' 'foreach (Actor actor in army.getUnits())'
Require-Absent 'per-warrior retention cannot recount the whole army' 'Code/core/lineage/StandingArmyService.cs' 'return CountOrdinaryMilitary(pCity)'
Require-Absent 'Xia genome retains the vanilla offspring cap' `
    'Code/content/XiaRace.cs' '("offspring",'
Require-Absent 'Xia fertility rules do not define an extra offspring cap' `
    'Code/content/XiaFertilityRules.cs' 'XiaOffspringDelta'
Require-Present 'war notice signature key' 'Code/core/lineage/LineageKeys.cs' 'DECISION_NOTICE_SIGNATURE = "aw_decision_notice_signature"'
Require-Present 'war notice earliest-year key' 'Code/core/lineage/LineageKeys.cs' 'DECISION_NOTICE_EARLIEST_YEAR = "aw_decision_notice_earliest_year"'
Require-Present 'war notice forced-year key' 'Code/core/lineage/LineageKeys.cs' 'DECISION_NOTICE_FORCED_YEAR = "aw_decision_notice_forced_year"'
Require-Present 'diplomatic declaration state key' 'Code/core/lineage/LineageKeys.cs' 'DIPLOMATIC_WAR_PENDING = "aw_diplomatic_war_pending"'
Require-Present 'diplomatic declaration service owns issue flow' 'Code/core/lineage/DiplomaticWarDeclarationService.cs' 'public static bool Issue('
Require-Present 'diplomatic declaration advances without policy points' 'Code/core/lineage/DiplomaticWarDeclarationService.cs' 'WarNoticeService.CanCompleteDiplomaticDeclaration(pAttacker)'
Require-Present 'diplomatic notice uses the point-free gate' 'Code/core/lineage/WarNoticeService.cs' 'WarNoticeRules.EvaluateDiplomaticGate('
Require-Present 'kingdom year advances diplomatic declarations' 'Code/core/policy/KingdomAnnualWorkService.cs' 'DiplomaticWarDeclarationService.OnKingdomYear(pKingdom);'
Require-Present 'war target window submits an authoritative multiplayer command' `
    'Code/ui/windows/WarDecisionTargetWindow.cs' 'AW3CommandRequest.DeclareWar(pSource.id,'
Require-Present 'war notice is visible in the diplomacy conversation' 'Code/core/lineage/WarNoticeService.cs' 'DiplomacyConversationService.RecordWarNotice('
Require-Present 'successful declaration returns to diplomacy' 'Code/ui/windows/WarDecisionTargetWindow.cs' 'DiplomacyConversationWindow.Open(pSource.id);'
Require-Absent 'declare war is not a kingdom decision definition' 'Code/content/policies/KingdomPolicyDefs.cs' 'Id = "aw_decision_declare_war"'
Require-Absent 'kingdom policy service no longer owns war declarations' 'Code/core/policy/KingdomPolicyService.cs' 'StartWarDecision('
Require-Present 'war end records cross-side truce treaties' 'Code/patch/AW_WarPatch.cs' 'DiplomacyProposalService.RegisterCoalitionTruces(pWar,'
Require-Present 'mandate captures final-city hostility before war settlement' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'MandateService.OnCityTransferStarting(__instance,'
Require-Present 'mandate succession only records the final hostile city transfer' `
    'Code/core/lineage/MandateService.cs' `
    'ResolveHostileMandateFinalCityConqueror('
Require-Present 'truce is persisted as a treaty' 'Code/core/lineage/DiplomacyProposalService.cs' 'public static bool RegisterTruce(War pWar)'
Require-Present 'coalition truce covers every cross-side pair' 'Code/core/lineage/DiplomacyProposalService.cs' 'public static bool RegisterCoalitionTruces(War pWar,'
Require-Present 'war notice runtime rebuild exists' 'Code/core/lineage/WarNoticeService.cs' 'public static void RebuildRuntime()'
Require-Present 'war notice runtime clear exists' 'Code/core/lineage/WarNoticeService.cs' 'public static void ClearRuntime()'
Require-Present 'kingdom year invokes temporary levies' 'Code/core/policy/KingdomAnnualWorkService.cs' 'TemporaryLevyService.OnKingdomYear(pKingdom);'
Require-Present 'war start activates temporary levies' 'Code/patch/AW_WarPatch.cs' 'TemporaryLevyService.OnWarStarted(__result'
Require-Present 'war end reevaluates temporary levies' 'Code/patch/AW_WarPatch.cs' 'TemporaryLevyService.OnWarEnded(pWar);'
Require-Present 'war notice issued history locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_war_notice_issued_mid,'
Require-Present 'war notice received history locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_war_notice_received_mid,'
Require-Present 'temporary levy enlistment history locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_temporary_levy_enlisted,'
Require-Present 'temporary levy demobilization history locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_temporary_levy_demobilized,'
Require-Present 'temporary slave vanguard demobilization locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_temporary_slave_vanguard_demobilized,'
Require-Present 'temporary levy work-item bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxWorkItemsPerKingdomYear = 4'
Require-Present 'temporary levy per-item scan bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxCandidatesPerWorkItem = 16'
Require-Present 'temporary levy per-item recruitment bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxRecruitsPerWorkItem = 8'
Require-Present 'temporary levy scan bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxCandidatesPerKingdomYear = 64'
Require-Present 'temporary levy recruitment bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxRecruitsPerKingdomYear = 32'
Require-Present 'temporary levy demobilization bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'DemobilizationBatchSize = 8'
Require-Present 'temporary levy persisted work-item key' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_LEVY_WORK_ITEMS = "aw_temporary_levy_work_items"'
Require-Present 'temporary levy persisted scan key' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_LEVY_SCANNED = "aw_temporary_levy_scanned"'
Require-Present 'temporary levy persisted recruit key' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_LEVY_RECRUITED = "aw_temporary_levy_recruited"'
Require-Present 'temporary levy persisted frontier cursor key' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_LEVY_FRONTIER_CURSOR = "aw_temporary_levy_frontier_cursor"'
Require-Present 'actor retirement rejects temporary service' 'Code/patch/AW_RetirementPatch.cs' 'TemporaryLevyService.IsTemporaryLevy(__instance) ||'
Require-Present 'fallback retirement rejects temporary service' 'Code/core/lineage/SlaveService.cs' 'TemporaryLevyService.IsTemporaryLevy(pActor) ||'
Require-Present 'temporary enlistment keeps noble biographies only' 'Code/patch/AW_EnlistPatch.cs' 'ShouldTrackPermanentEnlistmentHistory('
Require-Present 'temporary enlistment suppresses permanent service clock' 'Code/patch/AW_SlaveryPatch.cs' 'MilitaryRecruitmentScope.SuppressesPermanentEnlistmentHistory'
Require-Present 'temporary levy deferred cleanup' 'Code/core/lineage/TemporaryLevyService.cs' 'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'temporary levy hot lookup uses actor id index' 'Code/core/lineage/TemporaryLevyService.cs' 'ActiveActorIds'
Require-Present 'temporary levy pool activity is kingdom indexed' 'Code/core/lineage/TemporaryLevyService.cs' 'public static bool HasActivePool(Kingdom pKingdom)'
Require-Present 'temporary levy joins a city army immediately' 'Code/core/lineage/TemporaryLevyService.cs' 'EnsureArmyMembership('
Require-Absent 'temporary levy cannot use combat score' 'Code/core/lineage/TemporaryLevyService.cs' 'MilitaryScore('
Require-Absent 'city maintenance cannot form slave armies' 'Code/patch/AW_RetirementPatch.cs' 'SlaveService.EnsureSlaveArmy(pCity);'
Require-Absent 'forced slave control cannot form peacetime armies' 'Code/core/lineage/SlaveService.cs' 'EnsureSlaveArmy(city);'
Require-Absent 'slave enlistment cannot trigger legacy army formation' 'Code/core/lineage/SlaveService.cs' 'EnsureSlaveArmy(pCity ?? pActor.city);'
Require-Absent 'legacy slave frontline actor scan is removed' 'Code/core/lineage/SlaveService.cs' 'DriveSlaveArmyFrontline('
Require-Present 'slave population count is event indexed' 'Code/core/lineage/SlavePopulationIndexService.cs' 'internal static class SlavePopulationIndexService'
Require-Present 'slave population uses persisted city count' 'Code/core/lineage/SlavePopulationIndexService.cs' 'LineageKeys.SLAVE_POPULATION_COUNT'
Require-Present 'slave population tracks each actor city once' 'Code/core/lineage/SlavePopulationIndexService.cs' 'LineageKeys.SLAVE_COUNTED_CITY_ID'
Require-Absent 'slave population index cannot scan world units' 'Code/core/lineage/SlavePopulationIndexService.cs' 'World.world.units'
Require-Present 'enslavement increments slave population index' 'Code/core/lineage/SlaveService.cs' 'SlavePopulationIndexService.Activate('
Require-Present 'manumission decrements slave population index' 'Code/core/lineage/SlaveService.cs' 'SlavePopulationIndexService.Deactivate(pActor);'
Require-Present 'slave migration updates population index' 'Code/patch/AW_SlaveryPatch.cs' 'SlavePopulationIndexService.OnActorCityChanged(__instance);'
Require-Present 'slave death decrements population index' 'Code/patch/AW_ActorDeathPatch.cs' 'SlavePopulationIndexService.Deactivate(__instance);'
Require-Absent 'slave food quota cannot scan city residents' 'Code/core/lineage/SlaveService.cs' 'private static bool HasAnySlave('
Require-Absent 'slave labor count cannot scan city residents' 'Code/core/lineage/SlaveService.cs' 'private static int CountSlaves('
Require-Present 'city-fall enslavement has a resident scan cap' 'Code/core/lineage/SlaveService.cs' 'MAX_CITY_FALL_SCAN = 80'
Require-Present 'city-fall enslavement indexes bounded residents directly' 'Code/core/lineage/SlaveService.cs' 'Actor unit = pCity.units[i];'
Require-Absent 'city maintenance cannot invoke disabled retirement scans' 'Code/patch/AW_RetirementPatch.cs' 'SlaveService.CheckCityRetirements(pCity);'
Require-Present 'slave catcher cooldown is actor-local' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_CAPTURE_NEXT_SEARCH_TIME'
Require-Present 'slave catcher reads its own cooldown' 'Code/core/lineage/SlaveService.cs' 'LineageKeys.SLAVE_CAPTURE_NEXT_SEARCH_TIME, out float nextAllowed'
Require-Absent 'slave catcher cooldown cannot use a global actor dictionary' 'Code/core/lineage/SlaveService.cs' 'CaptureSearchNextAllowed'
Require-Absent 'slave catcher cooldown cannot run global pruning' 'Code/core/lineage/SlaveService.cs' 'PruneExpiredCaptureSearchCooldowns('
Require-Present 'capture scan cache has a hard state cap' 'Code/core/lineage/SlaveCaptureScanService.cs' 'MaxStates = 256'
Require-Present 'capture scan eviction is bounded per request' 'Code/core/lineage/SlaveCaptureScanService.cs' 'MaxEvictionsPerRequest = 8'
Require-Present 'capture scan completion uses an eviction queue' 'Code/core/lineage/SlaveCaptureScanService.cs' 'CompletedKeys.Enqueue(pState.key);'
Require-Absent 'capture scan cache cannot run full dictionary pruning' 'Code/core/lineage/SlaveCaptureScanService.cs' 'foreach (KeyValuePair<string, ScanState> entry in States)'
Require-Present 'capture scan waiter publication is bounded' 'Code/core/lineage/SlaveCaptureScanRules.cs' 'MaxWaiterNotificationsPerWorkItem = 4'
Require-Present 'capture scan waiter publication is deferred' 'Code/core/lineage/SlaveCaptureScanService.cs' '"slave_capture_waiters:" + pState.key'
Require-Absent 'capture scan completion cannot synchronously notify every waiting city' 'Code/core/lineage/SlaveCaptureScanService.cs' 'foreach (long cityId in pState.waitingCityIds)'
Require-Present 'temporary vanguard service exists' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'internal static class TemporarySlaveVanguardService'
Require-Present 'temporary vanguard uses kingdom coalescing key' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'CoalescingKey("slave_vanguard", pKingdomId)'
Require-Present 'temporary vanguard scans one city per work item' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'MaxCitiesPerWorkItem = 1'
Require-Present 'temporary vanguard uses 32-resident scan bound' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'MaxResidentsScannedPerWorkItem'
Require-Present 'temporary vanguard initial roster is atomic' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'FormInitialRosterAtomically('
Require-Present 'temporary vanguard uses special recruitment scope' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'MilitaryRecruitmentKind.SlaveVanguard'
Require-Present 'temporary vanguard cleanup is four actors' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'MaxActorsChangedPerWorkItem'
Require-Present 'ordinary armies wait for the indexed vanguard on the same front' `
    'Code/patch/AW_ArmySafetyPatch.cs' `
    'TemporarySlaveVanguardService.ShouldDelayBehindVanguard('
Require-Present 'vanguard assault ordering has an O(1) service gate' `
    'Code/core/lineage/TemporarySlaveVanguardService.cs' `
    'public static bool ShouldDelayBehindVanguard(Actor pActor)'
Require-Present 'invalid vanguard composition forces terminal cleanup' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'ForceCleanup'
Require-Absent 'forced vanguard cleanup cannot depend on a mutable cleaning flag' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'if (state.Cleaning && state.ForceCleanup)'
Require-Present 'vanguard casualties refresh cached deployment readiness' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'WarNoticeService.OnArmyChanged(kingdom, army, pRosterExpanded: false);'
Require-Present 'removed vanguards leave cached deployment blockers' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'WarNoticeService.OnArmyInvalidated(pKingdom, removedArmyId);'
Require-Present 'war notice activates slave vanguard' 'Code/core/lineage/WarNoticeService.cs' 'TemporarySlaveVanguardService.OnEmergencyChanged('
Require-Present 'war start activates slave vanguard' 'Code/patch/AW_WarPatch.cs' 'TemporarySlaveVanguardService.OnWarStarted(__result);'
Require-Present 'war end reevaluates slave vanguard' 'Code/patch/AW_WarPatch.cs' 'TemporarySlaveVanguardService.OnWarEnded(pWar);'
Require-Present 'temporary vanguard runtime rebuild' 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'TemporarySlaveVanguardService.RebuildRuntime'
Require-Present 'temporary vanguard runtime clear' 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'TemporarySlaveVanguardService.ClearRuntime'
Require-Present 'temporary vanguard persists bounded roster ids' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_SLAVE_VANGUARD_ROSTER_IDS'
Require-Present 'temporary vanguard rebuild reads roster ids' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'ReadRosterIds('
Require-Absent 'temporary vanguard cannot scan world units' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'World.world.units'
Require-Absent 'temporary vanguard cannot scan world armies' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'World.world.armies'
Require-Absent 'temporary vanguard cannot enumerate kingdom cities' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'getCities()'
Require-Present 'temporary vanguard indexes kingdom city list directly' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'pKingdom.cities'
Require-Present 'temporary vanguard indexes city residents directly' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'pCity.units'
Require-Present 'new slaves wake a dormant vanguard scan' 'Code/core/lineage/SlaveService.cs' 'TemporarySlaveVanguardService.OnCandidateAvailable('
Require-Present 'dead vanguard members invalidate by actor id' 'Code/patch/AW_ActorDeathPatch.cs' 'TemporarySlaveVanguardService.OnMemberInvalidated(__instance);'
Require-Present 'vanguard casualty scan restart is conditional' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'EnsureScanPassAvailable(state, kingdom);'
Require-Present 'dead levies invalidate by actor id' 'Code/patch/AW_ActorDeathPatch.cs' 'TemporaryLevyService.OnActorInvalidated(__instance);'
Require-Present 'freed vanguard members invalidate by actor id' 'Code/core/lineage/SlaveService.cs' 'TemporarySlaveVanguardService.OnMemberInvalidated(pActor);'
Require-Present 'nationality changes invalidate vanguard membership' 'Code/patch/AW_SlaveryPatch.cs' 'TemporarySlaveVanguardService.OnActorKingdomChanged(__instance, __state);'
Require-Present 'nationality changes invalidate levy membership' 'Code/patch/AW_SlaveryPatch.cs' 'TemporaryLevyService.OnActorInvalidated(__instance);'
Require-Present 'nationality changes release stale deployment' 'Code/patch/AW_SlaveryPatch.cs' 'ArmyDeploymentService.ReleaseActor(__instance, restoreJob: true);'
Require-Present 'nationality changes refresh the former kingdom army' 'Code/patch/AW_SlaveryPatch.cs' 'WarNoticeService.QueueArmyChanged(__state, __instance.army);'
Require-Present 'levy invalidation removes the hot actor index first' 'Code/core/lineage/TemporaryLevyService.cs' 'if (!ActiveActorIds.Remove(pActor.data.id)) return;'
Require-Absent 'retirement maintenance cannot drive vanguard work' 'Code/patch/AW_RetirementPatch.cs' 'TemporarySlaveVanguardService.OnEmergencyChanged'
Require-Absent 'legacy slave frontline cache is removed' 'Code/core/lineage/SlaveService.cs' 'FrontlineTargetCache'
Require-Absent 'legacy slave warrior-count cache is removed' 'Code/core/lineage/SlaveService.cs' 'CityWarriorCountCache'
Require-Absent 'slave identity checks cannot infer armies by roster scan' 'Code/core/lineage/SlaveService.cs' 'CountArmyComposition('
Require-Absent 'slave army naming cannot scan all armies' 'Code/core/lineage/SlaveService.cs' 'foreach (Army army in World.world.armies)'
Require-Absent 'obsolete city slave-army schedule key is removed' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_ARMY_LAST_CHECK'
Require-Absent 'obsolete city slave-army failure key is removed' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_ARMY_FAILURE_YEAR'
Require-Absent 'obsolete city slave-army cursor key is removed' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_ARMY_FILL_SCAN_CURSOR'
Require-Absent 'obsolete city slave-army continuation key is removed' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_ARMY_FILL_CONTINUE_TIME'
Require-Absent 'obsolete slave-army benchmark group is removed' 'Code/core/policy/CityMaintenanceBenchmarkRules.cs' 'public const string SlaveArmy ='
Require-Absent 'obsolete slave-army frontline benchmark is removed' 'Code/core/policy/CityMaintenanceBenchmarkRules.cs' 'SlaveArmyFrontlineScan'
Require-Absent 'obsolete slave-army maintenance rules are removed' 'Code/core/lineage/SlaveArmyMaintenanceRules.cs' 'ShouldRunMaintenance('
Require-Absent 'obsolete slave-army fill side effects are removed' 'Code/core/lineage/SlaveArmyFillSideEffectRules.cs' 'SlaveArmyFillSideEffectRules'
Require-Absent 'obsolete global special-army scan benchmark is removed' 'Code/core/policy/CityMaintenanceBenchmarkRules.cs' 'SpecialArmyGlobalScan'
Require-Present 'military emergencies use a kingdom war index' 'Code/core/lineage/MilitaryEmergencyService.cs' 'WarIdsByKingdom'
Require-Absent 'military emergency lookup cannot scan war manager' 'Code/core/lineage/MilitaryEmergencyService.cs' 'World.world?.wars?.hasWars('
Require-Present 'war start updates military emergency index' 'Code/patch/AW_WarPatch.cs' 'MilitaryEmergencyService.OnWarStarted(__result);'
Require-Present 'war end updates military emergency index' 'Code/patch/AW_WarPatch.cs' 'MilitaryEmergencyService.OnWarEnded(pWar);'
Require-Present 'late war participants enter emergency index' 'Code/patch/AW_WarPatch.cs' 'MilitaryEmergencyService.OnKingdomJoinedWar('
Require-Present 'departing war participants leave emergency index' 'Code/patch/AW_WarPatch.cs' 'MilitaryEmergencyService.OnKingdomLeftWar('
Require-Present 'levy emergency wakeup is deferred' 'Code/core/lineage/TemporaryLevyService.cs' 'CoalescingKey("levy_emergency"'
Require-Present 'levy annual recruitment is deferred' 'Code/core/lineage/TemporaryLevyService.cs' 'CoalescingKey("levy_recruit"'
Require-Present 'levy annual hook only schedules recruitment' 'Code/core/lineage/TemporaryLevyService.cs' 'ScheduleRecruitmentYear(pKingdom, year);'
Require-Absent 'levy annual hook cannot scan synchronously' 'Code/core/lineage/TemporaryLevyService.cs' 'private static void RecruitYearBatch('
Require-Present 'levy recruitment consumes cached frontier cities' 'Code/core/lineage/TemporaryLevyService.cs' 'ArmyDeploymentService.TryGetPreferredLevyCity('
Require-Present 'levy frontier cache has an independent cursor' 'Code/core/lineage/TemporaryLevyService.cs' 'public int PreferredCityCursor;'
Require-Present 'levy frontier cursor advances only after a cached hit' 'Code/core/lineage/TemporaryLevyService.cs' 'plan.PreferredCityCursor++;'
Require-Present 'same-year levy work resumes its remaining budget' 'Code/core/lineage/TemporaryLevyService.cs' 'ResumeRecruitmentYear(pKingdom, year);'
Require-Present 'same-year levy work restores a missing runtime plan' 'Code/core/lineage/TemporaryLevyService.cs' 'RestoreRecruitmentPlan(pKingdom, pYear)'
Require-Present 'levy batches persist their consumed budget' 'Code/core/lineage/TemporaryLevyService.cs' 'PersistRecruitmentPlan(kingdom, plan);'
Require-Present 'load rebuild resumes active levy plans' 'Code/core/lineage/TemporaryLevyService.cs' 'ResumeActiveRecruitmentPlans();'
Require-Present 'new notice immediately wakes attacker levies' 'Code/core/lineage/WarNoticeService.cs' 'TemporaryLevyService.OnEmergencyChanged(pAttacker);'
Require-Present 'new notice immediately wakes defender levies' 'Code/core/lineage/WarNoticeService.cs' 'TemporaryLevyService.OnEmergencyChanged(defender);'
Require-Present 'war preparation summary has a cached notice index' 'Code/core/lineage/WarNoticeService.cs' 'SummaryNoticeByKingdom'
Require-Present 'repeat notice indexing preserves cached summary' 'Code/core/lineage/WarNoticeService.cs' 'if (added) RefreshPreparationSummaries(pState);'
Require-Present 'war preparation summary reads cached levy count' 'Code/core/lineage/WarNoticeService.cs' 'TemporaryLevyService.ActiveLevyCount(pKingdom)'
Require-Present 'war preparation summary reads cached deployment state' 'Code/core/lineage/WarNoticeService.cs' 'ArmyDeploymentService.TryGetCachedReadiness('
Require-Present 'policy window reads cached war preparation summary' 'Code/ui/windows/KingdomPolicyWindow.cs' 'WarNoticeService.TryGetPreparationSummary('
Require-Present 'policy window shows war preparation status' 'Code/ui/windows/KingdomPolicyWindow.cs' 'BuildWarPreparationStatus('
Require-Present 'war preparation status supports vanilla tooltip hover' 'Code/ui/windows/KingdomPolicyWindow.cs' 'typeof(Image), typeof(Button), typeof(TipButton));'
Require-Present 'war preparation status is not a click command' 'Code/ui/windows/KingdomPolicyWindow.cs' 'tip.showOnClick = false;'
Require-Present 'war preparation locale' 'Locales/aw3_war_decisions.csv' 'aw_war_preparation,'
Require-Present 'war notice target locale' 'Locales/aw3_war_decisions.csv' 'aw_war_notice_target,'
Require-Present 'war notice years locale' 'Locales/aw3_war_decisions.csv' 'aw_war_notice_window,'
Require-Present 'war levy count locale' 'Locales/aw3_war_decisions.csv' 'aw_war_levy_count,'
Require-Present 'war deployment ready locale' 'Locales/aw3_war_decisions.csv' 'aw_war_deployment_ready,'
Require-Present 'war deployment preparing locale' 'Locales/aw3_war_decisions.csv' 'aw_war_deployment_preparing,'
Require-Present 'military emergency runtime rebuild' 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'MilitaryEmergencyService.RebuildRuntime'
Require-Present 'military emergency runtime clear' 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'MilitaryEmergencyService.ClearRuntime'
Require-Present 'temporary levies use indexed emergency lookup' 'Code/core/lineage/TemporaryLevyService.cs' 'MilitaryEmergencyService.HasAny(pKingdom)'
Require-Absent 'temporary levies cannot enumerate all active wars' 'Code/core/lineage/TemporaryLevyService.cs' 'foreach (War war in pKingdom.getWars())'
Require-Absent 'temporary levy recruitment cannot sort all cities' 'Code/core/lineage/TemporaryLevyService.cs' '.Sort('
Require-Absent 'temporary levy recruitment cannot enumerate kingdom cities' 'Code/core/lineage/TemporaryLevyService.cs' 'pKingdom.getCities()'
Require-Present 'temporary levy recruitment indexes kingdom cities directly' 'Code/core/lineage/TemporaryLevyService.cs' 'pKingdom.cities'
Require-Absent 'deployment cannot enumerate dirty kingdom city iterator' 'Code/core/lineage/ArmyDeploymentService.cs' 'pKingdom.getCities()'
Require-Absent 'deployment target discovery cannot enumerate dirty kingdom city iterator' 'Code/core/lineage/ArmyDeploymentService.cs' 'pDefender.getCities()'
Require-Absent 'deployment cannot scan all world armies' 'Code/core/lineage/ArmyDeploymentService.cs' 'foreach (Army army in World.world.armies)'
Require-Present 'special armies have kingdom role index' 'Code/core/lineage/AWArmyService.cs' 'RoleArmyIdsByKingdomRole'
Require-Present 'kingdom-unique special army lookup is indexed' 'Code/core/lineage/AWArmyService.cs' 'TryGetRoleArmy('
Require-Present 'special army lookup cache has reverse index' 'Code/core/lineage/AWArmyService.cs' 'LookupCacheKeysByArmy'
Require-Present 'deployment uses indexed special armies' 'Code/core/lineage/ArmyDeploymentService.cs' 'AWArmyService.GetRoleArmies('
Require-Present 'new border armies update deployment incrementally' 'Code/core/lineage/MandateBorderDefenseService.cs' 'WarNoticeService.OnArmyChanged(owner, borderArmy);'
Require-Present 'deployment caches facing cities per notice' 'Code/core/lineage/ArmyDeploymentService.cs' 'TargetCityIds'
Require-Present 'deployment caches required army ids per notice' 'Code/core/lineage/ArmyDeploymentService.cs' 'RequiredArmyIds'
Require-Present 'deployment arrival is indexed by army' 'Code/core/lineage/ArmyDeploymentService.cs' 'ArrivedArmyIds'
Require-Present 'deployment arrival requires the army captain' 'Code/core/lineage/ArmyDeploymentService.cs' 'ArmyDeploymentRules.ShouldMarkArmyArrived('
Require-Present 'repeat deployment arrivals do not enqueue refresh work' 'Code/core/lineage/ArmyDeploymentService.cs' 'if (!assignments.ArrivedArmyIds.Add(pActor.army.id)) return;'
Require-Present 'deployment caches exact frontier tile identities' 'Code/core/lineage/ArmyDeploymentService.cs' 'TargetTileByArmy'
Require-Absent 'deployment movement cannot allocate offset matrices' 'Code/core/lineage/ArmyDeploymentService.cs' 'int[,] offsets ='
Require-Present 'deployment actor mutation batch is bounded' 'Code/core/lineage/ArmyDeploymentService.cs' 'ActorMutationBatchSize = 16'
Require-Present 'deployment city discovery batch is bounded' 'Code/core/lineage/ArmyDeploymentRules.cs' 'MaxCitiesDiscoveredPerWorkItem = 8'
Require-Present 'deployment army review batch is bounded' 'Code/core/lineage/ArmyDeploymentRules.cs' 'MaxArmiesReviewedPerWorkItem = 8'
Require-Present 'deployment discovery is deferred and coalesced' 'Code/core/lineage/ArmyDeploymentService.cs' '"deployment_discovery:" + (pSignature ?? "")'
Require-Present 'deployment readiness gate uses cached blockers' 'Code/core/lineage/ArmyDeploymentService.cs' 'BlockingArmyIds.Count == 0'
Require-Present 'new warriors coalesce deployment refresh by army id' 'Code/patch/AW_EnlistPatch.cs' 'WarNoticeService.QueueArmyChanged('
Require-Present 'new warriors preserve roster expansion semantics' 'Code/patch/AW_EnlistPatch.cs' 'pRosterExpanded: true'
Require-Absent 'warrior enlistment cannot synchronously fan out notices' 'Code/patch/AW_EnlistPatch.cs' 'WarNoticeService.OnArmyChanged('
Require-Present 'levy enlistment coalesces deployment refresh' 'Code/core/lineage/TemporaryLevyService.cs' 'WarNoticeService.QueueArmyChanged(pKingdom, pActor.army, pRosterExpanded: true);'
Require-Absent 'levy enlistment cannot synchronously fan out notices' 'Code/core/lineage/TemporaryLevyService.cs' 'WarNoticeService.OnArmyChanged(pKingdom, pActor.army);'
Require-Present 'army losses coalesce deployment refresh by army id' 'Code/core/lineage/WarNoticeService.cs' 'CoalescingKey("deployment_army_changed", armyId)'
Require-Present 'coalesced army changes retain expansion state' 'Code/core/lineage/WarNoticeService.cs' 'PendingExpandedArmyIds'
Require-Present 'actor death refreshes only its former army' 'Code/patch/AW_ActorDeathPatch.cs' 'WarNoticeService.QueueArmyChanged(__instance.kingdom, __instance.army);'
Require-Present 'warrior demotion refreshes only its former army' 'Code/patch/AW_EnlistPatch.cs' 'WarNoticeService.QueueArmyChanged(__instance.kingdom, __instance.army);'
Require-Present 'warrior demotion releases actor deployment immediately' 'Code/patch/AW_EnlistPatch.cs' 'ArmyDeploymentService.ReleaseActor(__instance, restoreJob: true);'
Require-Present 'warrior demotion removes stale temporary levy index immediately' 'Code/patch/AW_EnlistPatch.cs' 'TemporaryLevyService.OnActorInvalidated(__instance);'
Require-Present 'deployment border lookup verifies a tile touching opponent land' 'Code/core/lineage/ArmyDeploymentService.cs' 'TouchesOpponentLand('
Require-Absent 'deployment border lookup cannot enumerate neighbour cities' 'Code/core/lineage/ArmyDeploymentService.cs' 'foreach (City other in pCity.neighbours_cities)'
Require-Present 'deployment actor changes use deferred queue' 'Code/core/lineage/ArmyDeploymentService.cs' 'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'deployment cancellation is phased' 'Code/core/lineage/ArmyDeploymentService.cs' 'assignments.Closing = true;'
Require-Present 'closing deployment removes its own member ids directly' 'Code/core/lineage/ArmyDeploymentService.cs' 'assignments.ActorIds.Remove(batch[i]);'
Require-Present 'closing deployment preserves actors reassigned to another notice' 'Code/core/lineage/ArmyDeploymentService.cs' 'ArmyDeploymentRules.ShouldClearForClosingNotice('
Require-Absent 'closing deployment cannot release whichever notice happens to be current' 'Code/core/lineage/ArmyDeploymentService.cs' 'ReleaseActor(actor, assignments.RestoreJobs);'
Require-Present 'stale deployment task restores normal job' 'Code/ai/behaviours/actor/BehWarDeploymentMove.cs' 'pActor.ai?.setJob(pActor.getNextJob())'
Require-Absent 'deployment cannot sort all facing cities' 'Code/core/lineage/ArmyDeploymentService.cs' 'selected.Sort('
Require-Absent 'deployment cannot enumerate army members through iterator' 'Code/core/lineage/ArmyDeploymentService.cs' 'foreach (Actor actor in pArmy.getUnits())'
Require-Absent 'temporary levy no longer allocates city priority snapshots' 'Code/core/lineage/TemporaryLevyService.cs' 'CityPrioritySnapshot'
Require-Present 'city maintenance invokes standing army' 'Code/patch/AW_RetirementPatch.cs' 'StandingArmyService.MaintainCity(pCity);'
Require-Present 'guard readiness reads direct ordinary army counts' 'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'StandingArmyService.CountOrdinaryStandingFast('
Require-Present 'normal-army guard cleanup uses the bounded roster index' 'Code/core/lineage/RoyalGuardService.cs' 'List<long> rosterIds = ReadGuardRosterIds(kingdom);'
Require-Absent 'normal-army guard cleanup cannot copy and scan the full army' 'Code/core/lineage/RoyalGuardService.cs' 'new List<Actor>(pArmy.getUnits())'
Require-Present 'guard cursor scans index kingdom units directly' 'Code/core/lineage/RoyalGuardService.cs' 'List<Actor> units = pKingdom.units;'
Require-Absent 'guard cursor scans cannot re-enumerate skipped prefixes' 'Code/core/lineage/RoyalGuardService.cs' 'scanned++ < cursor'
Require-Absent 'guard candidate cursor cannot re-enumerate skipped prefixes' 'Code/core/lineage/RoyalGuardService.cs' 'skipped++ < cursor'
Require-Present 'guard readiness indexes kingdom cities directly' 'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'pKingdom.cities'
Require-Absent 'guard readiness cannot allocate dirty city iterators' 'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'pKingdom.getCities()'
Require-Absent 'guard readiness cannot scan standing army actors' 'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'StandingArmyService.CountOrdinaryStanding(city)'
Require-Present 'standing maintenance retains only bounded weakest actors' 'Code/core/lineage/StandingArmyService.cs' 'AddBoundedWeakest('
Require-Absent 'standing maintenance cannot sort the full standing army' 'Code/core/lineage/StandingArmyService.cs' 'pStanding.Sort(CompareWeakestFirst)'
Require-Absent 'standing army cannot run from actor updateAge' 'Code/patch/AW_RetirementPatch.cs' 'UpdateAge_Postfix(Actor __instance)\n        {\n            StandingArmyService'
Require-Present 'special army attachment rejects royal refugees' 'Code/core/lineage/AWArmyService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'royal refugee next job stays asylum job' 'Code/patch/AW_EnlistPatch.cs' '__result = RoyalAsylumContent.ActorJobId;'
Require-Present 'city leader selection rejects royal refugees' 'Code/patch/AW_CityLeaderPatch.cs' 'RoyalAsylumService.IsActive(pUnit)'
Require-Present 'court appointment rejects royal refugees' 'Code/core/court/CourtService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'general selection rejects royal refugees' 'Code/core/lineage/GeneralService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'royal guard selection rejects royal refugees' 'Code/core/lineage/RoyalGuardService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'slave army selection rejects royal refugees' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'new heir is recalled from royal asylum' 'Code/core/lineage/HeirService.cs' 'RoyalAsylumService.RecallForSuccession(heir, pKingdom);'
Require-Present 'new king is recalled from royal asylum' 'Code/patch/AW_PromotionPatch.cs' 'RoyalAsylumService.RecallForSuccession(pActor, __instance)'
Require-Present 'royal asylum started person event key' 'Code/core/lineage/ChronicleKeys.cs' 'ROYAL_ASYLUM_STARTED = "royal_asylum_started"'
Require-Present 'royal asylum relocated person event key' 'Code/core/lineage/ChronicleKeys.cs' 'ROYAL_ASYLUM_RELOCATED = "royal_asylum_relocated"'
Require-Present 'royal asylum returned person event key' 'Code/core/lineage/ChronicleKeys.cs' 'ROYAL_ASYLUM_RETURNED = "royal_asylum_returned"'
Require-Present 'royal asylum naturalized person event key' 'Code/core/lineage/ChronicleKeys.cs' 'ROYAL_ASYLUM_NATURALIZED = "royal_asylum_naturalized"'
Require-Present 'royal asylum history service exists' 'Code/core/lineage/RoyalAsylumHistoryService.cs' 'internal static class RoyalAsylumHistoryService'
Require-Present 'evacuation records asylum start once' 'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumHistoryService.RecordStarted(pActor, pHome, hostCity);'
Require-Present 'host change records asylum relocation once' 'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumHistoryService.RecordRelocated(pActor, pHome, hostCity);'
Require-Present 'return records asylum completion once' 'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumHistoryService.RecordReturned(pActor, pHome, destination);'
Require-Present 'extinction records asylum naturalization once' 'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumHistoryService.RecordNaturalized(actor, homeName, host, hostCity);'
Require-Present 'history window localizes asylum event labels' 'Code/core/lineage/WarDisplayLabelRules.cs' 'case "royal_asylum_started"'
Require-Present 'royal asylum started history localization' 'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_royal_asylum_started'
Require-Present 'royal asylum relocated history localization' 'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_royal_asylum_relocated'
Require-Present 'royal asylum returned history localization' 'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_royal_asylum_returned'
Require-Present 'royal asylum naturalized history localization' 'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_royal_asylum_naturalized'
Require-Present 'actor window shows logical asylum host' 'Code/patch/AW_UnitWindowPatch.cs' 'RoyalAsylumService.ResolveHostCity(actor)'
Require-Present 'actor window labels asylum host row' 'Code/patch/AW_UnitWindowPatch.cs' 'ShowRawRow(__instance, "aw_royal_asylum_host"'
Require-Present 'empty target city attack uses target zones' 'Code/core/lineage/CityAttackZoneService.cs' 'targetCity.zones.GetRandom()'
Require-Absent 'empty target city attack cannot use source zones' 'Code/core/lineage/CityAttackZoneService.cs' 'pSourceCity.zones.GetRandom()'
Require-Present 'army leaders outside a city zone recover toward the attack zone' `
    'Code/patch/AW_ArmySafetyPatch.cs' `
    'TryRecoverMissingCurrentZone(pActor,'
Require-Absent 'heir minimap cannot scan every kingdom unit' 'Code/patch/AW_HeirMinimapPatch.cs' 'foreach (Actor unit in kingdom.getUnits())'
Require-Present 'heir minimap uses stored heir index without succession mutation' 'Code/patch/AW_HeirMinimapPatch.cs' 'Actor unit = HeirService.PeekStoredHeirForMinimap(kingdom);'
Require-Present 'heir minimap resolves current visual affiliation' 'Code/patch/AW_HeirMinimapPatch.cs' 'HeirMinimapVisualRules.ResolveVisualKingdomId('
Require-Present 'heir minimap colors from resolved affiliation' 'Code/patch/AW_HeirMinimapPatch.cs' 'DynamicSprites.getIcon(baseIcon, visualKingdom.getColor())'
Require-Present 'heir minimap display lookup exists' 'Code/core/lineage/HeirService.cs' 'public static Actor PeekStoredHeirForMinimap(Kingdom pKingdom)'
Require-Present 'heir flag clear counts other live registrations' 'Code/core/lineage/HeirService.cs' 'CountOtherLiveHeirRegistrations(oldId, pKingdom)'
Require-Present 'heir flag clear applies shared registration rule' 'Code/core/lineage/HeirService.cs' 'HeirRegistrationRules.ShouldClearGlobalFlag(otherRegistrations)'
Require-Present 'heir registration scan applies tested eligibility rule' 'Code/core/lineage/HeirService.cs' 'HeirRegistrationRules.CountsAsOtherLiveRegistration('
Require-Absent 'heir minimap trusts kingdom registration instead of global actor flag' 'Code/core/lineage/HeirService.cs' 'heir.data.get(LineageKeys.IS_HEIR'
Require-Present 'heir minimap follows king marker visibility option' 'Code/patch/AW_HeirMinimapPatch.cs' 'PlayerConfig.optionBoolEnabled("map_kings_leaders")'
Require-Present 'heir minimap uses complete visibility rule' 'Code/patch/AW_HeirMinimapPatch.cs' 'HeirMinimapVisualRules.ShouldDrawIcon('
Require-Present 'heir minimap bounds quantum sprite growth' 'Code/patch/AW_HeirMinimapPatch.cs' 'if (createdThisFrame > 2) break;'
Require-Present 'heir minimap reuses one actor marker index' 'Code/patch/AW_HeirMinimapPatch.cs' 'private static readonly HashSet<long> DrawnHeirActorIds'
Require-Present 'heir minimap resets marker index once per draw' 'Code/patch/AW_HeirMinimapPatch.cs' 'DrawnHeirActorIds.Clear();'
Require-Present 'heir minimap deduplicates cross-realm registrations' 'Code/patch/AW_HeirMinimapPatch.cs' 'MinimapActorMarkerRules.TryReserve(DrawnHeirActorIds, unit.data.id)'
Require-Present 'historical figures preserve intended favorite protection' 'Code/content/figures/HistoricalFigureService.cs' 'pActor.data.favorite = true;'
Require-Present 'historical figure replaces favorite draw in one pass' 'Code/patch/AW_FigurePatch.cs' 'public static bool DrawFavoritesMap_Figure_Prefix(QuantumSpriteAsset pAsset)'
Require-Absent 'historical figure cannot append a second favorite marker pass' 'Code/patch/AW_FigurePatch.cs' 'DrawFavoritesMap_Figure_Postfix'
Require-Present 'historical figure replacement preserves ordinary favorite star' 'Code/patch/AW_FigurePatch.cs' 'SpriteTextureLoader.getSprite("ui/Icons/iconFavoriteStar_Map")'
Require-Present 'historical figure replacement reuses visible favorite index' 'Code/patch/AW_FigurePatch.cs' 'World.world.units.visible_units_with_favorite.array'
Require-Present 'historical figure minimap follows favorite marker option' 'Code/patch/AW_FigurePatch.cs' 'PlayerConfig.optionBoolEnabled("marks_favorites")'
Require-Present 'historical figure minimap resolves current visual affiliation' 'Code/patch/AW_FigurePatch.cs' 'HeirMinimapVisualRules.ResolveVisualKingdomId('
Require-Present 'historical figure minimap colors from resolved affiliation' 'Code/patch/AW_FigurePatch.cs' 'DynamicSprites.getIcon(baseIcon, visualKingdom.getColor())'
Require-Present 'historical figure minimap uses authoritative FigureState identity' 'Code/patch/AW_FigurePatch.cs' 'FigureStateStore.IndexOfActor(unit.data.id) >= 0'
Require-Absent 'historical figure minimap cannot trust figure trait alone' 'Code/patch/AW_FigurePatch.cs' 'unit.hasTrait(HistoricalFigureService.TRAIT_FIGURE)'
Require-Absent 'historical figure minimap cannot trust first trait alone' 'Code/patch/AW_FigurePatch.cs' 'unit.hasTrait(HistoricalFigureService.TRAIT_FIRST)'
Require-Absent 'king is never persisted as a court officer' 'Code/core/court/CourtService.cs' '"king_council"'
Require-Present 'new king clears prior court office' `
    'Code/core/lineage/AccessionIdentityService.cs' `
    'CourtService.ClearOfficeForReignTransition(pActor, "became_king")'
Require-Present 'new king uses the atomic accession identity transition' `
    'Code/patch/AW_HeirPatch.cs' 'AccessionIdentityService.Commit(__instance, king)'
Require-Present 'new king identity is prepared before vanilla setKing mutates the throne' `
    'Code/patch/AW_HeirPatch.cs' 'AccessionIdentityService.Prepare(__instance, pActor)'
Require-Present 'registered heir leaves royal guard before heir id is committed' `
    'Code/core/lineage/HeirService.cs' `
    'RoyalGuardService.ReleaseForRegisteredHeir('
Require-Present 'accession retries idempotent registered-heir guard cleanup' `
    'Code/core/lineage/AccessionIdentityService.cs' `
    'RoyalGuardService.ReleaseForRegisteredHeir('
Require-Present 'setKing guard gate supports registered-heir cleanup only' `
    'Code/patch/AW_RoyalGuardPatch.cs' `
    'RoyalGuardService.ReleaseForRegisteredHeir('
Require-Present 'realm crisis uses the bounded ruler weakness rule' `
    'Code/core/lineage/GeneralRebellionService.cs' `
    'GeneralRebellionRules.RulerWeaknessScore('
Require-Absent 'army march cannot suppress vanilla random movement used for transport recovery' `
    'Code/patch/AW_ArmySafetyPatch.cs' `
    '[HarmonyPatch(typeof(BehFindRandomTile), nameof(BehFindRandomTile.execute))]'
Require-Present 'failed follower correction stops before vanilla path submission' `
    'Code/patch/AW_ArmySafetyPatch.cs' `
    '__result = BehResult.Stop;'
Require-Present 'submitted shared army march requires the follower to remain on the captain island' `
    'Code/core/lineage/AWArmyMarchService.cs' `
    'ArmyMarchRules.ShouldOwnFollowerMarch('
Require-Absent 'pathfinding ownership changes are not written to the runtime log' `
    'Code/core/pathfinding/AWPathfindingBootstrap.cs' `
    'AW3 pathfinding owner:'
Require-Present 'pathfinding diagnostics are drained without runtime log spam' `
    'Code/core/pathfinding/AWPathfindingBootstrap.cs' `
    'Diagnostics.DrainAndMaybeLog(32,'
Require-Present 'pathfinding diagnostics disable the runtime logger' `
    'Code/core/pathfinding/AWPathfindingBootstrap.cs' `
    '_finder?.QueueDepth ?? 0, null);'
Require-Present 'bounded military water recovery authorizes its validated first swim step' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'AWNarrowWaterRecoveryRules.CanEnterDamagingWater('
Require-Present 'wartime field armies may launch when their local standing core is complete' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'TemporaryLevyRules.CanLaunchEmergencyArmy('
Require-Absent 'single-city launch readiness cannot depend on a kingdom-wide levy pool' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'temporaryLevyPoolActive: TemporaryLevyService.HasActivePool('
Require-Absent 'realm crisis cannot treat ordinary ruler stats as near maximum weakness' `
    'Code/core/lineage/GeneralRebellionService.cs' '* 0.08f'
Require-Present 'an intact royal house requires an extreme crisis before a coup can succeed' `
    'Code/core/court/MinisterialPowerRules.cs' `
    'if (intactRoyalHouse && boundedCrisis < 90) return false;'
Require-Present 'failed accession preparation cancels vanilla setKing' `
    'Code/patch/AW_HeirPatch.cs' 'if (!__state.IdentityPrepared) return false;'
Require-Present 'skipped setKing cannot mutate republic state from a postfix' `
    'Code/patch/AW_RepublicGovernmentPatch.cs' 'if (!__runOriginal || pFromLoad'
Require-Present 'accession preparation validates the destination capital before closing guest service' `
    'Code/core/lineage/AccessionIdentityService.cs' 'if (!TryGetValidCapital(pKingdom, out City capital)) return false;'
Require-Present 'accession clears a stale guest status without a serving affiliation' `
    'Code/core/lineage/AccessionIdentityService.cs' 'ClearGuestStatus(pActor);'
Require-Present 'accession clears the canonical school guest status' `
    'Code/core/lineage/AccessionIdentityService.cs' 'HistoricalSchoolContent.GuestStatusId'
Require-Present 'first capital finalizes deferred founding identity' `
    'Code/patch/AW_HeirPatch.cs' 'AccessionIdentityService.FinalizeDeferredFounding(__instance);'
Require-Present 'deferred founding establishes hereditary monarchy' `
    'Code/core/lineage/AccessionIdentityService.cs' 'RepublicGovernmentService.MarkMonarchyEstablished(pKingdom);'
Require-Present 'deferred founding immediately refreshes the heir' `
    'Code/core/lineage/AccessionIdentityService.cs' 'HeirService.RefreshHeir(pKingdom);'
$heirPatch = Read-Source 'Code/patch/AW_HeirPatch.cs'
$accessionCommit = $heirPatch.IndexOf(
    'AccessionIdentityService.Commit(__instance, king)',
    [System.StringComparison]::Ordinal)
$accessionBranch = $heirPatch.IndexOf(
    'LineageService.OnKingFoundBranch(__instance, king',
    [System.StringComparison]::Ordinal)
if ($accessionCommit -lt 0 -or $accessionBranch -lt 0 -or
    $accessionCommit -gt $accessionBranch) {
    $failures.Add('new king identity must commit before cadet branch creation')
}
foreach ($accessionContract in @(
    @{ Name = 'accession closes guest office with the dual-table transaction'; Text = 'GuestOfficeEndPersistence.PrepareEnd(' },
    @{ Name = 'accession commits the guest office end transaction'; Text = 'GuestOfficeEndPersistence.End(' },
    @{ Name = 'accession adopts the committed guest affiliation'; Text = 'HistoricalAffiliationService.AdoptCommittedServiceEnd(' },
    @{ Name = 'accession retires an old general role'; Text = 'GeneralService.RetireForSuccession(pActor)' },
    @{ Name = 'accession dismisses an old royal guard role'; Text = 'RoyalGuardService.DismissGuard(pActor, "became_king")' },
    @{ Name = 'accession clears temporary levy state'; Text = 'TemporaryLevyService.OnActorInvalidated(pActor)' },
    @{ Name = 'accession clears wartime garrison state'; Text = 'WartimeGarrisonService.OnActorInvalidated(pActor)' },
    @{ Name = 'accession frees an enslaved ruler through the canonical service'; Text = 'SlaveService.FreeSlave(pActor, "became_king")' },
    @{ Name = 'accession clears captive title state'; Text = 'pActor.data.set(LineageKeys.CAPTIVE_NOBLE_TITLE, "")' },
    @{ Name = 'accession clears captive color state'; Text = 'pActor.data.set(LineageKeys.CAPTIVE_NOBLE_COLOR, "")' },
    @{ Name = 'accession opens a scoped capital transfer'; Text = 'FormalAffiliationTransferScope.Open(' },
    @{ Name = 'accession formally joins the destination kingdom'; Text = 'pActor.joinKingdom(pKingdom)' },
    @{ Name = 'accession formally joins the destination capital'; Text = 'pActor.joinCity(capital)' }
)) {
    Require-Present $accessionContract.Name `
        'Code/core/lineage/AccessionIdentityService.cs' `
        $accessionContract.Text
}
Require-Present 'king accession branch requires a foreign throne' `
    'Code/core/lineage/LineageService.cs' `
    'foreignThrone: originKingdom >= 0 && originKingdom != pKingdom.id'
Require-Present 'king accession cannot substitute city influence for a foreign throne' `
    'Code/core/lineage/LineageService.cs' 'highInfluenceElsewhere: false'
Require-Present 'ruler state-name binding compares the previous dynasty Shi' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'DynastyRecordWriter.GetCurrentDynastyShiId(pKingdom.id)'
Require-Present 'ruler state-name binding applies the empire-only rename rule' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'StateNameRules.ShouldProjectDynasticStateName('
Require-Present 'dynastic replacement reads only an existing Shi state name' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'StateNameService.GetBoundStateName(pShiId)'
Require-Present 'dynastic replacement projects an existing state name without binding one' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'StateNameService.ProjectExistingStateName('
Require-Present 'dynasty snapshots use the canonical live kingdom name' `
    'Code/core/lineage/DynastyRecordWriter.cs' `
    'string stateName = pKingdom.name ?? "";'
Require-Present 'reigns use temporal dynasty assignment instead of latest fallback' `
    'Code/core/lineage/HistoryQuery.cs' `
    'HistoryDynastyAssignmentRules.SelectIndex('
Require-Present 'kingdom history builds explicit destruction and restoration periods' `
    'Code/core/lineage/HistoryQuery.cs' `
    'HistoryDynastyAssignmentRules.BuildReignTimeline('
Require-Absent 'kingdom history cannot stretch a reign across kingdom destruction' `
    'Code/core/lineage/HistoryQuery.cs' `
    'if (e.event_type == KingdomEvent.DESTROYED) destroyedTime = e.world_time;'
Require-Absent 'kingdom rename cannot enumerate every archive table' `
    'Code/core/lineage/KingdomRenameSyncService.cs' `
    'sqlite_master'
Require-Absent 'kingdom rename cannot rewrite arbitrary snapshot columns' `
    'Code/core/lineage/KingdomRenameSyncService.cs' `
    'NAME_COLUMN_PAIRS'
Require-Absent 'kingdom rename cannot bulk-sync historical names' `
    'Code/core/lineage/KingdomRenameSyncService.cs' `
    'SyncNameSnapshots('
Require-Present 'kingdom rename uses one live projection refresh entrypoint' `
    'Code/patch/AW_KingdomRenamePatch.cs' `
    'KingdomRenameProjectionService.Refresh(kingdom)'
Require-Present 'kingdom rename history uses the committed object name' `
    'Code/patch/AW_KingdomRenamePatch.cs' `
    'string committedName = kingdom.name ?? kingdom.data?.name'
Require-Present 'kingdom rename invalidates the original nameplate text cache' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'World.world?.nameplate_manager?.clearCaches()'
Require-Present 'kingdom rename refreshes the ruler appellation cache' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'RulerAppellationService.RefreshLivingProjection(pKingdom)'
Require-Present 'nameplate appellations validate their source state name and suffix' `
    'Code/core/lineage/RulerAppellationService.cs' `
    'string.Equals(cached.Suffix, suffix,'
Require-Present 'stale nameplate appellations fall back to the live state name' `
    'Code/core/lineage/RulerAppellationRules.cs' `
    'ResolveLiveProjection('
Require-Present 'kingdom rename invalidates Mandate core map labels' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'MandateCoreMapModeService.DirtyMapIfActive()'
Require-Present 'kingdom rename invalidates technology map labels' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'TechMapModeService.DirtyMapIfActive()'
Require-Present 'kingdom rename invalidates development map labels' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'DevelopmentMapModeService.DirtyMapIfActive()'
Require-Present 'kingdom rename invalidates war-claim map labels' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'WarClaimMapModeService.DirtyMapIfActive()'
Require-Present 'kingdom rename invalidates war-core map labels' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'WarCoreMapModeService.DirtyMapIfActive()'
Require-Present 'war notices expose an indexed bilateral preparation check' `
    'Code/core/lineage/WarNoticeService.cs' `
    'HasActiveNoticeBetween('
Require-Present 'diplomacy availability reads bilateral war preparation' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'WarNoticeService.HasActiveNoticeBetween('
Require-Present 'all AI proposal creation uses receiver pre-assessment' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'DiplomacyProposalRules.CanSendAiProposal('
Require-Present 'AI proposal creation reads pair-type rejection cooldown' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'HasRecentAiRejection('
Require-Present 'war preparation diplomacy failure is localized' `
    'Locales/aw3_diplomacy.csv' `
    'aw_diplomacy_failure_war_preparation,'
Require-Present 'diplomacy UI explains the war preparation blocker' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    '"war_preparation" => AW_L10n.Text('
Require-Present 'noble trait grants the requested health reserve' `
    'Code/content/XiaTraits.cs' `
    'guizu.base_stats["health"] = 500f;'
$slaveCaptureSource = Read-Source 'Code/core/lineage/SlaveService.cs'
$nobleChanceUseCount = [regex]::Matches($slaveCaptureSource,
    'NobleCaptureRules\.ResolveChance\(').Count
if ($nobleChanceUseCount -lt 4) {
    $failures.Add('battlefield, catcher, city-fall, and occupation capture must all use NobleCaptureRules')
}
Require-Present 'path movement retries unreachable soldiers through bounded water' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'TryStartNarrowWaterRecovery('
Require-Present 'streaming search tracks consecutive recovery water tiles' `
    'Code/core/pathfinding/AWStreamingPathGenerator.cs' `
    'MaximumConsecutiveWaterTiles'
$pathMovementBridge = Read-Source 'Code/core/pathfinding/AWPathMovementBridge.cs'
$pathFailureStart = $pathMovementBridge.IndexOf(
    'private static void HandleFailure(', [System.StringComparison]::Ordinal)
$pathRetryStart = $pathMovementBridge.IndexOf(
    'private static bool TryStartDueRetry(', [System.StringComparison]::Ordinal)
if ($pathFailureStart -lt 0 -or $pathRetryStart -lt 0 -or
    -not $pathMovementBridge.Substring($pathFailureStart,
        $pathRetryStart - $pathFailureStart).Contains('pActor.cancelAllBeh();')) {
    $failures.Add('terminal path failure must clear the stale actor behavior stack')
}
Require-Present 'all null-data or dead armies are blocked before original save' `
    'Code/patch/AW_ArmySafetyPatch.cs' `
    'ArmyCreationSafetyRules.ShouldSkipSave(hasData, alive)'
Require-Present 'invalid armies are removed after save enumeration' `
    'Code/patch/AW_ArmySafetyPatch.cs' `
    'ArmyInvalidCleanupQueue.Schedule(__instance)'
Require-Present 'special-army creation rolls back half-created objects' `
    'Code/core/lineage/AWArmyService.cs' `
    'ArmyInvalidCleanupQueue.RemoveFailedCreation(army,'
Require-Present 'royal-guard creation rolls back half-created objects' `
    'Code/core/lineage/RoyalGuardService.cs' `
    'ArmyInvalidCleanupQueue.RemoveFailedCreation(army,'
$armySaveSource = Read-Source 'Code/patch/AW_ArmySafetyPatch.cs'
$armySaveStart = $armySaveSource.IndexOf(
    'public static bool ArmySave_Prefix(', [System.StringComparison]::Ordinal)
$armyDisposeStart = $armySaveSource.IndexOf(
    'public static void ArmyDispose_Prefix(', [System.StringComparison]::Ordinal)
if ($armySaveStart -lt 0 -or $armyDisposeStart -lt 0 -or
    $armySaveSource.Substring($armySaveStart,
        $armyDisposeStart - $armySaveStart).Contains(
            'if (__instance.data == null) return true;')) {
    $failures.Add('Army.save must never pass a null-data army to vanilla save')
}
$lineageLiveTitles = Read-Source 'Code/core/lineage/LineageQuery.cs'
$liveKingPriority = $lineageLiveTitles.IndexOf('if (pLive.isKing())',
    [System.StringComparison]::Ordinal)
$liveFormerKingPriority = $lineageLiveTitles.IndexOf(
    'pLive.data.get(LineageKeys.FORMER_KING_TITLE',
    [System.StringComparison]::Ordinal)
$liveNobleTitlePriority = $lineageLiveTitles.IndexOf(
    'DynasticTitleService.ResolveLivingTitle(pLive)',
    [System.StringComparison]::Ordinal)
$liveCaptivePriority = $lineageLiveTitles.IndexOf(
    'pLive.data.get(LineageKeys.CAPTIVE_NOBLE_TITLE',
    [System.StringComparison]::Ordinal)
if ($liveKingPriority -lt 0 -or $liveFormerKingPriority -lt 0 -or
    $liveNobleTitlePriority -lt 0 -or
    $liveCaptivePriority -lt 0 -or
    $liveKingPriority -gt $liveFormerKingPriority -or
    $liveFormerKingPriority -gt $liveNobleTitlePriority -or
    $liveNobleTitlePriority -gt $liveCaptivePriority) {
    $failures.Add('live family tree title must prioritize rulers and formal noble titles over captive guest status')
}
$lineageArchiveTitles = Read-Source 'Code/core/lineage/LineageArchiveWriter.cs'
$archiveKingPriority = $lineageArchiveTitles.IndexOf('if (pActor.isKing())',
    [System.StringComparison]::Ordinal)
$archiveFormerKingPriority = $lineageArchiveTitles.IndexOf(
    'pActor.data.get(LineageKeys.FORMER_KING_TITLE',
    [System.StringComparison]::Ordinal)
$archiveNobleTitlePriority = $lineageArchiveTitles.IndexOf(
    'DynasticTitleService.ResolveLivingTitle(pActor)',
    [System.StringComparison]::Ordinal)
$archiveCaptivePriority = $lineageArchiveTitles.IndexOf(
    'pActor.data.get(LineageKeys.CAPTIVE_NOBLE_TITLE',
    [System.StringComparison]::Ordinal)
if ($archiveKingPriority -lt 0 -or $archiveFormerKingPriority -lt 0 -or
    $archiveNobleTitlePriority -lt 0 -or
    $archiveCaptivePriority -lt 0 -or
    $archiveKingPriority -gt $archiveFormerKingPriority -or
    $archiveFormerKingPriority -gt $archiveNobleTitlePriority -or
    $archiveNobleTitlePriority -gt $archiveCaptivePriority) {
    $failures.Add('archived family tree title must prioritize rulers and formal noble titles over captive guest status')
}
$ancestryTitles = Read-Source 'Code/core/lineage/AncestryAnalysisService.cs'
$ancestryResolverStart = $ancestryTitles.IndexOf(
    'private static string ResolveAncestorSocialTitle(',
    [System.StringComparison]::Ordinal)
$ancestryResolverEnd = $ancestryTitles.IndexOf(
    'private static bool TryGetPosthumousTitle(', $ancestryResolverStart,
    [System.StringComparison]::Ordinal)
if ($ancestryResolverStart -lt 0 -or $ancestryResolverEnd -le
    $ancestryResolverStart) {
    $failures.Add('ancestry social title resolver must remain available')
}
else {
    $ancestryResolver = $ancestryTitles.Substring($ancestryResolverStart,
        $ancestryResolverEnd - $ancestryResolverStart)
    $ancestryKingPriority = $ancestryResolver.IndexOf('if (pActor.isKing())',
        [System.StringComparison]::Ordinal)
    $ancestryFormerKingPriority = $ancestryResolver.IndexOf(
        'pActor.data.get(LineageKeys.FORMER_KING_TITLE',
        [System.StringComparison]::Ordinal)
    $ancestryNoblePriority = $ancestryResolver.IndexOf(
        'DynasticTitleService.ResolveLivingTitle(pActor)',
        [System.StringComparison]::Ordinal)
    $ancestryArchivedPriority = $ancestryResolver.IndexOf(
        'pRow?.social_title', [System.StringComparison]::Ordinal)
    if ($ancestryKingPriority -lt 0 -or $ancestryFormerKingPriority -lt 0 -or
        $ancestryNoblePriority -lt 0 -or $ancestryArchivedPriority -lt 0 -or
        $ancestryKingPriority -gt $ancestryFormerKingPriority -or
        $ancestryFormerKingPriority -gt $ancestryNoblePriority -or
        $ancestryNoblePriority -gt $ancestryArchivedPriority) {
        $failures.Add('ancestry title must prioritize rulers and formal noble titles over archived guest status')
    }
}
Require-Present 'abdication closes prior court office' 'Code/patch/AW_AbdicatePatch.cs' 'CourtService.ClearOfficeForReignTransition(__state, "abdicated")'
Require-Present 'manual appointment window id' 'Code/ui/AW_LineageWindowIds.cs' 'COURT_APPOINTMENT = "aw_court_appointment"'
Require-Present 'vacant court card opens appointment window' 'Code/ui/items/CourtActorNodeView.cs' 'CourtAppointmentWindow.Open(pKingdom.id, pNode.OfficeId)'
Require-Present 'court cards create a visible management button' 'Code/ui/items/CourtActorNodeView.cs' 'new GameObject("ManageOffice"'
Require-Present 'filled court card uses replace action' 'Code/ui/items/CourtActorNodeView.cs' 'CourtManualOfficeAction.Replace'
Require-Present 'court management passes frozen incumbent id' 'Code/ui/items/CourtActorNodeView.cs' 'CourtAppointmentWindow.Open(pKingdom.id, pNode.OfficeId, incumbentActorId)'
Require-Present 'manual appointment window submits through the multiplayer command facade' `
    'Code/ui/windows/CourtAppointmentWindow.cs' `
    'AW3MultiplayerCommandFacade.DispatchFromUi('
Require-Present 'authoritative court handler uses the revalidating appointment service' `
    'Code/core/multiplayer/commands/AW3CourtCommandHandler.cs' `
    'CourtService.TryManualAppointment(request.CountryId,'
Require-Present 'manual appointment success explicitly refreshes court' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtWindow.OpenAndRefresh(_kingdomId);'
Require-Present 'manual appointment candidate uses a live avatar' 'Code/ui/items/CourtAppointmentCandidateListItem.cs' '_avatar.show(actor);'
Require-Present 'manual appointment excludes minors with original adulthood state' 'Code/core/court/CourtService.cs' 'adult: pActor.isAdult()'
Require-Present 'manual appointment snapshots actor ids before incremental projection' 'Code/core/court/CourtService.cs' 'BeginManualAppointmentScan('
Require-Present 'manual appointment scan is frame bounded' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtManualAppointmentRules.CandidateScanPerFrame'
Require-Present 'manual appointment scan has a time budget' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CandidateFrameBudgetMilliseconds'
Require-Present 'manual appointment portrait rows are frame bounded' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtManualAppointmentRules.CandidateRowsPerFrame'
Require-Present 'manual appointment candidates are paged' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtManualAppointmentRules.CandidatePageSize'
Require-Absent 'manual appointment window cannot build every candidate synchronously' 'Code/ui/windows/CourtAppointmentWindow.cs' 'GetManualAppointmentCandidates('
Require-Present 'manual appointment revalidates current tier' 'Code/core/court/CourtService.cs' 'IsManualOfficeInCurrentTier(pKingdom, pOfficeId)'
$easternZhouCourtService = Read-Source 'Code/core/court/CourtService.cs'
$ensureMinimumStart = $easternZhouCourtService.IndexOf(
    'private static void EnsureMinimumCourt(', [System.StringComparison]::Ordinal)
$ensureMinimumEnd = if ($ensureMinimumStart -ge 0) {
    $easternZhouCourtService.IndexOf('private static void EnsureKingProjection(',
        $ensureMinimumStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($ensureMinimumStart -lt 0 -or $ensureMinimumEnd -le $ensureMinimumStart -or
    $easternZhouCourtService.Substring($ensureMinimumStart,
        $ensureMinimumEnd - $ensureMinimumStart).Contains(
            'if (!HasOfficialCourt(pKingdom)) return;')) {
    $failures.Add('Eastern Zhou offices must populate before official-court research')
}
$manualTierStart = $easternZhouCourtService.IndexOf(
    'internal static bool IsManualOfficeInCurrentTier(',
    [System.StringComparison]::Ordinal)
$manualTierEnd = if ($manualTierStart -ge 0) {
    $easternZhouCourtService.IndexOf('private static bool ReplaceOfficer(',
        $manualTierStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($manualTierStart -lt 0 -or $manualTierEnd -le $manualTierStart -or
    $easternZhouCourtService.Substring($manualTierStart,
        $manualTierEnd - $manualTierStart).Contains('!HasOfficialCourt')) {
    $failures.Add('Eastern Zhou offices must support manual appointment before official-court research')
}
Require-Present 'Eastern Zhou six ministers share the high court pyramid rank' `
    'Code/core/court/CourtPyramidRules.cs' 'case CourtOfficeId.TaiZai:'
Require-Present 'Eastern Zhou six ministers use the senior career office grade' `
    'Code/core/court/OfficialCareerStateService.cs' 'pOfficeId == CourtOfficeId.TaiZai || pOfficeId == CourtOfficeId.SiTu'
Require-Present 'Eastern Zhou Sima uses the military career track' `
    'Code/core/court/OfficialCareerStateService.cs' 'pOfficeId == CourtOfficeId.SiMa'
Require-Present 'Eastern Zhou Sima candidate scoring values warfare' `
    'Code/core/court/CourtService.cs' 'pOfficeId == CourtOfficeId.Marshal || pOfficeId == CourtOfficeId.SiMa'
Require-Present 'Eastern Zhou court retains the standalone heir row' `
    'Code/core/court/CourtPyramidRules.cs' 'pTier == CourtTier.EasternZhou'
Require-Absent 'court localization no longer exposes primitive council wording' `
    'Locales/aw3_court.csv' 'Primitive Council'
Require-Absent 'history fallbacks no longer expose primitive council wording' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'primitive council'
Require-Present 'Eastern Zhou Taizai office has localization' `
    'Locales/aw3_court.csv' 'aw_court_office_taizai,'
Require-Present 'Eastern Zhou Sikong office has localization' `
    'Locales/aw3_court.csv' 'aw_court_office_sikong,'
Require-Present 'aristocratic groups have a focused bounded service' `
    'Code/core/court/CourtAristocraticGroupService.cs' 'internal static class CourtAristocraticGroupService'
Require-Present 'court refresh reuses one bounded officer index read' `
    'Code/core/court/CourtService.cs' 'List<CourtOfficerView> activeOfficers = GetActiveOfficers(pKingdom, 96);'
Require-Present 'court refresh projects aristocratic groups from the reused officer list' `
    'Code/core/court/CourtService.cs' 'CourtAristocraticGroupService.Refresh(pKingdom, activeOfficers);'
Require-Present 'aristocratic groups reuse the durable general read model' `
    'Code/core/court/CourtAristocraticGroupService.cs' 'GeneralService.GetActiveGeneralsForReadModel('
Require-Present 'aristocratic groups read the existing kingdom city list' `
    'Code/core/court/CourtAristocraticGroupService.cs' 'pKingdom.cities'
Require-Absent 'aristocratic groups cannot scan every kingdom unit' `
    'Code/core/court/CourtAristocraticGroupService.cs' 'getUnits('
Require-Absent 'aristocratic groups cannot scan the world unit collection' `
    'Code/core/court/CourtAristocraticGroupService.cs' 'World.world.units'
Require-Present 'aristocratic group cache persists with the court snapshot' `
    'Code/core/db/KingdomCourtStateTableItem.cs' 'public string aristocratic_group_cache;'
Require-Present 'court continuity restores the aristocratic group cache' `
    'Code/core/court/CourtService.cs' 'ARISTOCRATIC_GROUP_CACHE'
Require-Present 'court snapshot writes the aristocratic group cache' `
    'Code/core/court/CourtService.cs' 'ColumnVal.Create("ARISTOCRATIC_GROUP_CACHE"'
Require-Present 'court summary reads aristocratic groups from the hot cache' `
    'Code/ui/windows/CourtWindow.cs' 'CourtAristocraticGroupService.GetCachedGroups(pKingdom)'
Require-Absent 'court UI cannot query SQLite for aristocratic groups' `
    'Code/ui/windows/CourtWindow.cs' 'SQLiteCommand'
Require-Present 'aristocratic group summary has localization' `
    'Locales/aw3_court.csv' 'aw_court_aristocratic_groups,'
Require-Present 'ministerial power runs for the initial Eastern Zhou court' `
    'Code/core/court/MinisterialPowerService.cs' 'CourtService.HasOfficialCourt(pKingdom) || CourtService.HasPrimitiveCourt(pKingdom)'
Require-Present 'manual appointment immediately refreshes aristocratic groups' `
    'Code/core/court/CourtService.cs' 'CourtAristocraticGroupService.Refresh(kingdom, GetActiveOfficers(kingdom, 96));'
Require-Present 'court institutions have a pure Zhou-Han-Tang-Song rule table' `
    'Code/core/court/CourtInstitutionRules.cs' 'public static class CourtInstitutionRules'
Require-Present 'court institutions are projected to a kingdom hot key' `
    'Code/core/lineage/LineageKeys.cs' 'COURT_INSTITUTION'
Require-Present 'court institution service keeps UI reads off SQLite' `
    'Code/core/court/CourtInstitutionService.cs' 'internal static class CourtInstitutionService'
Require-Absent 'court institution hot service cannot query SQLite' `
    'Code/core/court/CourtInstitutionService.cs' 'SQLite'
Require-Present 'court institution effects use one immutable pure rule table' `
    'Code/core/court/CourtInstitutionEffectRules.cs' 'public readonly struct CourtInstitutionEffects'
Require-Present 'court institution effects have a data-only runtime adapter' `
    'Code/core/court/CourtInstitutionEffectService.cs' 'internal static class CourtInstitutionEffectService'
Require-Present 'institution eligibility reads the original Xia asset hot field' `
    'Code/core/court/CourtInstitutionEffectService.cs' 'pKingdom.data.original_actor_asset'
Require-Present 'institution eligibility reads the Xiaization hot level' `
    'Code/core/court/CourtInstitutionEffectService.cs' 'LineageKeys.XIAIZATION_LEVEL'
Require-Absent 'institution effect hot reads cannot query SQLite' `
    'Code/core/court/CourtInstitutionEffectService.cs' 'SQLite'
Require-Absent 'institution effect hot reads cannot inspect policy state' `
    'Code/core/court/CourtInstitutionEffectService.cs' 'KingdomPolicyService'
Require-Absent 'institution effect hot reads cannot scan world actors' `
    'Code/core/court/CourtInstitutionEffectService.cs' 'World.world.units'
Require-Absent 'institution effect hot reads cannot scan kingdom actors' `
    'Code/core/court/CourtInstitutionEffectService.cs' 'getUnits('
Require-Present 'city economy consumes institution output modifiers once per kingdom' `
    'Code/core/policy/CityEconomyService.cs' 'CourtInstitutionEffectService.Read(pKingdom)'
Require-Present 'domestic technology spread consumes institution modifiers' `
    'Code/core/policy/CityTechService.cs' 'DomesticTechSpreadMultiplier'
Require-Present 'effective warrior slots consume institution modifiers' `
    'Code/core/lineage/MandateMilitaryPhaseService.cs' 'WarriorSlotMultiplier'
Require-Present 'direct vassal terms consume institution autonomy caps' `
    'Code/core/lineage/VassalService.cs' 'DirectVassalAutonomyCapReduction'
Require-Present 'direct vassal terms consume institution tribute rates' `
    'Code/core/lineage/VassalService.cs' 'DirectVassalTributeRateBonus'
Require-Present 'loose tributaries bypass realm fiscal modifiers' `
    'Code/core/lineage/VassalService.cs' 'applyRealmModifiers: false'
Require-Present 'feudatory maintenance consumes institution loyalty' `
    'Code/core/lineage/FeudatoryService.cs' 'FeudatoryMaintenanceLoyaltyBonus'
Require-Present 'vassal AI consumes the institution soft cap' `
    'Code/core/lineage/VassalAIService.cs' 'VassalSoftCap'
Require-Present 'court summary exposes the active institution effects' `
    'Code/ui/windows/CourtWindow.cs' 'CourtInstitutionService.EffectSummary(pKingdom)'
Require-Present 'Zhou institution effects are localized' `
    'Locales/aw3_court.csv' 'aw_court_institution_effect_zhou,'
Require-Present 'Han institution effects are localized' `
    'Locales/aw3_court.csv' 'aw_court_institution_effect_han,'
Require-Present 'Tang institution effects are localized' `
    'Locales/aw3_court.csv' 'aw_court_institution_effect_tang,'
Require-Present 'Song institution effects are localized' `
    'Locales/aw3_court.csv' 'aw_court_institution_effect_song,'
Require-Present 'Song institutions are an actual research node' `
    'Code/content/policies/KingdomPolicyDefs.cs' 'Id = "aw_tech_song_court"'
Require-Present 'policy completion refreshes the active court institution' `
    'Code/core/policy/KingdomPolicyService.cs' 'CourtInstitutionService.Refresh(pKingdom, pRecordHistory: true)'
Require-Present 'court research AI consumes institution era preference' `
    'Code/core/policy/KingdomPolicyAI.cs' 'CourtInstitutionRules.ResearchEraScore('
Require-Present 'court actor cards resolve office names through the realm institution' `
    'Code/ui/items/CourtActorNodeView.cs' 'CourtInstitutionService.OfficeName(pKingdom, pOfficeId)'
Require-Present 'court read model gates historical grades behind the current Nine-Rank institution' `
    'Code/core/court/CourtReadModelService.cs' 'OfficialCareerRankRules.CanDisplayRankedCareer('
Require-Present 'court tooltip gates historical grades behind the current Nine-Rank institution' `
    'Code/ui/items/CourtActorNodeView.cs' 'OfficialCareerRankRules.CanDisplayRankedCareer('
Require-Present 'court institution reform is recorded as its own history event' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnCourtInstitutionReformed('
Require-Present 'career rows freeze the institution used at appointment' `
    'Code/core/db/CourtOfficerTableItem.cs' 'institution_at_appointment'
Require-Present 'career appointment captures the current institution' `
    'Code/core/court/OfficialCareerService.cs' 'InstitutionAtAppointment = CourtInstitutionService.GetInstitution(pKingdom)'
Require-Present 'career history resolves the frozen institution rather than the current realm law' `
    'Code/ui/windows/HistoryListWindow.cs' 'pCareer.InstitutionAtAppointment'
Require-Present 'political tension has a focused bounded rebellion service' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'internal static class MandateDeclineRebellionService'
Require-Present 'Mandate annual evaluation drives political tension rebellions' `
    'Code/core/lineage/MandateService.cs' 'MandateDeclineRebellionService.OnMandateYear('
Require-Present 'political tension rebellion scans the existing city list' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'pKingdom.cities'
Require-Present 'political tension rebellion uses only the current city leader' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'pCity.leader'
Require-Present 'political tension rebellion uses the ordinary rebellion war' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'GeneralRebellionService.WAR_GENERAL_REBELLION'
Require-Present 'political tension rebellion validates the created war before committing success' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'if (!IsValidRebellionWar(war, rebel, pKingdom))'
Require-Present 'political tension rebellion rolls its seed city back when war creation fails' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'RollbackFailedRebellion(pKingdom, rebel, pCity);'
Require-Present 'political tension rebellion applies only modest catalyst pressure' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'MandateDeclineRebellionRules.SuccessCatalystPressure'
Require-Present 'catalyst adjustment immediately evaluates the mature chaos threshold' `
    'Code/core/lineage/MandatePhaseService.cs' 'MandatePhaseRules.ShouldEnterChaosAfterCatalyst('
Require-Absent 'political tension rebellion cannot invoke the vanilla neighboring-city expansion' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'checkMoreAlignedCities('
Require-Absent 'political tension rebellion cannot transfer another city into the rebel kingdom' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'joinAnotherKingdom(rebel'
Require-Absent 'political tension rebellion cannot scan kingdom actors' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'getUnits('
Require-Absent 'political tension rebellion cannot scan city actors' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'pCity.getUnits('
Require-Absent 'political tension rebellion cannot become a Mandate claimant' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'LineageKeys.MANDATE_REBEL'
Require-Absent 'political tension rebellion cannot claim the Mandate' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'TryClaimMandate('
Require-Absent 'political tension rebellion cannot declare the Mandate' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'TryDeclareMandate('
Require-Absent 'political tension rebellion cannot use the full rebel government' `
    'Code/core/lineage/MandateDeclineRebellionService.cs' 'KingdomPolicyDefs.ClassRebel'
Require-Present 'manual appointment revalidates vacancy' 'Code/core/court/CourtService.cs' 'HasActiveOffice(pKingdom, pOfficeId)'
Require-Present 'manual appointment cleans only the selected central office' `
    'Code/core/court/CourtService.cs' 'CloseStaleCentralOfficeRow(kingdom, pOfficeId);'
Require-Absent 'manual appointment cannot scan every active officer before one click' `
    'Code/core/court/CourtService.cs' 'if (kingdom?.data != null) CloseStaleOfficerRows(kingdom);'
Require-Present 'central office lookup uses its exact indexed key' `
    'Code/core/court/CourtService.cs' 'AND LAYER = @layer AND OFFICE_ID = @office LIMIT 1'
Require-Present 'central office lookup binds the central layer' `
    'Code/core/court/CourtService.cs' 'cmd.Parameters.AddWithValue("@layer", CourtOfficeLayer.Central);'
Require-Present 'stale office cleanup closes only the exact durable row' `
    'Code/core/court/CourtService.cs' 'OfficialCareerService.EndForOffice('
$manualCourtService = Read-Source 'Code/core/court/CourtService.cs'
$manualAppointmentStart = $manualCourtService.IndexOf(
    'internal static CourtManualAppointmentResult TryManualAppointment(',
    [System.StringComparison]::Ordinal)
$manualStaleCleanup = if ($manualAppointmentStart -ge 0) {
    $manualCourtService.IndexOf('CloseStaleCentralOfficeRow(kingdom, pOfficeId);',
        $manualAppointmentStart, [System.StringComparison]::Ordinal)
} else { -1 }
$manualTargetValidation = if ($manualAppointmentStart -ge 0) {
    $manualCourtService.IndexOf('ValidateManualAppointmentTarget(kingdom, pOfficeId,',
        $manualAppointmentStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($manualStaleCleanup -lt 0 -or $manualTargetValidation -lt 0 -or
    $manualStaleCleanup -gt $manualTargetValidation) {
    $failures.Add('manual appointment must reconcile stale offices before target validation')
}
Require-Present 'manual appointment uses persisted nationality authority' 'Code/core/court/CourtService.cs' 'CourtAffiliationResolver.IsDomestic(pActor, pKingdom)'
Require-Present 'manual appointment commits through official career path' 'Code/core/court/CourtService.cs' ': SetOfficer(actor, kingdom, CourtOfficeLayer.Central,'
Require-Present 'school never gates manual appointment eligibility' 'Code/core/court/CourtManualAppointmentRules.cs' 'return true;'
Require-Present 'civil appointment has a focused military transition service' 'Code/core/court/CourtOfficerMilitaryTransitionService.cs' 'ReleaseAfterCommittedAppointment('
$courtService = Read-Source 'Code/core/court/CourtService.cs'
$committedProjection = $courtService.IndexOf('internal static bool ApplyCommittedOfficerProjection(',
    [System.StringComparison]::Ordinal)
$committedGate = if ($committedProjection -ge 0) {
    $courtService.IndexOf('!careerResult.IsCommitted', $committedProjection,
        [System.StringComparison]::Ordinal)
} else { -1 }
$militaryRelease = if ($committedProjection -ge 0) {
    $courtService.IndexOf('CourtOfficerMilitaryTransitionService.ReleaseAfterCommittedAppointment(',
        $committedProjection, [System.StringComparison]::Ordinal)
} else { -1 }
if ($committedGate -lt 0 -or $militaryRelease -lt 0 -or
    $committedGate -gt $militaryRelease) {
    $failures.Add('civil officer military identity must be released only after appointment commit')
}
Require-Present 'replacement persistence owns one transaction' 'Code/core/court/CourtOfficerReplacementPersistence.cs' 'transaction = pDb.BeginTransaction();'
Require-Present 'official career close normalizes nullable legacy text in compare and set' `
    'Code/core/court/OfficialCareerPersistence.cs' `
    "IFNULL(INSTITUTION_AT_APPOINTMENT,'')=@oInstitution"
Require-Present 'replacement closes incumbent before successor insert' 'Code/core/court/CourtOfficerReplacementPersistence.cs' 'OfficialCareerPersistence.StageClose('
Require-Present 'replacement inserts successor in the same transaction' 'Code/core/court/CourtOfficerReplacementPersistence.cs' 'OfficialCareerPersistence.Stage(pDb, transaction, appointmentToken);'
Require-Present 'guest replacement closes affiliation and career together' 'Code/core/court/CourtOfficerReplacementPersistence.cs' 'GuestOfficeEndPersistence.EndInTransaction('
$courtReplacement = Read-Source 'Code/core/court/CourtOfficerReplacementPersistence.cs'
$replacementGuestClose = $courtReplacement.IndexOf(
    'guestResult = GuestOfficeEndPersistence.EndInTransaction(',
    [System.StringComparison]::Ordinal)
$replacementLocalClose = $courtReplacement.IndexOf(
    'OfficialCareerPersistence.StageClose(',
    [System.StringComparison]::Ordinal)
$replacementAppointment = $courtReplacement.IndexOf(
    'OfficialCareerPersistence.Stage(pDb, transaction, appointmentToken);',
    [System.StringComparison]::Ordinal)
$replacementCommit = $courtReplacement.IndexOf('transaction.Commit();',
    [System.StringComparison]::Ordinal)
if ($replacementGuestClose -lt 0 -or $replacementLocalClose -lt 0 -or
    $replacementAppointment -lt 0 -or $replacementCommit -lt 0 -or
    $replacementGuestClose -gt $replacementAppointment -or
    $replacementLocalClose -gt $replacementAppointment -or
    $replacementAppointment -gt $replacementCommit) {
    $failures.Add('court replacement must close either incumbent path before inserting and committing the successor')
}
Require-Present 'aristocratic succession service exists' 'Code/core/lineage/AristocraticSuccessionService.cs' 'internal static class AristocraticSuccessionService'
Require-Present 'vacancy resolver selects a noble house' 'Code/core/lineage/RepublicGovernmentService.cs' 'AristocraticSuccessionService.SelectRuler(pKingdom)'
Require-Present 'house accession records clan fallback mode' 'Code/core/lineage/RepublicGovernmentService.cs' 'HeirService.MarkClanFallbackSuccession(pKingdom, houseRuler)'
Require-Present 'royal-clan path uses unified vacancy resolver' 'Code/patch/AW_HeirPatch.cs' 'RepublicGovernmentService.ResolveRulerForVacancy(pKingdom)'
Require-Absent 'house selection cannot mutate class policy' 'Code/core/lineage/AristocraticSuccessionService.cs' 'POLICY_CLASS_STATE'
Require-Present 'vassal settlement requires surviving defender cities' 'Code/core/lineage/VassalService.cs' 'pVassal.hasCities()'
Require-Present 'independence war records service suspension' 'Code/core/lineage/VassalService.cs' 'BeginIndependenceSuspension(pWar, attacker, defender);'
Require-Present 'independence war leaves old suzerain wars' 'Code/core/lineage/VassalService.cs' 'LeaveSuzerainWarsForIndependence(pWar, attacker, defender);'
Require-Present 'independence settlement clears service suspension' 'Code/core/lineage/VassalService.cs' 'EndIndependenceSuspension(pWar, attacker);'
Require-Present 'yearly vassal pull checks independence suspension' 'Code/core/lineage/VassalService.cs' 'HasActiveIndependenceSuspension(vassal, suzerain)'
Require-Absent 'alliance constructor cannot return null from AW prefix' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'NewAlliance_Prefix'
Require-Present 'subject alliance plot is rejected before vanilla target scan' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'GetAllianceTarget_Prefix'
Require-Present 'alliance plot filters vassal target before construction' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'GetAllianceTarget_Postfix'
Require-Present 'alliance plot uses tested vassal permission rule' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'VassalWarPermissionRules.CanUseAlliancePlot('
Require-Present 'forced alliance path rejects subjects before construction' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'ForceAlliance_Prefix'
Require-Present 'alliance join path rejects subjects including forced joins' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'AllianceJoin_Prefix'
Require-Present 'school extinction transfer rule exists' 'Code/core/schools/SchoolAffiliationTransferRules.cs' 'AllowsExtinctionRelease('
Require-Present 'school affiliation guard applies extinction release' 'Code/core/schools/HistoricalAffiliationService.cs' 'SchoolAffiliationTransferRules.AllowsExtinctionRelease('
Require-Present 'school extinction release waits for stable city index' 'Code/core/schools/HistoricalAffiliationService.cs' '!manager.hasDirtyCities()'
Require-Present 'school extinction release targets actor wild kingdom' 'Code/core/schools/HistoricalAffiliationService.cs' 'pTarget.asset.id == pActor.asset.kingdom_id_wild'

$lineage = Read-Source 'Code/core/lineage/LineageService.cs'
$branchStart = $lineage.IndexOf('public static void OnKingFoundBranch(', [System.StringComparison]::Ordinal)
$newClan = $lineage.IndexOf('newClan(pKing', $branchStart, [System.StringComparison]::Ordinal)
$freezeShi = $lineage.IndexOf('GenerateShiName(pKing)', $branchStart, [System.StringComparison]::Ordinal)
if ($branchStart -lt 0 -or $newClan -lt 0 -or $freezeShi -lt 0 -or $freezeShi -gt $newClan) {
    $failures.Add('king-founded branch must resolve its shi before newClan(pKing)')
}

$vacancy = Read-Source 'Code/core/lineage/RepublicGovernmentService.cs'
$houseSelection = $vacancy.IndexOf('AristocraticSuccessionService.SelectRuler(pKingdom)', [System.StringComparison]::Ordinal)
$setRepublic = $vacancy.IndexOf('SetRepublic(pKingdom)', [System.StringComparison]::Ordinal)
if ($houseSelection -lt 0 -or $setRepublic -lt 0 -or $houseSelection -gt $setRepublic) {
    $failures.Add('aristocratic house selection must run before SetRepublic(pKingdom)')
}

$contentPath = 'Code/content/schools/HistoricalSchoolContent.cs'
$academyContentPath = 'Code/content/schools/SchoolAcademyBuildingContent.cs'
$academyConstructionPath = 'Code/core/schools/HistoricalSchoolAcademyConstructionService.cs'
Require-Present 'academy building id' $academyContentPath 'BuildingId = "academy_Xia"'
Require-Present 'academy unique building type' $academyContentPath 'BuildingTypeId = "type_aw_school_academy"'
Require-Present 'academy clones Xia library' $academyContentPath 'AssetManager.buildings.clone(BuildingId, "library_Xia")'
Require-Present 'academy keeps book storage' $academyContentPath 'academy.book_slots = source.book_slots;'
Require-Present 'academy uses requested 1.45x scale' $academyContentPath 'new Vector3(0.07975f, 0.07975f, 0.25f)'
Require-Present 'academy uses stable footprint' $academyContentPath 'new BuildingFundament(3, 3, 2, 0)'
Require-Present 'academy replaces Xia library order' $academyContentPath 'pArchitecture.addBuildingOrderKey("order_library", BuildingId);'
Require-Present 'academy registration follows Xia building generation' 'Code/content/XiaArchitecture.cs' 'SchoolAcademyBuildingContent.Init(Xia);'
foreach ($academySprite in @('construction_0.png', 'main_0.png', 'mini_0.png', 'ruin_0.png')) {
    $academySpritePath = Join-Path $root "GameResources/buildings/civ_main/Xia/academy_Xia/$academySprite"
    if (-not [System.IO.File]::Exists($academySpritePath)) {
        $failures.Add("academy sprite missing: $academySprite")
    }
}
Require-Present 'academy minimap crop metadata' 'GameResources/buildings/civ_main/Xia/academy_Xia/sprites.json' '"Path": "mini_0.png"'
Require-Present 'academy minimap crop width' 'GameResources/buildings/civ_main/Xia/academy_Xia/sprites.json' '"RectW": 7'
Require-Present 'academy minimap crop height' 'GameResources/buildings/civ_main/Xia/academy_Xia/sprites.json' '"RectH": 6'
Require-Present 'academy construction uses committed-descent event' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'HistoricalSchoolAcademyConstructionService.TryStart(pHome);'
Require-Present 'academy construction service exists' $academyConstructionPath 'internal static class HistoricalSchoolAcademyConstructionService'
Require-Present 'academy construction rejects any indexed live academy' $academyConstructionPath 'HistoricalSchoolAcademyService.HasLiveAcademy(pCity)'
Require-Present 'academy construction keeps a per-city in-flight claim' $academyConstructionPath 'StartedAcademies.TryGetValue(cityId, out Building started)'
Require-Present 'academy construction applies pure eligibility rule' $academyConstructionPath 'SchoolAcademyConstructionRules.ShouldStart('
Require-Present 'academy construction bounds zone checks' $academyConstructionPath 'MaxZonesToInspect = 24'
Require-Present 'academy construction bounds tile checks' $academyConstructionPath 'MaxTilesPerZone = 8'
Require-Present 'academy construction rotates bounded retry windows' $academyConstructionPath 'SchoolAcademyConstructionRules.ZoneStartIndex('
Require-Present 'academy construction validates original footprint' $academyConstructionPath 'World.world.buildings.canBuildFrom('
Require-Present 'academy construction creates original building entity' $academyConstructionPath 'World.world.buildings.addBuilding('
Require-Present 'academy construction assigns current city kingdom' $academyConstructionPath 'building.setKingdom(pCity.kingdom);'
Require-Present 'academy construction starts unfinished site' $academyConstructionPath 'building.setUnderConstruction();'
Require-Present 'academy construction runtime claim is cleared' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolAcademyConstructionService.ClearRuntime();'
Require-Absent 'academy construction cannot scan all world cities' $academyConstructionPath 'World.world.cities'
Require-Absent 'academy registration diagnostics removed' $academyContentPath '[academy diagnostic]'
Require-Absent 'academy city diagnostics removed' 'Code/core/schools/HistoricalSchoolAcademyService.cs' '[academy diagnostic]'
Require-Present 'academy venue source installation' $contentPath 'HistoricalSchoolAcademyService.Init();'
Require-Present 'academy indexed city lookup' 'Code/core/schools/HistoricalSchoolAcademyService.cs' 'pCity.getBuildingListOfType('
Require-Absent 'academy lookup cannot scan the complete city building list' 'Code/core/schools/HistoricalSchoolAcademyService.cs' 'pCity.buildings'
Require-Absent 'academy cleanup cannot require living actor data' 'Code/core/schools/HistoricalSchoolAcademyService.cs' 'if (pActor?.data == null || pAcademy == null) return;'
Require-Present 'academic work has no outdoor fallback' 'Code/core/schools/HistoricalSchoolVenueProvider.cs' 'HistoricalSchoolVenueRules.RequiresAcademy(pKind)'
Require-Present 'venue selection carries academy building' 'Code/core/schools/HistoricalSchoolVenueProvider.cs' 'out Building pAcademy'
Require-Present 'venue claim carries academy building' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'public Building Academy { get; }'
Require-Present 'same academy debate layout rule' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'HistoricalSchoolVenueRules.IsDebateLayoutValid('
Require-Present 'same academy tile reserved only once' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'secondary != null && secondary != primary'
Require-Present 'lecture prepares academy building target' 'Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs' 'out Building academy'
Require-Present 'lecture assigns academy building target' 'Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs' 'pActor.beh_building_target = academy;'
Require-Absent 'lecture cannot move to a bare venue tile' 'Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs' 'pActor.beh_tile_target = target;'
Require-Present 'lecture walks to academy building' $contentPath 'lecture.addBeh(new BehGoToBuildingTarget());'
Require-Present 'lecture stays inside academy' $contentPath 'lecture.addBeh(new BehStayInBuildingTarget(4f, 7f));'
Require-Present 'lecture completion requires exact academy interior' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolAcademyService.IsInside(pActor, activity.Venue?.Academy)'
Require-Present 'lecture terminal path exits academy' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolAcademyService.Exit(actor, pActivity.Venue?.Academy);'
Require-Present 'debate prepares academy building target' 'Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs' 'out Building academy'
Require-Present 'debate assigns academy building target' 'Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs' 'pActor.beh_building_target = academy;'
Require-Absent 'debate cannot move to a bare venue tile' 'Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs' 'pActor.beh_tile_target = target;'
Require-Present 'first debater walks to academy building' $contentPath 'travel.addBeh(new BehGoToBuildingTarget());'
Require-Present 'first debater enters academy before debate task' $contentPath 'travel.addBeh(new BehStayInBuildingTarget(0f, 0f));'
Require-Present 'second debater stays inside academy' $contentPath 'receiving.addBeh(new BehStayInBuildingTarget(4f, 7f));'
Require-Present 'debate completion requires exact academy interior' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'HistoricalSchoolAcademyService.IsInside(pActor, activity.Venue?.Academy)'
Require-Present 'debate first terminal path exits academy' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'HistoricalSchoolAcademyService.Exit(first, pActivity.Venue?.Academy);'
Require-Present 'debate second terminal path exits academy' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'HistoricalSchoolAcademyService.Exit(second, pActivity.Venue?.Academy);'
Require-Present 'lecture task id' $contentPath 'LectureTaskId = "aw_historical_school_lecture"'
Require-Present 'debate travel task id' $contentPath 'DebateTravelTaskId = "aw_historical_school_debate_travel"'
Require-Present 'debate task id' $contentPath 'DebateTaskId = "aw_historical_school_debate"'
Require-Present 'debate receiver task id' $contentPath 'DebateReceivingTaskId ='
Require-Present 'debate receiver task value' $contentPath '"aw_historical_school_debate_receiving"'

$localePath = 'Locales/others.csv'
Require-Present 'academy asset locale' $localePath 'academy_Xia,'
Require-Present 'academy building locale' $localePath 'building_academy_Xia,'
Require-Present 'academy type locale' $localePath 'type_aw_school_academy,'
Require-Present 'academy generic locale' $localePath 'aw_school_academy,'
Require-Present 'lecture task locale' $localePath 'task_unit_aw_historical_school_lecture,'
Require-Present 'debate travel task locale' $localePath 'task_unit_aw_historical_school_debate_travel,'
Require-Present 'debate task locale' $localePath 'task_unit_aw_historical_school_debate,'
Require-Present 'debate receiver task locale' $localePath 'task_unit_aw_historical_school_debate_receiving,'

Require-Present 'frame activity queue' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolActivityQueue.ProcessFrame()'
Require-Present 'school-level canonical master slots' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'HistoricalSchoolActiveMasterSlots'
Require-Present 'deferred school maintenance schedule' `
    'Code/core/schools/HistoricalSchoolActionService.cs' `
    'ScheduleDeferredActions(pPlanning.Year);'
Require-Present 'deferred school maintenance frame step' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolActionService.ProcessDeferredFrame()'
Require-Absent 'per-teacher city resident scan' 'Code/core/schools/HistoricalSchoolActionService.cs' 'foreach (Actor actor in pCity.units)'
Require-Present 'school activity save flush' 'Code/patch/AW_SavePatch.cs' 'HistoricalSchoolActivityQueue.FlushPendingPersistenceForSave()'
Require-Present 'lecture ready save flush' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolDebateActivityService.FlushPendingPersistenceForSave()'
Require-Present 'debate ready save flush' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'FlushPendingPersistenceForSave()'
Require-Absent 'debate unknown persistence discard' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' '++ready.Attempts >= 3'
Require-Absent 'school activity city-center fallback' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'result.Add(center)'
Require-Present 'city-change activity task restoration' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'CancelActor(__instance, pRestoreActor: true)'
Require-Present 'death activity cleanup without restoration' 'Code/core/schools/SchoolMembershipService.cs' 'CancelActor(pActor, pRestoreActor: false)'
Require-Present 'lecture excludes active debate actors' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolDebateActivityService.IsActorBusy(actor.data.id)'
Require-Present 'debate excludes active lecture actors' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolActivityQueue.IsLectureActorBusy(pActor.data.id)'
Require-Present 'pending master requires slot attachment' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'if (!ActiveMasterSlots.TryAttachActor(pMaster.SchoolId, pMaster.Id, actorId))'
Require-Present 'nearby lecture completion effect' 'Code/core/schools/HistoricalSchoolActionService.cs' 'EffectsLibrary.spawnAtTileRandomScale("fx_experience_gain"'

Require-Absent 'school updateAge synchronous runner' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolRuntime.OnWorldYear()'
Require-Absent 'school yearly enqueue cannot run once per kingdom' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'typeof(Kingdom), nameof(Kingdom.updateAge)'
Require-Present 'school yearly enqueue runs once per world year' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HarmonyPatch(typeof(MapBox), "updateObjectAge")'
Require-Absent 'school frame stopwatch allocation' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'Stopwatch.StartNew()'
Require-Absent 'per-frame activity LINQ ordering' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' '.OrderBy('
Require-Absent 'per-frame debate distinct scan' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' '.Distinct()'
Require-Absent 'permanent scholar job restoration' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'RestoreScholarJob('
Require-Absent 'direct scholar travel task replacement' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'pActor.setTask('
Require-Absent 'lecture requires vanilla city equality' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'pActor.city?.data?.id == residence.data.id'
Require-Absent 'inactive school map rebuild' 'Code/core/policy/SchoolMapModeService.cs' 'IsActive() ? 4 : 1'
Require-Absent 'dirty city batch array' 'Code/core/court/CitySchoolSnapshotService.cs' 'Dirty.TakeBatch('
Require-Present 'dirty city dequeue' 'Code/core/court/CitySchoolSnapshotService.cs' 'source.TryDequeue('
Require-Absent 'global school resident rebuild' 'Code/core/court/CitySchoolSnapshotService.cs' 'SchoolMembershipService.ActiveMemberships()'
Require-Absent 'obsolete global resident index cache' 'Code/core/court/CitySchoolResidentIndexRules.cs' 'class CitySchoolResidentIndexCache'
Require-Present 'indexed city residents' 'Code/core/court/CitySchoolSnapshotService.cs' 'HistoricalSchoolRuntimeIndex.Instance.ResidentIds('
Require-Present 'bottom bar visibility gate' 'Code/core/policy/SchoolMapBottomBarController.cs' '_visibleOrPending'
Require-Absent 'global roster membership revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'SchoolMembershipService.Version'
Require-Absent 'global roster residence revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalAffiliationService.ResidenceRevision'
Require-Absent 'global roster lecture revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolStore.LectureRevision'
Require-Absent 'obsolete global lecture revision counter' 'Code/core/schools/HistoricalSchoolStore.cs' '_lectureRevision'
Require-Absent 'obsolete global residence revision facade' 'Code/core/schools/HistoricalAffiliationService.cs' 'public static long ResidenceRevision'
Require-Absent 'obsolete global residence revision counter' 'Code/core/schools/HistoricalSchoolRevisionService.cs' '_residenceRevision'
Require-Absent 'global roster disciple count scan' 'Code/core/schools/SchoolRosterReadModelService.cs' 'SchoolLineageService.BuildDirectDiscipleCounts()'
Require-Present 'indexed roster disciple count' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolRuntimeIndex.Instance.DirectDiscipleCount('
Require-Present 'narrow roster revision stamp' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolRosterRevisionStamp.Capture('
Require-Absent 'school overview global membership revision' 'Code/ui/windows/SchoolWindow.cs' 'SchoolMembershipService.Version'
Require-Absent 'school overview global lecture revision' 'Code/ui/windows/SchoolWindow.cs' 'HistoricalSchoolStore.LectureRevision'
Require-Present 'school overview selected-school revision' 'Code/ui/windows/SchoolWindow.cs' '_displayedSchoolRevisionStamp'
Require-Present 'lecture activity revision projection' 'Code/core/schools/HistoricalSchoolActionService.cs' 'HistoricalSchoolRevisionService.MarkActivity('
Require-Present 'debate activity revision projection' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolRevisionService.MarkActivity('
Require-Present 'bounded school write buffer' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' 'public const int MaxCapacity = 512'
Require-Present 'single school SQL batch transaction' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' '_db.BeginTransaction()'
Require-Present 'school SQL batch diagnostics' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' 'HistoricalSchoolDiagnostics.RecordSqlBatch('
Require-Present 'teaching transaction overload' 'Code/core/schools/HistoricalSchoolTeachingPersistenceDb.cs' 'RecordInTransaction('
Require-Present 'school write frame drain' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolWriteBufferService.ProcessFrame()'
Require-Present 'school write save flush' 'Code/patch/AW_SavePatch.cs' 'HistoricalSchoolWriteBufferService.FlushForSave()'
Require-Absent 'annual full ledger decay stage' 'Code/core/schools/HistoricalSchoolScheduler.cs' 'ApplyLedgerDecay('
Require-Absent 'annual full ledger decay update' 'Code/core/schools/HistoricalSchoolStore.cs' 'public static int ApplyLedgerDecay('
Require-Present 'lazy ledger read decay' 'Code/core/schools/HistoricalSchoolStore.cs' 'HistoricalSchoolLedgerDecayRules.Effective('
Require-Present 'lazy teaching ledger decay' 'Code/core/schools/HistoricalSchoolTeachingPersistenceDb.cs' 'HistoricalSchoolLedgerDecayRules.Effective('
Require-Absent 'direct ready lecture transaction' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'CommitQueuedLecture(pActivity)'
Require-Present 'buffered ready lecture write' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'TryQueueLectureCommit(pActivity)'
Require-Absent 'direct ready debate transaction' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'CommitQueuedDebate(pActivity)'
Require-Present 'buffered ready debate write' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'TryQueueDebateCommit(pActivity)'
Require-Present 'debate transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'RecordDebateAndLedgerInTransaction('
Require-Present 'guest start transaction overload' 'Code/core/schools/GuestOfficePersistenceDb.cs' 'StartInTransaction('
Require-Present 'guest end transaction overload' 'Code/core/schools/GuestOfficeEndPersistence.cs' 'EndInTransaction('
Require-Present 'buffered guest office writes' 'Code/core/schools/SchoolGuestOfficeService.cs' 'HistoricalSchoolWriteBufferService.TryEnqueue('
Require-Present 'membership join transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'InsertMembershipInTransaction('
Require-Present 'membership conversion transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'ConvertMembershipInTransaction('
Require-Present 'membership close transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'CloseMembershipInTransaction('
Require-Present 'buffered membership join' 'Code/core/schools/SchoolMembershipService.cs' 'TryQueueJoin('
Require-Present 'buffered membership conversion' 'Code/core/schools/SchoolMembershipService.cs' 'TryQueueConversion('
Require-Present 'membership event city identity' 'Code/core/schools/SchoolMembershipService.cs' 'public long CityId;'
Require-Present 'membership commit ledger invalidation' 'Code/core/schools/SchoolMembershipService.cs' 'HistoricalSchoolStore.InvalidateTeachingCommit(Event.CityId)'
Require-Present 'committed membership projection retry' 'Code/core/schools/SchoolMembershipService.cs' 'committed school membership adoption failed'
Require-Absent 'synchronous school action join' 'Code/core/schools/HistoricalSchoolActionService.cs' 'SchoolMembershipService.TryJoin('
Require-Absent 'synchronous school action conversion' 'Code/core/schools/HistoricalSchoolActionService.cs' 'SchoolMembershipService.TryConvert('
Require-Present 'year token enqueue' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolRuntime.EnqueueWorldYear()'
Require-Present 'temporary school task scheduling' 'Code/core/schools/HistoricalSchoolTaskLeaseService.cs' 'scheduleTask('
Require-Present 'travel task lease scheduling' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalSchoolTaskLeaseService.TrySchedule('
Require-Present 'travel arrival uses buffered atomic persistence' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalSchoolWriteBufferService.TryEnqueue('
Require-Present 'travel arrival writes affiliation in the school transaction' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'SaveAffiliationTransitionInTransaction('
Require-Present 'travel arrival writes its event in the school transaction' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'RecordSchoolEventInTransaction('
Require-Absent 'actor travel arrival cannot synchronously save affiliation' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalAffiliationService.TryArrive('
Require-Absent 'actor travel arrival cannot synchronously insert school events' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalSchoolStore.RecordSchoolEvent('
Require-Absent 'actor travel arrival cannot synchronously write person history' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoryWriter.RecordPerson('
Require-Absent 'actor travel arrival cannot synchronously write city history' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoryWriter.RecordCity('
Require-Present 'indexed quarterly travel bucket' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalSchoolRuntimeIndex.Instance.TravelEligibleIds(bucket)'
Require-Present 'quarterly travel is deferred to frame work' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolTravelService.ProcessFrame()'
Require-Present 'quarterly travel processes one actor per frame' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'int offset = work.Processed++;'
Require-Present 'travel destination city index is annual cached' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'IndexedCities(work.Year)'
Require-Present 'city lifecycle invalidates travel city index' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolTravelService.InvalidateCityIndex();'
Require-Present 'bounded venue city cache' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'HistoricalSchoolFixedLru<long, CityVenueCacheEntry>'
Require-Present 'bounded recruit city cache' 'Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs' 'HistoricalSchoolFixedLru<long, Entry>'
Require-Present 'rotating annual school recruit window' 'Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs' 'HistoricalSchoolRecruitCandidateRules.ScanStart('
Require-Present 'school recruit scan remains bounded' 'Code/core/schools/HistoricalSchoolRecruitCandidateRules.cs' 'MaxScanPerCityYear = 96'
Require-Present 'school recruit cache skips existing members' 'Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs' 'SchoolMembershipService.GetActive(actor.data.id) != null'
Require-Absent 'school recruit cache cannot scan a fixed city prefix' 'Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs' 'foreach (Actor actor in pCity.units)'
Require-Present 'bounded active venue claims' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'HistoricalSchoolActiveReservationBook<string, HistoricalSchoolVenueClaim>'
Require-Present 'twelve active venue maximum' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'MaxActiveClaims = 12'
Require-Present 'eight concurrent lectures' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'MaxConcurrentLectures = 8'
Require-Present 'two-year lecture backlog bound' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'MaxRetainedLectures = MaxQueuedLectures * 2'
Require-Present 'lecture global backlog gate' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'PendingLectures.Count + ActiveLectures.Count'
Require-Present 'lecture concurrent activation gate' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'ActiveLectures.Count, MaxConcurrentLectures'
Require-Present 'two-year debate backlog bound' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'MaxRetainedDebates = MaxQueuedPerYear * 2'
Require-Present 'debate global backlog gate' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'PendingCities.Count + ActivitiesById.Count'
Require-Present 'bounded active travel reservations' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolTravelReservationBook'
Require-Present 'transient teacher death gate' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolTransientIdGate'
Require-Absent 'permanent handled teacher deaths' 'Code/core/schools/SchoolLineageService.cs' 'HandledTeacherDeaths'
Require-Present 'teacher death gate terminal release' 'Code/core/schools/SchoolLineageService.cs' 'ProcessingTeacherDeaths.Complete(teacherId)'
Require-Present 'buffered lineage successor write' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolWriteBufferService.TryEnqueue('
Require-Present 'transactional lineage successor event' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolStore.RecordSchoolEventInTransaction('
Require-Absent 'synchronous lineage successor event' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolStore.RecordSchoolEvent('
Require-Present 'fresh world clears school actions' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolActionService.ClearRuntime()'
Require-Present 'fresh world clears school activities' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolActivityQueue.ClearRuntime()'
Require-Present 'fresh world clears school writes' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolWriteBufferService.Clear()'
Require-Present 'fresh world clears guest offices' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'SchoolGuestOfficeService.ClearRuntime()'
Require-Present 'fresh world clears lineage reservations' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'SchoolLineageService.ClearRuntime()'
Require-Present 'fresh world clears travel targets' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolTravelService.ClearRuntime()'
Require-Present 'fresh world clears school memberships' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'SchoolMembershipService.ClearRuntime()'
Require-Absent 'map clear does not duplicate membership clear' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'SchoolMembershipService.ClearRuntime();'
Require-Absent 'archive switch does not duplicate membership clear' 'Code/patch/AW_SavePatch.cs' 'try { SchoolMembershipService.ClearRuntime(); } catch { }'
Require-Present 'stale guest pending nodes are skipped' 'Code/core/schools/SchoolGuestOfficeService.cs' '!Pending.TryGetValue(actorId'
Require-Present 'stale death retry nodes are skipped' 'Code/core/schools/SchoolMembershipService.cs' '!QueuedDeathRetries.Contains(candidate)'
Require-Present 'canonical master idle roam behaviour' 'Code/ai/behaviours/actor/BehHistoricalSchoolIdleRoam.cs' 'class BehHistoricalSchoolIdleRoam'
Require-Present 'canonical master idle roam task' $contentPath 'IdleRoamTaskId = "aw_historical_school_idle_roam"'
Require-Present 'scoped formal affiliation transfer' 'Code/core/schools/FormalAffiliationTransferScope.cs' 'FormalAffiliationTransferRules.Allows'
Require-Present 'exact guest city permit' 'Code/core/schools/HistoricalAffiliationService.cs' 'FormalAffiliationTransferScope.Allows('
Require-Present 'exact guest kingdom permit' 'Code/core/schools/HistoricalAffiliationService.cs' 'FormalAffiliationTransferScope.AllowsKingdom('
Require-Present 'committed guest formal transfer scope' 'Code/core/schools/SchoolGuestOfficeService.cs' 'using (FormalAffiliationTransferScope.Open('
Require-Present 'committed guest vanilla city transfer' 'Code/core/schools/SchoolGuestOfficeService.cs' 'actor.joinCity(residence);'
Require-Present 'committed guest transfer verification' 'Code/core/schools/SchoolGuestOfficeService.cs' 'actor.city == residence && actor.kingdom == host'
Require-Absent 'travel cannot formally join a city' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'joinCity('
$courtServiceSource = Read-Source 'Code/core/court/CourtService.cs'
$clearOfficerStart = $courtServiceSource.IndexOf(
    'private static void ClearOfficer(', [System.StringComparison]::Ordinal)
$clearOfficerEnd = if ($clearOfficerStart -ge 0) {
    $courtServiceSource.IndexOf('private static void CloseStaleOfficerRows(',
        $clearOfficerStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($clearOfficerStart -lt 0 -or $clearOfficerEnd -le $clearOfficerStart) {
    $failures.Add('guest dismissal ClearOfficer segment could not be located')
} else {
    $clearOfficerSegment = $courtServiceSource.Substring($clearOfficerStart,
        $clearOfficerEnd - $clearOfficerStart)
    foreach ($forbiddenTransfer in @('joinCity(', 'joinKingdom(')) {
        if ($clearOfficerSegment.Contains($forbiddenTransfer)) {
            $failures.Add("guest dismissal cannot move formal affiliation: " +
                "found forbidden text '$forbiddenTransfer' in ClearOfficer")
        }
    }
}
Require-Absent 'guest end retains immutable home kingdom' 'Code/core/schools/GuestOfficeEndPersistence.cs' 'desired.HomeKingdomId ='
Require-Absent 'guest end retains immutable hometown city' 'Code/core/schools/GuestOfficeEndPersistence.cs' 'desired.HometownCityId ='
Require-Present 'archive WAL pragma' 'Code/core/db/LineageArchivePragmaService.cs' 'PRAGMA journal_mode=WAL'
Require-Present 'archive NORMAL sync pragma' 'Code/core/db/LineageArchivePragmaService.cs' 'PRAGMA synchronous=NORMAL'
Require-Present 'archive save checkpoint' 'Code/patch/AW_SavePatch.cs' 'LineageArchivePragmaService.CheckpointForSave'
Require-Present 'school performance counters' 'Code/core/schools/HistoricalSchoolDiagnostics.cs' 'IdleAllocatedBytes'
Require-Absent 'unconditional school residence revision' 'Code/core/schools/HistoricalAffiliationService.cs' 'AdvanceResidenceRevision()'
Require-Present 'membership runtime index projection' 'Code/core/schools/SchoolMembershipService.cs' 'HistoricalSchoolRuntimeIndex.Instance.Upsert'
Require-Present 'narrow affiliation revisions' 'Code/core/schools/HistoricalAffiliationService.cs' 'HistoricalSchoolRevisionService.ApplyAffiliationChange'
Require-Absent 'reputation twenty-five lecture gate' 'Code/core/schools/HistoricalSchoolLectureRules.cs' 'LaterTeacherMinimumReputation'
Require-Absent 'annual member snapshot runtime' 'Code/core/schools/HistoricalSchoolScheduler.cs' 'HistoricalSchoolAnnualMemberSnapshotBuilder.Build'
Require-Absent 'annual member snapshot action planner' 'Code/core/schools/HistoricalSchoolActionService.cs' 'HistoricalSchoolAnnualMemberSnapshot<Actor>'
Require-Absent 'annual member snapshot debate planner' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolAnnualMemberSnapshot<Actor>'
Require-Absent 'annual active affiliation array' 'Code/core/schools/SchoolGuestOfficeService.cs' 'ActiveSnapshots()'
Require-Absent 'quarter active affiliation array' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'ActiveSnapshots('
Require-Absent 'lecture formal-city equality' 'Code/core/schools/HistoricalSchoolActionService.cs' 'pTeacher.city?.data?.id != residence.data.id'

$chroniclePatch = Read-Source 'Code/patch/AW_ChroniclePatch.cs'
$removePrefix = $chroniclePatch.IndexOf(
    'internal static void RemoveKingdom_Prefix(',
    [System.StringComparison]::Ordinal)
$asylumNaturalization = if ($removePrefix -ge 0) {
    $chroniclePatch.IndexOf(
        'RoyalAsylumService.NaturalizeBeforeExtinction(pKingdom);',
        $removePrefix, [System.StringComparison]::Ordinal)
} else { -1 }
$claimCapture = if ($removePrefix -ge 0) {
    $chroniclePatch.IndexOf(
        'RoyalClaimService.CreateClaimsFromFallenKingdom(pKingdom)',
        $removePrefix, [System.StringComparison]::Ordinal)
} else { -1 }
if ($removePrefix -lt 0 -or $asylumNaturalization -lt $removePrefix -or
    $claimCapture -lt 0 -or $asylumNaturalization -gt $claimCapture) {
    $failures.Add(
        'royal asylum naturalization must run in the destruction prefix before claim capture')
}
Require-Absent 'zero-city verification cannot bypass destruction hooks' `
    'Code/core/lineage/KingdomExtinctionQueue.cs' `
    'makeSurvivorsToNomads()'

$activityQueue = Read-Source 'Code/core/schools/HistoricalSchoolActivityQueue.cs'
$debateFrame = $activityQueue.IndexOf('if (HistoricalSchoolDebateActivityService.ProcessFrame()) return;', [System.StringComparison]::Ordinal)
$deferredFrame = $activityQueue.IndexOf('HistoricalSchoolActionService.ProcessDeferredFrame()', [System.StringComparison]::Ordinal)
if ($debateFrame -lt 0 -or $deferredFrame -lt 0 -or $debateFrame -gt $deferredFrame) {
    $failures.Add('visible debate transitions must be scheduled before deferred school maintenance')
}

$diplomaticDeclarationService = Read-Source 'Code/core/lineage/DiplomaticWarDeclarationService.cs'
$warCompleteStart = $diplomaticDeclarationService.IndexOf('public static void OnKingdomYear(',
    [System.StringComparison]::Ordinal)
$warEffect = if ($warCompleteStart -ge 0) {
    $diplomaticDeclarationService.IndexOf('ExecutionResult result = Execute(pAttacker, defender);', $warCompleteStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$warClear = if ($warEffect -ge 0) {
    $diplomaticDeclarationService.IndexOf('Clear(pAttacker);', $warEffect,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($warEffect -lt 0 -or $warClear -lt 0 -or $warEffect -gt $warClear) {
    $failures.Add('diplomatic war must start before its declaration state is cleared')
}

$armyService = Read-Source 'Code/core/lineage/AWArmyService.cs'
$duplicateCleanupStart = $armyService.IndexOf('private static void CleanupDuplicateArmies(',
    [System.StringComparison]::Ordinal)
$duplicateCleanupEnd = if ($duplicateCleanupStart -ge 0) {
    $armyService.IndexOf('private static void MergeDuplicateIntoKeeper(', $duplicateCleanupStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($duplicateCleanupStart -lt 0 -or $duplicateCleanupEnd -lt 0 -or
    $armyService.Substring($duplicateCleanupStart, $duplicateCleanupEnd - $duplicateCleanupStart).Contains(
        'World.world.armies')) {
    $failures.Add('duplicate special-army cleanup must use the kingdom-role index')
}

$cacheRemovalStart = $armyService.IndexOf('private static void RemoveArmyFromCache(',
    [System.StringComparison]::Ordinal)
$cacheRemovalEnd = if ($cacheRemovalStart -ge 0) {
    $armyService.IndexOf('private static string BuildRoleIndexKey(', $cacheRemovalStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($cacheRemovalStart -lt 0 -or $cacheRemovalEnd -lt 0 -or
    $armyService.Substring($cacheRemovalStart, $cacheRemovalEnd - $cacheRemovalStart).Contains(
        'foreach (KeyValuePair<string, long> entry in RoleArmyCache)')) {
    $failures.Add('special army cache removal must use its reverse key index')
}

$findArmyStart = $armyService.IndexOf('public static Army FindArmy(',
    [System.StringComparison]::Ordinal)
$findArmyEnd = if ($findArmyStart -ge 0) {
    $armyService.IndexOf('public static void MarkArmy(', $findArmyStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($findArmyStart -lt 0 -or $findArmyEnd -lt 0 -or
    $armyService.Substring($findArmyStart, $findArmyEnd - $findArmyStart).Contains(
        'World.world.armies')) {
    $failures.Add('runtime special-army lookup must not scan the world army manager')
}

$retirementPatch = Read-Source 'Code/patch/AW_RetirementPatch.cs'
$retirementWarriorGate = $retirementPatch.IndexOf('if (rekt || !warrior) return;',
    [System.StringComparison]::Ordinal)
$retirementSupportedLookup = $retirementPatch.IndexOf(
    'SlaveService.IsSupportedSlaveryActor(__instance)', [System.StringComparison]::Ordinal)
if ($retirementWarriorGate -lt 0 -or $retirementSupportedLookup -lt 0 -or
    $retirementWarriorGate -gt $retirementSupportedLookup) {
    $failures.Add('actor updateAge must reject dead and non-warrior actors before race checks')
}
$retirementCheapGate = $retirementPatch.IndexOf(
    'SoldierRetirementRules.ShouldEnterActorUpdateAgeRetirement(',
    [System.StringComparison]::Ordinal)
$retirementTemporaryLookup = $retirementPatch.IndexOf(
    'TemporaryLevyService.IsTemporaryLevy(__instance)',
    [System.StringComparison]::Ordinal)
if ($retirementCheapGate -lt 0 -or $retirementTemporaryLookup -lt 0 -or
    $retirementCheapGate -gt $retirementTemporaryLookup) {
    $failures.Add('actor updateAge must reject non-warriors before temporary-service lookups')
}

$standingArmyService = Read-Source 'Code/core/lineage/StandingArmyService.cs'
$ordinaryMilitaryStart = $standingArmyService.IndexOf('public static int CountOrdinaryMilitary(',
    [System.StringComparison]::Ordinal)
$ordinaryMilitaryEnd = if ($ordinaryMilitaryStart -ge 0) {
    $standingArmyService.IndexOf('public static bool ShouldKeepWithinOriginalArmyLimit(',
        $ordinaryMilitaryStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($ordinaryMilitaryStart -lt 0 -or $ordinaryMilitaryEnd -lt 0 -or
    $standingArmyService.Substring($ordinaryMilitaryStart,
        $ordinaryMilitaryEnd - $ordinaryMilitaryStart).Contains('foreach (')) {
    $failures.Add('temporary levy city checks must use direct normal-army counts')
}
Require-Present 'standing maintenance publishes one city readiness observation' `
    'Code/core/lineage/StandingArmyService.cs' `
    'KingdomMilitaryReadinessService.ObserveCity(pCity);'

$readinessService = Read-Source 'Code/core/lineage/KingdomMilitaryReadinessService.cs'
$readinessQueryStart = $readinessService.IndexOf('public static bool HasReadyStandingCore(',
    [System.StringComparison]::Ordinal)
$readinessQueryEnd = if ($readinessQueryStart -ge 0) {
    $readinessService.IndexOf('public static void ObserveCity(', $readinessQueryStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($readinessQueryStart -lt 0 -or $readinessQueryEnd -lt 0) {
    $failures.Add('standing-core readiness must expose an indexed query and city observation API')
} else {
    $readinessQueryRegion = $readinessService.Substring($readinessQueryStart,
        $readinessQueryEnd - $readinessQueryStart)
    if ($readinessQueryRegion.Contains('for (') -or
        $readinessQueryRegion.Contains('foreach (')) {
        $failures.Add('standing-core readiness query must never enumerate kingdom cities')
    }
}
if (-not $readinessService.Contains('KingdomMilitaryReadinessRules.MaxCitiesPerWorkItem') -or
    -not $readinessService.Contains('DeferredRuntimeWorkService.EnqueueCoalesced(') -or
    -not $readinessService.Contains('kingdom.cities[i]')) {
    $failures.Add('standing-core readiness backfill must use direct cursors and fixed deferred batches')
}
if (-not $readinessService.Contains('KingdomMilitaryReadinessIndex')) {
    $failures.Add('standing-core counters must use the tested generation index')
}
Require-Present 'warrior death invalidates only its ordinary army city readiness' `
    'Code/patch/AW_ActorDeathPatch.cs' `
    'KingdomMilitaryReadinessService.MarkOrdinaryArmyActorDirty(__instance);'
Require-Present 'city transfer updates standing readiness membership' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'KingdomMilitaryReadinessService.OnCityKingdomChanged('
Require-Present 'city destruction removes standing readiness membership' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'KingdomMilitaryReadinessService.OnCityDestroyed(__instance);'
Require-Present 'army reassignment dirties only affected city readiness' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))'
Require-Present 'army reassignment uses coalesced city readiness refresh' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'KingdomMilitaryReadinessService.MarkArmyCitiesDirty('
if (-not $readinessService.Contains('ResolveOrdinaryArmyCity(')) {
    $failures.Add('army readiness dirties only original ordinary city armies')
}
$markCityDirtyStart = $readinessService.IndexOf('public static void MarkCityDirty(',
    [System.StringComparison]::Ordinal)
$markArmyDirtyStart = $readinessService.IndexOf('public static void MarkArmyCitiesDirty(',
    [System.StringComparison]::Ordinal)
if ($markCityDirtyStart -lt 0 -or $markArmyDirtyStart -lt 0 -or
    -not $readinessService.Substring($markCityDirtyStart,
        $markArmyDirtyStart - $markCityDirtyStart).Contains(
            'if (States.Count == 0) return;')) {
    $failures.Add('load-time army repair cannot enqueue readiness work before the index exists')
}
Require-Present 'archive load rebuilds standing readiness index' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'KingdomMilitaryReadinessService.RebuildRuntime'
Require-Present 'archive switch clears standing readiness index' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'KingdomMilitaryReadinessService.ClearRuntime'

$royalGuardService = Read-Source 'Code/core/lineage/RoyalGuardService.cs'
if (-not $royalGuardService.Contains(
        'bool standingCoreReady = !militaryEmergency &&') -or
    $royalGuardService.Contains(
        'bool standingCoreReady = active.Count > 0 ||')) {
    $failures.Add('existing guards may be preserved but must never satisfy the standing-core recruitment gate')
}
if ($royalGuardService.Contains('CollectActiveGuardsFallbackBounded(') -or
    $royalGuardService.Contains('ROYAL_GUARD_ACTIVE_SCAN_CURSOR')) {
    $failures.Add('active guard recovery must use the army or persisted roster, never a partial kingdom population scan')
}
if (-not $royalGuardService.Contains(
        'RoyalGuardMaintenanceRules.ShouldClearStaleGuardStateWithoutRoster(')) {
    $failures.Add('missing army and roster must clear stale kingdom guard hints')
}

$warNoticeService = Read-Source 'Code/core/lineage/WarNoticeService.cs'
$preparationSummaryStart = $warNoticeService.IndexOf(
    'public static bool TryGetPreparationSummary(', [System.StringComparison]::Ordinal)
$preparationSummaryEnd = if ($preparationSummaryStart -ge 0) {
    $warNoticeService.IndexOf('public static ', $preparationSummaryStart + 1,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($preparationSummaryStart -lt 0 -or $preparationSummaryEnd -lt 0) {
    $failures.Add('missing cached war preparation summary API')
} elseif ($warNoticeService.Substring($preparationSummaryStart,
        $preparationSummaryEnd - $preparationSummaryStart).Contains('foreach (')) {
    $failures.Add('policy redraw summary must not enumerate notices, armies, cities, or actors')
}
$noticeArmyChangedStart = $warNoticeService.IndexOf('public static void OnArmyChanged(',
    [System.StringComparison]::Ordinal)
$noticeArmyChangedEnd = if ($noticeArmyChangedStart -ge 0) {
    $warNoticeService.IndexOf('public static void QueueArmyChanged(', $noticeArmyChangedStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($noticeArmyChangedStart -lt 0 -or $noticeArmyChangedEnd -lt 0 -or
    -not $warNoticeService.Substring($noticeArmyChangedStart,
        $noticeArmyChangedEnd - $noticeArmyChangedStart).Contains(
            'ArmyDeploymentService.OnArmyChanged(pKingdom, pArmy, pRosterExpanded);')) {
    $failures.Add('army changes must route once through the defender deployment group')
}
if ($noticeArmyChangedStart -ge 0 -and $noticeArmyChangedEnd -gt $noticeArmyChangedStart -and
    $warNoticeService.Substring($noticeArmyChangedStart,
        $noticeArmyChangedEnd - $noticeArmyChangedStart).Contains(
            'foreach (string signature in signatures)')) {
    $failures.Add('army changes must not fan out synchronously to every incoming notice')
}
$noticeYearStart = $warNoticeService.IndexOf('public static void OnKingdomYear(',
    [System.StringComparison]::Ordinal)
$noticeYearEnd = if ($noticeYearStart -ge 0) {
    $warNoticeService.IndexOf('public static void OnDiplomaticDeclarationClearing(', $noticeYearStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($noticeYearStart -lt 0 -or $noticeYearEnd -lt 0 -or
    $warNoticeService.Substring($noticeYearStart, $noticeYearEnd - $noticeYearStart).Contains(
        'ArmyDeploymentService.RefreshNotice(')) {
    $failures.Add('kingdom-year notice maintenance must not rescan deployment cities or armies')
}

$deploymentService = Read-Source 'Code/core/lineage/ArmyDeploymentService.cs'
if (-not $deploymentService.Contains('KingdomNoticeGroups') -or
    -not $deploymentService.Contains('SortedSet<NoticePriority>') -or
    -not $deploymentService.Contains('ResolvePrimaryAssignments(') -or
    -not $deploymentService.Contains('BuildAssignmentKey(')) {
    $failures.Add('each kingdom side must own one deterministic primary deployment across concurrent notices')
}
$militaryEmergencyService = Read-Source 'Code/core/lineage/MilitaryEmergencyService.cs'
if (-not $militaryEmergencyService.Contains(
        'ArmyDeploymentService.OnKingdomEnteredWar(kingdom);')) {
    $failures.Add('entering any real war must release stale prewar deployment jobs')
}
$deploymentCleanupStart = $deploymentService.IndexOf('private static void CleanupBatch(',
    [System.StringComparison]::Ordinal)
$deploymentCleanupEnd = if ($deploymentCleanupStart -ge 0) {
    $deploymentService.IndexOf('private static void ScheduleDiscovery(', $deploymentCleanupStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$deploymentCleanupRegion = if ($deploymentCleanupStart -ge 0 -and
    $deploymentCleanupEnd -gt $deploymentCleanupStart) {
    $deploymentService.Substring($deploymentCleanupStart,
        $deploymentCleanupEnd - $deploymentCleanupStart)
} else { '' }
$deploymentOwnRemoval = $deploymentCleanupRegion.IndexOf(
    'assignments.ActorIds.Remove(batch[i]);', [System.StringComparison]::Ordinal)
$deploymentActorResolve = $deploymentCleanupRegion.IndexOf(
    'Actor actor = ResolveActor(batch[i]);', [System.StringComparison]::Ordinal)
if ($deploymentOwnRemoval -lt 0 -or $deploymentActorResolve -lt 0 -or
    $deploymentOwnRemoval -gt $deploymentActorResolve) {
    $failures.Add('deployment cleanup must remove the closing notice member before inspecting actor state')
}

$vanguardService = Read-Source 'Code/core/lineage/TemporarySlaveVanguardService.cs'
$vanguardAssaultStart = $vanguardService.IndexOf(
    'public static bool ShouldDelayBehindVanguard(', [System.StringComparison]::Ordinal)
$vanguardAssaultEnd = if ($vanguardAssaultStart -ge 0) {
    $vanguardService.IndexOf('public static void OnEmergencyChanged(',
        $vanguardAssaultStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($vanguardAssaultStart -lt 0 -or $vanguardAssaultEnd -lt 0) {
    $failures.Add('missing bounded vanguard assault gate')
} else {
    $vanguardAssaultRegion = $vanguardService.Substring($vanguardAssaultStart,
        $vanguardAssaultEnd - $vanguardAssaultStart)
    if ($vanguardAssaultRegion.Contains('foreach (') -or
        $vanguardAssaultRegion.Contains('for (') -or
        $vanguardAssaultRegion.Contains('Finder.')) {
        $failures.Add('vanguard assault ordering must use only indexed army, captain, and city lookups')
    }
}
$candidateStart = $vanguardService.IndexOf('public static void OnCandidateAvailable(',
    [System.StringComparison]::Ordinal)
$candidateEnd = if ($candidateStart -ge 0) {
    $vanguardService.IndexOf('public static void OnMemberInvalidated(', $candidateStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($candidateStart -lt 0 -or $candidateEnd -lt 0 -or
    $vanguardService.Substring($candidateStart, $candidateEnd - $candidateStart).Contains(
        'ResetScanPass(')) {
    $failures.Add('a new slave event must evaluate that actor without restarting a kingdom scan')
}
$candidateRegion = if ($candidateStart -ge 0 -and $candidateEnd -gt $candidateStart) {
    $vanguardService.Substring($candidateStart, $candidateEnd - $candidateStart)
} else { '' }
$candidateForceCleanup = $candidateRegion.IndexOf('if (state.ForceCleanup)',
    [System.StringComparison]::Ordinal)
$candidateClearsCleaning = $candidateRegion.IndexOf('state.Cleaning = false;',
    [System.StringComparison]::Ordinal)
if ($candidateForceCleanup -lt 0 -or $candidateClearsCleaning -lt 0 -or
    $candidateForceCleanup -gt $candidateClearsCleaning) {
    $failures.Add('forced vanguard cleanup must take precedence over new candidate events')
}

$memberInvalidationStart = $vanguardService.IndexOf('public static void OnMemberInvalidated(',
    [System.StringComparison]::Ordinal)
$memberInvalidationEnd = if ($memberInvalidationStart -ge 0) {
    $vanguardService.IndexOf('public static void OnActorKingdomChanged(',
        $memberInvalidationStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($memberInvalidationStart -lt 0 -or $memberInvalidationEnd -lt 0 -or
    $vanguardService.Substring($memberInvalidationStart,
        $memberInvalidationEnd - $memberInvalidationStart).Contains('ResetScanPass(')) {
    $failures.Add('vanguard casualties must not restart an in-progress kingdom scan')
}

$slaveService = Read-Source 'Code/core/lineage/SlaveService.cs'
$combatCaptureStart = $slaveService.IndexOf('public static bool TryCaptureCombatTarget(',
    [System.StringComparison]::Ordinal)
$combatCaptureEnd = if ($combatCaptureStart -ge 0) {
    $slaveService.IndexOf('public static bool CaptureTargetAsSlave(', $combatCaptureStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$captureSlaveryGate = if ($combatCaptureStart -ge 0) {
    $slaveService.IndexOf('IsSlaveryEnabled(captorKingdom)', $combatCaptureStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$captureTargetEligibility = if ($combatCaptureStart -ge 0) {
    $slaveService.IndexOf('CanBeCapturedAsTarget(pTarget', $combatCaptureStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($combatCaptureEnd -lt 0 -or $captureSlaveryGate -lt 0 -or
    $captureTargetEligibility -lt 0 -or $captureSlaveryGate -gt $captureTargetEligibility) {
    $failures.Add('weapon-hit capture must reject non-slavery attackers before target identity checks')
}

Require-Present 'restoration uprising persists active state' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_ACTIVE'
Require-Present 'restoration uprising persists seed city' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_SEED_CITY_ID'
Require-Present 'restoration uprising persists bounded roster' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_ROSTER_IDS'
Require-Present 'restoration uprising persists annual work count' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_WORK_ITEMS'
Require-Present 'restoration uprising persists candidate count' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_SCANNED'
Require-Present 'restoration uprising persists recruit count' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_RECRUITED'
Require-Present 'restoration uprising persists resident cursor' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_ACTOR_CURSOR'
Require-Present 'restoration uprising marks exact members' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_MEMBER'
Require-Present 'restoration uprising records original residence' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_ORIGINAL_CITY_ID'
Require-Present 'restoration uprising marks its army' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_ARMY'
Require-Present 'restoration uprising records army id' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_ARMY_ID'
Require-Present 'restoration uprising markers share campaign identity' `
    'Code/core/lineage/LineageKeys.cs' 'RESTORATION_UPRISING_CAMPAIGN_ID'
Require-Present 'restoration uprising has a dedicated enlistment scope' `
    'Code/core/lineage/MilitaryRecruitmentScope.cs' 'RestorationUprising'
Require-Present 'restoration uprising suppresses permanent enlistment history' `
    'Code/core/lineage/MilitaryRecruitmentScope.cs' '_current == MilitaryRecruitmentKind.RestorationUprising'
Require-Present 'restoration uprising has a dedicated bounded runtime service' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'internal static class RestorationUprisingMobilizationService'
Require-Present 'restoration uprising work uses the deferred queue' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'restoration uprising applies per-item candidate budget' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'RestorationUprisingRules.MaxCandidatesPerWorkItem'
Require-Present 'restoration uprising applies per-item mutation budget' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'RestorationUprisingRules.MaxRecruitsPerWorkItem'
Require-Present 'restoration uprising cleanup uses a fixed batch' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'RestorationUprisingRules.DemobilizationBatchSize'
Require-Present 'restoration uprising persists exact roster ids' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'LineageKeys.RESTORATION_UPRISING_ROSTER_IDS'
Require-Absent 'restoration uprising must not scan world actors' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' 'World.world.units'
Require-Absent 'restoration uprising must not scan kingdom population' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' 'pKingdom.getUnits()'
Require-Present 'ordinary levies stop before scanning an active restoration realm' `
    'Code/core/lineage/TemporaryLevyService.cs' `
    'AutonomousRestorationService.IsActiveCampaignKingdom(pKingdom)'
Require-Present 'stale uprising roster requires physical cleanup before removal' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'CleanupMemberSafely(pState, pKingdom, actor))'
$temporaryLevySource = Read-Source 'Code/core/lineage/TemporaryLevyService.cs'
$temporaryEmergencyStart = $temporaryLevySource.IndexOf(
    'public static void OnEmergencyChanged(', [System.StringComparison]::Ordinal)
$temporaryEmergencyEnd = $temporaryLevySource.IndexOf(
    'private static void ProcessEmergencyChanged(', $temporaryEmergencyStart,
    [System.StringComparison]::Ordinal)
if ($temporaryEmergencyStart -lt 0 -or $temporaryEmergencyEnd -lt 0 -or
    -not $temporaryLevySource.Substring($temporaryEmergencyStart,
        $temporaryEmergencyEnd - $temporaryEmergencyStart).Contains(
            'AutonomousRestorationService.IsActiveCampaignKingdom(pKingdom)')) {
    $failures.Add('active restoration must reject ordinary levy work before enqueue')
}
Require-Present 'autonomous restoration enforces the Mandate vacancy gate' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'RoyalRestorationRules.CanStartAutonomousCampaign('
Require-Present 'autonomous restoration reads current Mandate occupancy' `
    'Code/core/lineage/AutonomousRestorationService.cs' 'MandateService.Exists'
Require-Present 'autonomous restoration ranks bounded seed candidates' `
    'Code/core/lineage/AutonomousRestorationService.cs' 'RestorationSeedScore'
Require-Present 'autonomous restoration starts local uprising mobilization' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'RestorationUprisingMobilizationService.TryStartWithInitialCohort('
Require-Present 'active restoration refreshes bounded uprising recruitment' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'RestorationUprisingMobilizationService.OnCampaignYear('
Require-Present 'completed restoration demobilizes tracked uprising roster' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'RestorationUprisingMobilizationService.Complete('
Require-Present 'failed restoration cleans tracked uprising roster' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'RestorationUprisingMobilizationService.Fail('
Require-Present 'restoration launch and annual maintenance share core war selection' `
    'Code/core/lineage/AutonomousRestorationService.cs' 'TryStartNextCoreWar('
Require-Present 'world reset clears restoration uprising runtime state' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'RestorationUprisingMobilizationService.ClearRuntime();'
$autonomousRestorationSource = Read-Source `
    'Code/core/lineage/AutonomousRestorationService.cs'
$maintainCampaignStart = $autonomousRestorationSource.IndexOf(
    'private static void MaintainCampaign(', [System.StringComparison]::Ordinal)
$maintainCampaignEnd = $autonomousRestorationSource.IndexOf(
    'private static bool TryStartNextCoreWar(', $maintainCampaignStart,
    [System.StringComparison]::Ordinal)
$ensureCampaignRuntime = if ($maintainCampaignStart -ge 0) {
    $autonomousRestorationSource.IndexOf('EnsureCampaignRuntime(',
        $maintainCampaignStart, [System.StringComparison]::Ordinal)
} else { -1 }
$resumeUprisingRuntime = if ($maintainCampaignStart -ge 0) {
    $autonomousRestorationSource.IndexOf(
        'RestorationUprisingMobilizationService.OnCampaignYear(',
        $maintainCampaignStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($maintainCampaignEnd -lt 0 -or $ensureCampaignRuntime -lt $maintainCampaignStart -or
    $resumeUprisingRuntime -lt $maintainCampaignStart -or
    $ensureCampaignRuntime -gt $resumeUprisingRuntime) {
    $failures.Add('campaign runtime projection must be restored before uprising recruitment resumes')
}
Require-Present 'restoration fallback war keeps the prewar defender snapshot' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'Kingdom fallbackDefender = fallback.kingdom;'
$restorationLaunchStart = $autonomousRestorationSource.IndexOf(
    'RestorationUprisingMobilizationService.TryStartWithInitialCohort(',
    [System.StringComparison]::Ordinal)
$restorationLaunchEnd = if ($restorationLaunchStart -ge 0) {
    $autonomousRestorationSource.IndexOf(
        'return true;', $restorationLaunchStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$restorationLaunchBody = if ($restorationLaunchStart -ge 0 -and
    $restorationLaunchEnd -ge $restorationLaunchStart) {
    $autonomousRestorationSource.Substring($restorationLaunchStart,
        $restorationLaunchEnd - $restorationLaunchStart)
} else { '' }
if ($restorationLaunchStart -lt 0 -or $restorationLaunchEnd -lt 0 -or
    $restorationLaunchBody.Contains('CompleteCampaign(')) {
    $failures.Add('newly launched uprising must not complete before synchronous initial mobilization succeeds')
}
Require-Present 'archive load rebuilds restoration uprising runtime' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'AutonomousRestorationService.RebuildRuntime'
Require-Present 'archive switch clears restoration uprising runtime before rebuild' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'AutonomousRestorationService.ClearRuntime'
Require-Present 'restoration player feedback explains active Mandate order' `
    'Code/patch/AW_UnitTabPatch.cs' 'case "restoration_mandate_order":'
Require-Present 'restoration Mandate-order error is localized' `
    'Locales/aw3_war_decisions.csv' 'aw_restoration_error_mandate_order,'
Require-Present 'restoration uprising never reuses a foreign army id' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'IsArmyOwnedBy(army, pKingdom)'
Require-Present 'restoration uprising honors historical military eligibility' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'HistoricalMasterMilitaryContext.OrdinaryWarrior'
Require-Absent 'restoration uprising does not ban every historical figure' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'pActor.hasTrait("figure")'
Require-Absent 'restoration uprising does not ban every first master' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'pActor.hasTrait("first")'
Require-Present 'restoration service exposes bounded AI autonomy preference' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'public static bool ShouldPreferSelfRestoration('
Require-Present 'host AI yields strong viable claims to autonomous restoration' `
    'Code/core/lineage/WarDecisionAI.cs' `
    'AutonomousRestorationService.ShouldPreferSelfRestoration('
$uprisingMobilizationSource = Read-Source `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs'
$uprisingEnlistStart = $uprisingMobilizationSource.IndexOf(
    'private static bool Enlist(', [System.StringComparison]::Ordinal)
$uprisingEnlistEnd = $uprisingMobilizationSource.IndexOf(
    'private static void PublishArmyChanged(', $uprisingEnlistStart,
    [System.StringComparison]::Ordinal)
if ($uprisingEnlistStart -lt 0 -or $uprisingEnlistEnd -lt 0 -or
    $uprisingMobilizationSource.Substring($uprisingEnlistStart,
        $uprisingEnlistEnd - $uprisingEnlistStart).Contains('QueueArmyChanged(')) {
    $failures.Add('restoration uprising must publish one army change per batch, not per recruit')
}
Require-Present 'restoration uprising publishes one batched army change' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'if (recruited > 0) PublishArmyChanged(state, kingdom);'
Require-Present 'restoration launch falls back to a real former-owner war' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'TryStartFormerOwnerWar('
Require-Present 'restoration launch uses former-owner war only without a core war' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'if (!coreWarStarted)'
Require-Present 'restoration claimant clears old roles before accession' `
    'Code/core/lineage/KingdomIdentityContinuityService.cs' `
    'PrepareClaimantForRestorationAccession('
Require-Present 'restoration accession atomically closes court office' `
    'Code/core/lineage/KingdomIdentityContinuityService.cs' `
    'CourtService.ClearOfficeForReignTransition('
Require-Present 'restoration accession retires old general and fief' `
    'Code/core/lineage/KingdomIdentityContinuityService.cs' `
    'GeneralService.RetireForSuccession(pClaimant);'
Require-Present 'restoration accession clears old host heir registration' `
    'Code/core/lineage/KingdomIdentityContinuityService.cs' `
    'HeirService.ClearHeir(previousHost);'
Require-Present 'foreign uprising army references clear their stale marker' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'DiscardForeignArmyReference(pState, pKingdom);'
Require-Present 'one invalid uprising candidate cannot abort its batch' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'TryEnlistCandidate(pState, pKingdom, pSeed, actor)'
Require-Present 'one invalid uprising member cannot abort demobilization' `
    'Code/core/lineage/RestorationUprisingMobilizationService.cs' `
    'CleanupMemberSafely(pState, pKingdom, actor);'
Require-Present 'Mandate order skips dormant restoration candidate queries' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'if (MandateService.Exists) return;'

Require-Present 'Mandate phase persists phase id' `
    'Code/core/db/MandateStateTableItem.cs' 'public string mandate_phase'
Require-Present 'Mandate phase persists start year' `
    'Code/core/db/MandateStateTableItem.cs' 'public int phase_since_year'
Require-Present 'Mandate phase persists recovery streak' `
    'Code/core/db/MandateStateTableItem.cs' 'public int phase_stability_years'
Require-Present 'Mandate phase persists catalyst pressure' `
    'Code/core/db/MandateStateTableItem.cs' 'public int catalyst_score'
Require-Present 'Mandate phase persists annual evaluation guard' `
    'Code/core/db/MandateStateTableItem.cs' 'public int phase_last_year'
Require-Present 'Mandate phase has a dedicated singleton service' `
    'Code/core/lineage/MandatePhaseService.cs' 'internal static class MandatePhaseService'
Require-Present 'Mandate phase exposes cached occupation multiplier' `
    'Code/core/lineage/MandatePhaseService.cs' 'public static float OccupationMultiplier'
Require-Present 'Mandate phase exposes cached contest gate' `
    'Code/core/lineage/MandatePhaseService.cs' 'public static bool CanContestMandate'
Require-Present 'world switch clears Mandate phase runtime cache' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' 'MandatePhaseService.ClearRuntime'
Require-Absent 'Mandate phase service must not scan world actors' `
    'Code/core/lineage/MandatePhaseService.cs' 'World.world.units'
Require-Absent 'Mandate phase service must not scan kingdom population' `
    'Code/core/lineage/MandatePhaseService.cs' '.getUnits()'
Require-Present 'vacant Mandate performs one global phase evaluation' `
    'Code/core/lineage/MandateService.cs' `
    'MandatePhaseService.EvaluateVacantWorldYear('
Require-Present 'active Mandate evaluates phase after annual legitimacy' `
    'Code/core/lineage/MandateService.cs' `
    'MandatePhaseService.EvaluateActiveMandateYear('
Require-Present 'Mandate collapse forces chaos before rebel creation' `
    'Code/core/lineage/MandateService.cs' `
    'MandatePhaseService.ForceChaos("mandate_collapse");'
Require-Present 'new Mandate enters first-rule or renewal phase' `
    'Code/core/lineage/MandateService.cs' `
    'MandatePhaseService.OnMandateEstablished('
Require-Present 'discrete Mandate changes feed catalyst pressure' `
    'Code/core/lineage/MandateService.cs' `
    'MandatePhaseRules.CatalystDeltaForMandateChange(pDelta)'
Require-Present 'Mandate war casus belli reads the cached chaos gate' `
    'Code/core/lineage/WarDecisionService.cs' `
    'MandatePhaseService.CanContestMandate &&'
Require-Present 'Mandate war execution rechecks the cached chaos gate' `
    'Code/core/lineage/WarTerritoryService.cs' `
    'if (!MandatePhaseService.CanContestMandate) return false;'
$warDecisionSource = Read-Source 'Code/core/lineage/WarDecisionService.cs'
$validCbStart = $warDecisionSource.IndexOf(
    'public static bool HasValidCasusBelli(', [System.StringComparison]::Ordinal)
$validCbEnd = $warDecisionSource.IndexOf(
    'public static long CreateClaim(', $validCbStart, [System.StringComparison]::Ordinal)
$validCbPhaseGate = if ($validCbStart -ge 0) {
    $warDecisionSource.IndexOf(
        'type == MandateService.WAR_TIANMING && !MandatePhaseService.CanContestMandate',
        $validCbStart, [System.StringComparison]::Ordinal)
} else { -1 }
$validCbClaimLookup = if ($validCbStart -ge 0) {
    $warDecisionSource.IndexOf('HasActiveClaim(', $validCbStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($validCbEnd -lt 0 -or $validCbPhaseGate -lt $validCbStart -or
    $validCbClaimLookup -lt $validCbStart -or $validCbPhaseGate -gt $validCbClaimLookup) {
    $failures.Add('Mandate phase must reject stale claims before the active-claim shortcut')
}
$startWarStart = $warDecisionSource.IndexOf(
    'private static War StartWar(', [System.StringComparison]::Ordinal)
$startWarEnd = $warDecisionSource.IndexOf(
    'private static bool CanPassVassalWarRules(', $startWarStart,
    [System.StringComparison]::Ordinal)
$systemPhaseGate = if ($startWarStart -ge 0) {
    $warDecisionSource.IndexOf(
        'type == MandateService.WAR_TIANMING && !MandatePhaseService.CanContestMandate',
        $startWarStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($startWarEnd -lt 0 -or $systemPhaseGate -lt $startWarStart -or
    $systemPhaseGate -gt $startWarEnd) {
    $failures.Add('system Mandate wars must pass the same cached chaos gate')
}
Require-Present 'autonomous restoration scheduler reads the cached chaos gate' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'MandatePhaseService.CanLaunchAutonomousRestoration'
Require-Present 'restoration phase error has a unit action mapping' `
    'Code/patch/AW_UnitTabPatch.cs' 'case "restoration_phase_order":'
Require-Present 'restoration phase error is localized' `
    'Locales/aw3_war_decisions.csv' 'aw_restoration_error_phase_order,'
Require-Present 'Mandate window reads cached global phase' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'MandatePhaseService.CurrentPhase'
Require-Present 'Mandate window reads cached phase start year' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'MandatePhaseService.PhaseSinceYear'
Require-Present 'Mandate window reads cached catalyst pressure' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'MandatePhaseService.CatalystScore'
Require-Present 'Mandate phase label is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_phase,'
Require-Present 'golden phase is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_phase_golden,'
Require-Present 'decline phase is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_phase_decline,'
Require-Present 'chaos phase is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_phase_chaos,'
Require-Present 'renewal phase is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_phase_renewal,'
Require-Present 'phase start year is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_phase_since,'
Require-Present 'phase catalyst is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_catalyst,'

Require-Present 'grand sacrifice has a structured archive table' `
    'Code/core/db/SacrificeRecordTableItem.cs' `
    'public sealed class SacrificeRecordTableItem'
Require-Present 'grand sacrifice record has a primary id' `
    'Code/core/db/SacrificeRecordTableItem.cs' `
    '[TableItemDef(pIsPrimary: true)] public long record_id;'
Require-Present 'grand sacrifice records the Mandate period snapshot' `
    'Code/core/db/SacrificeRecordTableItem.cs' 'public long period_id = -1;'
Require-Present 'grand sacrifice records the ruler snapshot' `
    'Code/core/db/SacrificeRecordTableItem.cs' 'public long emperor_actor_id = -1;'
Require-Present 'grand sacrifice records qualification' `
    'Code/core/db/SacrificeRecordTableItem.cs' 'public int qualified;'
Require-Present 'grand sacrifice records its exact roll' `
    'Code/core/db/SacrificeRecordTableItem.cs' 'public int roll_basis_points;'
Require-Present 'grand sacrifice records all four effect channels' `
    'Code/core/db/SacrificeRecordTableItem.cs' 'public int annual_mandate_delta;'
Require-Present 'grand sacrifice persists its last completion year' `
    'Code/core/lineage/LineageKeys.cs' 'MANDATE_SACRIFICE_LAST_YEAR'
Require-Present 'grand sacrifice persists its buff expiry' `
    'Code/core/lineage/LineageKeys.cs' 'MANDATE_SACRIFICE_BUFF_UNTIL'
Require-Present 'grand sacrifice persists its annual buff value' `
    'Code/core/lineage/LineageKeys.cs' 'MANDATE_SACRIFICE_BUFF_DELTA'
Require-Present 'grand sacrifice persists ritual completeness' `
    'Code/core/lineage/LineageKeys.cs' 'MANDATE_RITUAL_COMPLETENESS'
Require-Present 'grand sacrifice has one settlement service' `
    'Code/core/lineage/MandateSacrificeService.cs' `
    'internal static class MandateSacrificeService'
Require-Present 'grand sacrifice owns a private RNG' `
    'Code/core/lineage/MandateSacrificeService.cs' `
    'private static readonly System.Random _random = new System.Random();'
Require-Present 'grand sacrifice requires the rites policy' `
    'Code/core/lineage/MandateSacrificeService.cs' '"aw_policy_mandate_rites"'
Require-Present 'grand sacrifice requires rites and music technology' `
    'Code/core/lineage/MandateSacrificeService.cs' '"aw_tech_rites_music"'
Require-Present 'grand sacrifice qualification scans only capital buildings' `
    'Code/core/lineage/MandateRitesService.cs' `
    'foreach (Building building in capital.buildings)'
Require-Present 'grand sacrifice accepts only temple assets' `
    'Code/core/lineage/MandateRitesService.cs' `
    'assetId.StartsWith("temple_", StringComparison.Ordinal)'
Require-Absent 'grand sacrifice must not scan world actors' `
    'Code/core/lineage/MandateSacrificeService.cs' 'World.world.units'
Require-Absent 'grand sacrifice must not scan kingdom population' `
    'Code/core/lineage/MandateSacrificeService.cs' '.getUnits()'
Require-Present 'grand sacrifice inserts one structured record' `
    'Code/core/lineage/MandateSacrificeService.cs' `
    'DB.Insert(SacrificeRecordTableItem.GetTableName(),'
$sacrificeServicePath = Join-Path $root `
    'Code/core/lineage/MandateSacrificeService.cs'
if ([System.IO.File]::Exists($sacrificeServicePath)) {
    $sacrificeSource = [System.IO.File]::ReadAllText($sacrificeServicePath)
    $recordInsertCount = [regex]::Matches($sacrificeSource,
        [regex]::Escape('DB.Insert(SacrificeRecordTableItem.GetTableName(),')).Count
    if ($recordInsertCount -ne 1) {
        $failures.Add("grand sacrifice must insert exactly one structured row per settlement path: found $recordInsertCount insert sites")
    }
}
Require-Present 'Mandate settlement applies sacrifice effects centrally' `
    'Code/core/lineage/MandateService.cs' `
    'public static bool ApplySacrificeOutcome(Kingdom pKingdom,'
Require-Present 'Mandate settlement uses the sacrifice effect value object' `
    'Code/core/lineage/MandateService.cs' 'MandateSacrificeEffects pEffects'
Require-Absent 'fixed ritual settlement path is removed' `
    'Code/core/lineage/MandateService.cs' 'TryStabilizeMandate('
$mandateSacrificeSource = Read-Source 'Code/core/lineage/MandateService.cs'
$sacrificeApplyStart = $mandateSacrificeSource.IndexOf(
    'public static bool ApplySacrificeOutcome(', [System.StringComparison]::Ordinal)
$sacrificeApplyEnd = if ($sacrificeApplyStart -ge 0) {
    $mandateSacrificeSource.IndexOf(
        'public static bool HasMandateProtection(', $sacrificeApplyStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($sacrificeApplyStart -lt 0 -or $sacrificeApplyEnd -lt 0) {
    $failures.Add('grand sacrifice settlement method boundaries are missing')
} else {
    $sacrificeApplyBody = $mandateSacrificeSource.Substring(
        $sacrificeApplyStart, $sacrificeApplyEnd - $sacrificeApplyStart)
    $changeCount = [regex]::Matches($sacrificeApplyBody,
        [regex]::Escape('ChangeMandate(')).Count
    if ($changeCount -ne 1) {
        $failures.Add("grand sacrifice settlement must change Mandate exactly once: found $changeCount calls")
    }
    if (-not $sacrificeApplyBody.Contains('pRecordEvent: false')) {
        $failures.Add('grand sacrifice must defer its Mandate event until all effect channels are committed')
    }
    $sacrificeStateUpdate = $sacrificeApplyBody.LastIndexOf(
        'UpdateState(', [System.StringComparison]::Ordinal)
    $sacrificeEventWrite = $sacrificeApplyBody.IndexOf(
        'RecordEvent(', [System.StringComparison]::Ordinal)
    if ($sacrificeStateUpdate -lt 0 -or $sacrificeEventWrite -lt 0 -or
        $sacrificeEventWrite -lt $sacrificeStateUpdate) {
        $failures.Add('grand sacrifice event snapshot must be written after authority and prestige update')
    }
}
$mandateYearlyStart = $mandateSacrificeSource.IndexOf(
    'private static int CalculateYearlyDelta(', [System.StringComparison]::Ordinal)
$mandateYearlyEnd = $mandateSacrificeSource.IndexOf(
    'private static int CalculateAuthority(', $mandateYearlyStart,
    [System.StringComparison]::Ordinal)
if ($mandateYearlyStart -lt 0 -or $mandateYearlyEnd -lt 0) {
    $failures.Add('Mandate annual delta method boundaries are missing')
} else {
    $mandateYearlyBody = $mandateSacrificeSource.Substring(
        $mandateYearlyStart, $mandateYearlyEnd - $mandateYearlyStart)
    foreach ($needle in @(
        'LineageKeys.MANDATE_SACRIFICE_BUFF_UNTIL',
        'LineageKeys.MANDATE_SACRIFICE_BUFF_DELTA',
        'MandateSacrificeRules.ActiveAnnualDelta(')) {
        if (-not $mandateYearlyBody.Contains($needle)) {
            $failures.Add("Mandate annual delta must read the cached sacrifice modifier: missing '$needle'")
        }
    }
    foreach ($forbidden in @('SQLite', 'SacrificeRecordTableItem', '.buildings', '.getUnits()')) {
        if ($mandateYearlyBody.Contains($forbidden)) {
            $failures.Add("Mandate annual sacrifice modifier must remain O(1): found '$forbidden'")
        }
    }
}

Require-Present 'Mandate decisions mark sacrifice definitions explicitly' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'public MandateSacrificeLevel? SacrificeLevel;'
Require-Present 'Mandate window exposes the gamble sacrifice' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'aw_mandate_decision_sacrifice_gamble'
Require-Present 'Mandate window exposes the moderate sacrifice' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'aw_mandate_decision_sacrifice_moderate'
Require-Present 'Mandate window exposes the conservative sacrifice' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'aw_mandate_decision_sacrifice_conservative'
Require-Present 'Mandate ritual start uses unified eligibility' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'return MandateSacrificeService.CanExecute(pKingdom);'
Require-Present 'Mandate ritual progression spends real political points' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'MandateSacrificeRules.SpendForYear('
Require-Present 'Mandate ritual progression deducts political points' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'pKingdom.data.set(LineageKeys.POLICY_POINTS, politicalPoints - spend);'
Require-Present 'non-sacrifice Mandate projects retain independent progress' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'GetProgress(pKingdom) + EstimateYearlyGain(pKingdom)'
Require-Present 'Mandate AI asks the sacrifice service for one preferred choice' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'MandateSacrificeService.PreferredAiDecisionId(pKingdom)'
Require-Present 'Mandate ritual completion uses one settlement service' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'MandateSacrificeService.Execute(pKingdom, pDef.SacrificeLevel.Value)'
Require-Absent 'fixed Mandate ritual effect is removed' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'new MandateSacrificeEffects(8, 4, 3, 0)'
$productionCsFiles = Get-ChildItem -LiteralPath (Join-Path $root 'Code') `
    -Recurse -File -Filter '*.cs'
foreach ($productionFile in $productionCsFiles) {
    $productionText = [System.IO.File]::ReadAllText($productionFile.FullName)
    foreach ($obsoleteRitualId in @(
        'aw_decision_mandate_ritual',
        'aw_mandate_decision_ritual')) {
        if ($productionText.Contains($obsoleteRitualId)) {
            $relative = $productionFile.FullName.Substring($root.Length + 1)
            $failures.Add("obsolete ritual id '$obsoleteRitualId' remains in $relative")
        }
    }
}

foreach ($localeKey in @(
    'aw_mandate_decision_sacrifice_gamble,',
    'aw_mandate_decision_sacrifice_gamble_desc,',
    'aw_mandate_decision_sacrifice_moderate,',
    'aw_mandate_decision_sacrifice_moderate_desc,',
    'aw_mandate_decision_sacrifice_conservative,',
    'aw_mandate_decision_sacrifice_conservative_desc,',
    'aw_mandate_sacrifice_qualification,',
    'aw_mandate_sacrifice_qualified,',
    'aw_mandate_sacrifice_unqualified,',
    'aw_mandate_sacrifice_outcome_auspicious,',
    'aw_mandate_sacrifice_outcome_neutral,',
    'aw_mandate_sacrifice_outcome_ominous,',
    'aw_mandate_sacrifice_yearly_spend,',
    'aw_mandate_ritual_completeness,',
    'aw_mandate_sacrifice_annual_effect,')) {
    Require-Present "grand sacrifice locale $localeKey" `
        'Locales/aw3_mandate.csv' $localeKey
}
foreach ($historyKey in @(
    'aw_hist_event_mandate_sacrifice_auspicious',
    'aw_hist_event_mandate_sacrifice_neutral',
    'aw_hist_event_mandate_sacrifice_ominous',
    'aw_hist_sacrifice_performed',
    'aw_hist_sacrifice_qualification_mid',
    'aw_hist_sacrifice_result_mid',
    'aw_hist_sacrifice_mandate_mid')) {
    Require-Present "grand sacrifice history locale $historyKey" `
        'Code/core/lineage/HistoryLocalizationRules.cs' $historyKey
}
foreach ($eventRoute in @(
    'case "mandate_sacrifice_auspicious": return T("aw_hist_event_mandate_sacrifice_auspicious", pLanguage);',
    'case "mandate_sacrifice_neutral": return T("aw_hist_event_mandate_sacrifice_neutral", pLanguage);',
    'case "mandate_sacrifice_ominous": return T("aw_hist_event_mandate_sacrifice_ominous", pLanguage);')) {
    Require-Present "grand sacrifice event label route $eventRoute" `
        'Code/core/lineage/WarDisplayLabelRules.cs' $eventRoute
}
Require-Present 'Mandate yearly estimate accepts the selected definition' `
    'Code/core/lineage/MandateDecisionService.cs' `
    'EstimateYearlyGain(Kingdom pKingdom, MandateDecisionDef pDef)'
Require-Present 'Mandate decision list displays definition-specific spend' `
    'Code/ui/windows/MandateDecisionWindow.cs' `
    'MandateDecisionService.EstimateYearlyGain(pKingdom, pDef)'
Require-Present 'Mandate dynasty tooltip displays definition-specific spend' `
    'Code/ui/windows/MandateDynastyWindow.cs' `
    'MandateDecisionService.EstimateYearlyGain(pKingdom, pDef)'
Require-Present 'Mandate decision list shows sacrifice qualification' `
    'Code/ui/windows/MandateDecisionWindow.cs' `
    'aw_mandate_sacrifice_qualification'
Require-Present 'Mandate dynasty status shows ritual completeness' `
    'Code/ui/windows/MandateDynastyWindow.cs' `
    'aw_ritual_total'
Require-Present 'Mandate dynasty status shows the annual sacrifice effect' `
    'Code/ui/windows/MandateDynastyWindow.cs' `
    'LineageKeys.MANDATE_SACRIFICE_BUFF_DELTA'
Require-Absent 'grand sacrifice history must not be hard-coded English' `
    'Code/core/lineage/MandateSacrificeService.cs' ' grand sacrifice '

Require-Present 'cadet Shi keeps parent id' `
    'Code/core/lineage/LineageService.cs' 'seed.ParentShiId'
Require-Present 'cadet Shi keeps clan name' `
    'Code/core/lineage/LineageService.cs' 'seed.ClanName'
Require-Absent 'king branch cannot reroll before resolving current Shi' `
    'Code/core/lineage/LineageService.cs' '(string clanName, _) = GenerateShiName(pKing);'
Require-Present 'Shi parent index' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'idx_ShiBranch_parent'
Require-Present 'Shi state-name index' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'idx_ShiBranch_state_name'
Require-Present 'Shi parent column' `
    'Code/core/db/ShiBranchTableItem.cs' 'public long   parent_shi_id = -1;'
Require-Present 'visible Clan uses origin-city Shi display' `
    'Code/core/lineage/LineageService.cs' `
    'ShiBranchRules.BuildDisplayName(branch.origin_city_name, branch.clan_name)'

Require-Present 'ruler title facts are built at reign end' `
    'Code/core/lineage/RulerTitleFactService.cs' 'BuildAtReignEnd('
Require-Present 'personal ruler facts are archived before actor removal' `
    'Code/patch/AW_ActorDeathPatch.cs' 'RulerTitleFactService.ArchivePersonalSnapshot'
Require-Present 'title registry owns per-Shi uniqueness' `
    'Code/core/lineage/DynastyTitleRegistryService.cs' 'TryReserve('
Require-Present 'ruler titles have one transaction boundary' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'RulerTitleCommitResult Commit('
Require-Present 'title registry unique index' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'uq_DynastyTitleRegistry_value'
Require-Present 'reign snapshot stores Shi identity' `
    'Code/core/db/KingdomReignTableItem.cs' 'public long   shi_id = -1;'
Require-Present 'posthumous table stores temple title authority' `
    'Code/core/db/PosthumousTitleTableItem.cs' 'public string temple_name = "";'
Require-Absent 'reign snapshots cannot enumerate every kingdom actor' `
    'Code/core/lineage/ReignRecordWriter.cs' 'foreach (Actor unit in k.getUnits())'
Require-Absent 'posthumous title facts cannot enumerate every kingdom actor' `
    'Code/core/lineage/PosthumousTitleService.cs' 'foreach (Actor unit in k.getUnits())'
Require-Present 'posthumous selection consumes the authoritative reign facts' `
    'Code/core/lineage/PosthumousTitleService.cs' 'RulerTitleFactService.BuildAtReignEnd('
Require-Present 'posthumous selection reads per-Shi title reservations' `
    'Code/core/lineage/PosthumousTitleService.cs' 'DynastyTitleRegistryService.ReadUsed('
Require-Present 'posthumous selection resumes the latest registry cycle' `
    'Code/core/lineage/PosthumousTitleService.cs' 'DynastyTitleRegistryService.ReadLatestCycle('
Require-Present 'posthumous selection uses the atomic title commit' `
    'Code/core/lineage/PosthumousTitleService.cs' 'RulerTitleCommitService.Commit('
Require-Present 'posthumous transport has one factory' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'ForPosthumous('
Require-Present 'imperial reign end selects one temple name' `
    'Code/core/lineage/PosthumousTitleService.cs' 'TempleTitleRules.Select('
Require-Absent 'deposed emperors cannot lose temple-name eligibility' `
    'Code/core/lineage/PosthumousTitleService.cs' `
    'facts.HighestTitle >= (int)KingdomTitle.Emperor && !useMandateDeposedTitle'
Require-Present 'deposed emperors retain their public deposed appellation' `
    'Code/core/lineage/PosthumousTitleService.cs' `
    'FormerKingTraitRules.BuildMandateDeposedTitle('
Require-Present 'reign-end transport carries posthumous and temple choices together' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'ForReignEnd('
Require-Present 'title rows retain both posthumous and temple qualification facts' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'QualificationSnapshot(pDecision)'
Require-Present 'posthumous service uses the combined reign-end factory' `
    'Code/core/lineage/PosthumousTitleService.cs' 'RulerTitleDecision.ForReignEnd('
Require-Absent 'posthumous service cannot use the posthumous-only transport after temple integration' `
    'Code/core/lineage/PosthumousTitleService.cs' 'RulerTitleDecision.ForPosthumous('
Require-Absent 'posthumous selection cannot use System.Random' `
    'Code/core/lineage/PosthumousTitleService.cs' 'System.Random'
Require-Absent 'posthumous selection cannot use a random pool picker' `
    'Code/core/lineage/PosthumousTitleService.cs' 'PickFromPool('
Require-Absent 'posthumous selection cannot use a priority random picker' `
    'Code/core/lineage/PosthumousTitleService.cs' 'PickByPriority('
Require-Absent 'posthumous uniqueness cannot be scoped to Kingdom id' `
    'Code/core/lineage/PosthumousTitleService.cs' 'GetUsedTitleChars('
Require-Absent 'Mandate reign end cannot write a second title' `
    'Code/core/lineage/PosthumousTitleService.cs' 'MandateRulerTitleService.OnMandateReignEnded'
Require-Absent 'posthumous service cannot directly insert title rows' `
    'Code/core/lineage/PosthumousTitleService.cs' 'db.Insert(PosthumousTitleTableItem.GetTableName()'
Require-Absent 'posthumous display cannot truncate multi-character state names' `
    'Code/core/lineage/PosthumousTitleService.cs' 'FirstChar(pContext.KingdomName)'
Require-Present 'reign-end facts honor a Mandate acquired during the reign' `
    'Code/core/lineage/RulerTitleFactService.cs' 'facts.IsMandate = facts.MandatePeriodId >= 0;'
Require-Present 'Shi state persists former Mandate displacement' `
    'Code/core/db/ShiBranchTableItem.cs' 'public int    restored_pending;'
Require-Present 'Shi state persists autonomous restoration completion' `
    'Code/core/db/ShiBranchTableItem.cs' 'public int    self_restoration_completed;'
Require-Present 'Shi state persists Mandate restoration' `
    'Code/core/db/ShiBranchTableItem.cs' 'public int    regained_mandate;'
Require-Present 'Shi state records the actual autonomous refounder' `
    'Code/core/db/ShiBranchTableItem.cs' 'public long   self_restoration_actor_id = -1;'
Require-Present 'Shi state records the actual Mandate restorer' `
    'Code/core/db/ShiBranchTableItem.cs' 'public long   regained_mandate_actor_id = -1;'
Require-Present 'Mandate loss marks the displaced Shi' `
    'Code/core/lineage/MandateService.cs' 'RulerTitleRestorationStateService.MarkMandateLost(current);'
Require-Present 'Mandate-state destruction marks the known kingdom before lookup can disappear' `
    'Code/core/lineage/MandateService.cs' 'RulerTitleRestorationStateService.MarkMandateLost(pKingdom);'
Require-Present 'autonomous restoration marks the restored Shi' `
    'Code/core/lineage/AutonomousRestorationService.cs' `
    'RulerTitleRestorationStateService.MarkAutonomousRestorationCompleted(pRestored);'
Require-Present 'self-restored Mandate marks the Shi as renewed' `
    'Code/core/lineage/MandateService.cs' 'RulerTitleRestorationStateService.MarkMandateRegained(pKingdom);'
Require-Present 'ruler title facts read persistent restoration state' `
    'Code/core/lineage/RulerTitleFactService.cs' 'RulerTitleRestorationStateService.Read('
Require-Present 'capital moves have a dedicated historical event' `
    'Code/core/lineage/ChronicleKeys.cs' 'public const string CAPITAL_MOVED = "capital_moved";'
Require-Present 'capital decisions record the dedicated move event' `
    'Code/core/policy/KingdomPolicyService.cs' 'KingdomEvent.CAPITAL_MOVED'
Require-Present 'ruler title facts count capital moves by reign interval' `
    'Code/core/lineage/RulerTitleFactService.cs' 'facts.CapitalMoves = CountCapitalMoveEvents('
Require-Present 'ruler title facts preserve collateral succession' `
    'Code/core/lineage/RulerTitleFactService.cs' 'facts.CollateralSuccession = restoredShiId >= 0;'
Require-Present 'ruler title facts preserve legal-core restoration' `
    'Code/core/lineage/RulerTitleFactService.cs' `
    'facts.RestoredLegalCore = restoration.SelfRestorationActorId == facts.ActorId;'
Require-Present 'dynasty founders qualify even in an older Kingdom identity' `
    'Code/core/lineage/RulerTitleFactService.cs' 'IsDynastyFounder('
Require-Present 'ruler title facts count only wars initiated by the ruler state' `
    'Code/core/lineage/RulerTitleFactService.cs' `
    'facts.OffensiveWars = WarRecordWriter.GetOffensiveWarCount('
Require-Present 'war title facts query the attacking kingdom directly' `
    'Code/core/lineage/WarRecordWriter.cs' 'GetOffensiveWarCount('
Require-Present 'war records index attacking kingdoms by start time' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'idx_WarRecord_attacker_start'
Require-Present 'reign war totals filter the requested kingdom in SQL' `
    'Code/core/lineage/WarRecordWriter.cs' `
    '(ATTACKER_KINGDOM_ID=@kingdom OR DEFENDER_KINGDOM_ID=@kingdom)'
Require-Present 'first imperial transition attempts paternal ancestor awards' `
    'Code/core/lineage/KingdomTitleService.cs' `
    'RetrospectiveTitleService.TryAwardFirstImperialAncestors('
Require-Absent 'imperial-transition idempotency cannot exclude the same restored emperor' `
    'Code/core/lineage/RetrospectiveTitleService.cs' 'KING_ACTOR_ID<>@actor'
Require-Present 'retrospective ancestor titles have one factory' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'ForRetrospective('
Require-Present 'retrospective title rows are idempotent per Shi and actor' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'uq_PosthumousTitle_retrospective_actor'
Require-Present 'retrospective ancestry resolves the father relation' `
    'Code/core/lineage/RetrospectiveTitleService.cs' '"father"'
Require-Present 'retrospective ancestry resolves the paternal grandfather relation' `
    'Code/core/lineage/RetrospectiveTitleService.cs' '"paternal_grandfather"'
Require-Present 'retrospective ancestry traces through a living father to the grandfather' `
    'Code/core/lineage/RetrospectiveTitleService.cs' 'ResolveMaleParent('
Require-Absent 'retrospective ancestry cannot stop parent resolution at living fathers' `
    'Code/core/lineage/RetrospectiveTitleService.cs' 'ResolveDeceasedMaleParent('
Require-Present 'retrospective awards verify live WorldBox state before honoring an archive row' `
    'Code/core/lineage/RetrospectiveTitleService.cs' 'IsLivingActor(pActorId)'
Require-Absent 'retrospective ancestry cannot fabricate a reign' `
    'Code/core/lineage/RetrospectiveTitleService.cs' 'ReignRecordWriter.OpenReign'
Require-Absent 'Mandate history queries cannot repair or write missing temple titles' `
    'Code/core/lineage/MandateHistoryQuery.cs' 'RepairMissingTempleTitles('
Require-Absent 'legacy Mandate title service cannot retain a second title writer' `
    'Code/core/lineage/MandateRulerTitleService.cs' 'MandateRulerTitleTableItem.GetTableName()'
Require-Present 'state names use one bounded stable selector' `
    'Code/core/lineage/StateNameService.cs' 'StateNameRules.SelectFirstAvailable('
Require-Present 'state-name binding uses an empty-row compare and set' `
    'Code/core/lineage/StateNameService.cs' `
    "IFNULL(STATE_NAME,'')=''"
Require-Present 'state-name binding persists the random source' `
    'Code/core/lineage/StateNameService.cs' 'STATE_NAME_SOURCE=@source'
Require-Present 'state-name projection occurs only after a committed result' `
    'Code/core/lineage/StateNameService.cs' 'ProjectCommittedStateName('
Require-Present 'committed state-name projection has one bounded retry' `
    'Code/core/lineage/StateNameService.cs' 'RetryCommittedProjection('
Require-Absent 'state-name service cannot call the legacy random picker' `
    'Code/core/lineage/StateNameService.cs' 'XiaPreQinKingdomNameRules.Pick('
Require-Absent 'state-name service cannot allocate process random state' `
    'Code/core/lineage/StateNameService.cs' 'System.Random'
Require-Absent 'random state names have no political-point cost' `
    'Code/core/lineage/StateNameService.cs' 'POLICY_POINTS'
Require-Present 'king-change history binds the initial state name before snapshots' `
    'Code/core/lineage/ChronicleEvents.cs' 'EnsureInitialStateNameForRuler('
Require-Present 'historical rulers cannot invent a new state name during dynastic replacement' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'StateNameService.GetBoundStateName(pShiId)'
Require-Present 'state-name persistence resolves historical, bound, and current names before random binding' `
    'Code/core/lineage/StateNameService.cs' `
    'StateNameRules.ResolveInitialBoundName('
Require-Present 'first-ruler state-name binding records an existing kingdom-name source' `
    'Code/core/lineage/StateNameService.cs' '"existing_kingdom"'
Require-Absent 'historical figures cannot temporarily rename kingdoms outside state-name authority' `
    'Code/content/figures/HistoricalFigureService.cs' `
    'KingdomRenameSyncService.Suppress(() => pKingdom.setName('
Require-Present 'state-name binding covers fully Xiaized institutional kingdoms' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'XiaizationService.UsesXiaizedInstitutionSystem(pKingdom)'
Require-Present 'dynasty continuity compares authoritative Shi ids' `
    'Code/core/lineage/DynastyRecordWriter.cs' 'StateNameRules.IsSameShiContinuity('
Require-Present 'dynasty snapshots persist the bound state name' `
    'Code/core/lineage/DynastyRecordWriter.cs' 'ColumnVal.Create("STATE_NAME"'
Require-Present 'active state-name lookup has a directed dynasty index' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'idx_DynastyPeriod_shi_active_state'
Require-Present 'active state-name index starts with the active-row predicate' `
    'Code/core/db/LineageArchiveIndexRules.cs' '"END_TIME, SHI_ID, STATE_NAME"'
Require-Present 'active kingdom names have a directed archive index' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'idx_KingdomArchive_alive_name'
Require-Present 'state-name exclusion includes live archived kingdoms' `
    'Code/core/lineage/StateNameService.cs' 'KingdomArchiveTableItem.GetTableName()'
Require-Present 'kingdom archive color snapshots resolve directly from the current color id' `
    'Code/core/lineage/KingdomArchiveWriter.cs' `
    'string colorText = HistoryColors.FromKingdom(pKingdom);'
Require-Present 'kingdom visual changes immediately synchronize the archive snapshot' `
    'Code/patch/AW_KingdomColorPatch.cs' `
    '[HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateColor))]'
Require-Present 'Xia kingdom renaming delegates a valid Shi to state-name authority' `
    'Code/content/XiaNamingRepair.cs' 'StateNameService.EnsureBoundStateName('
Require-Present 'Xia kingdom renaming projects only the committed Shi state name' `
    'Code/content/XiaNamingRepair.cs' 'StateNameService.ProjectCommittedStateName('
Require-Present 'restoration requests carry the bound Shi state name' `
    'Code/core/lineage/KingdomIdentityContinuityService.cs' 'public string state_name = "";'
Require-Present 'restoration identity resolves names from the original Shi' `
    'Code/core/lineage/KingdomIdentityContinuityService.cs' `
    'StateNameService.GetBoundStateName(pRequest.shi_id)'
Require-Present 'reign snapshots read state names through the Shi authority' `
    'Code/core/lineage/ReignRecordWriter.cs' `
    'StateNameService.GetBoundOrCurrentName(pKingdom, shiId)'
Require-Present 'autonomous restoration carries its original bound state name' `
    'Code/core/lineage/AutonomousRestorationService.cs' 'state_name = claim.shiId'

Require-Present 'era periods persist Shi identity' 'Code/core/db/EraPeriodTableItem.cs' 'public long shi_id = -1;'
Require-Present 'era periods persist reign identity' 'Code/core/db/EraPeriodTableItem.cs' 'public long reign_id = -1;'
Require-Present 'era periods persist idempotent source events' 'Code/core/db/EraPeriodTableItem.cs' 'public string source_event_id = "";'
Require-Present 'era events have a unique directed index' 'Code/core/db/LineageArchiveIndexRules.cs' 'uq_EraPeriod_event'
Require-Present 'era names use the exact weighted historical pool' 'Code/core/lineage/EraNameRules.cs' 'public static readonly IReadOnlyList<string> HistoricalSlots'
Require-Absent 'era generation cannot use process random state' 'Code/core/lineage/YearNameService.cs' 'System.Random'
Require-Absent 'era generation cannot retain the legacy random field' 'Code/core/lineage/YearNameService.cs' 'private static readonly System.Random Rng'
Require-Absent 'era service cannot retain the legacy change API' 'Code/core/lineage/YearNameService.cs' 'ChangeYearName('
Require-Present 'era persistence uses one atomic transaction helper' 'Code/core/lineage/EraRecordWriter.cs' 'EraAtomicPersistence.TryCommit(DB, pRequest)'
Require-Present 'era transaction closes and opens eras together' 'Code/core/lineage/EraAtomicPersistence.cs' 'ClosePreviousEra(pDb, transaction, pRequest);'
Require-Present 'era transaction reserves the Shi name before commit' 'Code/core/lineage/EraAtomicPersistence.cs' 'InsertRegistry(pDb, transaction, pRequest);'
Require-Present 'era transaction writes reserved kingdom history before commit' 'Code/core/lineage/EraAtomicPersistence.cs' 'InsertKingdomHistory(pDb, transaction, pKingdomEventId,'
Require-Present 'voluntary era changes reserve thirty political points' 'Code/core/lineage/YearNameService.cs' 'public const int VoluntaryChangeCost = 30;'
Require-Present 'political points commit only through one reservation service' 'Code/core/lineage/YearNameService.cs' 'PoliticalPointReservationService.Commit(reservationId)'
Require-Present 'emperor accession creates an era after opening the reign' 'Code/core/lineage/ChronicleEvents.cs' 'YearNameService.TryStartAccessionEra(pKingdom, pNewKing);'
Require-Present 'first imperial title transition creates a proclamation era' `
    'Code/core/lineage/KingdomTitleService.cs' `
    'YearNameService.TryStartImperialProclamationEra('
Require-Absent 'first imperial title transition cannot reopen accession' `
    'Code/core/lineage/KingdomTitleService.cs' 'TryStartAccessionEra('
Require-Present 'AI era changes consume event markers only annually' 'Code/core/policy/KingdomPolicyService.cs' 'EraChangeTriggerService.TryProcessAnnualAi(pKingdom);'
Require-Present 'vassal chronology resolves the root suzerain once' 'Code/core/lineage/YearNameService.cs' 'VassalService.GetRootSuzerain(pKingdom)'
Require-Present 'ordinary ruler chronology persists one reign-start hot key' `
    'Code/core/lineage/LineageKeys.cs' 'KINGDOM_REIGN_START'
Require-Present 'opening a reign publishes its start to kingdom data' `
    'Code/core/lineage/ReignRecordWriter.cs' `
    'pKingdom.data.set(LineageKeys.KINGDOM_REIGN_START'
Require-Present 'display chronology falls back from formal era to local regnal chronology' `
    'Code/core/lineage/YearNameService.cs' 'RegnalChronologyRules.SelectDisplay('
Require-Present 'history prefixes use the shared Ganzhi formatter' `
    'Code/core/lineage/HistoryWriter.cs' `
    'GanzhiChronologyRules.FormatPrefix(era, date, raw[2])'
Require-Absent 'history prefixes cannot retain the pre-Ganzhi era format' `
    'Code/core/lineage/HistoryWriter.cs' `
    'era + "(" + date + ")"'
Require-Absent 'Ganzhi chronology cannot perform database work' `
    'Code/core/lineage/GanzhiChronologyRules.cs' 'SQLite'
Require-Absent 'Ganzhi chronology cannot depend on yearly updates' `
    'Code/core/lineage/GanzhiChronologyRules.cs' 'UpdateAge'
$yearNameSource = Read-Source 'Code/core/lineage/YearNameService.cs'
$localRegnalStart = $yearNameSource.IndexOf(
    'private static string BuildLocalRegnalChronology(',
    [System.StringComparison]::Ordinal)
if ($localRegnalStart -lt 0) {
    $failures.Add('ordinary regnal chronology has no bounded local-data builder')
}
else {
    $localRegnalEnd = $yearNameSource.IndexOf(
        'public static bool RetryCommittedProjection(',
        $localRegnalStart + 20, [System.StringComparison]::Ordinal)
    if ($localRegnalEnd -lt 0) { $localRegnalEnd = $yearNameSource.Length }
    $localRegnalBody = $yearNameSource.Substring(
        $localRegnalStart, $localRegnalEnd - $localRegnalStart)
    if ($localRegnalBody.Contains('SQLiteCommand') -or
        $localRegnalBody.Contains('World.world.units') -or
        $localRegnalBody.Contains('foreach')) {
        $failures.Add('ordinary regnal chronology hot path must use only local kingdom and actor data')
    }
}
Require-Absent 'Mandate decisions cannot retain a second era-name action' 'Code/core/lineage/MandateDecisionService.cs' 'aw_mandate_decision_year_name'

Require-Present 'city economy reads one centralization snapshot' `
    'Code/core/policy/CityEconomyService.cs' `
    'CentralizationEffects effects = CentralizationService.ReadSnapshot(pKingdom).effects;'
Require-Present 'city economy exposes cached tax contribution' `
    'Code/core/policy/CityEconomyService.cs' 'public static float GetTaxContribution(Kingdom pKingdom)'
Require-Present 'city economy exposes cached foreign border state' `
    'Code/core/policy/CityEconomyService.cs' 'public static bool HasForeignLandBorder(Kingdom pKingdom)'
Require-Present 'city economy SQL fallback batches all contribution sums' `
    'Code/core/policy/CityEconomyService.cs' `
    'SELECT SUM(POLICY_POINTS),SUM(TECH_POINTS),SUM(TAX_VALUE) FROM '
Require-Absent 'cached city tax lookup cannot scan actors' `
    'Code/core/policy/CityEconomyService.cs' 'getUnits()'

$cityEconomyService = Read-Source 'Code/core/policy/CityEconomyService.cs'
$centralizationSnapshotIndex = $cityEconomyService.IndexOf(
    'CentralizationEffects effects = CentralizationService.ReadSnapshot(pKingdom).effects;',
    [System.StringComparison]::Ordinal)
$cityEconomyLoopIndex = $cityEconomyService.IndexOf('foreach (City city in cities)',
    [System.StringComparison]::Ordinal)
if ($centralizationSnapshotIndex -lt 0 -or $cityEconomyLoopIndex -lt 0 -or
    $centralizationSnapshotIndex -gt $cityEconomyLoopIndex) {
    $failures.Add('city economy must read centralization once before its existing city loop')
}

Require-Present 'vassal relations maintain a direct count' `
    'Code/core/lineage/VassalService.cs' 'LineageKeys.VASSAL_DIRECT_COUNT'
Require-Present 'new direct vassal increments the maintained count once' `
    'Code/core/lineage/VassalService.cs' 'AdjustDirectVassalCount(pSuzerain, 1);'
Require-Present 'closing an active relation decrements the maintained count once' `
    'Code/core/lineage/VassalService.cs' 'AdjustDirectVassalCount(FindKingdom(suzerainId), -1);'
Require-Present 'relation close verifies active state before count mutation' `
    'Code/core/lineage/VassalService.cs' `
    'WHERE RELATION_ID=@r AND ACTIVE=1 AND END_TIME<0 LIMIT 1'
Require-Present 'vassal tribute has one annual settlement entry' `
    'Code/core/lineage/VassalService.cs' 'public static void SettleAnnualTribute(Kingdom pSuzerain)'
Require-Present 'direct vassal relations use one batch query' `
    'Code/core/lineage/VassalService.cs' 'WHERE SUZERAIN_ID=@s AND ACTIVE=1 AND END_TIME<0'
Require-Present 'tribute consumes only the in-memory city tax cache' `
    'Code/core/lineage/VassalService.cs' `
    'CityEconomyService.TryGetLatestCachedTaxContribution(vassal, out float annualTax);'
Require-Present 'central-power tribute forecast applies live balances and court reserve' `
    'Code/core/lineage/VassalService.cs' `
    'float political = VassalFiscalRules.PoliticalTribute('
Require-Absent 'central-power tribute forecast cannot ignore current balances' `
    'Code/core/lineage/VassalService.cs' `
    'float political = VassalFiscalRules.ForecastPoliticalTribute('
Require-Absent 'tribute cannot trigger per-vassal city tax SQL fallback' `
    'Code/core/lineage/VassalService.cs' 'CityEconomyService.GetTaxContribution(vassal)'
Require-Present 'tribute is benchmarked immediately after city economy' `
    'Code/core/policy/KingdomAnnualWorkService.cs' 'VassalService.SettleAnnualTribute(pKingdom)'

$vassalService = Read-Source 'Code/core/lineage/VassalService.cs'
$settleStart = $vassalService.IndexOf(
    'public static void SettleAnnualTribute(Kingdom pSuzerain)',
    [System.StringComparison]::Ordinal)
$settleEnd = if ($settleStart -ge 0) {
    $vassalService.IndexOf('public static ', $settleStart + 20,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($settleStart -lt 0 -or $settleEnd -lt 0 -or
    $vassalService.Substring($settleStart, $settleEnd - $settleStart).Contains('GetVassals(')) {
    $failures.Add('annual tribute must fast-reject and batch relations without GetVassals')
}

$kingdomAnnualWork = Read-Source 'Code/core/policy/KingdomAnnualWorkService.cs'
$economyCallIndex = $kingdomAnnualWork.IndexOf('CityEconomyService.OnKingdomYear(pKingdom)',
    [System.StringComparison]::Ordinal)
$tributeCallIndex = $kingdomAnnualWork.IndexOf('VassalService.SettleAnnualTribute(pKingdom)',
    [System.StringComparison]::Ordinal)
$heavyIndex = $kingdomAnnualWork.IndexOf('if (runHeavy)', [System.StringComparison]::Ordinal)
if ($economyCallIndex -lt 0 -or $tributeCallIndex -lt 0 -or
    $tributeCallIndex -lt $economyCallIndex -or
    ($heavyIndex -ge 0 -and $tributeCallIndex -gt $heavyIndex)) {
    $failures.Add('annual tribute must run after city economy and before the heavy schedule')
}

Require-Present 'vassal obligation decisions use the war data key' `
    'Code/core/lineage/VassalService.cs' 'LineageKeys.VASSAL_OBLIGATION_DECISIONS'
Require-Present 'war start batches all active vassal relations once' `
    'Code/core/lineage/VassalService.cs' `
    'WHERE ACTIVE=1 AND END_TIME<0 ORDER BY SUZERAIN_ID,START_TIME'
Require-Present 'yearly vassal repair cannot create new obligation rolls' `
    'Code/core/lineage/VassalService.cs' 'pAllowNewDecisions: false'
Require-Absent 'legacy unconditional vassal network join is removed' `
    'Code/core/lineage/VassalService.cs' 'JoinNetwork('
Require-Present 'vassal internal war reads root centralization' `
    'Code/core/lineage/WarDecisionService.cs' `
    'CentralizationService.ReadSnapshot(attackerRoot)'

$obligationJoinStart = $vassalService.IndexOf(
    'private static void JoinObligatedNetwork(', [System.StringComparison]::Ordinal)
$obligationJoinEnd = if ($obligationJoinStart -ge 0) {
    $vassalService.IndexOf('private static void RepairObligatedNetwork(',
        $obligationJoinStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($obligationJoinStart -lt 0 -or $obligationJoinEnd -lt 0) {
    $failures.Add('obligation breadth-first join method must exist')
} else {
    $obligationJoinBody = $vassalService.Substring(
        $obligationJoinStart, $obligationJoinEnd - $obligationJoinStart)
    if ($obligationJoinBody.Contains('GetVassals(') -or
        $obligationJoinBody.Contains('SQLiteCommand')) {
        $failures.Add('obligation breadth-first join cannot query relations per vassal')
    }
}

Require-Present 'Mandate border defense delegates existing armies' `
    'Code/core/lineage/MandateBorderDefenseService.cs' `
    'BorderArmyReanchorService.ReanchorExistingArmies('
Require-Present 'centralization border defense delegates existing armies' `
    'Code/core/lineage/CentralizationBorderDeploymentService.cs' `
    'BorderArmyReanchorService.ReanchorExistingArmies('
Require-Absent 'ordinary policy yearly flow cannot reform centralization' `
    'Code/core/policy/KingdomPolicyService.cs' 'CentralizationService.OnKingdomYear(pKingdom);'
Require-Present 'central power reform submits a Mandate decision command' `
    'Code/ui/windows/CentralPowerWindow.cs' `
    'AW3CommandRequest.StartMandateDecision('
Require-Present 'authoritative policy handler queues a Mandate decision' `
    'Code/core/multiplayer/commands/AW3PolicyCommandHandler.cs' `
    'MandateDecisionService.ForceStart('
Require-Absent 'AI centralization cannot scan actors' `
    'Code/core/lineage/CentralizationService.cs' 'getUnits()'
Require-Absent 'central deployment cannot appoint guards' `
    'Code/core/lineage/CentralizationBorderDeploymentService.cs' 'AppointBorderGuards'
Require-Absent 'central deployment cannot build walls' `
    'Code/core/lineage/CentralizationBorderDeploymentService.cs' 'BuildBorderWalls'
Require-Absent 'central deployment cannot build towers' `
    'Code/core/lineage/CentralizationBorderDeploymentService.cs' 'BuildBorderTowers'
Require-Absent 'central deployment cannot create armies' `
    'Code/core/lineage/CentralizationBorderDeploymentService.cs' 'EnsureArmy('

$warPatch = Read-Source 'Code/patch/AW_WarPatch.cs'
$vassalWarStartIndex = $warPatch.IndexOf('VassalService.OnWarStarted(__result);',
    [System.StringComparison]::Ordinal)
$centralDeployIndex = $warPatch.IndexOf(
    'CentralizationBorderDeploymentService.OnWarStarted(__result);',
    [System.StringComparison]::Ordinal)
$mandateWarStartIndex = $warPatch.IndexOf('MandateService.OnWarStarted(__result);',
    [System.StringComparison]::Ordinal)
if ($vassalWarStartIndex -lt 0 -or $centralDeployIndex -lt 0 -or
    $mandateWarStartIndex -lt 0 -or $centralDeployIndex -lt $vassalWarStartIndex -or
    $centralDeployIndex -gt $mandateWarStartIndex) {
    $failures.Add('central border deployment must run after vassal participation and before Mandate defense')
}

Require-Present 'central power window has a stable ID' `
    'Code/ui/AW_LineageWindowIds.cs' 'public const string CENTRAL_POWER = "aw_central_power";'
Require-Present 'central power window uses lazy create lifecycle' `
    'Code/ui/windows/CentralPowerWindow.cs' 'CreateAndInit(AW_LineageWindowIds.CENTRAL_POWER)'
Require-Present 'central power window uses shared wide chrome' `
    'Code/ui/windows/CentralPowerWindow.cs' 'WideWindowChrome.Attach('
Require-Present 'court window retains shared wide chrome' `
    'Code/ui/windows/CourtWindow.cs' 'WideWindowChrome.Attach('
Require-Absent 'ordinary kingdom details cannot expose central power' `
    'Code/ui/windows/KingdomWindowAddition.cs' 'CentralPowerStatus'
Require-Present 'ordinary kingdom details retain a stable fourth row' `
    'Code/ui/windows/KingdomWindowAddition.cs' 'ReservedStatus'
Require-Present 'fourth kingdom row opens diplomacy' `
    'Code/ui/windows/KingdomWindowAddition.cs' 'ConfigureDiplomacyButton('
Require-Present 'diplomacy row is interactable' `
    'Code/ui/windows/KingdomWindowAddition.cs' 'button.interactable = true;'
Require-Present 'diplomacy row title is localized' `
    'Locales/aw3_diplomacy.csv' 'aw_diplomacy_window_title,'
Require-Present 'diplomacy row description is localized' `
    'Locales/aw3_diplomacy.csv' 'aw_diplomacy_window_desc,'
Require-Present 'Mandate window exposes central power action' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'filter_key = "central_power"'
Require-Present 'Mandate central power action opens current Mandate realm' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'CentralPowerWindow.Open(kingdom.id);'
Require-Present 'centralization participation checks current Mandate realm' `
    'Code/core/lineage/CentralizationService.cs' 'IsCurrentMandateKingdom('
Require-Absent 'chaos centralization downgrade cannot scan every kingdom' `
    'Code/core/lineage/CentralizationService.cs' 'foreach (Kingdom kingdom in World.world.kingdoms)'
Require-Present 'central power window gates command completion refreshes' `
    'Code/ui/windows/CentralPowerWindow.cs' `
    'if (!_commandRefreshRequested) return;'
Require-Absent 'central power window cannot use SQLite' `
    'Code/ui/windows/CentralPowerWindow.cs' 'SQLite'
Require-Absent 'central power window cannot query relation rows directly' `
    'Code/ui/windows/CentralPowerWindow.cs' 'ReadActiveRelationDetails'

Require-Present 'player mandate grant has one service transaction entry' `
    'Code/core/lineage/MandateService.cs' 'public static bool TryGrantMandateByPlayer('
Require-Present 'player mandate grant reuses declaration transaction' `
    'Code/core/lineage/MandateService.cs' `
    'TryDeclareMandate(pTarget, "player_grant", "player_grant", "player_grant")'
Require-Present 'Mandate replacement closes an active record even when old runtime kingdom is missing' `
    'Code/core/lineage/MandateService.cs' `
    'previousReport.active && previousReport.kingdom_id != pKingdom.id'
Require-Present 'player mandate grant has a dedicated history event' `
    'Code/core/lineage/MandateStartRecordRules.cs' 'mandate_declared_player_grant'
Require-Present 'lineage tab exposes player mandate grant power' `
    'Code/ui/AW_LineageTab.cs' 'GodPowerLibrary.GRANT_MANDATE'
Require-Present 'GodPower delegates player mandate grant to service' `
    'Code/content/GodPowerLibrary.cs' 'MandateService.TryGrantMandateByPlayer('
Require-Absent 'GodPower cannot write Mandate periods directly' `
    'Code/content/GodPowerLibrary.cs' 'MandatePeriodTableItem'
Require-Absent 'GodPower cannot insert Mandate database rows directly' `
    'Code/content/GodPowerLibrary.cs' 'DB.Insert('

$yearNameService = Read-Source 'Code/core/lineage/YearNameService.cs'
$eraCommitIndex = $yearNameService.IndexOf('EraRecordWriter.TryCommit(request)',
    [System.StringComparison]::Ordinal)
$eraPointCommitIndex = $yearNameService.IndexOf(
    'PoliticalPointReservationService.Commit(reservationId)',
    [System.StringComparison]::Ordinal)
$eraProjectionIndex = $yearNameService.IndexOf(
    'ProjectCommittedEra(pKingdom, committed.EraName, startTime)',
    [System.StringComparison]::Ordinal)
if ($eraCommitIndex -lt 0 -or $eraPointCommitIndex -lt 0 -or
    $eraProjectionIndex -lt 0 -or $eraCommitIndex -gt $eraPointCommitIndex -or
    $eraCommitIndex -gt $eraProjectionIndex) {
    $failures.Add('era database commit must precede political-point deduction and object projection')
}

foreach ($productionFile in $productionCsFiles) {
    $relative = $productionFile.FullName.Substring($root.Length + 1).Replace('\', '/')
    if ($relative -eq 'Code/core/lineage/YearNameService.cs') { continue }
    $productionText = [System.IO.File]::ReadAllText($productionFile.FullName)
    if ($productionText.Contains('.set(LineageKeys.KINGDOM_YEAR_NAME')) {
        $failures.Add("direct era-name projection remains outside YearNameService: $relative")
    }
    if ($productionText.Contains('.set(LineageKeys.KINGDOM_YEAR_START')) {
        $failures.Add("direct era-start projection remains outside YearNameService: $relative")
    }
}

Require-Present 'official career state has one authoritative actor row' `
    'Code/core/db/OfficialCareerStateTableItem.cs' '[TableDef("OfficialCareerState")]'
Require-Present 'official career state actor id is the primary key' `
    'Code/core/db/OfficialCareerStateTableItem.cs' `
    '[TableItemDef(pIsPrimary: true)] public long actor_id;'
Require-Present 'official rank has a hot actor-data mirror' `
    'Code/core/lineage/LineageKeys.cs' 'public const string OFFICER_RANK = "aw_officer_rank";'
Require-Present 'official track has a hot actor-data mirror' `
    'Code/core/lineage/LineageKeys.cs' 'public const string OFFICER_TRACK = "aw_officer_track";'
Require-Present 'committed appointments initialize official career state' `
    'Code/core/court/CourtService.cs' 'OfficialCareerStateService.ProjectAppointment('
Require-Present 'local standing preserves principal-rank entry anchors' `
    'Code/core/court/OfficialCareerStateService.cs' `
    'OfficialCareerRankRules.ApplyEntryRankBonus(rank,'
Require-Present 'dismissal clears only the current official projection' `
    'Code/core/court/CourtService.cs' 'OfficialCareerStateService.ClearCurrentOffice('
Require-Present 'official career states are read once by kingdom' `
    'Code/core/court/OfficialCareerStateService.cs' 'WHERE KINGDOM_ID=@kingdom'
Require-Absent 'candidate score cannot query official career state database' `
    'Code/core/court/CourtService.cs' 'OfficialCareerStateService.LoadState('
Require-Present 'official careers run once after city economy' `
    'Code/core/policy/KingdomAnnualWorkService.cs' 'OfficialCareerStateService.OnKingdomYear(pKingdom)'
Require-Present 'official yearly work has a kingdom idempotency key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string OFFICIAL_CAREER_LAST_YEAR = "aw_official_career_last_year";'
Require-Present 'official yearly work rejects a duplicate year' `
    'Code/core/court/OfficialCareerStateService.cs' 'if (lastYear == year) return;'
Require-Present 'official yearly work batches city economy by kingdom' `
    'Code/core/court/OfficialCareerStateService.cs' `
    'FROM " + CityEconomyStateTableItem.GetTableName() + " WHERE KINGDOM_ID=@kingdom"'
Require-Present 'official yearly mutations share one transaction' `
    'Code/core/court/OfficialCareerStateService.cs' 'CommitAnnualMutations('
Require-Present 'official evaluations enter personal biographies' `
    'Code/core/court/OfficialCareerStateService.cs' 'HistoryWriter.RecordPerson('
Require-Absent 'official evaluations cannot spam kingdom history' `
    'Code/core/court/OfficialCareerStateService.cs' 'HistoryWriter.RecordKingdom('
Require-Present 'official career yearly work has a benchmark entry' `
    'Code/core/policy/UpdateAgeBenchmarkRules.cs' 'KingdomOfficialCareer'
Require-Present 'merit reward DTO carries the integer career merit cap' `
    'Code/core/court/CourtMeritRewardCandidateQuery.cs' `
    'public int CivilMeritCap { get; }'
Require-Present 'merit reward SQL detaches the integer career merit cap' `
    'Code/core/court/CourtMeritRewardCandidateQuery.cs' `
    'IFNULL(career.MERIT_CAP,0) AS CIVIL_MERIT_CAP'
Require-Present 'merit reward facts consume the detached career merit cap' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'pCandidate.CivilMeritCap'
Require-Absent 'merit reward cannot restore live career merit hot reads' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'out int civilCap, 0);'
Require-Present 'general projection repair is a separate reward lane' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'RepairIndependentGeneralProjections(pKingdom);'
Require-Present 'general projection repair scans authoritative live actors' `
    'Code/core/lineage/GeneralService.cs' `
    'List<Actor> units = pKingdom.units;'
Require-Absent 'general projection repair cannot discover through the DB read model' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'GetActiveGeneralsForReadModel('
Require-Present 'general projection repair detects missing and stale rows' `
    'Code/core/lineage/GeneralService.cs' `
    'NeedsGeneralProjectionRepair('
Require-Present 'general projection repair uses tested bounded orchestration' `
    'Code/core/lineage/GeneralService.cs' `
    'CourtRepairOrchestration.ScanBounded('
Require-Present 'candidate mismatch repair uses tested independent orchestration' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'CourtRepairOrchestration.TryRepairIndependent('
Require-Present 'kingdom destruction clears both court repair cursors' `
    'Code/core/lineage/GeneralService.cs' `
    'CourtRepairOrchestration.ClearKingdomCursors('
Require-Present 'archive projection repair persists a fair cursor' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'ArchiveRepairCursorByKingdom.Set(pKingdom.id,'
Require-Present 'archive projection repair advances through every inspected id' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'repairs[i].ActorId);'
Require-Present 'merit reward service resolves cooldown state after grant attempts' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'CourtMeritRewardRules.ResolveCooldownCommit('
Require-Present 'merit reward service writes the projected kingdom cooldown' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'cooldown.KingdomLastRewardYear'
Require-Present 'merit reward service writes the projected actor cooldown' `
    'Code/core/court/CourtMeritRewardService.cs' `
    'cooldown.ActorLastRewardYear'
$meritRewardSource = Read-Source 'Code/core/court/CourtMeritRewardService.cs'
$meritGrantIndex = $meritRewardSource.IndexOf('NobleRankService.TryGrant(')
$meritProjectionIndex = $meritRewardSource.IndexOf(
    'CourtMeritRewardRules.ResolveCooldownCommit(')
$meritKingdomWriteIndex = $meritRewardSource.IndexOf(
    'cooldown.KingdomLastRewardYear')
$meritActorWriteIndex = $meritRewardSource.IndexOf(
    'cooldown.ActorLastRewardYear')
if ($meritGrantIndex -lt 0 -or $meritProjectionIndex -le $meritGrantIndex -or
    $meritKingdomWriteIndex -le $meritProjectionIndex -or
    $meritActorWriteIndex -le $meritProjectionIndex) {
    $failures.Add('merit reward cooldowns must be projected and written only after the grant attempt')
}
Require-Present 'court influence accepts a hot official rank' `
    'Code/core/court/CourtInfluenceRules.cs' 'float merit, int rank'
Require-Present 'court direction scales central officials by rank' `
    'Code/core/court/CourtDirectionService.cs' 'OfficialCareerRankRules.InfluenceMultiplier('
Require-Present 'candidate scoring reads official rank from actor data' `
    'Code/core/court/CourtService.cs' 'OfficialCareerStateService.ReadRankFast(pActor)'
Require-Present 'candidate scoring matches rank to office grade' `
    'Code/core/court/CourtService.cs' 'OfficialCareerRankRules.OfficeRankMatchScore('
Require-Absent 'school preference cannot exclude an otherwise valid official' `
    'Code/core/court/CourtService.cs' 'CourtManualAppointmentRules.IsSchoolEligible('
Require-Present 'court nodes keep layout rank separate from official rank' `
    'Code/core/court/CourtPyramidRules.cs' 'public int OfficialRank;'
Require-Present 'court nodes expose official career merit' `
    'Code/core/court/CourtPyramidRules.cs' 'public float OfficialMerit;'
Require-Present 'court read model batches career state once' `
    'Code/core/court/CourtReadModelService.cs' 'OfficialCareerStateService.LoadKingdomStates(pKingdom.id)'
Require-Present 'court cards resolve named Tang career ranks' `
    'Code/ui/items/CourtActorNodeView.cs' `
    'OfficialCareerRankRules.NamedRankKey(pTrack, pRank)'
Require-Present 'court cards share compact and full career title composition' `
    'Code/ui/items/CourtActorNodeView.cs' `
    'OfficialCareerRankRules.ComposeCareerTitle('
Require-Present 'unit stats expose an independent official career row' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'ShowOfficialCareerRow(__instance, actor);'
Require-Present 'unit official career row uses hot rank projection' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'OfficialCareerStateService.ReadRankFast(pActor)'
Require-Present 'unit official career row uses shared title composition' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'OfficialCareerRankRules.ComposeCareerTitle('
Require-Present 'unit official career row resolves display-only general office fallback' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'OfficialCareerRankRules.ResolveDisplayedOfficeId('
Require-Present 'unit official career row resolves military track for general fallback' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'OfficialCareerRankRules.ResolveDisplayedTrack('
Require-Present 'unit official career row detects active generals without a database read' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'GeneralService.IsActiveGeneralFast(pActor)'
Require-Present 'unit official career kingdom falls back to the actor kingdom' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'return pActor.kingdom;'
Require-Absent 'unit official career rendering cannot forge a court office assignment' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'data.set(LineageKeys.COURT_OFFICE_ID'
Require-Absent 'unit official career rendering cannot forge a court kingdom assignment' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'data.set(LineageKeys.COURT_KINGDOM_ID'
Require-Present 'unit official career row shrinks long titles' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'row.value.resizeTextForBestFit = true;'
Require-Present 'unit official career row expands the pooled layout row' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'OfficialCareerRankRules.UnitWindowCareerRowHeight()'
Require-Present 'unit official career row publishes its expanded preferred height' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'layout.preferredHeight = rowHeight;'
Require-Present 'unit official career row allows text inside its expanded layout' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'row.value.verticalOverflow = VerticalWrapMode.Overflow;'
Require-Absent 'unit official career row cannot retain the vanilla vertical truncation mode' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'row.value.verticalOverflow = VerticalWrapMode.Truncate;'
Require-Present 'unit official career row wraps long titles' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'row.value.horizontalOverflow = HorizontalWrapMode.Wrap;'
Require-Present 'unit official career row keeps full title in a tooltip' `
    'Code/patch/AW_UnitWindowPatch.cs' `
    'row.on_hover_value = () => Tooltip.show('
Require-Present 'court tooltip shows official term year' `
    'Code/ui/items/CourtActorNodeView.cs' 'aw_court_official_term_end'
Require-Present 'court tooltip shows official evaluation' `
    'Code/ui/items/CourtActorNodeView.cs' 'aw_court_official_kaoke'
Require-Present 'ministerial power runs once after official careers' `
    'Code/core/policy/KingdomAnnualWorkService.cs' 'MinisterialPowerService.OnKingdomYear(pKingdom)'
Require-Present 'ministerial power has a kingdom idempotency key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string MINISTERIAL_POWER_LAST_YEAR = "aw_ministerial_power_last_year";'
Require-Present 'ministerial power rejects duplicate years' `
    'Code/core/court/MinisterialPowerService.cs' 'if (lastYear == year) return;'
Require-Present 'ministerial power performs one active-officer batch read' `
    'Code/core/court/MinisterialPowerService.cs' 'CourtService.GetActiveOfficers(pKingdom, 96)'
Require-Present 'ministerial premier uses stable pure candidate ordering' `
    'Code/core/court/MinisterialPowerService.cs' 'MinisterialPowerRules.CompareCandidates('
Require-Present 'ministerial power tracks the previous premier by id' `
    'Code/core/court/MinisterialPowerService.cs' 'LineageKeys.MINISTERIAL_PREMIER_ID'
Require-Absent 'ministerial power cannot scan all realm actors' `
    'Code/core/court/MinisterialPowerService.cs' 'getUnits()'
Require-Absent 'ministerial power service cannot query SQLite directly' `
    'Code/core/court/MinisterialPowerService.cs' 'SQLite'
Require-Present 'ministerial power skips republics' `
    'Code/core/court/MinisterialPowerService.cs' 'RepublicGovernmentService.IsRepublic(pKingdom)'
Require-Present 'ministerial power has a top-level benchmark' `
    'Code/core/policy/UpdateAgeBenchmarkRules.cs' 'KingdomMinisterialPower'

$kingdomAnnualWork = Read-Source 'Code/core/policy/KingdomAnnualWorkService.cs'
$officialCareerCall = $kingdomAnnualWork.IndexOf(
    'OfficialCareerStateService.OnKingdomYear(pKingdom)',
    [System.StringComparison]::Ordinal)
$ministerialPowerCall = $kingdomAnnualWork.IndexOf(
    'MinisterialPowerService.OnKingdomYear(pKingdom)',
    [System.StringComparison]::Ordinal)
if ($officialCareerCall -lt 0 -or $ministerialPowerCall -lt 0 -or
    $ministerialPowerCall -lt $officialCareerCall) {
    $failures.Add('ministerial power must run after official career projection')
}
Require-Present 'ministerial thresholds enter personal biography' `
    'Code/core/court/MinisterialPowerService.cs' 'HistoryWriter.RecordPerson('
Require-Present 'ministerial thresholds enter realm history' `
    'Code/core/court/MinisterialPowerService.cs' 'HistoryWriter.RecordKingdom('
Require-Present 'court direction reads the cached premier id once' `
    'Code/core/court/CourtDirectionService.cs' 'LineageKeys.MINISTERIAL_PREMIER_ID'
Require-Present 'court direction applies bounded premier power' `
    'Code/core/court/CourtDirectionService.cs' 'MinisterialPowerRules.DirectionMultiplier('
Require-Absent 'court direction cannot query ministerial state from SQLite' `
    'Code/core/court/CourtDirectionService.cs' 'MinisterialPowerService.'
Require-Present 'court actor tooltip exposes ministerial power' `
    'Code/ui/items/CourtActorNodeView.cs' 'aw_court_ministerial_power'
Require-Present 'general rebellion exposes a generic palace coup resolver' `
    'Code/core/lineage/GeneralRebellionService.cs' 'internal static bool TryResolvePalaceCoup('
Require-Present 'general palace coup reuses the generic resolver' `
    'Code/core/lineage/GeneralRebellionService.cs' 'TryResolvePalaceCoup(pGeneral, pKingdom, pRisk)'
Require-Present 'ministerial power delegates its coup to the generic resolver' `
    'Code/core/court/MinisterialPowerService.cs' 'GeneralRebellionService.TryResolvePalaceCoup('
Require-Present 'ministerial usurpation requires the ambitious trait' `
    'Code/core/court/MinisterialPowerService.cs' 'pActor.hasTrait("ambitious")'
Require-Present 'content ministers are barred from usurpation' `
    'Code/core/court/MinisterialPowerService.cs' 'pActor.hasTrait("content")'
Require-Present 'ministerial usurpation derives an explicit puppet ruler gate' `
    'Code/core/court/MinisterialPowerService.cs' 'MinisterialPowerRules.IsPuppetRuler('
Require-Present 'generic palace coups cannot bypass ministerial eligibility' `
    'Code/core/lineage/GeneralRebellionService.cs' 'MinisterialPowerService.CanResolvePalaceCoup('
$ministerialPowerSource = Read-Source 'Code/core/court/MinisterialPowerService.cs'
$coupEligibilityStart = $ministerialPowerSource.IndexOf(
    'internal static bool CanResolvePalaceCoup(',
    [System.StringComparison]::Ordinal)
$coupEligibilityEnd = if ($coupEligibilityStart -ge 0) {
    $ministerialPowerSource.IndexOf(
        'private static bool IsAmbitiousUsurper(', $coupEligibilityStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($coupEligibilityStart -lt 0 -or $coupEligibilityEnd -le
    $coupEligibilityStart) {
    $failures.Add('missing centralized ministerial coup eligibility gate')
}
elseif ($ministerialPowerSource.Substring($coupEligibilityStart,
            $coupEligibilityEnd - $coupEligibilityStart).Contains(
            'HasLowMandate(')) {
    $failures.Add('low Mandate cannot replace the puppet-ruler usurpation gate')
}
Require-Present 'generic palace coup clears obsolete court office before accession' `
    'Code/core/lineage/GeneralRebellionService.cs' 'CourtService.ClearOfficeForReignTransition('
Require-Present 'failed ministerial coup dismisses the premier' `
    'Code/core/court/MinisterialPowerService.cs' 'CourtService.ClearOfficeForReignTransition('
Require-Absent 'civil premier coup cannot mark general rebellion state' `
    'Code/core/court/MinisterialPowerService.cs' 'GeneralService.MarkRebelled'
Require-Absent 'civil premier coup cannot set the general one-shot flag' `
    'Code/core/court/MinisterialPowerService.cs' 'aw_general_rebelled_once'
Require-Present 'ministerial coup history title is localized' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_ministerial_palace_coup_success'
Require-Present 'ministerial threshold history title is localized' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_ministerial_power_40'
Require-Present 'former premier power remembers its source realm' `
    'Code/core/lineage/LineageKeys.cs' 'MINISTERIAL_POWER_LAST_KINGDOM_ID'
Require-Present 'palace coup accession opens a formal affiliation transfer' `
    'Code/core/lineage/GeneralRebellionService.cs' 'FormalAffiliationTransferScope.Open('
Require-Present 'palace coup accession moves the challenger to the capital' `
    'Code/core/lineage/GeneralRebellionService.cs' 'pChallenger.joinCity(capital);'
Require-Present 'successful usurpation snapshots loyalist opposition before accession' `
    'Code/core/lineage/GeneralRebellionService.cs' 'CoupRestorationService.Prepare(pKingdom, pChallenger)'
Require-Present 'coup loyalists use a bounded multi-city coalition' `
    'Code/core/lineage/CoupRestorationRules.cs' 'MaximumCoalitionCities = 3'
Require-Present 'coup loyalist coalition selection is deterministic' `
    'Code/core/lineage/CoupRestorationRules.cs' 'SelectCoalition('
Require-Present 'coup loyalist supporters persist on the war' `
    'Code/core/lineage/CoupRestorationService.cs' 'COUP_RESTORATION_SUPPORTER_IDS'
Require-Present 'coup loyalist cities persist on the war' `
    'Code/core/lineage/CoupRestorationService.cs' 'COUP_RESTORATION_SEAT_CITY_IDS'
Require-Present 'coup loyalist coalition uses one shared rebel kingdom' `
    'Code/core/lineage/CoupRestorationService.cs' 'JoinCoalitionCities('
Require-Present 'loyalist victory resolves an old-dynasty claimant' `
    'Code/core/lineage/CoupRestorationService.cs' 'ResolveClaimant(rebel, oldRulerId, alternateId)'
Require-Present 'loyalist victory installs the old dynasty in the original realm' `
    'Code/core/lineage/CoupRestorationService.cs' 'InstallClaimant(original, claimant)'
Require-Present 'coup restoration is settled from the real war result' `
    'Code/patch/AW_WarPatch.cs' 'CoupRestorationService.OnWarEnded(pWar, pWinner);'

Require-Present 'vassal relation persists its contract tier' `
    'Code/core/db/VassalRelationTableItem.cs' 'public int contract_tier = VassalContractTierRules.Outer;'
Require-Present 'relation creation writes the contract tier' `
    'Code/core/lineage/VassalService.cs' 'ColumnVal.Create("CONTRACT_TIER", contractTier)'
Require-Present 'relation reads include the contract tier' `
    'Code/core/lineage/VassalService.cs' 'CONTRACT_TIER,START_TIME'
Require-Present 'tributary uses a separate suzerain hot key' `
    'Code/core/lineage/LineageKeys.cs' 'TRIBUTARY_SUZERAIN_ID'
Require-Present 'tributary uses a separate relation hot key' `
    'Code/core/lineage/LineageKeys.cs' 'TRIBUTARY_RELATION_ID'
Require-Present 'tributary suzerains use an O(1) direct count gate' `
    'Code/core/lineage/LineageKeys.cs' 'TRIBUTARY_DIRECT_COUNT'
Require-Present 'annual tribute skips realms without direct subjects' `
    'Code/core/lineage/VassalService.cs' 'GetDirectTributaryCount(pSuzerain) <= 0'
Require-Present 'vassal service exposes tributary creation' `
    'Code/core/lineage/VassalService.cs' 'public static bool SetTributary('
Require-Present 'closing a relation respects tier hierarchy semantics' `
    'Code/core/lineage/VassalService.cs' 'VassalContractTierRules.CountsAsVassal(contractTier)'
Require-Present 'loose tributaries are excluded from war adjacency' `
    'Code/core/lineage/VassalService.cs' 'VassalContractTierRules.CanJoinSuzerainWar(relation.contract_tier)'
Require-Present 'tributary war asset is registered' `
    'Code/content/DiplomacyContent.cs' 'AddWarType("tributary_war"'
Require-Present 'tributary war has an intrinsic casus belli' `
    'Code/core/lineage/WarDecisionService.cs' 'case WAR_TRIBUTARY:'
Require-Present 'tributary war settlement creates a loose tributary' `
    'Code/core/lineage/VassalService.cs' 'SetTributary(defender, attacker, "tributary_war"'
Require-Present 'war permission resolves both vassal and tributary suzerains' `
    'Code/core/lineage/WarDecisionService.cs' 'VassalService.GetDiplomaticSuzerain(pAttacker)'
Require-Present 'ordinary war targets resolve both vassal and tributary status' `
    'Code/core/lineage/WarTerritoryService.cs' 'VassalService.GetDiplomaticSuzerain(pTarget)'
Require-Present 'tributaries can expose only their independence war against the tribute suzerain' `
    'Code/core/lineage/WarTerritoryService.cs' 'VassalService.GetDiplomaticSuzerain(pSource) == pTarget'
Require-Present 'war target window exposes the tributary action' `
    'Code/ui/windows/WarDecisionTargetWindow.cs' 'aw_war_force_tributary'
Require-Present 'tributary war is localized' `
    'Locales/war.csv' 'war_type_tributary_war,'
Require-Present 'contract tier tooltip is localized' `
    'Locales/others.csv' 'aw_vassal_contract_tier,'
Require-Present 'diplomacy root suzerain is cached' `
    'Code/core/lineage/LineageKeys.cs' 'DIPLOMACY_ROOT_SUZERAIN_ID'
Require-Present 'diplomacy rites score is cached' `
    'Code/core/lineage/LineageKeys.cs' 'DIPLOMACY_RITES_SCORE'
Require-Present 'diplomacy mandate status is cached' `
    'Code/core/lineage/LineageKeys.cs' 'DIPLOMACY_IS_MANDATE'
Require-Present 'diplomacy culture is cached for institution opinion' `
    'Code/core/lineage/LineageKeys.cs' 'DIPLOMACY_CULTURE_ID'
Require-Present 'ritual diplomacy snapshot runs annually' `
    'Code/core/policy/KingdomAnnualWorkService.cs' 'RitualDiplomacyOpinionService.OnKingdomYear(pKingdom);'
Require-Present 'zhengshuo opinion asset is registered' `
    'Code/core/lineage/RitualDiplomacyOpinionService.cs' 'aw_opinion_zhengshuo'
Require-Present 'rites opinion asset is registered' `
    'Code/core/lineage/RitualDiplomacyOpinionService.cs' 'aw_opinion_rites'
Require-Present 'usurpation opinion asset is registered' `
    'Code/core/lineage/RitualDiplomacyOpinionService.cs' 'aw_opinion_usurpation'
Require-Present 'Tang openness opinion asset is registered' `
    'Code/core/lineage/RitualDiplomacyOpinionService.cs' 'aw_opinion_court_openness'
Require-Present 'Tang openness reads the annual culture snapshot' `
    'Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs' 'DIPLOMACY_CULTURE_ID'
Require-Absent 'opinion callbacks cannot traverse vassal relations' `
    'Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs' 'VassalService'
Require-Absent 'opinion callbacks cannot query mandate state' `
    'Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs' 'MandateService'
Require-Absent 'opinion callbacks cannot inspect policy state' `
    'Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs' 'KingdomPolicyService'
Require-Absent 'opinion callbacks cannot query SQLite' `
    'Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs' 'SQLite'
Require-Absent 'opinion callbacks cannot scan the world' `
    'Code/core/lineage/RitualDiplomacyOpinionCallbacks.cs' 'World.world'
Require-Present 'ritual diplomacy opinion is localized' `
    'Locales/others.csv' 'opinion_aw_zhengshuo,'
Require-Present 'Tang openness opinion is localized' `
    'Locales/aw3_court.csv' 'opinion_aw_court_openness,'

Require-Present 'feudatory service exists' `
    'Code/core/lineage/FeudatoryService.cs' 'class FeudatoryService'
Require-Absent 'feudatory service cannot replace governors' `
    'Code/core/lineage/FeudatoryService.cs' '.setLeader('
Require-Absent 'feudatories are not vassal relations' `
    'Code/core/lineage/FeudatoryService.cs' 'VassalRelation'
Require-Absent 'feudatory map mode cannot query SQLite' `
    'Code/core/policy/FeudatoryMapModeService.cs' 'SQLite'
Require-Absent 'feudatory map mode cannot scan actors' `
    'Code/core/policy/FeudatoryMapModeService.cs' 'World.world.units'
Require-Absent 'feudatory map mode cannot scan cities' `
    'Code/core/policy/FeudatoryMapModeService.cs' 'World.world.cities'
Require-Present 'feudatory map uses its own meta type' `
    'Code/core/policy/AWMapModeMetaTypes.cs' 'public const MetaType Feudatory = (MetaType)218;'
Require-Present 'feudatory map registers only an AW meta asset' `
    'Code/core/policy/AWMapModeMetaLibrary.cs' 'GetFeudatoryMetaForZone'
Require-Present 'feudatory map skips non-Mandate kingdom zones early' `
    'Code/core/policy/AWMapModeMetaLibrary.cs' 'FeudatoryMapModeService.IsMandateKingdom(pKingdom)'
Require-Present 'feudatory map has its own tab toggle' `
    'Code/ui/AW_LineageTab.cs' 'FeudatoryMapModeService.POWER_ID'
Require-Present 'feudatory overview reuses wide window chrome' `
    'Code/ui/windows/FeudatoryWindow.cs' 'WideWindowChrome.Attach('
Require-Present 'feudatory overview resizes the native viewport' `
    'Code/ui/windows/FeudatoryWindow.cs' 'nativeScrollRect.sizeDelta'
Require-Present 'feudatory scroll content uses fixed-width anchors' `
    'Code/ui/windows/FeudatoryWindow.cs' 'pContent.anchorMax = new Vector2(0f, 1f);'
Require-Present 'feudatory overview reads immutable kingdom snapshots' `
    'Code/ui/windows/FeudatoryWindow.cs' 'FeudatoryService.GetByKingdom('
Require-Present 'feudatory portrait reuses the live actor avatar' `
    'Code/ui/components/FeudatoryPortraitPanel.cs' 'UiUnitAvatarElement'
Require-Present 'feudatory overview has a dedicated window id' `
    'Code/ui/AW_LineageWindowIds.cs' 'FEUDATORIES = "aw_feudatories"'
Require-Present 'Mandate window opens feudatory overview' `
    'Code/ui/windows/MandateDynastyWindow.cs' 'FeudatoryWindow.Open(kingdom.id)'
Require-Absent 'feudatory overview cannot query SQLite' `
    'Code/ui/windows/FeudatoryWindow.cs' 'SQLite'
Require-Absent 'feudatory list rows cannot query SQLite' `
    'Code/ui/items/FeudatoryListItem.cs' 'SQLite'
Require-Present 'feudatory list labels stretch within narrow rows' `
    'Code/ui/items/FeudatoryListItem.cs' 'LayoutStretchWidth('
Require-Absent 'feudatory portraits cannot query SQLite' `
    'Code/ui/components/FeudatoryPortraitPanel.cs' 'SQLite'
Require-Present 'feudatory portrait labels stretch within detail width' `
    'Code/ui/components/FeudatoryPortraitPanel.cs' 'LayoutStretchWidth('
Require-Present 'feudatory window gates command completion refreshes' `
    'Code/ui/windows/FeudatoryWindow.cs' `
    'if (!_commandRefreshRequested) return;'
Require-Absent 'closed feudatory window cannot run LateUpdate' `
    'Code/ui/windows/FeudatoryWindow.cs' 'void LateUpdate('
Require-Absent 'feudatory window cannot query SQLite' `
    'Code/ui/windows/FeudatoryWindow.cs' 'SQLite'
Require-Absent 'feudatory service cannot scan all actors' `
    'Code/core/lineage/FeudatoryService.cs' 'World.world.units'
Require-Present 'feudatory header table exists' `
    'Code/core/db/FeudatoryTableItem.cs' '[TableDef("Feudatory")]'
Require-Present 'feudatory city table exists' `
    'Code/core/db/FeudatoryCityTableItem.cs' '[TableDef("FeudatoryCity")]'
Require-Present 'active prince uniqueness is indexed' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'uq_Feudatory_prince_active'
Require-Present 'active feudatory city uniqueness is indexed' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'uq_FeudatoryCity_city_active'
Require-Present 'feudatory persistence uses a transaction' `
    'Code/core/lineage/FeudatoryService.cs' 'BeginTransaction()'
Require-Present 'feudatory persistence commits before projection' `
    'Code/core/lineage/FeudatoryService.cs' 'transaction.Commit();'
Require-Present 'feudatory actor id has a hot key' `
    'Code/core/lineage/LineageKeys.cs' 'FEUDATORY_ID'
Require-Present 'feudatory city id has a separate hot key' `
    'Code/core/lineage/LineageKeys.cs' 'CITY_FEUDATORY_ID'
Require-Present 'feudatory names are persisted independently from their seats' `
    'Code/core/db/FeudatoryTableItem.cs' 'public string feudatory_name = "";'
Require-Present 'feudatory establishment reuses or grants one persistent princely title' `
    'Code/core/lineage/FeudatoryService.cs' 'NobleRankService.EnsureFeudatoryPrinceTitle('
Require-Present 'feudatory establishment creates one traceable cadet Shi' `
    'Code/core/lineage/FeudatoryService.cs' 'LineageService.EnsureFeudatoryShiBranch('
Require-Absent 'feudatory relocation cannot rename the persistent title from its new seat' `
    'Code/core/lineage/FeudatoryService.cs' 'BuildFeudatoryName(newSeat.data.name)'
Require-Present 'feudatory map labels combine the persistent title and current city' `
    'Code/core/policy/AWMapModeMetaLibrary.cs' 'FeudatoryMapModeRules.BuildCityLabel('
Require-Present 'feudatory map clicks inspect the prince actor' `
    'Code/core/policy/AWMapModeMetaLibrary.cs' 'FeudatoryAsset.click_action_zone = FeudatoryMapModeService.SelectPrince;'
Require-Present 'feudatory map actor navigation uses unit meta inspection' `
    'Code/core/policy/FeudatoryMapModeService.cs' 'unitMeta.selectAndInspect('
Require-Present 'feudatory princes reuse the heir texture without becoming imperial heirs' `
    'Code/content/XiaTexturePatch.cs' 'FeudatoryService.IsActivePrince(pActor)'
Require-Absent 'feudatory service cannot set the imperial heir identity flag' `
    'Code/core/lineage/FeudatoryService.cs' 'data.set(LineageKeys.IS_HEIR'
Require-Present 'feudatory succession requires founder-tree descent' `
    'Code/core/lineage/FeudatorySuccessionRules.cs' 'DirectTreeDescendant'
Require-Present 'birth refreshes contextual royal and feudatory titles' `
    'Code/patch/AW_BabyNamePatch.cs' 'DynasticTitleService.OnChildBorn('
Require-Present 'adulthood grants or refreshes dynastic titles once' `
    'Code/patch/AW_AgePatch.cs' 'DynasticTitleService.OnAgeUpdated('
Require-Present 'death snapshots and refreshes dynastic titles' `
    'Code/patch/AW_ActorDeathPatch.cs' 'DynasticTitleService.OnActorDying('
Require-Present 'unit windows use the shared dynastic title resolver' `
    'Code/patch/AW_UnitWindowPatch.cs' 'DynasticTitleService.ResolveLivingTitle('
Require-Present 'ancestry details use the shared dynastic title resolver' `
    'Code/core/lineage/AncestryAnalysisService.cs' 'DynasticTitleService.ResolveLivingTitle('
Require-Present 'family tree nodes use the shared dynastic title resolver' `
    'Code/core/lineage/LineageQuery.cs' 'DynasticTitleService.ResolveLivingTitle('
Require-Present 'feudatory portraits use the shared dynastic title resolver' `
    'Code/ui/windows/FeudatoryWindow.cs' 'DynasticTitleService.ResolveLivingTitle('
Require-Present 'favor order has a kingdom hot key' `
    'Code/core/lineage/LineageKeys.cs' 'FAVOR_ORDER_ENABLED'
Require-Absent 'favor order is not an ordinary social policy' `
    'Code/content/policies/KingdomPolicyDefs.cs' 'Id = "aw_policy_favor_order"'
Require-Absent 'ordinary policy AI cannot research favor order' `
    'Code/core/policy/KingdomPolicyAI.cs' 'aw_policy_favor_order'
Require-Present 'favor order is a Mandate decision' `
    'Code/core/lineage/MandateDecisionService.cs' 'Id = "aw_mandate_decision_favor_order"'
Require-Present 'favor order completion writes the permanent hot flag' `
    'Code/core/lineage/FeudatoryService.cs' 'pEmpire.data.set(LineageKeys.FAVOR_ORDER_ENABLED, true);'
Require-Present 'favor order succession closes one city in the inheritance transaction' `
    'Code/core/lineage/FeudatoryService.cs' 'END_REASON=''favor_order'''
Require-Present 'centralization immediately applies feudatory autonomy caps' `
    'Code/core/lineage/CentralizationService.cs' 'FeudatoryService.ApplyAutonomyCap('
Require-Present 'four-year feudatory maintenance applies loyalty evolution' `
    'Code/core/lineage/FeudatoryService.cs' 'ApplyMaintenanceEvolution(currentRows, mandateValue,'
Require-Present 'feudatory loyalty evolution commits in one transaction' `
    'Code/core/lineage/FeudatoryService.cs' 'SET LOYALTY=@loyalty WHERE FEUDATORY_ID=@id'
Require-Present 'feudatory loyalty evolution uses the immutable hot snapshot' `
    'Code/core/lineage/FeudatoryService.cs' 'WithAutonomyLoyalty(snapshot.Autonomy, loyalty)'
Require-Present 'revocation intensity has one pure rule source' `
    'Code/core/lineage/FeudatoryRevocationRules.cs' 'CityReclamationIntensity = 35'
Require-Present 'feudatory relocation uses bounded kingdom city candidates' `
    'Code/core/lineage/FeudatorySelectionService.cs' 'TrySelectRelocationCities('
Require-Present 'feudatory relocation excludes old member cities' `
    'Code/core/lineage/FeudatorySelectionService.cs' 'FeudatoryRevocationRules.CanUseRelocationCity('
Require-Present 'feudatory relocation has a public transaction entry' `
    'Code/core/lineage/FeudatoryService.cs' 'TryRelocateFeudatory('
Require-Present 'feudatory city reclamation has a public transaction entry' `
    'Code/core/lineage/FeudatoryService.cs' 'TryReclaimFeudatoryCity('
Require-Present 'manual feudatory abolition has a public transaction entry' `
    'Code/core/lineage/FeudatoryService.cs' 'TryAbolishFeudatory('
Require-Present 'relocation closes prior city assignments atomically' `
    'Code/core/lineage/FeudatoryService.cs' 'END_REASON=''relocation'''
Require-Present 'relocation updates the persisted seat atomically' `
    'Code/core/lineage/FeudatoryService.cs' 'SET SEAT_CITY_ID=@seat WHERE FEUDATORY_ID=@id'
Require-Present 'feudatory relocation writes chronicle history' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnFeudatoryRelocated('
Require-Present 'feudatory city reclamation writes chronicle history' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnFeudatoryCityReclaimed('
Require-Present 'active feudatory governance is localized' `
    'Locales/aw3_mandate.csv' 'aw_feudatory_action_relocate,'
Require-Present 'feudatory successor uses the historical heir-apparent title' `
    'Locales/aw3_mandate.csv' 'aw_feudatory_successor,'
Require-Present 'feudatory successor English fallback is heir apparent' `
    'Locales/aw3_mandate.csv' ',Heir Apparent,'
Require-Present 'missing feudatory successor uses the exact empty-state label' `
    'Locales/aw3_mandate.csv' ',No heir apparent,'
Require-Present 'feudatory window exposes relocation control' `
    'Code/ui/windows/FeudatoryWindow.cs' 'RelocateSelectedFeudatory'
Require-Present 'feudatory window exposes selected city reclamation' `
    'Code/ui/windows/FeudatoryWindow.cs' 'ReclaimSelectedCity'
Require-Present 'feudatory window requires abolition confirmation' `
    'Code/ui/windows/FeudatoryWindow.cs' '_abolishArmed'
Require-Present 'city economy reads feudatory autonomy from the hot cache' `
    'Code/core/policy/CityEconomyService.cs' 'FeudatoryService.TryGetByCity('
Require-Present 'feudatory garrison maintenance is deferred out of UpdateAge' `
    'Code/core/lineage/FeudatoryGarrisonService.cs' 'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'feudatory garrison scans have a hard candidate cap' `
    'Code/core/lineage/FeudatoryAutonomyRules.cs' 'MaximumGarrisonCandidateScan = 32'
Require-Present 'special army deletion detaches actor army references first' `
    'Code/core/lineage/AWArmyService.cs' 'unit.removeFromArmy();'
Require-Present 'favor order decision is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_decision_favor_order,'
Require-Present 'bounded feudatory selection service exists' `
    'Code/core/lineage/FeudatorySelectionService.cs' 'class FeudatorySelectionService'
Require-Present 'great enfeoffment reads only the kings children' `
    'Code/core/lineage/FeudatorySelectionService.cs' 'king.getChildren(false)'
Require-Absent 'great enfeoffment cannot scan all actors' `
    'Code/core/lineage/FeudatorySelectionService.cs' 'World.world.units'
Require-Present 'great enfeoffment is a Mandate decision' `
    'Code/core/lineage/MandateDecisionService.cs' 'aw_mandate_decision_great_enfeoffment'
Require-Present 'great enfeoffment validates through its service' `
    'Code/core/lineage/MandateDecisionService.cs' 'FeudatorySelectionService.CanExecuteGreatEnfeoffment(pKingdom)'
Require-Present 'great enfeoffment completion delegates to its service' `
    'Code/core/lineage/MandateDecisionService.cs' 'FeudatorySelectionService.ExecuteGreatEnfeoffment(pKingdom) > 0'
Require-Present 'great enfeoffment is localized' `
    'Locales/aw3_policy_ui.csv' 'aw_mandate_decision_great_enfeoffment,'
Require-Present 'feudatory content registers a prince job' `
    'Code/content/FeudatoryContent.cs' 'aw_job_feudatory_prince'
Require-Present 'feudatory content registers a roaming task' `
    'Code/content/FeudatoryContent.cs' 'aw_task_feudatory_prince_roam'
Require-Present 'feudatory content registers the prince trait' `
    'Code/content/FeudatoryContent.cs' 'fanwang'
$feudatoryContent = Get-Content -Raw -Encoding UTF8 `
    (Join-Path $root 'Code/content/FeudatoryContent.cs')
$feudatoryTraitAdd = $feudatoryContent.IndexOf(
    'AssetManager.traits.add(trait);', [System.StringComparison]::Ordinal)
$feudatoryTraitStats = $feudatoryContent.IndexOf(
    'trait.base_stats["stewardship"]', [System.StringComparison]::Ordinal)
if ($feudatoryTraitAdd -lt 0 -or $feudatoryTraitStats -lt 0 -or `
    $feudatoryTraitAdd -gt $feudatoryTraitStats) {
    $failures.Add('feudatory trait must be registered before base_stats are written')
}
Require-Present 'Xia content initializes feudatories' `
    'Code/content/XiaContent.cs' 'FeudatoryContent.Init();'
Require-Present 'feudatory establishment moves prince residence' `
    'Code/core/lineage/FeudatoryService.cs' 'pPrince.joinCity(pSeat);'
Require-Present 'feudatory prince job survives job refresh' `
    'Code/patch/AW_EnlistPatch.cs' 'FeudatoryContent.ActorJobId'
Require-Present 'feudatory princes are protected from ordinary enlistment' `
    'Code/patch/AW_EnlistPatch.cs' 'FeudatoryService.IsActivePrince(pActor)'
Require-Present 'feudatory establishment writes history' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnFeudatoryEstablished'
Require-Present 'actor death resolves feudatory succession immediately' `
    'Code/patch/AW_ActorDeathPatch.cs' 'DynasticTitleService.OnActorDying(__instance)'
Require-Present 'feudatory succession updates the active prince atomically' `
    'Code/core/lineage/FeudatoryService.cs' 'SET PRINCE_ACTOR_ID=@prince,PRINCE_NAME=@name,'
Require-Present 'feudatory succession preserves whole city membership' `
    'Code/core/lineage/FeudatoryService.cs' 'pSnapshot.WithPrince('
Require-Present 'extinct feudatories close all active city rows' `
    'Code/core/lineage/FeudatoryService.cs' 'WHERE FEUDATORY_ID=@id AND ACTIVE=1'
Require-Present 'collateral succession uses a bounded archive fallback' `
    'Code/core/lineage/LineageQuery.cs' 'ORDER BY BIRTH_TIME ASC,ID ASC LIMIT @limit'
Require-Present 'succession kin traversal has a hard node cap' `
    'Code/core/lineage/FeudatoryService.cs' 'MaximumSuccessionKinNodes = 128'
Require-Present 'feudatory inheritance is localized' `
    'Locales/aw3_mandate.csv' 'aw_hist_event_feudatory_inherited,'
Require-Present 'feudatory abolition is localized' `
    'Locales/aw3_mandate.csv' 'aw_hist_event_feudatory_abolished,'
Require-Present 'feudatory trait is localized' `
    'Locales/trait.csv' 'trait_fanwang,'
Require-Present 'feudatory job is localized' `
    'Locales/aw3_mandate.csv' 'aw_job_feudatory_prince,'
Require-Present 'feudatory roaming task is localized' `
    'Locales/aw3_mandate.csv' 'task_unit_aw_task_feudatory_prince_roam,'
Require-Present 'feudatory garrison service exists' `
    'Code/core/lineage/FeudatoryGarrisonService.cs' 'class FeudatoryGarrisonService'
Require-Present 'feudatory garrison uses indexed army creation' `
    'Code/core/lineage/FeudatoryGarrisonService.cs' 'AWArmyService.EnsureArmy('
Require-Present 'feudatory garrison reads active generals only when repairing' `
    'Code/core/lineage/FeudatoryGarrisonService.cs' 'GeneralService.GetActiveGenerals(pEmpire)'
Require-Absent 'feudatory garrison cannot scan city residents' `
    'Code/core/lineage/FeudatoryGarrisonService.cs' 'units.getSimpleList'
Require-Absent 'feudatory garrison cannot scan all actors' `
    'Code/core/lineage/FeudatoryGarrisonService.cs' 'World.world.units'
Require-Present 'feudatory establishment requests its garrison' `
    'Code/core/lineage/FeudatoryService.cs' 'FeudatoryGarrisonService.EnsureFor(snapshot)'
Require-Present 'feudatory cities become Mandate legal cores' `
    'Code/core/lineage/FeudatoryService.cs' 'MandateService.OnKingdomCoreCreated(pEmpire, pCities[i], "feudatory")'
Require-Present 'seat repair reanchors the persisted garrison' `
    'Code/core/lineage/FeudatoryGarrisonService.cs' 'AWArmyService.ReanchorArmy('
Require-Present 'city transfers repair only the affected feudatory' `
    'Code/patch/AW_ChroniclePatch.cs' 'FeudatoryService.OnCityTransferred('
Require-Present 'feudatory annual work has its own benchmark' `
    'Code/core/policy/KingdomAnnualWorkService.cs' 'UpdateAgeBenchmarkRules.KingdomFeudatoryIndex'
Require-Present 'feudatory annual work is called once per kingdom' `
    'Code/core/policy/KingdomAnnualWorkService.cs' 'FeudatoryService.OnKingdomYear(pKingdom)'
Require-Absent 'feudatory maintenance cannot scan world cities' `
    'Code/core/lineage/FeudatoryService.cs' 'World.world.cities'
Require-Present 'Jingnan has a dedicated war name template' `
    'Code/content/DiplomacyContent.cs' 'AddWarNameTemplate("war_jingnan"'
Require-Present 'Jingnan has a dedicated rebellion war type' `
    'Code/content/DiplomacyContent.cs' 'FeudatoryJingnanRules.WarTypeId'
Require-Present 'Jingnan is an intrinsic system casus belli' `
    'Code/core/lineage/WarDecisionService.cs' 'case FeudatoryJingnanRules.WarTypeId:'
Require-Present 'Jingnan has a localized display label' `
    'Code/core/lineage/WarDisplayLabelRules.cs' 'aw_hist_label_jingnan_war'
Require-Present 'Jingnan war type is localized' `
    'Locales/war.csv' 'war_type_jingnan_war,'
Require-Present 'Jingnan war name is localized' `
    'Locales/war.csv' 'war_name_jingnan_war,'
Require-Present 'Jingnan direct ancestors are an absolute rebellion bar' `
    'Code/core/lineage/FeudatoryJingnanRiskRules.cs' `
    'if (rulerIsDirectAgnaticAncestor) return false;'
Require-Present 'Jingnan revocation uses a deterministic threshold' `
    'Code/core/lineage/FeudatoryJingnanRiskRules.cs' `
    'RevocationRevoltThreshold = 90'
Require-Present 'Jingnan proactive revolt uses a higher threshold' `
    'Code/core/lineage/FeudatoryJingnanRiskRules.cs' `
    'ProactiveRevoltThreshold = 105'
Require-Present 'Jingnan runtime risk reads prince personality traits' `
    'Code/core/lineage/FeudatoryJingnanRiskService.cs' `
    'FeudatoryJingnanRiskRules.PersonalityAmbition('
Require-Present 'Jingnan runtime risk resolves agnatic seniority' `
    'Code/core/lineage/FeudatoryJingnanRiskService.cs' `
    'LineageQuery.NearestCommonAgnaticAncestor('
Require-Present 'Jingnan runtime risk caches ruler-relative kinship' `
    'Code/core/lineage/FeudatoryJingnanRiskService.cs' `
    'LineageKeys.JINGNAN_KIN_RULER_ID'
Require-Present 'Jingnan prince identity stores bounded base ambition' `
    'Code/core/lineage/FeudatoryService.cs' `
    'LineageKeys.FEUDATORY_AMBITION'
Require-Absent 'Jingnan risk cannot scan all world actors' `
    'Code/core/lineage/FeudatoryJingnanRiskService.cs' 'World.world.units'
Require-Absent 'Jingnan risk cannot scan kingdom actor lists' `
    'Code/core/lineage/FeudatoryJingnanRiskService.cs' '.getUnits('
Require-Present 'Jingnan activation splits its seat into a rebel kingdom' `
    'Code/core/lineage/FeudatoryJingnanService.cs' '.makeOwnKingdom('
Require-Present 'Jingnan activation joins remaining feudatory cities' `
    'Code/core/lineage/FeudatoryJingnanService.cs' '.joinAnotherKingdom('
Require-Present 'Jingnan activation starts a dedicated system war' `
    'Code/core/lineage/FeudatoryJingnanService.cs' `
    'WarDecisionService.TryStartSystemWar('
Require-Present 'multiple Jingnan rebels share one attacker side' `
    'Code/core/lineage/FeudatoryJingnanService.cs' 'war.joinAttackers(rebel)'
Require-Present 'Jingnan city splitting suppresses ordinary feudatory loss repair' `
    'Code/core/lineage/FeudatoryService.cs' 'IsIntentionalJingnanTransfer'
Require-Present 'Jingnan activation persists the rebellion status' `
    'Code/core/lineage/FeudatoryJingnanService.cs' `
    'FeudatoryRules.StatusRebelling'
Require-Present 'Jingnan rebels retain their original feudatory identity' `
    'Code/core/lineage/FeudatoryJingnanService.cs' `
    'LineageKeys.JINGNAN_FEUDATORY_ID'
Require-Present 'revocation checks Jingnan before mutating the feudatory' `
    'Code/core/lineage/FeudatoryService.cs' `
    'CheckRevoltOnRevocation('
Require-Present 'proactive Jingnan uses the higher deterministic threshold' `
    'Code/core/lineage/FeudatoryService.cs' `
    'FeudatoryJingnanRiskRules.ShouldProactivelyRevolt('
Require-Present 'proactive Jingnan reuses the bounded annual feudatory batch' `
    'Code/core/lineage/FeudatoryService.cs' `
    'MaximumAnnualRepairs = 4'
Require-Present 'Jingnan persistence records its active war' `
    'Code/core/db/FeudatoryTableItem.cs' 'active_war_id'
Require-Present 'Jingnan persistence records its rebel kingdom' `
    'Code/core/db/FeudatoryTableItem.cs' 'rebel_kingdom_id'
Require-Present 'Jingnan activation binds the persisted feudatory to its war' `
    'Code/core/lineage/FeudatoryJingnanService.cs' `
    'FeudatoryService.TryBindJingnanWar('
Require-Present 'capturing the original capital records the winning prince' `
    'Code/core/lineage/FeudatoryJingnanService.cs' `
    'LineageKeys.JINGNAN_VICTOR_REBEL_ID'
Require-Present 'city transfers notify the Jingnan capital objective' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'FeudatoryJingnanService.OnCityTransferred('
Require-Present 'war end invokes dedicated Jingnan settlement' `
    'Code/patch/AW_WarPatch.cs' 'FeudatoryJingnanService.OnWarEnded('
Require-Present 'Jingnan victory preserves the original kingdom identity' `
    'Code/core/lineage/FeudatoryJingnanService.cs' 'pEmpire.setKing(victorPrince)'
Require-Present 'Jingnan outbreak writes kingdom and personal history' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnFeudatoryJingnanStarted'
Require-Present 'Jingnan suppression writes kingdom and personal history' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnFeudatoryJingnanSuppressed'
Require-Present 'Jingnan accession writes kingdom and personal history' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnFeudatoryJingnanVictory'
Require-Present 'Jingnan history is localized' `
    'Locales/aw3_mandate.csv' 'aw_hist_event_jingnan_started,'
Require-Present 'Mandate collapse schedules cached feudatories before clearing the Mandate' `
    'Code/core/lineage/MandateService.cs' 'FeudatoryCollapseService.ScheduleOnMandateCollapse(pKingdom);'
Require-Present 'collapse feudatories use the shared deferred runtime queue' `
    'Code/core/lineage/FeudatoryCollapseService.cs' 'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'collapse queue coalesces by feudatory id' `
    'Code/core/lineage/FeudatoryCollapseService.cs' 'DeferredRuntimeWorkRules.CoalescingKey("mandate_collapse_feudatory", pFeudatoryId)'
Require-Absent 'collapse integration cannot scan all world actors' `
    'Code/core/lineage/FeudatoryCollapseService.cs' 'World.world.units'
Require-Absent 'collapse integration cannot scan all world cities' `
    'Code/core/lineage/FeudatoryCollapseService.cs' 'World.world.cities'
Require-Present 'collapse-mode Jingnan has an explicit runtime entry' `
    'Code/core/lineage/FeudatoryJingnanService.cs' 'TryActivateForMandateCollapse('
Require-Present 'collapse Jingnan victory records dynastic restoration' `
    'Code/core/lineage/FeudatoryJingnanService.cs' 'MarkDynasticRestorationCompleted('
Require-Present 'collapse Jingnan victory declares restored Mandate origin' `
    'Code/core/lineage/FeudatoryJingnanService.cs' 'MandateFeudatoryCompletionRules.RestorationOrigin'
Require-Present 'dynastic restoration inherits inactive Mandate legal cores' `
    'Code/core/lineage/MandateService.cs' 'ShouldInheritPreviousLegalCores('
Require-Present 'Mandate phases notify bounded military reconciliation' `
    'Code/core/lineage/MandatePhaseService.cs' 'MandateMilitaryPhaseService.OnPhaseChanged(previous, pPhase);'
Require-Present 'ending a Mandate reconciles the former Mandate army statuses' `
    'Code/core/lineage/MandateService.cs' 'MandateMilitaryPhaseService.OnMandateEnded(current);'
Require-Present 'warrior creation applies the current Mandate military status' `
    'Code/patch/AW_RetirementPatch.cs' 'MandateMilitaryPhaseService.ReconcileWarrior(pActor);'
Require-Present 'warrior dismissal clears Mandate military status' `
    'Code/patch/AW_RetirementPatch.cs' 'MandateMilitaryPhaseService.Clear(pActor);'
Require-Present 'standing armies consume effective Mandate warrior slots' `
    'Code/core/lineage/StandingArmyService.cs' 'EffectiveWarriorSlots(kingdom, pCity.status.warrior_slots)'
Require-Present 'military readiness consumes the same effective slot rule' `
    'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'EffectiveWarriorSlots(pCity.kingdom,'
Require-Present 'wartime levies consume effective Mandate warrior slots' `
    'Code/core/lineage/TemporaryLevyService.cs' 'EffectiveWarriorSlots(pKingdom, pCity.status.warrior_slots)'
Require-Present 'Mandate military quality uses status assets' `
    'Code/content/XiaStatus.cs' 'MandateMilitaryPhaseRules.RenewalStatusId'
Require-Absent 'Mandate military reconciliation cannot scan world actors' `
    'Code/core/lineage/MandateMilitaryPhaseService.cs' 'World.world.units'
Require-Absent 'Mandate military reconciliation cannot scan kingdom units' `
    'Code/core/lineage/MandateMilitaryPhaseService.cs' 'mandate.getUnits('
Require-Present 'feudatory offices have a dedicated court layer' `
    'Code/core/court/CourtIds.cs' 'public const string Feudatory = "feudatory";'
Require-Present 'feudatory offices have a chief clerk office id' `
    'Code/core/court/CourtIds.cs' 'public const string FeudatoryChiefClerk = "feudatory_changshi";'
Require-Present 'feudatory annual work maintains bounded yamen offices' `
    'Code/core/lineage/FeudatoryService.cs' 'FeudatoryOfficeService.MaintainBatch('
Require-Present 'feudatory appointments use the existing career writer' `
    'Code/core/court/CourtService.cs' 'TryAssignFeudatoryChiefClerk('
Require-Present 'feudatory office maintenance queries only the target seat' `
    'Code/core/court/FeudatoryOfficeService.cs' 'GetActiveFeudatoryOfficersAtSeat('
Require-Present 'feudatory office candidate scans use the hard cap' `
    'Code/core/court/FeudatoryOfficeService.cs' 'FeudatoryOfficeRules.MaxCandidateScan'
Require-Absent 'feudatory offices cannot scan all world actors' `
    'Code/core/court/FeudatoryOfficeService.cs' 'World.world.units'
Require-Absent 'feudatory offices cannot scan kingdom actor lists' `
    'Code/core/court/FeudatoryOfficeService.cs' 'pEmpire.getUnits('
Require-Present 'court read model shows active feudatory princes' `
    'Code/core/court/CourtReadModelService.cs' 'AddFeudatoryPrinces(seeds, pKingdom);'
Require-Present 'feudatory inspectors use the local court rank' `
    'Code/core/court/CourtReadModelService.cs' 'FeudatoryOfficeRules.InspectorRank'
Require-Present 'feudatory chief clerk is localized' `
    'Locales/aw3_court.csv' 'aw_court_office_feudatory_changshi,'
Require-Present 'feudatory prince court role is localized' `
    'Locales/aw3_court.csv' 'aw_court_office_feudatory_prince,'
Require-Present 'court direction exposes a hot cached read' `
    'Code/core/court/CourtDirectionService.cs' 'ReadCached(Kingdom pKingdom)'
Require-Present 'Mandate annual evaluation consumes cached political catalyst' `
    'Code/core/lineage/MandatePhaseService.cs' 'MandatePoliticalCatalystService.CourtDelta('
Require-Absent 'Mandate political catalyst cannot query court officers' `
    'Code/core/lineage/MandatePoliticalCatalystService.cs' 'GetActiveOfficers'
Require-Absent 'Mandate political catalyst cannot rebuild court contributions' `
    'Code/core/lineage/MandatePoliticalCatalystService.cs' 'BuildContributions'
Require-Present 'feudatory instability contributes during existing maintenance' `
    'Code/core/lineage/FeudatoryService.cs' 'FeudatoryInstabilityCatalystDelta('
Require-Present 'renewal military status title is localized' `
    'Locales/others.csv' 'status_title_aw_mandate_army_renewal,'
Require-Present 'golden military status description is localized' `
    'Locales/others.csv' 'status_description_aw_mandate_army_golden,'
Require-Present 'decline military status description is localized' `
    'Locales/others.csv' 'status_description_aw_mandate_army_decline,'
Require-Present 'chaos military status description is localized' `
    'Locales/others.csv' 'status_description_aw_mandate_army_chaos,'
Require-Present 'dynastic restoration origin is localized' `
    'Locales/aw3_mandate_extra.csv' 'aw_mandate_origin_feudatory_restoration,'
Require-Present 'dynastic restoration claimant is localized' `
    'Locales/aw3_mandate_extra.csv' 'aw_mandate_claimant_dynastic_restoration,'
Require-Present 'dynastic restoration history label is registered' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_mandate_declared_dynastic_restoration'
Require-Present 'individual noble ranks have a durable grant ledger' `
    'Code/core/db/EnfeoffmentTableItem.cs' '[TableDef("Enfeoffment")]'
Require-Present 'individual noble grants persist the title style' `
    'Code/core/db/EnfeoffmentTableItem.cs' 'public string title_style = "";'
Require-Present 'individual noble ranks have an actor hot key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string NOBLE_RANK = "aw_noble_rank";'
Require-Present 'individual noble styles have an actor hot key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string NOBLE_TITLE_STYLE = "aw_noble_title_style";'
Require-Present 'one actor can hold only one active titular grant' `
    'Code/core/db/LineageArchiveIndexRules.cs' 'uq_Enfeoffment_actor_active'
Require-Present 'titular grant replacement uses a SQLite transaction' `
    'Code/core/lineage/NobleRankService.cs' 'using SQLiteTransaction transaction = DB.BeginTransaction();'
Require-Present 'titular grant replacement closes the previous row' `
    'Code/core/lineage/NobleRankService.cs' 'CloseActiveGrant(transaction,'
Require-Present 'titular grant replacement inserts the new active row' `
    'Code/core/lineage/NobleRankService.cs' 'InsertActiveGrant(transaction,'
Require-Absent 'simplified titular ranks do not add personal prestige' `
    'Code/core/lineage/NobleRankService.cs' 'prestige'
Require-Present 'titular succession runs before the engine removes the actor' `
    'Code/patch/AW_ActorDeathPatch.cs' 'NobleRankService.OnActorDying(__instance)'
Require-Present 'titular succession inspects direct children only' `
    'Code/core/lineage/NobleRankService.cs' 'pHolder.getChildren(false)'
Require-Present 'titular succession uses stable eldest-son selection' `
    'Code/core/lineage/NobleRankService.cs' 'NobleRankRules.SelectEldestEligibleId(candidates)'
Require-Present 'titular succession preserves or upgrades the inherited rank' `
    'Code/core/lineage/NobleRankService.cs' 'NobleRankRules.ResultingInheritedRank('
Require-Present 'titular extinction closes the active grant' `
    'Code/core/lineage/NobleRankService.cs' '"extinct"'
Require-Present 'royal titular grants use a distinct Mandate decision' `
    'Code/core/lineage/MandateDecisionService.cs' 'aw_mandate_decision_grant_royal_titles'
Require-Present 'royal titular grant availability is delegated to its service' `
    'Code/core/lineage/MandateDecisionService.cs' 'NobleRankService.CanExecuteGreatRoyalGrant(pKingdom)'
Require-Present 'royal titular grant execution is delegated to its service' `
    'Code/core/lineage/MandateDecisionService.cs' 'NobleRankService.ExecuteGreatRoyalGrant(pKingdom) > 0'
Require-Present 'great royal grant availability requires a real grant plan' `
    'Code/core/lineage/NobleRankService.cs' 'BuildRoyalGrantPlan(pKingdom, pEmperor).Count > 0'
Require-Present 'great royal grant cannot consume the ruler marker for zero recipients' `
    'Code/core/lineage/NobleRankService.cs' 'if (planned.Count == 0)'
Require-Present 'great royal grant availability is cached by ruler-year' `
    'Code/core/lineage/NobleRankService.cs' 'NobleRankRules.ShouldReuseGreatGrantAvailability('
Require-Present 'great royal grants have a hard candidate cap' `
    'Code/core/lineage/NobleRankService.cs' 'public const int MaximumGreatGrantCandidates = 96;'
Require-Present 'great royal grants run once per ruler' `
    'Code/core/lineage/LineageKeys.cs' 'public const string NOBLE_GREAT_GRANT_RULER_ID = "aw_noble_great_grant_ruler_id";'
Require-Present 'great royal grants enumerate only the royal clan' `
    'Code/core/lineage/NobleRankService.cs' 'royalClan.units'
Require-Absent 'great royal grants cannot enumerate all world actors' `
    'Code/core/lineage/NobleRankService.cs' 'foreach (Actor actor in World.world.units'
Require-Present 'great royal grant decision title is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_decision_grant_royal_titles,'
Require-Present 'great royal grant decision description is localized' `
    'Locales/aw3_mandate.csv' 'aw_mandate_decision_grant_royal_titles_desc,'
Require-Absent 'individual noble ranks have no commandery prince tier' `
    'Locales/aw3_mandate.csv' 'aw_noble_rank_commandery_prince,'
Require-Present 'successful titular grants write chronicle history' `
    'Code/core/lineage/NobleRankService.cs' 'ChronicleEvents.OnNobleRankGranted('
Require-Present 'titular inheritance writes chronicle history' `
    'Code/core/lineage/NobleRankService.cs' 'ChronicleEvents.OnNobleRankInherited('
Require-Present 'titular extinction writes chronicle history' `
    'Code/core/lineage/NobleRankService.cs' 'ChronicleEvents.OnNobleRankExtinct('
Require-Present 'great royal grants write one kingdom summary' `
    'Code/core/lineage/NobleRankService.cs' 'ChronicleEvents.OnGreatRoyalGrant('
Require-Present 'live genealogy titles use the shared dynastic projection' `
    'Code/core/lineage/LineageQuery.cs' 'DynasticTitleService.ResolveLivingTitle(pLive)'
Require-Present 'noble-rank grant history label is registered' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_noble_rank_granted'
Require-Present 'noble-rank inheritance history label is localized' `
    'Locales/aw3_mandate.csv' 'aw_hist_event_noble_rank_inherited,'
Require-Present 'noble-rank extinction history label is localized' `
    'Locales/aw3_mandate.csv' 'aw_hist_event_noble_rank_extinct,'
Require-Present 'posthumous prose selects a life-summary sentence' `
    'Code/core/lineage/PosthumousTitleService.cs' 'CeremonialHistoryRules.LifeSummaryKey('
Require-Present 'posthumous prose resolves every selected ancient meaning' `
    'Code/core/lineage/PosthumousTitleService.cs' 'CeremonialHistoryRules.MeaningKeys('
Require-Present 'posthumous prose distinguishes temple and posthumous edicts' `
    'Code/core/lineage/PosthumousTitleService.cs' 'aw_hist_edict_temple_posthumous'
Require-Present 'abdicated rulers use a retirement edict' `
    'Code/core/lineage/PosthumousTitleService.cs' 'aw_hist_edict_abdication'
Require-Present 'deposed rulers use a censure edict' `
    'Code/core/lineage/PosthumousTitleService.cs' 'aw_hist_edict_deposed'
Require-Present 'posthumous prose preserves rich actor targets' `
    'Code/core/lineage/PosthumousTitleService.cs' 'HistoryText.Actor(pKing'
Require-Present 'posthumous prose preserves colored title targets' `
    'Code/core/lineage/PosthumousTitleService.cs' 'HistoryText.Colored(pDecision.DisplayTitle'
Require-Present 'titular grants use an investiture edict' `
    'Code/core/lineage/ChronicleEvents.cs' 'aw_hist_edict_noble_grant_as'
Require-Present 'great royal grants use one dynastic edict summary' `
    'Code/core/lineage/ChronicleEvents.cs' 'aw_hist_edict_great_royal_grant_prefix'
Require-Present 'landed fiefs use an enfeoffment edict' `
    'Code/core/lineage/FiefService.cs' 'aw_hist_edict_fief_grant_prefix'
Require-Absent 'landed fief history has no hard-coded Chinese prose' `
    'Code/core/lineage/FiefService.cs' '" \u53D7\u5C01\u4E8E"'
Require-Present 'court tier upgrades use a reform edict' `
    'Code/core/lineage/ChronicleEvents.cs' 'aw_hist_edict_court_tier_mid'
Require-Present 'school-led court reform uses a reform edict' `
    'Code/core/lineage/ChronicleEvents.cs' 'aw_hist_edict_court_reform_mid'
Require-Present 'Mandate accession uses a Heaven-received edict' `
    'Code/core/lineage/MandateService.cs' 'aw_hist_edict_mandate_claimed_mid'
Require-Present 'Mandate loss uses a complete abdication-of-Heaven edict' `
    'Code/core/lineage/MandateService.cs' 'aw_hist_edict_mandate_lost_suffix'
Require-Present 'Mandate collapse uses a disorder edict' `
    'Code/core/lineage/MandateService.cs' 'aw_hist_edict_mandate_collapse'
Require-Present 'era edict templates are localized' `
    'Locales/aw3_titles.csv' 'aw_hist_edict_accession_era,'
Require-Present 'noble and fief edicts are localized' `
    'Locales/aw3_mandate.csv' 'aw_hist_edict_fief_grant_prefix,'
Require-Present 'court reform edicts are localized' `
    'Locales/aw3_court.csv' 'aw_hist_edict_court_tier_mid,'
Require-Present 'monarch accession evaluates the accession-book rule' `
    'Code/core/lineage/ChronicleEvents.cs' 'CeremonialHistoryRules.ShouldWriteAccessionBook('
Require-Present 'republic state is passed into the accession-book gate' `
    'Code/core/lineage/ChronicleEvents.cs' 'RepublicGovernmentService.IsRepublic(pKingdom)'
Require-Present 'accession books enter the ruler biography' `
    'Code/core/lineage/ChronicleEvents.cs' 'PersonEvent.ACCESSION_BOOK'
Require-Present 'accession books enter kingdom history' `
    'Code/core/lineage/ChronicleEvents.cs' 'KingdomEvent.ACCESSION_BOOK'
Require-Present 'accession-book history preserves a rich actor target' `
    'Code/core/lineage/ChronicleEvents.cs' 'HistoryText.Actor(pNewKing)'
Require-Present 'accession-book history preserves a rich kingdom target' `
    'Code/core/lineage/ChronicleEvents.cs' 'HistoryText.Kingdom(pKingdom)'
Require-Present 'accession-book event label is localized' `
    'Locales/aw3_titles.csv' 'aw_hist_event_accession_book,'
Require-Present 'accession-book prose is localized' `
    'Locales/aw3_titles.csv' 'aw_hist_accession_book_prefix,'
Require-Present 'ministerial power evaluates the Nine Bestowments crossing' `
    'Code/core/court/MinisterialPowerService.cs' 'MinisterialPowerRules.ShouldGrantNineBestowments('
Require-Present 'Nine Bestowments use a durable actor hot flag' `
    'Code/core/court/MinisterialPowerService.cs' 'LineageKeys.MINISTERIAL_NINE_BESTOWMENTS_GRANTED'
Require-Present 'Nine Bestowments enter the minister biography' `
    'Code/core/court/MinisterialPowerService.cs' 'PersonEvent.NINE_BESTOWMENTS'
Require-Present 'Nine Bestowments enter kingdom history' `
    'Code/core/court/MinisterialPowerService.cs' 'KingdomEvent.NINE_BESTOWMENTS'
Require-Present 'Nine Bestowments event label is localized' `
    'Locales/aw3_court.csv' 'aw_hist_event_nine_bestowments,'
Require-Present 'Nine Bestowments prose is localized' `
    'Locales/aw3_court.csv' 'aw_hist_nine_bestowments_prefix,'
Require-Absent 'Nine Bestowments do not add actor traits' `
    'Code/core/court/MinisterialPowerService.cs' 'addTrait('
Require-Absent 'Nine Bestowments do not add status effects' `
    'Code/core/court/MinisterialPowerService.cs' 'addStatus'
Require-Absent 'Nine Bestowments do not create item assets' `
    'Code/core/court/MinisterialPowerService.cs' 'ItemAsset'
Require-Present 'committed official evaluations dispatch promotion edicts' `
    'Code/core/court/OfficialCareerStateService.cs' 'ChronicleEvents.OnOfficialRankPromoted('
Require-Present 'promotion edicts enter the official biography' `
    'Code/core/lineage/ChronicleEvents.cs' 'PersonEvent.OFFICIAL_APPOINTMENT_EDICT'
Require-Present 'promotion edicts preserve the promoted actor target' `
    'Code/core/lineage/ChronicleEvents.cs' 'aw_hist_official_edict_prefix'
Require-Present 'court cards render a compact joint title' `
    'Code/ui/items/CourtActorNodeView.cs' 'OfficialJointTitle(pKingdom, pNode, actor, compact: true)'
Require-Present 'two-action court cards use the narrow career label policy' `
    'Code/ui/items/CourtActorNodeView.cs' `
    'OfficialCareerRankRules.ComposeCardCareerLabel('
Require-Present 'two-action court cards lower the best-fit floor through pure policy' `
    'Code/ui/items/CourtActorNodeView.cs' `
    'OfficialCareerRankRules.CardCareerMinimumFontSize('
Require-Present 'court cards retain their stable total height' `
    'Code/ui/items/CourtActorNodeView.cs' 'public const float Height = 104f;'
Require-Present 'court card tooltips render the full joint title' `
    'Code/ui/items/CourtActorNodeView.cs' 'OfficialJointTitle(pKingdom, pNode, pActor, compact: false)'
Require-Present 'court cards hide joint titles before the Nine Rank system' `
    'Code/ui/items/CourtActorNodeView.cs' 'live && CanShowOfficialJointTitle(pKingdom, pNode)'
Require-Present 'court tooltips hide joint titles before the Nine Rank system' `
    'Code/ui/items/CourtActorNodeView.cs' 'OfficialJointTitle(pKingdom, pNode, pActor, compact: false)'
Require-Present 'joint titles include the live noble-title projection' `
    'Code/ui/items/CourtActorNodeView.cs' 'NobleRankService.GetDisplayTitle(pActor)'
Require-Present 'joint-title labels are localized' `
    'Locales/aw3_court.csv' 'aw_court_joint_title,'
Require-Present 'promotion-edict labels are localized' `
    'Locales/aw3_court.csv' 'aw_hist_event_official_appointment_edict,'
Require-Absent 'joint-title rendering cannot query SQLite' `
    'Code/ui/items/CourtActorNodeView.cs' 'SQLite'

Require-Present 'runtime pathfinding has a dedicated benchmark' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'RecentFeatureBenchmarkRules.PathfindingIndex'
Require-Present 'school runtime has a dedicated benchmark' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'RecentFeatureBenchmarkRules.SchoolsIndex'
Require-Present 'diplomacy response polling has a dedicated benchmark' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'RecentFeatureBenchmarkRules.DiplomacyIndex'
Require-Present 'deferred work has a dedicated recent-feature benchmark' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'RecentFeatureBenchmarkRules.DeferredWorkIndex'
Require-Present 'capture scanning has a dedicated recent-feature benchmark' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'RecentFeatureBenchmarkRules.CaptureScanIndex'
Require-Present 'school map refresh has a dedicated benchmark' `
    'Code/patch/AW_DeferredRuntimeWorkPatch.cs' 'RecentFeatureBenchmarkRules.SchoolMap'
Require-Present 'army formation callbacks have a dedicated benchmark' `
    'Code/patch/AW_ArmySafetyPatch.cs' 'RecentFeatureBenchmarkRules.ArmyMarch'
Require-Present 'wartime garrison work has a dedicated benchmark' `
    'Code/core/lineage/WartimeGarrisonService.cs' 'RecentFeatureBenchmarkRules.WartimeGarrison'
Require-Present 'recent feature benchmarks are accumulated without hot-path Bench dictionaries' `
    'Code/core/policy/RecentFeatureBenchmark.cs' 'Stopwatch.GetTimestamp()'
Require-Present 'recent feature benchmark totals do not double-count Benchmark All' `
    'Code/core/policy/RecentFeatureBenchmarkRules.cs' 'public const string TotalParentGroup = "aw3_recent_runtime_summary";'
Require-Present 'nested recent feature scopes report exclusive child costs' `
    'Code/core/policy/RecentFeatureBenchmark.cs' 'ExclusiveScopeTicks('
Require-Present 'parallel actor benchmark counters update atomically' `
    'Code/core/policy/RecentFeatureBenchmark.cs' 'Interlocked.Add(ref Ticks[pIndex], exclusive);'
Require-Present 'benchmark flush skips unsampled entries' `
    'Code/core/policy/RecentFeatureBenchmark.cs' 'RecentFeatureBenchmarkRules.ShouldSaveSample(count)'
Require-Present 'recent feature benchmark has its own debug panel' `
    'Code/content/RecentFeatureBenchmarkContent.cs' 'public const string ToolId = "AW3 Recent Runtime";'
Require-Present 'annual mobilization has a dedicated benchmark' `
    'Code/core/policy/KingdomAnnualWorkService.cs' 'RecentFeatureBenchmarkRules.KingdomMobilizationIndex'
Require-Present 'custom path movement has a dedicated benchmark' `
    'Code/patch/AW_GlobalPathfindingPatch.cs' 'RecentFeatureBenchmarkRules.PathMovementIndex'
Require-Present 'AW3 nameplate projection has a dedicated benchmark' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'RecentFeatureBenchmarkRules.NameplatesIndex'
Require-Present 'AW3 minimap projection has a dedicated benchmark' `
    'Code/patch/AW_HeirMinimapPatch.cs' 'RecentFeatureBenchmarkRules.MinimapMarkersIndex'
Require-Present 'AW3 map invalidation has a dedicated benchmark' `
    'Code/core/policy/FeudatoryMapModeService.cs' 'RecentFeatureBenchmarkRules.MapDirtyIndex'
Require-Present 'occupation acceleration has a dedicated benchmark' `
    'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'RecentFeatureBenchmarkRules.OccupationIndex'
Require-Present 'wartime garrison recruitment is deferred and coalesced' `
    'Code/core/lineage/WartimeGarrisonService.cs' 'EnqueueCoalesced('
Require-Absent 'wartime garrisons do not scan all world actors' `
    'Code/core/lineage/WartimeGarrisonService.cs' 'foreach (Actor actor in World.world.units'
Require-Present 'wartime garrisons are excluded from offensive army assignment' `
    'Code/patch/AW_StandingArmyPatch.cs' 'WartimeGarrisonService.ShouldBlockArmyAssignment'
Require-Present 'war notice issued history text resolves without leaking a key' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'new Entry("aw_hist_war_notice_issued_mid"'
Require-Present 'war notice received history text resolves without leaking a key' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'new Entry("aw_hist_war_notice_received_mid"'
Require-Present 'levy enlistment history text resolves without leaking a key' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'new Entry("aw_hist_temporary_levy_enlisted"'
Require-Present 'levy demobilization history text resolves without leaking a key' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'new Entry("aw_hist_temporary_levy_demobilized"'
Require-Present 'slave vanguard demobilization history text resolves without leaking a key' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'new Entry("aw_hist_temporary_slave_vanguard_demobilized"'

Require-Present 'term law has a kingdom hot key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_TERM_LAW = "aw_court_term_law";'
Require-Present 'term law has an independent change year' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_TERM_LAW_LAST_CHANGE_YEAR = "aw_court_term_law_last_change_year";'
Require-Present 'border command law has a kingdom hot key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_BORDER_COMMAND_LAW = "aw_court_border_command_law";'
Require-Present 'border command law has an independent change year' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_BORDER_COMMAND_LAW_LAST_CHANGE_YEAR = "aw_court_border_command_law_last_change_year";'
Require-Present 'appointment culture law has a kingdom hot key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_APPOINTMENT_CULTURE_LAW = "aw_court_appointment_culture_law";'
Require-Present 'appointment culture law has an independent change year' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_APPOINTMENT_CULTURE_LAW_LAST_CHANGE_YEAR = "aw_court_appointment_culture_law_last_change_year";'
Require-Present 'auxiliary law AI has a twelve-year throttle key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_AUXILIARY_LAW_AI_LAST_EVALUATION_YEAR = "aw_court_auxiliary_law_ai_last_evaluation_year";'
Require-Present 'auxiliary laws have a single change entry' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'TryChangeLaw('
Require-Present 'law changes spend through policy points' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'KingdomPolicyService.TrySpendPoliticalPoints('
Require-Present 'AI law changes preserve a political reserve' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'pAiInitiated ? PoliticalPointSpendingRules.CourtReserve : 0f'
Require-Present 'failed law mutation restores political points' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'KingdomPolicyService.RestorePoliticalPoints('
Require-Present 'policy points expose a narrow restore path' `
    'Code/core/policy/KingdomPolicyService.cs' 'internal static void RestorePoliticalPoints('
Require-Present 'law changes dispatch one kingdom history event' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'ChronicleEvents.OnCourtAuxiliaryLawChanged('
Require-Present 'law changes have a dedicated chronicle key' `
    'Code/core/lineage/ChronicleKeys.cs' 'COURT_AUXILIARY_LAW_CHANGED = "court_auxiliary_law_changed"'
Require-Present 'approved border petitions have a dedicated chronicle key' `
    'Code/core/lineage/ChronicleKeys.cs' 'BORDER_PETITION_APPROVED = "border_petition_approved"'
Require-Present 'law-change history preserves the ruler target' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnCourtAuxiliaryLawChanged('
Require-Present 'approved petition history preserves its participants' `
    'Code/core/lineage/ChronicleEvents.cs' 'OnBorderPetitionApproved('
Require-Present 'law-change history labels have fallback registration' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'new Entry("aw_hist_event_court_auxiliary_law_changed"'
Require-Present 'petition history labels have fallback registration' `
    'Code/core/lineage/HistoryLocalizationRules.cs' 'new Entry("aw_hist_event_border_petition_approved"'
Require-Absent 'auxiliary law AI cannot scan actors' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'World.world.units'
Require-Absent 'auxiliary law state cannot scan cities' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'foreach (City'
Require-Absent 'auxiliary law state cannot query SQLite' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'SQLite'
Require-Present 'new official terms obey the kingdom term law' `
    'Code/core/court/OfficialCareerStateService.cs' 'CourtAuxiliaryLawService.ResolveTermEndYear(pKingdom, age,'
Require-Present 'due official terms obey the current kingdom law' `
    'Code/core/court/OfficialCareerStateService.cs' 'CourtAuxiliaryLawService.ResolveTermEndYear(pKingdom,'
Require-Present 'non-nine-rank courts still renew expired terms' `
    'Code/core/court/OfficialCareerStateService.cs' 'RenewDueOfficial('
Require-Present 'lifetime migration recognizes only the explicit sentinel' `
    'Code/core/court/OfficialCareerStateService.cs' 'state.TermEndYear == int.MaxValue'
Require-Present 'lifetime migration uses the fixed annual budget' `
    'Code/core/court/OfficialCareerStateService.cs' 'CourtAuxiliaryLawRules.MaximumLifetimeMigrationsPerYear'
Require-Present 'lifetime migration processes officials in stable actor order' `
    'Code/core/court/OfficialCareerStateService.cs' 'orderedStates.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));'
$officialCareerSource = Read-Source 'Code/core/court/OfficialCareerStateService.cs'
$careerStateLoads = [regex]::Matches($officialCareerSource,
    'LoadKingdomStates\(pKingdom\.id\)').Count
if ($careerStateLoads -ne 1) {
    $failures.Add("official career annual work must load kingdom states exactly once: found $careerStateLoads")
}
Require-Present 'candidate scoring receives the target kingdom' `
    'Code/core/court/CourtService.cs' 'ScoreCandidate(Kingdom pKingdom, Actor pActor,'
Require-Present 'automatic appointments pass the target kingdom into scoring' `
    'Code/core/court/CourtService.cs' 'ScoreCandidate(pKingdom, actor, pOfficeId, pPreferredSchool,'
Require-Present 'manual appointments pass the target kingdom into scoring' `
    'Code/core/court/CourtService.cs' 'ScoreCandidate(kingdom, actor, pScan.office_id,'
Require-Present 'all appointment paths share the culture preference score' `
    'Code/core/court/CourtService.cs' 'CourtAuxiliaryLawService.AppointmentCultureScore(pKingdom, pActor)'
$courtServiceSource = Read-Source 'Code/core/court/CourtService.cs'
$appointmentCultureCalls = [regex]::Matches($courtServiceSource,
    'CourtAuxiliaryLawService\.AppointmentCultureScore\(').Count
if ($appointmentCultureCalls -ne 1) {
    $failures.Add("appointment culture must enter the shared candidate score exactly once: found $appointmentCultureCalls")
}
Require-Present 'school remains a sorting preference rather than eligibility' `
    'Code/core/court/CourtManualAppointmentRules.cs' 'public static bool IsSchoolEligible('
Require-Present 'border petitions have an annual gate key' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_BORDER_PETITION_LAST_YEAR = "aw_court_border_petition_last_year";'
Require-Present 'border petitions persist the vassal cursor' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_BORDER_VASSAL_CURSOR = "aw_court_border_vassal_cursor";'
Require-Present 'border petitions persist the central city cursor' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_BORDER_CITY_CURSOR = "aw_court_border_city_cursor";'
Require-Present 'border petitions persist each requester city cursor' `
    'Code/core/lineage/LineageKeys.cs' 'public const string COURT_BORDER_REQUEST_CITY_CURSOR = "aw_court_border_request_city_cursor";'
Require-Present 'border petition work uses its total candidate budget' `
    'Code/core/court/CourtBorderPetitionService.cs' 'CourtAuxiliaryLawRules.MaximumPetitionCandidatesPerYear'
Require-Present 'border petition work caps direct-vassal candidates' `
    'Code/core/court/CourtBorderPetitionService.cs' 'CourtAuxiliaryLawRules.MaximumVassalPetitionCandidatesPerYear'
Require-Present 'border petition work caps border-general candidates' `
    'Code/core/court/CourtBorderPetitionService.cs' 'CourtAuxiliaryLawRules.MaximumBorderGeneralCandidatesPerYear'
Require-Present 'vassal discovery has a fixed inspection budget' `
    'Code/core/court/CourtBorderPetitionService.cs' 'CourtAuxiliaryLawRules.MaximumVassalSlotsInspectedPerYear'
Require-Present 'vassal candidates use their hot suzerain id' `
    'Code/core/court/CourtBorderPetitionService.cs' 'VassalService.GetSuzerainId(candidate) == pSuzerain.id'
Require-Present 'petition strength never recursively scans vassals' `
    'Code/core/court/CourtBorderPetitionService.cs' 'VassalService.GetPowerScore(pSuzerain, pIncludeVassals: false)'
Require-Present 'approved petitions enter the diplomatic declaration queue' `
    'Code/core/court/CourtBorderPetitionService.cs' 'DiplomaticWarDeclarationService.Issue(pSuzerain, option)'
Require-Present 'auxiliary annual work dispatches bounded petitions' `
    'Code/core/court/CourtAuxiliaryLawService.cs' 'CourtBorderPetitionService.OnKingdomYear(pKingdom);'
Require-Absent 'petition work cannot enumerate vassal trees' `
    'Code/core/court/CourtBorderPetitionService.cs' 'VassalService.GetVassals'
Require-Absent 'petition work cannot load all active generals' `
    'Code/core/court/CourtBorderPetitionService.cs' 'GetActiveGenerals'
Require-Absent 'petition work cannot scan world actors' `
    'Code/core/court/CourtBorderPetitionService.cs' 'World.world.units'
Require-Absent 'petition work cannot query SQLite' `
    'Code/core/court/CourtBorderPetitionService.cs' 'SQLite'
Require-Absent 'petition work cannot allocate LINQ orderings' `
    'Code/core/court/CourtBorderPetitionService.cs' '.OrderBy('
Require-Present 'auxiliary law work has an append-only benchmark index' `
    'Code/core/policy/RecentFeatureBenchmarkRules.cs' 'public const int KingdomAuxiliaryLawsIndex = 19;'
Require-Present 'existing occupation benchmark index remains stable' `
    'Code/core/policy/RecentFeatureBenchmarkRules.cs' 'public const int OccupationIndex = 18;'
Require-Present 'auxiliary law work has a dedicated benchmark id' `
    'Code/core/policy/RecentFeatureBenchmarkRules.cs' 'public const string KingdomAuxiliaryLaws = "aw3_year_auxiliary_laws";'
Require-Present 'kingdom annual work benchmarks auxiliary laws' `
    'Code/core/policy/KingdomAnnualWorkService.cs' 'RecentFeatureBenchmarkRules.KingdomCourtAuxiliaryIndex'
Require-Present 'auxiliary laws own a dedicated window id' `
    'Code/ui/AW_LineageWindowIds.cs' 'COURT_AUXILIARY_LAWS = "aw_court_auxiliary_laws"'
Require-Present 'auxiliary law window defaults to 580 by 360' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'DefaultSize = new Vector2(580f, 360f);'
Require-Present 'auxiliary law window remains usable at 420 by 280' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'MinimumSize = new Vector2(420f, 280f);'
Require-Present 'auxiliary law window uses the shared resize chrome' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'WideWindowChrome.Attach('
Require-Present 'auxiliary law minimum view keeps a visible scrollbar' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'ScrollRect.ScrollbarVisibility.Permanent'
Require-Present 'term law has its own UI section' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'CreateLawSection(CourtAuxiliaryLawKind.Term, 4)'
Require-Present 'border command has its own UI section' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'CreateLawSection(CourtAuxiliaryLawKind.BorderCommand, 3)'
Require-Present 'appointment culture has its own UI section' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'CreateLawSection(CourtAuxiliaryLawKind.AppointmentCulture, 3)'
Require-Present 'each law section owns an apply control' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'section.ApplyButton = CreateButton('
Require-Present 'successful law changes refresh the live window' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'CourtAuxiliaryLawChangeResult.Success'
Require-Present 'auxiliary law results use explicit snake case keys' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' `
    'CourtAuxiliaryLawChangeResult.InvalidKingdom => "aw_court_aux_result_invalid_kingdom"'
Require-Absent 'auxiliary law result keys are not inferred from enum casing' `
    'Code/ui/windows/CourtAuxiliaryLawWindow.cs' 'pResult.ToString().ToLowerInvariant()'
Require-Present 'policy summary opens the auxiliary-law window' `
    'Code/ui/windows/KingdomPolicyWindow.cs' 'CourtAuxiliaryLawWindow.Open(pKingdom.id);'
Require-Present 'auxiliary law entry is localized' `
    'Locales/aw3_court.csv' 'aw_court_auxiliary_laws_entry,'
Require-Present 'auxiliary law apply command is localized' `
    'Locales/aw3_court.csv' 'aw_court_aux_apply,'
Require-Present 'term law title is localized' `
    'Locales/aw3_court.csv' 'aw_court_aux_law_term,'
Require-Present 'border law title is localized' `
    'Locales/aw3_court.csv' 'aw_court_aux_law_border,'
Require-Present 'appointment law title is localized' `
    'Locales/aw3_court.csv' 'aw_court_aux_law_appointment,'
Require-Present 'law cooldown feedback is localized' `
    'Locales/aw3_court.csv' 'aw_court_aux_result_cooldown,'
Require-Present 'law history event title is localized' `
    'Locales/aw3_court.csv' 'aw_hist_event_court_auxiliary_law_changed,'
Require-Present 'petition history event title is localized' `
    'Locales/aw3_court.csv' 'aw_hist_event_border_petition_approved,'

$auxiliaryLawLocaleKeys = @(
    'aw_court_auxiliary_laws_title',
    'aw_court_auxiliary_laws_entry',
    'aw_court_auxiliary_laws_entry_desc',
    'aw_court_aux_apply',
    'aw_court_aux_points',
    'aw_court_aux_cost',
    'aw_court_aux_current',
    'aw_court_aux_ready',
    'aw_court_aux_cooldown',
    'aw_court_aux_years',
    'aw_court_aux_hint',
    'aw_court_aux_law_term',
    'aw_court_aux_law_border',
    'aw_court_aux_law_appointment',
    'aw_court_term_lifetime',
    'aw_court_term_lifetime_desc',
    'aw_court_term_three',
    'aw_court_term_three_desc',
    'aw_court_term_dynamic',
    'aw_court_term_dynamic_desc',
    'aw_court_term_nine',
    'aw_court_term_nine_desc',
    'aw_court_border_discretionary',
    'aw_court_border_discretionary_desc',
    'aw_court_border_petition',
    'aw_court_border_petition_desc',
    'aw_court_border_centralized',
    'aw_court_border_centralized_desc',
    'aw_court_appointment_merit',
    'aw_court_appointment_merit_desc',
    'aw_court_appointment_preference',
    'aw_court_appointment_preference_desc',
    'aw_court_appointment_centered',
    'aw_court_appointment_centered_desc',
    'aw_court_aux_result_success',
    'aw_court_aux_result_invalid_kingdom',
    'aw_court_aux_result_invalid_choice',
    'aw_court_aux_result_unchanged',
    'aw_court_aux_result_insufficient_points',
    'aw_court_aux_result_cooldown',
    'aw_court_aux_result_persistence_failed',
    'aw_hist_event_court_auxiliary_law_changed',
    'aw_hist_event_border_petition_approved',
    'aw_hist_court_auxiliary_law_changed_mid',
    'aw_hist_court_auxiliary_law_from',
    'aw_hist_court_auxiliary_law_to',
    'aw_hist_border_petition_source_mid',
    'aw_hist_border_petition_target_mid',
    'aw_hist_border_petition_reason_mid',
    'aw_hist_border_petition_suffix',
    'aw3_year_auxiliary_laws'
)
foreach ($localeKey in $auxiliaryLawLocaleKeys) {
    Require-Present "auxiliary law locale key $localeKey is present" `
        'Locales/aw3_court.csv' ($localeKey + ',')
}

Require-Present 'conferred titles own a dedicated decision constructor' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'ForConferred('
Require-Present 'conferred cooldowns load in one database query' `
    'Code/core/lineage/ConferredPosthumousTitleQuery.cs' `
    'public IReadOnlyDictionary<long, double> ReadLastConferredTimes()'
Require-Present 'conferred AI cooldown cache is loaded once per archive' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'EnsureCooldownCacheLoaded();'
Require-Present 'conferred AI candidates share one cooldown snapshot' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'pKingdomId, candidate.ActorId, cooldown);'
Require-Present 'conferred AI commits the already validated preview' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'CommitPrepared(best, ConferredPosthumousSource.Ai);'
Require-Present 'conferred candidate reads are bounded at 96' `
    'Code/core/lineage/ConferredPosthumousTitleRules.cs' `
    'public const int MaximumCandidates = 96;'
Require-Present 'conferred full evaluations are bounded at eight' `
    'Code/core/lineage/ConferredPosthumousTitleRules.cs' `
    'public const int MaximumFullEvaluations = 8;'
Require-Present 'conferred realm cooldown is five years' `
    'Code/core/lineage/ConferredPosthumousTitleRules.cs' `
    'public const int CooldownYears = 5;'
Require-Present 'conferred candidate sources use indexed union-all branches' `
    'Code/core/lineage/ConferredPosthumousTitleQuery.cs' 'UNION ALL'
Require-Present 'conferred cooldown has a realm-kind-time index' `
    'Code/core/db/LineageArchiveIndexRules.cs' `
    'idx_PosthumousTitle_kingdom_kind_time'
Require-Present 'conferred title idempotency has a unique actor-realm index' `
    'Code/core/db/LineageArchiveIndexRules.cs' `
    'uq_PosthumousTitle_conferred_actor_kingdom'
Require-Present 'conferred decisions use the explicit title kind' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'TitleKind = "conferred"'
Require-Present 'title commit validates identity through shared conferred rules' `
    'Code/core/lineage/RulerTitleCommitService.cs' `
    'ConferredPosthumousTitleRules.CanCommitIdentity('
Require-Present 'conferred replay has actor-realm idempotency' `
    'Code/core/lineage/RulerTitleCommitService.cs' 'TryReadExistingConferred('
Require-Present 'unknown Shi skips only the conferred registry reservation' `
    'Code/core/lineage/RulerTitleCommitService.cs' `
    'ConferredPosthumousTitleRules.ShouldReserveShiTitle('
Require-Present 'conferred history selects its dedicated event type' `
    'Code/core/lineage/RulerTitleCommitService.cs' `
    'ConferredPosthumousTitleRules.HistoryEventType('
Require-Present 'person chronicle defines conferred posthumous history' `
    'Code/core/lineage/ChronicleKeys.cs' `
    'public const string CONFERRED_POSTHUMOUS = "conferred_posthumous";'
Require-Present 'kingdom chronicle defines conferred posthumous history' `
    'Code/core/lineage/ChronicleKeys.cs' `
    'public const string CONFERRED_POSTHUMOUS = "conferred_posthumous";'
Require-Present 'conferred service exposes one preview path' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'public static ConferredPosthumousPreview Prepare('
Require-Present 'conferred service exposes one commit path' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'public static ConferredPosthumousCommitResult TryCommit('
Require-Present 'conferred service commits through the authoritative transaction' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'RulerTitleCommitService.Commit(decision)'
Require-Present 'conferred AI uses one coalesced persistent work item' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'conferred AI full evaluation is capped by shared rules' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'ConferredPosthumousTitleRules.FullEvaluationCount('
Require-Absent 'conferred service cannot scan world actors' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' 'World.world.units'
foreach ($conferredBoundedFile in @(
    'Code/core/lineage/ConferredPosthumousTitleQuery.cs',
    'Code/core/lineage/ConferredPosthumousTitleFactService.cs',
    'Code/core/lineage/ConferredPosthumousTitleService.cs'
)) {
    Require-Absent "conferred bounded path cannot enumerate world actors in $conferredBoundedFile" `
        $conferredBoundedFile 'World.world.units'
    Require-Absent "conferred bounded path cannot loop raw actors in $conferredBoundedFile" `
        $conferredBoundedFile 'foreach (Actor'
    Require-Absent "conferred bounded path cannot use LINQ OrderBy in $conferredBoundedFile" `
        $conferredBoundedFile '.OrderBy('
    Require-Absent "conferred bounded path cannot scan every dead archive row in $conferredBoundedFile" `
        $conferredBoundedFile 'SELECT * FROM ActorArchive WHERE IS_ALIVE=0'
    Require-Absent "conferred bounded path cannot spend political points in $conferredBoundedFile" `
        $conferredBoundedFile 'TrySpendPoliticalPoints'
    Require-Absent "conferred bounded path cannot insert title rows in $conferredBoundedFile" `
        $conferredBoundedFile 'INSERT INTO PosthumousTitle'
}
Require-Absent 'conferred service never spends political points' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    'TrySpendPoliticalPoints'
Require-Present 'conferred annual work is wired once' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'ConferredPosthumousTitleService.OnKingdomYear(pKingdom);'
Require-OccurrenceCount 'conferred annual work has exactly one runtime call site' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'ConferredPosthumousTitleService.OnKingdomYear(pKingdom);' 1
Require-Present 'conferred runtime state clears on archive switch' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'ConferredPosthumousTitleService.ClearRuntime'
Require-OccurrenceCount 'conferred runtime state has exactly one archive reset call' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'ConferredPosthumousTitleService.ClearRuntime' 1
Require-Present 'conferred benchmark index is append-only' `
    'Code/core/policy/RecentFeatureBenchmarkRules.cs' `
    'public const int ConferredPosthumousIndex = 20;'
Require-Present 'conferred benchmark owns a stable id' `
    'Code/core/policy/RecentFeatureBenchmarkRules.cs' `
    'public const string ConferredPosthumous = "aw3_year_conferred_posthumous";'
Require-Present 'person biography carries an explicit granting realm' `
    'Code/ui/windows/HistoryListWindow.cs' `
    'OpenPerson(long pActorId, long pConferKingdomId = -1L)'
Require-Present 'person biography stores the granting realm separately' `
    'Code/ui/windows/HistoryListWindow.cs' '_personConferKingdomId'
Require-Present 'kingdom history passes its realm into ruler biography' `
    'Code/ui/windows/HistoryListWindow.cs' `
    'action_kingdom_id = _contextId'
Require-Present 'history rows carry action realm context' `
    'Code/core/lineage/LineageDTO.cs' 'public long   action_kingdom_id = -1;'
Require-Present 'history actions default to enabled' `
    'Code/core/lineage/LineageDTO.cs' 'public bool   action_enabled = true;'
Require-Present 'history list dispatches conferred actions explicitly' `
    'Code/ui/items/HistoryListItem.cs' 'OnConferredPosthumous'
Require-Present 'disabled history actions cannot execute' `
    'Code/ui/items/HistoryListItem.cs' 'if (!_actionEnabled) return;'
Require-Present 'biography renders one conferred status snapshot' `
    'Code/ui/windows/HistoryListWindow.cs' `
    'ConferredPosthumousTitleService.Prepare('
Require-Present 'successful conferment refreshes the existing biography' `
    'Code/ui/windows/HistoryListWindow.cs' `
    'RefreshPersonAfterConferment('
Require-Present 'conferred titles own a dedicated window id' `
    'Code/ui/AW_LineageWindowIds.cs' `
    'CONFERRED_POSTHUMOUS = "aw_conferred_posthumous"'
Require-Present 'conferred window defaults to 520 by 340' `
    'Code/ui/windows/ConferredPosthumousTitleWindow.cs' `
    'DefaultSize = new Vector2(520f, 340f);'
Require-Present 'conferred window remains usable at 420 by 280' `
    'Code/ui/windows/ConferredPosthumousTitleWindow.cs' `
    'MinimumSize = new Vector2(420f, 280f);'
Require-Present 'conferred window uses shared resize chrome' `
    'Code/ui/windows/ConferredPosthumousTitleWindow.cs' 'WideWindowChrome.Attach('
Require-Present 'conferred window rebuilds archived portraits' `
    'Code/ui/windows/ConferredPosthumousTitleWindow.cs' `
    'FamilyTreeNodeView.BuildArchivedPortrait('
Require-Present 'conferred window keeps a permanent scrollbar' `
    'Code/ui/windows/ConferredPosthumousTitleWindow.cs' `
    'ScrollRect.ScrollbarVisibility.Permanent'
Require-Present 'conferred window submits the preview token through a command' `
    'Code/ui/windows/ConferredPosthumousTitleWindow.cs' `
    'AW3CommandRequest.ConferPosthumousTitle('
Require-Present 'authoritative records handler commits the conferred preview token' `
    'Code/core/multiplayer/commands/AW3RecordsCommandHandler.cs' `
    'ConferredPosthumousTitleService.TryCommit('
Require-Present 'conferred history event has fallback registration' `
    'Code/core/lineage/HistoryLocalizationRules.cs' `
    'new Entry("aw_hist_event_conferred_posthumous"'
Require-Present 'conferred edict has fallback registration' `
    'Code/core/lineage/HistoryLocalizationRules.cs' `
    'new Entry("aw_hist_conferred_posthumous_edict"'
Require-Absent 'conferred role locale keys are not inferred at runtime' `
    'Code/core/lineage/ConferredPosthumousTitleService.cs' `
    '"aw_conferred_role_" + role'
$conferredLocaleKeys = @(
    'aw_hist_event_conferred_posthumous',
    'aw_hist_conferred_posthumous_edict',
    'aw3_year_conferred_posthumous',
    'aw_conferred_window_title',
    'aw_conferred_action',
    'aw_conferred_action_desc',
    'aw_conferred_unavailable',
    'aw_conferred_confirm',
    'aw_conferred_unknown_actor',
    'aw_conferred_relationship',
    'aw_conferred_proposed',
    'aw_conferred_title_meaning',
    'aw_conferred_major_deeds',
    'aw_conferred_highest_office',
    'aw_conferred_noble_title',
    'aw_conferred_none',
    'aw_conferred_existing',
    'aw_conferred_deeds_civil',
    'aw_conferred_deeds_military',
    'aw_conferred_deeds_tenure',
    'aw_conferred_role_former_king',
    'aw_conferred_role_royal_clan',
    'aw_conferred_role_general',
    'aw_conferred_role_official',
    'aw_conferred_result_invalid_kingdom',
    'aw_conferred_result_missing_context',
    'aw_conferred_result_missing_archive',
    'aw_conferred_result_target_living',
    'aw_conferred_result_no_relationship',
    'aw_conferred_result_already_titled',
    'aw_conferred_result_cooldown',
    'aw_conferred_result_no_title',
    'aw_conferred_result_stale',
    'aw_conferred_result_persistence_failed',
    'aw_conferred_result_unavailable'
)
foreach ($localeKey in $conferredLocaleKeys) {
    Require-Present "conferred locale key $localeKey is present" `
        'Locales/aw3_titles.csv' ($localeKey + ',')
}

Require-Present 'family tree branch Shi label is localized' `
    'Locales/aw3_titles.csv' 'aw_branch_shi_label,'

Require-Present 'succession war CB validates the permanent split pair and ruler generation' `
    'Code/core/lineage/WarDecisionService.cs' `
    'SuccessionDisputeService.CanDeclareReunification('
Require-Present 'diplomatic war options expose the bounded reunification goal' `
    'Code/core/lineage/WarTerritoryService.cs' `
    'GOAL_REUNIFY_SUCCESSION'
Require-Present 'diplomatic declaration executes succession reunification through its owner service' `
    'Code/core/lineage/DiplomaticWarDeclarationService.cs' `
    'SuccessionDisputeService.TryDeclareReunificationWar('
Require-Present 'permanent split war settlement uses the original kingdom identity' `
    'Code/core/lineage/SuccessionDisputeService.cs' `
    'SettleReunification(snapshot, winnerKingdomId'
Require-Present 'succession dispute history labels are localized' `
    'Locales/aw3_court.csv' 'aw_hist_event_succession_dispute_started,'
Require-Present 'succession reunification history labels are localized' `
    'Locales/aw3_court.csv' 'aw_hist_event_succession_reunified,'
Require-Present 'heir selection changes write a biography event' `
    'Code/core/lineage/HeirService.cs' `
    'ChronicleEvents.OnHeirDesignated('
Require-Present 'heir designation history is localized' `
    'Locales/aw3_court.csv' 'aw_hist_event_heir_designated,'
Require-Present 'inheritance window shows the cached succession dispute state' `
    'Code/ui/windows/InheritanceLawWindow.cs' `
    'SuccessionDisputeService.TryGetMaterializedByKingdom('
foreach ($inheritanceFactionKey in @(
    'aw_inheritance_faction_imperial',
    'aw_inheritance_faction_military',
    'aw_inheritance_faction_civil')) {
    Require-Present "inheritance score bar uses faction label $inheritanceFactionKey" `
        'Code/ui/windows/InheritanceLawWindow.cs' `
        ('AW_L10n.Text("' + $inheritanceFactionKey + '"')
    Require-Present "inheritance faction label $inheritanceFactionKey is localized" `
        'Locales/aw3_court.csv' ($inheritanceFactionKey + ',')
}
Require-Present 'inheritance window consumes compact tested dimensions' `
    'Code/ui/windows/InheritanceLawWindow.cs' `
    'InheritanceLawRules.DefaultWindowWidth'
Require-Present 'inheritance window scrolls compact content instead of overflowing' `
    'Code/ui/windows/InheritanceLawWindow.cs' `
    'native.vertical = requiresScroll;'
Require-Present 'chronicle rows consume compact tested typography' `
    'Code/ui/items/HistoryListItem.cs' `
    'HistoryListLayoutRules.BodyFontSize'
Require-Present 'court appointment history uses the explicit no-school label' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'AW_L10n.Text("aw_court_school_none", "No school")'
Require-Present 'diplomacy selector consumes bounded layout rules' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'DiplomacyConversationRules.SecondarySelectorHeight'
foreach ($heirTitleKey in @(
    'aw_heir_shizi', 'aw_heir_taizi', 'aw_heir_liuhou', 'aw_heir_sijun')) {
    Require-Present "history localization resolves $heirTitleKey" `
        'Code/core/lineage/HistoryLocalizationRules.cs' `
        ('new Entry("' + $heirTitleKey + '"')
}
foreach ($selectorLocale in @(
    'aw_diplomacy_selector_coalition_prompt,',
    'aw_diplomacy_selector_marriage_prompt,',
    'aw_diplomacy_selector_forgery_prompt,',
    'aw_diplomacy_selector_spy_prompt,')) {
    Require-Present "diplomacy selector locale $selectorLocale" `
        'Locales/aw3_diplomacy.csv' $selectorLocale
}
Require-Present 'succession reunification generation has a kingdom hot field' `
    'Code/core/lineage/LineageKeys.cs' `
    'SUCCESSION_REUNIFICATION_GENERATION'
Require-Present 'succession generation UI reads the hot projection' `
    'Code/core/lineage/SuccessionDisputeService.cs' `
    'LineageKeys.SUCCESSION_REUNIFICATION_GENERATION'
Require-Absent 'inheritance window never executes SQLite during refresh' `
    'Code/ui/windows/InheritanceLawWindow.cs' 'SQLiteCommand'
Require-Present 'inheritance dispute status is localized' `
    'Locales/aw3_court.csv' 'aw_inheritance_dispute_none,'
Require-Present 'succession display projection reads only materialized runtime state' `
    'Code/core/lineage/SuccessionDisputeService.cs' `
    'if (!TryGetMaterializedByKingdom(pKingdom.id,'
Require-Present 'AW3 map mode tooltips use projected succession realm names' `
    'Code/patch/AW_MapModeTooltipPatch.cs' `
    'SuccessionDisputeService.GetDisplayName(pKingdom)'
Require-Present 'war peace participant cards use projected succession realm names' `
    'Code/ui/windows/WarPeaceNegotiationController.cs' `
    'SuccessionDisputeService.GetDisplayName(pKingdom)'
Require-Present 'diplomatic kingdom list uses the unified state-name projection' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'RulerAppellationService.GetProjectedStateName(other)'
Require-Present 'materialized succession courts commit the canonical live kingdom name' `
    'Code/core/lineage/SuccessionDisputeService.cs' 'CommitCanonicalNames(row);'
Require-Absent 'succession rebuild canonical repair is not gated by territorial materialization' `
    'Code/core/lineage/SuccessionDisputeService.cs' `
    'if (row.Materialized) CommitCanonicalNames(row)'
Require-Present 'succession live-name writes explicitly use the canonical name rule' `
    'Code/core/lineage/SuccessionDisputeService.cs' `
    'CanonicalNameForLiveCommit(pRow.OriginalStateName)'
Require-Present 'prepared succession rebuild can adopt an already-created rival' `
    'Code/core/lineage/SuccessionDisputeService.cs' `
    'claimant.kingdom == seed.kingdom &&'
Require-Present 'failed succession war binding compensates the runtime war' `
    'Code/core/lineage/SuccessionDisputeService.cs' 'EndUnboundWar(war)'
Require-Absent 'succession qualifiers never rewrite canonical Shi state names' `
    'Code/core/lineage/SuccessionDisputeService.cs' 'SetBoundStateName('
Require-Present 'succession paternal veto traverses archived dead fathers' `
    'Code/core/lineage/SuccessionDisputeService.cs' `
    'LineageQuery.GetFatherId)'
Require-Absent 'succession paternal veto does not stop at unresolved live parent objects' `
    'Code/core/lineage/SuccessionDisputeService.cs' `
    'foreach (long parentId in new[]'

Require-Present 'royal marriage query uses the indexed realm-lineage predicate' `
    'Code/core/lineage/DiplomaticMarriageQuery.cs' `
    'AND KINGDOM_ID=@kingdom AND IS_ALIVE=1'
Require-Present 'royal marriage archive scan uses its bounded over-read cap' `
    'Code/core/lineage/DiplomaticMarriageQuery.cs' `
    'MaximumRoyalArchiveIdsScannedPerRealm'
Require-Present 'resolved royal marriage candidates retain the UI pool cap' `
    'Code/core/lineage/DiplomaticMarriageService.cs' `
    '.MaximumRoyalCandidatesPerRealm'
Require-Present 'royal marriage commits through the original lover API' `
    'Code/core/lineage/DiplomaticMarriageService.cs' 'becomeLoversWith('
Require-Present 'royal marriage history imports the shared localization helper' `
    'Code/core/lineage/DiplomaticMarriageService.cs' `
    'using AncientWarfare3.ui;'
Require-Present 'royal marriage history event has a localized fallback' `
    'Code/core/lineage/HistoryLocalizationRules.cs' `
    'new Entry("aw_hist_event_royal_marriage"'
Require-Absent 'royal marriage cannot rebuild original family objects' `
    'Code/core/lineage/DiplomaticMarriageService.cs' 'newFamily('
Require-Absent 'royal marriage cannot scan all living actors' `
    'Code/core/lineage/DiplomaticMarriageService.cs' 'units_only_alive'
Require-Absent 'royal marriage cannot enumerate the world actor manager' `
    'Code/core/lineage/DiplomaticMarriageService.cs' 'foreach (Actor'
Require-Present 'diplomacy proposal serializes royal marriage' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'DiplomacyProposalType.RoyalMarriage => "royal_marriage"'
Require-Present 'diplomacy proposal parses royal marriage' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    '"royal_marriage" => DiplomacyProposalType.RoyalMarriage'
Require-Present 'proposal persistence carries the requester marriage actor' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'ColumnVal.Create("REQUESTER_ACTOR_ID"'
Require-Present 'proposal persistence carries the responder marriage actor' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'ColumnVal.Create("RESPONDER_ACTOR_ID"'
Require-Present 'royal marriage has a visible diplomacy action name' `
    'Code/core/lineage/DiplomacyConversationService.cs' `
    'DiplomacyProposalType.RoyalMarriage => AW_L10n.Text('
Require-Present 'royal marriage letters resolve the fixed requester actor' `
    'Code/core/lineage/DiplomacyConversationService.cs' `
    'LineageQuery.GetActorDisplayName(pProposal.RequesterActorId)'
Require-Present 'royal marriage letters resolve the fixed responder actor' `
    'Code/core/lineage/DiplomacyConversationService.cs' `
    'LineageQuery.GetActorDisplayName(pProposal.ResponderActorId)'
Require-Present 'royal marriage letters localize missing candidates' `
    'Code/core/lineage/DiplomacyConversationService.cs' `
    'aw_diplomacy_marriage_unknown_candidate'
Require-Present 'royal marriage maintenance is bounded per realm' `
    'Code/core/lineage/DiplomaticMarriageService.cs' `
    'LIMIT @limit'
Require-Present 'royal marriage maintenance resumes after its prior row' `
    'Code/core/lineage/DiplomaticMarriageService.cs' `
    'MARRIAGE_ID>@cursor'
Require-Present 'royal marriage maintenance persists its bounded cursor' `
    'Code/core/lineage/DiplomaticMarriageService.cs' `
    'LineageKeys.DIPLOMACY_MARRIAGE_CURSOR'
Require-Present 'royal marriage ledger and opinion commit together' `
    'Code/core/lineage/DiplomaticMarriageService.cs' `
    'using SQLiteTransaction transaction = DB.BeginTransaction();'
Require-Present 'royal marriage modifier participates in its transaction' `
    'Code/core/lineage/DiplomaticMarriageService.cs' `
    'DiplomaticRelationModifierService.Upsert(transaction,'
Require-Present 'stale royal marriage disables its opinion modifier' `
    'Code/core/lineage/DiplomaticMarriageService.cs' `
    'DiplomaticRelationModifierService.DeactivateSource('
Require-Present 'annual kingdom work validates active royal marriages' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'DiplomaticMarriageService.OnKingdomYear(pKingdom)'

Require-Present 'coalition service uses the recorded third-country target' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' `
    'pProposal.TargetKingdomId'
Require-Absent 'a coalition cannot be consumed by its first joined war' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' `
    'JOINED_WAR_ID<0'
Require-Present 'coalition war reads are bounded' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' 'LIMIT @limit'
Require-Absent 'coalition maintenance cannot strand old active rows' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' 'END_YEAR>=@minimum'
Require-Present 'accepted coalitions add their relation modifier' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' `
    'DiplomaticRelationModifierService.Upsert('
Require-Present 'coalition ledger and opinion commit together' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' `
    'using SQLiteTransaction transaction = DB.BeginTransaction();'
Require-Absent 'coalitions cannot create an ordinary alliance' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' 'newAlliance('
Require-Absent 'coalitions cannot force an ordinary alliance' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' 'forceAlliance('
Require-Absent 'coalitions cannot mutate normal alliance membership' `
    'Code/core/lineage/DiplomaticCoalitionService.cs' 'getAlliance('
Require-Present 'the authoritative war-created hook triggers coalition support' `
    'Code/patch/AW_WarPatch.cs' 'DiplomaticCoalitionService.OnWarStarted(__result);'
Require-Present 'coalition proposals serialize their append-only type' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'DiplomacyProposalType.Coalition => "coalition"'
Require-Present 'coalition proposals parse their append-only type' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    '"coalition" => DiplomacyProposalType.Coalition'

Require-Present 'covert operations dequeue one indexed due row' `
    'Code/core/lineage/DiplomaticOperationService.cs' `
    'WHERE STATUS=0 AND DUE_TIME<=@now ORDER BY DUE_TIME,OPERATION_ID LIMIT 1'
Require-Present 'covert dequeue claims work in a transaction' `
    'Code/core/lineage/DiplomaticOperationService.cs' 'BeginTransaction()'
Require-Present 'covert dequeue conditionally claims a pending row' `
    'Code/core/lineage/DiplomaticOperationService.cs' `
    'WHERE OPERATION_ID=@id AND STATUS=0'
Require-Present 'covert resolution excludes its own processing row' `
    'Code/core/lineage/DiplomaticOperationService.cs' `
    'OPERATION_ID<>@ignore'
Require-Present 'forgery spends spy points through the atomic purchase ledger' `
    'Code/core/lineage/DiplomaticOperationService.cs' `
    'SpyNetworkPointLedger.TryPurchase('
Require-Present 'forgery creates its claim inside the purchase transaction' `
    'Code/core/lineage/DiplomaticOperationService.cs' `
    'WarDecisionService.TryCreateClaimInTransaction('
Require-Absent 'covert operations cannot write war claims directly' `
    'Code/core/lineage/DiplomaticOperationService.cs' 'WarClaim'
Require-Absent 'covert operations cannot create war projects directly' `
    'Code/core/lineage/DiplomaticOperationService.cs' 'CreateProject('
Require-Absent 'covert operations cannot scan all kingdoms' `
    'Code/core/lineage/DiplomaticOperationService.cs' 'World.world.kingdoms'
Require-Absent 'covert operations cannot scan all actors' `
    'Code/core/lineage/DiplomaticOperationService.cs' 'units_only_alive'
Require-Present 'the deferred frame loop resolves covert work' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'DiplomaticOperationService.ProcessFrame'
Require-Present 'annual kingdom work only signals bounded covert work' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'DiplomaticOperationService.OnKingdomYear(pKingdom)'
Require-Present 'world switches reset covert processing state' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'DiplomaticOperationService.ResetRuntime'
Require-Present 'covert frame polling is gated by the earliest due time' `
    'Code/core/lineage/DiplomaticOperationService.cs' '_nextDueTime'

Require-Present 'diplomatic modifier reads use the normalized pair cache' `
    'Code/core/lineage/DiplomaticRelationModifierService.cs' `
    'public static int ReadCached('
Require-Present 'diplomatic modifier cache loads through the pair index' `
    'Code/core/lineage/DiplomaticRelationModifierService.cs' `
    'KINGDOM_A_ID=@a AND KINGDOM_B_ID=@b AND ACTIVE=1 AND UNTIL_YEAR>=@year'
Require-Present 'diplomatic modifier writes invalidate only their pair' `
    'Code/core/lineage/DiplomaticRelationModifierService.cs' `
    'Cache.Invalidate(pKingdomA, pKingdomB);'
Require-Present 'corrected vanilla opinion includes cached AW3 modifiers' `
    'Code/core/lineage/DiplomacyOpinionService.cs' `
    'DiplomaticRelationModifierService.ReadCached('
Require-Present 'world switches clear diplomatic relation cache' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'DiplomaticRelationModifierService.ClearRuntime'
Require-Present 'world switches clear political-point reservations' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'PoliticalPointReservationService.Clear'
Require-Present 'archive loads rebuild committed era projections' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'YearNameService.RebuildCommittedProjections'
Require-Present 'failed live era projection retries the committed record' `
    'Code/core/lineage/YearNameService.cs' `
    'RetryCommittedProjection(pKingdom);'
foreach ($mapModeReset in @(
    'TechMapModeService.ResetRuntime()',
    'DevelopmentMapModeService.ResetRuntime()',
    'WarClaimMapModeService.ResetRuntime()',
    'WarCoreMapModeService.ResetRuntime()',
    'VassalMapModeService.ResetRuntime()',
    'MandateCoreMapModeService.ResetRuntime()',
    'MandateDynastyMapModeService.ResetRuntime()',
    'FeudatoryMapModeService.ResetRuntime()',
    'SchoolMapModeService.ResetRuntime()')) {
    Require-Present "world switches reset $mapModeReset" `
        'Code/core/policy/AWMapModeMetaLibrary.cs' $mapModeReset
}
Require-Absent 'diplomacy window hot paths cannot access SQLite' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' 'SQLite'

Require-Present 'diplomacy actions include coalition and marriage group' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'aw_diplomacy_group_coalition_marriage'
Require-Present 'diplomacy actions include strategy group' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'aw_diplomacy_group_strategy'
Require-Present 'diplomacy menu exposes coalition action' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'DiplomacyProposalType.Coalition'
Require-Present 'diplomacy menu exposes royal marriage action' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'DiplomacyProposalType.RoyalMarriage'
Require-Present 'diplomacy menu exposes spy action' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'DiplomaticOperationType.SpyNetwork'
Require-Present 'diplomacy menu exposes forgery action' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'DiplomaticOperationType.ForgeDocuments'
Require-Present 'diplomacy action scrollbar remains visible' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'ScrollRect.ScrollbarVisibility.Permanent'
Require-Present 'marriage selector renders real actor portraits' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'pSlot.Avatar.show(actor);'
Require-Present 'marriage UI reuses one bounded preview assessment' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'AssessRoyalMarriageWithPreview('
Require-Present 'AI marriage response can ignore its own pending proposal' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'pIgnorePending: pIgnorePending'
Require-Present 'coalition selector renders its target flag' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'KingdomFlagBuilder.Build('
Require-Present 'coalition send submits the selected third country' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'selectionTargetCountryId: _selectedCoalitionTargetId'
Require-Present 'authoritative diplomacy handler persists the coalition target' `
    'Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs' `
    'DiplomacyProposalService.TryCreateWithSelection('
Require-Present 'AI coalition response reuses the persisted target' `
    'Code/core/lineage/DiplomacyProposalService.cs' `
    'proposal.TargetKingdomId, proposal.RequesterActorId'
Require-Present 'spy action submits an authoritative command' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'AW3CommandRequest.StartSpyNetwork('
Require-Present 'authoritative diplomacy handler starts the delayed spy operation' `
    'Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs' `
    'DiplomaticOperationService.TryStartSpyNetwork('
Require-Present 'vassal annexation requires an active spy network' `
    'Code/core/lineage/VassalService.cs' `
    'HasActiveSpyNetwork('
Require-Present 'AI starts a spy network before annexing a vassal' `
    'Code/core/lineage/VassalAIService.cs' `
    'DiplomaticOperationService.TryStartSpyNetwork('
Require-Present 'annual kingdom work reaches vassal AI' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'VassalAIService.OnKingdomYear(pKingdom);'
Require-Present 'annual kingdom work reaches merit rewards' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'CourtMeritRewardService.OnKingdomYear(pKingdom);'
Require-Present 'annual kingdom work reaches mandate decisions' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'MandateDecisionService.OnKingdomYear(pKingdom)'
Require-Present 'policy annual work reaches policy AI' `
    'Code/core/policy/KingdomPolicyService.cs' `
    'KingdomPolicyAI.TryFillEmptySlots(pKingdom);'
Require-Present 'successful annexation consumes the active spy network' `
    'Code/core/lineage/VassalService.cs' `
    'DiplomaticOperationService.ConsumeActiveSpyNetwork('
Require-Present 'annexation action switching explicitly includes network state' `
    'Code/core/lineage/DiplomacyActionExpansionRules.cs' `
    'bool sourceIsDirectSuzerain, bool hasActiveSpyNetwork'
Require-Present 'forgery action submits an authoritative command' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'AW3CommandRequest.StartForgeDocuments('
Require-Present 'authoritative diplomacy handler purchases the forgery claim' `
    'Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs' `
    'DiplomaticOperationService.TryStartForgeDocuments('
Require-Present 'secret action preview labels discovery risk' `
    'Code/ui/windows/DiplomacyConversationWindow.cs' `
    'aw_diplomacy_discovery_chance'
Require-Present 'secret action preview exposes network expiry' `
    'Code/core/lineage/DiplomaticOperationService.cs' `
    'public int NetworkUntilYear;'
Require-Present 'legacy queued forgeries are retired without replaying side effects' `
    'Code/core/lineage/DiplomaticOperationService.cs' `
    'Finish(pRow, StatusCancelled, "legacy_forgery_removed",'
Require-Absent 'legacy queued forgeries do not emit obsolete result bubbles' `
    'Code/core/lineage/DiplomaticOperationService.cs' `
    'RecordCancelledOperationResult(pRow, source, target'
$diplomacyExpansionLocaleKeys = @(
    'aw_diplomacy_group_coalition_marriage',
    'aw_diplomacy_group_strategy',
    'aw_diplomacy_action_coalition',
    'aw_diplomacy_action_royal_marriage',
    'aw_diplomacy_action_spy_network',
    'aw_diplomacy_action_forge_documents',
    'aw_diplomacy_covert_preview',
    'aw_diplomacy_discovery_chance',
    'aw_diplomacy_forgery_weak',
    'aw_diplomacy_forgery_strong',
    'aw_diplomacy_failure_coalition_target',
    'aw_diplomacy_failure_coalition_limit',
    'aw_diplomacy_failure_active_coalition',
    'aw_diplomacy_failure_missing_royal_house',
    'aw_diplomacy_failure_active_royal_marriage',
    'aw_diplomacy_failure_no_royal_candidate',
    'aw_diplomacy_failure_spy_suzerain',
    'aw_diplomacy_failure_covert_pending',
    'aw_diplomacy_failure_network_active',
    'aw_diplomacy_failure_network_required',
    'aw_diplomacy_failure_target_city_changed',
    'aw_diplomacy_failure_fabrication_unavailable',
    'aw_diplomacy_failure_network_too_weak',
    'aw_diplomacy_covert_result_invalid',
    'aw_diplomacy_covert_result_at_war',
    'aw_diplomacy_covert_result_cannot_spy_on_suzerain',
    'aw_diplomacy_covert_result_covert_operation_pending',
    'aw_diplomacy_covert_result_spy_network_required',
    'aw_diplomacy_covert_result_target_city_changed',
    'aw_diplomacy_covert_result_fabrication_unavailable',
    'aw_diplomacy_covert_result_network_too_weak'
)
foreach ($localeKey in $diplomacyExpansionLocaleKeys) {
    Require-Present "diplomacy expansion locale key $localeKey is present" `
        'Locales/aw3_diplomacy.csv' ($localeKey + ',')
}

Require-Absent 'war target viewer cannot start fabrication decisions' `
    'Code/ui/windows/WarDecisionTargetWindow.cs' `
    'KingdomPolicyService.StartFabricationDecision('
Require-Present 'war target viewer retains current core project status' `
    'Code/ui/windows/WarDecisionTargetWindow.cs' `
    'KingdomPolicyService.GetCoreFabricationCityId('
Require-Present 'war target viewer directs fabrication to diplomacy' `
    'Code/ui/windows/WarDecisionTargetWindow.cs' `
    'aw_war_fabrication_moved_to_diplomacy'

Require-Present 'court disposition returns to the owning kingdom window' `
    'Code/ui/windows/CourtDispositionWindow.cs' `
    'AW_LineageWindowIds.ShowKingdom(_kingdomId)'
Require-Present 'kingdom destruction closes court state before object removal' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'CourtService.OnKingdomDestroying(pKingdom);'
Require-Present 'kingdom destruction reads only its indexed active officers' `
    'Code/core/court/CourtService.cs' `
    'GetActiveOfficers(pKingdom,'
Require-Present 'kingdom destruction closes stale durable officer rows' `
    'Code/core/court/CourtService.cs' `
    'OfficialCareerService.EndForOffice(row.actor_id,'
Require-Present 'inheritance law returns to the owning kingdom window' `
    'Code/ui/windows/InheritanceLawWindow.cs' `
    'AW_LineageWindowIds.ShowKingdom(_kingdomId)'
Require-Absent 'levy diagnostics never scan actors into ad-hoc log lines' `
    'Code/core/lineage/TemporaryLevyService.cs' `
    '[levy benchmark]'
Require-Absent 'war notices never emit per-kingdom levy snapshots' `
    'Code/core/lineage/WarNoticeService.cs' `
    'LogBenchmarkSnapshot'
Require-Present 'political projects preserve a court reserve' `
    'Code/core/policy/KingdomPolicyService.cs' `
    'PoliticalPointSpendingRules.AutomaticSpend('
Require-Present 'AI auxiliary-law reform preserves the shared court reserve' `
    'Code/core/court/CourtAuxiliaryLawService.cs' `
    'pAiInitiated ? PoliticalPointSpendingRules.CourtReserve : 0f'
Require-Present 'automatic vassal tribute preserves the shared court reserve' `
    'Code/core/lineage/VassalFiscalRules.cs' `
    'source - PoliticalPointSpendingRules.CourtReserve'
Require-Absent 'war declaration never consumes policy research points' `
    'Code/core/lineage/WarDecisionService.cs' `
    'LineageKeys.POLICY_POINTS'
Require-Present 'official career hot state can persist unranked officers' `
    'Code/core/court/OfficialCareerStateService.cs' `
    'OfficialCareerRankRules.Unranked'
Require-Present 'local grade estimator returns unranked before unlock' `
    'Code/core/court/OfficialCareerStateService.cs' `
    'return NineRankRules.Unranked;'
Require-Absent 'official career persistence never defaults a missing rank to rank one' `
    'Code/core/court/OfficialCareerPersistence.cs' `
    'RankAtAppointment = 1'
Require-Absent 'official state reader never defaults a missing rank to rank one' `
    'Code/core/court/OfficialCareerStateService.cs' `
    'Int(pReader, 3, 1)'
Require-Present 'locked court appointments persist an explicit unranked snapshot' `
    'Code/core/court/OfficialCareerService.cs' `
    ': OfficialCareerRankRules.Unranked'
Require-Present 'foreign occupation hot path reads the city occupation id cache' `
    'Code/core/lineage/ForeignOccupationService.cs' `
    'pCity.data.get(LineageKeys.FOREIGN_OCCUPATION_ID,'
Require-Present 'foreign occupation lookup uses the cached primary key' `
    'Code/core/lineage/ForeignOccupationService.cs' `
    'WHERE OCCUPATION_ID=@occupation AND CITY_ID=@city'
Require-Present 'path request timeout uses an unscaled realtime clock' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'Time.realtimeSinceStartupAsDouble'
Require-Absent 'path request timeout does not use scaled session time' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'getCurSessionTime()'
Require-Absent 'army formation correction does not use scaled session time' `
    'Code/core/lineage/AWArmyMarchService.cs' `
    'getCurSessionTime()'
Require-Absent 'AW3-owned army followers never fall back to full vanilla search' `
    'Code/patch/AW_ArmySafetyPatch.cs' `
    'if (!AWArmyMarchService.TryGetFollowerTarget(pActor, out WorldTile target)) return true;'
Require-Present 'terminal path failure releases the current AI behaviour' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'pActor.cancelAllBeh();'
Require-Present 'queued path requests use the short pending timeout' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'if (pFinder.IsWaitingForWorker(pActor.data.id))'
Require-Present 'worker accepted paths use a bounded no-progress timeout' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'else if (pFinder.IsWorkerRunning(pActor.data.id))'
Require-Present 'waiting path timeout releases the owned army march' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'ExpireWaitingRequestIfNeeded('
Require-Present 'captain path completion removes the owned march state' `
    'Code/core/lineage/AWArmyMarchService.cs' `
    'States.Remove(pActor.army.id)'
Require-Present 'followers retain vanilla movement until a route step exists' `
    'Code/core/lineage/AWArmyMarchService.cs' `
    'hasRouteSteps: state.Route.Count > 0'
Require-Absent 'path frame loop does not repeatedly drain transport actors' `
    'Code/core/pathfinding/AWPathfindingBootstrap.cs' `
    'AWPathMovementBridge.ProcessTransports('
Require-Present 'alliance creation is recorded after final naming callbacks' `
    'Code/patch/AW_DiplomacyConversationPatch.cs' `
    '[HarmonyPatch(typeof(AllianceManager), nameof(AllianceManager.newAlliance))]'
Require-Present 'alliance creation snapshot is a final postfix' `
    'Code/patch/AW_DiplomacyConversationPatch.cs' `
    'private static void ReleaseNewAlliancePostfix('
Require-Present 'alliance creation snapshot reads the committed live name' `
    'Code/patch/AW_DiplomacyConversationPatch.cs' `
    '__result.name, "")'
Require-Absent 'WorldLog postfix cannot race later alliance naming callbacks' `
    'Code/patch/AW_DiplomacyConversationPatch.cs' `
    'WorldLogAllianceCreatedPostfix('
Require-Present 'benchmark scopes encode nesting without allocating tokens' `
    'Code/core/policy/RecentFeatureBenchmark.cs' `
    'RecentFeatureBenchmarkRules.EncodeScopeStart('
Require-Present 'benchmark total records only outermost scopes' `
    'Code/core/policy/RecentFeatureBenchmark.cs' `
    'RecentFeatureBenchmarkRules.IsOutermostScopeToken('
Require-Present 'benchmark flush skips unsampled entries' `
    'Code/core/policy/RecentFeatureBenchmark.cs' `
    'RecentFeatureBenchmarkRules.ShouldSaveSample(count)'
Require-Absent 'dynastic state-name projection is not restricted to the origin kingdom' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'StateNameRules.CanReuseBranchBoundName('
Require-Present 'projected state names update the open dynasty snapshot' `
    'Code/core/lineage/ChronicleEvents.cs' `
    'DynastyRecordWriter.UpdateCurrentStateName('
Require-Present 'kingdom rename projection clears live nameplate caches' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'nameplate_manager?.clearCaches()'
Require-Present 'kingdom rename projection invalidates the Mandate dynasty layer' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'MandateDynastyMapModeService.DirtyMapIfActive()'
Require-Present 'kingdom rename projects the active Mandate read model' `
    'Code/core/lineage/KingdomRenameProjectionService.cs' `
    'MandateService.RefreshKingdomNameProjection(pKingdom)'
Require-Present 'Mandate rename projection updates only an open period' `
    'Code/core/lineage/MandateService.cs' `
    'AND KINGDOM_ID=@kingdom AND END_TIME=-1'
$chronicleAccessionSource = Get-Content -Raw -LiteralPath (
    Join-Path $root 'Code/core/lineage/ChronicleEvents.cs')
$dynastyCommitIndex = $chronicleAccessionSource.IndexOf(
    'DynastyRecordWriter.OnKingChanged(pKingdom, pNewKing)',
    [StringComparison]::Ordinal)
$dynasticProjectionIndex = $chronicleAccessionSource.IndexOf(
    'ProjectDynasticStateNameForRuler(pKingdom, pNewKing,',
    [StringComparison]::Ordinal)
if ($dynastyCommitIndex -lt 0 -or $dynasticProjectionIndex -lt 0 -or
    $dynastyCommitIndex -gt $dynasticProjectionIndex) {
    $failures.Add('dynastic state-name ordering: persist the new dynasty before projecting its old state name')
}
Require-Present 'inheritance factions project actual candidates' `
    'Code/ui/windows/InheritanceLawWindow.cs' `
    'InheritanceCandidateService.ResolveFactionSupport('
Require-Present 'inheritance faction rows show live candidate portraits' `
    'Code/ui/windows/InheritanceLawWindow.cs' `
    'row.Portrait.show(actor)'
Require-Present 'imperial faction previews primogeniture independently' `
    'Code/core/lineage/InheritanceCandidateService.cs' `
    'PreviewPrimogenitureCandidate'
Require-Present 'inheritance candidates share one kinship context per evaluation' `
    'Code/core/lineage/InheritanceCandidateService.cs' `
    'KinshipContext kinship = BuildKinshipContext(king);'
Require-Present 'inheritance candidate kinship caches immutable father links' `
    'Code/core/lineage/InheritanceCandidateService.cs' `
    'private static long CachedFatherId('
Require-Absent 'inheritance candidate evaluation cannot rebuild both ancestry chains per actor' `
    'Code/core/lineage/InheritanceCandidateService.cs' `
    'LineageQuery.NearestCommonAgnaticAncestor('
Require-Present 'family tree checks former ruler before captive guest title' `
    'Code/core/lineage/LineageQuery.cs' `
    'FORMER_KING_TITLE'
$lineageQuerySource = Get-Content -Raw -LiteralPath (
    Join-Path $root 'Code/core/lineage/LineageQuery.cs')
$formerTitleIndex = $lineageQuerySource.IndexOf(
    'LineageKeys.FORMER_KING_TITLE', [StringComparison]::Ordinal)
$nobleTitleIndex = $lineageQuerySource.IndexOf(
    'DynasticTitleService.ResolveLivingTitle(pLive)', [StringComparison]::Ordinal)
$captiveTitleIndex = $lineageQuerySource.IndexOf(
    'LineageKeys.CAPTIVE_NOBLE_TITLE', [StringComparison]::Ordinal)
if ($formerTitleIndex -lt 0 -or $nobleTitleIndex -lt 0 -or
    $captiveTitleIndex -lt 0 -or
    $formerTitleIndex -gt $nobleTitleIndex -or
    $nobleTitleIndex -gt $captiveTitleIndex) {
    $failures.Add('family tree title priority: former ruler and formal noble title must precede captive guest')
}

Get-ChildItem -LiteralPath (Join-Path $root 'Locales') -Filter '*.csv' |
    ForEach-Object {
        $lines = [IO.File]::ReadAllLines($_.FullName, [Text.Encoding]::UTF8)
        if ($lines.Length -eq 0) { return }
        $expectedCommas = ($lines[0].ToCharArray() | Where-Object { $_ -eq ',' }).Count
        for ($lineIndex = 1; $lineIndex -lt $lines.Length; $lineIndex++) {
            if ([string]::IsNullOrWhiteSpace($lines[$lineIndex])) { continue }
            $actualCommas = ($lines[$lineIndex].ToCharArray() |
                Where-Object { $_ -eq ',' }).Count
            if ($actualCommas -ne $expectedCommas) {
                $failures.Add("locale csv column count: $($_.Name):$($lineIndex + 1) " +
                    "has $actualCommas commas, expected $expectedCommas")
            }
        }
    }

$windowIds = @(
    'aw_lineage_overview', 'aw_shi_list', 'aw_family_tree', 'aw_history',
    'aw_kingdom_roster', 'aw_policy_tree', 'aw_ancestry_analysis',
    'aw_mandate_dynasty', 'aw_mandate_cycle', 'aw_mandate_decisions',
    'aw_court', 'aw_court_disposition', 'aw_court_auxiliary_laws',
    'aw_inheritance_laws', 'aw_school_browser', 'aw_school_roster',
    'aw_name_decision', 'aw_conferred_posthumous', 'aw_central_power',
    'aw_feudatories', 'aw_diplomacy_conversations',
    'aw_diplomatic_war_declaration')
$localeKeys = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
Get-ChildItem -LiteralPath (Join-Path $root 'Locales') -Filter '*.csv' |
    ForEach-Object {
        foreach ($line in [IO.File]::ReadAllLines(
                     $_.FullName, [Text.Encoding]::UTF8)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            [void]$localeKeys.Add($line.Split(',')[0].Trim())
        }
    }
foreach ($windowId in $windowIds) {
    if (-not $localeKeys.Contains($windowId)) {
        $failures.Add("custom window title localization: $windowId")
    }
}

Require-Present 'academy usability accepts tile-based city attachment' `
    'Code/core/schools/HistoricalSchoolAcademyService.cs' `
    'HistoricalSchoolVenueRules.IsAttachedToRequestedCity('
Require-Present 'academy construction throttles repeated yearly placement scans' `
    'Code/core/schools/HistoricalSchoolAcademyConstructionService.cs' `
    'SchoolAcademyConstructionRules.ShouldAttemptPlacement('
Require-Present 'followers reserve an accepted leader march before the first route step' `
    'Code/core/lineage/AWArmyMarchService.cs' `
    'ArmyMarchRules.ShouldOwnFollowerMarch('
Require-Present 'leader submission immediately owns the follower march' `
    'Code/core/lineage/AWArmyMarchService.cs' `
    'HasPlan = true;'
Require-Present 'dedicated core fabrication is excluded from the general AI decision slot' `
    'Code/core/policy/KingdomPolicyAI.cs' `
    'ShouldUseGeneralDecisionSlot(def.Id)'
Require-Present 'world switching clears the royal claimant index' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'RoyalClaimService.ClearRuntime'
Require-Present 'world switching clears feudatory runtime state' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'FeudatoryService.ResetRuntime'
Require-Present 'loading a save rebuilds persisted feudatories' `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs' `
    'FeudatoryService.LoadActiveCache'
Require-Present 'cached economy probes reject stale years' `
    'Code/core/policy/CityEconomyService.cs' `
    'CityEconomyUpdateRules.ShouldUseContributionCache('
Require-Present 'ordinary army membership changes refresh prewar deployment incrementally' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'WarNoticeService.QueueArmyChanged('
Require-Absent 'zero-city extinction cannot strip nationality before destruction hooks' `
    'Code/patch/AW_KingdomExtinctionPatch.cs' `
    '__instance.makeSurvivorsToNomads()'
Require-Present 'zero-city realms bypass survivor-count removal delay' `
    'Code/patch/AW_KingdomExtinctionPatch.cs' `
    'KingdomExtinctionRules.ShouldForceImmediateRemoval('
Require-Present 'blocked mandate fragmentation records the succession crisis' `
    'Code/patch/AW_MandateSuccessionPatch.cs' `
    'MandateService.OnPeacefulFellApartBlocked(pMainKingdom)'
Require-Present 'vanilla rebellion writes the shared rebellion chronicle' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'nameof(DiplomacyHelpersRebellion.startRebellion)'
Require-Present 'inspired rebellion writes the shared rebellion chronicle' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'HarmonyPatch(typeof(City), "useInspire")'
Require-Present 'rebellion patch commits the shared rebellion chronicle' `
    'Code/patch/AW_ChroniclePatch.cs' `
    '__state.OriginalKingdom);'
Require-Absent 'guest office cannot bypass its atomic two-table transaction' `
    'Code/core/schools/HistoricalAffiliationService.cs' `
    'public static bool TryBeginService('
Require-Present 'realm destruction closes indexed prewar notices' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'WarNoticeService.OnKingdomDestroying(pKingdom)'
Require-Present 'realm destruction releases temporary levies' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'TemporaryLevyService.OnKingdomDestroying(pKingdom)'
Require-Present 'realm destruction releases wartime garrisons' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'WartimeGarrisonService.OnKingdomDestroying(pKingdom)'
Require-Present 'realm destruction releases temporary slave vanguards' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'TemporarySlaveVanguardService.OnKingdomDestroying(pKingdom)'
Require-Present 'realm destruction retires stale generals' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'GeneralService.OnKingdomDestroying(pKingdom)'
Require-Present 'realm destruction dissolves royal guards' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'RoyalGuardService.OnKingdomDestroying(pKingdom)'
Require-Present 'realm destruction closes internal feudatories' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'FeudatoryService.OnKingdomDestroying(pKingdom)'
Require-Present 'realm destruction removes military emergency indexes' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'MilitaryEmergencyService.OnKingdomDestroying(pKingdom)'
Require-Present 'realm destruction removes standing-readiness indexes' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'KingdomMilitaryReadinessService.OnKingdomDestroying(pKingdom)'

Require-Present 'kingdom annual work is queued from updateAge' `
    'Code/patch/AW_KingdomPolicyPatch.cs' `
    'KingdomAnnualWorkService.Schedule(__instance)'
Require-Absent 'kingdom updateAge no longer runs heir maintenance inline' `
    'Code/patch/AW_KingdomPolicyPatch.cs' `
    'HeirService.OnKingdomYear(__instance)'
Require-Present 'annual work uses one coalesced item per kingdom' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'annual work advances through bounded stages' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'KingdomAnnualWorkRules.NextStage('
Require-Present 'sampled actor AI records the exact task id' `
    'Code/patch/AW_ActorAiBenchmarkPatch.cs' `
    '__state.TaskId'
Require-Present 'runtime diagnostics aggregate exact actor task samples' `
    'Code/core/policy/RuntimePerformanceDiagnostic.cs' `
    'RecordActorTask('
Require-Present 'annual stages keep a diagnostic independent of the outer deferred item' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'RuntimePerformanceDiagnostic.EndAnnualStage('
Require-Present 'deferred runtime drain retains a strict frame budget' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'DrainFrame(pMilliseconds: 1.0'
Require-Present 'deferred runtime drain uses a bounded adaptive item quota' `
    'Code/core/performance/AWAuthorityCycleService.cs' `
    'AuthorityDeferredDrainRules.ResolveItemLimit('
Require-Present 'idle actors use the custom path ownership fast gate' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' `
    'if (!HasOwnedPathState(actorId)) return false;'
Require-Present 'custom smooth movement only intercepts AW3-owned routes' `
    'Code/patch/AW_GlobalPathfindingPatch.cs' `
    'if (!AWPathMovementBridge.ShouldUseCustomSmoothMovement(__instance)) return true;'
Require-Present 'school idle roam uses the bounded local probe budget' `
    'Code/core/schools/HistoricalSchoolVenueService.cs' `
    'HistoricalSchoolVenueRules.IdleRoamProbeCount('
Require-Present 'school travel checks general status without sqlite' `
    'Code/core/schools/HistoricalSchoolTravelService.cs' `
    'GeneralService.IsActiveGeneralFast(pActor)'
Require-Absent 'school travel cannot query general sqlite from actor ai' `
    'Code/core/schools/HistoricalSchoolTravelService.cs' `
    'GeneralService.IsGeneral(pActor)'
Require-Present 'annual policy stage exposes xiaization diagnostic' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'MeasureDiagnostic("annual_policy_xiaization"'
Require-Present 'annual policy stage exposes policy diagnostic' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'MeasureDiagnostic("annual_policy_core"'
Require-Present 'annual work has a separate court support frame slice' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'KingdomAnnualWorkStage.CourtSupport => "annual_court_support"'
Require-Present 'annual work has a separate auxiliary court frame slice' `
    'Code/core/policy/KingdomAnnualWorkService.cs' `
    'KingdomAnnualWorkStage.CourtAuxiliary =>'
Require-Present 'idle path ownership is decided without pathfinder cancellation' `
    'Code/core/pathfinding/AWPathLifecycleRules.cs' `
    'ShouldInspectCustomPathState(bool hasRetryContext,'
Require-Present 'royal deaths invalidate heir selection without inline search' `
    'Code/patch/AW_ActorDeathPatch.cs' `
    'HeirService.MarkSuccessionDirtyForActor(__instance);'
Require-Present 'stable heirs use event driven cache validation' `
    'Code/core/lineage/HeirService.cs' `
    'HeirDirectSonRules.NeedsEventDrivenRefresh(pForce,'
Require-Present 'inheritance law uses bounded adult royal existence lookup' `
    'Code/core/lineage/InheritanceLawService.cs' `
    'InheritanceCandidateService.HasAdultRoyalCandidate('
Require-Present 'heir benchmark splits inheritance law evaluation' `
    'Code/core/lineage/HeirService.cs' `
    'RecentFeatureBenchmarkRules.KingdomInheritanceLawIndex'
Require-Present 'temporary levies preserve the shared city population floor' `
    'Code/core/lineage/TemporaryLevyService.cs' `
    'WartimeRecruitmentPopulationRules.RecruitmentCapacity('
Require-Present 'wartime garrisons preserve the shared city population floor' `
    'Code/core/lineage/WartimeGarrisonService.cs' `
    'WartimeRecruitmentPopulationRules.RecruitmentCapacity('
Require-Present 'new wars persist every participant city baseline' `
    'Code/core/lineage/WarScoreRuntimeBridge.cs' `
    'WarParticipantCityBaselineService.RegisterExistingParticipants(war);'
Require-Present 'joining realms persist their own city baseline' `
    'Code/patch/AW_WarPatch.cs' `
    'WarParticipantCityBaselineService.RegisterParticipant(pWar, pKingdom);'
Require-Present 'frozen city score reads the owner initial city baseline' `
    'Code/core/lineage/WarScoreRuntimeBridge.cs' `
    'WarParticipantCityBaselineService.GetOrRegister(pWar, pCity.kingdom)'

$heirSource = Read-Source 'Code/core/lineage/HeirService.cs'
$heirReconcileStart = $heirSource.IndexOf(
    'public static Actor ReconcileHeir(',
    [System.StringComparison]::Ordinal)
$heirReconcileEnd = $heirSource.IndexOf(
    'public static Actor PeekRegisteredHeir(', $heirReconcileStart,
    [System.StringComparison]::Ordinal)
if ($heirReconcileStart -lt 0 -or $heirReconcileEnd -le $heirReconcileStart) {
    $failures.Add('heir reconcile source segment could not be located')
}
else {
    $heirReconcile = $heirSource.Substring($heirReconcileStart,
        $heirReconcileEnd - $heirReconcileStart)
    if ($heirReconcile.Contains('PickEldestLivingSon(') -or
        $heirReconcile.Contains('LineageQuery.')) {
        $failures.Add('stable annual heir reconcile cannot query descendants or lineage SQL')
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Source guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host 'Source guards passed.'
