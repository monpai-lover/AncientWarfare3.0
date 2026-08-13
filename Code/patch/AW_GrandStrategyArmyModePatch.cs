using AncientWarfare3.core.grandstrategy;
using AncientWarfare3.core.lineage;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_GrandStrategyArmyModePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(City), "updateCapture")]
        private static bool UpdateCapturePrefix()
        {
            return !GrandStrategyRuntimeHost.Active;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(BehCityActorCheckAttack),
            nameof(BehCityActorCheckAttack.execute))]
        private static bool CityActorCheckAttackPrefix(Actor pActor,
            ref BehResult __result)
        {
            if (!ShouldOwnWarrior(pActor)) return true;
            __result = BehResult.Stop;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(CityBehCheckAttackZone),
            nameof(CityBehCheckAttackZone.execute))]
        private static bool CityCheckAttackZonePrefix(City pCity,
            ref BehResult __result)
        {
            if (!GrandStrategyRuntimeHost.Active) return true;
            if (pCity != null)
            {
                pCity.target_attack_city = null;
                pCity.target_attack_zone = null;
            }
            __result = BehResult.Continue;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(DecisionHelper),
            nameof(DecisionHelper.makeDecisionFor))]
        private static bool MakeDecisionForPrefix(Actor pActor,
            ref string pLastDecisionID, ref bool __result)
        {
            if (!ShouldOwnWarrior(pActor)) return true;
            pLastDecisionID = string.Empty;
            __result = false;
            return false;
        }

        private static bool ShouldOwnWarrior(Actor actor)
        {
            if (!GrandStrategyRuntimeHost.Active || actor?.data == null ||
                !actor.is_profession_warrior ||
                RoyalGuardService.IsRoyalGuard(actor)) return false;
            try
            {
                Kingdom kingdom = actor.kingdom;
                if (kingdom?.data == null) return false;
                foreach (War war in kingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) return true;
                return false;
            }
            catch { return false; }
        }
    }
}
