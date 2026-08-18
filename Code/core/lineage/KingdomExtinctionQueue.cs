namespace AncientWarfare3.core.lineage
{
    internal static class KingdomExtinctionQueue
    {
        private static readonly System.Collections.Generic.HashSet<long>
            VerifiedForVanillaRemoval = new();

        internal static void ClearRuntime()
        {
            VerifiedForVanillaRemoval.Clear();
        }

        internal static bool IsVerifiedForVanillaRemoval(
            Kingdom pKingdom,
            int pLiveCityCount)
        {
            if (pKingdom?.data == null) return false;
            if (pLiveCityCount > 0)
            {
                VerifiedForVanillaRemoval.Remove(pKingdom.id);
                return false;
            }
            return VerifiedForVanillaRemoval.Contains(pKingdom.id);
        }

        public static void Schedule(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            long kingdomId = pKingdom.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "zero_city_extinction", kingdomId),
                DeferredWorkClass.Runtime,
                () => Verify(kingdomId));
        }

        private static void Verify(long pKingdomId)
        {
            KingdomManager manager = World.world?.kingdoms;
            if (manager == null) return;
            Kingdom kingdom;
            try { kingdom = manager.get(pKingdomId); }
            catch { return; }
            if (kingdom?.data == null || kingdom.isRekt()) return;
            if (KingdomExtinctionRules.ShouldDeferRemovalVerification(
                    cityIndexStable: !manager.hasDirtyCities(),
                    actorKingdomIndexStable: !manager.isUnitsDirty()))
            {
                Schedule(kingdom);
                return;
            }
            bool hasCities;
            try { hasCities = kingdom.countCities() > 0; }
            catch { hasCities = kingdom.hasCities(); }
            if (hasCities) return;

            SuccessionDisputeService.OnZeroCityKingdom(kingdom);
            if (kingdom?.data == null || kingdom.isRekt()) return;
            try
            {
                if (kingdom.countCities() > 0) return;
            }
            catch { }
            if (!WarRosterIntegrityService.TryDetachFromActiveWars(kingdom))
            {
                Schedule(kingdom);
                return;
            }
            MarkVerifiedForVanillaRemoval(kingdom);
        }

        private static void MarkVerifiedForVanillaRemoval(Kingdom pKingdom)
        {
            if (pKingdom?.data != null)
                VerifiedForVanillaRemoval.Add(pKingdom.id);
        }
    }
}
