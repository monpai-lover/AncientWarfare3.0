namespace AncientWarfare3.core.lineage
{
    // Compatibility facade retained for callers from the army and guard
    // patches. Vanilla army membership and capacity are authoritative.
    internal static class KingdomMilitaryReadinessService
    {
        public static bool HasReadyStandingCore(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && !pKingdom.isRekt();
            }
            catch { return false; }
        }

        public static void ObserveCity(City pCity) { }
        public static void MarkCityDirty(City pCity) { }
        public static void MarkArmyCitiesDirty(Actor pActor,
            Army pPreviousArmy, Army pCurrentArmy) { }
        public static void MarkOrdinaryArmyActorDirty(Actor pActor) { }
        public static void OnCityKingdomChanged(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom) { }
        public static void OnCityDestroyed(City pCity) { }
        public static void OnKingdomDestroying(Kingdom pKingdom) { }
        public static void RebuildRuntime() { }
        public static void ClearRuntime() { }
    }
}
