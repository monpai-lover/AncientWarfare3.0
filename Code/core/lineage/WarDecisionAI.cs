using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class WarDecisionAI
    {
        private const string LAST_CHECK_YEAR = "aw_war_ai_last_check_year";
        private const string LAST_ACTION_YEAR = "aw_war_ai_last_action_year";
        private const string CLAIM_TARGET_ID = "aw_war_ai_claim_target_id";
        private const int CHECK_INTERVAL = 6;
        private const int ACTION_COOLDOWN = 18;
        private static readonly Random Rng = new Random();

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!CanRunFor(pKingdom)) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LAST_CHECK_YEAR, out int lastCheck, -99999);
            if (year - lastCheck < CHECK_INTERVAL) return;
            pKingdom.data.set(LAST_CHECK_YEAR, year);

            if (TryDeclarePreparedWar(pKingdom))
            {
                pKingdom.data.set(LAST_ACTION_YEAR, year);
                return;
            }

            pKingdom.data.get(LAST_ACTION_YEAR, out int lastAction, -99999);
            if (year - lastAction < ACTION_COOLDOWN) return;
            if (!Chance(0.28f)) return;

            Kingdom target = PickNormalWarTarget(pKingdom);
            if (target?.data == null) return;
            City targetCity = WarTerritoryService.FindFirstFabricationTargetCity(pKingdom, target);
            if (!KingdomPolicyService.StartFabricationDecision(pKingdom, target, targetCity,
                    WarTerritoryService.PROJECT_WEAK_CLAIM))
                return;
            pKingdom.data.set(CLAIM_TARGET_ID, target.id);
            pKingdom.data.set(LAST_ACTION_YEAR, year);
        }

        public static bool TryQueueFromVanillaWarPlot(Actor pActor, Kingdom pPreferredTarget)
        {
            Kingdom kingdom = pActor?.kingdom;
            if (!CanRunForPlotRedirect(kingdom)) return false;

            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(kingdom) &&
                !KingdomPolicyService.SetPolicyEnabled(kingdom, true))
                return false;

            if (!string.IsNullOrEmpty(KingdomPolicyService.GetCurrent(kingdom, PolicyNodeKind.Decision)))
                return true;

            Kingdom target = IsUsableRedirectTarget(kingdom, pPreferredTarget)
                ? pPreferredTarget
                : PickNormalWarTarget(kingdom);
            if (target?.data == null) return false;

            WarTerritoryService.WarTargetOption option = PickBestImmediateOption(kingdom, target);
            if (option != null)
            {
                bool queued = KingdomPolicyService.StartWarDecision(kingdom, option);
                if (queued) kingdom.data.set(CLAIM_TARGET_ID, target.id);
                return queued;
            }

            City targetCity = WarTerritoryService.FindFirstFabricationTargetCity(kingdom, target);
            if (targetCity?.data == null) return false;
            bool started = KingdomPolicyService.StartFabricationDecision(kingdom, target, targetCity,
                WarTerritoryService.PROJECT_WEAK_CLAIM);
            if (started) kingdom.data.set(CLAIM_TARGET_ID, target.id);
            return started;
        }

        private static bool TryDeclarePreparedWar(Kingdom pKingdom)
        {
            pKingdom.data.get(CLAIM_TARGET_ID, out long targetId, -1L);
            Kingdom target = FindKingdom(targetId);
            if (target?.data == null)
            {
                target = WarTerritoryService.FindBestClaimWarTarget(pKingdom);
                if (target?.data != null) pKingdom.data.set(CLAIM_TARGET_ID, target.id);
            }
            if (target?.data == null || target.isRekt() || pKingdom.hasEnemies() || target.hasEnemies())
            {
                if (targetId >= 0) pKingdom.data.set(CLAIM_TARGET_ID, -1L);
                return false;
            }
            if (WarTerritoryService.IsVassalDecisionOnlyTarget(pKingdom, target))
            {
                pKingdom.data.set(CLAIM_TARGET_ID, -1L);
                return false;
            }

            if (!WarDecisionService.HasValidCasusBelli(pKingdom, target, WarDecisionService.WAR_NORMAL))
            {
                if (!WarTerritoryService.HasActiveProjectAgainst(pKingdom, target))
                    pKingdom.data.set(CLAIM_TARGET_ID, -1L);
                return false;
            }

            if (!StillWantsWar(pKingdom, target)) return false;
            if (!string.IsNullOrEmpty(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision)))
                return false;

            WarTerritoryService.WarTargetOption option = PickBestImmediateOption(pKingdom, target);
            bool started = option != null && KingdomPolicyService.StartWarDecision(pKingdom, option);
            if (started) pKingdom.data.set(CLAIM_TARGET_ID, -1L);
            return started;
        }

        private static Kingdom PickNormalWarTarget(Kingdom pKingdom)
        {
            float own = Math.Max(1f, VassalService.GetPowerScore(pKingdom, pIncludeVassals: true));
            Kingdom best = null;
            float bestScore = 0f;

            foreach (Kingdom other in CandidateKingdoms(pKingdom))
            {
                if (other == pKingdom || other.hasEnemies()) continue;
                if (VassalService.GetRootSuzerain(other) == VassalService.GetRootSuzerain(pKingdom)) continue;
                if (WarTerritoryService.IsVassalDecisionOnlyTarget(pKingdom, other)) continue;
                if (WarTerritoryService.FindFirstFabricationTargetCity(pKingdom, other)?.data == null) continue;
                if (!AreNeighbors(pKingdom, other) && Opinion(pKingdom, other) > -65) continue;

                float target = Math.Max(1f, VassalService.GetPowerScore(other, pIncludeVassals: true));
                if (own < target * 1.35f) continue;

                float score = 120f;
                if (AreNeighbors(pKingdom, other)) score += 90f;
                score += Math.Max(0, -Opinion(pKingdom, other));
                score += Math.Min(160f, target);
                if (MandateService.GetCurrentMandateKingdom() == other) score -= 140f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = other;
            }

            return best;
        }

        private static WarTerritoryService.WarTargetOption PickBestImmediateOption(Kingdom pKingdom, Kingdom pTarget)
        {
            WarTerritoryService.WarTargetOption best = null;
            foreach (WarTerritoryService.WarTargetOption option in WarTerritoryService.BuildTargetOptions(pKingdom, pTarget))
            {
                if (option == null || option.goal_type == WarTerritoryService.GOAL_NO_CB) continue;
                if (best == null || option.score > best.score) best = option;
            }
            return best;
        }

        private static bool IsUsableRedirectTarget(Kingdom pKingdom, Kingdom pTarget)
        {
            if (pKingdom?.data == null || pTarget?.data == null || pTarget == pKingdom ||
                pTarget.isRekt() || !pTarget.isCiv() || pTarget.isNeutral())
                return false;

            Kingdom suzerain = VassalService.GetSuzerain(pKingdom);
            if (suzerain == pTarget) return true;
            return !WarTerritoryService.IsVassalDecisionOnlyTarget(pKingdom, pTarget);
        }

        private static bool StillWantsWar(Kingdom pKingdom, Kingdom pTarget)
        {
            if (pKingdom?.data == null || pTarget?.data == null) return false;
            float own = VassalService.GetPowerScore(pKingdom, pIncludeVassals: true);
            float target = Math.Max(1f, VassalService.GetPowerScore(pTarget, pIncludeVassals: true));
            return own >= target * 1.15f && (AreNeighbors(pKingdom, pTarget) || Opinion(pKingdom, pTarget) <= -55);
        }

        private static IEnumerable<Kingdom> CandidateKingdoms(Kingdom pKingdom)
        {
            if (World.world?.kingdoms == null) yield break;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom == pKingdom || kingdom.isRekt() ||
                    !kingdom.isCiv() || kingdom.isNeutral()) continue;
                yield return kingdom;
            }
        }

        private static bool AreNeighbors(Kingdom pA, Kingdom pB)
        {
            try
            {
                foreach (City city in pA.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    foreach (Kingdom neighbor in city.neighbours_kingdoms)
                        if (neighbor == pB) return true;
                }
            }
            catch { }
            return false;
        }

        private static Kingdom FindKingdom(long pId)
        {
            if (pId < 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom byId = World.world.kingdoms.get(pId);
                if (byId?.data != null) return byId;
            }
            catch { }
            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && kingdom.id == pId) return kingdom;
            return null;
        }

        private static bool CanRunFor(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return false;
            if (!pKingdom.hasKing() || pKingdom.hasEnemies()) return false;
            if (VassalService.IsVassalKingdom(pKingdom)) return false;
            return KingdomPolicyService.CanUsePolicySystem(pKingdom) || LineageService.IsXiaKingdom(pKingdom);
        }

        private static bool CanRunForPlotRedirect(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return false;
            if (!pKingdom.hasKing() || pKingdom.hasEnemies()) return false;
            return KingdomPolicyService.CanUsePolicySystem(pKingdom) || LineageService.IsXiaKingdom(pKingdom);
        }

        private static int Opinion(Kingdom pMain, Kingdom pTarget)
        {
            try { return World.world.diplomacy.getOpinion(pMain, pTarget).total; }
            catch { return 0; }
        }

        private static bool Chance(float pChance)
        {
            return Rng.NextDouble() < pChance;
        }
    }
}
