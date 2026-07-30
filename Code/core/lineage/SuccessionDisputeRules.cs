using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct SuccessionCitySupportFacts
    {
        public SuccessionCitySupportFacts(long pCityId, bool pIsCapital,
            int pClaimantSupport, int pLoyalistSupport)
        {
            CityId = pCityId;
            IsCapital = pIsCapital;
            ClaimantSupport = pClaimantSupport;
            LoyalistSupport = pLoyalistSupport;
        }

        public long CityId { get; }
        public bool IsCapital { get; }
        public int ClaimantSupport { get; }
        public int LoyalistSupport { get; }
    }

    public enum SuccessionClaimantKind
    {
        None,
        FormerHeir,
        MilitaryDesignate,
        CivilDesignate,
        FirstCollateral
    }

    public enum SuccessionDirection
    {
        None,
        East,
        West,
        South,
        North,
        Former,
        Later
    }

    public enum SuccessionDisputeStatus
    {
        Prepared,
        RivalCreated,
        CitiesTransferred,
        WarStarted,
        Active,
        Settling,
        Closed,
        PermanentSplit
    }

    public readonly struct SuccessionClaimantFacts
    {
        public SuccessionClaimantFacts(long claimantActorId,
            SuccessionClaimantKind kind, int claimantSupport,
            int successorSupport, bool hasSupportCity,
            bool hasActiveDispute,
            bool hasLivingDirectPaternalAncestor)
        {
            ClaimantActorId = claimantActorId;
            Kind = kind;
            ClaimantSupport = claimantSupport;
            SuccessorSupport = successorSupport;
            HasSupportCity = hasSupportCity;
            HasActiveDispute = hasActiveDispute;
            HasLivingDirectPaternalAncestor =
                hasLivingDirectPaternalAncestor;
        }

        public long ClaimantActorId { get; }
        public SuccessionClaimantKind Kind { get; }
        public int ClaimantSupport { get; }
        public int SuccessorSupport { get; }
        public bool HasSupportCity { get; }
        public bool HasActiveDispute { get; }
        public bool HasLivingDirectPaternalAncestor { get; }

        public SuccessionClaimantFacts WithSupport(int pClaimant,
            int pSuccessor)
        {
            return new SuccessionClaimantFacts(ClaimantActorId, Kind,
                pClaimant, pSuccessor, HasSupportCity, HasActiveDispute,
                HasLivingDirectPaternalAncestor);
        }

        public SuccessionClaimantFacts WithLivingAncestor(bool pValue)
        {
            return new SuccessionClaimantFacts(ClaimantActorId, Kind,
                ClaimantSupport, SuccessorSupport, HasSupportCity,
                HasActiveDispute, pValue);
        }

        public SuccessionClaimantFacts WithActiveDispute(bool pValue)
        {
            return new SuccessionClaimantFacts(ClaimantActorId, Kind,
                ClaimantSupport, SuccessorSupport, HasSupportCity, pValue,
                HasLivingDirectPaternalAncestor);
        }

        public SuccessionClaimantFacts WithKind(
            SuccessionClaimantKind pKind)
        {
            return new SuccessionClaimantFacts(ClaimantActorId, pKind,
                ClaimantSupport, SuccessorSupport, HasSupportCity,
                HasActiveDispute, HasLivingDirectPaternalAncestor);
        }
    }

    public static class SuccessionDisputeRules
    {
        public const string WarTypeId = "succession_dispute_war";
        public const int MaximumRivalCourts = 1;
        public const int MaximumRivalCities = 64;
        public const int PermanentSplitYears = 12;
        public const int ReunificationClaimGenerations = 3;
        private const float DirectionAxisEpsilon = 0.001f;

        public static bool CanPrepare(SuccessionClaimantFacts pFacts)
        {
            return pFacts.ClaimantActorId >= 0 &&
                   IsLegalKind(pFacts.Kind) &&
                   (long)pFacts.ClaimantSupport - pFacts.SuccessorSupport >=
                   InheritanceLawRules.DecisiveCandidateLead &&
                   pFacts.HasSupportCity && !pFacts.HasActiveDispute;
        }

        public static int CityLimit(int pOriginalCityCount)
        {
            if (pOriginalCityCount <= 1) return 0;
            return Math.Min(MaximumRivalCities, pOriginalCityCount - 1);
        }

        public static bool ShouldJoinClaimant(int claimantSupport,
            int loyalistSupport)
        {
            return claimantSupport > loyalistSupport;
        }

        public static bool CanFormBalancedTerritorialSplit(
            int pTotalCityCount, int pRivalCityCount)
        {
            if (pTotalCityCount < 2 || pRivalCityCount <= 0 ||
                pRivalCityCount >= pTotalCityCount)
                return false;
            int loyalistCityCount = pTotalCityCount - pRivalCityCount;
            return (long)pRivalCityCount * 3 >= pTotalCityCount &&
                   (long)loyalistCityCount * 3 >= pTotalCityCount;
        }

        public static bool CanCloseCompensation(int pOriginalCityCount,
            int pRivalCityCount, bool pTemporaryWarActive)
        {
            return pOriginalCityCount > 0 && pRivalCityCount == 0 &&
                   !pTemporaryWarActive;
        }

        public static IReadOnlyList<long> SelectBalancedSupportCityIds(
            IReadOnlyList<SuccessionCitySupportFacts> pCities,
            int pTotalCityCount, int pMaximumCities)
        {
            if (pCities == null || pMaximumCities <= 0)
                return Array.Empty<long>();
            var ranked = new List<SuccessionCitySupportFacts>();
            for (int i = 0; i < pCities.Count; i++)
            {
                SuccessionCitySupportFacts city = pCities[i];
                if (city.CityId < 0 || city.IsCapital ||
                    !ShouldJoinClaimant(city.ClaimantSupport,
                        city.LoyalistSupport)) continue;
                ranked.Add(city);
            }
            if (ranked.Count == 0 || ranked.Count > pMaximumCities ||
                !CanFormBalancedTerritorialSplit(pTotalCityCount,
                    ranked.Count))
                return Array.Empty<long>();
            ranked.Sort((left, right) =>
            {
                int leftMargin = left.ClaimantSupport -
                                 left.LoyalistSupport;
                int rightMargin = right.ClaimantSupport -
                                  right.LoyalistSupport;
                int margin = rightMargin.CompareTo(leftMargin);
                return margin != 0
                    ? margin
                    : left.CityId.CompareTo(right.CityId);
            });
            var result = new long[ranked.Count];
            for (int i = 0; i < ranked.Count; i++)
                result[i] = ranked[i].CityId;
            return result;
        }

        public static long SelectAuthoritativeSupportTarget(
            long pRecordedTargetActorId, long pClaimantActorId,
            long pSuccessorActorId)
        {
            if (pRecordedTargetActorId < 0 || pClaimantActorId < 0 ||
                pSuccessorActorId < 0 ||
                pClaimantActorId == pSuccessorActorId)
                return -1L;
            return pRecordedTargetActorId == pClaimantActorId ||
                   pRecordedTargetActorId == pSuccessorActorId
                ? pRecordedTargetActorId
                : -1L;
        }

        public static long SelectAgnaticBranchSupportTarget(
            bool pClaimantLine, bool pSuccessorLine,
            long pClaimantActorId, long pSuccessorActorId)
        {
            if (pClaimantActorId < 0 || pSuccessorActorId < 0 ||
                pClaimantActorId == pSuccessorActorId ||
                pClaimantLine == pSuccessorLine)
                return -1L;
            return pClaimantLine ? pClaimantActorId : pSuccessorActorId;
        }

        public static long SelectExplicitBranchSupportTarget(
            long supporterShiId, long claimantShiId,
            long claimantActorId, long successorShiId,
            long successorActorId)
        {
            if (supporterShiId < 0 || claimantActorId < 0 ||
                successorActorId < 0) return -1L;
            bool claimant = claimantShiId >= 0 &&
                            supporterShiId == claimantShiId;
            bool successor = successorShiId >= 0 &&
                             supporterShiId == successorShiId;
            if (claimant == successor) return -1L;
            return claimant ? claimantActorId : successorActorId;
        }

        public static long SelectLocalFactionSupportTarget(
            long supporterShiId, long claimantShiId,
            long claimantActorId, long successorShiId,
            long successorActorId, string localFactionMode,
            string claimantMode, string successorMode)
        {
            long branchTarget = SelectExplicitBranchSupportTarget(
                supporterShiId, claimantShiId, claimantActorId,
                successorShiId, successorActorId);
            if (branchTarget >= 0) return branchTarget;
            if (string.IsNullOrWhiteSpace(localFactionMode)) return -1L;
            bool claimant = string.Equals(localFactionMode, claimantMode,
                StringComparison.Ordinal);
            bool successor = string.Equals(localFactionMode, successorMode,
                StringComparison.Ordinal);
            if (claimant == successor) return -1L;
            return claimant ? claimantActorId : successorActorId;
        }

        public static bool CanMaintainTerritorialInvariant(
            int pOriginalCityCount, int pRivalCityCount)
        {
            return pOriginalCityCount > 0 && pRivalCityCount > 0;
        }

        public static bool IsMaterialized(SuccessionDisputeStatus pStatus,
            long rivalKingdomId, int originalCityCount, int rivalCityCount)
        {
            return pStatus >= SuccessionDisputeStatus.RivalCreated &&
                   pStatus != SuccessionDisputeStatus.Closed &&
                   rivalKingdomId >= 0 &&
                   CanMaintainTerritorialInvariant(originalCityCount,
                       rivalCityCount);
        }

        public const int OpposedCourtOpinionPenalty = -100;

        public static bool AreOpposedCourts(
            SuccessionDisputeStatus pStatus, long pFirstKingdomId,
            long pSecondKingdomId, long pOriginalKingdomId,
            long pRivalKingdomId, int pOriginalCityCount,
            int pRivalCityCount)
        {
            if (pFirstKingdomId < 0 || pSecondKingdomId < 0 ||
                pFirstKingdomId == pSecondKingdomId ||
                pOriginalKingdomId < 0 || pRivalKingdomId < 0 ||
                !IsMaterialized(pStatus, pRivalKingdomId,
                    pOriginalCityCount, pRivalCityCount)) return false;
            return pFirstKingdomId == pOriginalKingdomId &&
                   pSecondKingdomId == pRivalKingdomId ||
                   pFirstKingdomId == pRivalKingdomId &&
                   pSecondKingdomId == pOriginalKingdomId;
        }

        public static int OpposedCourtOpinion(
            SuccessionDisputeStatus pStatus, long pFirstKingdomId,
            long pSecondKingdomId, long pOriginalKingdomId,
            long pRivalKingdomId, int pOriginalCityCount,
            int pRivalCityCount)
        {
            return AreOpposedCourts(pStatus, pFirstKingdomId,
                pSecondKingdomId, pOriginalKingdomId, pRivalKingdomId,
                pOriginalCityCount, pRivalCityCount)
                ? OpposedCourtOpinionPenalty
                : 0;
        }

        public static SuccessionDirection ResolveDirection(float pDeltaX,
            float pDeltaY, bool claimantAccededLater)
        {
            float horizontal = Math.Abs(pDeltaX);
            float vertical = Math.Abs(pDeltaY);
            if (horizontal <= DirectionAxisEpsilon &&
                vertical <= DirectionAxisEpsilon)
                return claimantAccededLater
                    ? SuccessionDirection.Later
                    : SuccessionDirection.Former;
            if (horizontal > vertical)
                return pDeltaX > 0f
                    ? SuccessionDirection.East
                    : SuccessionDirection.West;
            if (vertical > horizontal)
                return pDeltaY > 0f
                    ? SuccessionDirection.North
                    : SuccessionDirection.South;
            return claimantAccededLater
                ? SuccessionDirection.Later
                : SuccessionDirection.Former;
        }

        public static int DeadlineYear(int pStartYear)
        {
            long deadline = (long)pStartYear + PermanentSplitYears;
            return deadline >= int.MaxValue
                ? int.MaxValue
                : (int)deadline;
        }

        public static bool ShouldBecomePermanent(int currentYear,
            int deadlineYear, bool warStillActive)
        {
            return warStillActive && deadlineYear >= 0 &&
                   currentYear >= deadlineYear;
        }

        public static bool ShouldBecomePermanent(int currentYear,
            int deadlineYear, bool warStillActive,
            bool hasTerritorialRival)
        {
            return hasTerritorialRival && ShouldBecomePermanent(currentYear,
                deadlineYear, warStillActive);
        }

        public static bool CanUseReunificationClaim(
            SuccessionDisputeStatus pStatus, bool isOppositeCourt,
            int rulerGeneration, int generationBoundary,
            bool hasActiveWar)
        {
            return pStatus == SuccessionDisputeStatus.PermanentSplit &&
                   isOppositeCourt && !hasActiveWar &&
                   rulerGeneration >= 0 && generationBoundary >= 0 &&
                   rulerGeneration <= generationBoundary;
        }

        public static bool ShouldPreserveDisputeIdentity(
            SuccessionDisputeStatus pStatus, bool isDisputeCourt)
        {
            if (!isDisputeCourt) return false;
            return pStatus >= SuccessionDisputeStatus.RivalCreated &&
                   pStatus <= SuccessionDisputeStatus.Settling ||
                   pStatus == SuccessionDisputeStatus.PermanentSplit;
        }

        public static SuccessionDisputeStatus NextDeferredStage(
            SuccessionDisputeStatus pCurrent)
        {
            return pCurrent switch
            {
                SuccessionDisputeStatus.Prepared =>
                    SuccessionDisputeStatus.RivalCreated,
                SuccessionDisputeStatus.RivalCreated =>
                    SuccessionDisputeStatus.CitiesTransferred,
                SuccessionDisputeStatus.CitiesTransferred =>
                    SuccessionDisputeStatus.WarStarted,
                SuccessionDisputeStatus.WarStarted =>
                    SuccessionDisputeStatus.Active,
                SuccessionDisputeStatus.Settling =>
                    SuccessionDisputeStatus.Closed,
                _ => pCurrent
            };
        }

        public static bool ShouldCompensateBeforeWar(
            SuccessionDisputeStatus pStatus, bool hasActiveWar)
        {
            return !hasActiveWar &&
                   pStatus >= SuccessionDisputeStatus.RivalCreated &&
                   pStatus <= SuccessionDisputeStatus.CitiesTransferred;
        }

        public static bool CanSettleInitialWar(
            SuccessionDisputeStatus pStatus, long recordedWarId,
            long endedWarId, bool materialized)
        {
            return pStatus == SuccessionDisputeStatus.Active &&
                   recordedWarId >= 0 && recordedWarId == endedWarId &&
                   materialized;
        }

        public static bool CanActivatePreparedForSuccessor(
            SuccessionDisputeStatus pStatus, long preparedSuccessorId,
            long installedSuccessorId)
        {
            return pStatus == SuccessionDisputeStatus.Prepared &&
                   preparedSuccessorId >= 0 &&
                   preparedSuccessorId == installedSuccessorId;
        }

        public static string DirectionId(SuccessionDirection pDirection)
        {
            return pDirection switch
            {
                SuccessionDirection.East => "east",
                SuccessionDirection.West => "west",
                SuccessionDirection.South => "south",
                SuccessionDirection.North => "north",
                SuccessionDirection.Former => "former",
                SuccessionDirection.Later => "later",
                _ => ""
            };
        }

        private static bool IsLegalKind(SuccessionClaimantKind pKind)
        {
            return pKind == SuccessionClaimantKind.FormerHeir ||
                   pKind == SuccessionClaimantKind.MilitaryDesignate ||
                   pKind == SuccessionClaimantKind.CivilDesignate ||
                   pKind == SuccessionClaimantKind.FirstCollateral;
        }

    }
}
