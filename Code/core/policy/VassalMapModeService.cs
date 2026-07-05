using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.policy
{
    internal static class VassalMapModeService
    {
        public const string POWER_ID = "aw_vassal_mapmode";

        [System.ThreadStatic] private static Dictionary<long, IMetaObject> _zoneRootCache;

        public static bool IsActive()
        {
            return AWMapModeCoordinator.IsActive(POWER_ID);
        }

        public static IMetaObject GetRootMetaForZone(TileZone pZone)
        {
            City city = pZone?.city;
            Kingdom kingdom = city?.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return null;

            _zoneRootCache ??= new Dictionary<long, IMetaObject>();
            if (_zoneRootCache.TryGetValue(kingdom.id, out IMetaObject cached)) return cached;

            Kingdom root = VassalService.GetRootSuzerain(kingdom);
            IMetaObject result = root?.data == null ? kingdom : root;
            _zoneRootCache[kingdom.id] = result;
            return result;
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            string header = AW_L10n.Text("aw_vassal_mapmode_tooltip", "\u9644\u5EB8\u5730\u56FE");
            string body = VassalService.BuildTooltip(pKingdom);
            return string.IsNullOrEmpty(body) ? header : header + "\n" + body;
        }

        public static void DirtyMap()
        {
            try
            {
                _zoneRootCache?.Clear();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
        }

        public static void DirtyMapIfActive()
        {
            if (!IsActive()) return;
            DirtyMap();
        }

    }
}
