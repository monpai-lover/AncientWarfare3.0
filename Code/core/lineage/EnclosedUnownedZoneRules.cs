using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct EnclosedZoneNeighbourFacts
    {
        public readonly bool IsOwned;
        public readonly bool IsLive;
        public readonly long CityId;
        public readonly long KingdomId;
        public readonly int CityCenterX;
        public readonly int CityCenterY;

        public EnclosedZoneNeighbourFacts(bool pIsOwned, bool pIsLive,
            long pCityId, long pKingdomId, int pCityCenterX,
            int pCityCenterY)
        {
            IsOwned = pIsOwned;
            IsLive = pIsLive;
            CityId = pCityId;
            KingdomId = pKingdomId;
            CityCenterX = pCityCenterX;
            CityCenterY = pCityCenterY;
        }
    }

    public static class EnclosedUnownedZoneRules
    {
        public static long SelectTargetCity(bool zoneAlreadyOwned,
            bool worldEdge, int groundTileCount,
            int cardinalNeighbourCount, int zoneX, int zoneY,
            IReadOnlyList<EnclosedZoneNeighbourFacts> neighbours)
        {
            if (zoneAlreadyOwned || worldEdge || groundTileCount <= 0 ||
                cardinalNeighbourCount != 4 || neighbours == null ||
                neighbours.Count != 4)
                return -1L;

            long enclosingKingdomId = -1L;
            for (int i = 0; i < neighbours.Count; i++)
            {
                EnclosedZoneNeighbourFacts neighbour = neighbours[i];
                if (!neighbour.IsOwned || !neighbour.IsLive ||
                    neighbour.CityId < 0L || neighbour.KingdomId < 0L)
                    return -1L;

                if (enclosingKingdomId < 0L)
                    enclosingKingdomId = neighbour.KingdomId;
                else if (neighbour.KingdomId != enclosingKingdomId)
                    return -1L;
            }

            return SelectBestCity(zoneX, zoneY, neighbours);
        }

        public static long SelectComponentTargetCity(bool touchesWorldEdge,
            bool exceededZoneBudget, int componentCenterX,
            int componentCenterY,
            IReadOnlyList<EnclosedZoneNeighbourFacts> ownedBoundary)
        {
            if (touchesWorldEdge || exceededZoneBudget ||
                ownedBoundary == null || ownedBoundary.Count == 0)
                return -1L;

            long enclosingKingdomId = -1L;
            for (int i = 0; i < ownedBoundary.Count; i++)
            {
                EnclosedZoneNeighbourFacts neighbour = ownedBoundary[i];
                if (!neighbour.IsOwned || !neighbour.IsLive ||
                    neighbour.CityId < 0L || neighbour.KingdomId < 0L)
                    return -1L;

                if (enclosingKingdomId < 0L)
                    enclosingKingdomId = neighbour.KingdomId;
                else if (neighbour.KingdomId != enclosingKingdomId)
                    return -1L;
            }

            return SelectBestCity(componentCenterX, componentCenterY,
                ownedBoundary);
        }

        public static bool CanStartComponentScan(
            int pOwnedCardinalNeighbourCount)
        {
            return pOwnedCardinalNeighbourCount >= 2;
        }

        private static long SelectBestCity(int pCenterX, int pCenterY,
            IReadOnlyList<EnclosedZoneNeighbourFacts> pCandidates)
        {
            long bestCityId = -1L;
            int bestSharedSides = -1;
            long bestDistanceSquared = long.MaxValue;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                EnclosedZoneNeighbourFacts candidate = pCandidates[i];
                if (AppearedEarlier(pCandidates, i, candidate.CityId))
                    continue;

                int sharedSides = CountSharedSides(pCandidates,
                    candidate.CityId);
                long distanceSquared = DistanceSquared(pCenterX, pCenterY,
                    candidate.CityCenterX, candidate.CityCenterY);
                bool better = sharedSides > bestSharedSides ||
                              sharedSides == bestSharedSides &&
                              (distanceSquared < bestDistanceSquared ||
                               distanceSquared == bestDistanceSquared &&
                               (bestCityId < 0L ||
                                candidate.CityId < bestCityId));
                if (!better) continue;

                bestCityId = candidate.CityId;
                bestSharedSides = sharedSides;
                bestDistanceSquared = distanceSquared;
            }

            return bestCityId;
        }

        public static int ResolveDrainCount(int pPendingCount, int pBudget)
        {
            return Math.Min(Math.Max(0, pPendingCount),
                Math.Max(0, pBudget));
        }

        public static int ResolveSweepCount(int pTotalCount, int pCursor,
            int pBudget)
        {
            int remaining = Math.Max(0,
                pTotalCount - Math.Max(0, pCursor));
            return Math.Min(remaining, Math.Max(0, pBudget));
        }

        private static bool AppearedEarlier(
            IReadOnlyList<EnclosedZoneNeighbourFacts> pNeighbours,
            int pExclusiveEnd, long pCityId)
        {
            for (int i = 0; i < pExclusiveEnd; i++)
                if (pNeighbours[i].CityId == pCityId)
                    return true;
            return false;
        }

        private static int CountSharedSides(
            IReadOnlyList<EnclosedZoneNeighbourFacts> pNeighbours,
            long pCityId)
        {
            int count = 0;
            for (int i = 0; i < pNeighbours.Count; i++)
                if (pNeighbours[i].CityId == pCityId)
                    count++;
            return count;
        }

        private static long DistanceSquared(int pX1, int pY1, int pX2,
            int pY2)
        {
            long dx = (long)pX1 - pX2;
            long dy = (long)pY1 - pY2;
            return dx * dx + dy * dy;
        }
    }
}
