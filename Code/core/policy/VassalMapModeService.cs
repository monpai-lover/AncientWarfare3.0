using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.policy
{
    internal static class VassalMapModeService
    {
        public const string POWER_ID = "aw_vassal_mapmode";
        private const double DIRTY_MIN_INTERVAL = 0.25;

        [System.ThreadStatic] private static Dictionary<long, IMetaObject> _zoneRootCache;
        private static double _lastDirtyTime = -1.0;

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
            IMetaObject result = root?.data == null || root.isRekt()
                ? kingdom
                : root;
            _zoneRootCache[kingdom.id] = result;
            return result;
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            string header = AW_L10n.Text("aw_vassal_mapmode_tooltip", "\u9644\u5EB8\u5730\u56FE");
            string body = VassalService.BuildTooltip(pKingdom);
            string war = BuildWarSummary(pKingdom);
            string result = string.IsNullOrEmpty(body) ? header : header + "\n" + body;
            return string.IsNullOrEmpty(war) ? result : result + "\n" + war;
        }

        public static void DirtyMap()
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                _zoneRootCache?.Clear();
                AWMapModeMetaLibrary.ClearDynamicMetaCache();
                World.world?.zone_calculator?.dirtyAndClear();
            }
            catch { }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.MapDirtyIndex, benchmark);
            }
        }

        public static void DirtyMapIfActive()
        {
            double now = LineageService.CurTime();
            if (!MapModeDirtyThrottleRules.ShouldDirty(IsActive(), now, _lastDirtyTime, DIRTY_MIN_INTERVAL)) return;
            _lastDirtyTime = now;
            DirtyMap();
        }

        internal static void ResetRuntime()
        {
            _zoneRootCache?.Clear();
            _lastDirtyTime = -1.0;
        }

        private static bool HasActiveWar(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            try
            {
                foreach (War war in pKingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) return true;
            }
            catch { }
            return false;
        }

        private static string BuildWarSummary(Kingdom pKingdom)
        {
            if (!HasActiveWar(pKingdom)) return "";
            var enemies = new List<string>(3);
            try
            {
                foreach (War war in pKingdom.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    Kingdom enemy = war.getMainAttacker() == pKingdom
                        ? war.getMainDefender()
                        : war.getMainAttacker();
                    if (enemy?.data == null || enemies.Contains(enemy.name))
                        continue;
                    enemies.Add(enemy.name);
                    if (enemies.Count >= 3) break;
                }
            }
            catch { }
            string label = AW_L10n.Text("aw_vassal_mapmode_at_war", "\u4EA4\u6218\u4E2D");
            return enemies.Count == 0 ? label : label + ": " + string.Join("\u3001", enemies);
        }

    }
}
