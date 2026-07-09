using System;
using AncientWarfare3.core.db;
using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace WarFabricationRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
            ExpectBlocked("same_kingdom_or_invalid",
                pForeignCivilTarget: false,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false);

            ExpectBlocked("target_city_invalid",
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: false,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false);

            ExpectBlocked("not_neighbor",
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: false,
                pBlockedByVassalRelation: false);

            ExpectBlocked("vassal_annex_by_decision",
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: true);

            ExpectAllowed(
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false);

            ExpectCoreProjectAllowed();
            ExpectCoreProjectBlocked("not_own_city",
                pSourceValid: true,
                pTargetOwnCity: false,
                pAlreadyCore: false,
                pExistingProject: false);
            ExpectCoreProjectBlocked("already_core",
                pSourceValid: true,
                pTargetOwnCity: true,
                pAlreadyCore: true,
                pExistingProject: false);
            ExpectCoreProjectBlocked("project_exists",
                pSourceValid: true,
                pTargetOwnCity: true,
                pAlreadyCore: false,
                pExistingProject: true);

            ExpectClaimProjectAllowed();
            ExpectClaimProjectBlocked("same_kingdom_or_invalid",
                pForeignCivilTarget: false,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false,
                pExistingProject: false);
            ExpectClaimProjectBlocked("vassal_annex_by_decision",
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: true,
                pExistingProject: false);
            ExpectClaimProjectBlocked("project_exists",
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false,
                pExistingProject: true);

            ExpectFabricationDecisionVisible("aw_decision_fabricate_core",
                hasCoreProjectTarget: true, hasClaimProjectTarget: false);
            ExpectFabricationDecisionHidden("aw_decision_fabricate_core",
                hasCoreProjectTarget: false, hasClaimProjectTarget: true);
            ExpectFabricationDecisionVisible("aw_decision_fabricate_strong_claim",
                hasCoreProjectTarget: false, hasClaimProjectTarget: true);

            ExpectStrongClaimDisplayCount(3, explicitStrongClaims: 1, coreTargets: 2);
            ExpectEffectiveStrongClaimCount(2, explicitStrongClaims: 0, coreTargets: 2);
            ExpectClaimLikeCasusBelli(true, weakClaims: 0, explicitStrongClaims: 0, coreTargets: 1);
            ExpectClaimLikeCasusBelli(false, weakClaims: 0, explicitStrongClaims: 0, coreTargets: 0);

            ExpectDateParts(6, 3, 21, "6\u5e743\u670821\u65e5");
            ExpectHistoryPeriodRules();
            ExpectKingdomRenameRules();
            ExpectAncestryOriginRules();
            ExpectSlaveKingAbdicationRules();
            ExpectPosthumousTitleRules();

            ExpectRestorationBlocked("no_hosted_claim",
                pHasHostedClaim: false,
                pBlockedByVassalRelation: false,
                pAlreadyAtWar: false);

            ExpectRestorationBlocked("vassal_annex_by_decision",
                pHasHostedClaim: true,
                pBlockedByVassalRelation: true,
                pAlreadyAtWar: false);

            ExpectRestorationBlocked("already_at_war",
                pHasHostedClaim: true,
                pBlockedByVassalRelation: false,
                pAlreadyAtWar: true);

            ExpectRestorationAllowed(
                pHasHostedClaim: true,
                pBlockedByVassalRelation: false,
                pAlreadyAtWar: false);

            ExpectCoreMapColor("core", "core");
            ExpectCoreMapColor("pending_core", "pending_core");
            ExpectCoreMapColor("owned_non_core", "owned_non_core");
            ExpectCoreMapColor("", "");
            ExpectWarMapHex("core", "#226B3A", WarMapModeColorRules.CoreHexForStatus("core"));
            ExpectWarMapHex("pending_core", "#D7A928", WarMapModeColorRules.CoreHexForStatus("pending_core"));
            ExpectWarMapHex("owned_non_core", "#B3124B", WarMapModeColorRules.CoreHexForStatus("owned_non_core"));

            ExpectClaimMapColor("strong_claim", "strong_claim");
            ExpectClaimMapColor("weak_claim", "weak_claim");
            ExpectClaimMapColor("pending_claim", "pending_claim");
            ExpectClaimMapColor("", "");
            ExpectWarMapHex("strong_claim", "#226B3A", WarMapModeColorRules.ClaimHexForStatus("strong_claim"));
            ExpectWarMapHex("weak_claim", "#D7A928", WarMapModeColorRules.ClaimHexForStatus("weak_claim"));
            ExpectWarMapHex("pending_claim", "#E08226", WarMapModeColorRules.ClaimHexForStatus("pending_claim"));

            ExpectIconPath("aw_normal_war", "ui/wars/war_conquest", "wars/war_conquest");
            ExpectIconPath("general_rebellion_war", "ui/wars/war_rebellion", "wars/war_rebellion");
            ExpectIconPath("vassal_war", "ui/wars/war_vassal", "ui/wars/war_vassal");
            ExpectTargetIconPath("take_core_city", "ui/plots/plot_reclaim");
            ExpectTargetIconPath("restore_kingdom", "ui/plots/plot_usurpation");
            ExpectTargetIconPath("fabricate_core", "ui/icons/iconKnowledge");

            ExpectVassalAnnexAllowed();
            ExpectVassalAnnexBlocked("not_direct_vassal",
                pSuzerainValid: true,
                pTargetDirectVassal: false,
                pSuzerainAtWar: false,
                pTargetAtWar: false);
            ExpectVassalAnnexBlocked("at_war",
                pSuzerainValid: true,
                pTargetDirectVassal: true,
                pSuzerainAtWar: true,
                pTargetAtWar: false);
            ExpectVassalRelationRules();
            ExpectVassalIndependenceRules();
            ExpectVassalWarSupportRules();

            ExpectDecisionTargetLine("\u76ee\u6807\uff1a\u8d8a");
            ExpectDecisionTargetLine("");
            ExpectWarLabel("weak_claim_decision", "\u5236\u9020\u5f31\u5ba3\u79f0");
            ExpectWarLabel("vassal_war", "\u9644\u5eb8\u6218\u4e89");
            ExpectWarLabel("core_reclaim", "\u6536\u590d\u6838\u5fc3");
            ExpectWarLabel("tianmingrebel", "\u4e49\u519b\u5929\u547d\u6218\u4e89");
            ExpectHistoryEventLabel("war_claim_created", "\u5236\u9020\u5ba3\u79f0");
            ExpectHistoryEventLabel("war_start", "\u6218\u4e89\u7206\u53d1");
            ExpectHistoryEventLabel("mandate_ruler_title", "\u8ffd\u4e0a\u5e99\u8c25");
            ExpectHistoryEventLabel("weak_claim_decision", "\u5236\u9020\u5f31\u5ba3\u79f0");
            ExpectHistoryLocalizationRules();
            ExpectHistoryContentNormalization(
                "\u53d6\u5f97\u5ba3\u6218\u7406\u7531\uff1aweak_claim_decision",
                "\u53d6\u5f97\u5ba3\u6218\u7406\u7531\uff1a\u5236\u9020\u5f31\u5ba3\u79f0");
            ExpectHistoryContentNormalization(
                "\u7206\u53d1\u6218\u4e89(vassal_war)",
                "\u7206\u53d1\u6218\u4e89(\u9644\u5eb8\u6218\u4e89)");

            ExpectVassalWarBlocked("vassal_external_war_blocked",
                pAttackerIsVassal: true,
                pDefenderIsSuzerain: false,
                pSameSuzerain: false,
                pWarType: "aw_normal_war");
            ExpectVassalWarAllowed(
                pAttackerIsVassal: true,
                pDefenderIsSuzerain: false,
                pSameSuzerain: true,
                pWarType: "aw_normal_war");
            ExpectVassalWarAllowed(
                pAttackerIsVassal: true,
                pDefenderIsSuzerain: true,
                pSameSuzerain: false,
                pWarType: "independence_war");
            ExpectAllianceBlockedForVassal();

            ExpectWarDecisionSummary(
                "\u5ba3\u6218\u7406\u7531\uff1a\u6536\u590d\u6838\u5fc3\n\u76ee\u6807\u56fd\uff1a\u8d8a\n\u6218\u4e89\u76ee\u6807\uff1a\u4f1a\u7a3d",
                "\u6536\u590d\u6838\u5fc3",
                "\u8d8a",
                "\u4f1a\u7a3d");
            ExpectWarDecisionTargetDisplayRules();
            ExpectWarDecisionTargetOrder();

            ExpectWarQueueAllowed("take_core_city",
                pBasicAllowed: true,
                pHasNormalCb: false,
                pCanForceNoCb: false,
                pHasCoreTarget: true,
                pHasClaimTarget: false,
                pCanForceVassal: false,
                pIsIndependenceTarget: false,
                pHasRestorationTarget: false);
            ExpectWarQueueBlocked("missing_core_target",
                "take_core_city",
                pBasicAllowed: true,
                pHasNormalCb: false,
                pCanForceNoCb: false,
                pHasCoreTarget: false,
                pHasClaimTarget: false,
                pCanForceVassal: false,
                pIsIndependenceTarget: false,
                pHasRestorationTarget: false);
            ExpectWarQueueBlocked("missing_claim_target",
                "press_claim_city",
                pBasicAllowed: true,
                pHasNormalCb: true,
                pCanForceNoCb: false,
                pHasCoreTarget: false,
                pHasClaimTarget: false,
                pCanForceVassal: false,
                pIsIndependenceTarget: false,
                pHasRestorationTarget: false);
            ExpectWarQueueAllowed("press_claim_city",
                pBasicAllowed: true,
                pHasNormalCb: true,
                pCanForceNoCb: false,
                pHasCoreTarget: false,
                pHasClaimTarget: true,
                pCanForceVassal: false,
                pIsIndependenceTarget: false,
                pHasRestorationTarget: false);
            ExpectWarQueueAllowed("independence",
                pBasicAllowed: true,
                pHasNormalCb: false,
                pCanForceNoCb: false,
                pHasCoreTarget: false,
                pHasClaimTarget: false,
                pCanForceVassal: false,
                pIsIndependenceTarget: true,
                pHasRestorationTarget: false);
            ExpectWarQueueBlocked("missing_restoration_target",
                "restore_kingdom",
                pBasicAllowed: true,
                pHasNormalCb: false,
                pCanForceNoCb: false,
                pHasCoreTarget: false,
                pHasClaimTarget: false,
                pCanForceVassal: false,
                pIsIndependenceTarget: false,
                pHasRestorationTarget: false);

            ExpectTargetScore("core_city", 140, "take_core_city", hasCore: true, hasStrongClaim: false,
                hasWeakClaim: false, restorationStrength: 0, population: 50);
            ExpectTargetScore("strong_claim_city", 110, "press_claim_city", hasCore: false, hasStrongClaim: true,
                hasWeakClaim: false, restorationStrength: 0, population: 50);
            ExpectTargetScore("restoration", 125, "restore_kingdom", hasCore: false, hasStrongClaim: false,
                hasWeakClaim: false, restorationStrength: 80, population: 50);

            ExpectFocusId(12, pCurrentFocusId: 12, pSelectedKingdomId: 99);
            ExpectFocusId(99, pCurrentFocusId: -1, pSelectedKingdomId: 99);
            ExpectFocusId(-1, pCurrentFocusId: -1, pSelectedKingdomId: -1);

            ExpectVassalTitleUpgradeBlocked("must_remain_below_suzerain",
                pSuzerainTitle: 2,
                pVassalCurrentTitle: 1);
            ExpectVassalTitleUpgradeAllowed(
                pSuzerainTitle: 3,
                pVassalCurrentTitle: 1);

            ExpectAwMapModeRuntimeType();
            ExpectAwMapModeNameplateRules();
            ExpectAwMapModePowerRules();
            ExpectTechMapModeOptionRules();
            ExpectWarMapModeOptionRules();
            ExpectAwMapModeStatusCacheKeys();
            ExpectMandateDynastyMapRules();
            ExpectMandateCoreTooltipRules();
            ExpectWorldSwitchCacheRules();
            ExpectWarPlotRedirectRules();
            ExpectWarPlotProgressRedirectRules();
            ExpectWarTypeAssetRules();
            ExpectMetaWindowSafetyRules();
            ExpectPathfindingSafetyRules();
            ExpectRestorationSettlementRules();
            ExpectWarGoalControlRules();
            ExpectSlaveArmyNameRefreshRule();
            ExpectCityMaintenanceThrottleRules();
            ExpectRoyalGuardMaintenanceRules();
            ExpectAwArmyRoleRules();
            ExpectSpecialArmyLookupCacheRules();
            ExpectSlaveArmyFormationRules();
            ExpectSlaveCaptureCommandRules();
            ExpectNonCoreLoyaltyRules();
            ExpectWarTerritoryCacheRules();
            ExpectHeirTitleRules();
            ExpectArmyRetreatRules();
            ExpectCityOccupationAccelerationRules();
            ExpectFamilyTreePortraitFrameRules();
            ExpectClanBannerFrameRules();
            ExpectFamilyTreeToolbarLayoutRules();
            ExpectVassalNameplateFlagLayoutRules();
            ExpectFabricateCoreDecisionPriority();
            ExpectCoreFabricationSlotRules();
            ExpectDecisionQueueRules();
            ExpectPolicyNodeLockRules();
            ExpectTechResearchPaceRules();
            ExpectForeignOccupationDetectionRules();
            ExpectMandateSuccessionRules();
            ExpectMandateDeclarationOriginRules();
            ExpectXiaizationEligibilityRules();
            ExpectXiaContactRules();
            ExpectForeignPseudoLineageRules();
            ExpectMandatePowerRules();
            ExpectMandateStartRecordRules();
            ExpectMandateRebelStateRules();
            ExpectRepublicGovernmentRules();
            ExpectMandateWarAiRules();
            ExpectMandateConquestRules();
            ExpectMandateBorderWallRules();
            ExpectCapitalMoveRules();
            ExpectCollateralSuccessionFallbackRules();
            ExpectHeirRecallRules();
            ExpectLineageBranchRules();
            ExpectRoyalGuardSelectionRules();
            ExpectXiaAuthorityGenderRules();
            ExpectHeirCandidateRules();
            ExpectRoyalSuccessionBirthRules();
            ExpectFormerRulerPosthumousRules();
            ExpectFormerRulerRecordRules();
            ExpectFormerKingTraitRules();
            ExpectSetKingPostfixRules();
            ExpectCityEconomyMilestoneRules();
            ExpectAncestryDisplayRules();
            ExpectMandateMapMarkerRules();
            ExpectLineageArchiveIndexRules();
            ExpectMetaColorCacheRules();
            ExpectKingdomVisualRandomizationRules();
            ExpectMapModeMetaCacheRules();
            ExpectMapModeDirtyThrottleRules();
            ExpectActorAiSearchThrottleRules();
            ExpectKingdomYearSchedulerRules();
            ExpectFiefCacheRules();
            ExpectXiaNameRepairRules();
            ExpectXiaFallbackNameRules();
            ExpectXiaCityNameLibraryRules();
            ExpectCityTechChronicleRules();
            ExpectCityMaintenanceBenchmarkRules();
            ExpectUpdateAgeBenchmarkRules();
            ExpectDeathBondRules();
            ExpectXiaItemEffectRules();
            ExpectTraitIconUsageRules();
            ExpectVisibleClanRenameRules();

            Console.WriteLine("War fabrication rule tests passed.");
            return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message);
                return 1;
            }
        }

        private static void ExpectBlocked(string pReason,
            bool pForeignCivilTarget,
            bool pTargetCityOwnedByTarget,
            bool pNeighboringCity,
            bool pBlockedByVassalRelation)
        {
            bool allowed = WarFabricationRules.CanFabricate(
                pForeignCivilTarget,
                pTargetCityOwnedByTarget,
                pNeighboringCity,
                pBlockedByVassalRelation,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected block '{pReason}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectAllowed(
            bool pForeignCivilTarget,
            bool pTargetCityOwnedByTarget,
            bool pNeighboringCity,
            bool pBlockedByVassalRelation)
        {
            bool allowed = WarFabricationRules.CanFabricate(
                pForeignCivilTarget,
                pTargetCityOwnedByTarget,
                pNeighboringCity,
                pBlockedByVassalRelation,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected allowed fabrication, got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectCoreProjectAllowed()
        {
            bool allowed = WarFabricationRules.CanFabricateCore(
                pSourceValid: true,
                pTargetOwnCity: true,
                pAlreadyCore: false,
                pExistingProject: false,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected allowed core project, got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectCoreProjectBlocked(string pReason,
            bool pSourceValid,
            bool pTargetOwnCity,
            bool pAlreadyCore,
            bool pExistingProject)
        {
            bool allowed = WarFabricationRules.CanFabricateCore(
                pSourceValid,
                pTargetOwnCity,
                pAlreadyCore,
                pExistingProject,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected core block '{pReason}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectClaimProjectAllowed()
        {
            bool allowed = WarFabricationRules.CanFabricateClaim(
                pForeignCivilTarget: true,
                pTargetCityOwnedByTarget: true,
                pNeighboringCity: true,
                pBlockedByVassalRelation: false,
                pExistingProject: false,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected allowed claim project, got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectClaimProjectBlocked(string pReason,
            bool pForeignCivilTarget,
            bool pTargetCityOwnedByTarget,
            bool pNeighboringCity,
            bool pBlockedByVassalRelation,
            bool pExistingProject)
        {
            bool allowed = WarFabricationRules.CanFabricateClaim(
                pForeignCivilTarget,
                pTargetCityOwnedByTarget,
                pNeighboringCity,
                pBlockedByVassalRelation,
                pExistingProject,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected claim block '{pReason}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectFabricationDecisionVisible(string pDecisionId, bool hasCoreProjectTarget,
            bool hasClaimProjectTarget)
        {
            if (!WarFabricationRules.CanExposeFabricationDecision(pDecisionId, hasCoreProjectTarget,
                    hasClaimProjectTarget))
                throw new Exception($"Expected fabrication decision '{pDecisionId}' to be visible.");
        }

        private static void ExpectFabricationDecisionHidden(string pDecisionId, bool hasCoreProjectTarget,
            bool hasClaimProjectTarget)
        {
            if (WarFabricationRules.CanExposeFabricationDecision(pDecisionId, hasCoreProjectTarget,
                    hasClaimProjectTarget))
                throw new Exception($"Expected fabrication decision '{pDecisionId}' to be hidden.");
        }

        private static void ExpectStrongClaimDisplayCount(int pExpected, int explicitStrongClaims, int coreTargets)
        {
            int actual = WarTargetSelectionRules.CountStrongClaimsForDisplay(explicitStrongClaims, coreTargets);
            if (actual != pExpected)
                throw new Exception($"Expected displayed strong claims {pExpected}, got {actual}.");
        }

        private static void ExpectEffectiveStrongClaimCount(int pExpected, int explicitStrongClaims, int coreTargets)
        {
            int actual = WarTargetSelectionRules.CountEffectiveStrongClaims(explicitStrongClaims, coreTargets);
            if (actual != pExpected)
                throw new Exception($"Expected effective strong claims {pExpected}, got {actual}.");
        }

        private static void ExpectClaimLikeCasusBelli(bool pExpected, int weakClaims, int explicitStrongClaims,
            int coreTargets)
        {
            bool actual = WarTargetSelectionRules.HasClaimLikeCasusBelli(weakClaims, explicitStrongClaims, coreTargets);
            if (actual != pExpected)
                throw new Exception($"Expected claim-like CB {pExpected}, got {actual}.");
        }

        private static void ExpectDateParts(int pYear, int pMonth, int pDay, string pExpected)
        {
            string actual = ChronicleFormatRules.FormatDateParts(pYear, pMonth, pDay);
            if (actual != pExpected)
                throw new Exception($"Expected date '{pExpected}', got '{actual}'.");
        }

        private static void ExpectRestorationBlocked(string pReason,
            bool pHasHostedClaim,
            bool pBlockedByVassalRelation,
            bool pAlreadyAtWar)
        {
            bool allowed = WarRestorationRules.CanExposeRestorationAction(
                pHasHostedClaim,
                pBlockedByVassalRelation,
                pAlreadyAtWar,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected restoration block '{pReason}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectRestorationAllowed(
            bool pHasHostedClaim,
            bool pBlockedByVassalRelation,
            bool pAlreadyAtWar)
        {
            bool allowed = WarRestorationRules.CanExposeRestorationAction(
                pHasHostedClaim,
                pBlockedByVassalRelation,
                pAlreadyAtWar,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected allowed restoration, got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectCoreMapColor(string pStatus, string pExpectedKey)
        {
            string actual = WarMapModeColorRules.CoreColorKey(pStatus);
            if (actual != pExpectedKey)
                throw new Exception($"Expected core map color '{pExpectedKey}', got '{actual}'.");
        }

        private static void ExpectClaimMapColor(string pStatus, string pExpectedKey)
        {
            string actual = WarMapModeColorRules.ClaimColorKey(pStatus);
            if (actual != pExpectedKey)
                throw new Exception($"Expected claim map color '{pExpectedKey}', got '{actual}'.");
        }

        private static void ExpectWarMapHex(string pLabel, string pExpectedHex, string pActualHex)
        {
            if (pActualHex != pExpectedHex)
                throw new Exception($"Expected {pLabel} map hex '{pExpectedHex}', got '{pActualHex}'.");
        }

        private static void ExpectIconPath(string pWarType, string pInput, string pExpected)
        {
            string actual = WarIconPathRules.ResolveWarIconPath(pWarType, pInput);
            if (actual != pExpected)
                throw new Exception($"Expected icon path '{pExpected}', got '{actual}'.");
        }

        private static void ExpectTargetIconPath(string pKind, string pExpected)
        {
            string actual = WarIconPathRules.ResolveTargetIconPath(pKind);
            if (actual != pExpected)
                throw new Exception($"Expected target icon path '{pExpected}', got '{actual}'.");
        }

        private static void ExpectVassalAnnexAllowed()
        {
            bool allowed = VassalAnnexDecisionRules.CanStart(
                pSuzerainValid: true,
                pTargetDirectVassal: true,
                pSuzerainAtWar: false,
                pTargetAtWar: false,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected annex allowed, got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectVassalAnnexBlocked(string pReason,
            bool pSuzerainValid,
            bool pTargetDirectVassal,
            bool pSuzerainAtWar,
            bool pTargetAtWar)
        {
            bool allowed = VassalAnnexDecisionRules.CanStart(
                pSuzerainValid,
                pTargetDirectVassal,
                pSuzerainAtWar,
                pTargetAtWar,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected annex block '{pReason}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectDecisionTargetLine(string pExpected)
        {
            string actual = DecisionTargetTextRules.TargetLine(pExpected == "" ? "" : "\u8d8a");
            if (actual != pExpected)
                throw new Exception($"Expected decision target line '{pExpected}', got '{actual}'.");
        }

        private static void ExpectHistoryEventLabel(string pKey, string pExpected)
        {
            string actual = WarDisplayLabelRules.EventLabel(pKey);
            if (actual != pExpected)
                throw new Exception($"Expected history event label '{pExpected}', got '{actual}'.");
        }

        private static void ExpectHistoryLocalizationRules()
        {
            if (WarDisplayLabelRules.Label("vassal_war", "en") != "Vassal War")
                throw new Exception("War labels should support English.");
            if (WarDisplayLabelRules.Label("vassal_war", "ch") != "\u9644\u5eb8\u6230\u722d")
                throw new Exception("War labels should support Traditional Chinese.");
            if (WarDisplayLabelRules.EventLabel("mandate_declared_foreign_pseudo", "en") != "Foreign Pseudo-Dynasty")
                throw new Exception("Mandate history event labels should support English.");
            if (WarDisplayLabelRules.EventLabel("mandate_declared_foreign_pseudo", "ch") != "\u5916\u65cf\u507d\u671d")
                throw new Exception("Mandate history event labels should support Traditional Chinese.");
            if (WarDisplayLabelRules.NormalizeEmbeddedKeys("war_start:tianmingrebel", "en") != "war_start:Rebel Mandate War")
                throw new Exception("Embedded history labels should normalize in English.");
            if (HistoryLocalizationRules.Text("aw_hist_kingdom_founded_suffix", "en") != " was founded")
                throw new Exception("History templates should provide English text.");
            if (HistoryLocalizationRules.Text("aw_hist_kingdom_founded_suffix", "ch") != " \u5efa\u7acb")
                throw new Exception("History templates should provide Traditional Chinese text.");
            if (HistoryLocalizationRules.Text("aw_hist_city_transfer_to", "en") != " transferred to ")
                throw new Exception("City history transfer templates should provide English text.");
            if (HistoryLocalizationRules.Text("aw_hist_slave_army_formed", "ch") != " \u958b\u59cb\u7de8\u7d44\u5974\u96b8\u8ecd")
                throw new Exception("Slave army history templates should provide Traditional Chinese text.");
            if (HistoryLocalizationRules.Text("aw_hist_slave_reason_city_fall", "en") != "captured when the city fell")
                throw new Exception("Slave reason labels should support English.");
            if (HistoryLocalizationRules.Text("aw_hist_slave_reason_military_merit", "ch") != "\u8ecd\u529f\u91cb\u5974")
                throw new Exception("Slave reason labels should support Traditional Chinese.");
            if (HistoryLocalizationRules.Text("aw_hist_posthumous_title_label", "en") != "Posthumous title:")
                throw new Exception("Posthumous tooltip labels should support English.");
            if (HistoryLocalizationRules.Text("aw_hist_posthumous_dimension_war", "ch") != "\u6230\u529f")
                throw new Exception("Posthumous score dimensions should support Traditional Chinese.");
            if (HistoryLocalizationRules.Text("aw_hist_paren_open", "en") != " (")
                throw new Exception("History punctuation templates should support English.");
            if (HistoryLocalizationRules.Text("aw_hist_colon", "en") != ": ")
                throw new Exception("History colon templates should support English spacing.");
            if (HistoryLocalizationRules.Text("aw_hist_goal_mandate_conquest", "en") != "Mandate Conquest")
                throw new Exception("Mandate conquest goal labels should support English.");
            if (HistoryLocalizationRules.Text("aw_hist_project_core", "ch") != "\u88fd\u9020\u6838\u5fc3")
                throw new Exception("War project labels should support Traditional Chinese.");
            if (HistoryLocalizationRules.Text("aw_map_can_mandate_conquest", "en") != "Can launch Mandate conquest")
                throw new Exception("War target tooltips should support English Mandate conquest labels.");
            if (WarDisplayLabelRules.Label("mandate_conquest", "en") != "Mandate Conquest")
                throw new Exception("Mandate conquest war labels should support English.");
            if (HistoryLocalizationRules.Text("aw_hist_war_claim_created_mid", "en") != " gained a casus belli against ")
                throw new Exception("War claim history templates should support English.");
            if (HistoryLocalizationRules.Text("aw_hist_vassal_absorb_mid", "ch") != " 吞併附庸 ")
                throw new Exception("Vassal history templates should support Traditional Chinese.");
            if (HistoryLocalizationRules.Text("aw_hist_former_king_after_fall_mid", "en") != " became ")
                throw new Exception("Former ruler history templates should support English.");
            if (HistoryLocalizationRules.Text("aw_hist_mandate_ruler_title_mid", "ch") != " 追上天命廟諡：")
                throw new Exception("Mandate ruler title history templates should support Traditional Chinese.");
            if (HistoryLocalizationRules.Text("aw_hist_general_high_risk_person", "en") != " held troops independently and alarmed the court")
                throw new Exception("General history templates should support English.");
            if (HistoryLocalizationRules.Text("aw_hist_era_changed", "ch") != "改元 ")
                throw new Exception("Era change history templates should support Traditional Chinese.");
        }

        private static void ExpectVassalWarBlocked(string pReason,
            bool pAttackerIsVassal,
            bool pDefenderIsSuzerain,
            bool pSameSuzerain,
            string pWarType)
        {
            bool allowed = VassalWarPermissionRules.CanDeclareWar(
                pAttackerIsVassal,
                pDefenderIsSuzerain,
                pSameSuzerain,
                pWarType,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected vassal war block '{pReason}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectVassalWarAllowed(
            bool pAttackerIsVassal,
            bool pDefenderIsSuzerain,
            bool pSameSuzerain,
            string pWarType)
        {
            bool allowed = VassalWarPermissionRules.CanDeclareWar(
                pAttackerIsVassal,
                pDefenderIsSuzerain,
                pSameSuzerain,
                pWarType,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected vassal war allowed, got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectAllianceBlockedForVassal()
        {
            bool allowed = VassalWarPermissionRules.CanCreateAlliance(pActorIsVassal: true, out string reason);
            if (allowed || reason != "vassal_no_alliance")
                throw new Exception($"Expected vassal alliance block, got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectVassalRelationRules()
        {
            if (!VassalRelationRules.CanSetVassal(
                    pBasicValid: true,
                    pVassalIsRebel: false,
                    pSuzerainIsRebel: false,
                    pSuzerainTitleAboveVassal: true,
                    pCycleDetected: false,
                    out string allowedReason) || allowedReason != "")
                throw new Exception($"Expected normal vassal relation allowed, got reason='{allowedReason}'.");

            if (VassalRelationRules.CanSetVassal(
                    pBasicValid: true,
                    pVassalIsRebel: true,
                    pSuzerainIsRebel: false,
                    pSuzerainTitleAboveVassal: true,
                    pCycleDetected: false,
                    out string rebelVassalReason) || rebelVassalReason != "rebel_no_vassal")
                throw new Exception("Peasant rebel kingdoms must not become vassals.");

            if (VassalRelationRules.CanSetVassal(
                    pBasicValid: true,
                    pVassalIsRebel: false,
                    pSuzerainIsRebel: true,
                    pSuzerainTitleAboveVassal: true,
                    pCycleDetected: false,
                    out string rebelSuzerainReason) || rebelSuzerainReason != "rebel_no_suzerain")
                throw new Exception("Peasant rebel kingdoms must not become suzerains.");

            if (VassalRelationRules.CanAbsorbVassal(
                    pBaseAllowed: true,
                    pSuzerainIsRebel: true,
                    pVassalIsRebel: false,
                    out string absorbReason) || absorbReason != "rebel_no_suzerain")
                throw new Exception("Peasant rebel kingdoms must not absorb vassals.");
        }

        private static void ExpectVassalIndependenceRules()
        {
            if (!VassalIndependenceRules.ShouldUseSuzerainPersonalPowerForBreakaway(
                    pOwnPower: 220f,
                    pSuzerainPersonalPower: 100f,
                    pSuzerainNetworkPower: 232f))
                throw new Exception("Strong vassal independence must compare against suzerain personal power, not a network score containing the vassal itself.");

            if (!VassalIndependenceRules.ShouldAttemptIndependence(
                    pOwnPower: 220f,
                    pSuzerainPersonalPower: 100f,
                    pYearsAsVassal: 6,
                    pOpinion: 20,
                    pRandomRoll: 0.99f))
                throw new Exception("A vassal far stronger than its suzerain should always attempt independence after a short relation.");

            if (VassalIndependenceRules.ShouldAttemptIndependence(
                    pOwnPower: 120f,
                    pSuzerainPersonalPower: 100f,
                    pYearsAsVassal: 6,
                    pOpinion: 20,
                    pRandomRoll: 0.0f))
                throw new Exception("A mildly stronger loyal vassal should not rush independence during the early relation.");

            if (!VassalIndependenceRules.ShouldAttemptIndependence(
                    pOwnPower: 80f,
                    pSuzerainPersonalPower: 120f,
                    pYearsAsVassal: 20,
                    pOpinion: -90,
                    pRandomRoll: 0.2f))
                throw new Exception("A long-term hostile vassal should still be able to attempt independence.");
        }

        private static void ExpectVassalWarSupportRules()
        {
            if (!VassalWarSupportRules.ShouldPullIntoSuzerainWar(
                    pSuzerainInWar: true,
                    pVassalAlreadyHelping: false,
                    pVassalAlreadyInWar: false,
                    pVassalOpposesSuzerain: false))
                throw new Exception("A vassal absent from its suzerain's war should be pulled into the suzerain side.");

            if (VassalWarSupportRules.ShouldPullIntoSuzerainWar(
                    pSuzerainInWar: true,
                    pVassalAlreadyHelping: true,
                    pVassalAlreadyInWar: true,
                    pVassalOpposesSuzerain: false))
                throw new Exception("A vassal already helping its suzerain must not be joined again.");

            if (VassalWarSupportRules.ShouldPullIntoSuzerainWar(
                    pSuzerainInWar: true,
                    pVassalAlreadyHelping: false,
                    pVassalAlreadyInWar: true,
                    pVassalOpposesSuzerain: true))
                throw new Exception("A vassal on the opposite side must not be force-switched by support maintenance.");

            if (VassalWarSupportRules.ShouldPullIntoSuzerainWar(
                    pSuzerainInWar: false,
                    pVassalAlreadyHelping: false,
                    pVassalAlreadyInWar: false,
                    pVassalOpposesSuzerain: false))
                throw new Exception("Vassal support maintenance should only run for active suzerain wars.");
        }

        private static void ExpectWarDecisionSummary(string pExpected, string pReason, string pTargetKingdom,
            string pTargetCity)
        {
            string actual = WarDecisionTargetTextRules.BuildSummary(pReason, pTargetKingdom, pTargetCity);
            if (actual != pExpected)
                throw new Exception($"Expected war decision summary '{pExpected}', got '{actual}'.");
        }

        private static void ExpectWarQueueAllowed(string pGoalType,
            bool pBasicAllowed,
            bool pHasNormalCb,
            bool pCanForceNoCb,
            bool pHasCoreTarget,
            bool pHasClaimTarget,
            bool pCanForceVassal,
            bool pIsIndependenceTarget,
            bool pHasRestorationTarget)
        {
            bool allowed = WarDecisionQueueRules.CanQueueGoal(
                pGoalType,
                pBasicAllowed,
                pHasNormalCb,
                pCanForceNoCb,
                pHasCoreTarget,
                pHasClaimTarget,
                pCanForceVassal,
                pIsIndependenceTarget,
                pHasRestorationTarget,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected war queue allowed for '{pGoalType}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectWarQueueBlocked(string pReason,
            string pGoalType,
            bool pBasicAllowed,
            bool pHasNormalCb,
            bool pCanForceNoCb,
            bool pHasCoreTarget,
            bool pHasClaimTarget,
            bool pCanForceVassal,
            bool pIsIndependenceTarget,
            bool pHasRestorationTarget)
        {
            bool allowed = WarDecisionQueueRules.CanQueueGoal(
                pGoalType,
                pBasicAllowed,
                pHasNormalCb,
                pCanForceNoCb,
                pHasCoreTarget,
                pHasClaimTarget,
                pCanForceVassal,
                pIsIndependenceTarget,
                pHasRestorationTarget,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected war queue block '{pReason}' for '{pGoalType}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectTargetScore(string pLabel, int pExpectedMin, string pGoalType,
            bool hasCore, bool hasStrongClaim, bool hasWeakClaim, int restorationStrength, int population)
        {
            int score = WarTargetSelectionRules.ScoreTarget(pGoalType, hasCore, hasStrongClaim,
                hasWeakClaim, restorationStrength, population);
            if (score < pExpectedMin)
                throw new Exception($"Expected {pLabel} score >= {pExpectedMin}, got {score}.");
        }

        private static void ExpectFocusId(long pExpected, long pCurrentFocusId, long pSelectedKingdomId)
        {
            long actual = MapModeFocusRules.ResolveFocusId(pCurrentFocusId, pSelectedKingdomId);
            if (actual != pExpected)
                throw new Exception($"Expected focus id {pExpected}, got {actual}.");
        }

        private static void ExpectVassalTitleUpgradeBlocked(string pReason, int pSuzerainTitle,
            int pVassalCurrentTitle)
        {
            bool allowed = KingdomTitleUpgradeRules.CanVassalUpgradeUnderSuzerain(
                pSuzerainTitle,
                pVassalCurrentTitle,
                out string reason);
            if (allowed || reason != pReason)
                throw new Exception($"Expected vassal title block '{pReason}', got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectVassalTitleUpgradeAllowed(int pSuzerainTitle, int pVassalCurrentTitle)
        {
            bool allowed = KingdomTitleUpgradeRules.CanVassalUpgradeUnderSuzerain(
                pSuzerainTitle,
                pVassalCurrentTitle,
                out string reason);
            if (!allowed || reason != "")
                throw new Exception($"Expected vassal title upgrade allowed, got allowed={allowed}, reason='{reason}'.");
        }

        private static void ExpectAwMapModeRuntimeType()
        {
            if (AWMapModeMetaRules.ResolveRuntimeMetaTypeId("aw_tech_level_mapmode") != 210)
                throw new Exception("Tech mapmode must use its own AW3 meta type.");

            if (AWMapModeMetaRules.ResolveRuntimeMetaTypeId("aw_vassal_mapmode") != 211)
                throw new Exception("Vassal mapmode must use its own AW3 meta type.");

            if (AWMapModeMetaRules.ResolveRuntimeMetaTypeId("aw_core_mapmode") != 212)
                throw new Exception("Core mapmode must use its own AW3 meta type.");

            if (AWMapModeMetaRules.ResolveRuntimeMetaTypeId("aw_claim_mapmode") != 213)
                throw new Exception("Claim mapmode must use its own AW3 meta type.");

            if (AWMapModeMetaRules.ResolveRuntimeMetaTypeId("aw_mandate_dynasty_mapmode") != 214)
                throw new Exception("Mandate dynasty mapmode must use its own AW3 meta type.");

            if (AWMapModeMetaRules.ResolveRuntimeMetaTypeId("aw_mandate_core_mapmode") != 215)
                throw new Exception("Mandate core mapmode must use its own AW3 meta type.");

            if (AWMapModeMetaRules.ResolveRuntimeMetaTypeId("aw_development_mapmode") != 216)
                throw new Exception("Development mapmode must use its own AW3 meta type.");

            string powerId = "aw_core_mapmode";
            if (AWMapModeMetaRules.ResolveOptionId(powerId) != "map_" + powerId)
                throw new Exception("AW3 mapmodes must bind option_id to the original map option key.");

            if (AWMapModeMetaRules.ResolveAssetOptionId("aw_development_mapmode") !=
                AWMapModeMetaRules.ResolveOptionId("aw_tech_level_mapmode"))
                throw new Exception("Development layer asset must share the tech map option to avoid orphan options.");

            if (AWMapModeMetaRules.ResolveAssetOptionId("aw_claim_mapmode") !=
                AWMapModeMetaRules.ResolveOptionId("aw_core_mapmode"))
                throw new Exception("Claim layer asset must share the core/claim map option to avoid orphan options.");

            if (AWMapModeMetaRules.ResolvePowerOptionZoneId(powerId) != powerId)
                throw new Exception("AW3 mapmodes must bind power_option_zone_id to their power id.");

            if (AWMapModeMetaRules.ShouldRenderWithVanillaKingdomAsset())
                throw new Exception("AW3 mapmodes must not render through the vanilla kingdom meta asset.");

            if (AWMapModeMetaRules.ShouldUseMainZoneForColorContext())
                throw new Exception("AW3 mapmodes must use their MetaTypeAsset getter as the color source.");

            if (AWMapModeMetaRules.ShouldOverrideKingdomGetColor())
                throw new Exception("AW3 mapmodes must not override Kingdom.getColor for zone colors.");
        }

        private static void ExpectAwMapModeNameplateRules()
        {
            var required = AWMapModeNameplateRules.GetRequiredNameplateMetaTypeIds();
            if (required.Length != 7)
                throw new Exception("Every AW3 custom mapmode must register a matching nameplate asset.");
            if (required[0] != 210 || required[6] != 216)
                throw new Exception("AW3 nameplate registration list must cover the custom meta type range.");

            string[] optionLocales = AWMapModeNameplateRules.GetDefaultZoneOptionLocaleIds();
            if (optionLocales.Length == 0 || string.IsNullOrEmpty(optionLocales[0]))
                throw new Exception("AW3 mapmode options must have a non-empty zone option locale id.");
        }

        private static void ExpectAwMapModePowerRules()
        {
            if (AWMapModePowerRules.ResolveForcedMapModeForLayerPowerId() != 0)
                throw new Exception("AW3 custom layer powers must be option-driven, not force_map_mode-driven.");

            if (!AWMapModePowerRules.ShouldUseGodPowerMultiToggle(2))
                throw new Exception("Two-option AW3 mapmode powers must enable GodPower.multi_toggle.");

            if (AWMapModePowerRules.ShouldUseGodPowerMultiToggle(1))
                throw new Exception("Single-option AW3 mapmode powers must not enable GodPower.multi_toggle.");
        }

        private static void ExpectTechMapModeOptionRules()
        {
            string[] optionLocales = AWMapModeNameplateRules.GetTechZoneOptionLocaleIds();
            if (optionLocales.Length != 2)
                throw new Exception("Tech mapmode must expose two zone options: city tech and development.");
            if (optionLocales[0] != "aw_tech_mapmode_option_tech" ||
                optionLocales[1] != "aw_tech_mapmode_option_development")
                throw new Exception("Tech mapmode option locale ids are incorrect.");

            if (TechMapModeOptionRules.ResolveLayer(0) != TechMapModeLayer.CityTech)
                throw new Exception("Tech mapmode option 0 must be city tech.");
            if (TechMapModeOptionRules.ResolveLayer(1) != TechMapModeLayer.Development)
                throw new Exception("Tech mapmode option 1 must be development.");
            if (TechMapModeOptionRules.ResolveLayer(99) != TechMapModeLayer.Development)
                throw new Exception("Tech mapmode unknown option must fall back to development like vanilla option 2 fallback.");
        }

        private static void ExpectWarMapModeOptionRules()
        {
            string[] optionLocales = AWMapModeNameplateRules.GetWarZoneOptionLocaleIds();
            if (optionLocales.Length != 2)
                throw new Exception("War mapmode must expose two zone options: core and claim.");
            if (optionLocales[0] != "aw_war_mapmode_option_core" ||
                optionLocales[1] != "aw_war_mapmode_option_claim")
                throw new Exception("War mapmode option locale ids are incorrect.");

            if (WarMapModeOptionRules.ResolveLayer(0) != WarMapModeLayer.Core)
                throw new Exception("War mapmode option 0 must be core.");
            if (WarMapModeOptionRules.ResolveLayer(1) != WarMapModeLayer.Claim)
                throw new Exception("War mapmode option 1 must be claim.");
            if (WarMapModeOptionRules.ResolveLayer(99) != WarMapModeLayer.Claim)
                throw new Exception("War mapmode unknown option must fall back to claim like vanilla option 2 fallback.");
        }

        private static void ExpectAwMapModeStatusCacheKeys()
        {
            string key = AWMapModeMetaRules.BuildFocusedCityStatusCacheKey(12, 34);
            if (key != "12:34")
                throw new Exception($"Expected focused city cache key '12:34', got '{key}'.");

            if (AWMapModeMetaRules.BuildFocusedCityStatusCacheKey(-1, 34) != "")
                throw new Exception("Focused city cache key must be empty for invalid focus.");

            if (AWMapModeMetaRules.BuildFocusedCityStatusCacheKey(12, -1) != "")
                throw new Exception("Focused city cache key must be empty for invalid city.");

            if (AWMapModeMetaRules.BuildCityStatusCacheKey(34) != "34")
                throw new Exception("City status cache key must be the city id.");

            if (AWMapModeMetaRules.BuildCityStatusCacheKey(-1) != "")
                throw new Exception("City status cache key must be empty for invalid city.");
        }

        private static void ExpectMandateDynastyMapRules()
        {
            if (MandateDynastyMapRules.ResolveStatus(pIsMandateKingdom: true, pRootSuzerainIsMandate: false) !=
                "mandate")
                throw new Exception("Mandate dynasty map must mark the mandate kingdom itself.");

            if (MandateDynastyMapRules.ResolveStatus(pIsMandateKingdom: false, pRootSuzerainIsMandate: true) !=
                "vassal")
                throw new Exception("Mandate dynasty map must mark mandate vassals separately.");

            if (MandateDynastyMapRules.ResolveStatus(pIsMandateKingdom: false, pRootSuzerainIsMandate: false) != "")
                throw new Exception("Mandate dynasty map must not draw kingdoms outside the mandate order.");

            if (!MandateDynastyMapRules.ShouldDrawStatus("mandate") ||
                !MandateDynastyMapRules.ShouldDrawStatus("vassal") ||
                MandateDynastyMapRules.ShouldDrawStatus(""))
                throw new Exception("Mandate dynasty map must skip non-mandate kingdoms before drawing their zones.");

            if (MandateDynastyMapRules.BuildStatusCacheKey(9, 12) != "9:12")
                throw new Exception("Mandate dynasty status cache key must bind current mandate and zone kingdom.");

            if (MandateDynastyMapRules.BuildStatusCacheKey(-1, 12) != "" ||
                MandateDynastyMapRules.BuildStatusCacheKey(9, -1) != "")
                throw new Exception("Mandate dynasty status cache key must be empty for invalid ids.");

            if (!MandateDynastyMapRules.ShouldUseKingdomColor("mandate") ||
                !MandateDynastyMapRules.ShouldUseKingdomColor("vassal") ||
                MandateDynastyMapRules.ShouldUseKingdomColor(""))
                throw new Exception("Mandate dynasty map should draw mandate-order kingdoms using their own kingdom colors.");

            if (MandateCoreMapRules.HexForStatus("controlled") != "#226B3A" ||
                MandateCoreMapRules.HexForStatus("vassal") != "#4F8F45" ||
                MandateCoreMapRules.HexForStatus("lost") != "#B3124B")
                throw new Exception("Mandate core map controlled/vassal/lost colors should use the green-to-red semantic palette.");

            if (AWMapModeMetaRules.NormalizeMapColorHex("  #d72f8a ") != "#D72F8A")
                throw new Exception("Map color hex cache keys must be normalized.");

            if (AWMapModeMetaRules.NormalizeMapColorHex("") != "#242424")
                throw new Exception("Empty map colors must normalize to the fallback color.");

            if (!AWMapModeMetaRules.ShouldUseCityTooltipForPowerId("aw_tech_level_mapmode") ||
                !AWMapModeMetaRules.ShouldUseCityTooltipForPowerId("aw_development_mapmode") ||
                !AWMapModeMetaRules.ShouldUseCityTooltipForPowerId("aw_core_mapmode") ||
                !AWMapModeMetaRules.ShouldUseCityTooltipForPowerId("aw_claim_mapmode") ||
                !AWMapModeMetaRules.ShouldUseCityTooltipForPowerId("aw_mandate_core_mapmode"))
                throw new Exception("City-scoped mapmodes must pass the hovered city into tooltip rendering.");

            if (AWMapModeMetaRules.ShouldUseCityTooltipForPowerId("aw_vassal_mapmode") ||
                AWMapModeMetaRules.ShouldUseCityTooltipForPowerId("aw_mandate_dynasty_mapmode"))
                throw new Exception("Network-level mapmodes should keep kingdom-scoped tooltip rendering.");
        }

        private static void ExpectMandateCoreTooltipRules()
        {
            string line = MandateCoreTooltipRules.BuildPointedKingdomControlLine("\u95fd", 0.434f);
            if (!line.Contains("\u6307\u5411\u56fd\u5bb6") ||
                !line.Contains("\u95fd") ||
                !line.Contains("43%"))
                throw new Exception("Mandate legal-core tooltip must show the hovered kingdom's own control rate.");

            if (MandateCoreTooltipRules.BuildPointedKingdomControlLine("", 0.5f) != "")
                throw new Exception("Mandate legal-core tooltip should skip pointed kingdom control when no kingdom is hovered.");

            string countLine = MandateCoreTooltipRules.BuildPointedKingdomCoreCountLine(
                "\u95fd", 3, 12, "\u6307\u5411\u56fd\u5bb6\u62e5\u6709\u6cd5\u7406\u5730\uff1a");
            if (!countLine.Contains("\u95fd") || !countLine.Contains("3/12"))
                throw new Exception("Mandate legal-core tooltip must show the hovered kingdom's legal-core city count.");

            string cityLine = MapModeTooltipTextRules.BuildPointedCityStatusBlock(
                "\u6307\u5411\u57ce\u5e02\uff1a",
                "\u57ce\u5e02\u72b6\u6001\uff1a",
                "\u8fdb\u5ea6\uff1a",
                "\u4f1a\u7a3d",
                "\u5236\u9020\u6838\u5fc3",
                45.0,
                100.0);
            if (!cityLine.Contains("\u6307\u5411\u57ce\u5e02\uff1a\u4f1a\u7a3d") ||
                !cityLine.Contains("\u57ce\u5e02\u72b6\u6001\uff1a\u5236\u9020\u6838\u5fc3") ||
                !cityLine.Contains("\u8fdb\u5ea6\uff1a45%"))
                throw new Exception("Mapmode tooltips must include hovered city status and project progress.");
        }

        private static void ExpectMandateSuccessionRules()
        {
            if (!MandateSuccessionRules.ShouldBlockPeacefulFellApart(
                    pIsActiveMandate: true,
                    pMandateValue: 88,
                    pCrisisLevel: "golden",
                    pHasSuccessionCandidate: false))
                throw new Exception("Golden mandate must not use vanilla peaceful fell-apart after king death.");

            if (!MandateSuccessionRules.ShouldBlockPeacefulFellApart(
                    pIsActiveMandate: true,
                    pMandateValue: 45,
                    pCrisisLevel: "stable",
                    pHasSuccessionCandidate: true))
                throw new Exception("Mandate kingdom with any succession candidate must block peaceful fell-apart.");

            if (MandateSuccessionRules.ShouldBlockPeacefulFellApart(
                    pIsActiveMandate: false,
                    pMandateValue: 88,
                    pCrisisLevel: "golden",
                    pHasSuccessionCandidate: true))
                throw new Exception("Non-mandate kingdoms must keep vanilla fell-apart behavior.");

            if (MandateSuccessionRules.ChildScarcityPenalty(
                    pAdultSons: 1,
                    pUnderageSons: 0,
                    pTotalChildren: 3,
                    pHasKing: true,
                    pYearsSinceAccession: 12) != 0)
                throw new Exception("Mandate king with an adult son should not receive succession scarcity penalty.");

            if (MandateSuccessionRules.ChildScarcityPenalty(
                    pAdultSons: 0,
                    pUnderageSons: 1,
                    pTotalChildren: 2,
                    pHasKing: true,
                    pYearsSinceAccession: 12) != 0)
                throw new Exception("Mandate king with children should not receive the no-offspring mandate penalty.");

            if (MandateSuccessionRules.ChildScarcityPenalty(
                    pAdultSons: 0,
                    pUnderageSons: 0,
                    pTotalChildren: 2,
                    pHasKing: true,
                    pYearsSinceAccession: 12) != 0)
                throw new Exception("Mandate king with daughters should not receive the no-offspring mandate penalty.");

            if (MandateSuccessionRules.ChildScarcityPenalty(
                    pAdultSons: 0,
                    pUnderageSons: 0,
                    pTotalChildren: 0,
                    pHasKing: true,
                    pYearsSinceAccession: 9) != 0)
                throw new Exception("Mandate king within ten accession years should not receive no-offspring penalty.");

            if (MandateSuccessionRules.ChildScarcityPenalty(
                    pAdultSons: 0,
                    pUnderageSons: 0,
                    pTotalChildren: 0,
                    pHasKing: true,
                    pYearsSinceAccession: 10) != -4)
                throw new Exception("Mandate king without children after ten accession years should receive severe penalty.");

            if (!MandateSuccessionRules.CanUseUnderageDirectSonFallback(
                    pIsDirectSon: true,
                    pIsMale: true,
                    pIsAlive: true,
                    pIsKing: false,
                    pHasAdultDirectSon: false))
                throw new Exception("Underage direct son should be a succession fallback when there is no adult son.");

            if (MandateSuccessionRules.CanUseUnderageDirectSonFallback(
                    pIsDirectSon: true,
                    pIsMale: true,
                    pIsAlive: true,
                    pIsKing: false,
                    pHasAdultDirectSon: true))
                throw new Exception("Underage direct son must not replace an adult direct son.");

            if (MandateSuccessionRules.CanUseUnderageDirectSonFallback(
                    pIsDirectSon: false,
                    pIsMale: true,
                    pIsAlive: true,
                    pIsKing: false,
                    pHasAdultDirectSon: false))
                throw new Exception("Non-direct children must not use the underage direct-son fallback.");

            if (!MandateSuccessionRules.ShouldRecordSuccessionCrisis(
                    pLastRecordedYear: -1,
                    pCurrentYear: 50))
                throw new Exception("Succession crisis should record when it has never been recorded.");

            if (MandateSuccessionRules.ShouldRecordSuccessionCrisis(
                    pLastRecordedYear: 50,
                    pCurrentYear: 50))
                throw new Exception("Succession crisis must not spam multiple records in the same year.");

            if (MandateSuccessionRules.ResolveSuccessionMode(
                    hasAdultDirectSon: true,
                    hasUnderageDirectSon: true,
                    hasRegisteredHeir: true,
                    hasCollateralRestoration: true,
                    hasClanFallback: true,
                    hasLeaderFallback: true) != "direct")
                throw new Exception("Adult direct sons must outrank all succession fallback layers.");

            if (MandateSuccessionRules.ResolveSuccessionMode(
                    hasAdultDirectSon: false,
                    hasUnderageDirectSon: true,
                    hasRegisteredHeir: true,
                    hasCollateralRestoration: true,
                    hasClanFallback: true,
                    hasLeaderFallback: true) != "underage_direct")
                throw new Exception("Underage direct sons must outrank registered and collateral fallback.");

            if (MandateSuccessionRules.ResolveSuccessionMode(
                    hasAdultDirectSon: false,
                    hasUnderageDirectSon: false,
                    hasRegisteredHeir: false,
                    hasCollateralRestoration: true,
                    hasClanFallback: true,
                    hasLeaderFallback: true) != "collateral_restore")
                throw new Exception("Collateral restoration must run before ordinary clan and leader fallback.");

            if (!MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                    isXia: true,
                    isMale: true,
                    isAlive: true,
                    isAdult: true,
                    isKing: false,
                    hasMadness: false,
                    sameLineage: true,
                    belongsToLegitimateShi: true))
                throw new Exception("A living Xia male in the legitimate line should be a collateral restoration candidate.");

            if (MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                    isXia: true,
                    isMale: true,
                    isAlive: true,
                    isAdult: true,
                    isKing: false,
                    hasMadness: false,
                    sameLineage: true,
                    belongsToLegitimateShi: false,
                    canTraceToLegitimateBranch: false))
                throw new Exception("Collateral restoration must not accept same-surname or same-lineage candidates without a genealogy trace.");

            if (!MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                    isXia: true,
                    isMale: true,
                    isAlive: true,
                    isAdult: true,
                    isKing: false,
                    hasMadness: false,
                    sameLineage: true,
                    belongsToLegitimateShi: false,
                    canTraceToLegitimateBranch: true))
                throw new Exception("Collateral restoration should accept a branch founder or descendant that traces back to the legitimate line.");
        }

        private static void ExpectCollateralSuccessionFallbackRules()
        {
            if (MandateSuccessionRules.ShouldUseOrdinaryClanFallbackAfterCollateralSearch(
                    hasDirectSon: false,
                    hasRegisteredHeir: false,
                    hasCollateralRestorationCandidate: false,
                    isMandateOrLegitimateDynasty: true))
                throw new Exception("A legitimate dynasty must not pick an ordinary royal-clan fallback when no traceable collateral heir exists.");

            if (!MandateSuccessionRules.ShouldUseOrdinaryClanFallbackAfterCollateralSearch(
                    hasDirectSon: false,
                    hasRegisteredHeir: false,
                    hasCollateralRestorationCandidate: false,
                    isMandateOrLegitimateDynasty: false))
                throw new Exception("Non-legitimate fallback rules may still use ordinary royal-clan fallback.");

            if (MandateSuccessionRules.ShouldUseOrdinaryClanFallbackAfterCollateralSearch(
                    hasDirectSon: true,
                    hasRegisteredHeir: false,
                    hasCollateralRestorationCandidate: false,
                    isMandateOrLegitimateDynasty: false))
                throw new Exception("Ordinary clan fallback should not run when a direct son already exists.");
        }

        private static void ExpectHeirRecallRules()
        {
            if (!HeirRecallRules.ShouldRecallForSuccession(
                    pWasRegisteredHeir: true,
                    pIsNewKing: true,
                    pIsCityLeader: true,
                    pIsArmyCaptain: false,
                    pIsGeneral: false,
                    pHasFief: false))
                throw new Exception("A registered heir who becomes king must be recalled from a city leader post.");

            if (!HeirRecallRules.ShouldRecallForSuccession(
                    pWasRegisteredHeir: false,
                    pIsNewKing: true,
                    pIsCityLeader: false,
                    pIsArmyCaptain: false,
                    pIsGeneral: true,
                    pHasFief: true))
                throw new Exception("A new king must be recalled from general/fief state even when the heir flag was lost.");

            if (HeirRecallRules.ShouldRecallForSuccession(
                    pWasRegisteredHeir: true,
                    pIsNewKing: false,
                    pIsCityLeader: true,
                    pIsArmyCaptain: true,
                    pIsGeneral: true,
                    pHasFief: true))
                throw new Exception("Actors who are not the new king must not be recalled by succession cleanup.");

            if (HeirRecallRules.ShouldRecallForSuccession(
                    pWasRegisteredHeir: true,
                    pIsNewKing: true,
                    pIsCityLeader: false,
                    pIsArmyCaptain: false,
                    pIsGeneral: false,
                    pHasFief: false))
                throw new Exception("A clean heir succession should not trigger extra recall cleanup.");

            if (!HeirRecallRules.ShouldPreferRegisteredHeirBeforeLeaderFallback(pHasRegisteredHeir: true))
                throw new Exception("Registered heirs must be preferred before vanilla leader fallback succession.");
            if (HeirRecallRules.ShouldPreferRegisteredHeirBeforeLeaderFallback(pHasRegisteredHeir: false))
                throw new Exception("Leader fallback should proceed when there is no registered heir.");
            if (!HeirRecallRules.ShouldUseLeaderFallbackForXiaizedSuccession(
                    pHasRegisteredHeir: false,
                    pHasLeaderCandidate: true))
                throw new Exception("Xia kingdoms should allow a leader fallback when no registered heir exists.");
            if (HeirRecallRules.ShouldUseLeaderFallbackForXiaizedSuccession(
                    pHasRegisteredHeir: true,
                    pHasLeaderCandidate: true))
                throw new Exception("Registered heirs must still beat Xiaized leader fallback.");
            if (HeirRecallRules.ShouldUseLeaderFallbackForXiaizedSuccession(
                    pHasRegisteredHeir: false,
                    pHasLeaderCandidate: false))
                throw new Exception("Xiaized leader fallback needs a real leader candidate.");
            if (!HeirRecallRules.ShouldRecallForeignSelectedHeir(
                    pHasHeir: true,
                    pSameKingdom: false,
                    pHasCapital: true))
                throw new Exception("A selected heir living abroad must be recalled to the Xia capital.");
            if (HeirRecallRules.ShouldRecallForeignSelectedHeir(
                    pHasHeir: true,
                    pSameKingdom: true,
                    pHasCapital: true))
                throw new Exception("A selected heir already in the kingdom must not be migrated.");
            if (HeirRecallRules.ShouldRecallForeignSelectedHeir(
                    pHasHeir: true,
                    pSameKingdom: false,
                    pHasCapital: false))
                throw new Exception("Foreign heir recall needs a valid capital city.");
        }

        private static void ExpectAncestryDisplayRules()
        {
            string full = AncestryDisplayRules.FormatNobleAncestorLabel(
                "\u5f00\u5c01",
                "\u59ec",
                "\u59ec\u67d0",
                "\u5468\u6587\u738b",
                25f);
            if (full != "\u5f00\u5c01\u59ec\u6c0f 25.0% \u59ec\u67d0 \u5468\u6587\u738b")
                throw new Exception($"Unexpected noble ancestor label: '{full}'.");

            string noCity = AncestryDisplayRules.FormatNobleAncestorLabel(
                "",
                "\u59ec",
                "\u59ec\u67d0",
                "\u5468\u6587\u738b",
                25f);
            if (noCity != "\u59ec\u6c0f 25.0% \u59ec\u67d0 \u5468\u6587\u738b")
                throw new Exception($"Unexpected noble ancestor label without city: '{noCity}'.");

            string noTitle = AncestryDisplayRules.FormatNobleAncestorLabel(
                "\u5f00\u5c01",
                "\u59ec",
                "\u59ec\u67d0",
                "",
                25f);
            if (noTitle != "\u5f00\u5c01\u59ec\u6c0f 25.0% \u59ec\u67d0")
                throw new Exception($"Unexpected noble ancestor label without title: '{noTitle}'.");

            if (AncestryDisplayRules.PercentForAncestorDistance(1).ToString("0.0") != "50.0")
                throw new Exception("Parent generation must contribute 50.0%.");
            if (AncestryDisplayRules.PercentForAncestorDistance(2).ToString("0.0") != "25.0")
                throw new Exception("Grandparent generation must contribute 25.0%.");
            if (AncestryDisplayRules.PercentForAncestorDistance(3).ToString("0.0") != "12.5")
                throw new Exception("Great-grandparent generation must contribute 12.5%.");
            if (!AncestryDisplayRules.ShouldUseNobleAncestorRowsForSocialSection(1))
                throw new Exception("Social ancestry should prefer traced noble ancestor rows when available.");
            if (AncestryDisplayRules.ShouldUseNobleAncestorRowsForSocialSection(0))
                throw new Exception("Social ancestry should fall back to aggregate rows without noble ancestors.");
        }

        private static void ExpectLineageBranchRules()
        {
            if (!LineageBranchRules.IsDirectSuccessionFromKnownKing(
                    newKingParent1Id: 101,
                    newKingParent2Id: 22,
                    previousKingId: -1,
                    recordedPreSuccessionKingId: 101))
                throw new Exception("A recorded pre-succession king id matching a parent must count as direct succession.");

            if (LineageBranchRules.IsDirectSuccessionFromKnownKing(
                    newKingParent1Id: 101,
                    newKingParent2Id: 22,
                    previousKingId: -1,
                    recordedPreSuccessionKingId: 303))
                throw new Exception("A recorded pre-succession king id that is not a parent must not count as direct succession.");

            if (LineageBranchRules.ShouldFoundKingBranch(
                    validKingdom: true,
                    isXiaKing: true,
                    wasHeir: false,
                    isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: true,
                    isCollateralRestoration: false,
                    hasLineage: true,
                    hasShi: true,
                    isHistoricalFigure: false,
                    isLineageRootFounder: false,
                    aliveInCurrentShi: 12,
                    minAliveForNewBranch: 8,
                    currentKingdomId: 20,
                    originKingdomId: 10,
                    alreadyFoundedForKingdom: false))
                throw new Exception("Direct father-to-son succession must not create a new shi branch.");

            if (LineageBranchRules.ShouldFoundKingBranch(
                    validKingdom: true,
                    isXiaKing: true,
                    wasHeir: true,
                    isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: false,
                    isCollateralRestoration: false,
                    hasLineage: true,
                    hasShi: true,
                    isHistoricalFigure: false,
                    isLineageRootFounder: false,
                    aliveInCurrentShi: 12,
                    minAliveForNewBranch: 8,
                    currentKingdomId: 20,
                    originKingdomId: 10,
                    alreadyFoundedForKingdom: false))
                throw new Exception("Registered heir succession must not create a new shi branch.");

            if (LineageBranchRules.ShouldFoundKingBranch(
                    validKingdom: true,
                    isXiaKing: true,
                    wasHeir: false,
                    isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: false,
                    isCollateralRestoration: true,
                    hasLineage: true,
                    hasShi: true,
                    isHistoricalFigure: false,
                    isLineageRootFounder: false,
                    aliveInCurrentShi: 12,
                    minAliveForNewBranch: 8,
                    currentKingdomId: 20,
                    originKingdomId: 10,
                    alreadyFoundedForKingdom: false))
                throw new Exception("Collateral restoration must not create a new shi branch.");

            if (LineageBranchRules.ShouldApplyCollateralRestoration(
                    successionMode: "collateral_restore",
                    wasHeir: true,
                    isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: false))
                throw new Exception("Registered heirs must not be reinterpreted as collateral restoration.");

            if (LineageBranchRules.ShouldApplyCollateralRestoration(
                    successionMode: "collateral_restore",
                    wasHeir: false,
                    isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: true))
                throw new Exception("Direct sons must not be reinterpreted as collateral restoration.");

            if (!LineageBranchRules.ShouldApplyCollateralRestoration(
                    successionMode: "collateral_restore",
                    wasHeir: false,
                    isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: false))
                throw new Exception("True collateral restoration should still be allowed.");

            if (!LineageBranchRules.ShouldFoundKingBranch(
                    validKingdom: true,
                    isXiaKing: true,
                    wasHeir: false,
                    isCurrentHeir: false,
                    isDirectSuccessionFromPreviousKing: false,
                    isCollateralRestoration: false,
                    hasLineage: true,
                    hasShi: true,
                    isHistoricalFigure: false,
                    isLineageRootFounder: false,
                    aliveInCurrentShi: 12,
                    minAliveForNewBranch: 8,
                    currentKingdomId: 20,
                    originKingdomId: 10,
                    alreadyFoundedForKingdom: false))
                throw new Exception("A non-heir noble king founding a separate kingdom may create a new shi branch.");
        }

        private static void ExpectRoyalGuardSelectionRules()
        {
            if (!RoyalGuardSelectionRules.IsEligibleCore(
                    isXia: true,
                    sameKingdom: true,
                    isMale: true,
                    isBoat: false,
                    isRekt: false,
                    isAdult: true,
                    isKing: false,
                    isCityLeader: false,
                    isSlave: false,
                    isRetiredSoldier: false,
                    isCurrentHeir: false,
                    hasMadness: false,
                    isHistoricalFigure: false))
                throw new Exception("Adult male Xia soldiers should remain eligible for royal guard selection.");

            if (!RoyalGuardSelectionRules.IsEligibleCore(
                    isXia: true,
                    sameKingdom: true,
                    isMale: false,
                    isBoat: false,
                    isRekt: false,
                    isAdult: true,
                    isKing: false,
                    isCityLeader: false,
                    isSlave: false,
                    isRetiredSoldier: false,
                    isCurrentHeir: false,
                    hasMadness: false,
                    isHistoricalFigure: false))
                throw new Exception("Female actors should be eligible for royal guard service when other guard rules pass.");
        }

        private static void ExpectXiaAuthorityGenderRules()
        {
            if (!XiaAuthorityGenderRules.ShouldAllowSetLeader(
                    pIsXiaActor: true,
                    pIsMale: true,
                    pIsNewAppointment: true))
                throw new Exception("Male Xia actors should be allowed to become city leaders.");
            if (XiaAuthorityGenderRules.ShouldAllowSetLeader(
                    pIsXiaActor: true,
                    pIsMale: false,
                    pIsNewAppointment: true))
                throw new Exception("Female Xia actors must not become city leaders.");
            if (XiaAuthorityGenderRules.ShouldAllowSetLeader(
                    pIsXiaActor: true,
                    pIsMale: false,
                    pIsNewAppointment: false))
                throw new Exception("Loaded or restored female Xia leaders should be rejected and replaced.");
            if (!XiaAuthorityGenderRules.ShouldAllowSetLeader(
                    pIsXiaActor: false,
                    pIsMale: false,
                    pIsNewAppointment: true))
                throw new Exception("Non-Xia leader rules should be left to the base game.");

            if (!XiaAuthorityGenderRules.ShouldAllowSetKing(
                    pFromLoad: false,
                    pCandidateIsMale: true,
                    pCandidateIsXia: true,
                    pKingdomIsXia: true))
                throw new Exception("Male Xia actors should be allowed to become king.");
            if (XiaAuthorityGenderRules.ShouldAllowSetKing(
                    pFromLoad: false,
                    pCandidateIsMale: false,
                    pCandidateIsXia: true,
                    pKingdomIsXia: true))
                throw new Exception("Female Xia actors must not become king.");
            if (XiaAuthorityGenderRules.ShouldAllowSetKing(
                    pFromLoad: true,
                    pCandidateIsMale: false,
                    pCandidateIsXia: true,
                    pKingdomIsXia: true))
                throw new Exception("Loaded female Xia kings should be rejected so succession can recover.");
            if (!XiaAuthorityGenderRules.ShouldAllowSetKing(
                    pFromLoad: false,
                    pCandidateIsMale: false,
                    pCandidateIsXia: false,
                    pKingdomIsXia: false))
                throw new Exception("Non-Xia king rules should be left to the base game.");
            if (!XiaAuthorityGenderRules.ShouldAllowSetKing(
                    pFromLoad: false,
                    pCandidateIsMale: true,
                    pCandidateIsXia: false,
                    pKingdomIsXia: true))
                throw new Exception("Male human candidates must be allowed to inherit Xia kingdoms for Xiaization.");
        }

        private static void ExpectHeirCandidateRules()
        {
            if (!HeirCandidateRules.IsFallbackEligibleCore(
                    isSuitable: true,
                    sameKingdom: true,
                    hasLineage: true,
                    hasShi: true))
                throw new Exception("Fallback heirs with complete AW3 lineage should be eligible.");

            if (HeirCandidateRules.IsBasicMaleSuccessionEligible(
                    isAlive: true,
                    sameAsCurrentKing: false,
                    isMale: true,
                    isCurrentKing: false,
                    isAdult: true,
                    hasMadness: false,
                    isSlave: true))
                throw new Exception("Enslaved actors must not be selected as monarchy succession candidates while free candidates can exist.");

            if (!HeirCandidateRules.IsBasicMaleSuccessionEligible(
                    isAlive: true,
                    sameAsCurrentKing: false,
                    isMale: true,
                    isCurrentKing: false,
                    isAdult: true,
                    hasMadness: false,
                    isSlave: false))
                throw new Exception("Free adult male actors should remain eligible for monarchy succession.");

            if (HeirCandidateRules.IsUnderageDirectSonEligible(
                    isDirectSon: true,
                    isMale: true,
                    isAlive: true,
                    isCurrentKing: false,
                    hasAdultDirectSon: false,
                    hasMadness: false,
                    isSlave: true))
                throw new Exception("Enslaved underage direct sons must not be used as succession fallbacks.");

            if (HeirCandidateRules.IsFallbackEligibleCore(
                    isSuitable: true,
                    sameKingdom: true,
                    hasLineage: false,
                    hasShi: false))
                throw new Exception("Vanilla royal clan commoners without AW3 lineage must not become fallback heirs.");
        }

        private static void ExpectRoyalSuccessionBirthRules()
        {
            if (!RoyalSuccessionBirthRules.ShouldRefreshHeirForNewChild(
                    childIsMale: true,
                    fatherIsCurrentKing: true))
                throw new Exception("A new direct royal son should refresh the registered heir.");
            if (RoyalSuccessionBirthRules.ShouldRefreshHeirForNewChild(
                    childIsMale: false,
                    fatherIsCurrentKing: true))
                throw new Exception("A daughter should not refresh male-only succession.");
            if (RoyalSuccessionBirthRules.KingChildCap(hasLivingDirectSon: false) <=
                RoyalSuccessionBirthRules.KingChildCap(hasLivingDirectSon: true))
                throw new Exception("Kings without a living direct son should get a wider child cap.");
        }

        private static void ExpectFormerRulerPosthumousRules()
        {
            if (!FormerRulerPosthumousRules.ShouldTryPosthumousOnDeath(
                    isCurrentKing: false,
                    hasUntitledClosedReign: true))
                throw new Exception("Former rulers with an untitled closed reign should receive posthumous review on death.");
            if (FormerRulerPosthumousRules.ShouldTryPosthumousOnDeath(
                    isCurrentKing: true,
                    hasUntitledClosedReign: true))
                throw new Exception("Current kings are already handled by the direct king death path.");
            if (FormerRulerPosthumousRules.ShouldTryPosthumousOnDeath(
                    isCurrentKing: false,
                    hasUntitledClosedReign: false))
                throw new Exception("Common dead actors without a ruler reign must not receive posthumous review.");
            if (!FormerRulerPosthumousRules.ShouldTryPosthumousOnDeath(
                    isCurrentKing: false,
                    hasUntitledClosedReign: false,
                    hasCapturedRulerSnapshot: true))
                throw new Exception("Captured former rulers should receive posthumous review from the captured ruler snapshot.");
            if (FormerRulerPosthumousRules.ShouldTryPosthumousOnDeath(
                    isCurrentKing: true,
                    hasUntitledClosedReign: false,
                    hasCapturedRulerSnapshot: true))
                throw new Exception("Current kings are still handled by the direct king death path even with a captured snapshot.");
            if (!CapturedRulerCaptureRules.ShouldPreserveFormerKingContext(
                    wasKingBeforeRelocation: true,
                    formerKingdomId: 10,
                    captorKingdomId: 20))
                throw new Exception("Capturing a king must preserve the kingdom he ruled before relocation.");
            if (CapturedRulerCaptureRules.ShouldPreserveFormerKingContext(
                    wasKingBeforeRelocation: true,
                    formerKingdomId: 10,
                    captorKingdomId: 10))
                throw new Exception("Same-kingdom slavery must not create a captured ruler context.");
            if (CapturedRulerCaptureRules.ShouldPreserveFormerKingContext(
                    wasKingBeforeRelocation: false,
                    formerKingdomId: 10,
                    captorKingdomId: 20))
                throw new Exception("Non-king captives must not be treated as captured rulers.");
        }

        private static void ExpectPosthumousTitleRules()
        {
            if (!PosthumousTitleRules.ShouldUseTaizuForOrdinaryFirstEmperor(
                    pIsMandateKingdom: false,
                    pIsEmperor: true,
                    pHasPriorEmperorTitle: false))
                throw new Exception("The first ordinary emperor should receive Taizu in the posthumous title.");

            if (PosthumousTitleRules.ShouldUseTaizuForOrdinaryFirstEmperor(
                    pIsMandateKingdom: true,
                    pIsEmperor: true,
                    pHasPriorEmperorTitle: false))
                throw new Exception("Mandate emperors are handled by the mandate temple-name system.");

            if (PosthumousTitleRules.ShouldUseTaizuForOrdinaryFirstEmperor(
                    pIsMandateKingdom: false,
                    pIsEmperor: true,
                    pHasPriorEmperorTitle: true))
                throw new Exception("Later ordinary emperors must not all receive Taizu.");

            string full = PosthumousTitleRules.BuildFullTitle(
                "\u95fd", "\u7a46", "\u5e1d",
                pUseOrdinaryFirstEmperorTaizu: true);
            if (full != "\u95fd\u592a\u7956\u7a46\u5e1d")
                throw new Exception($"Expected ordinary first emperor title with Taizu, got '{full}'.");

            string later = PosthumousTitleRules.BuildFullTitle(
                "\u95fd", "\u5ba3", "\u5e1d",
                pUseOrdinaryFirstEmperorTaizu: false);
            if (later != "\u95fd\u5ba3\u5e1d")
                throw new Exception($"Expected later ordinary emperor title without Taizu, got '{later}'.");

            string repaired = PosthumousTitleRules.RepairFirstOrdinaryEmperorDisplayTitle(
                "\u95fd\u7a46\u5e1d",
                pHasPriorOrdinaryEmperorTitle: false);
            if (repaired != "\u95fd\u592a\u7956\u7a46\u5e1d")
                throw new Exception($"Expected legacy first emperor title repair, got '{repaired}'.");

            string unchanged = PosthumousTitleRules.RepairFirstOrdinaryEmperorDisplayTitle(
                "\u95fd\u5ba3\u5e1d",
                pHasPriorOrdinaryEmperorTitle: true);
            if (unchanged != "\u95fd\u5ba3\u5e1d")
                throw new Exception($"Later emperor display title should stay unchanged, got '{unchanged}'.");
        }

        private static void ExpectFormerRulerRecordRules()
        {
            if (!FormerRulerRecordRules.ShouldRecordLostThrone(
                    previousKingId: 10,
                    newKingId: 20,
                    previousAlive: true))
                throw new Exception("Living previous rulers should get a lost-throne record when replaced.");
            if (FormerRulerRecordRules.ShouldRecordLostThrone(
                    previousKingId: 20,
                    newKingId: 20,
                    previousAlive: true))
                throw new Exception("Same king must not get a lost-throne record.");
            if (FormerRulerRecordRules.ShouldRecordLostThrone(
                    previousKingId: 10,
                    newKingId: 20,
                    previousAlive: false))
                throw new Exception("Dead previous rulers are handled by death/posthumous records.");
        }

        private static void ExpectFormerKingTraitRules()
        {
            if (!FormerKingTraitRules.ShouldMarkFormerKing(
                    pKingdomDestroyed: true,
                    pWasLastKing: true,
                    pFormerKingAlive: true))
                throw new Exception("A living last king of a destroyed kingdom should receive formerking.");
            if (FormerKingTraitRules.ShouldMarkFormerKing(
                    pKingdomDestroyed: true,
                    pWasLastKing: true,
                    pFormerKingAlive: false))
                throw new Exception("A dead last king should not receive a living formerking trait.");
            if (FormerKingTraitRules.ShouldMarkFormerKing(
                    pKingdomDestroyed: true,
                    pWasLastKing: false,
                    pFormerKingAlive: true))
                throw new Exception("Only the final king should receive formerking on kingdom fall.");

            if (!FormerKingTraitRules.ShouldUseMandateDeposedTitle(
                    pIsMandateKingdom: true,
                    pEndReason: "kingdom_fell",
                    pFormerKingAlive: true))
                throw new Exception("A living fallen Mandate ruler should receive a deposed-emperor title.");
            if (FormerKingTraitRules.ShouldUseMandateDeposedTitle(
                    pIsMandateKingdom: true,
                    pEndReason: "kingdom_fell",
                    pFormerKingAlive: false))
                throw new Exception("A dead fallen Mandate ruler should stay on normal posthumous handling.");
            if (FormerKingTraitRules.ShouldUseMandateDeposedTitle(
                    pIsMandateKingdom: false,
                    pEndReason: "kingdom_fell",
                    pFormerKingAlive: true))
                throw new Exception("Ordinary fallen kingdoms should not use deposed-emperor titles.");

            if (FormerKingTraitRules.BuildMandateDeposedTitle("\u5468") != "\u5468\u5E9F\u5E1D")
                throw new Exception("Mandate former kings should use the dynasty prefix plus Deposed Emperor.");
            if (FormerKingTraitRules.BuildMandateDeposedTitle("\u5927\u5468") != "\u5927\u5E9F\u5E1D")
                throw new Exception("Mandate former king title should use the first kingdom character.");
        }

        private static void ExpectSetKingPostfixRules()
        {
            if (!SetKingPostfixRules.ShouldRun(pFromLoad: false, pActorIsActualKing: true))
                throw new Exception("Successful runtime setKing should run AW3 postfix side effects.");
            if (SetKingPostfixRules.ShouldRun(pFromLoad: true, pActorIsActualKing: true))
                throw new Exception("Load-time setKing must not run runtime side effects.");
            if (SetKingPostfixRules.ShouldRun(pFromLoad: false, pActorIsActualKing: false))
                throw new Exception("Skipped or rejected setKing calls must not run AW3 postfix side effects.");
        }

        private static void ExpectWorldSwitchCacheRules()
        {
            if (!WorldSwitchCacheRules.ShouldClearContextBoundWindow(12))
                throw new Exception("Context-bound history windows must clear their context on world switch.");

            if (WorldSwitchCacheRules.ShouldClearContextBoundWindow(-1))
                throw new Exception("Context-bound history windows without a context should not require clearing.");

            if (!WorldSwitchCacheRules.ShouldRefreshContextFreeWindow(true))
                throw new Exception("Context-free roster windows should refresh when they are open during world switch.");

            if (WorldSwitchCacheRules.ShouldRefreshContextFreeWindow(false))
                throw new Exception("Closed context-free roster windows should not refresh eagerly during world switch.");
        }

        private static void ExpectWarPlotRedirectRules()
        {
            if (!WarPlotRedirectRules.ShouldRedirectNewWarPlot("new_war",
                    pCivilKingdom: true,
                    pCanUseAwDecision: true,
                    pAw3AllowedWarStart: false))
                throw new Exception("Vanilla new_war plots for AW3 kingdoms must redirect to AW3 war decisions.");

            if (WarPlotRedirectRules.ShouldRedirectNewWarPlot("alliance_create",
                    pCivilKingdom: true,
                    pCanUseAwDecision: true,
                    pAw3AllowedWarStart: false))
                throw new Exception("Non-war plots must not redirect to AW3 war decisions.");

            if (WarPlotRedirectRules.ShouldRedirectNewWarPlot("new_war",
                    pCivilKingdom: true,
                    pCanUseAwDecision: true,
                    pAw3AllowedWarStart: true))
                throw new Exception("AW3 scoped war starts must not redirect their own path.");

            if (!WarPlotRedirectRules.ShouldRedirectNewWarPlot("new_war",
                    pCivilKingdom: true,
                    pCanUseAwDecision: false,
                    pAw3AllowedWarStart: false))
                throw new Exception("Civil kingdoms must still have vanilla new_war plots intercepted even before AW3 decisions are enabled.");
        }

        private static void ExpectWarPlotProgressRedirectRules()
        {
            if (!WarPlotRedirectRules.ShouldInterceptActiveNewWarPlot("new_war",
                    pCivilKingdom: true,
                    pCanUseAwDecision: true,
                    pAw3AllowedWarStart: false))
                throw new Exception("Active vanilla new_war plots must be intercepted before progress.");

            if (!WarPlotRedirectRules.ShouldInterceptActiveNewWarPlot("new_war",
                    pCivilKingdom: true,
                    pCanUseAwDecision: true,
                    pAw3AllowedWarStart: true))
                throw new Exception("Active vanilla new_war plots must be consumed even during AW3 war-start scope.");

            if (WarPlotRedirectRules.ShouldInterceptActiveNewWarPlot("alliance_create",
                    pCivilKingdom: true,
                    pCanUseAwDecision: true,
                    pAw3AllowedWarStart: false))
                throw new Exception("Only active new_war plots should be intercepted.");
        }

        private static void ExpectWarTypeAssetRules()
        {
            if (WarTypeAssetRules.ResolveWarNameLocale("war_type_aw_normal_war") != "war_name_aw_normal_war")
                throw new Exception("AW3 war type locale should map to a war name locale.");

            if (WarTypeAssetRules.ResolveWarNameLocale("") != "war_name_conquest")
                throw new Exception("Empty AW3 war type locale should fall back to a vanilla-safe war name locale.");

            if (WarTypeAssetRules.ResolveWarNameTemplate("restoration_war", "war_restoration") != "war_restoration")
                throw new Exception("Restoration war must use the AW3 restoration name template.");
            if (WarTypeAssetRules.ResolveWarNameTemplate("reclaim", "war_reclaim") != "war_reclaim")
                throw new Exception("Reclaim war must use the AW3 reclaim name template.");
            if (WarTypeAssetRules.ResolveWarNameTemplate("broken_war", "war_missing") != "war_conquest")
                throw new Exception("Unknown war name templates must fall back to a vanilla-safe template.");
            if (WarTypeAssetRules.ResolveWarNameTemplate("aw_normal_war", "war_conquest") != "war_conquest")
                throw new Exception("Normal AW3 war should keep the conquest name template.");
        }

        private static void ExpectMetaWindowSafetyRules()
        {
            if (!MetaWindowSafetyRules.ShouldUseNameInput(pHasNameInput: true))
                throw new Exception("Meta windows with a name input should keep the vanilla name input flow.");
            if (MetaWindowSafetyRules.ShouldUseNameInput(pHasNameInput: false))
                throw new Exception("Meta windows without a name input must skip the vanilla name input flow.");
        }

        private static void ExpectPathfindingSafetyRules()
        {
            if (!PathfindingSafetyRules.ShouldConvertGlobalPathExceptionToNotFound(
                    new NullReferenceException(),
                    pHasStartTile: true,
                    pHasTargetTile: true))
                throw new Exception("RegionPathFinder null refs from disconnected region paths should become NotFound.");
            if (PathfindingSafetyRules.ShouldConvertGlobalPathExceptionToNotFound(
                    new InvalidOperationException(),
                    pHasStartTile: true,
                    pHasTargetTile: true))
                throw new Exception("Pathfinding safety must not swallow unrelated exceptions.");
            if (PathfindingSafetyRules.ShouldConvertGlobalPathExceptionToNotFound(
                    new NullReferenceException(),
                    pHasStartTile: false,
                    pHasTargetTile: true))
                throw new Exception("Pathfinding safety should not hide null input bugs.");
        }

        private static void ExpectMandateMapMarkerRules()
        {
            if (!MandateMapMarkerRules.ShouldReplaceSpeciesIcon("moh_nameplate", pHasSpeciesImage: true))
                throw new Exception("Mandate markers must replace the stable primary kingdom nameplate icon.");
            if (MandateMapMarkerRules.ShouldUseSpecialIcon("moh_nameplate", pHasSpecialImage: true))
                throw new Exception("Mandate markers must not use the drifting special-icon slot.");
            if (MandateMapMarkerRules.ShouldUseSpecialIcon("", pHasSpecialImage: true))
                throw new Exception("Empty mandate marker paths must leave the original nameplate icon untouched.");
            if (MandateMapMarkerRules.ShouldReplaceSpeciesIcon("moh_nameplate", pHasSpeciesImage: false))
                throw new Exception("Mandate marker replacement needs an existing primary image target.");
            if (MandateMapMarkerRules.ShouldReplaceSpeciesIcon("", pHasSpeciesImage: true))
                throw new Exception("Empty mandate marker paths must not replace the primary icon.");
            if (!MandateMapMarkerRules.ShouldClearSpecialIcon("", pHasSpecialImage: true))
                throw new Exception("Pooled kingdom nameplates must clear stale special markers when this kingdom has no marker.");
            if (!MandateMapMarkerRules.ShouldClearSpecialIcon("moh_nameplate", pHasSpecialImage: true))
                throw new Exception("Active mandate markers must also clear stale special icon state from older builds.");
        }

        private static void ExpectMandateDeclarationOriginRules()
        {
            if (MandateDeclarationRules.CanDeclareForeignPseudo(
                    pIsXiaKingdom: false,
                    pWonMandateWar: false,
                    pHasEnoughLegalCoreControl: true,
                    pMandateAlreadyExists: false,
                    out string reason) || reason != "requires_mandate_war")
                throw new Exception("Foreign pseudo-dynasties must not claim Mandate through a normal decision.");

            if (!MandateDeclarationRules.CanDeclareForeignPseudo(
                    pIsXiaKingdom: false,
                    pWonMandateWar: true,
                    pHasEnoughLegalCoreControl: true,
                    pMandateAlreadyExists: false,
                    out reason) || reason != "")
                throw new Exception("Foreign pseudo-dynasties should claim Mandate after winning a Mandate war.");

            if (MandateDeclarationRules.CanDeclareForeignPseudo(
                    pIsXiaKingdom: false,
                    pWonMandateWar: true,
                    pHasEnoughLegalCoreControl: false,
                    pMandateAlreadyExists: false,
                    out reason) || reason != "core_control")
                throw new Exception("Foreign pseudo-dynasties still need enough legal-core control after winning.");
        }

        private static void ExpectXiaizationEligibilityRules()
        {
            if (!XiaizationEligibilityRules.CanUseMandateSystem(pIsXiaKingdom: true, pXiaizationLevel: 0))
                throw new Exception("Xia kingdoms must keep normal Mandate access.");
            if (!XiaizationEligibilityRules.CanUsePolicySystem(pIsXiaKingdom: true, pXiaizationLevel: 0))
                throw new Exception("Xia kingdoms must keep normal policy access.");
            if (XiaizationEligibilityRules.CanUseMandateSystem(pIsXiaKingdom: false, pXiaizationLevel: 0))
                throw new Exception("Foreign kingdoms must not claim Mandate through ordinary peaceful/decision paths.");
            if (XiaizationEligibilityRules.CanUsePolicySystem(pIsXiaKingdom: false, pXiaizationLevel: 0))
                throw new Exception("Foreign kingdoms must not use AW3 policy by default before pseudo-dynasty Xiaization.");
            if (XiaizationEligibilityRules.CanUseMandateSystem(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.PseudoDynastyLevel))
                throw new Exception("Soft-contact Level 2 foreign kingdoms must not use the Mandate system.");
            if (!XiaizationEligibilityRules.CanUseMandateSystem(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.PseudoDynastyLevel,
                    pIsForeignPseudoDynasty: true))
                throw new Exception("Foreign pseudo-dynasties should use Mandate maintenance after seizing Mandate.");
            if (!XiaizationEligibilityRules.CanUseMandateSystem(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.XiaInstitutionsLevel,
                    pIsForeignPseudoDynasty: false))
                throw new Exception("Level 4 foreign Xiaized kingdoms should use the Mandate system.");
            if (!XiaizationEligibilityRules.CanUsePolicySystem(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.PseudoDynastyLevel))
                throw new Exception("Foreign pseudo-dynasties should use AW3 policy/decision systems after Xiaization.");
            if (XiaizationEligibilityRules.CanUseInstitutionSystem(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.PseudoDynastyLevel,
                    pIsForeignPseudoDynasty: false))
                throw new Exception("Soft-contact Level 2 foreign kingdoms must not enter the full lineage institution system.");
            if (!XiaizationEligibilityRules.CanUseInstitutionSystem(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.PseudoDynastyLevel,
                    pIsForeignPseudoDynasty: true))
                throw new Exception("Foreign pseudo-dynasties should enter the lineage institution system.");
            if (!XiaizationEligibilityRules.CanUseInstitutionSystem(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.XiaInstitutionsLevel,
                    pIsForeignPseudoDynasty: false))
                throw new Exception("Level 4 foreign Xiaized kingdoms should enter the lineage institution system.");
        }

        private static void ExpectXiaContactRules()
        {
            float gain = XiaContactRules.CalculateYearlyGain(
                pBordersXia: true,
                pDiplomaticContact: true,
                pVassalContact: false,
                pOccupiedXiaCityCount: 1,
                pMixedChildEvents: 2,
                pOfficialContact: true);
            if (Math.Abs(gain - 27f) > 0.001f)
                throw new Exception($"Xia contact yearly gain should combine border/diplomacy/occupation/mixed child/official contact, got {gain}.");
            float nearbyGain = XiaContactRules.CalculateYearlyGain(
                pBordersXia: false,
                pDiplomaticContact: false,
                pVassalContact: false,
                pOccupiedXiaCityCount: 0,
                pMixedChildEvents: 0,
                pOfficialContact: false,
                pNearbyXiaContact: true);
            if (Math.Abs(nearbyGain - XiaContactRules.NearbyGain) > 0.001f)
                throw new Exception($"Nearby Xia kingdoms should create soft yearly Xia contact, got {nearbyGain}.");

            string sourceMask = XiaContactRules.BuildSourceMask(
                pBordersXia: true,
                pDiplomaticContact: true,
                pVassalContact: false,
                pOccupiedXiaCityCount: 2,
                pMixedChildEvents: 1,
                pOfficialContact: true);
            if (sourceMask != "border;diplomacy;occupation;mixed;official")
                throw new Exception("Xia contact source mask should be stable and compact, got " + sourceMask + ".");

            if (XiaContactRules.PrimaryReason(sourceMask) != "xia_occupation_contact")
                throw new Exception("Occupation should be the primary Xia contact reason when present.");
            string nearbySourceMask = XiaContactRules.BuildSourceMask(
                pBordersXia: false,
                pDiplomaticContact: false,
                pVassalContact: false,
                pOccupiedXiaCityCount: 0,
                pMixedChildEvents: 0,
                pOfficialContact: false,
                pNearbyXiaContact: true);
            if (nearbySourceMask != "nearby")
                throw new Exception($"Unexpected nearby Xia contact source mask '{nearbySourceMask}'.");
            if (XiaContactRules.PrimaryReason(nearbySourceMask) != "xia_nearby_contact")
                throw new Exception("Nearby Xia kingdoms should be recorded as nearby Xia contact.");

            if (XiaContactRules.LevelForProgress(0f) != 0)
                throw new Exception("No Xia contact progress should remain level 0.");
            if (XiaContactRules.LevelForProgress(1f) != XiaContactRules.LevelKnownXia)
                throw new Exception("Any Xia contact progress should mark the kingdom as knowing Xia.");
            if (XiaContactRules.LevelForProgress(XiaContactRules.PolicyUnlockProgress - 0.01f) != XiaContactRules.LevelKnownXia)
                throw new Exception("Soft Xia contact should not unlock policy before the progress threshold.");
            if (XiaContactRules.LevelForProgress(XiaContactRules.PolicyUnlockProgress) != XiaContactRules.LevelAdoptCustoms)
                throw new Exception("Soft Xia contact should unlock the Xiaization route at the policy threshold.");

            if (!XiaizationEligibilityRules.CanUsePolicyNode(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.PseudoDynastyLevel,
                    pNodeId: "aw_policy_adopt_xia_rites",
                    pIsXiaizationPolicy: true))
                throw new Exception("Level 2 foreign Xiaized kingdoms should be able to research the Xiaization route.");
            if (XiaizationEligibilityRules.CanUsePolicyNode(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.PseudoDynastyLevel,
                    pNodeId: "aw_tech_writing",
                    pIsXiaizationPolicy: false))
                throw new Exception("Level 2 foreign Xiaized kingdoms must not open the full tech tree before adopting Xia institutions.");
            if (!XiaizationEligibilityRules.CanUsePolicyNode(
                    pIsXiaKingdom: false,
                    pXiaizationLevel: XiaizationEligibilityRules.XiaInstitutionsLevel,
                    pNodeId: "aw_tech_writing",
                    pIsXiaizationPolicy: false))
                throw new Exception("Level 4 foreign Xiaized kingdoms should open the full AW3 policy tree.");
        }

        private static void ExpectForeignPseudoLineageRules()
        {
            if (ForeignPseudoLineageRules.ExtractClanName("所罗巴伯 蒲察", "蒲察") != "蒲察")
                throw new Exception("Foreign pseudo lineage must use the text after the name delimiter as clan name.");
            if (ForeignPseudoLineageRules.ExtractClanName("阿骨打·完颜", "") != "完颜")
                throw new Exception("Middle-dot foreign names should use the right side as clan name.");
            if (ForeignPseudoLineageRules.ExtractGivenName("所罗巴伯 蒲察") != "所罗巴伯")
                throw new Exception("Foreign pseudo lineage should keep the left side as given name.");
            if (!ForeignPseudoLineageRules.ShouldUseAwLineageSystem(
                    pIsXiaActor: false,
                    pKingdomIsForeignPseudoDynasty: true,
                    pKingdomIsXia: false,
                    pHasLineage: true))
                throw new Exception("Foreign pseudo-dynasty officials with lineage must pass AW3 chronicle gates.");
            if (ForeignPseudoLineageRules.ShouldUseAwLineageSystem(
                    pIsXiaActor: false,
                    pKingdomIsForeignPseudoDynasty: false,
                    pKingdomIsXia: false,
                    pHasLineage: true))
                throw new Exception("Ordinary foreign nobles must not be treated as AW3 Xia lineage actors.");
            if (!ForeignPseudoLineageRules.ShouldUseAwLineageSystem(
                    pIsXiaActor: false,
                    pKingdomIsForeignPseudoDynasty: false,
                    pKingdomIsXia: true,
                    pHasLineage: true))
                throw new Exception("Xiaized human descendants inside Xia kingdoms should use the AW3 lineage system.");
            if (!ForeignPseudoLineageRules.ShouldIntegrateOfficial(
                    pIsKing: false,
                    pIsCityLeader: true,
                    pIsArmyLeader: false))
                throw new Exception("Foreign pseudo-dynasty city leaders should be integrated into the AW3 lineage system.");
        }

        private static void ExpectMandatePowerRules()
        {
            float realmPower = MandatePowerRules.CalculateRealmPower(
                pPopulation: 250,
                pCityCount: 3,
                pArmyPower: 40f,
                pKingStewardship: 7f);
            if (Math.Abs(realmPower - 660f) > 0.001f)
                throw new Exception($"Mandate realm power must use population + cities*100 + army + stewardship*10, got {realmPower}.");

            float territorialRealmPower = MandatePowerRules.CalculateRealmPower(
                pPopulation: 250,
                pCityCount: 3,
                pArmyPower: 40f,
                pKingStewardship: 7f,
                pTerritoryZones: 400);
            if (Math.Abs(territorialRealmPower - 1260f) > 0.001f)
                throw new Exception($"Mandate realm power must give stronger weight to territory size, got {territorialRealmPower}.");

            float power = MandatePowerRules.CalculateCompetitionPower(pOwnPower: 100f, pVassalPower: 50f);
            if (Math.Abs(power - 130f) > 0.001f)
                throw new Exception($"Mandate competition power must use own + vassals*0.6, got {power}.");

            if (!MandatePowerRules.HasRequiredLeadForMandate(
                    pCandidatePower: 138f,
                    pStrongestOtherPower: 100f,
                    pWeakestOtherPower: 20f))
                throw new Exception("Mandate claimant should pass only when above second-plus-weakest power by 15 percent.");

            if (MandatePowerRules.HasRequiredLeadForMandate(
                    pCandidatePower: 137.99f,
                    pStrongestOtherPower: 100f,
                    pWeakestOtherPower: 20f))
                throw new Exception("Mandate claimant must be blocked below the second-plus-weakest 15 percent threshold.");

            if (MandatePowerRules.HasRequiredLeadForMandate(
                    pCandidatePower: 116f,
                    pStrongestOtherPower: 100f,
                    pWeakestOtherPower: 20f))
                throw new Exception("Mandate claimant must not pass with only the old 15 percent lead over the second strongest realm.");

            if (MandatePowerRules.HasRequiredLeadForMandate(
                    pCandidatePower: 90f,
                    pStrongestOtherPower: 100f,
                    pWeakestOtherPower: 20f))
                throw new Exception("Mandate claimant must be blocked when it is not the strongest realm.");

            if (MandatePowerRules.IsEligibleCompetitor(
                    pIsValidCivilKingdom: true,
                    pIsVassal: true,
                    pSupportsMandateSystem: true))
                throw new Exception("Vassal kingdoms must be excluded from strongest-Mandate competition.");

            if (!MandatePowerRules.IsEligibleCompetitor(
                    pIsValidCivilKingdom: true,
                    pIsVassal: false,
                    pSupportsMandateSystem: true))
                throw new Exception("Independent supported kingdoms should compete for strongest-Mandate status.");

            int winner = MandatePowerRules.SelectWinningCandidateIndex(
                new[] { 80f, 138f, 100f, 20f },
                new[] { true, true, true, true });
            if (winner != 1)
                throw new Exception($"Expected strongest valid Mandate claimant index 1, got {winner}.");

            int blocked = MandatePowerRules.SelectWinningCandidateIndex(
                new[] { 80f, 137.99f, 100f, 20f },
                new[] { true, true, true, true });
            if (blocked != -1)
                throw new Exception("Mandate candidate table must apply the same lead threshold as direct checks.");

            int ignored = MandatePowerRules.SelectWinningCandidateIndex(
                new[] { 400f, 138f, 100f, 20f },
                new[] { false, true, true, true });
            if (ignored != 1)
                throw new Exception($"Ineligible high-power kingdoms must not win Mandate selection, got {ignored}.");

            if (MandatePowerRules.CalculateStrongestPowerPenalty(
                    pMandatePower: 100f,
                    pStrongestPower: 100f) != 0)
                throw new Exception("Equal power should not penalize Mandate.");

            if (MandatePowerRules.CalculateStrongestPowerPenalty(
                    pMandatePower: 100f,
                    pStrongestPower: 130f) != -4)
                throw new Exception("A 1.25x stronger rival should apply the AW2-style stronger-state penalty.");

            if (MandatePowerRules.CalculateStrongestPowerPenalty(
                    pMandatePower: 100f,
                    pStrongestPower: 180f) != -6)
                throw new Exception("A 1.75x stronger rival should apply the severe stronger-state penalty.");
        }

        private static void ExpectMandateStartRecordRules()
        {
            if (MandateStartRecordRules.EventType("native", "orthodox") != "mandate_declared_orthodox")
                throw new Exception("Native orthodox mandate starts need a distinct history event type.");
            if (MandateStartRecordRules.EventType("rebel", "rebel") != "mandate_declared_rebel")
                throw new Exception("Rebel mandate starts need a distinct history event type.");
            if (MandateStartRecordRules.EventType("pseudo_foreign", "foreign_pseudo") !=
                "mandate_declared_foreign_pseudo")
                throw new Exception("Foreign pseudo mandate starts need a distinct history event type.");

            if (!MandateStartRecordRules.IsForeignPseudo("pseudo_foreign", "orthodox") ||
                !MandateStartRecordRules.IsForeignPseudo("native", "foreign_pseudo"))
                throw new Exception("Foreign pseudo origin and claimant markers should both be recognized.");
        }

        private static void ExpectMandateRebelStateRules()
        {
            if (!MandateRebelStateRules.IsCurrentRebelGovernment(
                    pRebelFlag: true,
                    pClassState: "default",
                    pOriginType: "",
                    pClaimantKind: ""))
                throw new Exception("A live rebel flag should mark the kingdom as a current peasant rebel government.");

            if (!MandateRebelStateRules.IsCurrentRebelGovernment(
                    pRebelFlag: false,
                    pClassState: "peasant_rebel",
                    pOriginType: "",
                    pClaimantKind: ""))
                throw new Exception("A live peasant rebel class state should mark the kingdom as a current peasant rebel government.");

            if (MandateRebelStateRules.IsCurrentRebelGovernment(
                    pRebelFlag: false,
                    pClassState: "default",
                    pOriginType: "rebel",
                    pClaimantKind: "rebel"))
                throw new Exception("Historical rebel origin must not keep a settled kingdom in peasant rebel government.");

            if (MandateRebelStateRules.SettledClassAfterRebellion("peasant_rebel") != "default")
                throw new Exception("A peasant rebel kingdom should return to ordinary political class after rebellion war settlement.");
            if (!MandateRebelStateRules.ShouldUseActiveClaimantCache(
                    pCachedYear: 12, pCurrentYear: 12, pCachedKingdomCount: 8, pCurrentKingdomCount: 8))
                throw new Exception("Active rebel claimant scan should reuse same-year same-count cache.");
            if (MandateRebelStateRules.ShouldUseActiveClaimantCache(
                    pCachedYear: 11, pCurrentYear: 12, pCachedKingdomCount: 8, pCurrentKingdomCount: 8))
                throw new Exception("Active rebel claimant scan cache must expire across years.");
            if (MandateRebelStateRules.ShouldUseActiveClaimantCache(
                    pCachedYear: 12, pCurrentYear: 12, pCachedKingdomCount: 7, pCurrentKingdomCount: 8))
                throw new Exception("Active rebel claimant scan cache must expire when kingdom count changes.");
        }

        private static void ExpectRepublicGovernmentRules()
        {
            if (!RepublicGovernmentRules.ShouldBecomeRepublic(
                    pIsCiv: true,
                    pIsRekt: false,
                    pHasKing: false,
                    pHasMonarchyCandidate: false,
                    pIsRebelGovernment: false))
                throw new Exception("A surviving civil kingdom with no king and no succession candidate should become a republic.");

            if (RepublicGovernmentRules.ShouldBecomeRepublic(
                    pIsCiv: true,
                    pIsRekt: false,
                    pHasKing: true,
                    pHasMonarchyCandidate: false,
                    pIsRebelGovernment: false))
                throw new Exception("A kingdom that still has a king must not become a republic.");

            if (RepublicGovernmentRules.ShouldBecomeRepublic(
                    pIsCiv: true,
                    pIsRekt: false,
                    pHasKing: false,
                    pHasMonarchyCandidate: true,
                    pIsRebelGovernment: false))
                throw new Exception("A kingdom with a valid monarchy candidate must not become a republic.");

            if (RepublicGovernmentRules.ShouldBecomeRepublic(
                    pIsCiv: true,
                    pIsRekt: false,
                    pHasKing: false,
                    pHasMonarchyCandidate: false,
                    pIsRebelGovernment: true))
                throw new Exception("Peasant rebel governments must keep the rebel state instead of becoming republics.");

            if (RepublicGovernmentRules.SuffixForNameplate(pIsRepublic: true) != "\u5171\u548c\u56fd")
                throw new Exception("Republic kingdoms should use the republic nameplate suffix.");
        }

        private static void ExpectMandateWarAiRules()
        {
            if (MandateWarAiRules.ShouldConsiderTakeMandate(
                    pTargetIsCurrentMandate: false,
                    pVassalBlocked: false,
                    pAttackerPower: 200f,
                    pDefenderPower: 100f,
                    pMandateValue: 20))
                throw new Exception("AI should only consider take-Mandate against the current Mandate realm.");

            if (MandateWarAiRules.ShouldConsiderTakeMandate(
                    pTargetIsCurrentMandate: true,
                    pVassalBlocked: true,
                    pAttackerPower: 200f,
                    pDefenderPower: 100f,
                    pMandateValue: 20))
                throw new Exception("Vassal-blocked targets should not be considered for take-Mandate.");

            if (MandateWarAiRules.ShouldConsiderTakeMandate(
                    pTargetIsCurrentMandate: true,
                    pVassalBlocked: false,
                    pAttackerPower: 90f,
                    pDefenderPower: 100f,
                    pMandateValue: 20))
                throw new Exception("AI should not challenge Mandate while clearly weaker.");

            if (!MandateWarAiRules.ShouldConsiderTakeMandate(
                    pTargetIsCurrentMandate: true,
                    pVassalBlocked: false,
                    pAttackerPower: 130f,
                    pDefenderPower: 100f,
                    pMandateValue: 25))
                throw new Exception("AI should consider take-Mandate when stronger and Mandate is weak.");

            int weakScore = MandateWarAiRules.ScoreTakeMandate(pAttackerPower: 130f, pDefenderPower: 100f,
                pMandateValue: 20);
            int stableScore = MandateWarAiRules.ScoreTakeMandate(pAttackerPower: 130f, pDefenderPower: 100f,
                pMandateValue: 80);
            if (weakScore <= stableScore)
                throw new Exception("AI take-Mandate score should rise when the Mandate value is weak.");
        }

        private static void ExpectMandateConquestRules()
        {
            if (!MandateConquestRules.CanUseMandateConquest(
                    pAttackerIsCurrentMandate: true,
                    pVassalBlocked: false,
                    pSameAlliance: false,
                    pAttackerSystemPower: 180f,
                    pDefenderAlliancePower: 120f))
                throw new Exception("A stronger Mandate realm should be able to use a penalty-free conquest CB.");
            if (MandateConquestRules.CanUseMandateConquest(
                    pAttackerIsCurrentMandate: false,
                    pVassalBlocked: false,
                    pSameAlliance: false,
                    pAttackerSystemPower: 300f,
                    pDefenderAlliancePower: 100f))
                throw new Exception("Non-Mandate realms must not receive the Mandate conquest CB.");
            if (MandateConquestRules.CanUseMandateConquest(
                    pAttackerIsCurrentMandate: true,
                    pVassalBlocked: false,
                    pSameAlliance: true,
                    pAttackerSystemPower: 300f,
                    pDefenderAlliancePower: 100f))
                throw new Exception("Mandate conquest should still respect alliance blocks.");
            if (MandateConquestRules.CanUseMandateConquest(
                    pAttackerIsCurrentMandate: true,
                    pVassalBlocked: false,
                    pSameAlliance: false,
                    pAttackerSystemPower: 120f,
                    pDefenderAlliancePower: 110f))
                throw new Exception("Mandate conquest should consider the defender's alliance power.");
            if (MandateConquestRules.ScoreMandateConquest(220f, 100f, pNeighbor: true) <=
                MandateConquestRules.ScoreMandateConquest(150f, 100f, pNeighbor: false))
                throw new Exception("Mandate conquest AI should strongly prefer weaker neighboring targets.");
        }

        private static void ExpectMandateBorderWallRules()
        {
            if (MandateBorderWallRules.PreferredWallTopTileId != "wall_order")
                throw new Exception("Mandate border walls should use the vanilla Stone Wall top tile, not iron wall.");
            if (MandateBorderWallRules.ShouldBuildWallAtOrderedIndex(8))
                throw new Exception("Border wall builder should leave periodic gaps in long wall lines.");
            if (!MandateBorderWallRules.ShouldBuildWallAtOrderedIndex(7) ||
                !MandateBorderWallRules.ShouldBuildWallAtOrderedIndex(9))
                throw new Exception("Border wall gaps should be narrow, not break the entire border line.");
            if (MandateBorderWallRules.CompareWallTileOrder(3, 4, 2, 5) >= 0)
                throw new Exception("Border wall candidates should be sorted into a stable continuous line order.");
            if (!MandateBorderWallRules.IsExternalLandBorderNeighbor(
                    pNeighborHasCity: true,
                    pNeighborGround: true,
                    pNeighborLiquid: false,
                    pNeighborLava: false,
                    pNeighborBlock: false,
                    pNeighborNeutral: false,
                    pSameMandateSystem: false))
                throw new Exception("Border walls should accept land tiles belonging to an outside realm.");
            if (MandateBorderWallRules.IsExternalLandBorderNeighbor(
                    pNeighborHasCity: false,
                    pNeighborGround: true,
                    pNeighborLiquid: false,
                    pNeighborLava: false,
                    pNeighborBlock: false,
                    pNeighborNeutral: false,
                    pSameMandateSystem: false))
                throw new Exception("Border walls must not treat empty wilderness as a realm border.");
            if (MandateBorderWallRules.IsExternalLandBorderNeighbor(
                    pNeighborHasCity: true,
                    pNeighborGround: false,
                    pNeighborLiquid: true,
                    pNeighborLava: false,
                    pNeighborBlock: false,
                    pNeighborNeutral: false,
                    pSameMandateSystem: false))
                throw new Exception("Border walls must not be built along sea or river edges.");
            if (MandateBorderWallRules.IsExternalLandBorderNeighbor(
                    pNeighborHasCity: true,
                    pNeighborGround: true,
                    pNeighborLiquid: false,
                    pNeighborLava: false,
                    pNeighborBlock: false,
                    pNeighborNeutral: false,
                    pSameMandateSystem: true))
                throw new Exception("Mandate vassal and own land borders should not receive border walls.");
            if (!MandateBorderWallRules.IsWallBuildTileTerrainValid(
                    pInsideCity: true,
                    pGround: true,
                    pLiquid: false,
                    pLava: false,
                    pBlock: false,
                    pWall: false,
                    pRoad: false,
                    pHasTopTile: false,
                    pHasBuilding: false))
                throw new Exception("Border wall build tile should accept empty city land.");
            if (MandateBorderWallRules.IsWallBuildTileTerrainValid(
                    pInsideCity: true,
                    pGround: true,
                    pLiquid: false,
                    pLava: false,
                    pBlock: false,
                    pWall: false,
                    pRoad: true,
                    pHasTopTile: false,
                    pHasBuilding: false))
                throw new Exception("Border walls should not overwrite city roads.");
        }

        private static void ExpectCapitalMoveRules()
        {
            if (CapitalMoveRules.CanConsiderCandidate(
                    pCandidateAlive: true,
                    pIsCurrentCapital: false,
                    pIsCoreCity: false,
                    pHasOwnNeighbor: true))
                throw new Exception("Capital moves must only target core cities.");
            if (CapitalMoveRules.CanConsiderCandidate(
                    pCandidateAlive: true,
                    pIsCurrentCapital: false,
                    pIsCoreCity: true,
                    pHasOwnNeighbor: false))
                throw new Exception("Capital moves should prefer connected cities like AW2.");
            if (!CapitalMoveRules.CanConsiderCandidate(
                    pCandidateAlive: true,
                    pIsCurrentCapital: false,
                    pIsCoreCity: true,
                    pHasOwnNeighbor: true))
                throw new Exception("Alive connected core cities should be valid capital candidates.");

            float current = CapitalMoveRules.ScoreCity(
                pCityAge: 100,
                pCurrentCapitalAge: 100,
                pPopulation: 80,
                pCurrentPopulation: 80,
                pZones: 50,
                pCurrentZones: 50,
                pOwnNeighborCount: 1,
                pCentralityScore: 0f);
            float better = CapitalMoveRules.ScoreCity(
                pCityAge: 120,
                pCurrentCapitalAge: 100,
                pPopulation: 160,
                pCurrentPopulation: 80,
                pZones: 90,
                pCurrentZones: 50,
                pOwnNeighborCount: 3,
                pCentralityScore: 20f);
            if (!CapitalMoveRules.ShouldMoveCapital(current, better))
                throw new Exception("A clearly better connected core city should pass the AW2-style capital move threshold.");
            if (CapitalMoveRules.ShouldMoveCapital(current, current + 5f))
                throw new Exception("Tiny improvements should not trigger capital movement.");
        }

        private static void ExpectLineageArchiveIndexRules()
        {
            var specs = LineageArchiveIndexRules.GetRequiredIndexes();
            if (!LineageArchiveIndexRules.ContainsIndex(specs, "idx_FamilyEdge_child_slot"))
                throw new Exception("Family tree parent lookups need an index on FamilyEdge(CHILD_ID, PARENT_SLOT).");
            if (!LineageArchiveIndexRules.ContainsIndex(specs, "idx_FamilyEdge_parent_time"))
                throw new Exception("Family tree child lookups need an index on FamilyEdge(PARENT_ID, CREATED_TIME, CHILD_ID).");
            if (!LineageArchiveIndexRules.ContainsIndex(specs, "idx_ActorArchive_shi_alive_birth"))
                throw new Exception("Shi tree reads need an index on ActorArchive(SHI_ID, IS_ALIVE, BIRTH_TIME).");
            if (!LineageArchiveIndexRules.ContainsIndex(specs, "idx_WarClaim_source_target_active"))
                throw new Exception("War claim map/AI reads need an index on active source-target claims.");
            if (!LineageArchiveIndexRules.ContainsIndex(specs, "idx_MandateCoreCity_kingdom_city_active"))
                throw new Exception("Mandate legal core map reads need an index on active kingdom-city cores.");
        }

        private static void ExpectMapModeMetaCacheRules()
        {
            if (!MapModeMetaCacheRules.IsDynamicMetaKey("Tech:city:10:tech_2"))
                throw new Exception("City-colored tech meta keys must be treated as dynamic cache entries.");
            if (!MapModeMetaCacheRules.IsDynamicMetaKey("aw3_tech_map:city:10:tech_2"))
                throw new Exception("Runtime AW3 tech meta ids must be treated as dynamic cache entries.");
            if (!MapModeMetaCacheRules.IsDynamicMetaKey("212:owned_non_core"))
                throw new Exception("Numeric custom MetaType keys must be treated as dynamic cache entries.");
            if (!MapModeMetaCacheRules.IsDynamicMetaKey("WarCore:owned_non_core"))
                throw new Exception("Runtime war core meta keys must be treated as dynamic cache entries.");
            if (MapModeMetaCacheRules.IsDynamicMetaKey("Kingdom:42"))
                throw new Exception("Vanilla kingdom meta keys must not be cleared by AW3 dynamic cache pruning.");
            if (!MapModeMetaCacheRules.ShouldClearForWorldSwitch(pHadAnyDynamicMeta: true))
                throw new Exception("World switches must clear AW3 dynamic map meta cache.");
        }

        private static void ExpectMetaColorCacheRules()
        {
            if (!MetaColorCacheRules.ShouldRefreshAfterGeneratedColor(
                    pHasMetaObject: true,
                    pColorId: 12,
                    pColorCount: 32))
                throw new Exception("A valid generated meta color must clear stale cached kingdom colors.");

            if (MetaColorCacheRules.ShouldRefreshAfterGeneratedColor(
                    pHasMetaObject: false,
                    pColorId: 12,
                    pColorCount: 32))
                throw new Exception("Missing meta objects must not run color cache refresh.");

            if (MetaColorCacheRules.ShouldRefreshAfterGeneratedColor(
                    pHasMetaObject: true,
                    pColorId: -1,
                    pColorCount: 32))
                throw new Exception("Invalid color ids must not run color cache refresh.");

            if (MetaColorCacheRules.ShouldRefreshAfterGeneratedColor(
                    pHasMetaObject: true,
                    pColorId: 32,
                    pColorCount: 32))
                throw new Exception("Out-of-range color ids must not run color cache refresh.");
        }

        private static void ExpectKingdomVisualRandomizationRules()
        {
            if (!KingdomVisualRandomizationRules.ShouldRerollNewCivVisuals(
                    pHasKingdom: true,
                    pIsCivilized: true,
                    pIsNeutral: false,
                    pColorCount: 8,
                    pBackgroundCount: 3,
                    pIconCount: 4))
                throw new Exception("New civilized kingdoms should reroll visuals with the AW3 private RNG.");

            if (KingdomVisualRandomizationRules.ShouldRerollNewCivVisuals(
                    pHasKingdom: true,
                    pIsCivilized: false,
                    pIsNeutral: false,
                    pColorCount: 8,
                    pBackgroundCount: 3,
                    pIconCount: 4))
                throw new Exception("Non-civilized kingdoms must not use civ visual rerolling.");

            if (KingdomVisualRandomizationRules.ShouldRerollNewCivVisuals(
                    pHasKingdom: true,
                    pIsCivilized: true,
                    pIsNeutral: false,
                    pColorCount: 0,
                    pBackgroundCount: 3,
                    pIconCount: 4))
                throw new Exception("Visual rerolling must require a valid color library.");

            if (KingdomVisualRandomizationRules.NormalizeVisualIndex(
                    pCandidateIndex: 2,
                    pCurrentIndex: 2,
                    pCount: 5) == 2)
                throw new Exception("Visual rerolling should avoid keeping the original Randy-picked index when alternatives exist.");

            if (KingdomVisualRandomizationRules.NormalizeVisualIndex(
                    pCandidateIndex: 4,
                    pCurrentIndex: 2,
                    pCount: 5) != 4)
                throw new Exception("Visual rerolling should keep a different valid candidate.");

            if (KingdomVisualRandomizationRules.NormalizeVisualIndex(
                    pCandidateIndex: 9,
                    pCurrentIndex: 0,
                    pCount: 5) != 4)
                throw new Exception("Visual rerolling should normalize out-of-range candidates.");

            if (KingdomVisualRandomizationRules.NormalizeVisualIndex(
                    pCandidateIndex: 0,
                    pCurrentIndex: 0,
                    pCount: 1) != 0)
                throw new Exception("Single-option banner pools must keep their only valid index.");
        }

        private static void ExpectKingdomYearSchedulerRules()
        {
            if (!KingdomYearSchedulerRules.ShouldRunHeavySystem(pYear: 120, pKingdomId: 6, pModulo: 4, pSlot: 2))
                throw new Exception("Staggered yearly scheduler should run when kingdom/year slot matches.");
            if (KingdomYearSchedulerRules.ShouldRunHeavySystem(pYear: 120, pKingdomId: 7, pModulo: 4, pSlot: 2))
                throw new Exception("Staggered yearly scheduler should skip non-matching kingdoms.");
            if (!KingdomYearSchedulerRules.ShouldRunHeavySystem(pYear: 120, pKingdomId: 7, pModulo: 0, pSlot: 2))
                throw new Exception("Invalid modulo should fall back to running instead of disabling maintenance.");
        }

        private static void ExpectMapModeDirtyThrottleRules()
        {
            if (!MapModeDirtyThrottleRules.ShouldDirty(pActive: true, pNow: 10.0, pLastDirty: -1.0, pMinInterval: 0.25))
                throw new Exception("Active map modes must dirty on the first invalidation.");
            if (MapModeDirtyThrottleRules.ShouldDirty(pActive: true, pNow: 10.1, pLastDirty: 10.0, pMinInterval: 0.25))
                throw new Exception("Map modes should coalesce repeated invalidations in the same short time slice.");
            if (!MapModeDirtyThrottleRules.ShouldDirty(pActive: true, pNow: 10.3, pLastDirty: 10.0, pMinInterval: 0.25))
                throw new Exception("Map modes should allow a later invalidation after the throttle interval.");
            if (MapModeDirtyThrottleRules.ShouldDirty(pActive: false, pNow: 10.3, pLastDirty: 10.0, pMinInterval: 0.25))
                throw new Exception("Inactive map modes must not dirty the zone calculator.");
        }

        private static void ExpectActorAiSearchThrottleRules()
        {
            if (!ActorAiSearchThrottleRules.ShouldSearch(pNow: 10.0, pNextAllowed: -1.0))
                throw new Exception("Actor AI target search should run when it has no cooldown.");
            if (ActorAiSearchThrottleRules.ShouldSearch(pNow: 10.0, pNextAllowed: 12.0))
                throw new Exception("Actor AI target search should skip while a miss cooldown is active.");
            if (!ActorAiSearchThrottleRules.ShouldSearch(pNow: 12.0, pNextAllowed: 12.0))
                throw new Exception("Actor AI target search should resume at the cooldown boundary.");
            if (ActorAiSearchThrottleRules.NextAllowedAfterMiss(pNow: 10.0, pCooldown: 2.0) != 12.0)
                throw new Exception("Actor AI target search miss cooldown should be based on current world time.");
            if (ActorAiSearchThrottleRules.NextAllowedAfterMiss(pNow: 10.0, pCooldown: -1.0) != 10.0)
                throw new Exception("Invalid actor AI target search cooldown should not push searches into the future.");
        }

        private static void ExpectFiefCacheRules()
        {
            if (!FiefCacheRules.IsUnknown(FiefCacheRules.UnknownGeneralId))
                throw new Exception("Fief cache must distinguish unknown state from no active fief.");
            if (!FiefCacheRules.HasActiveFief(42))
                throw new Exception("Positive cached general id should mean the city has an active fief.");
            if (FiefCacheRules.HasActiveFief(FiefCacheRules.NoActiveFiefGeneralId))
                throw new Exception("No-active-fief marker should not be treated as an active fief.");
            if (FiefCacheRules.HasActiveFief(FiefCacheRules.UnknownGeneralId))
                throw new Exception("Unknown fief cache state must not be treated as active without a DB lookup.");
        }

        private static void ExpectCityEconomyMilestoneRules()
        {
            if (!CityEconomyMilestoneRules.ShouldRecord(pExisted: false, pRoleChanged: false,
                    pTaxValue: 2f, pCurrentYear: 100, pLastMajorTaxYear: -99999))
                throw new Exception("New city economy records should write an initial milestone.");
            if (!CityEconomyMilestoneRules.ShouldRecord(pExisted: true, pRoleChanged: true,
                    pTaxValue: 2f, pCurrentYear: 100, pLastMajorTaxYear: 99))
                throw new Exception("City economy role changes should be recorded.");
            if (CityEconomyMilestoneRules.ShouldRecord(pExisted: true, pRoleChanged: false,
                    pTaxValue: 30f, pCurrentYear: 100, pLastMajorTaxYear: 95))
                throw new Exception("Major tax milestones should not be recorded every year.");
            if (!CityEconomyMilestoneRules.ShouldRecord(pExisted: true, pRoleChanged: false,
                    pTaxValue: 30f, pCurrentYear: 120, pLastMajorTaxYear: 95))
                throw new Exception("Major tax milestones should be recorded again after the cooldown.");
            if (CityEconomyMilestoneRules.ShouldRecord(pExisted: true, pRoleChanged: false,
                    pTaxValue: 12f, pCurrentYear: 120, pLastMajorTaxYear: -99999))
                throw new Exception("Unchanged ordinary economy records should stay quiet.");
        }

        private static void ExpectXiaNameRepairRules()
        {
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName("NAME"))
                throw new Exception("NAME placeholder must be repaired before it reaches vanilla meta windows.");
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName("No_Name [Xia]"))
                throw new Exception("NO_NAME placeholders must be repaired.");
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(""))
                throw new Exception("Empty generated names must be repaired.");
            if (XiaNameRepairRules.IsInvalidGeneratedMetaName("\u5468"))
                throw new Exception("A real single-character kingdom name must not be treated as invalid.");
            if (!XiaNameRepairRules.IsInvalidXiaSubspeciesName("\u590F\u4EBA\u4EBA"))
                throw new Exception("Duplicated Xia subspecies names must be repaired.");
            if (XiaNameRepairRules.IsInvalidXiaSubspeciesName("\u534E\u590F\u4EBA"))
                throw new Exception("A valid Xia subspecies name must not be repaired.");
            if (!XiaNameRepairRules.IsInvalidXiaReligionName("NO\u2014\u2014Name\u4FE1\u4EF0"))
                throw new Exception("Placeholder Xia religion names must be repaired.");
            if (XiaNameRepairRules.IsInvalidXiaReligionName("\u793E\u7A37\u793C"))
                throw new Exception("A valid Xia religion name must not be repaired.");
        }

        private static void ExpectXiaFallbackNameRules()
        {
            if (XiaFallbackNameRules.FirstUsefulMetaName("NAME", "", "\u5468") != "\u5468")
                throw new Exception("Fallback selector must skip NAME placeholders before accepting a real meta name.");
            if (XiaFallbackNameRules.FirstUsefulSubspeciesName("\u590F\u4EBA", "\u590F\u4EBA\u4EBA", "\u534E\u590F\u4EBA") != "\u534E\u590F\u4EBA")
                throw new Exception("Subspecies fallback selector must skip bare or duplicated Xia names.");

            for (long seed = 0; seed < 24; seed++)
            {
                if (XiaNameRepairRules.IsInvalidGeneratedMetaName(XiaFallbackNameRules.LocalKingdomName(seed)))
                    throw new Exception("Local kingdom fallback must not produce placeholder names.");
                if (XiaNameRepairRules.IsInvalidGeneratedMetaName(XiaFallbackNameRules.LocalLanguageName(seed)))
                    throw new Exception("Local language fallback must not produce placeholder names.");
                if (XiaNameRepairRules.IsInvalidXiaReligionName(XiaFallbackNameRules.LocalReligionName(seed)))
                    throw new Exception("Local religion fallback must not produce placeholder names.");
                if (XiaNameRepairRules.IsInvalidGeneratedMetaName(XiaFallbackNameRules.LocalCultureName(seed)))
                    throw new Exception("Local culture fallback must not produce placeholder names.");
                if (XiaNameRepairRules.IsInvalidXiaSubspeciesName(XiaFallbackNameRules.LocalSubspeciesName(seed)))
                    throw new Exception("Local subspecies fallback must not produce bare or duplicated Xia names.");
            }
        }

        private static void ExpectXiaCityNameLibraryRules()
        {
            var names = XiaCityNameLibraryRules.ExtractQuotedNames("\"广南\",\"淮阴\", \"广南\", \"\"");
            if (names.Count != 2 || names[0] != "广南" || names[1] != "淮阴")
                throw new Exception("Xia city name import should extract quoted names with stable de-duplication.");

            var imported = XiaCityNameLibraryRules.GetImportedCityNames();
            if (imported.Count != 502 || imported[0] != "广南" || imported[imported.Count - 1] != "田州")
                throw new Exception("Xia city name library should mirror the Sui/Zhou imported place list.");

            if (!XiaCityNameLibraryRules.ShouldUseOnlyRealCityTemplates(new[] { "real" }))
                throw new Exception("Xia city fallback generator should accept the real-name template.");
            if (XiaCityNameLibraryRules.ShouldUseOnlyRealCityTemplates(new[] { "real", "prefix,suffix" }))
                throw new Exception("Xia city fallback generator must reject legacy prefix/suffix templates.");
            if (!XiaCityNameLibraryRules.IsLegacyCityTemplate("{中文城名上}{中文城名下}"))
                throw new Exception("Chinese_Name Xia city generator must reject the old split-name template.");
        }

        private static void ExpectCityTechChronicleRules()
        {
            if (!CityTechChronicleRules.ShouldRecordNationalCompletionInKingdomHistory())
                throw new Exception("National tech completion must remain visible in kingdom history.");
            if (CityTechChronicleRules.ShouldRecordCityAdoptionInKingdomHistory())
                throw new Exception("City tech adoption/transmission must be kept in city chronicles only.");
        }

        private static void ExpectCityMaintenanceBenchmarkRules()
        {
            string[] ids = CityMaintenanceBenchmarkRules.EntryIds;
            if (ids.Length < 7)
                throw new Exception("City maintenance benchmark needs enough component entries.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_retirements"))
                throw new Exception("City retirement benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_slave_labor"))
                throw new Exception("City slave labor benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard"))
                throw new Exception("Royal guard benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_fief_command"))
                throw new Exception("Fief command benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_army_cleanup"))
                throw new Exception("City army cleanup benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_food"))
                throw new Exception("Vanilla city food benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_status"))
                throw new Exception("Vanilla city status benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_citizens"))
                throw new Exception("Vanilla city citizens benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_capture"))
                throw new Exception("Vanilla city capture benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_candidates"))
                throw new Exception("Royal guard candidate scan benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_refresh"))
                throw new Exception("Royal guard identity refresh benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_refresh_captain"))
                throw new Exception("Royal guard captain refresh detail benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_refresh_batch"))
                throw new Exception("Royal guard batch refresh detail benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_refresh_persist"))
                throw new Exception("Royal guard persist refresh detail benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_refresh_runtime"))
                throw new Exception("Royal guard runtime refresh detail benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_slave_catchers_target_scan"))
                throw new Exception("Slave catcher target scan benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_slave_army_name_scan"))
                throw new Exception("Slave army name scan benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_army_cleanup_guard_strip"))
                throw new Exception("Army cleanup guard strip benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_fief_command_captain"))
                throw new Exception("Fief command captain benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_active_army_fast_path"))
                throw new Exception("Royal guard active army fast-path benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_active_fallback_scan"))
                throw new Exception("Royal guard active fallback scan benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_candidate_scan"))
                throw new Exception("Royal guard candidate scan detail benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_candidate_score"))
                throw new Exception("Royal guard candidate score detail benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_city_royal_guard_candidate_sort"))
                throw new Exception("Royal guard candidate sort detail benchmark entry is missing.");
            if (!CityMaintenanceBenchmarkRules.Contains("aw3_death_bond_child_scan"))
                throw new Exception("Death bond child scan benchmark entry is missing.");
        }

        private static void ExpectUpdateAgeBenchmarkRules()
        {
            string[] ids = UpdateAgeBenchmarkRules.EntryIds;
            if (ids.Length < 18)
                throw new Exception("UpdateAge benchmark needs actor, city, and kingdom component entries.");
            if (UpdateAgeBenchmarkRules.ParentGroup != "update_age")
                throw new Exception("AW3 updateAge total benchmark should appear under vanilla update_age.");
            if (UpdateAgeBenchmarkRules.Total != "aw3_update_age_total")
                throw new Exception("UpdateAge details should use the AW3 update-age parent entry.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_update_object_age_wall"))
                throw new Exception("UpdateAge benchmark needs a full MapBox.updateObjectAge wall-clock entry.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_update_age_unaccounted_wall"))
                throw new Exception("UpdateAge benchmark needs an unaccounted wall-clock entry for vanilla/overhead time.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_actor_update_age_wall"))
                throw new Exception("UpdateAge benchmark needs full actor updateAge wall-clock entry.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_city_update_age_wall"))
                throw new Exception("UpdateAge benchmark needs full city updateAge wall-clock entry.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_kingdom_update_age_wall"))
                throw new Exception("UpdateAge benchmark needs full kingdom updateAge wall-clock entry.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_actor_update_age_retirement"))
                throw new Exception("Actor retirement updateAge benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_actor_update_age_old_head"))
                throw new Exception("Actor old-head updateAge benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_city_update_age_slave_food"))
                throw new Exception("City slave-food updateAge benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_kingdom_update_age_policy"))
                throw new Exception("Kingdom policy updateAge benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_kingdom_update_age_mandate"))
                throw new Exception("Kingdom mandate updateAge benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_kingdom_update_age_war_ai"))
                throw new Exception("Kingdom war AI updateAge benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_kingdom_update_age_vassal_ai"))
                throw new Exception("Kingdom vassal AI updateAge benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_kingdom_update_age_general"))
                throw new Exception("Kingdom general updateAge benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_kingdom_policy_ai"))
                throw new Exception("Kingdom policy AI sub-benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_city_tech_spread_completed"))
                throw new Exception("City tech completed-spread sub-benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_city_tech_neighbor_influence"))
                throw new Exception("City tech neighbor-influence sub-benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_city_economy_update_cities"))
                throw new Exception("City economy per-city sub-benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_city_economy_tech_report"))
                throw new Exception("City economy tech-report sub-benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_city_economy_slave_count"))
                throw new Exception("City economy slave-count sub-benchmark entry is missing.");
            if (!UpdateAgeBenchmarkRules.Contains("aw3_city_economy_db_upsert"))
                throw new Exception("City economy DB-upsert sub-benchmark entry is missing.");
        }

        private static void ExpectDeathBondRules()
        {
            if (!DeathBondRules.ShouldRecordBondDeathForParentsAndLover(pDeadIsTraceable: true))
                throw new Exception("Traceable deaths should still notify parents and lovers.");
            if (DeathBondRules.ShouldUseWorldScanForChildren(
                    pCanUseActorChildrenList: true,
                    pDeadIsImportant: true))
                throw new Exception("Death bond should prefer Actor.getChildren over world scans.");
            if (DeathBondRules.ShouldUseWorldScanForChildren(
                    pCanUseActorChildrenList: false,
                    pDeadIsImportant: false))
                throw new Exception("Common deaths must not scan all world units for children.");
            if (!DeathBondRules.ShouldUseWorldScanForChildren(
                    pCanUseActorChildrenList: false,
                    pDeadIsImportant: true))
                throw new Exception("Only important deaths may use the expensive child-scan fallback.");
        }

        private static void ExpectXiaItemEffectRules()
        {
            if (XiaItemEffectRules.ShouldApplyQingStatusEffect())
                throw new Exception("Qing hit effect should not use status effects; slash animation is enough.");
        }

        private static void ExpectTraitIconUsageRules()
        {
            if (TraitIconUsageRules.IconForTrait("figure") != "ui/Icons/traits/iconhistorical")
                throw new Exception("Historical figure trait should use iconhistorical.");
            if (TraitIconUsageRules.IconForTrait("aw_general") != "ui/Icons/traits/icondajiang")
                throw new Exception("General trait should use icondajiang.");
            if (TraitIconUsageRules.IconForTrait("aw_army_commander") != "ui/Icons/traits/iconjiang")
                throw new Exception("Army commander trait should use iconjiang.");
            if (TraitIconUsageRules.IconForTrait("formerking") != "ui/Icons/traits/iconformerking")
                throw new Exception("Former king trait should use iconformerking.");
            if (TraitIconUsageRules.IconForTrait("zhuhou") != "ui/Icons/traits/iconzhuhou")
                throw new Exception("Zhuhou identity should keep iconzhuhou.");
        }

        private static void ExpectVisibleClanRenameRules()
        {
            if (!VisibleClanRenameRules.TryNormalizeClanName(" 新氏 ", out string normalized) || normalized != "新")
                throw new Exception("Visible clan rename should trim whitespace and remove the shi suffix.");
            if (!VisibleClanRenameRules.TryNormalizeClanName("王", out normalized) || normalized != "王")
                throw new Exception("Visible clan rename should keep a single-character clan name.");
            if (VisibleClanRenameRules.TryNormalizeClanName("   氏  ", out _))
                throw new Exception("Visible clan rename should reject an empty clan name after suffix removal.");

            var ids = VisibleClanRenameRules.CollectValidVisibleActorIds(new long[] { 3, -1, 3, 8, 0, 8 });
            if (ids.Count != 3 || ids[0] != 3 || ids[1] != 8 || ids[2] != 0)
                throw new Exception("Visible clan rename should keep visible actor ids in first-seen order and remove duplicates.");

            if (!VisibleClanRenameRules.ShouldUpdateBranchName(pModeIsBigTree: true, pShiId: 7, pVisibleActorCount: 2))
                throw new Exception("Visible clan rename should update the current shi branch when renaming visible big-tree members.");
            if (VisibleClanRenameRules.ShouldUpdateBranchName(pModeIsBigTree: false, pShiId: 7, pVisibleActorCount: 2))
                throw new Exception("Family-tree visible rename should not update a whole shi branch name.");
            if (VisibleClanRenameRules.ShouldUpdateBranchName(pModeIsBigTree: true, pShiId: -1, pVisibleActorCount: 2))
                throw new Exception("Invalid shi id should not update a branch name.");

            if (!VisibleClanRenameRules.ShouldUseWholeShiTreeScope(pModeIsBigTree: true, pShiId: 7))
                throw new Exception("Big-tree clan rename should use the whole displayable shi tree scope.");
            if (VisibleClanRenameRules.ShouldUseWholeShiTreeScope(pModeIsBigTree: false, pShiId: 7))
                throw new Exception("Family-tree rename must not use whole shi tree scope.");
            if (VisibleClanRenameRules.ShouldUseWholeShiTreeScope(pModeIsBigTree: true, pShiId: -1))
                throw new Exception("Invalid shi id must not use whole shi tree scope.");
        }

        private static void ExpectHistoryContentNormalization(string pRaw, string pExpected)
        {
            string actual = WarDisplayLabelRules.NormalizeEmbeddedKeys(pRaw);
            if (actual != pExpected)
                throw new Exception($"Expected normalized history text '{pExpected}', got '{actual}'.");
        }

        private static void ExpectRestorationSettlementRules()
        {
            if (!RestorationSettlementRules.ShouldMoveClaimantToTargetCityBeforeKingdomCreation(
                    pClaimantInTargetCity: false))
                throw new Exception("Restoration must move the claimant to the target city before makeOwnKingdom.");
            if (RestorationSettlementRules.ShouldMoveClaimantToTargetCityBeforeKingdomCreation(
                    pClaimantInTargetCity: true))
                throw new Exception("Restoration should not move a claimant who is already in the target city.");
        }

        private static void ExpectWarGoalControlRules()
        {
            if (!WarGoalControlRules.ShouldResolveTransferredCityGoal(
                    "take_core_city",
                    pTargetCityMatchesGoal: true,
                    pNewOwnerIsWarAttacker: true))
                throw new Exception("Core target captured by any attacking-side kingdom should settle the war.");

            if (!WarGoalControlRules.ShouldResolveTransferredCityGoal(
                    "press_claim_city",
                    pTargetCityMatchesGoal: true,
                    pNewOwnerIsWarAttacker: true))
                throw new Exception("Claim target captured by any attacking-side kingdom should settle the war.");

            if (WarGoalControlRules.ShouldResolveTransferredCityGoal(
                    "take_core_city",
                    pTargetCityMatchesGoal: false,
                    pNewOwnerIsWarAttacker: true))
                throw new Exception("A captured non-target city must not settle a city war goal.");

            if (WarGoalControlRules.ShouldResolveTransferredCityGoal(
                    "take_core_city",
                    pTargetCityMatchesGoal: true,
                    pNewOwnerIsWarAttacker: false))
                throw new Exception("A target city held outside the attacking side must not settle.");

            if (!WarGoalControlRules.ShouldResolveControlledCityGoal(
                    "take_core_city",
                    pTargetCityControlledByAttackerSystem: true))
                throw new Exception("Controlled core targets should allow immediate war-goal settlement.");

            if (!WarGoalControlRules.ShouldResolveControlledCityGoal(
                    "press_claim_city",
                    pTargetCityControlledByAttackerSystem: true))
                throw new Exception("Controlled claim targets should allow immediate war-goal settlement.");

            if (!WarGoalControlRules.ShouldResolveTransferredCityGoal(
                    "mandate_conquest",
                    pTargetCityMatchesGoal: true,
                    pNewOwnerIsWarAttacker: true))
                throw new Exception("Mandate conquest target captures should settle the war.");

            if (!WarGoalControlRules.ShouldResolveControlledCityGoal(
                    "restore_kingdom",
                    pTargetCityControlledByAttackerSystem: true))
                throw new Exception("Restoration target control should settle the war.");

            if (WarGoalControlRules.ShouldResolveControlledCityGoal(
                    "take_mandate",
                    pTargetCityControlledByAttackerSystem: true))
                throw new Exception("Taking the Mandate itself must not settle from a single captured city.");

            if (WarGoalControlRules.ShouldResolveControlledCityGoal(
                    "force_vassal",
                    pTargetCityControlledByAttackerSystem: true))
                throw new Exception("Non-city-control goals must not settle from a captured target city.");

            if (WarGoalControlRules.ShouldResolveControlledCityGoal(
                    "take_core_city",
                    pTargetCityControlledByAttackerSystem: false))
                throw new Exception("Uncontrolled city goals must not settle.");
        }

        private static void ExpectHeirTitleRules()
        {
            if (HeirTitleRules.TitleKey(pIsMandateKingdom: false) != "aw_heir_shizi")
                throw new Exception("Non-Mandate heirs should be titled shizi.");
            if (HeirTitleRules.TitleKey(pIsMandateKingdom: true) != "aw_heir_taizi")
                throw new Exception("Mandate heirs should be titled taizi.");
            if (!HeirTitleRules.ShouldRewriteOriginalHeirTitle("heir"))
                throw new Exception("Original heir stats rows should be rewritten.");
            if (HeirTitleRules.ShouldRewriteOriginalHeirTitle("village_statistics_king"))
                throw new Exception("Only the heir row title should be rewritten.");
        }

        private static void ExpectArmyRetreatRules()
        {
            if (ArmyRetreatRules.ShouldRetreat(
                    pRole: "",
                    pBaselineUnits: 10,
                    pCurrentUnits: 4,
                    pCaptainAlive: true,
                    pIsAttacking: true,
                    pCooldownActive: false) == false)
                throw new Exception("Normal armies below 45 percent of their attack strength should retreat.");

            if (ArmyRetreatRules.ShouldRetreat(
                    pRole: "",
                    pBaselineUnits: 10,
                    pCurrentUnits: 5,
                    pCaptainAlive: true,
                    pIsAttacking: true,
                    pCooldownActive: false))
                throw new Exception("Normal armies at half strength should keep fighting.");

            if (!ArmyRetreatRules.ShouldRetreat(
                    pRole: AWArmyRole.SlaveArmy,
                    pBaselineUnits: 10,
                    pCurrentUnits: 3,
                    pCaptainAlive: true,
                    pIsAttacking: true,
                    pCooldownActive: false))
                throw new Exception("Slave armies should retreat when they collapse below their looser threshold.");

            if (ArmyRetreatRules.ShouldRetreat(
                    pRole: AWArmyRole.RoyalGuard,
                    pBaselineUnits: 20,
                    pCurrentUnits: 2,
                    pCaptainAlive: true,
                    pIsAttacking: true,
                    pCooldownActive: false))
                throw new Exception("Royal guards should not use the normal retreat mechanic.");

            if (ArmyRetreatRules.ShouldRetreat(
                    pRole: "",
                    pBaselineUnits: 7,
                    pCurrentUnits: 1,
                    pCaptainAlive: true,
                    pIsAttacking: true,
                    pCooldownActive: false))
                throw new Exception("Tiny armies should not trigger retreat churn.");

            if (!ArmyRetreatRules.ShouldRetreat(
                    pRole: "",
                    pBaselineUnits: 10,
                    pCurrentUnits: 5,
                    pCaptainAlive: false,
                    pIsAttacking: true,
                    pCooldownActive: false))
                throw new Exception("Armies with a lost captain should retreat earlier.");

            if (!ArmyRetreatRules.ShouldSkipAttackWhileRetreating(pRetreatUntilYear: 25, pCurrentYear: 24))
                throw new Exception("Retreating armies should stop attack AI until the cooldown ends.");
            if (ArmyRetreatRules.ShouldSkipAttackWhileRetreating(pRetreatUntilYear: 25, pCurrentYear: 25))
                throw new Exception("Retreat cooldown should expire at its end year.");
        }

        private static void ExpectCityOccupationAccelerationRules()
        {
            if (CityOccupationAccelerationRules.ExtraCapturePoints(
                    pIsBeingCapturedByEnemy: false,
                    pHasDefenders: false,
                    pHasCityControlGoal: true,
                    pWatchTowerCount: 0) != 0f)
                throw new Exception("Occupation acceleration should require an enemy capture.");

            float goalBonus = CityOccupationAccelerationRules.ExtraCapturePoints(
                pIsBeingCapturedByEnemy: true,
                pHasDefenders: false,
                pHasCityControlGoal: true,
                pWatchTowerCount: 0);
            float normalBonus = CityOccupationAccelerationRules.ExtraCapturePoints(
                pIsBeingCapturedByEnemy: true,
                pHasDefenders: false,
                pHasCityControlGoal: false,
                pWatchTowerCount: 0);
            if (goalBonus <= normalBonus || normalBonus <= 0f)
                throw new Exception("War-goal cities should capture faster than ordinary undefended cities.");

            if (CityOccupationAccelerationRules.ExtraCapturePoints(
                    pIsBeingCapturedByEnemy: true,
                    pHasDefenders: true,
                    pHasCityControlGoal: true,
                    pWatchTowerCount: 0) != 0f)
                throw new Exception("Cities with defenders should not receive occupation acceleration.");

            if (CityOccupationAccelerationRules.ExtraCapturePoints(
                    pIsBeingCapturedByEnemy: true,
                    pHasDefenders: false,
                    pHasCityControlGoal: true,
                    pWatchTowerCount: 2) >= goalBonus)
                throw new Exception("Watch towers should reduce the occupation acceleration bonus.");
        }

        private static void ExpectSlaveArmyNameRefreshRule()
        {
            if (!SlaveArmyMaintenanceRules.ShouldRefreshKingdomArmyNames(pIsSlaveArmy: true))
                throw new Exception("Expected slave armies to refresh kingdom slave army names.");
            if (SlaveArmyMaintenanceRules.ShouldRefreshKingdomArmyNames(pIsSlaveArmy: false))
                throw new Exception("Expected non-slave armies to skip kingdom-wide slave army name refresh.");
        }

        private static void ExpectCityMaintenanceThrottleRules()
        {
            if (!CityMaintenanceThrottleRules.ShouldRun(pNow: 100, pLastRun: -1, pInterval: 15))
                throw new Exception("Expected city maintenance to run when it has never run.");
            if (CityMaintenanceThrottleRules.ShouldRun(pNow: 110, pLastRun: 100, pInterval: 15))
                throw new Exception("Expected city maintenance to skip before interval.");
            if (!CityMaintenanceThrottleRules.ShouldRun(pNow: 115, pLastRun: 100, pInterval: 15))
                throw new Exception("Expected city maintenance to run at interval boundary.");
            if (!CityMaintenanceThrottleRules.ShouldRun(pNow: 90, pLastRun: 100, pInterval: 15))
                throw new Exception("Expected city maintenance to run when time moves backward.");
            if (CityMaintenanceThrottleRules.ShouldRunStaggered(pNow: 100, pLastRun: -1, pInterval: 10,
                    pObjectId: 3))
                throw new Exception("First staggered city maintenance should wait for its object slot.");
            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(pNow: 103, pLastRun: -1, pInterval: 10,
                    pObjectId: 3))
                throw new Exception("First staggered city maintenance should run on its object slot.");
            if (CityMaintenanceThrottleRules.ShouldRunStaggered(pNow: 112, pLastRun: 103, pInterval: 10,
                    pObjectId: 3))
                throw new Exception("Staggered city maintenance should not run before its interval slot.");
            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(pNow: 123, pLastRun: 103, pInterval: 10,
                    pObjectId: 3))
                throw new Exception("Staggered city maintenance should run on its next object slot.");
        }

        private static void ExpectRoyalGuardMaintenanceRules()
        {
            if (!RoyalGuardMaintenanceRules.ShouldCheckFromCity(
                    pHasCity: true,
                    pHasKingdom: true,
                    pHasCapital: true,
                    pIsCapital: true))
                throw new Exception("Royal guard kingdom maintenance should run from the capital city.");
            if (RoyalGuardMaintenanceRules.ShouldCheckFromCity(
                    pHasCity: true,
                    pHasKingdom: true,
                    pHasCapital: true,
                    pIsCapital: false))
                throw new Exception("Non-capital cities must not trigger kingdom-wide royal guard scans.");
            if (!RoyalGuardMaintenanceRules.ShouldCheckFromCity(
                    pHasCity: true,
                    pHasKingdom: true,
                    pHasCapital: false,
                    pIsCapital: false))
                throw new Exception("A kingdom without a known capital should keep a fallback guard check.");

            if (RoyalGuardMaintenanceRules.ShouldRunScheduledCheck(
                    pNow: 100,
                    pLastCheck: -1,
                    pInterval: 20,
                    pKingdomId: 3))
                throw new Exception("First royal guard maintenance should wait for its kingdom id slot.");
            if (!RoyalGuardMaintenanceRules.ShouldRunScheduledCheck(
                    pNow: 103,
                    pLastCheck: -1,
                    pInterval: 20,
                    pKingdomId: 3))
                throw new Exception("First royal guard maintenance should run on its staggered kingdom id slot.");
            if (RoyalGuardMaintenanceRules.ShouldRunScheduledCheck(
                    pNow: 119,
                    pLastCheck: 103,
                    pInterval: 20,
                    pKingdomId: 3))
                throw new Exception("Royal guard maintenance should not run before the interval even on nearby years.");
            if (!RoyalGuardMaintenanceRules.ShouldRunScheduledCheck(
                    pNow: 123,
                    pLastCheck: 103,
                    pInterval: 20,
                    pKingdomId: 3))
                throw new Exception("Royal guard maintenance should run again on the next kingdom id slot.");
            if (!RoyalGuardMaintenanceRules.ShouldDismissNonXiaKingdom(
                    pKingIsXia: false,
                    pHasGuardStateHint: true))
                throw new Exception("A non-Xia kingdom with guard state should dismiss stale royal guards.");
            if (RoyalGuardMaintenanceRules.ShouldDismissNonXiaKingdom(
                    pKingIsXia: false,
                    pHasGuardStateHint: false))
                throw new Exception("Ordinary non-Xia kingdoms with no guard state should skip royal guard scans.");

            if (RoyalGuardMaintenanceRules.ShouldSearchCandidates(
                    pActiveCount: 20,
                    pActiveNobleCount: 4,
                    pTargetNobleCount: 4,
                    pHasCaptain: true,
                    pRefillThreshold: 16))
                throw new Exception("A full valid royal guard should not scan the whole kingdom for candidates.");
            if (RoyalGuardMaintenanceRules.ShouldSearchCandidates(
                    pActiveCount: 18,
                    pActiveNobleCount: 4,
                    pTargetNobleCount: 4,
                    pHasCaptain: true,
                    pRefillThreshold: 16))
                throw new Exception("A healthy royal guard above the refill threshold should skip candidate scans.");
            if (RoyalGuardMaintenanceRules.ShouldSearchCandidates(
                    pActiveCount: 12,
                    pActiveNobleCount: 4,
                    pTargetNobleCount: 4,
                    pHasCaptain: true,
                    pRefillThreshold: 12))
                throw new Exception("Royal guard maintenance should not refill at the low-water mark.");
            if (!RoyalGuardMaintenanceRules.ShouldSearchCandidates(
                    pActiveCount: 11,
                    pActiveNobleCount: 4,
                    pTargetNobleCount: 4,
                    pHasCaptain: true,
                    pRefillThreshold: 12))
                throw new Exception("Royal guard maintenance should refill only after dropping below the lower low-water mark.");
            if (!RoyalGuardMaintenanceRules.ShouldSearchCandidates(
                    pActiveCount: 20,
                    pActiveNobleCount: 3,
                    pTargetNobleCount: 4,
                    pHasCaptain: true,
                    pRefillThreshold: 16))
                throw new Exception("Royal guard maintenance should scan when the noble quota is not satisfied.");
            if (!RoyalGuardMaintenanceRules.ShouldSearchCandidates(
                    pActiveCount: 20,
                    pActiveNobleCount: 4,
                    pTargetNobleCount: 4,
                    pHasCaptain: false,
                    pRefillThreshold: 16))
                throw new Exception("Royal guard maintenance should scan when no noble captain is available.");

            if (RoyalGuardMaintenanceRules.ShouldPersistGuardIdentityRefresh(
                    pWasGuard: true,
                    pWasCaptain: true,
                    pCaptain: true,
                    pKingdomChanged: false,
                    pNameChanged: false,
                    pMissingTrait: false,
                    pArmyChanged: false,
                    pProfessionChanged: false,
                    pJobChanged: false))
                throw new Exception("Stable guard identity should skip DB/archive/graphics refresh.");
            if (RoyalGuardMaintenanceRules.ShouldPersistGuardIdentityRefresh(
                    pWasGuard: true,
                    pWasCaptain: true,
                    pCaptain: true,
                    pKingdomChanged: false,
                    pNameChanged: false,
                    pMissingTrait: false,
                    pArmyChanged: true,
                    pProfessionChanged: true,
                    pJobChanged: true))
                throw new Exception("Runtime guard drift should be corrected without DB/archive/graphics refresh.");
            if (!RoyalGuardMaintenanceRules.ShouldApplyGuardRuntimeRefresh(
                    pArmyChanged: true,
                    pProfessionChanged: false,
                    pJobChanged: false))
                throw new Exception("Army drift must still be corrected with a lightweight runtime refresh.");
            if (RoyalGuardMaintenanceRules.ShouldPersistNewGuardDuringFill(pFinalRefreshWillRun: true))
                throw new Exception("New guard candidates should be persisted once in the final refresh, not during fill.");
            if (!RoyalGuardMaintenanceRules.ShouldPersistGuardIdentityRefresh(
                    pWasGuard: true,
                    pWasCaptain: false,
                    pCaptain: true,
                    pKingdomChanged: false,
                    pNameChanged: false,
                    pMissingTrait: false,
                    pArmyChanged: false,
                    pProfessionChanged: false,
                    pJobChanged: false))
                throw new Exception("Captain changes must persist guard identity.");
            if (!RoyalGuardMaintenanceRules.ShouldUseArmyFastPathForActiveGuards(
                    pGuardArmyFound: true,
                    pGuardArmyUnitCount: 1))
                throw new Exception("Royal guard active list should prefer the dedicated guard army.");
            if (!RoyalGuardMaintenanceRules.ShouldUseRosterFastPathForActiveGuards(
                    pGuardArmyFound: false,
                    pHasRoster: true))
                throw new Exception("Royal guard active list should use the roster before a kingdom-wide scan.");
            if (RoyalGuardMaintenanceRules.ShouldUseRosterFastPathForActiveGuards(
                    pGuardArmyFound: true,
                    pHasRoster: true))
                throw new Exception("Royal guard roster lookup should not replace a real guard army fast path.");
            if (!RoyalGuardMaintenanceRules.ShouldUseRosterForDismiss(
                    pHasRoster: true))
                throw new Exception("Royal guard dismissals should use the stored roster when it exists.");
            if (RoyalGuardMaintenanceRules.ShouldUseRosterForDismiss(
                    pHasRoster: false))
                throw new Exception("Royal guard dismissals without a roster must keep the legacy bounded fallback.");
            if (RoyalGuardMaintenanceRules.ShouldFallbackToKingdomScanForActiveGuards(
                    pGuardArmyFound: true,
                    pHasGuardStateHint: true))
                throw new Exception("Royal guard active scan should not fall back when a guard army exists.");
            if (!RoyalGuardMaintenanceRules.ShouldFallbackToKingdomScanForActiveGuards(
                    pGuardArmyFound: false,
                    pHasGuardStateHint: true))
                throw new Exception("Royal guard active scan should fall back only for dirty/missing guard-army state.");
            if (!RoyalGuardMaintenanceRules.ShouldKeepBoundedCandidate(
                    pCurrentCount: 31,
                    pLimit: 32,
                    pLowestScore: 10f,
                    pCandidateScore: 5f))
                throw new Exception("Bounded candidate pool should keep candidates until it reaches the limit.");
            if (!RoyalGuardMaintenanceRules.ShouldKeepBoundedCandidate(
                    pCurrentCount: 32,
                    pLimit: 32,
                    pLowestScore: 10f,
                    pCandidateScore: 11f))
                throw new Exception("Bounded candidate pool should replace the current weakest candidate.");
            if (RoyalGuardMaintenanceRules.ShouldKeepBoundedCandidate(
                    pCurrentCount: 32,
                    pLimit: 32,
                    pLowestScore: 10f,
                    pCandidateScore: 9f))
                throw new Exception("Bounded candidate pool should reject weak candidates after the limit.");
            if (!RoyalGuardMaintenanceRules.ShouldStopCandidateScan(
                    pScannedCount: 256,
                    pMaxScan: 256))
                throw new Exception("Royal guard candidate scans must have a hard per-pass cap.");
            if (RoyalGuardMaintenanceRules.ShouldStopCandidateScan(
                    pScannedCount: 255,
                    pMaxScan: 256))
                throw new Exception("Royal guard candidate scans should not stop before the hard cap.");
            if (RoyalGuardMaintenanceRules.NextBoundedScanCursor(
                    pStartCursor: 10,
                    pScannedCount: 64,
                    pScanComplete: false) != 74)
                throw new Exception("Bounded royal guard scans should continue from the next unscanned actor.");
            if (RoyalGuardMaintenanceRules.NextBoundedScanCursor(
                    pStartCursor: 10,
                    pScannedCount: 64,
                    pScanComplete: true) != 0)
                throw new Exception("Completed royal guard scans should reset their cursor.");
            if (RoyalGuardMaintenanceRules.MaxFastPathGuardArmyScan(20, 4) != 40)
                throw new Exception("Royal guard fast path should cap oversized army scans to twice the guard limit.");
            if (!RoyalGuardMaintenanceRules.ShouldStopFastPathGuardArmyScan(
                    pScanned: 40,
                    pActiveCount: 3,
                    pMaxActiveGuards: 20,
                    pMaxScan: 40))
                throw new Exception("Royal guard fast path must stop after the bounded scan cap.");
            if (!RoyalGuardMaintenanceRules.ShouldStopFastPathGuardArmyScan(
                    pScanned: 8,
                    pActiveCount: 20,
                    pMaxActiveGuards: 20,
                    pMaxScan: 40))
                throw new Exception("Royal guard fast path must stop after collecting the active guard cap.");
            if (!RoyalGuardMaintenanceRules.HasGuardDataForKingdom(
                    pGuardFlag: true,
                    pGuardKingdomId: -1,
                    pActorKingdomId: 9,
                    pTargetKingdomId: 9))
                throw new Exception("Legacy guard data without a kingdom id should be accepted only in the current kingdom.");
            if (RoyalGuardMaintenanceRules.HasGuardDataForKingdom(
                    pGuardFlag: false,
                    pGuardKingdomId: -1,
                    pActorKingdomId: 9,
                    pTargetKingdomId: 9))
                throw new Exception("Trait-only guards should not be treated as active in the fast path.");
            if (!RoyalGuardMaintenanceRules.ShouldRemoveStaleActorFromGuardArmy(
                    pHasGuardDataForKingdom: false,
                    pActorArmyIsGuardArmy: true,
                    pRemovedCount: 3,
                    pRemovalLimit: 4))
                throw new Exception("Fast path should remove a bounded number of stale non-guard actors from guard armies.");
            if (RoyalGuardMaintenanceRules.ShouldRemoveStaleActorFromGuardArmy(
                    pHasGuardDataForKingdom: false,
                    pActorArmyIsGuardArmy: true,
                    pRemovedCount: 4,
                    pRemovalLimit: 4))
                throw new Exception("Fast path stale cleanup must be bounded per maintenance pass.");
            if (!RoyalGuardMaintenanceRules.ShouldUseBoundedDismissScan(
                    pGuardArmyFound: false,
                    pHasGuardStateHint: true))
                throw new Exception("Dirty guard state without a guard army must be cleaned with a bounded scan.");
            if (RoyalGuardMaintenanceRules.ShouldUseBoundedDismissScan(
                    pGuardArmyFound: true,
                    pHasGuardStateHint: true))
                throw new Exception("A real guard army should use the army fast-path for dismissals.");
            if (!RoyalGuardMaintenanceRules.ShouldStopBoundedDismissScan(
                    pProcessed: 64,
                    pLimit: 64))
                throw new Exception("Bounded dismiss scans must stop at the per-pass limit.");
            if (RoyalGuardMaintenanceRules.ShouldStopBoundedDismissScan(
                    pProcessed: 63,
                    pLimit: 64))
                throw new Exception("Bounded dismiss scans should continue before the per-pass limit.");
            if (!RoyalGuardMaintenanceRules.ShouldClearGuardHintAfterFallbackScan(
                    pScanComplete: true,
                    pActiveGuardCount: 0,
                    pFoundGuardArmy: false))
                throw new Exception("A completed fallback scan with no active guards should clear stale guard hints.");
            if (RoyalGuardMaintenanceRules.ShouldClearGuardHintAfterFallbackScan(
                    pScanComplete: false,
                    pActiveGuardCount: 0,
                    pFoundGuardArmy: false))
                throw new Exception("Partial fallback scans must keep guard hints for the next bounded pass.");
            if (RoyalGuardMaintenanceRules.ShouldClearGuardHintAfterFallbackScan(
                    pScanComplete: true,
                    pActiveGuardCount: 2,
                    pFoundGuardArmy: false))
                throw new Exception("Fallback scans that find active guards must keep guard hints.");
            if (!RoyalGuardMaintenanceRules.ShouldKeepExistingCaptain(
                    pExistingCaptainValid: true,
                    pExistingCaptainNoble: true))
                throw new Exception("Royal guard should keep a valid noble captain instead of reselecting every pass.");
            if (RoyalGuardMaintenanceRules.ShouldKeepExistingCaptain(
                    pExistingCaptainValid: true,
                    pExistingCaptainNoble: false))
                throw new Exception("Royal guard captain cannot be kept if the noble requirement is no longer met.");
            if (RoyalGuardMaintenanceRules.ShouldKeepExistingCaptain(
                    pExistingCaptainValid: false,
                    pExistingCaptainNoble: true))
                throw new Exception("Invalid royal guard captain must be replaced.");
            if (RoyalGuardMaintenanceRules.ShouldDismissActiveGuardsForCaptainShortage(
                    pActiveGuardCount: 12,
                    pAvailableNobleCount: 0))
                throw new Exception("Temporary royal guard captain shortages must not bulk-dismiss existing guards.");
            if (!RoyalGuardMaintenanceRules.ShouldDeferGuardMaintenanceForCaptainShortage(
                    pActiveGuardCount: 12,
                    pAvailableNobleCount: 0))
                throw new Exception("Royal guard maintenance should defer when existing guards cannot satisfy noble captain rules.");
            if (RoyalGuardMaintenanceRules.ShouldDeferGuardMaintenanceForCaptainShortage(
                    pActiveGuardCount: 0,
                    pAvailableNobleCount: 0))
                throw new Exception("An empty royal guard should not enter a no-op defer loop.");
            if (RoyalGuardMaintenanceRules.ShouldDeferGuardMaintenanceForCaptainShortage(
                    pActiveGuardCount: 12,
                    pAvailableNobleCount: 1))
                throw new Exception("Royal guard maintenance should continue when a noble candidate exists.");
            if (!RoyalGuardMaintenanceRules.ShouldRefreshGuardInMaintenancePass(
                    pIsCaptain: true,
                    pIsNewlyAppointed: false,
                    pActorIndex: 19,
                    pCursor: 3,
                    pBatchLimit: 4,
                    pActiveCount: 20))
                throw new Exception("Royal guard captain must always be refreshed.");
            if (!RoyalGuardMaintenanceRules.ShouldRefreshGuardInMaintenancePass(
                    pIsCaptain: false,
                    pIsNewlyAppointed: true,
                    pActorIndex: 19,
                    pCursor: 3,
                    pBatchLimit: 4,
                    pActiveCount: 20))
                throw new Exception("Newly appointed guards must be refreshed immediately.");
            if (!RoyalGuardMaintenanceRules.ShouldRefreshGuardInMaintenancePass(
                    pIsCaptain: false,
                    pIsNewlyAppointed: false,
                    pActorIndex: 5,
                    pCursor: 3,
                    pBatchLimit: 4,
                    pActiveCount: 20))
                throw new Exception("Royal guard refresh should include actors inside the cursor batch.");
            if (RoyalGuardMaintenanceRules.ShouldRefreshGuardInMaintenancePass(
                    pIsCaptain: false,
                    pIsNewlyAppointed: false,
                    pActorIndex: 8,
                    pCursor: 3,
                    pBatchLimit: 4,
                    pActiveCount: 20))
                throw new Exception("Royal guard refresh should skip stable actors outside the cursor batch.");
            if (!RoyalGuardMaintenanceRules.ShouldRefreshGuardInMaintenancePass(
                    pIsCaptain: false,
                    pIsNewlyAppointed: false,
                    pActorIndex: 1,
                    pCursor: 18,
                    pBatchLimit: 4,
                    pActiveCount: 20))
                throw new Exception("Royal guard refresh cursor should wrap around the active list.");
            if (RoyalGuardMaintenanceRules.NextRefreshCursor(
                    pCursor: 18,
                    pActiveCount: 20,
                    pBatchLimit: 4) != 2)
                throw new Exception("Royal guard refresh cursor should advance and wrap.");
        }

        private static void ExpectAwArmyRoleRules()
        {
            if (!AWArmyRoleRules.IsSpecialRole(AWArmyRole.RoyalGuard) ||
                !AWArmyRoleRules.IsSpecialRole(AWArmyRole.SlaveArmy) ||
                !AWArmyRoleRules.IsSpecialRole(AWArmyRole.BorderArmy))
                throw new Exception("AW3 army layer must recognize royal guard, slave army, and border army roles.");

            if (AWArmyRoleRules.IsSpecialRole("") || AWArmyRoleRules.IsSpecialRole("normal"))
                throw new Exception("Ordinary armies must not be treated as AW3 special armies.");

            if (!AWArmyRoleRules.ShouldUseDetachedArmy(AWArmyRole.RoyalGuard))
                throw new Exception("Royal guards should use a detached army.");
            if (AWArmyRoleRules.ShouldUseDetachedArmy(AWArmyRole.SlaveArmy))
                throw new Exception("Slave armies should keep the original city binding for UI and city caps.");
            if (AWArmyRoleRules.ShouldUseDetachedArmy(AWArmyRole.BorderArmy))
                throw new Exception("Border armies should keep the original city binding for garrison behavior.");
            if (AWArmyRoleRules.MaxArmiesPerCity(AWArmyRole.SlaveArmy) != 1)
                throw new Exception("Each city may have at most one slave army.");
            if (AWArmyRoleRules.MaxArmiesPerKingdom(AWArmyRole.RoyalGuard) != 1)
                throw new Exception("Each kingdom may have at most one royal guard army.");
            if (AWArmyRoleRules.MaxArmiesPerKingdom(AWArmyRole.BorderArmy) != 3)
                throw new Exception("Each kingdom may have at most three border armies.");
            if (!AWArmyRoleRules.ShouldMatchArmyAnchor(AWArmyRole.RoyalGuard, pRequestedAnchorId: 10,
                    pArmyAnchorId: 99))
                throw new Exception("Royal guard army lookup must ignore changing actor city anchors.");
            if (AWArmyRoleRules.ShouldMatchArmyAnchor(AWArmyRole.SlaveArmy, pRequestedAnchorId: 10,
                    pArmyAnchorId: 99))
                throw new Exception("Slave army lookup must keep city anchor matching.");
            if (!AWArmyRoleRules.ShouldCleanupDuplicateArmy(AWArmyRole.RoyalGuard, pRequestedAnchorId: 10,
                    pArmyAnchorId: 99))
                throw new Exception("Royal guard duplicate cleanup must merge by kingdom.");
            if (!AWArmyRoleRules.ShouldCleanupDuplicateArmy(AWArmyRole.SlaveArmy, pRequestedAnchorId: 10,
                    pArmyAnchorId: 10))
                throw new Exception("Slave army duplicate cleanup must merge by city.");
            if (!AWArmyRoleRules.ShouldSetCaptain(pCurrentCaptainId: 10, pNewCaptainId: 11))
                throw new Exception("Special armies should set a new captain when the actor changes.");
            if (AWArmyRoleRules.ShouldSetCaptain(pCurrentCaptainId: 10, pNewCaptainId: 10))
                throw new Exception("Special armies must not append duplicate past captains for the same actor.");

            if (AWArmyRoleRules.DisplayName(AWArmyRole.BorderArmy, "周", 2) != "周 边军 2")
                throw new Exception("Border army names should include kingdom name, role label, and index.");
        }

        private static void ExpectSpecialArmyLookupCacheRules()
        {
            string royalKey = SpecialArmyLookupCacheRules.BuildKey(7, AWArmyRole.RoyalGuard, -1);
            string royalKeyWithCity = SpecialArmyLookupCacheRules.BuildKey(7, AWArmyRole.RoyalGuard, 88);
            if (royalKey != royalKeyWithCity)
                throw new Exception("Kingdom-wide special armies must ignore city ids in cache keys.");

            string slaveA = SpecialArmyLookupCacheRules.BuildKey(7, AWArmyRole.SlaveArmy, 88);
            string slaveB = SpecialArmyLookupCacheRules.BuildKey(7, AWArmyRole.SlaveArmy, 89);
            if (slaveA == slaveB)
                throw new Exception("City-scoped slave army cache keys must include the anchor city.");

            if (!SpecialArmyLookupCacheRules.ShouldUseCachedArmy(
                    pCachedArmyId: 12,
                    pCachedArmyAlive: true,
                    pRoleMatches: true,
                    pKingdomMatches: true,
                    pAnchorMatches: true))
                throw new Exception("Valid special army cache entries should be used before global scans.");

            if (SpecialArmyLookupCacheRules.ShouldUseCachedArmy(
                    pCachedArmyId: 12,
                    pCachedArmyAlive: true,
                    pRoleMatches: true,
                    pKingdomMatches: true,
                    pAnchorMatches: false))
                throw new Exception("Special army cache entries with stale anchors must be invalidated.");
        }

        private static void ExpectSlaveArmyFormationRules()
        {
            if (!SlaveArmyFormationRules.IsSlaveArmyComposition(
                    totalWarriors: 20,
                    slaveWarriors: 15,
                    nonSlaveWarriors: 5,
                    captainNonSlave: true))
                throw new Exception("A slave army may include at most five non-slave cadres including captain.");

            if (SlaveArmyFormationRules.IsSlaveArmyComposition(
                    totalWarriors: 20,
                    slaveWarriors: 14,
                    nonSlaveWarriors: 6,
                    captainNonSlave: true))
                throw new Exception("A slave army must reject more than five non-slave cadres.");

            if (SlaveArmyFormationRules.IsSlaveArmyComposition(
                    totalWarriors: 20,
                    slaveWarriors: 16,
                    nonSlaveWarriors: 4,
                    captainNonSlave: false))
                throw new Exception("A slave army captain must be non-slave.");

            if (!SlaveArmyFormationRules.CanAddSlaveToArmy(
                    totalWarriors: 10,
                    slaveWarriors: 7,
                    nonSlaveWarriors: 3))
                throw new Exception("Slave armies should prefer adding slaves while under the 80 percent target.");

            if (SlaveArmyFormationRules.CanAddNonSlaveCadre(
                    nonSlaveWarriors: 5,
                    hasNonSlaveCaptain: true))
                throw new Exception("Slave armies must cap non-slave cadres at five including captain.");

            if (!SlaveArmyMaintenanceRules.ShouldSkipStableArmyFill(
                    pArmyExists: true,
                    pTotalWarriors: 25,
                    pSlaveWarriors: 20,
                    pNonSlaveWarriors: 5,
                    pCaptainValid: true,
                    pCitySlaveCount: 20))
                throw new Exception("A full valid slave army should skip expensive fill scans.");
            if (!SlaveArmyMaintenanceRules.ShouldSkipStableArmyFill(
                    pArmyExists: true,
                    pTotalWarriors: 10,
                    pSlaveWarriors: 8,
                    pNonSlaveWarriors: 2,
                    pCaptainValid: true,
                    pCitySlaveCount: 8))
                throw new Exception("An underfilled slave army should skip expensive fill scans when no local slaves remain.");
            if (SlaveArmyMaintenanceRules.ShouldSkipStableArmyFill(
                    pArmyExists: true,
                    pTotalWarriors: 10,
                    pSlaveWarriors: 8,
                    pNonSlaveWarriors: 2,
                    pCaptainValid: true,
                    pCitySlaveCount: 12))
                throw new Exception("An underfilled slave army must still scan when local slaves remain available.");
            if (SlaveArmyMaintenanceRules.ShouldDriveFrontline(
                    pHasArmy: true,
                    pHasEnemies: false,
                    pOnSchedule: true))
                throw new Exception("Slave army frontline driving must be skipped outside wars.");
            if (!SlaveArmyMaintenanceRules.ShouldDriveFrontline(
                    pHasArmy: true,
                    pHasEnemies: true,
                    pOnSchedule: true))
                throw new Exception("Slave armies should still drive toward the front during scheduled wartime maintenance.");
            if (!SlaveArmyMaintenanceRules.ShouldStopFillBatch(
                    pAddedThisPass: 6,
                    pBatchLimit: 6))
                throw new Exception("Slave army fill should stop at the per-pass batch limit.");
            if (SlaveArmyMaintenanceRules.ShouldStopFillBatch(
                    pAddedThisPass: 5,
                    pBatchLimit: 6))
                throw new Exception("Slave army fill should continue before the batch limit.");
        }

        private static void ExpectSlaveCaptureCommandRules()
        {
            if (!SlaveCaptureCommandRules.CanCommandSlaveCapture(
                    pIsSlaveArmyCaptain: true,
                    pIsSlave: false,
                    pSlaveryEnabled: true))
                throw new Exception("Slave army non-slave captains should command slave capture.");

            if (SlaveCaptureCommandRules.CanCommandSlaveCapture(
                    pIsSlaveArmyCaptain: false,
                    pIsSlave: false,
                    pSlaveryEnabled: true))
                throw new Exception("Ordinary non-slave civilians must not be slave catchers.");

            if (SlaveCaptureCommandRules.CanCommandSlaveCapture(
                    pIsSlaveArmyCaptain: true,
                    pIsSlave: true,
                    pSlaveryEnabled: true))
                throw new Exception("Slave army captains must be non-slaves.");

            if (SlaveCaptureCommandRules.WaitAfterNoTarget(3f, 8f) != 3f)
                throw new Exception("No-target slave capture wait should use the low end for deterministic tests.");
            if (SlaveCaptureCommandRules.WaitAfterFailure(2f, 5f) != 2f)
                throw new Exception("Failed slave capture wait should use the low end for deterministic tests.");
            if (SlaveCaptureCommandRules.WaitAfterSuccess(5f, 10f) != 5f)
                throw new Exception("Successful slave capture wait should use the low end for deterministic tests.");
        }

        private static void ExpectNonCoreLoyaltyRules()
        {
            if (NonCoreLoyaltyRules.CalculatePenalty(pOwnedNonCore: false, pIsCapital: false) != 0)
                throw new Exception("Expected core cities to have no AW3 non-core loyalty penalty.");
            if (NonCoreLoyaltyRules.CalculatePenalty(pOwnedNonCore: true, pIsCapital: false) >= 0)
                throw new Exception("Expected non-core cities to lose loyalty.");
            if (NonCoreLoyaltyRules.CalculatePenalty(pOwnedNonCore: true, pIsCapital: true) <
                NonCoreLoyaltyRules.CalculatePenalty(pOwnedNonCore: true, pIsCapital: false))
                throw new Exception("Expected capitals to receive a softer non-core penalty.");
        }

        private static void ExpectWarTerritoryCacheRules()
        {
            string before = WarTerritoryCacheRules.BuildOwnedNonCoreKey(1, 10, 1);
            string after = WarTerritoryCacheRules.BuildOwnedNonCoreKey(1, 10, 2);
            if (before == after)
                throw new Exception("Expected non-core cache key to change when city owner changes.");
            if (WarTerritoryCacheRules.BuildOwnedNonCoreKey(-1, 10, 1) != "")
                throw new Exception("Expected invalid focus id to produce no non-core cache key.");
        }

        private static void ExpectFamilyTreePortraitFrameRules()
        {
            if (!FamilyTreeRelationRules.ShouldBuildLiveLineageNode(
                    isAlive: true,
                    isXia: false,
                    usesAwLineageSystem: true))
                throw new Exception("Family tree live nodes must use full lineage data for Xiaized non-Xia actors.");
            if (!FamilyTreeRelationRules.ShouldBuildLiveLineageNode(
                    isAlive: true,
                    isXia: true,
                    usesAwLineageSystem: false))
                throw new Exception("Family tree live nodes must use full lineage data for native Xia actors.");
            if (FamilyTreeRelationRules.ShouldBuildLiveLineageNode(
                    isAlive: false,
                    isXia: true,
                    usesAwLineageSystem: true))
                throw new Exception("Dead or rekt actors should use archived family tree snapshots.");
            if (!FamilyTreeRelationRules.ShouldUseReverseLiveParentLookup(
                    currentParentCount: 1,
                    hasLiveChild: true,
                    requestedByUi: true))
                throw new Exception("Family tree UI should recover missing parents from live parent child lists.");
            if (FamilyTreeRelationRules.ShouldUseReverseLiveParentLookup(
                    currentParentCount: 2,
                    hasLiveChild: true,
                    requestedByUi: true))
                throw new Exception("Family tree UI should not scan live parents when both parent slots are known.");
            if (FamilyTreeRelationRules.ShouldUseReverseLiveParentLookup(
                    currentParentCount: 0,
                    hasLiveChild: true,
                    requestedByUi: false))
                throw new Exception("High-frequency lineage queries must not run reverse live parent lookup by default.");

            if (FamilyTreePortraitFrameRules.ShouldShowRoleFrame(false, false, false))
                throw new Exception("Common family tree nodes must not show a leader/captain/king frame.");
            if (!FamilyTreePortraitFrameRules.ShouldShowRoleFrame(true, false, false))
                throw new Exception("King family tree nodes must show a role frame.");
            if (!FamilyTreePortraitFrameRules.ShouldShowRoleFrame(false, true, false))
                throw new Exception("City leader family tree nodes must show a role frame.");
            if (!FamilyTreePortraitFrameRules.ShouldShowRoleFrame(false, false, true))
                throw new Exception("Army captain family tree nodes must show a role frame.");
        }

        private static void ExpectClanBannerFrameRules()
        {
            if (!ClanBannerFrameRules.ShouldCacheDefaultFrame(
                    pHasCurrentFrame: true,
                    pCurrentIsXiaFrame: false,
                    pDefaultKnown: false))
                throw new Exception("A clan banner should cache its prefab frame before Xia replacement.");

            if (ClanBannerFrameRules.ShouldCacheDefaultFrame(
                    pHasCurrentFrame: true,
                    pCurrentIsXiaFrame: true,
                    pDefaultKnown: false))
                throw new Exception("A Xia frame must not be cached as the default clan frame.");

            if (!ClanBannerFrameRules.ShouldApplyXiaFrame(
                    pIsXiaClan: true,
                    pHasXiaFrame: true))
                throw new Exception("Xia clans should use the Xia clan frame.");

            if (ClanBannerFrameRules.ShouldApplyXiaFrame(
                    pIsXiaClan: false,
                    pHasXiaFrame: true))
                throw new Exception("Human/non-Xia clans must not use the Xia clan frame.");

            if (!ClanBannerFrameRules.ShouldRestoreDefaultFrame(
                    pIsXiaClan: false,
                    pDefaultKnown: true))
                throw new Exception("Human/non-Xia clans should restore the cached default frame.");

            if (ClanBannerFrameRules.ShouldRestoreDefaultFrame(
                    pIsXiaClan: true,
                    pDefaultKnown: true))
                throw new Exception("Xia clans should keep the Xia frame instead of restoring default.");
        }

        private static void ExpectFamilyTreeToolbarLayoutRules()
        {
            const float windowWidth = 480f;
            const float buttonWidth = 78f;
            const float inset = 80f;
            float x = FamilyTreeToolbarLayoutRules.RightAlignedX(inset);

            if (Math.Abs(x - 80f) > 0.01f)
                throw new Exception($"Family tree clan rename toolbar x should be 80, got {x}.");
            if (Math.Abs(FamilyTreeToolbarLayoutRules.RightAlignedX(12f) - 12f) > 0.01f)
                throw new Exception("Family tree toolbar x should use the provided positive offset.");
            if (Math.Abs(FamilyTreeToolbarLayoutRules.RightAlignedX(-80f) - 80f) > 0.01f)
                throw new Exception("Family tree toolbar x should normalize negative offsets to positive offsets.");
            if (!FamilyTreeToolbarLayoutRules.StaysInsideRightEdge(windowWidth, buttonWidth, x))
                throw new Exception("Family tree toolbar buttons must stay inside the window right edge.");
        }

        private static void ExpectVassalNameplateFlagLayoutRules()
        {
            if (VassalNameplateFlagLayoutRules.FlagSize < 20f)
                throw new Exception("Suzerain nameplate flag must be large enough to read on the kingdom nameplate.");
            if (VassalNameplateFlagLayoutRules.IconInset < 1f ||
                VassalNameplateFlagLayoutRules.IconInset >= VassalNameplateFlagLayoutRules.FlagSize / 3f)
                throw new Exception("Suzerain nameplate flag icon inset should keep the banner visible without shrinking it too much.");
        }

        private static void ExpectFabricateCoreDecisionPriority()
        {
            int fabricateCore = KingdomDecisionPriorityRules.ScoreDecision("aw_decision_fabricate_core",
                pCanStabilizeMandate: false,
                pCanRoyalExpansion: false,
                pCityCount: 1,
                pSlaveryEnabled: false,
                pXiaizationScore: 0,
                pMissingYearName: false);
            int claimMandate = KingdomDecisionPriorityRules.ScoreDecision("aw_decision_claim_mandate",
                pCanStabilizeMandate: false,
                pCanRoyalExpansion: false,
                pCityCount: 10,
                pSlaveryEnabled: false,
                pXiaizationScore: 0,
                pMissingYearName: false);
            int titleUpgrade = KingdomDecisionPriorityRules.ScoreDecision("aw_decision_title_upgrade",
                pCanStabilizeMandate: false,
                pCanRoyalExpansion: false,
                pCityCount: 10,
                pSlaveryEnabled: false,
                pXiaizationScore: 0,
                pMissingYearName: false);

            if (fabricateCore <= claimMandate || fabricateCore <= titleUpgrade)
                throw new Exception("Fabricating cores must be the highest priority auto decision.");
        }

        private static void ExpectCoreFabricationSlotRules()
        {
            if (!CoreFabricationSlotRules.ShouldUseDedicatedSlot("fabricate_core"))
                throw new Exception("Core fabrication must use its dedicated slot.");
            if (CoreFabricationSlotRules.ShouldUseDedicatedSlot("fabricate_strong_claim"))
                throw new Exception("Claim fabrication must stay in the normal decision slot.");
            if (!CoreFabricationSlotRules.ShouldStartWhenEmpty(currentCoreCityId: -1, hasAvailableCoreTarget: true))
                throw new Exception("An empty core fabrication slot should immediately start an available core target.");
            if (!CoreFabricationSlotRules.ShouldQueueWhenBusy(currentCoreCityId: 12, hasAvailableCoreTarget: true))
                throw new Exception("A busy core fabrication slot should queue additional core targets.");
            if (CoreFabricationSlotRules.ShouldQueueWhenBusy(currentCoreCityId: -1, hasAvailableCoreTarget: true))
                throw new Exception("An empty core fabrication slot should start immediately instead of queueing.");
            if (!CoreFabricationSlotRules.ShouldShowDecisionSidebarButton(pIsDecisionPanel: true, pPolicyEnabled: true))
                throw new Exception("Core fabrication queue entry belongs in the enabled decision panel sidebar.");
            if (CoreFabricationSlotRules.ShouldShowDecisionSidebarButton(pIsDecisionPanel: false, pPolicyEnabled: true))
                throw new Exception("Core fabrication queue entry must not show outside the decision panel.");
            if (CoreFabricationSlotRules.ShouldShowDecisionSidebarButton(pIsDecisionPanel: true, pPolicyEnabled: false))
                throw new Exception("Core fabrication queue entry must not show for kingdoms without policy enabled.");
            if (CoreFabricationSlotRules.BuildSidebarLabel("", 0, 0) != "核心队列")
                throw new Exception("Empty core fabrication queue should show a stable queue entry label.");
            if (CoreFabricationSlotRules.BuildSidebarLabel("洛阳", 2, 45) != "核心\n45%/2")
                throw new Exception("Busy core fabrication queue should show progress and total project count.");
        }

        private static void ExpectDecisionQueueRules()
        {
            if (DecisionQueueRules.ShouldPreemptCurrentDecisionForCore(
                    currentDecisionId: "aw_decision_title_upgrade",
                    coreDecisionAvailable: true))
                throw new Exception("Core fabrication must not preempt the normal decision slot after it has a dedicated slot.");

            if (DecisionQueueRules.ShouldPreemptCurrentDecisionForCore(
                    currentDecisionId: "aw_decision_fabricate_core",
                    coreDecisionAvailable: true))
                throw new Exception("Current core fabrication must not preempt itself.");

            if (DecisionQueueRules.ShouldPreemptCurrentDecisionForCore(
                    currentDecisionId: "aw_decision_title_upgrade",
                    coreDecisionAvailable: false))
                throw new Exception("Core fabrication must not preempt when no core target is available.");

            if (!DecisionQueueRules.ShouldQueueDecisionWhenBusy(
                    currentDecisionId: "aw_decision_title_upgrade",
                    nextDecisionId: "aw_decision_royal_expansion"))
                throw new Exception("A new decision should enter the queue when another decision is active.");

            if (DecisionQueueRules.ShouldQueueDecisionWhenBusy(
                    currentDecisionId: "",
                    nextDecisionId: "aw_decision_royal_expansion"))
                throw new Exception("An empty decision slot should start the decision immediately, not queue it.");
        }

        private static void ExpectPolicyNodeLockRules()
        {
            if (!PolicyNodeLockRules.IsLocked("aw_policy_slave_army;aw_tech_city_defense",
                    "aw_policy_slave_army"))
                throw new Exception("Expected node to be locked.");
            if (PolicyNodeLockRules.IsLocked("aw_policy_slave_army;aw_tech_city_defense",
                    "aw_policy_name_integration"))
                throw new Exception("Expected unrelated node to be unlocked.");
            if (PolicyNodeLockRules.ShouldAllowStart("aw_decision_claim_mandate",
                    "aw_decision_claim_mandate"))
                throw new Exception("Expected locked decision start to be rejected.");
            if (!PolicyNodeLockRules.ShouldAllowStart("aw_decision_claim_mandate",
                    "aw_decision_year_name"))
                throw new Exception("Expected unlocked decision start to be allowed.");
            if (!PolicyNodeLockRules.ShouldClearCurrent("aw_tech_city_defense", "aw_tech_city_defense"))
                throw new Exception("Expected matching current node to be cleared.");
            if (PolicyNodeLockRules.ShouldClearCurrent("aw_tech_city_defense", "aw_tech_writing"))
                throw new Exception("Expected different current node to remain.");
            if (!PolicyNodeLockRules.ShouldClearCoreFabrication("aw_decision_fabricate_core"))
                throw new Exception("Expected core fabrication lock to clear dedicated slot.");
            if (PolicyNodeLockRules.ShouldClearCoreFabrication("aw_decision_fabricate_weak_claim"))
                throw new Exception("Expected weak claim lock not to clear core fabrication.");

            string locked = PolicyNodeLockRules.SetLocked("", "aw_policy_slave_army", true);
            if (!PolicyNodeLockRules.IsLocked(locked, "aw_policy_slave_army"))
                throw new Exception("Expected SetLocked to add node.");
            locked = PolicyNodeLockRules.SetLocked(locked, "aw_policy_slave_army", false);
            if (PolicyNodeLockRules.IsLocked(locked, "aw_policy_slave_army"))
                throw new Exception("Expected SetLocked false to remove node.");

            if (!PolicyNodeLockRules.ShouldAllowStart("", "aw_decision_declare_war"))
                throw new Exception("Expected empty lock set to allow start.");
            if (PolicyNodeLockRules.ShouldAllowStart("aw_decision_declare_war", "aw_decision_declare_war"))
                throw new Exception("Expected locked war decision to be rejected.");
        }

        private static void ExpectTechResearchPaceRules()
        {
            if (Math.Abs(TechResearchPaceRules.FrontierMultiplier(
                    pIsTech: true,
                    pOwnTechLevel: 3,
                    pWorldMaxTechLevel: 3) - 1f) > 0.001f)
                throw new Exception("Tech level 3 should not receive frontier slowdown.");

            if (Math.Abs(TechResearchPaceRules.FrontierMultiplier(
                    pIsTech: true,
                    pOwnTechLevel: 4,
                    pWorldMaxTechLevel: 4) - TechResearchPaceRules.Level4FrontierMultiplier) > 0.001f)
                throw new Exception("A level 4 world-leading tech kingdom should research more slowly.");

            if (Math.Abs(TechResearchPaceRules.FrontierMultiplier(
                    pIsTech: true,
                    pOwnTechLevel: 5,
                    pWorldMaxTechLevel: 5) - TechResearchPaceRules.Level5FrontierMultiplier) > 0.001f)
                throw new Exception("A level 5 world-leading tech kingdom should receive the strongest slowdown.");

            if (Math.Abs(TechResearchPaceRules.FrontierMultiplier(
                    pIsTech: true,
                    pOwnTechLevel: 4,
                    pWorldMaxTechLevel: 5) - 1f) > 0.001f)
                throw new Exception("A high tech kingdom should not slow down when it is no longer the highest tech level.");

            if (Math.Abs(TechResearchPaceRules.FrontierMultiplier(
                    pIsTech: false,
                    pOwnTechLevel: 5,
                    pWorldMaxTechLevel: 5) - 1f) > 0.001f)
                throw new Exception("Frontier slowdown must not affect social policies or decisions.");
        }

        private static void ExpectForeignOccupationDetectionRules()
        {
            string type;
            if (ForeignOccupationDetectionRules.TryDetectOccupation(
                    ownerIsXia: false,
                    legalCore: true,
                    mandateCoreControlRatio: 0.8f,
                    cityHasXiaIdentity: false,
                    differentCultureOrLanguage: false,
                    sameOwnerOriginCity: true,
                    out type))
                throw new Exception("A foreign Mandate realm's own native city must not be recorded as foreign occupation.");

            if (!ForeignOccupationDetectionRules.TryDetectOccupation(
                    ownerIsXia: false,
                    legalCore: true,
                    mandateCoreControlRatio: 0.8f,
                    cityHasXiaIdentity: true,
                    differentCultureOrLanguage: true,
                    sameOwnerOriginCity: false,
                    out type) || type != "pseudo_dynasty")
                throw new Exception("A foreign realm controlling Xia legal-core cities should still be pseudo-dynasty occupation.");

            if (!ForeignOccupationDetectionRules.TryDetectOccupation(
                    ownerIsXia: false,
                    legalCore: false,
                    mandateCoreControlRatio: 0f,
                    cityHasXiaIdentity: false,
                    differentCultureOrLanguage: true,
                    sameOwnerOriginCity: false,
                    out type) || type != "normal_conquest")
                throw new Exception("Different-culture foreign conquest should still be recorded as occupation.");
        }

        private static void ExpectWarLabel(string pKey, string pExpected)
        {
            string actual = WarDisplayLabelRules.Label(pKey);
            if (actual != pExpected)
                throw new Exception($"Expected war label '{pExpected}', got '{actual}' for key '{pKey}'.");
        }

        private static void ExpectWarDecisionTargetOrder()
        {
            if (WarDecisionTargetOrderRules.SortOrder("fabricate_core") != 0)
                throw new Exception("Core fabrication should appear before war target rows.");
            if (WarDecisionTargetOrderRules.SortOrder("take_mandate") >=
                WarDecisionTargetOrderRules.SortOrder("take_core_city"))
                throw new Exception("Mandate seizure should be the first war reason against the current Mandate kingdom.");
            if (WarDecisionTargetOrderRules.SortOrder("take_core_city") >=
                WarDecisionTargetOrderRules.SortOrder("press_claim_city"))
                throw new Exception("Core-reclaim wars must sort before claim wars.");
            if (WarDecisionTargetOrderRules.SortOrder("restore_kingdom") >=
                WarDecisionTargetOrderRules.SortOrder("force_vassal"))
                throw new Exception("Restoration wars must sort before force-vassal wars.");
            if (WarDecisionTargetOrderRules.SortOrder("no_cb_punitive") <=
                WarDecisionTargetOrderRules.SortOrder("independence"))
                throw new Exception("No-CB wars should stay at the bottom of the target list.");
        }

        private static void ExpectWarDecisionTargetDisplayRules()
        {
            string richTarget = "<color=#88ff88>\u8d8a</color>";
            string label = WarDecisionTargetTextRules.BuildRowLabel(richTarget, "\u6536\u590d\u6838\u5fc3");
            if (label != "<color=#88ff88>\u8d8a</color>\uff1a\u6536\u590d\u6838\u5fc3")
                throw new Exception($"Unexpected war target row label: '{label}'.");

            string stats = WarDecisionTargetTextRules.BuildStatsLine(2, 3, 1, 4,
                "<color=#ffaa00>\u4f1a\u7a3d</color>");
            if (stats != "\u68382 \u5f3a3 \u5f311 \u90204 \u76ee\u6807\uff1a<color=#ffaa00>\u4f1a\u7a3d</color>")
                throw new Exception($"Unexpected war target stats line: '{stats}'.");
        }

        private static void ExpectHistoryPeriodRules()
        {
            if (HistoryPeriodRules.NormalizeEndTime(121.0, 2.0) != -1.0)
                throw new Exception("City history periods must not end before they start.");
            if (HistoryPeriodRules.NormalizeEndTime(121.0, -1.0) != -1.0)
                throw new Exception("Open city history periods should stay open.");
            if (Math.Abs(HistoryPeriodRules.NormalizeEndTime(121.0, 130.0) - 130.0) > 0.001)
                throw new Exception("Valid city history end time should be preserved.");
            if (Math.Abs(HistoryPeriodRules.CloseEndBeforeNextStart(82.0, -1.0, 83.0) - 83.0) > 0.001)
                throw new Exception("Open old city/reign periods must close at the next period start.");
            if (Math.Abs(HistoryPeriodRules.CloseEndBeforeNextStart(82.0, 120.0, 83.0) - 83.0) > 0.001)
                throw new Exception("Overlapping city/reign periods must be capped at the next period start.");
            if (Math.Abs(HistoryPeriodRules.CloseEndBeforeNextStart(82.0, 82.5, 83.0) - 82.5) > 0.001)
                throw new Exception("Already closed city/reign periods should keep their recorded end.");
            if (HistoryPeriodRules.CloseEndBeforeNextStart(83.0, -1.0, 82.0) != -1.0)
                throw new Exception("A next start before the current start must not create an inverted period.");
            if (!HistoryPeriodRules.ShouldKeepPeriod(121.0, 121.0, pEventCount: 1))
                throw new Exception("A same-day city history period with events should be kept.");
            if (HistoryPeriodRules.ShouldKeepPeriod(121.0, 2.0, pEventCount: 0))
                throw new Exception("Empty inverted city history periods should be dropped.");
        }

        private static void ExpectKingdomRenameRules()
        {
            if (!KingdomRenameRules.ShouldRecordRename("Tang", "Zhou", pTrack: true,
                    pArchivable: true, pSuppressed: false))
                throw new Exception("Manual tracked kingdom rename should be recorded.");
            if (KingdomRenameRules.ShouldRecordRename("Tang", "Tang", pTrack: true,
                    pArchivable: true, pSuppressed: false))
                throw new Exception("Unchanged kingdom names must not be recorded.");
            if (KingdomRenameRules.ShouldRecordRename("Tang", "Zhou", pTrack: false,
                    pArchivable: true, pSuppressed: false))
                throw new Exception("Untracked/system kingdom rename must not be recorded.");
            if (KingdomRenameRules.ShouldRecordRename("Tang", "Zhou", pTrack: true,
                    pArchivable: true, pSuppressed: true))
                throw new Exception("Suppressed kingdom rename must not be recorded.");
        }

        private static void ExpectAncestryOriginRules()
        {
            if (AncestryOriginRules.SelectLineageCity("Live", "Branch", "Root") != "Root")
                throw new Exception("Noble ancestry should prefer root lineage origin city.");
            if (AncestryOriginRules.SelectLineageCity("Live", "Branch", "") != "Branch")
                throw new Exception("Noble ancestry should fall back to branch origin city.");
            if (AncestryOriginRules.SelectLineageCity("Live", "", "") != "Live")
                throw new Exception("Noble ancestry should fall back to live/archive city.");
        }

        private static void ExpectSlaveKingAbdicationRules()
        {
            if (!SlaveKingAbdicationRules.ShouldForceAbdicate(
                    pIsKing: true, pWasSlave: false, pIsSlaveNow: true, pHasKingdom: true))
                throw new Exception("A newly enslaved king must abdicate.");
            if (SlaveKingAbdicationRules.ShouldForceAbdicate(
                    pIsKing: false, pWasSlave: false, pIsSlaveNow: true, pHasKingdom: true))
                throw new Exception("Only kings should use forced abdication.");
            if (!SlaveKingAbdicationRules.ShouldForceAbdicate(
                    pIsKing: true, pWasSlave: true, pIsSlaveNow: true, pHasKingdom: true))
                throw new Exception("An existing slave king must still abdicate while he holds the throne.");
            if (!SlaveKingAbdicationRules.ShouldConvertSlaveOnlyKingdomToRebel(
                    pIsKing: true, pIsSlaveNow: true, pHasKingdom: true,
                    pLivingCandidates: 3, pFreeCandidates: 0))
                throw new Exception("A kingdom with only slave living king candidates should convert to a rebel government.");
            if (SlaveKingAbdicationRules.ShouldConvertSlaveOnlyKingdomToRebel(
                    pIsKing: true, pIsSlaveNow: true, pHasKingdom: true,
                    pLivingCandidates: 3, pFreeCandidates: 1))
                throw new Exception("A slave king should abdicate normally when a free living candidate exists.");
            if (SlaveKingAbdicationRules.ShouldConvertSlaveOnlyKingdomToRebel(
                    pIsKing: false, pIsSlaveNow: true, pHasKingdom: true,
                    pLivingCandidates: 3, pFreeCandidates: 0))
                throw new Exception("Only a current slave king should trigger slave-only rebel conversion.");
        }
    }
}
