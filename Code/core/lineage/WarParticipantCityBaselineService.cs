using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class WarParticipantCityBaselineService
    {
        public static void RegisterExistingParticipants(War pWar)
        {
            if (pWar?.data == null) return;
            try
            {
                foreach (Kingdom kingdom in pWar.getAttackers())
                    RegisterParticipant(pWar, kingdom);
                foreach (Kingdom kingdom in pWar.getDefenders())
                    RegisterParticipant(pWar, kingdom);
            }
            catch { }
        }

        public static int RegisterParticipant(War pWar, Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null) return 1;
            string key = WarParticipantCityBaselineRules.Key(pKingdom.id);
            pWar.data.get(key, out int recorded, 0);
            if (recorded > 0)
                return WarParticipantCityBaselineRules.ResolveRemainingCityCount(
                    recorded, pLiveCount: 0,
                    pPermanentOwnershipChanged: false);

            int cityCount = 0;
            try { cityCount = pKingdom.countCities(); }
            catch { }
            int normalized = WarParticipantCityBaselineRules.
                ResolveRemainingCityCount(recorded, cityCount,
                    pPermanentOwnershipChanged: false);
            pWar.data.set(key, normalized);
            return normalized;
        }

        public static int GetOrRegister(War pWar, Kingdom pKingdom)
        {
            return RegisterParticipant(pWar, pKingdom);
        }

        public static void OnCityOwnerChanged(City pCity,
            Kingdom pOldOwner, Kingdom pNewOwner)
        {
            if (pCity?.data == null || pOldOwner == pNewOwner) return;
            WarRemainingTerritoryOrchestration.ApplyPermanentTransfer(
                SnapshotWars(pOldOwner), SnapshotWars(pNewOwner),
                pOldOwner, pNewOwner,
                war => war?.data?.id ?? -1L,
                IsActive,
                IsParticipant,
                UpdateParticipant);
        }

        internal static int SetRemainingCityCount(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null) return 1;
            int cityCount = 0;
            try { cityCount = pKingdom.countCities(); }
            catch { }
            pWar.data.get(WarParticipantCityBaselineRules.Key(pKingdom.id),
                out int recorded, 0);
            int normalized = WarParticipantCityBaselineRules.
                ResolveRemainingCityCount(recorded, cityCount,
                    pPermanentOwnershipChanged: true);
            pWar.data.set(WarParticipantCityBaselineRules.Key(pKingdom.id),
                normalized);
            return normalized;
        }

        private static IReadOnlyList<War> SnapshotWars(Kingdom pOwner)
        {
            if (pOwner?.data == null) return Array.Empty<War>();
            var result = new List<War>();
            try
            {
                foreach (War war in pOwner.getWars()) result.Add(war);
            }
            catch { }
            return result;
        }

        private static bool IsActive(War pWar)
        {
            if (pWar?.data == null) return false;
            try { return !pWar.hasEnded(); }
            catch { return false; }
        }

        private static void UpdateParticipant(War pWar, Kingdom pOwner)
        {
            SetRemainingCityCount(pWar, pOwner);
            WarScoreService.ScheduleParticipantCityControlRevaluation(
                pWar, pOwner);
        }

        private static bool IsParticipant(War pWar, Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null) return false;
            try
            {
                return pWar.isAttacker(pKingdom) || pWar.isDefender(pKingdom);
            }
            catch { return false; }
        }
    }
}
