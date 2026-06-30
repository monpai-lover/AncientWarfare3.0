using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     建筑染色防 null 崩(根治 Player.log 每帧 917 次 NullReferenceException)。
    ///
    ///     崩溃链:`Building.calculateColoredSprite(pMainSprite=null)`(Building.cs:1448)
    ///     → `DynamicSprites.getRecoloredBuilding` 对 `pMainSprite.GetHashCode()` 解空(DynamicSprites.cs:22)。
    ///     即某建筑的主贴图 list_main 为空 → calculateMainSprite 返 null → 每帧染色崩。
    ///
    ///     主因(夏朝建筑预读毒化 SpriteTextureLoader 缓存)已在 XiaArchitecture 删除;
    ///     本补丁是**通用防御**:任何建筑(含 D 盘部署缺图、其它 mod)主贴图为 null 时,
    ///     跳过染色返回 null(这一帧不画该建筑,下一帧贴图就绪后重算),杜绝每帧抛异常。
    /// </summary>
    [HarmonyPatch]
    public static class AW_BuildingSpritePatch
    {
        // 跳过原方法的条件(避免 getRecoloredBuilding 对 null 解引用每帧崩):
        //   ① pMainSprite 为 null(主贴图没加载出来);
        //   ② asset.atlas_asset 为 null(mod 建筑漏绑 atlas → getRecoloredBuilding 内 pAtlasAsset.getSprite 崩,
        //      DynamicSprites.cs:23。根治在 XiaArchitecture 已补绑 atlas_asset,此处兜底其它来源)。
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Building), nameof(Building.calculateColoredSprite))]
        public static bool CalculateColoredSprite_Prefix(Building __instance, Sprite pMainSprite, ref Sprite __result)
        {
            if (pMainSprite == null || __instance == null || __instance.asset == null
                || __instance.asset.atlas_asset == null)
            {
                __result = null; // 渲染层拿到 null 这一帧不画,下一帧重算,比抛异常好得多
                return false;     // 跳过原方法
            }
            return true;          // 正常放行
        }
    }
}
