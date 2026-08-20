using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public static class CustomLocalGovernmentPresetRules
    {
        public const string CivilTemplateId = "minzhou";
        public const string MilitaryTemplateId = "junfu";

        public static IReadOnlyList<CustomLocalCourtTemplate>
            CreateBuiltInCatalog()
        {
            return new[]
            {
                CreateCivil(),
                CreateMilitary()
            };
        }

        public static CustomLocalCourtTemplate CreateCivil(string pId =
            CivilTemplateId)
        {
            string id = string.IsNullOrWhiteSpace(pId)
                ? CivilTemplateId
                : pId;
            var template = Template(id, "民州", "Civil Prefecture",
                CustomLocalCourtDefaultKind.CivilDefault);
            template.Offices.Add(Office(id + "_governor", "郡守",
                "Commandery Governor", 10, 0f, 0f, CourtSchoolId.Legalist,
                Effect(CustomCourtEffectId.CivilOrder,
                    CustomCourtEffectMode.AddFlat,
                    CustomCourtEffectScope.City, 5f)));
            template.Offices.Add(Office(id + "_changshi", "长史",
                "Chief Clerk", 20, -180f, 160f, CourtSchoolId.Historian,
                Effect(CustomCourtEffectId.CivilOrder,
                    CustomCourtEffectMode.AddFlat,
                    CustomCourtEffectScope.City, 3f)));
            template.Offices.Add(Office(id + "_sihu", "司户",
                "Household Officer", 30, 0f, 160f, CourtSchoolId.Legalist,
                Effect(CustomCourtEffectId.TaxIncome,
                    CustomCourtEffectMode.AddPercent,
                    CustomCourtEffectScope.City, 8f)));
            template.Offices.Add(Office(id + "_sicang", "司仓",
                "Granary Officer", 30, 180f, 160f, CourtSchoolId.Agrarian,
                Effect(CustomCourtEffectId.FoodProduction,
                    CustomCourtEffectMode.AddPercent,
                    CustomCourtEffectScope.City, 10f)));
            AddManagementEdges(template, id + "_governor");
            return template;
        }

        public static CustomLocalCourtTemplate CreateMilitary(string pId =
            MilitaryTemplateId)
        {
            string id = string.IsNullOrWhiteSpace(pId)
                ? MilitaryTemplateId
                : pId;
            var template = Template(id, "军府", "Military Government",
                CustomLocalCourtDefaultKind.MilitaryDefault);
            template.Offices.Add(Office(id + "_dudu", "都督",
                "Commander", 10, 0f, 0f, CourtSchoolId.Military,
                Effect(CustomCourtEffectId.ArmyMorale,
                    CustomCourtEffectMode.AddFlat,
                    CustomCourtEffectScope.Army, 5f), true));
            template.Offices.Add(Office(id + "_changshi", "长史",
                "Chief Clerk", 20, -180f, 160f, CourtSchoolId.Historian,
                Effect(CustomCourtEffectId.CivilOrder,
                    CustomCourtEffectMode.AddFlat,
                    CustomCourtEffectScope.City, 3f)));
            template.Offices.Add(Office(id + "_sima", "司马",
                "Marshal", 20, 0f, 160f, CourtSchoolId.Military,
                Effect(CustomCourtEffectId.ArmyMorale,
                    CustomCourtEffectMode.AddFlat,
                    CustomCourtEffectScope.Army, 3f), true));
            template.Offices.Add(Office(id + "_canjun", "参军",
                "Staff Officer", 30, 180f, 160f, CourtSchoolId.Military,
                Effect(CustomCourtEffectId.CourtInfluence,
                    CustomCourtEffectMode.AddFlat,
                    CustomCourtEffectScope.Court, 3f)));
            AddManagementEdges(template, id + "_dudu");
            return template;
        }

        private static CustomLocalCourtTemplate Template(string pId,
            string pChinese, string pEnglish,
            CustomLocalCourtDefaultKind pKind)
        {
            return new CustomLocalCourtTemplate
            {
                Id = pId,
                Name = new CustomCourtLocalizedText
                {
                    Chinese = pChinese,
                    English = pEnglish
                },
                DefaultKind = pKind
            };
        }

        private static CustomCourtOffice Office(string pId, string pChinese,
            string pEnglish, int pGrade, float pX, float pY,
            string pSchool, CustomCourtOfficeEffect pEffect,
            bool pMilitary = false)
        {
            return new CustomCourtOffice
            {
                Id = pId,
                Name = new CustomCourtLocalizedText
                {
                    Chinese = pChinese,
                    English = pEnglish
                },
                Layer = CourtOfficeLayer.City,
                Grade = pGrade,
                Slots = 1,
                MilitaryCapable = pMilitary,
                PreferredSchoolId = pSchool,
                Layout = new CustomCourtOfficeLayout { X = pX, Y = pY },
                Effects = new List<CustomCourtOfficeEffect> { pEffect }
            };
        }

        private static CustomCourtOfficeEffect Effect(CustomCourtEffectId pId,
            CustomCourtEffectMode pMode, CustomCourtEffectScope pScope,
            float pValue)
        {
            return new CustomCourtOfficeEffect
            {
                Id = pId,
                Mode = pMode,
                Scope = pScope,
                Value = pValue
            };
        }

        private static void AddManagementEdges(
            CustomLocalCourtTemplate pTemplate, string pRootId)
        {
            for (int i = 1; i < pTemplate.Offices.Count; i++)
                pTemplate.Edges.Add(new CustomCourtEdge
                {
                    FromOfficeId = pRootId,
                    ToOfficeId = pTemplate.Offices[i].Id,
                    Kind = CustomCourtEdgeKind.Management
                });
        }
    }
}
