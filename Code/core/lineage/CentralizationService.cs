using System;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal sealed class CentralizationSnapshot
    {
        public bool supported;
        public int nominal_level;
        public int effective_level;
        public MandatePhase phase;
        public int phase_cap;
        public int next_target_level;
        public int reform_cost;
        public int reform_ready_year;
        public int current_year;
        public string required_tech_id = "";
        public float political_points;
        public bool can_reform;
        public string block_reason = "";
        public CentralizationEffects effects;
    }

    internal static class CentralizationService
    {
        public static CentralizationSnapshot ReadSnapshot(Kingdom pKingdom)
        {
            CentralizationSnapshot snapshot = BuildSnapshot(pKingdom);
            snapshot.can_reform = ValidateReform(pKingdom, snapshot,
                out string reason);
            snapshot.block_reason = reason;
            return snapshot;
        }

        public static bool CanReform(Kingdom pKingdom, out string pReason)
        {
            return ValidateReform(pKingdom, BuildSnapshot(pKingdom), out pReason);
        }

        public static bool TryReform(Kingdom pKingdom, out string pReason)
        {
            CentralizationSnapshot snapshot = BuildSnapshot(pKingdom);
            if (!ValidateReform(pKingdom, snapshot, out pReason)) return false;
            if (!KingdomPolicyService.TrySpendPoliticalPoints(pKingdom,
                    snapshot.reform_cost))
            {
                pReason = "insufficient_points";
                return false;
            }

            int target = snapshot.next_target_level;
            int readyYear = snapshot.current_year +
                            CentralizationRules.ReformCooldownYears(target);
            pKingdom.data.set(LineageKeys.CENTRALIZATION_LEVEL, target);
            pKingdom.data.set(LineageKeys.CENTRALIZATION_REFORM_READY_YEAR,
                readyYear);
            HistoryWriter.RecordKingdom(pKingdom, "centralization_reformed",
                HistoryLocalizationRules.Text("aw_hist_centralization_reformed_text") +
                target);
            pReason = "";
            return true;
        }

        public static void OnPhaseChanged(MandatePhase pPrevious,
            MandatePhase pNext, int pPhaseSinceYear)
        {
            if (pNext != MandatePhase.Chaos || World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsSupportedLivingKingdom(kingdom)) continue;
                kingdom.data.get(LineageKeys.CENTRALIZATION_LAST_CHAOS_EPOCH,
                    out int lastEpoch, int.MinValue);
                if (!CentralizationRules.ShouldApplyChaosDowngrade(pNext,
                        pPhaseSinceYear, lastEpoch)) continue;

                kingdom.data.get(LineageKeys.CENTRALIZATION_LEVEL,
                    out int storedLevel, 0);
                int oldLevel = CentralizationRules.NormalizeLevel(storedLevel);
                int nextLevel = Math.Max(0, oldLevel - 1);
                kingdom.data.set(LineageKeys.CENTRALIZATION_LAST_CHAOS_EPOCH,
                    pPhaseSinceYear);
                kingdom.data.set(LineageKeys.CENTRALIZATION_LEVEL, nextLevel);
                kingdom.data.set(LineageKeys.CENTRALIZATION_REFORM_READY_YEAR,
                    pPhaseSinceYear);
                if (nextLevel == oldLevel) continue;

                HistoryWriter.RecordKingdom(kingdom,
                    "centralization_chaos_downgrade",
                    HistoryLocalizationRules.Text(
                        "aw_hist_centralization_chaos_downgrade_text") + nextLevel);
            }
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!IsSupportedLivingKingdom(pKingdom)) return;
            pKingdom.data.get(LineageKeys.CENTRALIZATION_LEVEL,
                out int storedLevel, 0);
            int normalized = CentralizationRules.NormalizeLevel(storedLevel);
            if (normalized != storedLevel)
                pKingdom.data.set(LineageKeys.CENTRALIZATION_LEVEL, normalized);
            if (!KingdomPolicyService.IsPolicyAIEnabled(pKingdom)) return;
            CentralizationSnapshot snapshot = BuildSnapshot(pKingdom);
            bool baseAllowed = ValidateReform(pKingdom, snapshot, out _);
            CityEconomyService.TryGetLatestCachedForeignLandBorder(
                pKingdom, out bool foreignLandBorder);
            int score = CentralizationRules.AiScore(
                VassalService.GetDirectVassalCount(pKingdom), foreignLandBorder, snapshot.phase);
            int roll = CentralizationRules.AiPercentage(
                pKingdom.id, snapshot.current_year, snapshot.next_target_level);
            if (!CentralizationRules.CanAiReform(baseAllowed, snapshot.political_points,
                    snapshot.reform_cost, roll, score)) return;
            TryReform(pKingdom, out _);
        }

        private static CentralizationSnapshot BuildSnapshot(Kingdom pKingdom)
        {
            var snapshot = new CentralizationSnapshot
            {
                current_year = SafeCurrentYear(),
                phase = MandatePhaseService.CurrentPhase
            };
            snapshot.phase_cap = MandatePhaseRules.MaxCentralization(snapshot.phase);
            snapshot.supported = IsSupportedLivingKingdom(pKingdom);
            if (pKingdom?.data == null)
            {
                snapshot.block_reason = "invalid_kingdom";
                snapshot.effects = CentralizationRules.Effects(0);
                return snapshot;
            }

            pKingdom.data.get(LineageKeys.CENTRALIZATION_LEVEL,
                out int storedLevel, 0);
            pKingdom.data.get(LineageKeys.CENTRALIZATION_REFORM_READY_YEAR,
                out snapshot.reform_ready_year, 0);
            snapshot.nominal_level = CentralizationRules.NormalizeLevel(storedLevel);
            snapshot.effective_level = CentralizationRules.EffectiveLevel(
                snapshot.nominal_level, snapshot.phase);
            snapshot.next_target_level = snapshot.nominal_level <
                                         CentralizationRules.MaximumLevel
                ? snapshot.nominal_level + 1
                : snapshot.nominal_level;
            snapshot.reform_cost = CentralizationRules.ReformCost(
                snapshot.next_target_level);
            snapshot.required_tech_id = CentralizationRules.RequiredTechId(
                snapshot.next_target_level);
            snapshot.political_points = KingdomPolicyService.GetPoliticalPoints(pKingdom);
            snapshot.effects = CentralizationRules.Effects(snapshot.effective_level);
            return snapshot;
        }

        private static bool ValidateReform(Kingdom pKingdom,
            CentralizationSnapshot pSnapshot, out string pReason)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
            {
                pReason = "invalid_kingdom";
                return false;
            }
            if (!pSnapshot.supported)
            {
                pReason = "unsupported_kingdom";
                return false;
            }
            if (pSnapshot.nominal_level >= CentralizationRules.MaximumLevel)
            {
                pReason = "maximum_level";
                return false;
            }
            if (pSnapshot.next_target_level > pSnapshot.phase_cap)
            {
                pReason = "phase_cap";
                return false;
            }
            if (IsAtWar(pKingdom))
            {
                pReason = "at_war";
                return false;
            }
            if (pSnapshot.current_year < pSnapshot.reform_ready_year)
            {
                pReason = "cooldown";
                return false;
            }
            if (!string.IsNullOrEmpty(pSnapshot.required_tech_id) &&
                !KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Tech,
                    pSnapshot.required_tech_id))
            {
                pReason = "requires_tech";
                return false;
            }
            if (pSnapshot.political_points + 0.001f < pSnapshot.reform_cost)
            {
                pReason = "insufficient_points";
                return false;
            }

            pReason = "";
            return true;
        }

        private static bool IsSupportedLivingKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral() &&
                   XiaizationService.CanUsePolicySystem(pKingdom);
        }

        private static bool IsAtWar(Kingdom pKingdom)
        {
            try
            {
                if (pKingdom.hasEnemies()) return true;
                foreach (War _ in pKingdom.getWars()) return true;
            }
            catch { }
            return false;
        }

        private static int SafeCurrentYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }
    }
}
