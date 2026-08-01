using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public static class CourtSchoolRegistry
    {
        private static readonly CourtSchoolDefinition[] Definitions =
        {
            School(CourtSchoolId.Ru, CourtTraitId.Ru, "ui/Icons/traits/iconRujia", "#B44335",
                D(.70f, .30f, .20f, .65f, .80f, .45f, .55f),
                CourtOfficeId.Chancellor, CourtOfficeId.Erudite,
                CourtOfficeId.Libu, CourtOfficeId.WestHighPriest),
            School(CourtSchoolId.Mohist, CourtTraitId.Mohist, "ui/Icons/traits/iconmo", "#3E6F61",
                D(.75f, .35f, .15f, .80f, .55f, .40f, .80f),
                CourtOfficeId.Gongbu, CourtOfficeId.Justice),
            School(CourtSchoolId.Dao, CourtTraitId.Dao, "ui/Icons/traits/icontao", "#62875A",
                D(.60f, .15f, .05f, .90f, .30f, .35f, .50f),
                CourtOfficeId.Erudite, CourtOfficeId.ImperialPhysician),
            School(CourtSchoolId.Legalist, CourtTraitId.Legalist, "ui/Icons/traits/iconfajia", "#7A4C3A",
                D(.40f, .65f, .75f, .20f, .95f, .55f, .60f),
                CourtOfficeId.Censor, CourtOfficeId.Justice,
                CourtOfficeId.Xingbu, CourtOfficeId.WestHighJustice),
            School(CourtSchoolId.Military, CourtTraitId.Military, "ui/Icons/traits/iconbinfa", "#963B2E",
                D(.25f, 1.00f, .95f, .05f, .65f, .20f, .45f),
                CourtOfficeId.Marshal, CourtOfficeId.Bingbu, "general",
                CourtOfficeId.WestFieldGeneral,
                CourtOfficeId.WestMarshal),
            School(CourtSchoolId.Diplomat, CourtTraitId.Diplomat, "ui/Icons/traits/iconzonheng", "#2F6F9F",
                D(.40f, .35f, .25f, 1.00f, .45f, .70f, .55f),
                CourtOfficeId.Chancellor, CourtOfficeId.Zhongshu,
                CourtOfficeId.WestExecutive,
                CourtOfficeId.WestRoyalChamberlain),
            School(CourtSchoolId.Agrarian, CourtTraitId.Agrarian, "ui/Icons/traits/iconnong", "#8A7A2F",
                D(1.00f, .20f, .10f, .60f, .55f, .35f, .55f),
                CourtOfficeId.Steward, CourtOfficeId.Hubu, CourtOfficeId.GranaryOfficer,
                CourtOfficeId.Governor, CourtOfficeId.WestMayor,
                CourtOfficeId.WestPalaceSteward,
                CourtOfficeId.WestCount),
            School(CourtSchoolId.YinYang, CourtTraitId.YinYang, "ui/Icons/traits/iconyingyang", "#656B78",
                D(.55f, .40f, .35f, .60f, .65f, .40f, .75f),
                CourtOfficeId.ImperialAstrologer, CourtOfficeId.Ribu),
            School(CourtSchoolId.Logician, CourtTraitId.Logician, "ui/Icons/traits/iconmingjia", "#B06D32",
                D(.45f, .30f, .25f, .65f, .55f, .55f, .85f),
                CourtOfficeId.Menxia, CourtOfficeId.Libu, CourtOfficeId.Censor),
            School(CourtSchoolId.Medical, CourtTraitId.Medical, "ui/Icons/traits/iconoisha", "#4B8C7B",
                D(.95f, .10f, .05f, .80f, .45f, .35f, .75f),
                CourtOfficeId.ImperialPhysician),
            School(CourtSchoolId.Syncretist, CourtTraitId.Syncretist, "ui/Icons/traits/iconzajia", "#7B6751",
                D(.60f, .50f, .45f, .65f, .70f, .55f, .70f),
                CourtOfficeId.Shangshu, CourtOfficeId.Zhongshu),
            School(CourtSchoolId.Merchant, CourtTraitId.Merchant, "ui/Icons/traits/iconshangjia", "#B58A2E",
                D(.70f, .25f, .20f, .65f, .40f, 1.00f, .65f),
                CourtOfficeId.Hubu, CourtOfficeId.Governor,
                CourtOfficeId.WestTreasurer),
            School(CourtSchoolId.Craftsman, CourtTraitId.Craftsman, "ui/Icons/traits/icongongjia", "#68615A",
                D(.80f, .45f, .35f, .40f, .55f, .70f, .95f),
                CourtOfficeId.Gongbu),
            School(CourtSchoolId.Historian, CourtTraitId.Historian, "ui/Icons/traits/iconshijia", "#6D4C6E",
                D(.65f, .25f, .15f, .75f, .85f, .40f, .80f),
                CourtOfficeId.Erudite, CourtOfficeId.Libu,
                CourtOfficeId.WestSenateElder,
                CourtOfficeId.WestSecretary)
        };

        private static readonly Dictionary<string, CourtSchoolDefinition> ById =
            Definitions.ToDictionary(p => p.Id, StringComparer.Ordinal);

        public static IReadOnlyList<CourtSchoolDefinition> All => Definitions;

        public static CourtSchoolDefinition Find(string pId)
        {
            return !string.IsNullOrEmpty(pId) && ById.TryGetValue(pId, out CourtSchoolDefinition value)
                ? value
                : null;
        }

        public static string[] Validate()
        {
            var errors = new List<string>();
            if (Definitions.Length != 14) errors.Add("expected 14 fixed schools");
            foreach (CourtSchoolDefinition definition in Definitions)
            {
                if (!definition.IsComplete) errors.Add("incomplete school: " + definition.Id);
                if (Definitions.Count(p => p.Id == definition.Id) != 1)
                    errors.Add("duplicate school: " + definition.Id);
            }
            return errors.ToArray();
        }

        private static CourtSchoolDefinition School(string pId, string pTraitId, string pIcon,
            string pColor, CourtSchoolDirection pDirection, params string[] pOffices)
        {
            return new CourtSchoolDefinition(pId, pTraitId, pIcon, pColor, pDirection, pOffices);
        }

        private static CourtSchoolDirection D(float livelihood, float war, float aggression,
            float peace, float order, float commerce, float technology)
        {
            return new CourtSchoolDirection(livelihood, war, aggression, peace, order, commerce,
                technology);
        }
    }
}
