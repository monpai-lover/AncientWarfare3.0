using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyStrategicIndexService
    {
        private static readonly ArmyStrategicIdentityIndex Index =
            new ArmyStrategicIdentityIndex();

        public static void OnArmyRegistered(Army pArmy)
        {
            RefreshArmy(pArmy);
            KingdomWarDirectorService.QueueArmyChanged(SafeKingdom(pArmy));
        }

        public static void OnArmyKingdomChanged(Army pArmy)
        {
            Kingdom previousKingdom = IndexedKingdom(pArmy);
            RefreshArmy(pArmy);
            Kingdom currentKingdom = SafeKingdom(pArmy);
            KingdomWarDirectorService.QueueArmyChanged(previousKingdom);
            if (currentKingdom != previousKingdom)
                KingdomWarDirectorService.QueueArmyChanged(currentKingdom);
        }

        public static void OnArmyRosterChanged(Army pArmy)
        {
            RefreshArmy(pArmy);
            KingdomWarDirectorService.QueueArmyChanged(SafeKingdom(pArmy));
        }

        public static void OnArmyDisposed(Army pArmy)
        {
            if (pArmy == null) return;
            Kingdom kingdom = SafeKingdom(pArmy);
            CoalitionWarTaskService.OnArmyInvalidated(pArmy.id);
            Index.Remove(pArmy.id);
            ArmyFieldIndexService.OnArmyDisposed(pArmy);
            KingdomWarDirectorService.QueueArmyChanged(kingdom);
        }

        public static ArmyStrategicIdCursor CreateSnapshotCursor(
            Kingdom pKingdom)
        {
            return Index.CreateCursor(pKingdom?.data == null
                ? -1L
                : pKingdom.id);
        }

        public static int CopyArmyIdsAfter(Kingdom pKingdom,
            long pAfterArmyId, int pMaximum, List<long> pDestination,
            out bool pComplete)
        {
            return Index.CopyArmyIdsAfter(pKingdom?.data == null
                ? -1L
                : pKingdom.id, pAfterArmyId, pMaximum, pDestination,
                out pComplete);
        }

        public static Army ResolveIndexedArmy(long pArmyId,
            long pKingdomId)
        {
            if (!Index.TryGetKingdomId(pArmyId, out long indexedKingdomId) ||
                indexedKingdomId != pKingdomId) return null;
            Army army;
            try { army = World.world?.armies?.get(pArmyId); }
            catch { return null; }
            if (army?.data == null || !SafeAlive(army))
            {
                Index.Remove(pArmyId);
                return null;
            }
            return army;
        }

        public static void RebuildRuntime()
        {
            Index.Clear();
            ArmyFieldIndexService.ClearRuntime();
            if (World.world?.armies == null) return;
            foreach (Army army in World.world.armies)
                RefreshArmy(army);
        }

        public static void ClearRuntime()
        {
            Index.Clear();
            ArmyFieldIndexService.ClearRuntime();
        }

        private static void RefreshArmy(Army pArmy)
        {
            if (pArmy?.data == null || !SafeAlive(pArmy))
            {
                if (pArmy != null) Index.Remove(pArmy.id);
                ArmyFieldIndexService.OnArmyChanged(pArmy);
                return;
            }
            Kingdom kingdom;
            try { kingdom = pArmy.getKingdom(); }
            catch { kingdom = null; }
            if (kingdom?.data == null || kingdom.isRekt())
            {
                Index.Remove(pArmy.id);
                ArmyFieldIndexService.OnArmyChanged(pArmy);
                return;
            }
            Index.Register(pArmy.id, kingdom.id);
            ArmyFieldIndexService.OnArmyChanged(pArmy);
        }

        private static bool SafeAlive(Army pArmy)
        {
            try { return pArmy != null && pArmy.isAlive(); }
            catch { return false; }
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }

        private static Kingdom IndexedKingdom(Army pArmy)
        {
            if (pArmy == null ||
                !Index.TryGetKingdomId(pArmy.id, out long kingdomId))
                return null;
            try { return World.world?.kingdoms?.get(kingdomId); }
            catch { return null; }
        }
    }
}
