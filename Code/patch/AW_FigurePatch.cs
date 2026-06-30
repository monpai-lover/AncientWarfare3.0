using AncientWarfare3.content.figures;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     历史人物降临的 Harmony 钩子(均 Postfix,不接管原方法):
    ///
    ///     1. Postfix Actor.newCreature —— 新生文明单位的统一末尾,交给 HistoricalFigureService 门槛+掷骰。
    ///        (AW2 钩的 ActorManager.spawnPopPoint 新版已删;newCreature 是所有新单位必经点。)
    ///     2. Postfix Kingdom.setKing —— 历史人物成为某国国王(夺取/继承/创建都经此)时套用预留国名。
    ///     3. Postfix QuantumSpriteLibrary.drawKings —— 给存活历史人物头顶画 minimap_figure 小地图图标。
    ///
    ///     死亡门(figure 死→解锁下一位)接在 AW_ActorDeathPatch(已有的 Actor.die Prefix)里,不在此重复钩 die。
    /// </summary>
    [HarmonyPatch]
    public static class AW_FigurePatch
    {
        // ① 出生 → 尝试降临
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "newCreature")]
        public static void NewCreature_Postfix(Actor __instance)
        {
            HistoricalFigureService.TrySpawnOn(__instance);
        }

        // ② 成为 king → 套用预留国名(周/秦/…)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor)
        {
            if (__instance == null || pActor == null) return;
            HistoricalFigureService.OnFigureKingBecame(__instance, pActor);
        }

        // ③ 小地图图标:历史人物(first 特质,存活)头顶画 minimap_figure,国家色着色
        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawKings")]
        public static void DrawKings_Postfix(QuantumSpriteAsset pAsset)
        {
            // 没有任何存活历史人物时直接跳过(避免每帧全图遍历)。
            if (!FigureStateStoreHasAlive()) return;
            if (pAsset?.group_system == null) return;

            Sprite baseIcon = SpriteTextureLoader.getSprite("civ/icons/minimap_figure");
            if (baseIcon == null) return;

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom == null || !kingdom.isCiv()) continue;
                foreach (Actor unit in kingdom.getUnits())
                {
                    if (unit == null || !unit.isAlive()) continue;
                    if (unit.current_tile == null) continue; // 防 current_position 取空崩/堆 (0,0)
                    if (!unit.hasTrait(HistoricalFigureService.TRAIT_FIRST)) continue;
                    // 国王皇冠图标由原版 drawKings 画了,历史人物图标只画"非当前国王"的,避免叠图。
                    if (kingdom.king == unit) continue;

                    Vector3 pos = unit.current_position;
                    pos.y -= 3f;

                    QuantumSprite qs = pAsset.group_system.getNext();
                    if (qs == null) continue;
                    qs.setPosOnly(ref pos);
                    Sprite colored = DynamicSprites.getIcon(baseIcon, kingdom.getColor());
                    qs.setSprite(colored);
                }
            }
        }

        // 轻量包装:供 drawKings Postfix 早退判断(对外暴露 FigureStateStore.AnyAliveFigure)。
        private static bool FigureStateStoreHasAlive()
        {
            return core.db.FigureStateStore.AnyAliveFigure();
        }
    }
}
