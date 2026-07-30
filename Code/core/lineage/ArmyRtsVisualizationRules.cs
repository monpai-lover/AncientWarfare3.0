using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsRouteColor
    {
        White = 0,
        Red = 1,
        Gold = 2,
        Blue = 3
    }

    public sealed class ArmyRtsVisualizationCandidate
    {
        public ArmyRtsVisualizationCandidate(long armyId, long kingdomId,
            ArmyRtsState state, ArmyRtsRole role, bool playerOrder)
        {
            ArmyId = armyId;
            KingdomId = kingdomId;
            State = state;
            Role = role;
            PlayerOrder = playerOrder;
        }

        public long ArmyId { get; }
        public long KingdomId { get; }
        public ArmyRtsState State { get; }
        public ArmyRtsRole Role { get; }
        public bool PlayerOrder { get; }
    }

    public static class ArmyRtsVisualizationRules
    {
        public const int MaximumVisibleArmies = 24;
        public const int MaximumEntriesRefreshedPerFrame = 8;

        public static bool ShouldDisplay(ArmyRtsMode pMode,
            bool visualsEnabled, long selectedKingdomId)
        {
            return pMode == ArmyRtsMode.On && visualsEnabled &&
                   selectedKingdomId >= 0L;
        }

        public static IReadOnlyList<ArmyRtsVisualizationCandidate>
            SelectVisible(IEnumerable<ArmyRtsVisualizationCandidate> pCandidates,
                long selectedKingdomId)
        {
            var selected = new List<ArmyRtsVisualizationCandidate>(
                MaximumVisibleArmies);
            if (pCandidates == null || selectedKingdomId < 0L)
                return selected;
            foreach (ArmyRtsVisualizationCandidate candidate in pCandidates)
                TryAddVisibleCandidate(selected, candidate,
                    selectedKingdomId);
            return selected;
        }

        public static bool TryAddVisibleCandidate(
            List<ArmyRtsVisualizationCandidate> pSelected,
            ArmyRtsVisualizationCandidate pCandidate,
            long selectedKingdomId)
        {
            if (pSelected == null || pCandidate == null ||
                pCandidate.ArmyId < 0L || selectedKingdomId < 0L ||
                pCandidate.KingdomId != selectedKingdomId) return false;
            int insertAt = pSelected.Count;
            for (int i = 0; i < pSelected.Count; i++)
                if (CompareCandidates(pCandidate, pSelected[i]) < 0)
                {
                    insertAt = i;
                    break;
                }
            if (pSelected.Count >= MaximumVisibleArmies &&
                insertAt >= MaximumVisibleArmies) return false;
            pSelected.Insert(insertAt, pCandidate);
            if (pSelected.Count > MaximumVisibleArmies)
                pSelected.RemoveAt(MaximumVisibleArmies);
            return true;
        }

        public static int RefreshCount(int pVisibleCount)
        {
            return Math.Min(MaximumEntriesRefreshedPerFrame,
                Math.Max(0, pVisibleCount));
        }

        public static ArmyRtsRouteColor ColorFor(ArmyRtsState pState,
            ArmyRtsRole pRole)
        {
            if (pState == ArmyRtsState.Retreat)
                return ArmyRtsRouteColor.Blue;
            if (pState == ArmyRtsState.Assault ||
                pState == ArmyRtsState.Pursue ||
                pRole == ArmyRtsRole.Assault)
                return ArmyRtsRouteColor.Red;
            if (pState == ArmyRtsState.Hold ||
                pRole == ArmyRtsRole.Defense)
                return ArmyRtsRouteColor.Gold;
            return ArmyRtsRouteColor.White;
        }

        private static int CompareCandidates(
            ArmyRtsVisualizationCandidate pFirst,
            ArmyRtsVisualizationCandidate pSecond)
        {
            int byPriority = Priority(pFirst).CompareTo(Priority(pSecond));
            return byPriority != 0
                ? byPriority
                : pFirst.ArmyId.CompareTo(pSecond.ArmyId);
        }

        private static int Priority(ArmyRtsVisualizationCandidate pCandidate)
        {
            if (pCandidate.PlayerOrder) return 0;
            if (pCandidate.Role == ArmyRtsRole.Assault ||
                pCandidate.State == ArmyRtsState.Assault ||
                pCandidate.State == ArmyRtsState.Pursue) return 1;
            if (pCandidate.Role == ArmyRtsRole.Defense ||
                pCandidate.State == ArmyRtsState.Hold) return 2;
            return 3;
        }
    }
}
