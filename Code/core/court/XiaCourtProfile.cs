using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public sealed class XiaCourtProfile : ICourtProfile
    {
        private static readonly string[] ZhouOffices =
            CourtTierRules.CentralOfficesForTier(CourtTier.EasternZhou);
        private static readonly string[] HanOffices =
            CourtTierRules.CentralOfficesForTier(CourtTier.SanGongJiuQing);
        private static readonly string[] DepartmentOffices =
            CourtTierRules.CentralOfficesForTier(CourtTier.SanShengLiuBu);
        private static readonly CourtOfficeDefinition[] Definitions =
            BuildDefinitions();
        private static readonly Dictionary<string, CourtOfficeDefinition> ById =
            Definitions.ToDictionary(p => p.Id, StringComparer.Ordinal);

        public CourtProfileId Id => CourtProfileId.Xia;
        public string DefaultInstitutionId => CourtInstitutionId.Zhou;
        public IReadOnlyList<CourtOfficeDefinition> Offices => Definitions;

        public CourtOfficeDefinition FindOffice(string officeId)
        {
            return !string.IsNullOrEmpty(officeId) &&
                   ById.TryGetValue(officeId, out CourtOfficeDefinition value)
                ? value
                : null;
        }

        public IReadOnlyList<string> OfficeIdsForInstitution(
            string institutionId)
        {
            switch (institutionId ?? string.Empty)
            {
                case CourtInstitutionId.Han:
                    return HanOffices;
                case CourtInstitutionId.Tang:
                case CourtInstitutionId.Song:
                    return DepartmentOffices;
                default:
                    return ZhouOffices;
            }
        }

        public string ResolveInstitution(bool officeSystemUnlocked,
            bool electiveAdopted, bool feudalAdopted,
            bool royalDirectAdopted)
        {
            if (officeSystemUnlocked && electiveAdopted && feudalAdopted)
                return CourtInstitutionId.Song;
            if (officeSystemUnlocked && electiveAdopted)
                return CourtInstitutionId.Tang;
            return officeSystemUnlocked
                ? CourtInstitutionId.Han
                : CourtInstitutionId.Zhou;
        }

        private static CourtOfficeDefinition[] BuildDefinitions()
        {
            string[] all = ZhouOffices.Concat(HanOffices)
                .Concat(DepartmentOffices).Distinct(StringComparer.Ordinal)
                .ToArray();
            var result = new CourtOfficeDefinition[all.Length];
            for (int index = 0; index < all.Length; index++)
            {
                string id = all[index];
                result[index] = new CourtOfficeDefinition(id,
                    CourtOfficeLayer.Central, Grade(id),
                    CourtTierRules.PreferredSchoolForOffice(id),
                    "aw_court_office_" + id, IsMilitary(id),
                    Memberships(id));
            }
            return result;
        }

        private static string[] Memberships(string id)
        {
            var result = new List<string>(4);
            if (ZhouOffices.Contains(id, StringComparer.Ordinal))
                result.Add(CourtInstitutionId.Zhou);
            if (HanOffices.Contains(id, StringComparer.Ordinal))
                result.Add(CourtInstitutionId.Han);
            if (DepartmentOffices.Contains(id, StringComparer.Ordinal))
            {
                result.Add(CourtInstitutionId.Tang);
                result.Add(CourtInstitutionId.Song);
            }
            return result.ToArray();
        }

        private static int Grade(string id)
        {
            switch (id)
            {
                case CourtOfficeId.TaiZai:
                case CourtOfficeId.SiTu:
                case CourtOfficeId.ZongBo:
                case CourtOfficeId.SiMa:
                case CourtOfficeId.SiKou:
                case CourtOfficeId.SiKong:
                case CourtOfficeId.Chancellor:
                case CourtOfficeId.Marshal:
                case CourtOfficeId.Censor:
                case CourtOfficeId.Zhongshu:
                case CourtOfficeId.Menxia:
                case CourtOfficeId.Shangshu:
                    return 10;
                case CourtOfficeId.Justice:
                case CourtOfficeId.Steward:
                case CourtOfficeId.Erudite:
                case CourtOfficeId.Libu:
                case CourtOfficeId.Hubu:
                case CourtOfficeId.Ribu:
                case CourtOfficeId.Bingbu:
                case CourtOfficeId.Xingbu:
                case CourtOfficeId.Gongbu:
                    return 20;
                default:
                    return 30;
            }
        }

        private static bool IsMilitary(string id)
        {
            return id == CourtOfficeId.SiMa ||
                   id == CourtOfficeId.Marshal ||
                   id == CourtOfficeId.Bingbu;
        }
    }
}
