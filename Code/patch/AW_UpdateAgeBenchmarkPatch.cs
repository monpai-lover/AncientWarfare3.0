using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_UpdateAgeBenchmarkPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "updateObjectAge")]
        public static void UpdateObjectAge_Postfix(long __state)
        {
            UpdateAgeBenchmark.Flush(__state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), "updateObjectAge")]
        public static void UpdateObjectAge_Prefix(out long __state)
        {
            __state = UpdateAgeBenchmark.Begin();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "updateAge")]
        public static void ActorUpdateAge_Prefix(out long __state)
        {
            __state = UpdateAgeBenchmark.Begin();
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Actor), "updateAge")]
        public static void ActorUpdateAge_Postfix(long __state)
        {
            UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.ActorFullWallIndex, __state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.updateAge))]
        public static void CityUpdateAge_Prefix(out long __state)
        {
            __state = UpdateAgeBenchmark.Begin();
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(City), nameof(City.updateAge))]
        public static void CityUpdateAge_Postfix(long __state)
        {
            UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.CityFullWallIndex, __state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateAge))]
        public static void KingdomUpdateAge_Prefix(out long __state)
        {
            __state = UpdateAgeBenchmark.Begin();
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateAge))]
        public static void KingdomUpdateAge_Postfix(long __state)
        {
            UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.KingdomFullWallIndex, __state);
        }
    }
}
