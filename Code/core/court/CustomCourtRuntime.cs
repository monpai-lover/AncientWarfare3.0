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

        internal static void ClearRuntime()
        {
            Instances.Clear();
        }

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

        public static bool HasCustomLocalTemplates(Kingdom pKingdom)
        {
            return TryGetSnapshot(pKingdom,
                       out CustomCourtTemplate snapshot) &&
                   snapshot.LocalTemplates != null &&
                   snapshot.LocalTemplates.Count > 0;
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
            bool isCapital = pCity == pKingdom.capital ||
                pCity.isCapitalCity();
            CustomLocalGovernmentDefaultKind automaticKind =
                CustomLocalGovernmentRules.SelectDefault(manual,
                    CustomLocalGovernmentCityService.HasForeignLandBorder(
                        pCity, pKingdom),
                    CityEconomyService.IsFrontierMilitary(pKingdom, pCity),
                    isCapital);
            bool military = automaticKind ==
                CustomLocalGovernmentDefaultKind.Military;
            bool effectiveManual = manual && !isCapital;
            string resolvedId = CustomLocalCourtTemplateRules.ResolveTemplateId(
                templates, persistedId, effectiveManual, military);
            pTemplate = templates.FirstOrDefault(template =>
                template != null && string.Equals(template.Id, resolvedId,
                    System.StringComparison.Ordinal));
            if (pTemplate == null) return false;

            bool validManual = effectiveManual && string.Equals(persistedId,
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
            if (!TryGetLocalTemplate(pKingdom, pCity,
                    out CustomLocalCourtTemplate current) || current == null)
                return false;
            CustomLocalCourtTemplate target = templates.FirstOrDefault(template =>
                template != null && template.Id == pTemplateId);
            if (target == null || !CourtTemplateOfficerMigrationService
                    .TryMigrateLocal(pKingdom, pCity, current, target))
                return false;
            pCity.data.set(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                pTemplateId);
            pCity.data.set(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_MANUAL,
                pManual);
            CityBureauAnnualWorkService.RequestImmediateReconcile(pKingdom,
                pCity.data.id);
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
            pRegionTitle = "州";
            pGovernorTitle = "州牧";
            pLocalLevelTitle = "郡";
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
            CustomCourtTemplateScope scope = template?.Scope ??
                CustomCourtTemplateScope.CentralCourt;
            if (scope == CustomCourtTemplateScope.LocalGovernment)
                return TryApplyLocal(kingdom, template, incumbents);
            if (scope == CustomCourtTemplateScope.Combined)
                return TryApplyCombined(kingdom, template, incumbents);
            return TryApplyCentral(kingdom, template, incumbents);
        }

        public static bool TryApplyCentral(Kingdom kingdom,
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
            if (!CourtTemplateOfficerMigrationService.TryMigrateCentral(
                    kingdom, current?.ResolvedSnapshot, next.ResolvedSnapshot))
            {
                if (current == null) Instances.Remove(KingdomKey(kingdom));
                else Instances.Save(current);
                return false;
            }
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

        public static bool TryApplyLocal(Kingdom kingdom,
            CustomCourtTemplate template,
            IReadOnlyDictionary<string, long> incumbents)
        {
            if (kingdom?.data == null || template == null) return false;
            CustomCourtInstance current;
            TryGetInstance(kingdom, out current);
            var application = new CustomCourtApplicationService(Instances);
            CustomCourtInstance next;
            if (!application.TryBuildInstance(KingdomKey(kingdom), template,
                    current, incumbents, out next) || !Instances.Save(next))
                return false;
            try
            {
                foreach (City city in kingdom.getCities() ??
                         Enumerable.Empty<City>())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != kingdom) continue;
                    CustomLocalCourtTemplate target;
                    if (!TryGetLocalTemplate(kingdom, city, out target))
                        continue;
                    CustomLocalCourtTemplate source =
                        FindPersistedLocalTemplate(current?.ResolvedSnapshot,
                            city);
                    CourtTemplateOfficerMigrationService.TryMigrateLocal(
                        kingdom, city, source, target);
                    CityBureauAnnualWorkService.RequestImmediateReconcile(
                        kingdom, city.data.id);
                }
                RegionalGovernmentAggregationService.Invalidate(kingdom);
                PersistTemplateMetadata(kingdom, next);
                return true;
            }
            catch
            {
                if (current == null) Instances.Remove(KingdomKey(kingdom));
                else Instances.Save(current);
                return false;
            }
        }

        public static bool TryApplyCombined(Kingdom kingdom,
            CustomCourtTemplate template,
            IReadOnlyDictionary<string, long> incumbents)
        {
            if (kingdom?.data == null || template == null) return false;
            CustomCourtTemplate central = CustomCourtTemplateJsonCodec.Normalize(
                template);
            central.Scope = CustomCourtTemplateScope.CentralCourt;
            if (!TryApplyCentral(kingdom, central, incumbents)) return false;
            central.Scope = CustomCourtTemplateScope.Combined;
            return TryApplyLocal(kingdom, central, incumbents);
        }

        private static CustomLocalCourtTemplate FindPersistedLocalTemplate(
            CustomCourtTemplate pSnapshot, City pCity)
        {
            if (pSnapshot?.LocalTemplates == null || pCity?.data == null)
                return null;
            pCity.data.get(LineageKeys.CITY_LOCAL_COURT_TEMPLATE_ID,
                out string id, string.Empty);
            return pSnapshot.LocalTemplates.FirstOrDefault(template =>
                template != null && template.Id == id);
        }

        private static void PersistTemplateMetadata(Kingdom pKingdom,
            CustomCourtInstance pInstance)
        {
            pKingdom.data.set(LineageKeys.CUSTOM_COURT_TEMPLATE_ID,
                pInstance.TemplateId);
            pKingdom.data.set(LineageKeys.CUSTOM_COURT_TEMPLATE_REVISION,
                pInstance.TemplateRevision);
            pKingdom.data.set(LineageKeys.CUSTOM_COURT_TEMPLATE_HASH,
                pInstance.TemplateHash);
            pKingdom.data.set(LineageKeys.CUSTOM_COURT_INSTANCE_SNAPSHOT,
                CustomCourtInstanceCodec.Export(pInstance));
        }
    }
}
