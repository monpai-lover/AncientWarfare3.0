namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     编年史"该不该记"的统一门槛判断,消除各 On 方法里重复的 IsXia / lineage_id 检查。
    ///     全部判空安全。
    /// </summary>
    internal static class ChronicleGate
    {
        /// <summary>是否入谱贵族:Xia 且已建谱系(LINEAGE_ID≥0)。人物事件的默认门槛。</summary>
        public static bool IsNobleActor(Actor pActor)
        {
            if (pActor?.data == null) return false;
            // 短路重排(等价原 ShouldUseAwLineageSystem,但把 IsXiaKingdom 从~4 次降到≤1 次):
            // 无谱系的平民/奴隶最多,先廉价出局;Xia 角色不碰王国判定;其余才算一次王国级判定。
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
            if (lid < 0) return false;
            if (LineageService.IsXia(pActor)) return true;
            Kingdom kingdom = pActor.kingdom;
            if (LineageService.IsXiaKingdom(kingdom)) return true;
            return XiaizationService.UsesXiaizedInstitutionSystem(kingdom);
        }

        /// <summary>是否"重要人物":国王 / 城主 / 历史人物。用于重要击杀等跨门槛判定。</summary>
        public static bool IsImportant(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (pActor.isKing()) return true;
            if (pActor.isCityLeader()) return true;
            if (pActor.hasTrait(content.figures.HistoricalFigureService.TRAIT_FIGURE) ||
                pActor.hasTrait(content.figures.HistoricalFigureService.TRAIT_FIRST)) return true;
            return false;
        }
    }
}
