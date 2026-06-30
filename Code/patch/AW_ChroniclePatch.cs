using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     编年史新增钩点:
    ///     - Postfix Kingdom.newCivKingdom —— 建国(internal,用字符串方法名)。
    ///     - Postfix KingdomManager.removeObject —— 亡国(public override,KingdomManager 自身声明,typeof 正确)。
    ///     - Prefix  City.setKingdom —— 城市易主(internal;Prefix 时 city.kingdom 仍是旧国,参数为新国;
    ///       pFromLoad 读档回填跳过)。
    ///     成王/换君事件在 AW_FigurePatch.SetKing_Postfix 里走 ChronicleEvents.OnKingChanged,不在此重复。
    /// </summary>
    [HarmonyPatch]
    public static class AW_ChroniclePatch
    {
        // 建国(newCivKingdom 是 internal,用字符串名避免可见性问题)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), "newCivKingdom")]
        public static void NewCivKingdom_Postfix(Kingdom __instance)
        {
            ChronicleEvents.OnKingdomFounded(__instance);
        }

        // 亡国(removeObject 是 KingdomManager 自身的 public override,typeof(KingdomManager) 正确)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
        public static void RemoveKingdom_Postfix(Kingdom pKingdom)
        {
            ChronicleEvents.OnKingdomDestroyed(pKingdom);
        }

        // 城市易主(setKingdom 是 internal,用字符串名;Prefix 取旧国——原方法未执行,__instance.kingdom 仍是旧国)
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        public static void CitySetKingdom_Prefix(City __instance, Kingdom pKingdom, bool pFromLoad)
        {
            Kingdom oldKingdom = __instance != null ? __instance.kingdom : null;
            ChronicleEvents.OnCityTransferred(__instance, oldKingdom, pKingdom, pFromLoad);
        }
    }
}
