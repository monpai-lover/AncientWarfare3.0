using System.Globalization;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtRuntime
    {
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
            CustomCourtTemplate snapshot;
            if (!TryGetSnapshot(kingdom, out snapshot) ||
                snapshot.Offices == null) return string.Empty;
            foreach (CustomCourtOffice office in snapshot.Offices)
            {
                if (office == null || office.Id != officeId) continue;
                return LocalizedName(office.Name, office.Id);
            }
            return string.Empty;
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
