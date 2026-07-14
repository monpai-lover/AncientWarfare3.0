namespace AncientWarfare3.core.schools
{
    internal enum HistoricalSchoolVenueKind
    {
        Lecture,
        Debate,
        TravelArrival,
        IdleRoam
    }

    internal interface IHistoricalSchoolVenueSource
    {
        bool TryFind(
            City pCity,
            Actor pActor,
            string pSchoolId,
            HistoricalSchoolVenueKind pKind,
            out WorldTile pPrimary,
            out WorldTile pSecondary);
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
            out WorldTile pSecondary)
        {
            for (int i = 0; i < Sources.Length; i++)
                if (Sources[i].TryFind(
                        pCity, pActor, pSchoolId, pKind,
                        out pPrimary, out pSecondary)) return true;
            pPrimary = null;
            pSecondary = null;
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
                out WorldTile pSecondary)
            {
                pPrimary = null;
                pSecondary = null;
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
                out WorldTile pSecondary)
            {
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
                out WorldTile pSecondary)
            {
                return HistoricalSchoolVenueService.TryFindLocalVenue(
                    pCity, pActor, pSchoolId, pKind,
                    out pPrimary, out pSecondary);
            }
        }
    }
}
