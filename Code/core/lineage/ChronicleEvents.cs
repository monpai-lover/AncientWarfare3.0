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
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RULE_CHANGE,
                HistoryText.Actor(pNewKing, kingName) + " 即位为君");
            KingdomArchiveWriter.Upsert(pKingdom);

            // 人物·成王(仅入谱贵族)
            if (ChronicleGate.IsNobleActor(pNewKing))
                HistoryWriter.RecordPerson(pNewKing.data.id, pKingdom, kingName, PersonEvent.BECOME_KING,
                    HistoryText.Actor(pNewKing, kingName) + " 即位为 " + HistoryText.Kingdom(pKingdom) + " 之君",
                    ChronicleCategory.HONOR);

            // 结构表：君主世系 + 朝代（先关旧 reign，再开新 reign）
            ReignRecordWriter.CloseOpenReign(pKingdom.id, "replaced");
            DynastyRecordWriter.OnKingChanged(pKingdom, pNewKing);
            ReignRecordWriter.OpenReign(pKingdom, pNewKing);
        }

        // 建国
        public static void OnKingdomFounded(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.FOUND,
                HistoryText.Kingdom(pKingdom) + " 建立");
            KingdomArchiveWriter.Upsert(pKingdom); // 建国快照(名/旗/颜色/建国时间)
        }

        // 亡国
        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.DESTROYED,
                HistoryText.Kingdom(pKingdom) + " 灭亡");
            KingdomArchiveWriter.EnsureRow(pKingdom);
            KingdomArchiveWriter.MarkDestroyed(pKingdom);
            // 结构表：关闭该国所有开着的 reign / dynasty / era（kingdom_fell）
            ReignRecordWriter.CloseOpenReign(pKingdom.id, "kingdom_fell");
            DynastyRecordWriter.CloseOpenDynasty(pKingdom.id);
            EraRecordWriter.CloseOpenEra(pKingdom.id);
        }

        // 驾崩:在位君主死亡。国家史 + 关 reign + 评谥。
        public static void OnKingDied(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.KING_DIED,
                HistoryText.Actor(pKing) + " 驾崩");
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom.id, "died");
            PosthumousTitleService.OnReignEnded(pKingdom, pKing, "died", reign);
        }

        // 退位:君主主动让位(仍在世)。国家史 + 人物史 + 关 reign + 评谥。
        public static void OnAbdicate(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            string name = pKing.getName();
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ABDICATE,
                HistoryText.Actor(pKing, name) + " 退位");
            if (ChronicleGate.IsNobleActor(pKing))
                HistoryWriter.RecordPerson(pKing.data.id, pKingdom, name,
                    PersonEvent.ABDICATE, HistoryText.Actor(pKing, name) + " 退位", ChronicleCategory.HONOR);
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom.id, "abdicated");
            PosthumousTitleService.OnReignEnded(pKingdom, pKing, "abdicated", reign);
        }

        // 建城:City.newCityEvent(纯新建城,不含读档)。记一条 found 事件作城市史起点。
        public static void OnCityFounded(City pCity)
        {
            if (pCity?.data == null) return;
            Kingdom kingdom = pCity.kingdom;                 // 建城者所属国(newCityEvent 时已设)
            string cityName = pCity.data.name;
            HistoryText kingdomPart = kingdom != null
                ? HistoryText.PlainText("隶属于 ") + HistoryText.Kingdom(kingdom) + " 的"
                : HistoryText.PlainText("");
            HistoryWriter.RecordCity(pCity, kingdom, CityEvent.CITY_FOUND,
                HistoryText.City(pCity, kingdom, cityName) + " 作为" + kingdomPart + "城市建立");
            KingdomArchiveWriter.Upsert(kingdom);
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
            HistoryWriter.RecordCity(pCity, pNewKingdom, CityEvent.CITY_TRANSFER,
                HistoryText.City(pCity, pNewKingdom) + " 由 " + HistoryText.Kingdom(pOldKingdom, oldName) +
                " 易主至 " + HistoryText.Kingdom(pNewKingdom, newName));

            // 国家视角(批2):旧国失城、新国得城(同一信号,双国各记 KingdomHistory)。
            HistoryWriter.RecordKingdom(pOldKingdom, KingdomEvent.CITY_LOST,
                HistoryText.PlainText("失去 ") + HistoryText.City(pCity, pOldKingdom) +
                "(归 " + HistoryText.Kingdom(pNewKingdom, newName) + ")");
            HistoryWriter.RecordKingdom(pNewKingdom, KingdomEvent.CITY_GAINED,
                HistoryText.PlainText("夺得 ") + HistoryText.City(pCity, pNewKingdom) +
                "(原属 " + HistoryText.Kingdom(pOldKingdom, oldName) + ")");
            KingdomArchiveWriter.Upsert(pOldKingdom);
            KingdomArchiveWriter.Upsert(pNewKingdom);
        }

        // 战争开始:给双方各记一条 war_start 国家史(由 AW_WarPatch 分别传入自身国)。
        public static void OnWarStart(Kingdom pSelf, string pOpponentName, string pWarType)
        {
            OnWarStart(pSelf, null, pOpponentName, pWarType);
        }

        public static void OnWarStart(Kingdom pSelf, Kingdom pOpponent, string pOpponentName, string pWarType)
        {
            if (pSelf?.data == null) return;
            string label = string.IsNullOrEmpty(pWarType) ? "" : "(" + pWarType + ")";
            HistoryWriter.RecordKingdom(pSelf, KingdomEvent.WAR_START,
                HistoryText.PlainText("与 ") + HistoryText.Kingdom(pOpponent, pOpponentName) + " 爆发战争" + label);
        }

        // 战争结束:给双方各记一条 war_end 国家史。
        public static void OnWarEnd(Kingdom pSelf, string pOpponentName, string pResult)
        {
            OnWarEnd(pSelf, null, pOpponentName, HistoryText.PlainText(pResult));
        }

        public static void OnWarEnd(Kingdom pSelf, Kingdom pOpponent, string pOpponentName, HistoryText pResult)
        {
            if (pSelf?.data == null) return;
            HistoryWriter.RecordKingdom(pSelf, KingdomEvent.WAR_END,
                HistoryText.PlainText("与 ") + HistoryText.Kingdom(pOpponent, pOpponentName) + " 的战争结束:" + pResult);
        }

        // ───────────────────────── 人物事件(批1) ─────────────────────────

        /// <summary>父母得子:给贵族父/母各记一条"喜得子/女"。baby 出生已由谱系系统处理,此处只记父母视角。</summary>
        public static void OnHadChild(Actor pParent1, Actor pParent2, Actor pBaby)
        {
            if (pBaby?.data == null) return;
            string babyName = pBaby.getName();
            string kind = pBaby.isSexMale() ? "子" : "女";
            RecordParentHadChild(pParent1, pBaby, babyName, kind);
            RecordParentHadChild(pParent2, pBaby, babyName, kind);
        }

        private static void RecordParentHadChild(Actor pParent, Actor pBaby, string pBabyName, string pKind)
        {
            if (!ChronicleGate.IsNobleActor(pParent)) return;
            HistoryWriter.RecordPerson(pParent.data.id, pParent.kingdom, pParent.getName(),
                PersonEvent.HAD_CHILD,
                HistoryText.Actor(pParent) + " 喜得" + pKind + " " + HistoryText.Actor(pBaby, pBabyName),
                ChronicleCategory.LIFE);
        }

        /// <summary>封城主。</summary>
        public static void OnBecomeLeader(Actor pActor)
        {
            if (!ChronicleGate.IsNobleActor(pActor)) return;
            string name = pActor.getName();
            City city = pActor.city;
            string cityName = city?.data != null ? city.data.name : "某城";
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.BECOME_LEADER,
                HistoryText.Actor(pActor, name) + " 受封为 " + HistoryText.City(city, pActor.kingdom, cityName) + " 城主",
                ChronicleCategory.HONOR);
        }

        /// <summary>成为家主(氏族族长)。</summary>
        public static void OnBecomeClanChief(Actor pActor)
        {
            if (!ChronicleGate.IsNobleActor(pActor)) return;
            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.BECOME_CLAN_CHIEF, HistoryText.Actor(pActor, name) + " 成为家主", ChronicleCategory.CLAN);
        }

        /// <summary>被逐出氏族。</summary>
        public static void OnExiledFromClan(Actor pActor)
        {
            if (!ChronicleGate.IsNobleActor(pActor)) return;
            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.EXILED_CLAN, HistoryText.Actor(pActor, name) + " 被逐出氏族", ChronicleCategory.CLAN);
        }

        /// <summary>发动叛乱:人物记一条 + 原属国国家史记一条。</summary>
        public static void OnRebellion(Actor pActor, Kingdom pOldKingdom)
        {
            string name = pActor != null ? pActor.getName() : "某人";
            if (ChronicleGate.IsNobleActor(pActor))
                HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                    PersonEvent.REBELLION, HistoryText.Actor(pActor, name) + " 起兵反叛", ChronicleCategory.WAR);
            if (pOldKingdom?.data != null)
                HistoryWriter.RecordKingdom(pOldKingdom, KingdomEvent.REBELLION,
                    HistoryText.Actor(pActor, name) + " 在境内起兵反叛");
        }

        /// <summary>入伍(成为战士)。仅贵族。</summary>
        public static void OnEnlisted(Actor pActor)
        {
            if (!ChronicleGate.IsNobleActor(pActor)) return;
            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.ENLISTED, HistoryText.Actor(pActor, name) + " 入伍从军", ChronicleCategory.WAR);
        }

        /// <summary>
        ///     重要击杀:凶手是贵族 → 给凶手记一条;被杀者是王/城主/名人 → 额外给被杀者所属国国家史记一条。
        /// </summary>
        public static void OnImportantKill(Actor pKiller, Actor pDead, Kingdom pDeadPrevKingdom)
        {
            if (pKiller?.data == null || pDead?.data == null) return;
            bool deadImportant = ChronicleGate.IsImportant(pDead);

            // 凶手视角(凶手贵族才记;或被杀者是重要人物也值得给贵族凶手记)。
            if (ChronicleGate.IsNobleActor(pKiller) && (deadImportant || ChronicleGate.IsImportant(pKiller)))
            {
                string kname = pKiller.getName();
                HistoryWriter.RecordPerson(pKiller.data.id, pKiller.kingdom, kname,
                    PersonEvent.IMPORTANT_KILL,
                    HistoryText.Actor(pKiller, kname) + " 击杀了 " + HistoryText.Actor(pDead),
                    ChronicleCategory.WAR);
            }

            // 被杀重要人物 → 国家史留痕。
            if (deadImportant && pDeadPrevKingdom?.data != null)
                HistoryWriter.RecordKingdom(pDeadPrevKingdom, KingdomEvent.NOTABLE_DEATH,
                    HistoryText.Actor(pDead) + " 为 " + HistoryText.Actor(pKiller) + " 所杀");
        }

        // 恋爱双向去重:同一对(min_max id)本会话只记一次。
        private static readonly System.Collections.Generic.HashSet<string> _loverPairs =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>坠入爱河:双方各记一条(贵族门槛),同一对去重。</summary>
        public static void OnBecameLovers(Actor pA, Actor pB)
        {
            if (pA?.data == null || pB?.data == null) return;
            long a = pA.data.id, b = pB.data.id;
            string key = (a < b ? a + "_" + b : b + "_" + a);
            if (!_loverPairs.Add(key)) return; // 已记过这一对

            RecordLover(pA, pB);
            RecordLover(pB, pA);
        }

        private static void RecordLover(Actor pSelf, Actor pOther)
        {
            if (!ChronicleGate.IsNobleActor(pSelf)) return;
            string name = pSelf.getName();
            HistoryWriter.RecordPerson(pSelf.data.id, pSelf.kingdom, name,
                PersonEvent.FELL_IN_LOVE,
                HistoryText.Actor(pSelf, name) + " 与 " + HistoryText.Actor(pOther) + " 坠入爱河",
                ChronicleCategory.BOND);
        }

        /// <summary>
        ///     牵绊离世:死者的在世父母 / 配偶 / 子女中,贵族者各记一条"痛失至亲"。
        ///     在 Die_Prefix 调用(死者数据仍完整),死者本身可平民。
        /// </summary>
        public static void OnBondDeath(Actor pDead)
        {
            if (pDead?.data == null) return;
            string deadName = pDead.getName();

            // 配偶
            Actor lover = pDead.hasLover() ? pDead.lover : null;
            RecordBondDeath(lover, pDead, deadName, "伴侣");

            // 父母(用 data 上的 parent id 取,已验证字段;避免依赖 getParents 的具体返回类型)
            RecordBondDeath(GetUnit(pDead.data.parent_id_1), pDead, deadName, "亲人");
            RecordBondDeath(GetUnit(pDead.data.parent_id_2), pDead, deadName, "亲人");

            // 子女
            foreach (Actor child in GetChildren(pDead))
                RecordBondDeath(child, pDead, deadName, "亲人");
        }

        private static Actor GetUnit(long pId)
        {
            return pId > 0 ? World.world.units.get(pId) : null;
        }

        private static void RecordBondDeath(Actor pMourner, Actor pDead, string pDeadName, string pRelation)
        {
            if (pMourner == null || pMourner == pDead) return;
            if (pMourner.isRekt() || !pMourner.isAlive()) return; // 悼念者须在世
            if (!ChronicleGate.IsNobleActor(pMourner)) return;
            string name = pMourner.getName();
            HistoryWriter.RecordPerson(pMourner.data.id, pMourner.kingdom, name,
                PersonEvent.BOND_DEATH,
                HistoryText.Actor(pMourner, name) + " 痛失" + pRelation + " " + HistoryText.Actor(pDead, pDeadName),
                ChronicleCategory.BOND);
        }

        // 取子女:遍历死者所在世界单位,找 parent 是死者的(数量小,死亡时一次性)。
        private static System.Collections.Generic.IEnumerable<Actor> GetChildren(Actor pParent)
        {
            var result = new System.Collections.Generic.List<Actor>();
            if (pParent?.data == null) return result;
            long pid = pParent.data.id;
            foreach (Actor a in World.world.units)
            {
                if (a?.data == null || a == pParent) continue;
                if (a.data.parent_id_1 == pid || a.data.parent_id_2 == pid) result.Add(a);
            }
            return result;
        }
    }
}
