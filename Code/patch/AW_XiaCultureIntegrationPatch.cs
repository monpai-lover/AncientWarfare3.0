using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_XiaCultureIntegrationPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.addLoadWorldCallbacks))]
        private static void RegisterWorldLoaded_Postfix()
        {
            MapBox.on_world_loaded -= OnWorldLoaded;
            MapBox.on_world_loaded += OnWorldLoaded;
        }

        private static void OnWorldLoaded()
        {
            MapBox.on_world_loaded -= OnWorldLoaded;
            if (World.world == null) return;

            XiaizationService.ResetCultureIntegrationProjection();
            XiaizationService.RestorePersistedCultureIntegrations();
            if (World.world.cultures != null)
                foreach (Culture culture in World.world.cultures)
                    if (XiaCultureIntegrationService.IsNativeXiaCulture(
                            culture))
                        XiaCultureIntegrationService.MarkIntegrated(
                            culture);
            if (World.world.kingdoms != null)
                foreach (Kingdom kingdom in World.world.kingdoms)
                    XiaizationService.ProjectCultureIntegration(kingdom);

            int initialized = KingdomPolicyService.EnsureWorldInitialized();
            if (initialized > 0)
                ModClass.LogInfo("AW3 policy profiles restored for " +
                                 initialized + " kingdoms.");
        }
    }
}
