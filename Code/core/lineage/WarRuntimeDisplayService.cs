using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    internal static class WarRuntimeDisplayService
    {
        public static string Resolve(War pWar)
        {
            string liveName = "";
            string localeKey = "";
            try
            {
                liveName = pWar?.name ?? "";
                localeKey = pWar?.getAsset()?.localized_war_name ?? "";
            }
            catch { }
            string localized = string.IsNullOrEmpty(localeKey)
                ? ""
                : AW_L10n.Text(localeKey, "");
            return WarRuntimeDisplayRules.ResolveName(liveName, localized,
                AW_L10n.Text("aw_diplomacy_unnamed_war", "War"));
        }
    }
}
