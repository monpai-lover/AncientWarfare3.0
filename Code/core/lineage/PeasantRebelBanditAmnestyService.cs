using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.presentation;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditAmnestyService
    {
        internal static bool TryAmnesty(Kingdom pBandit,
            Kingdom pOfferingKingdom, out string pFailureKey)
        {
            pFailureKey = "aw_bandit_amnesty_unavailable";
            bool bandit = PeasantRebelRouteService.IsBandit(pBandit);
            bool stronghold = PeasantRebelBanditStrongholdService.
                HasActiveStronghold(pBandit);
            Kingdom origin = PeasantRebelRouteService.ResolveOrigin(pBandit);
            bool originValid = IsLiveOrigin(origin);
            bool offeringIsOrigin = originValid && origin == pOfferingKingdom;
            bool authoritative = PeasantRebelRouteRules.CanMutateAuthority(
                AW3MultiplayerReplicaScope.IsReplicaSession);
            bool applying = AW3MultiplayerReplicaScope.IsApplying;
            if (!PeasantRebelBanditAmnestyRules.CanAccept(bandit,
                    stronghold, originValid, offeringIsOrigin, authoritative,
                    applying))
            {
                pFailureKey = "aw_bandit_amnesty_" +
                    PeasantRebelBanditAmnestyRules.ResolveFailureKey(
                        bandit, stronghold, originValid, offeringIsOrigin);
                return false;
            }

            if (!EndBanditWars(pBandit))
            {
                pFailureKey = "aw_bandit_amnesty_war_failed";
                return false;
            }

            if (!RestoreOrdinaryGovernment(pBandit))
            {
                pFailureKey = "aw_bandit_amnesty_settlement_failed";
                return false;
            }

            HistoryWriter.RecordKingdom(pBandit,
                KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(pBandit) +
                HistoryLocalizationRules.H("aw_hist_bandit_amnestied"),
                HistoryTarget.Kingdom(origin));
            HistoryWriter.RecordKingdom(origin,
                KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(origin) +
                HistoryLocalizationRules.H("aw_hist_bandit_amnesty_granted") +
                HistoryText.Kingdom(pBandit),
                HistoryTarget.Kingdom(pBandit));
            pFailureKey = string.Empty;
            return true;
        }

        private static bool RestoreOrdinaryGovernment(Kingdom pBandit)
        {
            if (pBandit?.data == null || pBandit.isRekt()) return false;
            if (!PeasantRebelBanditStrongholdService.
                    DestroyForOrdinaryGovernment(pBandit)) return false;

            pBandit.data.set(LineageKeys.MANDATE_REBEL, false);
            pBandit.data.set(LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                -1L);
            pBandit.data.set(LineageKeys.MANDATE_REBEL_BUFF_UNTIL, 0);
            pBandit.data.get(LineageKeys.MANDATE_MAP_MARKER_KIND,
                out string marker, "");
            if (marker == "rebel_claimant")
                pBandit.data.set(LineageKeys.MANDATE_MAP_MARKER_KIND, "");
            MandateService.NormalizeMapMarkerAfterRebelSettlement(pBandit);

            foreach (Actor unit in pBandit.getUnits())
            {
                if (unit?.data == null) continue;
                unit.data.set(LineageKeys.MANDATE_REBEL, false);
                unit.data.set(LineageKeys.MANDATE_REBEL_LEADER, false);
                if (unit.hasTrait("rebel")) unit.removeTrait("rebel");
            }

            pBandit.data.set(LineageKeys.MANDATE_REBEL_ROUTE, "");
            pBandit.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string root, "");
            if (!string.IsNullOrWhiteSpace(root) &&
                !PeasantRebelRouteService.TryApplyRouteName(
                    pBandit, root.Trim())) return false;
            string targetClass = PeasantRebelBanditAmnestyRules.
                ResolveSettlementClass(true);
            if (!KingdomPolicyService.ApplyClassStateDirect(
                    pBandit, targetClass)) return false;

            PeasantRebelRouteService.RemoveRuntime(pBandit);
            RulerAppellationService.RefreshLivingProjection(pBandit);
            KingdomRenameProjectionService.Refresh(pBandit);
            PeasantRebelAppearanceService.OnProjectionChanged(pBandit);
            return true;
        }

        private static bool EndBanditWars(Kingdom pBandit)
        {
            if (pBandit?.data == null || World.world?.wars == null)
                return false;
            var wars = new List<War>();
            try
            {
                foreach (War war in pBandit.getWars())
                    if (war?.data != null && !war.hasEnded()) wars.Add(war);
            }
            catch { return false; }
            try
            {
                for (int i = 0; i < wars.Count; i++)
                    World.world.wars.endWar(wars[i], WarWinner.Peace);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit amnesty could not end wars: " +
                                    error.Message);
                return false;
            }
        }

        private static bool IsLiveOrigin(Kingdom pOrigin)
        {
            try
            {
                return pOrigin?.data != null && !pOrigin.isRekt() &&
                       pOrigin.isAlive() && !pOrigin.isNeutral() &&
                       pOrigin.isCiv();
            }
            catch { return false; }
        }
    }
}
