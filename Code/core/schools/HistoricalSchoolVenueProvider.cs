namespace AncientWarfare3.core.schools
{
    internal interface IHistoricalSchoolVenueSource
    {
        bool TryFind(
            City pCity,
            Actor pActor,
            string pSchoolId,
            HistoricalSchoolVenueKind pKind,
            out WorldTile pPrimary,
            out WorldTile pSecondary,
            out Building pAcademy);
    }

    internal static class HistoricalSchoolVenueProvider
    {
        private static readonly IHistoricalSchoolVenueSource[] Sources =
        {
            new EmptyAcademyVenueSource(),
            new PublicCityVenueSource(),
            new LocalVenueSource()
        };

        public static void SetAcademySource(IHistoricalSchoolVenueSource pSource)
        {
            Sources[0] = pSource ?? new EmptyAcademyVenueSource();
        }

        public static bool TryFind(
            City pCity,
            Actor pActor,
            string pSchoolId,
            HistoricalSchoolVenueKind pKind,
            out WorldTile pPrimary,
            out WorldTile pSecondary,
            out Building pAcademy)
        {
            if (HistoricalSchoolVenueRules.RequiresAcademy(pKind))
                return Sources[0].TryFind(
                    pCity, pActor, pSchoolId, pKind,
                    out pPrimary, out pSecondary, out pAcademy);
            for (int i = 0; i < Sources.Length; i++)
                if (Sources[i].TryFind(
                        pCity, pActor, pSchoolId, pKind,
                        out pPrimary, out pSecondary, out pAcademy)) return true;
            pPrimary = null;
            pSecondary = null;
            pAcademy = null;
            return false;
        }

        private sealed class EmptyAcademyVenueSource : IHistoricalSchoolVenueSource
        {
            public bool TryFind(
                City pCity,
                Actor pActor,
                string pSchoolId,
                HistoricalSchoolVenueKind pKind,
                out WorldTile pPrimary,
                out WorldTile pSecondary,
                out Building pAcademy)
            {
                pPrimary = null;
                pSecondary = null;
                pAcademy = null;
                return false;
            }
        }

        private sealed class PublicCityVenueSource : IHistoricalSchoolVenueSource
        {
            public bool TryFind(
                City pCity,
                Actor pActor,
                string pSchoolId,
                HistoricalSchoolVenueKind pKind,
                out WorldTile pPrimary,
                out WorldTile pSecondary,
                out Building pAcademy)
            {
                pAcademy = null;
                return HistoricalSchoolVenueService.TryFindPublicVenue(
                    pCity, pActor, pSchoolId, pKind,
                    out pPrimary, out pSecondary);
            }
        }

        private sealed class LocalVenueSource : IHistoricalSchoolVenueSource
        {
            public bool TryFind(
                City pCity,
                Actor pActor,
                string pSchoolId,
                HistoricalSchoolVenueKind pKind,
                out WorldTile pPrimary,
                out WorldTile pSecondary,
                out Building pAcademy)
            {
                pAcademy = null;
                return HistoricalSchoolVenueService.TryFindLocalVenue(
                    pCity, pActor, pSchoolId, pKind,
                    out pPrimary, out pSecondary);
            }
        }
    }
}
