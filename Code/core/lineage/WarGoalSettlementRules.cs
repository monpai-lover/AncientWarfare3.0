using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class WarGoalTypeIds
    {
        public const string TakeMandate = "take_mandate";
        public const string MandateConquest = "mandate_conquest";
        public const string TakeCoreCity = "take_core_city";
        public const string PressClaimCity = "press_claim_city";
        public const string TakeDeJureRegion = "take_de_jure_region";
        public const string ForceVassal = "force_vassal";
        public const string ForceTributary = "force_tributary";
        public const string Independence = "independence";
        public const string RestoreKingdom = "restore_kingdom";
        public const string ReunifySuccession = "reunify_succession";
        public const string NoCb = "no_cb_punitive";
        public const string LegacyNoCb = "no_cb";
        public const string ZhuluAnnexation = "zhulu_annexation";
        public const string BanditSuppression = "bandit_suppression";
    }

    public readonly struct WarGoalIdentity : IEquatable<WarGoalIdentity>
    {
        public WarGoalIdentity(string pGoalType, long pTargetCityId,
            long pTargetKingdomId, long pSourceClaimId,
            long pSourceCoreId, long pSourceProjectId,
            long pClaimantActorId, long pSourceDeJureRegionId = -1L)
        {
            GoalType = pGoalType ?? "";
            TargetCityId = pTargetCityId;
            TargetKingdomId = pTargetKingdomId;
            SourceClaimId = pSourceClaimId;
            SourceCoreId = pSourceCoreId;
            SourceProjectId = pSourceProjectId;
            ClaimantActorId = pClaimantActorId;
            SourceDeJureRegionId = pSourceDeJureRegionId;
        }

        public string GoalType { get; }
        public long TargetCityId { get; }
        public long TargetKingdomId { get; }
        public long SourceClaimId { get; }
        public long SourceCoreId { get; }
        public long SourceProjectId { get; }
        public long ClaimantActorId { get; }
        public long SourceDeJureRegionId { get; }

        public bool Equals(WarGoalIdentity other)
        {
            return string.Equals(GoalType ?? "", other.GoalType ?? "",
                       StringComparison.Ordinal) &&
                   TargetCityId == other.TargetCityId &&
                   TargetKingdomId == other.TargetKingdomId &&
                   SourceClaimId == other.SourceClaimId &&
                   SourceCoreId == other.SourceCoreId &&
                   SourceProjectId == other.SourceProjectId &&
                   ClaimantActorId == other.ClaimantActorId &&
                   SourceDeJureRegionId == other.SourceDeJureRegionId;
        }

        public override bool Equals(object obj)
        {
            return obj is WarGoalIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    GoalType ?? "");
                hash = hash * 397 ^ TargetCityId.GetHashCode();
                hash = hash * 397 ^ TargetKingdomId.GetHashCode();
                hash = hash * 397 ^ SourceClaimId.GetHashCode();
                hash = hash * 397 ^ SourceCoreId.GetHashCode();
                hash = hash * 397 ^ SourceProjectId.GetHashCode();
                hash = hash * 397 ^ ClaimantActorId.GetHashCode();
                return hash * 397 ^ SourceDeJureRegionId.GetHashCode();
            }
        }
    }

    public readonly struct WarGoalSettlementFacts
    {
        public WarGoalSettlementFacts(int pAchievedScore,
            long pWarGoalId, int pRequiredScore, bool pGoalCompleted,
            long pRequestedGoalTermWarGoalId)
        {
            AchievedScore = pAchievedScore;
            WarGoalId = pWarGoalId;
            RequiredScore = pRequiredScore;
            GoalCompleted = pGoalCompleted;
            RequestedGoalTermWarGoalId = pRequestedGoalTermWarGoalId;
        }

        public int AchievedScore { get; }
        public long WarGoalId { get; }
        public int RequiredScore { get; }
        public bool GoalCompleted { get; }
        public long RequestedGoalTermWarGoalId { get; }
    }

    public enum WarGoalAutomaticSettlementEffect
    {
        CedeCity,
        ForceVassal,
        ForceTributary,
        TakeMandate,
        RestoreKingdom,
        Independence,
        ReunifySuccession,
        NoCbOutcome,
        ZhuluAnnexation
    }

    public readonly struct WarGoalAutomaticSettlementProfile
    {
        public WarGoalAutomaticSettlementProfile(
            WarGoalAutomaticSettlementEffect pEffect,
            string pCompletionKind, int pRequiredWarScore,
            bool pUsesDynamicCityCost)
        {
            Effect = pEffect;
            CompletionKind = pCompletionKind ?? "";
            RequiredWarScore = pRequiredWarScore;
            UsesDynamicCityCost = pUsesDynamicCityCost;
        }

        public WarGoalAutomaticSettlementEffect Effect { get; }
        public string CompletionKind { get; }
        public int RequiredWarScore { get; }
        public bool UsesDynamicCityCost { get; }
    }

    public static class WarGoalSettlementRules
    {
        public const int MaximumPersistedGoals = 3;
        public const int MinimumRequiredScore = 1;
        public const int MaximumRequiredScore = 100;
        public const int DecisiveVictoryScore = 100;
        public const int TakeMandateRequiredScore = 100;
        public const int RestoreKingdomRequiredScore = 65;
        public const int IndependenceRequiredScore = 50;
        public const int ReunifySuccessionRequiredScore = 80;
        public const int NoCbOutcomeRequiredScore = 25;

        public static bool TryGetAutomaticSettlementProfile(
            string pGoalType,
            out WarGoalAutomaticSettlementProfile pProfile)
        {
            switch (pGoalType ?? "")
            {
                case WarGoalTypeIds.TakeCoreCity:
                case WarGoalTypeIds.PressClaimCity:
                case WarGoalTypeIds.TakeDeJureRegion:
                case WarGoalTypeIds.MandateConquest:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.CedeCity,
                        "city_control", MinimumRequiredScore,
                        pUsesDynamicCityCost: true);
                    return true;
                case WarGoalTypeIds.ForceVassal:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.ForceVassal,
                        "capital_control", 70,
                        pUsesDynamicCityCost: false);
                    return true;
                case WarGoalTypeIds.ForceTributary:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.ForceTributary,
                        "capital_control", 30,
                        pUsesDynamicCityCost: false);
                    return true;
                case WarGoalTypeIds.TakeMandate:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.TakeMandate,
                        "capital_control", TakeMandateRequiredScore,
                        pUsesDynamicCityCost: false);
                    return true;
                case WarGoalTypeIds.RestoreKingdom:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.RestoreKingdom,
                        "city_control", RestoreKingdomRequiredScore,
                        pUsesDynamicCityCost: false);
                    return true;
                case WarGoalTypeIds.Independence:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.Independence,
                        "capital_control", IndependenceRequiredScore,
                        pUsesDynamicCityCost: false);
                    return true;
                case WarGoalTypeIds.ReunifySuccession:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.ReunifySuccession,
                        "capital_control", ReunifySuccessionRequiredScore,
                        pUsesDynamicCityCost: false);
                    return true;
                case WarGoalTypeIds.NoCb:
                case WarGoalTypeIds.LegacyNoCb:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.NoCbOutcome,
                        "capital_control", NoCbOutcomeRequiredScore,
                        pUsesDynamicCityCost: false);
                    return true;
                case WarGoalTypeIds.ZhuluAnnexation:
                    pProfile = new WarGoalAutomaticSettlementProfile(
                        WarGoalAutomaticSettlementEffect.ZhuluAnnexation,
                        "principal_extinction", DecisiveVictoryScore,
                        pUsesDynamicCityCost: false);
                    return true;
                default:
                    pProfile = default;
                    return false;
            }
        }

        public static bool CanPersistGoal(
            IReadOnlyList<WarGoalIdentity> pExistingGoals,
            WarGoalIdentity pCandidate)
        {
            if (pExistingGoals == null) return false;
            int count = pExistingGoals.Count;
            if (count >= MaximumPersistedGoals) return false;
            for (int i = 0; i < count; i++)
                if (pExistingGoals[i].Equals(pCandidate)) return false;
            return true;
        }

        public static int SnapshotRequiredScore(int pActualTermCost)
        {
            return Math.Max(MinimumRequiredScore,
                Math.Min(MaximumRequiredScore, pActualTermCost));
        }

        public static int ResolveCedeCityCost(bool pAutomaticWarGoal,
            int pPersistedGoalScore, int pLiveCityCost)
        {
            return pAutomaticWarGoal
                ? SnapshotRequiredScore(pPersistedGoalScore)
                : Math.Max(MinimumRequiredScore, pLiveCityCost);
        }

        public static bool CanForceSettlement(
            WarGoalSettlementFacts pFacts)
        {
            if (pFacts.AchievedScore >= DecisiveVictoryScore) return true;
            if (pFacts.WarGoalId < 0L || pFacts.RequiredScore <= 0)
                return false;
            if (pFacts.RequestedGoalTermWarGoalId != pFacts.WarGoalId)
                return false;
            return pFacts.GoalCompleted &&
                   pFacts.AchievedScore >= pFacts.RequiredScore;
        }

        public static bool TryValidateForceBundle(int pAchievedScore,
            IReadOnlyList<WarGoalSettlementFacts> pFacts,
            int pExpectedGoalCount, out string pReason)
        {
            pReason = "";
            if (pExpectedGoalCount <= 0 ||
                pExpectedGoalCount > MaximumPersistedGoals ||
                pFacts == null || pFacts.Count != pExpectedGoalCount)
            {
                pReason = "war_goal_bundle_incomplete";
                return false;
            }

            var goalIds = new HashSet<long>();
            int required = 0;
            for (int i = 0; i < pFacts.Count; i++)
            {
                WarGoalSettlementFacts facts = pFacts[i];
                if (facts.WarGoalId < 0 || facts.RequiredScore <= 0 ||
                    !facts.GoalCompleted ||
                    facts.RequestedGoalTermWarGoalId != facts.WarGoalId ||
                    !goalIds.Add(facts.WarGoalId))
                {
                    pReason = "war_goal_bundle_incomplete";
                    return false;
                }
                required = Math.Min(MaximumRequiredScore,
                    required + facts.RequiredScore);
            }

            if (pAchievedScore < required)
            {
                pReason = "war_goal_score_insufficient";
                return false;
            }
            return true;
        }

        public static int[] SelectCompletedAffordableGoalIndices(
            int pAchievedScore,
            IReadOnlyList<WarGoalSettlementFacts> pFacts)
        {
            if (pFacts == null || pFacts.Count == 0 ||
                pAchievedScore <= 0) return Array.Empty<int>();

            int available = Math.Min(MaximumRequiredScore,
                pAchievedScore);
            var selected = new List<int>(Math.Min(pFacts.Count,
                MaximumPersistedGoals));
            var goalIds = new HashSet<long>();
            for (int i = 0; i < pFacts.Count &&
                            selected.Count < MaximumPersistedGoals; i++)
            {
                WarGoalSettlementFacts facts = pFacts[i];
                if (facts.WarGoalId < 0 || facts.RequiredScore <= 0 ||
                    !facts.GoalCompleted ||
                    facts.RequestedGoalTermWarGoalId != facts.WarGoalId ||
                    !goalIds.Add(facts.WarGoalId) ||
                    facts.RequiredScore > available) continue;
                selected.Add(i);
                available -= facts.RequiredScore;
            }
            return selected.ToArray();
        }
    }
}
