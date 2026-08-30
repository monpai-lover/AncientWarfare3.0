using System;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.presentation;
using HarmonyLib;

namespace AncientWarfare3.content
{
    [HarmonyPatch]
    public static class XiaTexturePatch
    {
        private static readonly bool EnableOfficialBodySkinSwitch = true;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "getUnitTexturePath")]
        public static bool GetUnitTexturePath_Prefix(Actor __instance, ref string __result)
        {
            if (__instance?.asset == null) return true;

            if (CivMonkeyTextureCatalog.TryGetRuntimeTexturePath(__instance, out string monkeyTexture))
            {
                __result = monkeyTexture;
                return false;
            }

            if (__instance.asset.id == LineageService.XIA_ASSET_ID)
            {
                if (__instance.isKing()) return true;
                if (IsHeirSkinActor(__instance))
                {
                    __result = XiaRace.TEXTURE_PATH + "heir";
                    return false;
                }

                if (EnableOfficialBodySkinSwitch)
                {
                    int rank = OfficialCareerRankRules.Unranked;
                    __instance.data?.get(LineageKeys.OFFICER_RANK, out rank,
                        OfficialCareerRankRules.Unranked);
                    string officeId = "";
                    string layer = "";
                    __instance.data?.get(LineageKeys.COURT_OFFICE_ID,
                        out officeId, "");
                    __instance.data?.get(LineageKeys.COURT_LAYER,
                        out layer, "");
                    int officeGrade =
                        OfficialCareerStateService.OfficeGradeForOffice(
                            __instance.kingdom, layer, officeId,
                            __instance.city);
                    string officialBody =
                        XiaActorTextureRules.ResolveOfficialBodyDirectory(
                            rank, officeGrade);
                    if (!string.IsNullOrEmpty(officialBody))
                    {
                        __result = XiaRace.TEXTURE_PATH + officialBody;
                        return false;
                    }
                }
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
