using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtRuntime
    {
        private static readonly IReadOnlyList<CustomLocalCourtTemplate>
            BuiltInLocalTemplates = CustomLocalGovernmentPresetRules
                .CreateBuiltInCatalog();

        public static readonly CustomCourtInstanceService Instances =
            new CustomCourtInstanceService();

        public static readonly CourtDefinitionResolver Resolver =
            new CourtDefinitionResolver(Instances);

        public static string KingdomKey(Kingdom kingdom)
        {
            return kingdom == null
                ? string.Empty
                : kingdom.id.ToString(CultureInfo.InvariantCulture);
        }

        public static bool HasInstance(Kingdom kingdom)
        {
            CustomCourtInstance instance;
            return TryGetInstance(kingdom, out instance);
        }

        public static bool TryGetInstance(Kingdom kingdom,
            out CustomCourtInstance instance)
        {
            instance = null;
            if (kingdom?.data == null) return false;
            string key = KingdomKey(kingdom);
            if (Instances.TryGet(key, out instance)) return true;
            kingdom.data.get(LineageKeys.CUSTOM_COURT_INSTANCE_SNAPSHOT,
                out string raw, string.Empty);
            if (!CustomCourtInstanceCodec.TryImport(raw, out instance) ||
                !string.Equals(instance.KingdomId, key,
                    System.StringComparison.Ordinal))
            {
                instance = null;
                return false;
            }
            return Instances.Save(instance);
        }

        public static bool TryGetSnapshot(Kingdom kingdom,
            out CustomCourtTemplate snapshot)
        {
            snapshot = null;
            CustomCourtInstance instance;
            if (!TryGetInstance(kingdom, out instance)) return false;
            snapshot = instance?.ResolvedSnapshot;
            return snapshot != null;
        }

        public static bool TryGetLocalTemplate(Kingdom pKingdom, City pCity,
            out CustomLocalCourtTemplate pTemplate)
        {
            pTemplate = null;
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom) return false;
            IReadOnlyList<CustomLocalCourtTemplate> templates =
                ResolvedLocalTemplates(pKingdom);

            pCity.data.get(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                out string persistedId, string.Empty);
            pCity.data.get(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_MANUAL,
                out bool manual, false);
            CustomLocalGovernmentDefaultKind automaticKind =
                CustomLocalGovernmentRules.SelectDefault(manual,
                    CustomLocalGovernmentCityService.HasForeignLandBorder(
                        pCity, pKingdom),
                    CityEconomyService.IsFrontierMilitary(pKingdom, pCity));
            bool military = automaticKind ==
                CustomLocalGovernmentDefaultKind.Military;
            string resolvedId = CustomLocalCourtTemplateRules.ResolveTemplateId(
                templates, persistedId, manual, military);
            pTemplate = templates.FirstOrDefault(template =>
                template != null && string.Equals(template.Id, resolvedId,
                    System.StringComparison.Ordinal));
            if (pTemplate == null) return false;

            bool validManual = manual && string.Equals(persistedId,
                resolvedId, System.StringComparison.Ordinal);
            if (!string.Equals(persistedId, resolvedId,
                    System.StringComparison.Ordinal))
                pCity.data.set(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                    resolvedId);
            if (manual != validManual)
                pCity.data.set(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_MANUAL,
                    validManual);
            return true;
        }

        public static bool TrySetLocalTemplate(Kingdom pKingdom, City pCity,
            string pTemplateId, bool pManual)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom || string.IsNullOrWhiteSpace(
                    pTemplateId)) return false;
            IReadOnlyList<CustomLocalCourtTemplate> templates =
                ResolvedLocalTemplates(pKingdom);
            if (!templates.Any(template => template != null &&
                    string.Equals(template.Id, pTemplateId,
                        System.StringComparison.Ordinal))) return false;
            pCity.data.set(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                pTemplateId);
            pCity.data.set(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_MANUAL,
                pManual);
            return true;
        }

        public static string DisplayName(Kingdom kingdom, string fallback)
        {
            CustomCourtTemplate snapshot;
            return TryGetSnapshot(kingdom, out snapshot)
                ? LocalizedName(snapshot.Name, fallback)
                : fallback ?? string.Empty;
        }

        public static string OfficeDisplayName(Kingdom kingdom,
            string officeId)
        {
            CustomCourtOffice office = null;
            if (TryGetSnapshot(kingdom, out CustomCourtTemplate snapshot))
                office = CustomCourtTemplateRules.FindOffice(snapshot,
                    officeId);
            if (office == null)
                foreach (CustomLocalCourtTemplate local in
                         ResolvedLocalTemplates(kingdom))
                {
                    office = (local?.Offices ??
                        new List<CustomCourtOffice>()).FirstOrDefault(item =>
                        item != null && string.Equals(item.Id, officeId,
                            System.StringComparison.Ordinal));
                    if (office != null) break;
                }
            return office == null
                ? string.Empty
                : LocalizedName(office.Name, office.Id);
        }

        internal static void RegionalTitles(Kingdom pKingdom,
            out string pRegionTitle, out string pGovernorTitle)
        {
            RegionalTitles(pKingdom, out pRegionTitle, out pGovernorTitle,
                out _);
        }

        internal static void RegionalTitles(Kingdom pKingdom,
            out string pRegionTitle, out string pGovernorTitle,
            out string pLocalLevelTitle)
        {
            pRegionTitle = "郡";
            pGovernorTitle = "郡守";
            pLocalLevelTitle = "州";
            if (!TryGetSnapshot(pKingdom, out CustomCourtTemplate snapshot) ||
                snapshot.RegionalGovernmentLayer == null) return;
            pRegionTitle = LocalizedName(snapshot.RegionalGovernmentLayer
                .RegionTitle, pRegionTitle);
            pGovernorTitle = LocalizedName(snapshot.RegionalGovernmentLayer
                .GovernorTitle, pGovernorTitle);
            pLocalLevelTitle = LocalizedName(snapshot.RegionalGovernmentLayer
                .LocalLevelTitle, pLocalLevelTitle);
        }

        internal static IReadOnlyList<CustomLocalCourtTemplate>
            ResolvedLocalTemplates(Kingdom pKingdom)
        {
            if (TryGetSnapshot(pKingdom, out CustomCourtTemplate snapshot) &&
                snapshot.LocalTemplates != null &&
                snapshot.LocalTemplates.Count > 0)
                return snapshot.LocalTemplates;
            return BuiltInLocalTemplates;
        }

        private static string LocalizedName(CustomCourtLocalizedText value,
            string fallback)
        {
            if (value == null) return fallback ?? string.Empty;
            string primary = HistoryLocalizationRules.CurrentLanguage() == "en"
                ? value.English
                : value.Chinese;
            string secondary = HistoryLocalizationRules.CurrentLanguage() == "en"
                ? value.Chinese
                : value.English;
            if (!string.IsNullOrWhiteSpace(primary)) return primary;
            if (!string.IsNullOrWhiteSpace(secondary)) return secondary;
            return fallback ?? string.Empty;
        }

        public static bool TryApply(Kingdom kingdom,
            CustomCourtTemplate template,
            IReadOnlyDictionary<string, long> incumbents)
        {
            if (kingdom?.data == null) return false;
            CustomCourtInstance current;
            TryGetInstance(kingdom, out current);
            var application = new CustomCourtApplicationService(Instances);
            CustomCourtInstance next;
            if (!application.TryBuildInstance(KingdomKey(kingdom), template,
                    current, incumbents, out next) || !Instances.Save(next))
                return false;
            RegionalGovernmentAggregationService.Invalidate(kingdom);
            try
            {
                kingdom.data.set(LineageKeys.CUSTOM_COURT_TEMPLATE_ID,
                    next.TemplateId);
                kingdom.data.set(LineageKeys.CUSTOM_COURT_TEMPLATE_REVISION,
                    next.TemplateRevision);
                kingdom.data.set(LineageKeys.CUSTOM_COURT_TEMPLATE_HASH,
                    next.TemplateHash);
                kingdom.data.set(LineageKeys.CUSTOM_COURT_INSTANCE_SNAPSHOT,
                    CustomCourtInstanceCodec.Export(next));
                return true;
            }
            catch
            {
                if (current == null) Instances.Remove(KingdomKey(kingdom));
                else Instances.Save(current);
                return false;
            }
        }
    }
}
