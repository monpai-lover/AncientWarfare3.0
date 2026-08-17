using System;
using System.Linq;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    public static partial class CourtProfileRegistry
    {
        public static ICourtProfile For(Kingdom pKingdom)
        {
            return For(KingdomPolicyService.GetPolicyProfile(pKingdom));
        }

        public static string[] CentralOfficeIdsFor(Kingdom pKingdom)
        {
            return OfficeIdsForLayer(pKingdom, CourtOfficeLayer.Central);
        }

        public static string[] OfficeIdsForLayer(Kingdom pKingdom,
            string pLayer)
        {
            ICourtProfile profile = For(pKingdom);
            if (profile == null || string.IsNullOrEmpty(pLayer))
                return Array.Empty<string>();
            string institution = CourtInstitutionService.GetInstitution(
                pKingdom);
            if (CustomCourtRuntime.HasInstance(pKingdom))
                return CustomCourtRuntime.Resolver.ResolveGraph(
                    CustomCourtRuntime.KingdomKey(pKingdom), profile,
                    institution).Where(p => p?.Layer == pLayer)
                    .Select(p => p.Id).ToArray();
            return profile.OfficeIdsForInstitution(institution)
                .Where(p => profile.FindOffice(p)?.Layer == pLayer)
                .ToArray();
        }

        public static CourtOfficeDefinition FindOffice(Kingdom pKingdom,
            string pOfficeId)
        {
            if (CustomCourtRuntime.HasInstance(pKingdom))
                return CustomCourtRuntime.Resolver.Resolve(
                    CustomCourtRuntime.KingdomKey(pKingdom), For(pKingdom),
                    CourtInstitutionService.GetInstitution(pKingdom),
                    pOfficeId);
            return For(pKingdom)?.FindOffice(pOfficeId);
        }

        public static CourtOfficeDefinition FindOfficeAcrossProfiles(
            string pOfficeId)
        {
            return Xia.FindOffice(pOfficeId) ?? Western.FindOffice(pOfficeId);
        }

        public static string PreferredSchoolFor(Kingdom pKingdom,
            string pOfficeId)
        {
            return FindOffice(pKingdom, pOfficeId)?.PreferredSchoolId ??
                   CourtSchoolId.None;
        }

        public static bool IsOfficeAvailableFor(Kingdom pKingdom,
            string pOfficeId, string pLayer = null)
        {
            CourtOfficeDefinition office = FindOffice(pKingdom, pOfficeId);
            if (office == null || pLayer != null &&
                !string.Equals(office.Layer, pLayer,
                    StringComparison.Ordinal)) return false;
            return CustomCourtRuntime.HasInstance(pKingdom) ||
                office.AvailableIn(CourtInstitutionService.GetInstitution(
                    pKingdom));
        }

        public static bool IsMilitaryOfficeAcrossProfiles(string pOfficeId)
        {
            return FindOfficeAcrossProfiles(pOfficeId)?.MilitaryCapable == true;
        }
    }
}
