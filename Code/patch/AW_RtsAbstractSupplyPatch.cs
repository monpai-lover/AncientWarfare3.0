using System;
using System.Reflection;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RtsAbstractSupplyPatch
    {
        private const string CityFoodTaskId = "try_to_eat_city_food";

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(AiSystemActor), "setTask");
        }

        [HarmonyPrefix]
        private static bool SetTaskPrefix(AiSystemActor __instance,
            string __0)
        {
            if (!string.Equals(__0, CityFoodTaskId,
                    StringComparison.Ordinal)) return true;
            try
            {
                bool supplied = ArmyRtsAbstractSupplyService.
                    TryConsumeHomeRation(__instance?.ai_object);
                if (ArmyRtsAbstractSupplyRules.
                        ShouldSuppressVanillaFoodTask(supplied))
                    return false;
                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
