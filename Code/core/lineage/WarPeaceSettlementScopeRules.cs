using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum WarPeaceSettlementScopeKind
    {
        Coalition = 0,
        SeparateParticipant = 1
    }

    public enum WarParticipantEntrySourceKind
    {
        Unknown = 0,
        MainBelligerent = 1,
        AllianceCall = 2,
        FormalVassalObligation = 3,
        IndependentDeclaration = 4,
        ScriptedJoin = 5,
        SeparatePeaceExit = 6
    }

    public readonly struct WarPeaceNegotiationAuthorityFacts
    {
        public WarPeaceNegotiationAuthorityFacts(bool sameWar,
            bool opposingSides, bool requesterIsParticipant,
            bool responderIsParticipant, bool requesterIsWarLeader,
            bool responderIsWarLeader,
            WarParticipantRoleKind exitRootRole)
        {
            SameWar = sameWar;
            OpposingSides = opposingSides;
            RequesterIsParticipant = requesterIsParticipant;
            ResponderIsParticipant = responderIsParticipant;
            RequesterIsWarLeader = requesterIsWarLeader;
            ResponderIsWarLeader = responderIsWarLeader;
            ExitRootRole = exitRootRole;
        }

        public bool SameWar { get; }
        public bool OpposingSides { get; }
        public bool RequesterIsParticipant { get; }
        public bool ResponderIsParticipant { get; }
        public bool RequesterIsWarLeader { get; }
        public bool ResponderIsWarLeader { get; }
        public WarParticipantRoleKind ExitRootRole { get; }
    }

    public readonly struct WarPeaceExitParticipantFacts
    {
        public WarPeaceExitParticipantFacts(bool activeParticipant,
            bool sameSide, bool parentIncluded, bool currentFormalVassal,
            bool suzerainMatchesParent, bool tributary,
            WarParticipantEntrySourceKind entrySource,
            bool hasIndependentEntrySource)
        {
            ActiveParticipant = activeParticipant;
            SameSide = sameSide;
            ParentIncluded = parentIncluded;
            CurrentFormalVassal = currentFormalVassal;
            SuzerainMatchesParent = suzerainMatchesParent;
            Tributary = tributary;
            EntrySource = entrySource;
            HasIndependentEntrySource = hasIndependentEntrySource;
        }

        public bool ActiveParticipant { get; }
        public bool SameSide { get; }
        public bool ParentIncluded { get; }
        public bool CurrentFormalVassal { get; }
        public bool SuzerainMatchesParent { get; }
        public bool Tributary { get; }
        public WarParticipantEntrySourceKind EntrySource { get; }
        public bool HasIndependentEntrySource { get; }
    }

    public sealed class WarPeaceSeparateExitPlan
    {
        public WarPeaceSeparateExitPlan(IReadOnlyList<long> pExitKingdomIds,
            IReadOnlyList<long> pOpposingKingdomIds, string pExitSide)
        {
            ExitKingdomIds = pExitKingdomIds ?? Array.Empty<long>();
            OpposingKingdomIds = pOpposingKingdomIds ?? Array.Empty<long>();
            ExitSide = pExitSide ?? "";
        }

        public IReadOnlyList<long> ExitKingdomIds { get; }
        public IReadOnlyList<long> OpposingKingdomIds { get; }
        public string ExitSide { get; }
    }

    public readonly struct WarPeaceExitPlanParticipantFacts
    {
        public WarPeaceExitPlanParticipantFacts(long pKingdomId,
            string pSide, bool pMainBelligerent, bool pIncludedInExitGroup)
        {
            KingdomId = pKingdomId;
            Side = pSide ?? "";
            MainBelligerent = pMainBelligerent;
            IncludedInExitGroup = pIncludedInExitGroup;
        }

        public long KingdomId { get; }
        public string Side { get; }
        public bool MainBelligerent { get; }
        public bool IncludedInExitGroup { get; }
    }

    public static class WarPeaceSettlementScopeRules
    {
        public const string CoalitionId = "coalition";
        public const string SeparateParticipantId = "separate_participant";

        public static bool CanNegotiate(WarPeaceSettlementScopeKind pScope,
            WarPeaceNegotiationAuthorityFacts pFacts)
        {
            if (!pFacts.SameWar || !pFacts.OpposingSides ||
                !pFacts.RequesterIsParticipant ||
                !pFacts.ResponderIsParticipant)
                return false;

            if (pScope == WarPeaceSettlementScopeKind.Coalition)
                return pFacts.RequesterIsWarLeader &&
                       pFacts.ResponderIsWarLeader;

            bool exactlyOneLeader = pFacts.RequesterIsWarLeader !=
                                    pFacts.ResponderIsWarLeader;
            bool eligibleExitRoot = pFacts.ExitRootRole ==
                                    WarParticipantRoleKind.Independent ||
                                    pFacts.ExitRootRole ==
                                    WarParticipantRoleKind.Tributary;
            return exactlyOneLeader && eligibleExitRoot;
        }

        public static bool ShouldIncludeVassal(
            WarPeaceExitParticipantFacts pFacts)
        {
            return pFacts.ActiveParticipant && pFacts.SameSide &&
                   pFacts.ParentIncluded && pFacts.CurrentFormalVassal &&
                   pFacts.SuzerainMatchesParent && !pFacts.Tributary &&
                   !pFacts.HasIndependentEntrySource &&
                   pFacts.EntrySource ==
                   WarParticipantEntrySourceKind.FormalVassalObligation;
        }

        public static string ScopeId(WarPeaceSettlementScopeKind pScope)
        {
            return pScope == WarPeaceSettlementScopeKind.SeparateParticipant
                ? SeparateParticipantId
                : CoalitionId;
        }

        public static WarPeaceSettlementScopeKind ParseScope(string pScope)
        {
            return string.Equals(pScope, SeparateParticipantId,
                StringComparison.Ordinal)
                ? WarPeaceSettlementScopeKind.SeparateParticipant
                : WarPeaceSettlementScopeKind.Coalition;
        }

        public static bool TryBuildSeparateExitPlan(long pExitRootKingdomId,
            IReadOnlyList<WarPeaceExitPlanParticipantFacts> pParticipants,
            out WarPeaceSeparateExitPlan pPlan, out string pReason)
        {
            pPlan = null;
            pReason = "separate_peace_exit_group_invalid";
            if (pExitRootKingdomId < 0 || pParticipants == null ||
                pParticipants.Count == 0) return false;

            string rootSide = "";
            var seen = new HashSet<long>();
            for (int i = 0; i < pParticipants.Count; i++)
            {
                WarPeaceExitPlanParticipantFacts participant =
                    pParticipants[i];
                if (participant.KingdomId < 0 ||
                    !seen.Add(participant.KingdomId)) return false;
                if (participant.KingdomId != pExitRootKingdomId) continue;
                if (!participant.IncludedInExitGroup ||
                    participant.MainBelligerent ||
                    !IsWarSide(participant.Side)) return false;
                rootSide = participant.Side;
            }
            if (string.IsNullOrEmpty(rootSide)) return false;

            var exits = new List<long>();
            var opponents = new List<long>();
            for (int i = 0; i < pParticipants.Count; i++)
            {
                WarPeaceExitPlanParticipantFacts participant =
                    pParticipants[i];
                if (!IsWarSide(participant.Side)) return false;
                bool sameSide = string.Equals(participant.Side,
                    rootSide, StringComparison.Ordinal);
                if (participant.IncludedInExitGroup)
                {
                    if (!sameSide || participant.MainBelligerent) return false;
                    exits.Add(participant.KingdomId);
                }
                else if (!sameSide)
                    opponents.Add(participant.KingdomId);
            }
            exits.Sort();
            opponents.Sort();
            pPlan = new WarPeaceSeparateExitPlan(exits, opponents,
                rootSide);
            pReason = "";
            return true;
        }

        private static bool IsWarSide(string pSide)
        {
            return string.Equals(pSide, "attacker",
                       StringComparison.Ordinal) ||
                   string.Equals(pSide, "defender",
                       StringComparison.Ordinal);
        }
    }
}
