using System;
using HarmonyLib;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     让 <see cref="XiaTextures.AnimationTextures"/> 委托表生效:
    ///     单位取贴图路径时,若该 ActorAsset 注册了动态贴图委托,则用委托返回值覆盖。
    ///     参考同版本 PVZ mod 的 getUnitTexturePath Prefix(pvz_harmony_actors.cs:1351)。
    /// </summary>
    [HarmonyPatch]
    public static class XiaTexturePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "getUnitTexturePath")]
        public static bool GetUnitTexturePath_Prefix(Actor __instance, ref string __result)
        {
            if (XiaTextures.AnimationTextures.TryGetValue(__instance.asset.id, out Func<Actor, string> action))
            {
                string texture = action(__instance);
                if (texture != null)
                {
                    __result = texture;
                    return false; // 跳过原方法,使用自定义路径
                }
            }

            return true; // 走原方法
        }
    }
}
