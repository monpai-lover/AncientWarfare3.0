using System;
using System.Collections.Generic;
using AncientWarfare3.content.figures;

namespace AncientWarfare3.core.lineage
{
    public sealed class HistoricalFigureCardDeploymentFacts
    {
        public HistoricalFigureCardDeploymentFacts(bool pHasCity,
            bool pCityIsLiving, bool pActorIsAdult, bool pHasKingdom,
            bool pArchiveAvailable, bool pTransactionActive,
            string pHistoricalKingdomName, bool pCardOwned = true)
        {
            HasCity = pHasCity;
            CityIsLiving = pCityIsLiving;
            ActorIsAdult = pActorIsAdult;
            HasKingdom = pHasKingdom;
            ArchiveAvailable = pArchiveAvailable;
            TransactionActive = pTransactionActive;
            HistoricalKingdomName = pHistoricalKingdomName ?? "";
            CardOwned = pCardOwned;
        }

        public bool HasCity { get; }
        public bool CityIsLiving { get; }
        public bool ActorIsAdult { get; }
        public bool HasKingdom { get; }
        public bool ArchiveAvailable { get; }
        public bool TransactionActive { get; }
        public string HistoricalKingdomName { get; }
        public bool CardOwned { get; }
    }

    public static class HistoricalFigureCardDeploymentRules
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> ActiveDeploymentIds =
            new HashSet<string>(StringComparer.Ordinal);

        public static bool CanDeploy(HistoricalFigureCardDeploymentFacts pFacts)
        {
            if (pFacts == null || !pFacts.HasCity || !pFacts.CityIsLiving ||
                !pFacts.ActorIsAdult || !pFacts.HasKingdom ||
                !pFacts.ArchiveAvailable || pFacts.TransactionActive ||
                !pFacts.CardOwned ||
                string.IsNullOrWhiteSpace(pFacts.HistoricalKingdomName))
                return false;
            return !HistoricalFigureCardCatalog.HasGeographicPrefix(
                pFacts.HistoricalKingdomName);
        }

        public static bool TryBegin(string pDeploymentId)
        {
            if (string.IsNullOrWhiteSpace(pDeploymentId)) return false;
            lock (Gate) return ActiveDeploymentIds.Add(pDeploymentId.Trim());
        }

        public static void End(string pDeploymentId)
        {
            if (string.IsNullOrWhiteSpace(pDeploymentId)) return;
            lock (Gate) ActiveDeploymentIds.Remove(pDeploymentId.Trim());
        }

        public static bool IsActive(string pDeploymentId)
        {
            if (string.IsNullOrWhiteSpace(pDeploymentId)) return false;
            lock (Gate) return ActiveDeploymentIds.Contains(pDeploymentId.Trim());
        }
    }
}
