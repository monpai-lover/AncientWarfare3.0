namespace AncientWarfare3.core.schools
{
    internal static class SchoolLandmarkService
    {
        private static readonly System.Collections.Generic.Dictionary<long,
            SchoolInstitutionReadModel> Cache =
            new System.Collections.Generic.Dictionary<long, SchoolInstitutionReadModel>();
        private static readonly System.Collections.Generic.HashSet<long> Known =
            new System.Collections.Generic.HashSet<long>();

        public static SchoolInstitutionReadModel Leading(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return null;
            long cityId = pCity.data.id;
            if (Known.Contains(cityId))
                return Cache.TryGetValue(cityId, out SchoolInstitutionReadModel cached)
                    ? cached
                    : null;
            SchoolInstitutionReadModel result = HistoricalSchoolStore.LoadLeadingInstitution(cityId);
            Known.Add(cityId);
            if (result != null) Cache[cityId] = result;
            return result;
        }

        public static void MarkDirty(long pCityId)
        {
            if (pCityId < 0) return;
            Known.Remove(pCityId);
            Cache.Remove(pCityId);
        }

        public static void Clear()
        {
            Known.Clear();
            Cache.Clear();
        }

        public static string Describe(City pCity)
        {
            SchoolInstitutionReadModel institution = Leading(pCity);
            if (institution == null) return "Academic landmark: none";
            return "Academic landmark: " + (institution.InstitutionType ?? "institution") +
                   " (Lv." + institution.Level + ")";
        }
    }
}
