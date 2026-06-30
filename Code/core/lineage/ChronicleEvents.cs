namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     把游戏钩子里的原始信号转成编年史事件(含防重复 / 仅入谱贵族 判断),
    ///     避免各 patch 文件塞业务逻辑。HistoryWriter 负责落库,本类负责"要不要记 + 记什么"。
    /// </summary>
    public static class ChronicleEvents
    {
        // setKing:新王就位 → 国家换君 + 人物成王。新王==旧记录则跳过(用 data 上的标记防同王重复)。
        public static void OnKingChanged(Kingdom pKingdom, Actor pNewKing)
        {
            if (pKingdom?.data == null || pNewKing?.data == null) return;

            // 防重复:记录上次为该国登记的王 id,相同则跳过。
            pKingdom.data.get(LineageKeys.CHRONICLE_LAST_KING_ID, out long lastKingId, -1L);
            if (lastKingId == pNewKing.data.id) return;
            pKingdom.data.set(LineageKeys.CHRONICLE_LAST_KING_ID, pNewKing.data.id);

            string kingName = pNewKing.getName();

            // 国家·换君
            HistoryWriter.RecordKingdom(pKingdom, "rule_change", kingName + " 即位为君");
            KingdomArchiveWriter.Upsert(pKingdom); // 顺带刷新名/旗/存活快照(廉价)

            // 人物·成王(仅入谱贵族)
            if (LineageService.IsXia(pNewKing))
            {
                pNewKing.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
                if (lid >= 0)
                    HistoryWriter.RecordPerson(pNewKing.data.id, pKingdom, kingName, "become_king",
                        kingName + " 即位为 " + pKingdom.name + " 之君");
            }
        }

        // 建国
        public static void OnKingdomFounded(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, "found", pKingdom.name + " 建立");
            KingdomArchiveWriter.Upsert(pKingdom); // 建国快照(名/旗/颜色/建国时间)
        }

        // 亡国
        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, "destroyed", pKingdom.name + " 灭亡");
            // 亡国时 getActorAsset()/getColor() 可能已半失效 → 不再 Upsert 覆盖(避免把好旗帜抹成空);
            // 建国/换君时已存过完整快照。只标记亡国 + destroyed_time。若从未建档(EnsureRow)则补一行兜底。
            KingdomArchiveWriter.EnsureRow(pKingdom);
            KingdomArchiveWriter.MarkDestroyed(pKingdom.id);
        }

        // 城市易主:仅当"旧国非空 且 旧国 != 新国"(真易主),且非读档回填。
        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom, bool pFromLoad)
        {
            if (pFromLoad) return;                                  // 读档回填不记
            if (pCity?.data == null) return;
            if (pOldKingdom == null) return;                        // 初次归属不记
            if (pNewKingdom == null) return;
            if (pOldKingdom == pNewKingdom) return;                 // 无变化不记

            string oldName = pOldKingdom.name;
            string newName = pNewKingdom.name;
            HistoryWriter.RecordCity(pCity, pNewKingdom, "city_transfer",
                pCity.data.name + " 由 " + oldName + " 易主至 " + newName);
        }
    }
}
