namespace AncientWarfare3.core.lineage
{
    internal readonly struct GuiyiSpawnFacts
    {
        internal GuiyiSpawnFacts(bool cityAlive, bool cityIntegrated,
            bool occupierIntegrated, bool foreignOccupation, int loyalty,
            bool occupierHasGuiyi, bool strongholdAvailable,
            bool residentAvailable)
        {
            CityAlive = cityAlive;
            CityIntegrated = cityIntegrated;
            OccupierIntegrated = occupierIntegrated;
            ForeignOccupation = foreignOccupation;
            Loyalty = loyalty;
            OccupierHasGuiyi = occupierHasGuiyi;
            StrongholdAvailable = strongholdAvailable;
            ResidentAvailable = residentAvailable;
        }

        internal bool CityAlive { get; }
        internal bool CityIntegrated { get; }
        internal bool OccupierIntegrated { get; }
        internal bool ForeignOccupation { get; }
        internal int Loyalty { get; }
        internal bool OccupierHasGuiyi { get; }
        internal bool StrongholdAvailable { get; }
        internal bool ResidentAvailable { get; }
    }

    internal enum GuiyiRestorationObjective
    {
        None,
        ReturnToLivingKingdom,
        RestoreExtinctKingdom
    }

    internal static class PeasantRebelGuiyiRules
    {
        internal const string RouteSubtype = "guiyi";
        internal const int LoyaltyThreshold = -50;

        internal static bool CanSpawn(GuiyiSpawnFacts pFacts)
        {
            return pFacts.CityAlive && pFacts.CityIntegrated &&
                   !pFacts.OccupierIntegrated &&
                   pFacts.ForeignOccupation &&
                   pFacts.Loyalty < LoyaltyThreshold &&
                   !pFacts.OccupierHasGuiyi &&
                   pFacts.StrongholdAvailable &&
                   pFacts.ResidentAvailable;
        }

        internal static GuiyiRestorationObjective ResolveObjective(
            bool originalKingdomAlive, bool originalIdentityArchived)
        {
            if (originalKingdomAlive)
                return GuiyiRestorationObjective.ReturnToLivingKingdom;
            return originalIdentityArchived
                ? GuiyiRestorationObjective.RestoreExtinctKingdom
                : GuiyiRestorationObjective.None;
        }

        internal static bool ShouldBeginRestoration(int guiYiStrength,
            int occupierStrength, GuiyiRestorationObjective objective)
        {
            if (objective == GuiyiRestorationObjective.None ||
                guiYiStrength <= 0) return false;
            if (occupierStrength <= 0) return true;
            return (long)guiYiStrength * 2L >
                   (long)occupierStrength * 3L;
        }
    }
}
