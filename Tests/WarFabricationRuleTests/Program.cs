using System;
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

            ExpectClaimMapColor("strong_claim", "strong_claim");
            ExpectClaimMapColor("weak_claim", "weak_claim");
            ExpectClaimMapColor("pending_claim", "pending_claim");
            ExpectClaimMapColor("", "");

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
            ExpectWorldSwitchCacheRules();
            ExpectWarPlotRedirectRules();
            ExpectWarPlotProgressRedirectRules();
            ExpectWarTypeAssetRules();
            ExpectMetaWindowSafetyRules();
            ExpectRestorationSettlementRules();
            ExpectSlaveArmyNameRefreshRule();
            ExpectCityMaintenanceThrottleRules();
            ExpectNonCoreLoyaltyRules();
            ExpectWarTerritoryCacheRules();
            ExpectFamilyTreePortraitFrameRules();
            ExpectFabricateCoreDecisionPriority();
            ExpectMandateSuccessionRules();
            ExpectHeirRecallRules();
            ExpectLineageBranchRules();
            ExpectAncestryDisplayRules();
            ExpectMandateMapMarkerRules();

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

            if (MandateDynastyMapRules.HexForStatus("mandate") == "#44B454" ||
                MandateDynastyMapRules.HexForStatus("vassal") == "#4696D2")
                throw new Exception("Mandate dynasty colors must not reuse low-contrast mandate core colors.");

            if (AWMapModeMetaRules.NormalizeMapColorHex("  #d72f8a ") != "#D72F8A")
                throw new Exception("Map color hex cache keys must be normalized.");

            if (AWMapModeMetaRules.NormalizeMapColorHex("") != "#242424")
                throw new Exception("Empty map colors must normalize to the fallback color.");
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
                    belongsToLegitimateShi: false))
                throw new Exception("Collateral restoration must prefer the legitimate old shi line.");
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

            if (WarPlotRedirectRules.ShouldInterceptActiveNewWarPlot("new_war",
                    pCivilKingdom: true,
                    pCanUseAwDecision: true,
                    pAw3AllowedWarStart: true))
                throw new Exception("AW3 scoped war starts must not intercept their own plot path.");

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

        private static void ExpectMandateMapMarkerRules()
        {
            if (!MandateMapMarkerRules.ShouldUseSpecialIcon("moh_nameplate", pHasSpecialImage: true))
                throw new Exception("Mandate markers must use the stable special-icon slot.");
            if (MandateMapMarkerRules.ShouldReplaceSpeciesIcon("moh_nameplate", pHasSpeciesImage: true))
                throw new Exception("Mandate markers must not replace the kingdom species icon.");
            if (MandateMapMarkerRules.ShouldUseSpecialIcon("", pHasSpecialImage: true))
                throw new Exception("Empty mandate marker paths must leave the original nameplate icon untouched.");
            if (MandateMapMarkerRules.ShouldUseSpecialIcon("moh_nameplate", pHasSpecialImage: false))
                throw new Exception("Mandate marker replacement needs an existing special image target.");
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
            if (FamilyTreePortraitFrameRules.ShouldShowRoleFrame(false, false, false))
                throw new Exception("Common family tree nodes must not show a leader/captain/king frame.");
            if (!FamilyTreePortraitFrameRules.ShouldShowRoleFrame(true, false, false))
                throw new Exception("King family tree nodes must show a role frame.");
            if (!FamilyTreePortraitFrameRules.ShouldShowRoleFrame(false, true, false))
                throw new Exception("City leader family tree nodes must show a role frame.");
            if (!FamilyTreePortraitFrameRules.ShouldShowRoleFrame(false, false, true))
                throw new Exception("Army captain family tree nodes must show a role frame.");
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
            if (label != "<color=#88ff88>\u8d8a</color> \u8def \u6536\u590d\u6838\u5fc3")
                throw new Exception($"Unexpected war target row label: '{label}'.");

            string stats = WarDecisionTargetTextRules.BuildStatsLine(2, 3, 1, 4,
                "<color=#ffaa00>\u4f1a\u7a3d</color>");
            if (stats != "\u68382 \u5f3a3 \u5f311 \u90204 \u8def <color=#ffaa00>\u4f1a\u7a3d</color>")
                throw new Exception($"Unexpected war target stats line: '{stats}'.");
        }
    }
}
