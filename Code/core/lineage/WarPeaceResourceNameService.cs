using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceResourceNameService
    {
        public static string Resolve(string pResourceId)
        {
            if (string.IsNullOrWhiteSpace(pResourceId))
                return AW_L10n.Text("aw_war_peace_resource_generic",
                    "resources");
            string originalTranslation = string.Empty;
            try
            {
                ResourceAsset asset = AssetManager.resources.get(
                    pResourceId);
                if (asset != null)
                    originalTranslation = asset.getTranslatedName();
            }
            catch { }
            string fallback = AW_L10n.Text(
                WarPeaceResourceNameRules.FallbackLocaleKey(pResourceId),
                WarPeaceResourceNameRules.BuiltInEnglishFallback(
                    pResourceId));
            return WarPeaceResourceNameRules.Resolve(pResourceId,
                originalTranslation, fallback);
        }
    }
}
