using AncientWarfare3.core.policy;

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
            if (!KingdomArchiveWriter.IsArchivable(pKingdom)) return;

            // 防重复:记录上次为该国登记的王 id,相同则跳过。
            pKingdom.data.get(LineageKeys.CHRONICLE_LAST_KING_ID, out long lastKingId, -1L);
            if (lastKingId == pNewKing.data.id) return;
            RecordPreviousKingLostThrone(pKingdom, lastKingId, pNewKing.data.id);
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
            ReignRecordWriter.CloseOpenReign(pKingdom, "replaced");
            DynastyRecordWriter.OnKingChanged(pKingdom, pNewKing);
            ReignRecordWriter.OpenReign(pKingdom, pNewKing);
        }

        private static void RecordPreviousKingLostThrone(Kingdom pKingdom, long pPreviousKingId, long pNewKingId)
        {
            Actor previous = pPreviousKingId < 0 ? null : World.world?.units?.get(pPreviousKingId);
            bool alive = previous?.data != null && !previous.isRekt() && previous.isAlive();
            if (!FormerRulerRecordRules.ShouldRecordLostThrone(pPreviousKingId, pNewKingId, alive)) return;

            string name = previous.getName();
            HistoryText text = HistoryText.Actor(previous, name) + HistoryText.PlainText(" \u5931\u4F4D");
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ABDICATE, text, HistoryTarget.Actor(previous));
            HistoryWriter.RecordPerson(previous.data.id, pKingdom, name,
                PersonEvent.ABDICATE, text, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));
        }

        // 建国
        internal static void OnCollateralRestoration(Kingdom pKingdom, Actor pPreviousKing, Actor pNewKing,
            ShiBranchInfo pRestoredBranch)
        {
            if (pKingdom?.data == null || pNewKing?.data == null) return;

            string label = BuildRestoredShiLabel(pRestoredBranch);
            string color = pRestoredBranch?.origin_kingdom_color;
            if (string.IsNullOrEmpty(color)) color = HistoryColors.FromKingdom(pKingdom);

            HistoryText text = HistoryText.Actor(pNewKing) +
                               HistoryText.PlainText("\u7531\u65c1\u7cfb\u5165\u7ee7\uff0c\u6062\u590d") +
                               HistoryText.Colored(label, color) +
                               HistoryText.PlainText("\u5b97\u7edf");
            if (pPreviousKing?.data != null)
                text += HistoryText.PlainText("\uff08\u524d\u541b ") + HistoryText.Actor(pPreviousKing) +
                        HistoryText.PlainText("\uff09");

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COLLATERAL_RESTORE, text,
                HistoryTarget.Actor(pNewKing));
            HistoryWriter.RecordPerson(pNewKing.data.id, pKingdom, pNewKing.getName(),
                PersonEvent.COLLATERAL_RESTORE, text, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));

            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID, out long periodId, -1L);
            if (periodId >= 0)
            {
                pKingdom.data.get(LineageKeys.MANDATE_VALUE, out int mandateValue, 0);
                MandateService.RecordMandateEvent("succession_collateral_restore", pKingdom, pNewKing,
                    pNewKing.city, 0, mandateValue, text.Plain);
            }
        }

        private static string BuildRestoredShiLabel(ShiBranchInfo pBranch)
        {
            if (pBranch == null) return "\u65e7\u6c0f";
            string city = pBranch.origin_city_name ?? "";
            string clan = pBranch.clan_name ?? "";
            if (string.IsNullOrEmpty(clan)) clan = "\u65e7";
            return city + clan + "\u6c0f";
        }

        public static void OnKingdomFounded(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!KingdomArchiveWriter.IsArchivable(pKingdom)) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.FOUND,
                HistoryText.Kingdom(pKingdom) + " 建立");
            KingdomArchiveWriter.Upsert(pKingdom); // 建国快照(名/旗/颜色/建国时间)
        }

        // 亡国
        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!KingdomArchiveWriter.IsArchivable(pKingdom)) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.DESTROYED,
                HistoryText.Kingdom(pKingdom) + " 灭亡");
            KingdomArchiveWriter.EnsureRow(pKingdom);
            RoyalClaimService.CreateClaimsFromFallenKingdom(pKingdom);
            KingdomArchiveWriter.MarkDestroyed(pKingdom);
            VassalService.OnKingdomDestroyed(pKingdom);
            MandateService.OnKingdomDestroyed(pKingdom);
            // 结构表：关闭该国所有开着的 reign / dynasty / era（kingdom_fell）
            Actor king = pKingdom.king;
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom, "kingdom_fell", king);
            if (king?.data != null)
                PosthumousTitleService.OnReignEnded(pKingdom, king, "kingdom_fell", reign);
            DynastyRecordWriter.CloseOpenDynasty(pKingdom.id, DynastyRecordWriter.END_REASON_KINGDOM_FELL);
            EraRecordWriter.CloseOpenEra(pKingdom.id);
        }

        // 驾崩:在位君主死亡。国家史 + 关 reign + 评谥。
        public static void OnKingDied(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom, "died", pKing);
            PosthumousTitleService.OnReignEnded(pKingdom, pKing, "died", reign);
        }

        // 退位:君主主动让位(仍在世)。国家史 + 人物史 + 关 reign + 评谥。
        public static void OnAbdicate(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            string name = pKing.getName();
            if (SlaveKingAbdicationService.TryConsumeReason(pKing.data.id, out string slaveReason))
            {
                string text = " \u56E0\u6CA6\u4E3A\u5974\u96B6\u9000\u4F4D\uFF08" +
                              SlaveService.ReasonLabel(slaveReason) + "\uFF09";
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ABDICATE,
                    HistoryText.Actor(pKing, name) + text);
                HistoryWriter.RecordPerson(pKing.data.id, pKingdom, name,
                    PersonEvent.ABDICATE, HistoryText.Actor(pKing, name) + text, ChronicleCategory.HONOR);
                ReignRecordWriter.ReignInfo slaveReign =
                    ReignRecordWriter.CloseOpenReign(pKingdom, "abdicated", pKing);
                PosthumousTitleService.OnReignEnded(pKingdom, pKing, "abdicated", slaveReign);
                return;
            }
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ABDICATE,
                HistoryText.Actor(pKing, name) + " 退位");
            if (ChronicleGate.IsNobleActor(pKing))
                HistoryWriter.RecordPerson(pKing.data.id, pKingdom, name,
                    PersonEvent.ABDICATE, HistoryText.Actor(pKing, name) + " 退位", ChronicleCategory.HONOR);
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom, "abdicated", pKing);
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
            WarTerritoryService.EnsureCore(kingdom, pCity, "founded", "自建城市");
        }

        // 城市易主:仅当"旧国非空 且 旧国 != 新国"(真易主),且非读档回填。
        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom, bool pFromLoad)
        {
            if (pFromLoad) return;                                  // 读档回填不记
            if (pCity?.data == null) return;
            if (pOldKingdom == null) return;                        // 初次归属不记
            if (pNewKingdom == null) return;
            if (pOldKingdom == pNewKingdom) return;                 // 无变化不记

            bool oldArchivable = KingdomArchiveWriter.IsArchivable(pOldKingdom);
            bool newArchivable = KingdomArchiveWriter.IsArchivable(pNewKingdom);
            if (!oldArchivable && !newArchivable) return;

            string oldName = pOldKingdom.name;
            string newName = pNewKingdom.name;
            if (!oldArchivable || !newArchivable)
            {
                if (oldArchivable)
                {
                    HistoryWriter.RecordCity(pCity, pOldKingdom, CityEvent.CITY_TRANSFER,
                        HistoryText.City(pCity, pOldKingdom) + " \u8131\u79BB" +
                        HistoryText.Kingdom(pOldKingdom, oldName) + "\uFF0C\u6210\u4E3A\u65E0\u6240\u5C5E\u57CE\u5E02");
                    HistoryWriter.RecordKingdom(pOldKingdom, KingdomEvent.CITY_LOST,
                        HistoryText.PlainText("\u5931\u53BB ") + HistoryText.City(pCity, pOldKingdom) +
                        "\uFF08\u57CE\u5E02\u5E9F\u5F03\u6216\u65E0\u6240\u5C5E\uFF09");
                    KingdomArchiveWriter.Upsert(pOldKingdom);
                }
                else
                {
                    HistoryWriter.RecordCity(pCity, pNewKingdom, CityEvent.CITY_TRANSFER,
                        HistoryText.City(pCity, pNewKingdom) + " \u5F52\u5C5E" +
                        HistoryText.Kingdom(pNewKingdom, newName));
                    HistoryWriter.RecordKingdom(pNewKingdom, KingdomEvent.CITY_GAINED,
                        HistoryText.PlainText("\u593A\u5F97 ") + HistoryText.City(pCity, pNewKingdom) +
                        "\uFF08\u539F\u4E3A\u65E0\u6240\u5C5E\u57CE\u5E02\uFF09");
                    KingdomArchiveWriter.Upsert(pNewKingdom);
                }
                return;
            }
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
            string label = string.IsNullOrEmpty(pWarType) ? "" : "（" + WarDisplayLabelRules.Label(pWarType) + "）";
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
            SlaveService.FlushPendingWarSlaveCaptures(pSelf);
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
            if (!ChronicleGate.IsNobleActor(pActor) && !LineageService.HasOriginalClan(pActor)) return;
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
            if (!ChronicleGate.IsNobleActor(pActor) && !LineageService.HasOriginalClan(pActor)) return;
            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.BECOME_CLAN_CHIEF, HistoryText.Actor(pActor, name) + " 成为家主", ChronicleCategory.CLAN);
        }

        /// <summary>被逐出氏族。</summary>
        public static void OnExiledFromClan(Actor pActor)
        {
            if (pActor?.data == null || !LineageService.IsXia(pActor)) return;
            pActor.data.set(LineageKeys.CHRONICLE_LAST_ORIGINAL_CLAN_ID, -1L);
            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.EXILED_CLAN, HistoryText.Actor(pActor, name) + " 被逐出氏族", ChronicleCategory.CLAN);
        }

        /// <summary>Original WorldBox clan membership, independent from AW lineage.</summary>
        public static void OnJoinedOriginalClan(Actor pActor, Clan pClan)
        {
            if (pActor?.data == null || pClan?.data == null) return;
            if (!LineageService.IsXia(pActor)) return;
            if (AncientWarfare3.core.db.LineageArchiveManager.Instance.OperatingDB == null) return;

            long clanId = pClan.data.id;
            pActor.data.get(LineageKeys.CHRONICLE_LAST_ORIGINAL_CLAN_ID, out long lastClanId, -1L);
            if (lastClanId == clanId) return;
            pActor.data.set(LineageKeys.CHRONICLE_LAST_ORIGINAL_CLAN_ID, clanId);

            string name = pActor.getName();
            string clanName = string.IsNullOrEmpty(pClan.data.name) ? "\u6C0F\u65CF" : pClan.data.name;
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.JOINED_CLAN,
                HistoryText.Actor(pActor, name) + " \u52A0\u5165\u6C0F\u65CF " +
                HistoryText.ClanName(clanName, pClan, pActor.kingdom),
                ChronicleCategory.CLAN,
                HistoryTarget.Actor(pActor));
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
        public static void OnEnslaved(Actor pActor, string pReason, Kingdom pKingdom, City pCity,
            bool pForceNationalRecord = false)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            string reason = SlaveService.ReasonLabel(pReason);
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.ENSLAVED,
                HistoryText.Actor(pActor, name) + " 沦为奴隶（" + reason + "）",
                ChronicleCategory.SOCIAL,
                HistoryTarget.Actor(pActor));

            if (kingdom?.data != null && (pForceNationalRecord || IsNationalSlaveEvent(pActor)))
                HistoryWriter.RecordKingdom(kingdom, KingdomEvent.ENSLAVED,
                    HistoryText.Actor(pActor, name) + " 被俘获为奴（" + reason + "）",
                    HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.ENSLAVED,
                    HistoryText.Actor(pActor, name) + " 在 " + HistoryText.City(city, kingdom) + " 被编入奴籍（" + reason + "）",
                    HistoryTarget.Actor(pActor));
        }

        public static void OnCapturedRulerEnslaved(Actor pActor, string pReason, Kingdom pFormerKingdom,
            Kingdom pCaptorKingdom, City pCaptorCity, Actor pCaptor)
        {
            if (pActor?.data == null || pFormerKingdom?.data == null) return;
            string name = pActor.getName();
            string reason = SlaveService.ReasonLabel(pReason);
            HistoryText captor = pCaptorKingdom?.data != null
                ? HistoryText.Kingdom(pCaptorKingdom)
                : HistoryText.PlainText("\u654c\u56fd");
            HistoryText text = HistoryText.Actor(pActor, name) +
                               HistoryText.PlainText("\u4ee5") +
                               HistoryText.Kingdom(pFormerKingdom) +
                               HistoryText.PlainText("\u541b\u4e3b\u4e4b\u8eab\u88ab") +
                               captor +
                               HistoryText.PlainText("\u4fd8\u83b7\uff0c\u6ca6\u4e3a\u5974\u96b6\uff08" +
                                                     reason + "\uff09");
            if (pCaptor?.data != null)
                text += HistoryText.PlainText("\uff0c\u4fd8\u83b7\u8005 ") + HistoryText.Actor(pCaptor);
            if (pCaptorCity?.data != null)
                text += HistoryText.PlainText("\uff0c\u5b89\u7f6e\u4e8e") +
                        HistoryText.City(pCaptorCity, pCaptorKingdom);

            HistoryWriter.RecordKingdom(pFormerKingdom, KingdomEvent.ENSLAVED, text,
                HistoryTarget.Actor(pActor));
            HistoryWriter.RecordPerson(pActor.data.id, pFormerKingdom, name,
                PersonEvent.ENSLAVED, text, ChronicleCategory.HONOR,
                HistoryTarget.Kingdom(pFormerKingdom));
        }

        public static void OnFreedSlave(Actor pActor, string pReason, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            string reason = SlaveService.ReasonLabel(pReason);
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.FREED_SLAVE,
                HistoryText.Actor(pActor, name) + " 脱离奴籍，成为平民（" + reason + "）",
                ChronicleCategory.SOCIAL,
                HistoryTarget.Actor(pActor));

            if (kingdom?.data != null && IsNationalSlaveEvent(pActor))
                HistoryWriter.RecordKingdom(kingdom, KingdomEvent.FREED_SLAVE,
                    HistoryText.Actor(pActor, name) + " 脱离奴籍（" + reason + "）",
                    HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.FREED_SLAVE,
                    HistoryText.Actor(pActor, name) + " 在 " + HistoryText.City(city, kingdom) + " 脱离奴籍（" + reason + "）",
                    HistoryTarget.Actor(pActor));
        }

        public static void OnRetiredSoldier(Actor pActor, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.RETIRED_SOLDIER,
                HistoryText.Actor(pActor, name) + " 退伍为老兵，不再应征",
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.RETIRED_SOLDIER,
                    HistoryText.Actor(pActor, name) + " 自 " + HistoryText.City(city, kingdom) + " 退伍",
                    HistoryTarget.Actor(pActor));
        }

        public static void OnSlaveEnlisted(Actor pActor, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.SLAVE_ENLISTED,
                HistoryText.Actor(pActor, name) + " 以奴隶兵身份入伍",
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.SLAVE_ENLISTED,
                    HistoryText.Actor(pActor, name) + " 在 " + HistoryText.City(city, kingdom) + " 被编入奴隶军",
                    HistoryTarget.Actor(pActor));
        }

        public static void OnSlaveMerit(Actor pActor, int pPoints, int pTotal, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;
            string meritText = "立下军功 " + pPoints + " 点，累计 " + pTotal + " 点";

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.SLAVE_MERIT,
                HistoryText.Actor(pActor, name) + meritText,
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.SLAVE_MERIT,
                    HistoryText.Actor(pActor, name) + " 奴隶兵" + meritText,
                    HistoryTarget.Actor(pActor));
        }

        public static void OnSlaveArmyFormed(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.SLAVE_ARMY_FORMED,
                HistoryText.Kingdom(pKingdom) + " 开始编组奴隶军");

            if (pCity?.data != null)
                HistoryWriter.RecordCity(pCity, pKingdom, CityEvent.SLAVE_ARMY_FORMED,
                    HistoryText.City(pCity, pKingdom) + " 开始编组奴隶军");
        }

        public static void OnSlaveLaborStarted(Kingdom pKingdom, City pCity, int pSlaveCount)
        {
            if (pCity?.data != null)
                HistoryWriter.RecordCity(pCity, pKingdom, CityEvent.SLAVE_LABOR_STARTED,
                    HistoryText.City(pCity, pKingdom) + " 登记奴隶劳役，奴隶 " + pSlaveCount.ToString() + " 人");
        }

        public static void OnWarSlavesCaptured(Kingdom pKingdom, City pCity, string pCityName, int pSlaveCount)
        {
            if (pSlaveCount <= 0) return;
            bool hasCity = IsHistoryCityValid(pCity);
            HistoryText cityText = HistoryText.City(pCity, pKingdom,
                string.IsNullOrEmpty(pCityName) ? "\u67D0\u57CE" : pCityName);
            if (pKingdom?.data != null)
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ENSLAVED,
                    HistoryText.Kingdom(pKingdom) + " \u5728\u6218\u4E89\u7ED3\u7B97\u4E2D\u4FD8\u83B7\u5974\u96B6 " + pSlaveCount.ToString() +
                    " \u540D\uFF0C\u5B89\u7F6E\u4E8E " + cityText,
                    hasCity ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pKingdom));

            if (hasCity)
                HistoryWriter.RecordCity(pCity, pKingdom, CityEvent.ENSLAVED,
                    cityText + " \u5B89\u7F6E\u6218\u4E89\u4FD8\u83B7\u5974\u96B6 " +
                    pSlaveCount.ToString() + " \u540D");
        }

        private static bool IsHistoryCityValid(City pCity)
        {
            try { return pCity?.data != null && pCity.data.id >= 0; }
            catch { return false; }
        }

        public static void OnRoyalGuardFormed(Kingdom pKingdom, string pGuardName)
        {
            if (pKingdom?.data == null) return;
            string guardName = string.IsNullOrEmpty(pGuardName) ? "\u7981\u536B\u519B" : pGuardName;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ROYAL_GUARD_FORMED,
                HistoryText.Kingdom(pKingdom) + " \u8BBE\u7ACB" + HistoryText.PlainText(guardName),
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnRoyalGuardAppointed(Actor pActor, Kingdom pKingdom, City pCity,
            string pGuardName, bool pCaptain)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;
            string guardName = string.IsNullOrEmpty(pGuardName) ? "\u7981\u536B\u519B" : pGuardName;
            string role = pCaptain ? "\u7EDF\u9886" : "\u5165\u9009";

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.ROYAL_GUARD_APPOINTED,
                HistoryText.Actor(pActor, name) + " " + role + HistoryText.PlainText(guardName),
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.ROYAL_GUARD_APPOINTED,
                    HistoryText.Actor(pActor, name) + " \u5728" + HistoryText.City(city, kingdom) +
                    " " + role + HistoryText.PlainText(guardName),
                    HistoryTarget.Actor(pActor));
        }

        public static void OnRoyalGuardDismissed(Actor pActor, Kingdom pKingdom, City pCity, string pReason)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;
            string reason = RoyalGuardReasonLabel(pReason);

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.ROYAL_GUARD_DISMISSED,
                HistoryText.Actor(pActor, name) + " \u79BB\u5F00\u7981\u536B\u519B\uFF08" + reason + "\uFF09",
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.ROYAL_GUARD_DISMISSED,
                    HistoryText.Actor(pActor, name) + " \u5728" + HistoryText.City(city, kingdom) +
                    " \u79BB\u5F00\u7981\u536B\u519B\uFF08" + reason + "\uFF09",
                    HistoryTarget.Actor(pActor));
        }

        private static bool IsNationalSlaveEvent(Actor pActor)
        {
            return pActor != null && (pActor.isKing() || pActor.isCityLeader() || ChronicleGate.IsImportant(pActor));
        }

        private static string RoyalGuardReasonLabel(string pReason)
        {
            return pReason switch
            {
                "died" => "\u6218\u6B7B\u6216\u8EAB\u6545",
                "no_king" => "\u65E0\u5728\u4F4D\u541B\u4E3B",
                "no_noble_captain" => "\u65E0\u8D35\u65CF\u7EDF\u9886",
                "over_limit" => "\u540D\u989D\u8C03\u6574",
                "invalid" => "\u8D44\u683C\u4E0D\u7B26",
                "enslaved" => "\u6CA6\u4E3A\u5974\u96B6",
                "became_leader" => "\u53D7\u4EFB\u57CE\u4E3B",
                _ => string.IsNullOrEmpty(pReason) ? "\u79BB\u4EFB" : pReason
            };
        }

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
            if (!DeathBondRules.ShouldRecordBondDeathForParentsAndLover(pDeadIsTraceable: true)) return;
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

            bool actorChildrenAvailable = TryCollectActorChildren(pParent, result);
            if (!DeathBondRules.ShouldUseWorldScanForChildren(
                    pCanUseActorChildrenList: actorChildrenAvailable,
                    pDeadIsImportant: IsImportantForBondDeathChildFallback(pParent)))
                return result;

            Bench.bench(CityMaintenanceBenchmarkRules.DeathBondChildScan, CityMaintenanceBenchmarkRules.Group);
            long pid = pParent.data.id;
            foreach (Actor a in World.world.units)
            {
                if (a?.data == null || a == pParent) continue;
                if (a.data.parent_id_1 == pid || a.data.parent_id_2 == pid) result.Add(a);
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.DeathBondChildScan, CityMaintenanceBenchmarkRules.Group);
            return result;
        }

        private static bool TryCollectActorChildren(Actor pParent, System.Collections.Generic.List<Actor> pResult)
        {
            if (pParent?.data == null || pResult == null) return false;
            try
            {
                foreach (Actor child in pParent.getChildren(pOnlyCurrentFamily: false))
                {
                    if (child?.data == null || child == pParent) continue;
                    pResult.Add(child);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsImportantForBondDeathChildFallback(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (ChronicleGate.IsImportant(pActor) || ChronicleGate.IsNobleActor(pActor)) return true;
            try { return pActor.isArmyGroupLeader(); }
            catch { return false; }
        }
    }
}
