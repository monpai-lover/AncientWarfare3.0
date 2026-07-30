using System;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.content
{
    [HarmonyPatch]
    public static class XiaTexturePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "getUnitTexturePath")]
        public static bool GetUnitTexturePath_Prefix(Actor __instance, ref string __result)
        {
            if (__instance?.asset == null) return true;

            if (__instance.asset.id == LineageService.XIA_ASSET_ID && IsHeirSkinActor(__instance))
            {
                __result = XiaRace.TEXTURE_PATH + "heir";
                return false;
            }

            if (XiaTextures.AnimationTextures.TryGetValue(__instance.asset.id, out Func<Actor, string> action))
            {
                string texture = action(__instance);
                if (texture != null)
                {
                    __result = texture;
                    return false;
                }
            }

            return true;
        }

        private static bool IsHeirSkinActor(Actor pActor)
        {
            if (pActor.data == null) return false;
            pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
            if (!isHeir && !FeudatoryService.IsActivePrince(pActor))
                return false;
            if (pActor.isBaby() || pActor.isEgg()) return false;
            if (pActor.isKing() || pActor.isCityLeader() || pActor.isWarrior()) return false;
            return true;
        }
    }
}
