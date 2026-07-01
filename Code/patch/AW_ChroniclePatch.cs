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
    ///     成王/换君事件在本 patch 的 Kingdom.setKing Postfix 里最后统一写入。
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
        public static void CitySetKingdom_Prefix(City __instance, Kingdom pKingdom, bool pFromLoad, out Kingdom __state)
        {
            Kingdom oldKingdom = __instance != null ? __instance.kingdom : null;
            __state = oldKingdom;
            ChronicleEvents.OnCityTransferred(__instance, oldKingdom, pKingdom, pFromLoad);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        public static void CitySetKingdom_Postfix(City __instance, Kingdom pKingdom, bool pFromLoad, Kingdom __state)
        {
            if (pFromLoad) return;
            KingdomArchiveWriter.Upsert(__state);
            KingdomArchiveWriter.Upsert(__instance?.kingdom ?? pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (pFromLoad) return;
            ChronicleEvents.OnKingChanged(__instance, pActor);
        }

        // 建城(newCityEvent 在 City 自身声明,typeof 正确;纯新建城,读档走 loadCity 不经此)。
        // Postfix:此时 generateName 已跑,city.data.name / city.kingdom 就绪,记城市史起点 found 事件。
        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.newCityEvent))]
        public static void NewCityEvent_Postfix(City __instance)
        {
            ChronicleEvents.OnCityFounded(__instance);
        }
    }
}
