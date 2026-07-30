using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class BoundedRoundRobinCursor<T> : IDisposable
    {
        private readonly Func<IEnumerable<T>> _source;
        private IEnumerator<T> _enumerator;

        public BoundedRoundRobinCursor(Func<IEnumerable<T>> source)
        {
            _source = source;
        }

        public IReadOnlyList<T> Take(int maximumItems)
        {
            var result = new List<T>(Math.Max(0, maximumItems));
            var seen = new HashSet<T>();
            int emptyOrInvalidPasses = 0;
            while (result.Count < maximumItems &&
                   emptyOrInvalidPasses < 2)
            {
                if (_enumerator == null)
                    _enumerator = (_source?.Invoke() ?? Array.Empty<T>())
                        .GetEnumerator();
                bool moved;
                try { moved = _enumerator.MoveNext(); }
                catch
                {
                    Reset();
                    emptyOrInvalidPasses++;
                    continue;
                }
                if (!moved)
                {
                    Reset();
                    emptyOrInvalidPasses++;
                    continue;
                }
                emptyOrInvalidPasses = 0;
                if (!seen.Add(_enumerator.Current)) break;
                result.Add(_enumerator.Current);
            }
            return result;
        }

        public void Dispose()
        {
            Reset();
        }

        private void Reset()
        {
            try { _enumerator?.Dispose(); }
            catch { }
            _enumerator = null;
        }
    }

    public enum WarSettlementAiDecision
    {
        None,
        Surrender,
        Peace,
        EnforceDemands
    }

    public enum WarSettlementPosition
    {
        Contested,
        Losing,
        Winning
    }

    public readonly struct WarMilitaryFacts
    {
        public WarMilitaryFacts(int availableFieldArmies,
            bool capitalThreatened, bool frontCollapsed,
            int averageSupply, int averageOrganization,
            bool canCounterattack)
        {
            AvailableFieldArmies = Math.Max(0, availableFieldArmies);
            CapitalThreatened = capitalThreatened;
            FrontCollapsed = frontCollapsed;
            AverageSupply = ClampPercent(averageSupply);
            AverageOrganization = ClampPercent(averageOrganization);
            CanCounterattack = canCounterattack;
        }

        public int AvailableFieldArmies { get; }
        public bool CapitalThreatened { get; }
        public bool FrontCollapsed { get; }
        public int AverageSupply { get; }
        public int AverageOrganization { get; }
        public bool CanCounterattack { get; }

        private static int ClampPercent(int pValue)
        {
            return Math.Max(0, Math.Min(100, pValue));
        }
    }

    public static class WarMilitaryFactsRules
    {
        private const int CriticalSupply = 10;
        private const int RetreatOrganization = 25;
        private const int CounterattackOrganization = 60;

        public static WarMilitaryFacts Resolve(int availableFieldArmies,
            bool capitalThreatened, int averageSupply,
            int averageOrganization, int signedWarScore)
        {
            int available = Math.Max(0, availableFieldArmies);
            int supply = Math.Max(0, Math.Min(100, averageSupply));
            int organization = Math.Max(0,
                Math.Min(100, averageOrganization));
            int score = Math.Max(-100, Math.Min(100, signedWarScore));
            bool collapsed = available == 0 ||
                             capitalThreatened &&
                             (available <= 1 ||
                              supply <= CriticalSupply ||
                              organization < RetreatOrganization) ||
                             score <= -80 && available <= 1;
            bool counterattack = !collapsed && available > 0 &&
                                 supply > CriticalSupply &&
                                 organization >= CounterattackOrganization;
            return new WarMilitaryFacts(available, capitalThreatened,
                collapsed, supply, organization, counterattack);
        }
    }

    public sealed class WarMilitaryFactsCache
    {
        private readonly Dictionary<(long WarId, long KingdomId),
            (long WorldDay, int SignedWarScore, WarMilitaryFacts Facts)>
                _entries =
                new Dictionary<(long, long),
                    (long, int, WarMilitaryFacts)>();

        public void Store(long warId, long kingdomId, long worldDay,
            WarMilitaryFacts pFacts, int signedWarScore = 0)
        {
            if (warId < 0L || kingdomId < 0L) return;
            _entries[(warId, kingdomId)] =
                (Math.Max(0L, worldDay), ClampScore(signedWarScore),
                    pFacts);
        }

        public bool TryGet(long warId, long kingdomId, long worldDay,
            out WarMilitaryFacts pFacts, int signedWarScore = 0)
        {
            pFacts = default;
            if (!_entries.TryGetValue((warId, kingdomId),
                    out (long WorldDay, int SignedWarScore,
                        WarMilitaryFacts Facts) entry) ||
                entry.WorldDay != Math.Max(0L, worldDay) ||
                entry.SignedWarScore != ClampScore(signedWarScore))
                return false;
            pFacts = entry.Facts;
            return true;
        }

        public int RemoveWar(long warId)
        {
            var keys = new List<(long WarId, long KingdomId)>();
            foreach ((long WarId, long KingdomId) key in _entries.Keys)
                if (key.WarId == warId) keys.Add(key);
            for (int i = 0; i < keys.Count; i++) _entries.Remove(keys[i]);
            return keys.Count;
        }

        public void Clear()
        {
            _entries.Clear();
        }

        private static int ClampScore(int pValue)
        {
            return Math.Max(-100, Math.Min(100, pValue));
        }
    }

    public readonly struct WarSettlementAiFacts
    {
        public WarSettlementAiFacts(int warYears,
            float requesterToOpponentMilitaryRatio,
            float requesterFieldLossRatio, float opponentFieldLossRatio,
            float requesterCitiesLostRatio, float opponentCitiesLostRatio,
            bool requesterCapitalThreatened, bool opponentCapitalThreatened,
            bool requesterBorderThreatened, float requesterWarFatigue,
            float opponentWarFatigue, float requesterFoodSecurity,
            float requesterOrder, bool rulerWeak, bool rulerResolute,
            bool peaceCourtDominant, bool warCourtDominant,
            bool highLegitimacyWar,
            int requesterAvailableFieldArmies = -1,
            int opponentAvailableFieldArmies = -1,
            bool requesterFrontCollapsed = false,
            bool opponentFrontCollapsed = false,
            int requesterAverageSupply = 100,
            int opponentAverageSupply = 100,
            int requesterAverageOrganization = 100,
            int opponentAverageOrganization = 100,
            bool requesterCanCounterattack = false,
            bool opponentCanCounterattack = false,
            int requesterWarExhaustion = 0,
            int opponentWarExhaustion = 0)
        {
            WarYears = Math.Max(0, warYears);
            RequesterToOpponentMilitaryRatio = Math.Max(0f,
                requesterToOpponentMilitaryRatio);
            RequesterFieldLossRatio = Clamp01(requesterFieldLossRatio);
            OpponentFieldLossRatio = Clamp01(opponentFieldLossRatio);
            RequesterCitiesLostRatio = Clamp01(requesterCitiesLostRatio);
            OpponentCitiesLostRatio = Clamp01(opponentCitiesLostRatio);
            RequesterCapitalThreatened = requesterCapitalThreatened;
            OpponentCapitalThreatened = opponentCapitalThreatened;
            RequesterBorderThreatened = requesterBorderThreatened;
            RequesterWarFatigue = Clamp01(requesterWarFatigue);
            OpponentWarFatigue = Clamp01(opponentWarFatigue);
            RequesterFoodSecurity = Clamp01(requesterFoodSecurity);
            RequesterOrder = Clamp01(requesterOrder);
            RulerWeak = rulerWeak;
            RulerResolute = rulerResolute;
            PeaceCourtDominant = peaceCourtDominant;
            WarCourtDominant = warCourtDominant;
            HighLegitimacyWar = highLegitimacyWar;
            RequesterAvailableFieldArmies = Math.Max(-1,
                requesterAvailableFieldArmies);
            OpponentAvailableFieldArmies = Math.Max(-1,
                opponentAvailableFieldArmies);
            RequesterFrontCollapsed = requesterFrontCollapsed;
            OpponentFrontCollapsed = opponentFrontCollapsed;
            RequesterAverageSupply = ClampPercent(requesterAverageSupply);
            OpponentAverageSupply = ClampPercent(opponentAverageSupply);
            RequesterAverageOrganization = ClampPercent(
                requesterAverageOrganization);
            OpponentAverageOrganization = ClampPercent(
                opponentAverageOrganization);
            RequesterCanCounterattack = requesterCanCounterattack;
            OpponentCanCounterattack = opponentCanCounterattack;
            RequesterWarExhaustion = ClampPercent(requesterWarExhaustion);
            OpponentWarExhaustion = ClampPercent(opponentWarExhaustion);
        }

        public int WarYears { get; }
        public float RequesterToOpponentMilitaryRatio { get; }
        public float RequesterFieldLossRatio { get; }
        public float OpponentFieldLossRatio { get; }
        public float RequesterCitiesLostRatio { get; }
        public float OpponentCitiesLostRatio { get; }
        public bool RequesterCapitalThreatened { get; }
        public bool OpponentCapitalThreatened { get; }
        public bool RequesterBorderThreatened { get; }
        public float RequesterWarFatigue { get; }
        public float OpponentWarFatigue { get; }
        public float RequesterFoodSecurity { get; }
        public float RequesterOrder { get; }
        public bool RulerWeak { get; }
        public bool RulerResolute { get; }
        public bool PeaceCourtDominant { get; }
        public bool WarCourtDominant { get; }
        public bool HighLegitimacyWar { get; }
        public int RequesterAvailableFieldArmies { get; }
        public int OpponentAvailableFieldArmies { get; }
        public bool RequesterFrontCollapsed { get; }
        public bool OpponentFrontCollapsed { get; }
        public int RequesterAverageSupply { get; }
        public int OpponentAverageSupply { get; }
        public int RequesterAverageOrganization { get; }
        public int OpponentAverageOrganization { get; }
        public bool RequesterCanCounterattack { get; }
        public bool OpponentCanCounterattack { get; }
        public int RequesterWarExhaustion { get; }
        public int OpponentWarExhaustion { get; }

        private static float Clamp01(float pValue)
        {
            return Math.Max(0f, Math.Min(1f, pValue));
        }

        private static int ClampPercent(int pValue)
        {
            return Math.Max(0, Math.Min(100, pValue));
        }
    }

    internal readonly struct DiplomacyProposalAiCandidate
    {
        public DiplomacyProposalAiCandidate(DiplomacyProposalType pType,
            bool allowed, int opinion, float requesterPowerRatio,
            bool directRoyalMarriage, float targetPowerRatio,
            bool targetHasMandate, long targetKingdomId = -1L,
            bool principalHouseholdOffer = false)
        {
            Type = pType;
            Allowed = allowed;
            Opinion = opinion;
            RequesterPowerRatio = requesterPowerRatio;
            DirectRoyalMarriage = directRoyalMarriage;
            TargetPowerRatio = targetPowerRatio;
            TargetHasMandate = targetHasMandate;
            TargetKingdomId = targetKingdomId;
            PrincipalHouseholdOffer = principalHouseholdOffer;
        }

        public DiplomacyProposalType Type { get; }
        public bool Allowed { get; }
        public int Opinion { get; }
        public float RequesterPowerRatio { get; }
        public bool DirectRoyalMarriage { get; }
        public float TargetPowerRatio { get; }
        public bool TargetHasMandate { get; }
        public long TargetKingdomId { get; }
        public bool PrincipalHouseholdOffer { get; }
    }

    internal readonly struct WarSettlementSelectionCandidate
    {
        public WarSettlementSelectionCandidate(bool eligible,
            WarSettlementAiDecision decision, int urgency,
            int warYears = 0)
        {
            Eligible = eligible;
            Decision = decision;
            Urgency = urgency;
            WarYears = Math.Max(0, warYears);
        }

        public bool Eligible { get; }
        public WarSettlementAiDecision Decision { get; }
        public int Urgency { get; }
        public int WarYears { get; }
    }

    public readonly struct SeparatePeaceAiCandidateFacts
    {
        public SeparatePeaceAiCandidateFacts(bool authorizedPair,
            bool totalWar, bool protectedWar, bool pendingProposal,
            bool recentRejection, WarParticipantRoleKind exitRootRole,
            float occupiedCityRatio, int exitWarExhaustion,
            float exitToRequesterPowerRatio,
            float exitShareOfCoalitionPower,
            bool requesterIsWarLeader, bool exitRootWantsPeace)
        {
            AuthorizedPair = authorizedPair;
            TotalWar = totalWar;
            ProtectedWar = protectedWar;
            PendingProposal = pendingProposal;
            RecentRejection = recentRejection;
            ExitRootRole = exitRootRole;
            OccupiedCityRatio = Clamp01(occupiedCityRatio);
            ExitWarExhaustion = Math.Max(0,
                Math.Min(100, exitWarExhaustion));
            ExitToRequesterPowerRatio = FiniteNonNegative(
                exitToRequesterPowerRatio);
            ExitShareOfCoalitionPower = Clamp01(
                exitShareOfCoalitionPower);
            RequesterIsWarLeader = requesterIsWarLeader;
            ExitRootWantsPeace = exitRootWantsPeace;
        }

        public bool AuthorizedPair { get; }
        public bool TotalWar { get; }
        public bool ProtectedWar { get; }
        public bool PendingProposal { get; }
        public bool RecentRejection { get; }
        public WarParticipantRoleKind ExitRootRole { get; }
        public float OccupiedCityRatio { get; }
        public int ExitWarExhaustion { get; }
        public float ExitToRequesterPowerRatio { get; }
        public float ExitShareOfCoalitionPower { get; }
        public bool RequesterIsWarLeader { get; }
        public bool ExitRootWantsPeace { get; }

        private static float Clamp01(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(1f, pValue));
        }

        private static float FiniteNonNegative(float pValue)
        {
            return float.IsNaN(pValue) || float.IsInfinity(pValue) ||
                   pValue < 0f ? 0f : pValue;
        }
    }

    internal static class DiplomacyProposalAiRules
    {
        public const int MaximumWarSettlementAssessments = 4;
        public const int MaximumWarSettlementScanBudget = 8;
        public const int MaximumCoalitionCities = 2;
        public const int MaximumCoalitionTargets = 12;
        public const int MaximumCoalitionAssessments = 3;
        public const int MaximumSeparatePeaceTargetAssessments = 4;
        public const int MaximumSeparatePeacePotentialCityScans = 16;
        private const int MinimumSeparatePeaceLeaderScore = 80;

        public static int BaseWillingness(WarSettlementAiDecision pDecision)
        {
            return pDecision switch
            {
                WarSettlementAiDecision.Surrender => -100,
                WarSettlementAiDecision.Peace => -20,
                WarSettlementAiDecision.EnforceDemands => 15,
                _ => 0
            };
        }

        public static bool IsExtremeEarlySurrenderEligible(
            WarSettlementAiFacts pFacts)
        {
            return IsShortUninvadedWar(pFacts) &&
                   pFacts.RequesterToOpponentMilitaryRatio <= .18f &&
                   pFacts.RequesterFieldLossRatio >= .55f &&
                   pFacts.RulerWeak && !pFacts.RulerResolute &&
                   pFacts.PeaceCourtDominant && !pFacts.WarCourtDominant &&
                   !pFacts.HighLegitimacyWar;
        }

        public static float EarlySurrenderProbability(
            WarSettlementAiFacts pFacts)
        {
            return IsExtremeEarlySurrenderEligible(pFacts) ? .04f : 0f;
        }

        public static bool IsMateriallyLosing(WarSettlementAiFacts pFacts)
        {
            return ResolvePosition(pFacts) == WarSettlementPosition.Losing;
        }

        public static bool IsMateriallyWinning(WarSettlementAiFacts pFacts)
        {
            return ResolvePosition(pFacts) == WarSettlementPosition.Winning;
        }

        public static WarSettlementPosition ResolvePosition(
            WarSettlementAiFacts pFacts)
        {
            int advantage = RelativeWarAdvantage(pFacts);
            if (advantage <= -20) return WarSettlementPosition.Losing;
            if (advantage >= 20) return WarSettlementPosition.Winning;
            return WarSettlementPosition.Contested;
        }

        public static WarSettlementPosition ResolvePositionFromSignedWarScore(
            int pSignedWarScore)
        {
            if (pSignedWarScore <= -20) return WarSettlementPosition.Losing;
            if (pSignedWarScore >= 20) return WarSettlementPosition.Winning;
            return WarSettlementPosition.Contested;
        }

        public static WarSettlementPosition Opposite(
            WarSettlementPosition pPosition)
        {
            return pPosition switch
            {
                WarSettlementPosition.Losing => WarSettlementPosition.Winning,
                WarSettlementPosition.Winning => WarSettlementPosition.Losing,
                _ => WarSettlementPosition.Contested
            };
        }

        public static WarSettlementAiDecision SelectWarSettlement(
            WarSettlementAiFacts pFacts, float earlySurrenderRoll)
        {
            return SelectWarSettlement(pFacts, ResolvePosition(pFacts),
                earlySurrenderRoll);
        }

        public static WarSettlementAiDecision SelectWarSettlement(
            WarSettlementAiFacts pFacts, WarSettlementPosition pPosition,
            float earlySurrenderRoll)
        {
            if (IsShortUninvadedWar(pFacts))
            {
                float probability = EarlySurrenderProbability(pFacts);
                return probability > 0f && earlySurrenderRoll >= 0f &&
                       earlySurrenderRoll < probability
                    ? WarSettlementAiDecision.Surrender
                    : WarSettlementAiDecision.None;
            }

            if (pPosition == WarSettlementPosition.Losing)
            {
                if (SurrenderScore(pFacts) >= 60)
                    return WarSettlementAiDecision.Surrender;
                if (LosingPeaceScore(pFacts) >= 60)
                    return WarSettlementAiDecision.Peace;
            }
            if (pPosition == WarSettlementPosition.Winning &&
                EnforcementScore(pFacts) >= 60)
                return WarSettlementAiDecision.EnforceDemands;
            if (IsExhaustedStalemate(pFacts))
                return WarSettlementAiDecision.Peace;
            return WarSettlementAiDecision.None;
        }

        public static bool IsReadyToAcceptPeace(WarSettlementAiFacts pFacts)
        {
            return IsReadyToAcceptPeace(pFacts, ResolvePosition(pFacts));
        }

        public static bool IsReadyToAcceptPeace(WarSettlementAiFacts pFacts,
            WarSettlementPosition pPosition)
        {
            WarSettlementAiDecision decision = SelectWarSettlement(pFacts,
                pPosition, earlySurrenderRoll: 1f);
            return decision == WarSettlementAiDecision.Peace ||
                   decision == WarSettlementAiDecision.Surrender;
        }

        public static bool IsReadyToConcede(WarSettlementAiFacts pFacts)
        {
            return IsReadyToConcede(pFacts, ResolvePosition(pFacts));
        }

        public static bool IsReadyToConcede(WarSettlementAiFacts pFacts,
            WarSettlementPosition pPosition)
        {
            return SelectWarSettlement(pFacts, pPosition,
                       earlySurrenderRoll: 1f) ==
                   WarSettlementAiDecision.Surrender;
        }

        public static int SettlementUrgency(WarSettlementAiFacts pFacts,
            WarSettlementAiDecision pDecision)
        {
            return SettlementUrgency(pFacts, pDecision,
                RelativeWarAdvantage(pFacts));
        }

        public static int SettlementUrgency(WarSettlementAiFacts pFacts,
            WarSettlementAiDecision pDecision, int pSignedWarScore)
        {
            int signedScore = Math.Max(-100, Math.Min(100,
                pSignedWarScore));
            return pDecision switch
            {
                WarSettlementAiDecision.Surrender => 300 +
                    Math.Max(0, SurrenderScore(pFacts)),
                WarSettlementAiDecision.Peace => 200 +
                    Math.Max(0, -signedScore) +
                    Math.Min(30, pFacts.WarYears * 2),
                WarSettlementAiDecision.EnforceDemands => 100 +
                    Math.Max(0, EnforcementScore(pFacts)),
                _ => int.MinValue
            };
        }

        public static int SelectBestWarSettlementIndex(
            IReadOnlyList<WarSettlementSelectionCandidate> pCandidates)
        {
            if (pCandidates == null) return -1;
            int bestIndex = -1;
            int bestUrgency = int.MinValue;
            int bestYears = -1;
            int bestDecisionPriority = int.MinValue;
            int count = Math.Min(MaximumWarSettlementAssessments,
                pCandidates.Count);
            for (int i = 0; i < count; i++)
            {
                WarSettlementSelectionCandidate candidate = pCandidates[i];
                if (!candidate.Eligible || candidate.Decision ==
                        WarSettlementAiDecision.None) continue;
                int decisionPriority = SettlementDecisionPriority(
                    candidate.Decision);
                bool better = bestIndex < 0 ||
                              decisionPriority > bestDecisionPriority ||
                              decisionPriority == bestDecisionPriority &&
                              (candidate.WarYears > bestYears ||
                               candidate.WarYears == bestYears &&
                               candidate.Urgency > bestUrgency);
                if (!better) continue;
                bestIndex = i;
                bestUrgency = candidate.Urgency;
                bestYears = candidate.WarYears;
                bestDecisionPriority = decisionPriority;
            }
            return bestIndex;
        }

        public static WarSettlementAiDecision ApplyMultiWarPeacePressure(
            WarSettlementAiDecision pDecision, int activeWarCount,
            int warYears, WarSettlementPosition pPosition)
        {
            if (pDecision != WarSettlementAiDecision.None)
                return pDecision;
            return activeWarCount >= 2 && warYears >= 2 &&
                   pPosition != WarSettlementPosition.Winning
                ? WarSettlementAiDecision.Peace
                : WarSettlementAiDecision.None;
        }

        public static int SeparatePeaceTargetScore(
            SeparatePeaceAiCandidateFacts pFacts)
        {
            int occupied = (int)Math.Round(pFacts.OccupiedCityRatio * 100f);
            int exhaustion = pFacts.ExitWarExhaustion;
            int weakness = (int)Math.Round(Math.Max(0f,
                1f - pFacts.ExitToRequesterPowerRatio) * 50f);
            int isolation = (int)Math.Round(
                pFacts.ExitShareOfCoalitionPower * 100f);
            return occupied + exhaustion + weakness + isolation;
        }

        public static bool CanQueueSeparatePeace(
            SeparatePeaceAiCandidateFacts pFacts)
        {
            if (!pFacts.AuthorizedPair || pFacts.TotalWar ||
                pFacts.ProtectedWar || pFacts.PendingProposal ||
                pFacts.RecentRejection) return false;
            if (pFacts.ExitRootRole != WarParticipantRoleKind.Independent &&
                pFacts.ExitRootRole != WarParticipantRoleKind.Tributary)
                return false;
            return pFacts.RequesterIsWarLeader
                ? SeparatePeaceTargetScore(pFacts) >=
                  MinimumSeparatePeaceLeaderScore
                : pFacts.ExitRootWantsPeace;
        }

        public static DiplomacyProposalType SeparatePeaceProposalType(
            bool requesterIsExitRoot, WarSettlementPosition pPosition)
        {
            if (requesterIsExitRoot)
                return pPosition == WarSettlementPosition.Losing
                    ? DiplomacyProposalType.Surrender
                    : DiplomacyProposalType.Peace;
            return pPosition == WarSettlementPosition.Winning
                ? DiplomacyProposalType.EnforceDemands
                : pPosition == WarSettlementPosition.Losing
                    ? DiplomacyProposalType.Surrender
                    : DiplomacyProposalType.Peace;
        }

        private static int SettlementDecisionPriority(
            WarSettlementAiDecision pDecision)
        {
            return pDecision switch
            {
                WarSettlementAiDecision.Surrender => 3,
                WarSettlementAiDecision.Peace => 2,
                WarSettlementAiDecision.EnforceDemands => 1,
                _ => 0
            };
        }

        public static bool CanQueueWarSettlementProposal(
            WarSettlementAiDecision pDecision,
            bool requesterReadyForPeace, bool actionAllowed,
            bool rejectionCooldownActive)
        {
            if (!actionAllowed || rejectionCooldownActive) return false;
            if (pDecision == WarSettlementAiDecision.None) return false;
            return pDecision != WarSettlementAiDecision.Peace ||
                   requesterReadyForPeace;
        }

        private static bool IsShortUninvadedWar(WarSettlementAiFacts pFacts)
        {
            return pFacts.WarYears < 3 &&
                   pFacts.RequesterCitiesLostRatio <= 0f &&
                   !pFacts.RequesterCapitalThreatened &&
                   pFacts.RequesterWarExhaustion < 30;
        }

        private static int SurrenderScore(WarSettlementAiFacts pFacts)
        {
            int score = BaseWillingness(WarSettlementAiDecision.Surrender);
            score += SurrenderPowerScore(pFacts);
            score += SurrenderWarSituationScore(pFacts);
            score += SurrenderResolveScore(pFacts);
            return score;
        }

        public static int SurrenderPowerScore(WarSettlementAiFacts pFacts)
        {
            float ratio = pFacts.RequesterToOpponentMilitaryRatio;
            return ratio <= .08f ? 120 : ratio <= .15f ? 90 :
                ratio <= .25f ? 70 : ratio <= .40f ? 45 :
                ratio <= .60f ? 20 : ratio >= 1.2f ? -35 : 0;
        }

        public static int SurrenderWarSituationScore(
            WarSettlementAiFacts pFacts)
        {
            int score = Round(pFacts.RequesterFieldLossRatio * 80f);
            score += Round(pFacts.RequesterCitiesLostRatio * 90f);
            score += pFacts.RequesterCapitalThreatened ? 45 : 0;
            score += pFacts.RequesterBorderThreatened ? 10 : 0;
            score += Round(pFacts.RequesterWarFatigue * 40f);
            score += Round((1f - pFacts.RequesterFoodSecurity) * 25f);
            score += Round((1f - pFacts.RequesterOrder) * 25f);
            score += Math.Min(30, pFacts.WarYears * 2);
            if (pFacts.RequesterAvailableFieldArmies >= 0)
            {
                if (pFacts.RequesterAvailableFieldArmies == 0) score += 25;
                if (pFacts.RequesterFrontCollapsed) score += 30;
                score += (100 - pFacts.RequesterAverageSupply) / 5;
                score += (100 - pFacts.RequesterAverageOrganization) / 5;
                if (pFacts.RequesterCanCounterattack) score -= 20;
            }
            return score;
        }

        public static int SurrenderResolveScore(WarSettlementAiFacts pFacts)
        {
            int score = pFacts.RulerWeak ? 20 : 0;
            score += pFacts.PeaceCourtDominant ? 20 : 0;
            score -= pFacts.RulerResolute ? 45 : 0;
            score -= pFacts.WarCourtDominant ? 35 : 0;
            score -= pFacts.HighLegitimacyWar ? 70 : 0;
            return score;
        }

        private static int EnforcementScore(WarSettlementAiFacts pFacts)
        {
            float ratio = pFacts.RequesterToOpponentMilitaryRatio;
            if (ratio < 1.2f) return int.MinValue;
            int score = BaseWillingness(
                WarSettlementAiDecision.EnforceDemands);
            score += ratio >= 3f ? 40 : ratio >= 2f ? 30 : 18;
            score += Round(pFacts.OpponentFieldLossRatio * 55f);
            score += Round(pFacts.OpponentCitiesLostRatio * 70f);
            score += pFacts.OpponentCapitalThreatened ? 35 : 0;
            score += Round(pFacts.OpponentWarFatigue * 20f);
            score += Math.Min(15, pFacts.WarYears * 2);
            score -= Round(pFacts.RequesterFieldLossRatio * 20f);
            if (pFacts.OpponentAvailableFieldArmies >= 0)
            {
                if (pFacts.OpponentAvailableFieldArmies == 0) score += 20;
                if (pFacts.OpponentFrontCollapsed) score += 25;
                score += (100 - pFacts.OpponentAverageSupply) / 5;
                score += (100 - pFacts.OpponentAverageOrganization) / 5;
                if (pFacts.OpponentCanCounterattack) score -= 20;
            }
            return score;
        }

        private static int LosingPeaceScore(WarSettlementAiFacts pFacts)
        {
            int score = BaseWillingness(WarSettlementAiDecision.Peace);
            score += Math.Max(0, -RelativeWarAdvantage(pFacts));
            score += Math.Min(24, pFacts.WarYears * 4);
            score += Round(pFacts.RequesterWarFatigue * 25f);
            score += Round(pFacts.RequesterFieldLossRatio * 15f);
            score += pFacts.RequesterBorderThreatened ? 8 : 0;
            score += pFacts.RulerWeak ? 10 : 0;
            score += pFacts.PeaceCourtDominant ? 15 : 0;
            score -= pFacts.RulerResolute ? 25 : 0;
            score -= pFacts.WarCourtDominant ? 20 : 0;
            score -= pFacts.HighLegitimacyWar ? 35 : 0;
            return score;
        }

        public static int RelativeMilitaryAdvantage(
            WarSettlementAiFacts pFacts)
        {
            return RelativeWarAdvantage(pFacts);
        }

        private static int RelativeWarAdvantage(WarSettlementAiFacts pFacts)
        {
            float ratio = Math.Max(.0001f,
                pFacts.RequesterToOpponentMilitaryRatio);
            float signedRatio = (ratio - 1f) / (ratio + 1f);
            int score = Math.Max(-50, Math.Min(50,
                Round(signedRatio * 126f)));
            score += Round((pFacts.OpponentFieldLossRatio -
                            pFacts.RequesterFieldLossRatio) * 50f);
            score += Round((pFacts.OpponentCitiesLostRatio -
                            pFacts.RequesterCitiesLostRatio) * 90f);
            if (pFacts.RequesterCapitalThreatened) score -= 35;
            if (pFacts.OpponentCapitalThreatened) score += 35;
            if (pFacts.RequesterAvailableFieldArmies >= 0 &&
                pFacts.OpponentAvailableFieldArmies >= 0)
            {
                score += Math.Max(-18, Math.Min(18,
                    (pFacts.RequesterAvailableFieldArmies -
                     pFacts.OpponentAvailableFieldArmies) * 6));
                score += (pFacts.RequesterAverageSupply -
                          pFacts.OpponentAverageSupply) / 10;
                score += (pFacts.RequesterAverageOrganization -
                          pFacts.OpponentAverageOrganization) / 10;
                if (pFacts.RequesterFrontCollapsed) score -= 20;
                if (pFacts.OpponentFrontCollapsed) score += 20;
                if (pFacts.RequesterCanCounterattack) score += 8;
                if (pFacts.OpponentCanCounterattack) score -= 8;
            }
            return Math.Max(-100, Math.Min(100, score));
        }

        private static bool IsExhaustedStalemate(
            WarSettlementAiFacts pFacts)
        {
            float ratio = pFacts.RequesterToOpponentMilitaryRatio;
            float averageFatigue = (pFacts.RequesterWarFatigue +
                                    pFacts.OpponentWarFatigue) * .5f;
            float averageLoss = (pFacts.RequesterFieldLossRatio +
                                 pFacts.OpponentFieldLossRatio) * .5f;
            return pFacts.WarYears >= 6 && ratio >= .67f && ratio <= 1.5f &&
                   pFacts.RequesterCitiesLostRatio < .2f &&
                   pFacts.OpponentCitiesLostRatio < .2f &&
                   !pFacts.RequesterCapitalThreatened &&
                   !pFacts.OpponentCapitalThreatened &&
                   (averageFatigue >= .55f || averageLoss >= .4f);
        }

        private static int Round(float pValue)
        {
            return (int)Math.Round(pValue, MidpointRounding.AwayFromZero);
        }

        public static int Score(DiplomacyProposalAiCandidate pCandidate)
        {
            if (!pCandidate.Allowed) return int.MinValue;
            int opinion = Math.Max(-100, Math.Min(100, pCandidate.Opinion));
            return pCandidate.Type switch
            {
                DiplomacyProposalType.Coalition => 100 +
                    Math.Min(60, (int)Math.Round(
                        Math.Max(0f, pCandidate.TargetPowerRatio) * 20f)) +
                    (pCandidate.TargetHasMandate ? 40 : 0) + opinion / 5,
                DiplomacyProposalType.RoyalMarriage => 80 + opinion +
                    (pCandidate.DirectRoyalMarriage ? 25 : 0),
                DiplomacyProposalType.HouseholdOffering => 65 + opinion +
                    (pCandidate.PrincipalHouseholdOffer ? 15 : 0),
                DiplomacyProposalType.Tributary => 90 +
                    Math.Min(60, (int)Math.Round(Math.Max(0f,
                        pCandidate.RequesterPowerRatio - 1f) * 30f)) +
                    opinion / 4,
                DiplomacyProposalType.Alliance => 65 + opinion,
                DiplomacyProposalType.NonAggression => 40 + opinion,
                DiplomacyProposalType.BreakNonAggression => 130 - opinion,
                DiplomacyProposalType.Surrender => BaseWillingness(
                    WarSettlementAiDecision.Surrender),
                DiplomacyProposalType.EnforceDemands => BaseWillingness(
                    WarSettlementAiDecision.EnforceDemands),
                DiplomacyProposalType.Peace => BaseWillingness(
                    WarSettlementAiDecision.Peace),
                _ => 0
            };
        }

        public static DiplomacyProposalAiCandidate SelectBest(
            IReadOnlyList<DiplomacyProposalAiCandidate> pCandidates)
        {
            IReadOnlyList<DiplomacyProposalAiCandidate> ranked =
                RankCandidates(pCandidates);
            return ranked.Count > 0
                ? ranked[0]
                : new DiplomacyProposalAiCandidate(
                    DiplomacyProposalType.None, false, 0, 0f, false, 0f,
                    false);
        }

        public static IReadOnlyList<DiplomacyProposalAiCandidate>
            RankCandidates(IReadOnlyList<DiplomacyProposalAiCandidate>
                pCandidates)
        {
            var result = new List<DiplomacyProposalAiCandidate>();
            if (pCandidates == null) return result;
            for (int index = 0; index < pCandidates.Count; index++)
            {
                DiplomacyProposalAiCandidate candidate = pCandidates[index];
                if (Score(candidate) == int.MinValue) continue;
                result.Add(candidate);
            }
            result.Sort((first, second) =>
                DiplomacyProposalOrderRules.Compare(Score(first),
                    StableTypeOrder(first.Type), first.TargetKingdomId,
                    Score(second), StableTypeOrder(second.Type),
                    second.TargetKingdomId));
            return result;
        }

        private static int StableTypeOrder(DiplomacyProposalType pType)
        {
            return pType switch
            {
                DiplomacyProposalType.Alliance => 1,
                DiplomacyProposalType.NonAggression => 2,
                DiplomacyProposalType.RoyalMarriage => 3,
                DiplomacyProposalType.HouseholdOffering => 4,
                DiplomacyProposalType.Tributary => 5,
                DiplomacyProposalType.Truce => 6,
                DiplomacyProposalType.EndAlliance => 7,
                DiplomacyProposalType.BreakNonAggression => 8,
                DiplomacyProposalType.Coalition => 9,
                _ => 100 + (int)pType
            };
        }
    }
}
