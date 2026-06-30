using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     继承人小地图图标:给被标记 IS_HEIR 的存活夏人(未成 king/城主)头顶画 minimap_heir,国家色着色。
    ///     照 AW_FigurePatch.DrawKings_Postfix(QuantumSpriteLibrary.drawKings Postfix)同款写法
    ///     —— 用 group_system.getNext() + qs.set(pos, base_scale) 防图标过大(见 figure 图标修复教训)。
    ///     继承人成为 king/城主后改由原版皇冠/城主图标表示 → 不再画 heir 图标(避免叠图)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_HeirMinimapPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawKings")]
        public static void DrawKings_Heir_Postfix(QuantumSpriteAsset pAsset)
        {
            if (pAsset?.group_system == null) return;

            Sprite baseIcon = SpriteTextureLoader.getSprite("civ/icons/minimap_heir");
            if (baseIcon == null) return;

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom == null || !kingdom.isCiv()) continue;
                foreach (Actor unit in kingdom.getUnits())
                {
                    if (unit == null || !unit.isAlive()) continue;
                    if (unit.current_tile == null) continue;          // 防 current_position 取空
                    if (!IsHeirMark(unit)) continue;                  // 仅被标记继承人
                    if (unit.isKing() || unit.isCityLeader()) continue; // 已是王/城主 → 原版图标表示

                    Vector3 pos = unit.current_position;
                    pos.y -= 3f;

                    QuantumSprite qs = pAsset.group_system.getNext();
                    if (qs == null) continue;
                    qs.set(ref pos, pAsset.base_scale);
                    Sprite colored = DynamicSprites.getIcon(baseIcon, kingdom.getColor());
                    qs.setSprite(colored);
                }
            }
        }

        private static bool IsHeirMark(Actor pUnit)
        {
            if (pUnit.data == null) return false;
            pUnit.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
            return isHeir;
        }
    }
}
