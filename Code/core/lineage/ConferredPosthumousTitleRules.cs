using System;

namespace AncientWarfare3.core.lineage
{
    [Flags]
    public enum ConferredPosthumousRole
    {
        None = 0,
        General = 1,
        Official = 2,
        Royal = 4,
        FormerRuler = 8
    }

    public enum ConferredPosthumousResult
    {
        Success = 0,
        InvalidKingdom = 1,
        MissingContext = 2,
        MissingArchive = 3,
        TargetLiving = 4,
        NoHistoricalRelationship = 5,
        AlreadyTitled = 6,
        Cooldown = 7,
        NoTitleAvailable = 8,
        StalePreview = 9,
        PersistenceFailed = 10
    }

    public enum ConferredPosthumousSource
    {
        Player = 0,
        Ai = 1
    }

    public readonly struct ConferredPosthumousCandidateScore
    {
        public readonly long ActorId;
        public readonly int RoleWeight;
        public readonly int NobleRank;
        public readonly int OfficeRank;
        public readonly int TenureYears;
        public readonly int Merit;
        public readonly int MilitaryMerit;

        public ConferredPosthumousCandidateScore(long pActorId,
            int pRoleWeight, int pNobleRank, int pOfficeRank,
            int pTenureYears, int pMerit, int pMilitaryMerit)
        {
            ActorId = pActorId;
            RoleWeight = pRoleWeight;
            NobleRank = pNobleRank;
            OfficeRank = pOfficeRank;
            TenureYears = pTenureYears;
            Merit = pMerit;
            MilitaryMerit = pMilitaryMerit;
        }
    }

    public static class ConferredPosthumousTitleRules
    {
        public const int MaximumCandidates = 96;
        public const int MaximumFullEvaluations = 8;
        public const int CooldownYears = 5;
        public const int AiRetryIntervalYears = 4;

        public static int CooldownRemaining(int pCurrentYear, int pLastYear)
        {
            if (pLastYear < 0) return 0;
            return Math.Max(0, CooldownYears -
                               Math.Max(0, pCurrentYear - pLastYear));
        }

        public static bool IsEligibleRole(ConferredPosthumousRole pRole)
        {
            const ConferredPosthumousRole eligible =
                ConferredPosthumousRole.FormerRuler |
                ConferredPosthumousRole.Royal |
                ConferredPosthumousRole.Official |
                ConferredPosthumousRole.General;
            return (pRole & eligible) != 0;
        }

        public static bool CanPreview(bool pDead, bool pHasKingdomContext,
            bool pHasHistoricalRole, bool pAlreadyTitled)
        {
            return pDead && pHasKingdomContext && pHasHistoricalRole &&
                   !pAlreadyTitled;
        }

        public static string ComposeDisplayTitle(string pActorName,
            string pNobleTitle, string pPosthumousTitle)
        {
            string actor = Normalize(pActorName);
            string noble = Normalize(pNobleTitle);
            string posthumous = Normalize(pPosthumousTitle);
            if (string.IsNullOrEmpty(posthumous)) return noble.Length > 0
                ? noble
                : actor;
            return noble.Length > 0
                ? noble + posthumous
                : actor + "，谥" + posthumous;
        }

        public static int CompareCandidate(
            ConferredPosthumousCandidateScore pLeft,
            ConferredPosthumousCandidateScore pRight)
        {
            int result = pRight.RoleWeight.CompareTo(pLeft.RoleWeight);
            if (result != 0) return result;
            result = pRight.NobleRank.CompareTo(pLeft.NobleRank);
            if (result != 0) return result;
            result = pRight.OfficeRank.CompareTo(pLeft.OfficeRank);
            if (result != 0) return result;
            result = pRight.TenureYears.CompareTo(pLeft.TenureYears);
            if (result != 0) return result;
            result = pRight.Merit.CompareTo(pLeft.Merit);
            if (result != 0) return result;
            result = pRight.MilitaryMerit.CompareTo(pLeft.MilitaryMerit);
            return result != 0 ? result : pLeft.ActorId.CompareTo(pRight.ActorId);
        }

        public static long StableActorTie(long pLeftActorId,
            long pRightActorId)
        {
            return Math.Min(pLeftActorId, pRightActorId);
        }

        public static int FullEvaluationCount(int pCandidateCount)
        {
            return Math.Min(MaximumFullEvaluations,
                Math.Max(0, pCandidateCount));
        }

        public static bool IsConferredKind(string pTitleKind)
        {
            return string.Equals(Normalize(pTitleKind), "conferred",
                StringComparison.Ordinal);
        }

        public static bool CanCommitIdentity(string pTitleKind,
            long pActorId, long pKingdomId, long pShiId, long pReignId)
        {
            if (pActorId < 0) return false;
            string kind = Normalize(pTitleKind);
            if (IsConferredKind(kind)) return pKingdomId >= 0;
            return pShiId >= 0;
        }

        public static bool ShouldReserveShiTitle(string pTitleKind,
            long pShiId)
        {
            return pShiId >= 0;
        }

        public static string HistoryEventType(string pTitleKind)
        {
            return IsConferredKind(pTitleKind)
                ? "conferred_posthumous"
                : "posthumous";
        }

        public static ConferredPosthumousResult ValidatePreview(bool pDead,
            bool pHasKingdomContext, bool pHasHistoricalRole,
            bool pAlreadyTitled, int pCooldownRemaining)
        {
            if (!pHasKingdomContext)
                return ConferredPosthumousResult.MissingContext;
            if (!pDead) return ConferredPosthumousResult.TargetLiving;
            if (!pHasHistoricalRole)
                return ConferredPosthumousResult.NoHistoricalRelationship;
            if (pAlreadyTitled)
                return ConferredPosthumousResult.AlreadyTitled;
            return pCooldownRemaining > 0
                ? ConferredPosthumousResult.Cooldown
                : ConferredPosthumousResult.Success;
        }

        public static string BuildPreviewToken(long pKingdomId,
            long pActorId, string pPosthumousTitle, int pCurrentYear,
            long pCooldownRecordId)
        {
            string value = pKingdomId + "|" + pActorId + "|" +
                           Normalize(pPosthumousTitle) + "|" +
                           pCurrentYear + "|" + pCooldownRecordId;
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 1099511628211UL;
                }
                return hash.ToString("x16");
            }
        }

        public static bool ShouldQueueAi(int pCurrentYear,
            int pLastConferredYear, int pLastQueuedYear,
            long pKingdomId)
        {
            if (CooldownRemaining(pCurrentYear, pLastConferredYear) > 0)
                return false;
            if (pLastQueuedYear >= 0)
                return pCurrentYear - pLastQueuedYear >=
                       AiRetryIntervalYears;
            int yearSlot = PositiveModulo(pCurrentYear,
                AiRetryIntervalYears);
            int kingdomSlot = PositiveModulo(pKingdomId,
                AiRetryIntervalYears);
            return yearSlot == kingdomSlot;
        }

        private static int PositiveModulo(long pValue, int pDivisor)
        {
            long value = pValue % pDivisor;
            return (int)(value < 0 ? value + pDivisor : value);
        }

        public static int RoleWeight(ConferredPosthumousRole pRoles)
        {
            if ((pRoles & ConferredPosthumousRole.FormerRuler) != 0)
                return 40;
            if ((pRoles & ConferredPosthumousRole.Royal) != 0)
                return 30;
            if ((pRoles & ConferredPosthumousRole.General) != 0)
                return 25;
            return (pRoles & ConferredPosthumousRole.Official) != 0
                ? 20
                : 0;
        }

        public static int FinalCandidateValue(
            ConferredPosthumousCandidateScore pCandidate,
            int pTitleTotalScore)
        {
            long value = (long)Math.Max(0, pCandidate.RoleWeight) * 1_000_000L +
                         (long)Math.Max(0, pCandidate.NobleRank) * 10_000L +
                         (long)Math.Max(0, pCandidate.OfficeRank) * 1_000L +
                         (long)Math.Max(0, pCandidate.TenureYears) * 100L +
                         (long)Math.Max(0, pCandidate.Merit) * 10L +
                         (long)Math.Max(0, pCandidate.MilitaryMerit) * 10L +
                         Math.Max(-1_000, Math.Min(1_000,
                             pTitleTotalScore));
            return value > int.MaxValue
                ? int.MaxValue
                : value < int.MinValue ? int.MinValue : (int)value;
        }

        private static string Normalize(string pValue)
        {
            return pValue?.Trim() ?? "";
        }
    }
}
