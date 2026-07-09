using System.Reflection;
using AncientWarfare3.core.lineage;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ArmySafetyPatch
    {
        private static readonly FieldInfo ArmyKingdomField = AccessTools.Field(typeof(Army), "_kingdom");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehCityActorCheckAttack), nameof(BehCityActorCheckAttack.execute))]
        public static bool CityActorCheckAttack_Prefix(Actor pActor, ref BehResult __result)
        {
            bool skip = ArmyAiSafetyRules.ShouldSkipCityAttackAction(
                pHasActor: pActor?.data != null && !pActor.isRekt(),
                pHasCity: pActor?.city?.data != null,
                pHasAttackZone: pActor?.city?.target_attack_zone != null,
                pHasArmy: pActor?.army?.data != null,
                pHasCurrentTile: pActor?.current_tile != null,
                pHasCurrentZone: pActor?.current_tile?.zone != null);

            if (skip)
            {
                __result = BehResult.Stop;
                return false;
            }

            if (!ArmyRetreatService.ShouldStopAttack(pActor)) return true;
            __result = BehResult.Stop;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Army), nameof(Army.save))]
        public static bool ArmySave_Prefix(Army __instance)
        {
            if (__instance?.data == null) return true;
            if (!AWArmyService.IsSpecialArmy(__instance)) return true;

            City city = SafeCity(__instance);
            Actor captain = SafeCaptain(__instance);
            Kingdom kingdom = SafeKingdom(__instance, city, captain);
            int unitCount = SafeUnitCount(__instance);

            if (ArmySaveSafetyRules.ShouldRemoveInvalidSpecialArmy(
                    pIsSpecialArmy: true,
                    pHasKingdom: kingdom?.data != null,
                    pHasCity: city?.data != null,
                    pHasCaptain: captain?.data != null,
                    pUnitCount: unitCount))
            {
                SafeRemoveArmy(__instance);
                __instance.data.id_city = -1L;
                __instance.data.id_kingdom = -1L;
                __instance.data.id_captain = -1L;
                return false;
            }

            if (kingdom?.data != null)
                TrySetKingdom(__instance, kingdom);

            if (!ArmySaveSafetyRules.ShouldUseSafeSpecialArmySave(true, kingdom?.data != null))
                return true;

            __instance.data.id_city = -1L;
            __instance.data.id_kingdom = kingdom?.data != null ? kingdom.id : -1L;
            __instance.data.id_captain = captain?.data != null ? captain.data.id : -1L;
            return false;
        }

        private static City SafeCity(Army pArmy)
        {
            try
            {
                City city = pArmy.getCity();
                if (city?.data != null) return city;
            }
            catch { }

            try
            {
                City anchor = AWArmyService.FindAnchorCity(pArmy);
                return anchor?.data != null ? anchor : null;
            }
            catch { return null; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try
            {
                Actor captain = pArmy.getCaptain();
                if (captain?.data != null && !captain.isRekt()) return captain;
            }
            catch { }

            try
            {
                foreach (Actor unit in pArmy.getUnits())
                    if (unit?.data != null && !unit.isRekt())
                        return unit;
            }
            catch { }

            return null;
        }

        private static Kingdom SafeKingdom(Army pArmy, City pCity, Actor pCaptain)
        {
            if (pCaptain?.kingdom?.data != null) return pCaptain.kingdom;
            if (pCity?.kingdom?.data != null) return pCity.kingdom;

            try
            {
                Kingdom kingdom = pArmy.getKingdom();
                if (kingdom?.data != null) return kingdom;
            }
            catch { }

            return null;
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return pArmy.countUnits(); }
            catch { return 0; }
        }

        private static void TrySetKingdom(Army pArmy, Kingdom pKingdom)
        {
            try { ArmyKingdomField?.SetValue(pArmy, pKingdom); }
            catch { }
        }

        private static void SafeRemoveArmy(Army pArmy)
        {
            try { World.world?.armies?.removeObject(pArmy); }
            catch { }
        }
    }
}
