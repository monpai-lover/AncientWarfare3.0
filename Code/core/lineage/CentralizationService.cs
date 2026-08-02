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
        public bool can_reform;
        public string block_reason = "";
        public CentralizationEffects effects;
        public bool western_direct_rule;
        public float administration_multiplier = 1f;
        public int noble_opinion;
        public int vassal_opinion;
    }

    internal static class CentralizationService
    {
        public static CentralizationSnapshot ReadSnapshot(Kingdom pKingdom)
        {
            CentralizationSnapshot snapshot = BuildSnapshot(pKingdom);
            KingdomPolicyEffects policyEffects =
                KingdomPolicyEffectService.Read(pKingdom);
            snapshot.western_direct_rule =
                policyEffects.CentralizationUnlocked;
            snapshot.administration_multiplier =
                policyEffects.AdministrationMultiplier;
            snapshot.noble_opinion = policyEffects.NobleOpinion;
            snapshot.vassal_opinion = policyEffects.VassalOpinion;
            snapshot.can_reform = ValidateReform(pKingdom, snapshot,
                snapshot.next_target_level, out string reason);
            snapshot.block_reason = reason;
            return snapshot;
        }

        public static bool CanStartMandateReform(Kingdom pKingdom,
            int pTargetLevel, out string pReason)
        {
            return ValidateReform(pKingdom, BuildSnapshot(pKingdom),
                pTargetLevel, out pReason);
        }

        public static bool TryCompleteMandateReform(Kingdom pKingdom,
            int pTargetLevel, out string pReason)
        {
            CentralizationSnapshot snapshot = BuildSnapshot(pKingdom);
            if (!ValidateReform(pKingdom, snapshot, pTargetLevel, out pReason))
                return false;

            int readyYear = snapshot.current_year +
                            CentralizationRules.ReformCooldownYears(pTargetLevel);
            pKingdom.data.set(LineageKeys.CENTRALIZATION_LEVEL, pTargetLevel);
            pKingdom.data.set(LineageKeys.CENTRALIZATION_REFORM_READY_YEAR,
                readyYear);
            FeudatoryService.ApplyAutonomyCap(pKingdom,
                CentralizationRules.Effects(pTargetLevel).AutonomyCap);
            HistoryWriter.RecordKingdom(pKingdom, "centralization_reformed",
                HistoryLocalizationRules.Text("aw_hist_centralization_reformed_text") +
                pTargetLevel);
            pReason = "";
            return true;
        }

        public static void OnPhaseChanged(MandatePhase pPrevious,
            MandatePhase pNext, int pPhaseSinceYear)
        {
            if (pNext != MandatePhase.Chaos) return;
            Kingdom kingdom = MandateService.GetCurrentMandateKingdom();
            if (!IsCurrentMandateKingdom(kingdom)) return;
            kingdom.data.get(LineageKeys.CENTRALIZATION_LAST_CHAOS_EPOCH,
                out int lastEpoch, int.MinValue);
            if (!CentralizationRules.ShouldApplyChaosDowngrade(pNext,
                    pPhaseSinceYear, lastEpoch)) return;

            kingdom.data.get(LineageKeys.CENTRALIZATION_LEVEL,
                out int storedLevel, 0);
            int oldLevel = CentralizationRules.NormalizeLevel(storedLevel);
            int nextLevel = Math.Max(0, oldLevel - 1);
            kingdom.data.set(LineageKeys.CENTRALIZATION_LAST_CHAOS_EPOCH,
                pPhaseSinceYear);
            kingdom.data.set(LineageKeys.CENTRALIZATION_LEVEL, nextLevel);
            kingdom.data.set(LineageKeys.CENTRALIZATION_REFORM_READY_YEAR,
                pPhaseSinceYear);
            if (nextLevel == oldLevel) return;

            HistoryWriter.RecordKingdom(kingdom,
                "centralization_chaos_downgrade",
                HistoryLocalizationRules.Text(
                    "aw_hist_centralization_chaos_downgrade_text") + nextLevel);
        }

        private static CentralizationSnapshot BuildSnapshot(Kingdom pKingdom)
        {
            var snapshot = new CentralizationSnapshot
            {
                current_year = SafeCurrentYear(),
                phase = MandatePhaseService.CurrentPhase
            };
            snapshot.phase_cap = MandatePhaseRules.MaxCentralization(snapshot.phase);
            snapshot.supported = IsCurrentMandateKingdom(pKingdom);
            if (pKingdom?.data == null)
            {
                snapshot.block_reason = "invalid_kingdom";
                snapshot.effects = CentralizationRules.Effects(0);
                return snapshot;
            }
            if (!snapshot.supported)
            {
                snapshot.phase_cap = 0;
                snapshot.effects = CentralizationRules.Effects(0);
                return snapshot;
            }

            pKingdom.data.get(LineageKeys.CENTRALIZATION_LEVEL,
                out int storedLevel, 0);
            pKingdom.data.get(LineageKeys.CENTRALIZATION_REFORM_READY_YEAR,
                out snapshot.reform_ready_year, 0);
            snapshot.nominal_level = CentralizationRules.NormalizeLevel(storedLevel);
            snapshot.effective_level = CentralizationRules.EffectiveLevel(
                snapshot.nominal_level, snapshot.phase, snapshot.supported);
            snapshot.next_target_level = snapshot.nominal_level <
                                         CentralizationRules.MaximumLevel
                ? snapshot.nominal_level + 1
                : snapshot.nominal_level;
            snapshot.reform_cost = CentralizationRules.ReformCost(
                snapshot.next_target_level);
            snapshot.required_tech_id = CentralizationRules.RequiredTechId(
                snapshot.next_target_level);
            snapshot.effects = CentralizationRules.Effects(snapshot.effective_level);
            return snapshot;
        }

        private static bool ValidateReform(Kingdom pKingdom,
            CentralizationSnapshot pSnapshot, int pTargetLevel, out string pReason)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
            {
                pReason = "invalid_kingdom";
                return false;
            }
            if (!pSnapshot.supported)
            {
                pReason = "not_mandate";
                return false;
            }
            if (pTargetLevel != pSnapshot.next_target_level ||
                pTargetLevel < 1 || pTargetLevel > CentralizationRules.MaximumLevel)
            {
                pReason = "invalid_target";
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
            pReason = "";
            return true;
        }

        private static bool IsCurrentMandateKingdom(Kingdom pKingdom)
        {
            bool valid = pKingdom?.data != null && !pKingdom.isRekt() &&
                         pKingdom.isCiv() && !pKingdom.isNeutral();
            Kingdom mandate = valid ? MandateService.GetCurrentMandateKingdom() : null;
            return CentralizationRules.CanParticipate(valid,
                mandate?.id == pKingdom?.id);
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
