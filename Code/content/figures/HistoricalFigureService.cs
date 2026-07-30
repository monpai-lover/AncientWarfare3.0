using System;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.content.figures
{
    /// <summary>
    ///     历代开国君主降临—— AW2 SpecialFigure 的新版重做。
    ///
    ///     相比 AW2 的改进:
    ///     - **随存档持久化**(FigureStateStore/SQLite),根治重进档重复生成。
    ///     - 钩 Actor.newCreature(AW2 钩的 spawnPopPoint 新版已删),复用出生分流。
    ///     - 随机用**私有 System.Random**(UnityEngine.Random 被 MapBox 固定播种,见 aw3-random-seed-pitfall)。
    ///
    ///     规则:严格顺序生成(前一个死后才轮下一个);存活互斥(同时只一个 figure);
    ///     无天命国(留桩,恒 true);刘邦起需世上已有夏人国姓氏合流;预设姓氏(姬=姓姬/氏姬…);
    ///     health 1500 + favorite + trait figure/first;成为 king 时套用国名;降临发世界日志。
    /// </summary>
    public static class HistoricalFigureService
    {
        // 开关:用 NML toggle GodPower + PlayerConfig 持久化(随玩家配置,非随存档)。
        public const string TOGGLE_NAME = "aw_figure_enabled";
        public const string TOGGLE_POWER_ID = "aw_toggle_figure";

        // 世界日志资产 id(message.csv:historicalMessage = "特殊人物$ren$降临这个世界")。
        public const string LOG_ASSET_ID = "historicalMessage";

        public const string TRAIT_FIGURE = "figure";
        public const string TRAIT_FIRST = "first";
        private const int FIGURE_HEALTH = 1500;

        // mod 私有随机(绝不用 UnityEngine.Random)。
        private static readonly System.Random Rng = new System.Random();

        private static bool _inited;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;
            RegisterToggleOption();
            RegisterTogglePower();
            RegisterWorldLog();
            // 兜底:首次加载若已在某存档中(load 钩可能早于 mod 加载),主动载一次生成状态。
            // 之后 AW_SavePatch 的 load/新世界钩会再刷新。
            FigureStateStore.Load();
        }

        // ───────────────────────── 注册:开关 / 日志 ─────────────────────────

        /// <summary>先注册 OptionAsset + PlayerConfig 项,default=true(默认开启)。
        /// 必须早于 CreateToggleButton(否则它建的 default_bool=false 会让默认关闭)。</summary>
        private static void RegisterToggleOption()
        {
            if (AssetManager.options_library.get(TOGGLE_NAME) == null)
            {
                AssetManager.options_library.add(new OptionAsset
                {
                    id = TOGGLE_NAME,
                    default_bool = true,
                    type = OptionType.Bool
                });
            }
            if (!PlayerConfig.dict.ContainsKey(TOGGLE_NAME))
            {
                PlayerConfig.instance.data.add(new PlayerOptionData(TOGGLE_NAME) { boolVal = true });
            }
        }

        /// <summary>注册一个 toggle 类型 GodPower 供 AW_LineageTab 的 CreateToggleButton 绑定。</summary>
        private static void RegisterTogglePower()
        {
            if (AssetManager.powers.get(TOGGLE_POWER_ID) != null) return;
            var p = new GodPower
            {
                id = TOGGLE_POWER_ID,
                name = TOGGLE_POWER_ID,        // 本地化键(others.csv 注册标题/描述)
                unselect_when_window = true
            };
            p.toggle_name = TOGGLE_NAME;       // 与上面 OptionAsset/PlayerConfig 同名 → 共享开关值
            AssetManager.powers.add(p);
        }

        /// <summary>注册 historicalMessage 世界日志资产:$ren$ → 带王国色的人名。</summary>
        private static void RegisterWorldLog()
        {
            if (AssetManager.world_log_library.get(LOG_ASSET_ID) != null) return;
            AssetManager.world_log_library.add(new WorldLogAsset
            {
                id = LOG_ASSET_ID,
                group = "kings",
                path_icon = "ui/Icons/iconXias",
                color = Toolbox.color_log_neutral,
                text_replacer = (WorldLogMessage pMsg, ref string pText) =>
                {
                    // special1 已是带颜色标记的人名(ApplyFigure 里 Toolbox.coloredString 包好)。
                    pText = pText.Replace("$ren$", pMsg.special1 ?? "");
                }
            });
        }

        /// <summary>开关是否开启(默认开)。读 PlayerConfig 持久值。</summary>
        public static bool Enabled
        {
            get
            {
                if (PlayerConfig.dict.TryGetValue(TOGGLE_NAME, out var opt)) return opt.boolVal;
                return true; // 无配置项视为开
            }
        }

        /// <summary>当前是否已有天命王朝。历史人物顺序生成会被现存天命国阻断。</summary>
        public static bool HasMandateKingdom() => core.lineage.MandateService.Exists;

        // ───────────────────────── 生成 ─────────────────────────

        /// <summary>诊断开关:定位"历史人物不自然产生"时设 true,会 LogInfo 每个早退原因 + 触发源。定位后关回 false。</summary>
        public static bool DiagnoseSpawn = false;

        /// <summary>由 AW_FigurePatch 在新单位出生(newCreature / applyParentsMeta Postfix)后调用。门槛检查 + 掷骰 + 降临。
        /// pSource 仅用于诊断日志标注触发路径(newCreature=神力/城市补人;baby=繁殖)。</summary>
        public static void TrySpawnOn(Actor pActor, string pSource = "?")
        {
            // —— cheap guard,按代价从低到高 ——
            if (!Enabled) { Diag(pSource, "toggle 关闭"); return; }
            // ⚠ 不能用 !isAlive() 卡:繁殖 baby 在 applyParentsMeta 钩点(makeBaby 中途)可能尚未注册为 alive
            //   (BabyMaker 后续才设性别/营养/标记),isAlive() 可能为假 → 历史人物永远不走繁殖路径降临
            //   (= 用户报"只能神力刷,不自然产生"的根因之一)。放宽为 data!=null 且非 rekt(已死/无效)。
            if (pActor?.data == null || pActor.isRekt()) { Diag(pSource, "actor 空/已死"); return; }
            if (pActor.isBaby() || pActor.isEgg()) { Diag(pSource, "baby/egg blocked"); return; }
            if (!LineageService.IsXia(pActor)) return; // 非夏人:沉默(每帧大量非夏单位,不刷日志)
            // ⚠ 不能用 isKingdomCiv()(=kingdom.isCiv()):Actor.newCreature() 里 kingdom 被设为 null
            //   (Actor.cs:1048),此钩为 newCreature Postfix,kingdom 恒 null → isKingdomCiv 恒 false/NullRef
            //   → 历史人物**永远不生成**(根因)。改用种族文明标志 asset.civ(对齐 AW2 的 race.civilization,不依赖 kingdom)。
            if (pActor.asset == null || !pActor.asset.civ) { Diag(pSource, "asset 非 civ:" + (pActor.asset?.id ?? "null")); return; }
            if (pActor.hasTrait(TRAIT_FIGURE) || pActor.hasTrait(TRAIT_FIRST)) { Diag(pSource, "已是 figure"); return; }
            if (!HistoricalFigureSpawnRules.CanEvaluate(FigureStateStore.IsReady))
            {
                Diag(pSource, "lineage archive unavailable");
                return;
            }

            // —— 存活互斥 + 天命国 ——
            if (FigureStateStore.AnyAliveFigure()) { Diag(pSource, "已有 figure 存活(互斥)"); return; }
            if (HasMandateKingdom()) { Diag(pSource, "天命国阻断"); return; }

            // —— 严格顺序:取当前应生成的那个人 ——
            int idx = FigureStateStore.NextSpawnableIndex();
            if (idx < 0) { Diag(pSource, "NextSpawnableIndex=-1(前一个还活着/全生成完)"); return; }
            var def = HistoricalFigureDef.Get(idx);
            if (def == null) return;

            if (!HistoricalFigureSpawnRules.IsDefinitionSpawnable(def.Id,
                    def.RegistryIndex, def.SpawnOrder, def.Chance))
            {
                Diag(pSource, def.Key + " 定义不可生成");
                return;
            }

            // —— 合流门:刘邦起需降临目标所在国完成姓氏合流 ——
            bool integrationReady = !def.RequiresIntegration ||
                                    IsSpawnKingdomIntegrated(pActor);
            if (!HistoricalFigureSpawnRules.CanAttemptDefinition(
                    def.RequiresIntegration, integrationReady, def.Chance))
            {
                Diag(pSource, def.Key + " 需所在国姓氏合流未满足");
                return;
            }

            // —— 掷骰(私有 Random) ——
            if (Rng.NextDouble() >= def.Chance) { Diag(pSource, def.Key + " 掷骰未中(chance=" + def.Chance + ")"); return; }

            ModClass.LogInfo($"历史人物命中:source={pSource} idx={idx} key={def.Key}");
            ApplyFigure(pActor, def, idx, integrationReady);
        }

        private static void Diag(string pSource, string pReason)
        {
            if (DiagnoseSpawn) ModClass.LogInfo($"[FigureDiag] source={pSource} 早退:{pReason}");
        }

        /// <summary>刘邦起的合流门只看降临目标所在国,避免别国完成合流后误放行到未合流国家。</summary>
        private static bool IsSpawnKingdomIntegrated(Actor pActor)
        {
            Kingdom kingdom = ResolveSpawnKingdom(pActor);
            return kingdom != null &&
                   LineageService.IsXiaKingdom(kingdom) &&
                   LineageService.IsKingdomIntegrated(kingdom);
        }

        private static Kingdom ResolveSpawnKingdom(Actor pActor)
        {
            if (pActor?.kingdom?.data != null && !pActor.kingdom.isRekt()) return pActor.kingdom;
            Kingdom cityKingdom = pActor?.city?.kingdom;
            if (cityKingdom?.data != null && !cityKingdom.isRekt()) return cityKingdom;
            return null;
        }

        /// <summary>降临:设属性 + 注入预设姓氏 + 标记持久化 + 发世界日志。</summary>
        private static void ApplyFigure(Actor pActor, HistoricalFigureDef pDef,
            int pIndex, bool pIntegrationReady)
        {
            var snapshot = new ActorFigureSnapshot(pActor);
            bool reservationCommitted = FigureStateStore.TryReserveSpawn(
                pIndex, pDef.Id, pActor.data.id, LineageService.CurTime());
            if (!HistoricalFigureSpawnRules.CanMutate(reservationCommitted))
            {
                Diag("reservation", pDef.Key + " persistence deferred");
                return;
            }

            long lineageId = -1;
            long shiId = -1;
            try
            {
                // 1) 基础属性:满血 1500、收藏、figure+first 特质。
                pActor.addTrait(TRAIT_FIGURE);
                pActor.addTrait(TRAIT_FIRST);
                pActor.setHealth(FIGURE_HEALTH);
                pActor.data.favorite = true;

                // 须在 OnActorPromoted→ApplyDisplayName 之前设置，后者会按性别拼名。
                pActor.data.sex = HistoricalFigureSpawnRules.IsFemale(pDef.Sex)
                    ? ActorSex.Female
                    : ActorSex.Male;

                // 2) 注入预设姓氏(不走随机):先手建姓族+氏支拿 id,再 set 字段,再晋升。
                //    必须先 set LINEAGE_ID,EnsureLineageForNoble 见已有谱系即跳过随机生成(LineageService.cs:199)。
                lineageId = LineageIdAllocator.NextLineageId();
                shiId = LineageIdAllocator.NextShiId();
                if (!FigureStateStore.TryBindPendingLineage(pIndex,
                        pActor.data.id, lineageId, shiId))
                    throw new InvalidOperationException(
                        "historical figure pending lineage bind failed");
                LineageService.InsertLineageGroup(lineageId, pDef.FamilyName,
                    pActor);
                LineageService.InsertShiBranch(shiId, lineageId, pDef.ClanName,
                    pActor, ShiSourceType.SPECIAL_FIGURE);

                pActor.data.set(LineageKeys.LINEAGE_ID, lineageId);
                pActor.data.set(LineageKeys.SHI_ID, shiId);
                pActor.data.set(LineageKeys.FAMILY_NAME, pDef.FamilyName);
                pActor.data.set(LineageKeys.CLAN_NAME, pDef.ClanName);
                pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME,
                    pDef.FamilyName);
                pActor.data.set(LineageKeys.GIVEN_NAME, pDef.GivenName);
                pActor.data.set(LineageKeys.NAME_INTEGRATED,
                    HistoricalFigureSpawnRules.ShouldUseIntegratedName(
                        pDef.RequiresIntegration, pIntegrationReady));
                pActor.data.set(LineageKeys.FOUNDED_BRANCH_SHI_ID, -1L);

                if (!FigureStateStore.TryCommitSpawn(pIndex, pActor.data.id))
                    throw new InvalidOperationException(
                        "historical figure reservation commit failed");
            }
            catch (Exception error)
            {
                try { snapshot.Restore(pActor); }
                catch (Exception restoreError)
                {
                    ModClass.LogWarning("Historical figure actor rollback failed: " +
                        restoreError.Message);
                }
                bool aborted = FigureStateStore.TryAbortSpawn(
                    pIndex, pActor.data.id);
                ModClass.LogWarning("Historical figure initialization failed: " +
                    error.Message + (aborted ? "" : "; reservation abort deferred"));
                return;
            }

            try
            {
                LineageService.OnActorPromoted(pActor, NobleTrigger.Figure);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical figure promotion failed: " +
                    error.Message);
                QueueFigurePromotionRepair(pActor.data.id);
            }

            // 4) 世界日志公告:特殊人物$ren$降临这个世界($ren$=带国色的人名)。
            AnnounceFigure(pActor, pDef);

            // 编年史:历史人物降临 = 一次"出生"事件(预设姓名已就绪)。
            try
            {
                core.lineage.HistoryWriter.RecordPerson(
                    pActor.data.id, pActor.kingdom, pActor.getName(), "birth",
                    core.lineage.HistoryText.Actor(pActor) +
                    core.lineage.HistoryLocalizationRules.H("aw_hist_figure_arrived"));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical figure history write failed: " +
                    error.Message);
            }

            ModClass.LogInfo($"历史人物降临:{pDef.Key}(序号 {pIndex},国名预留 {pDef.KingdomName})");
        }

        private static void QueueFigurePromotionRepair(long pActorId)
        {
            if (pActorId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "historical_figure_promotion", pActorId),
                DeferredWorkClass.Runtime,
                () =>
                {
                    Actor actor = World.world?.units?.get(pActorId);
                    if (actor?.data == null || actor.isRekt()) return;
                    LineageService.OnActorPromoted(actor, NobleTrigger.Figure);
                });
        }

        private sealed class ActorFigureSnapshot
        {
            private readonly int _health;
            private readonly bool _favorite;
            private readonly ActorSex _sex;
            private readonly string _name;
            private readonly bool _customName;
            private readonly bool _hadFigure;
            private readonly bool _hadFirst;
            private readonly bool _hadGuizu;
            private readonly LongDataSnapshot _lineageId;
            private readonly LongDataSnapshot _shiId;
            private readonly LongDataSnapshot _foundedBranchShiId;
            private readonly IntDataSnapshot _nobleDistance;
            private readonly StringDataSnapshot _familyName;
            private readonly StringDataSnapshot _clanName;
            private readonly StringDataSnapshot _chineseFamilyName;
            private readonly StringDataSnapshot _givenName;
            private readonly StringDataSnapshot _lineageStatus;
            private readonly StringDataSnapshot _displayName;
            private readonly BoolDataSnapshot _nameIntegrated;

            public ActorFigureSnapshot(Actor pActor)
            {
                _health = pActor.getHealth();
                _favorite = pActor.data.favorite;
                _sex = pActor.data.sex;
                _name = pActor.data.name;
                _customName = pActor.data.custom_name;
                _hadFigure = pActor.hasTrait(TRAIT_FIGURE);
                _hadFirst = pActor.hasTrait(TRAIT_FIRST);
                _hadGuizu = pActor.hasTrait(LineageKeys.TRAIT_GUIZU);
                _lineageId = new LongDataSnapshot(pActor, LineageKeys.LINEAGE_ID);
                _shiId = new LongDataSnapshot(pActor, LineageKeys.SHI_ID);
                _foundedBranchShiId = new LongDataSnapshot(pActor,
                    LineageKeys.FOUNDED_BRANCH_SHI_ID);
                _nobleDistance = new IntDataSnapshot(pActor,
                    LineageKeys.NOBLE_DISTANCE);
                _familyName = new StringDataSnapshot(pActor,
                    LineageKeys.FAMILY_NAME);
                _clanName = new StringDataSnapshot(pActor, LineageKeys.CLAN_NAME);
                _chineseFamilyName = new StringDataSnapshot(pActor,
                    LineageKeys.CHINESE_FAMILY_NAME);
                _givenName = new StringDataSnapshot(pActor, LineageKeys.GIVEN_NAME);
                _lineageStatus = new StringDataSnapshot(pActor,
                    LineageKeys.LINEAGE_STATUS);
                _displayName = new StringDataSnapshot(pActor, "display_name");
                _nameIntegrated = new BoolDataSnapshot(pActor,
                    LineageKeys.NAME_INTEGRATED);
            }

            public void Restore(Actor pActor)
            {
                pActor.setHealth(_health, pClamp: false);
                pActor.data.favorite = _favorite;
                pActor.data.sex = _sex;
                _lineageId.Restore(pActor);
                _shiId.Restore(pActor);
                _foundedBranchShiId.Restore(pActor);
                _nobleDistance.Restore(pActor);
                _familyName.Restore(pActor);
                _clanName.Restore(pActor);
                _chineseFamilyName.Restore(pActor);
                _givenName.Restore(pActor);
                _lineageStatus.Restore(pActor);
                _displayName.Restore(pActor);
                _nameIntegrated.Restore(pActor);
                RestoreTrait(pActor, TRAIT_FIGURE, _hadFigure);
                RestoreTrait(pActor, TRAIT_FIRST, _hadFirst);
                RestoreTrait(pActor, LineageKeys.TRAIT_GUIZU, _hadGuizu);
                try { pActor.setName(_name); }
                catch { pActor.data.name = _name; }
                pActor.data.custom_name = _customName;
            }

            private static void RestoreTrait(Actor pActor, string pTrait,
                bool pWasPresent)
            {
                bool present = pActor.hasTrait(pTrait);
                if (pWasPresent && !present) pActor.addTrait(pTrait);
                else if (!pWasPresent && present) pActor.removeTrait(pTrait);
            }
        }

        private readonly struct LongDataSnapshot
        {
            private readonly string _key;
            private readonly long _value;
            private readonly bool _exists;

            public LongDataSnapshot(Actor pActor, string pKey)
            {
                _key = pKey;
                long value = default;
                _exists = pActor.data.custom_data_long != null &&
                    pActor.data.custom_data_long.TryGetValue(pKey, out value);
                _value = value;
            }

            public void Restore(Actor pActor)
            {
                if (_exists) pActor.data.set(_key, _value);
                else pActor.data.removeLong(_key);
            }
        }

        private readonly struct IntDataSnapshot
        {
            private readonly string _key;
            private readonly int _value;
            private readonly bool _exists;

            public IntDataSnapshot(Actor pActor, string pKey)
            {
                _key = pKey;
                int value = default;
                _exists = pActor.data.custom_data_int != null &&
                    pActor.data.custom_data_int.TryGetValue(pKey, out value);
                _value = value;
            }

            public void Restore(Actor pActor)
            {
                if (_exists) pActor.data.set(_key, _value);
                else pActor.data.removeInt(_key);
            }
        }

        private readonly struct StringDataSnapshot
        {
            private readonly string _key;
            private readonly string _value;
            private readonly bool _exists;

            public StringDataSnapshot(Actor pActor, string pKey)
            {
                _key = pKey;
                string value = default;
                _exists = pActor.data.custom_data_string != null &&
                    pActor.data.custom_data_string.TryGetValue(pKey, out value);
                _value = value;
            }

            public void Restore(Actor pActor)
            {
                if (_exists) pActor.data.set(_key, _value);
                else pActor.data.removeString(_key);
            }
        }

        private readonly struct BoolDataSnapshot
        {
            private readonly string _key;
            private readonly bool _value;
            private readonly bool _exists;

            public BoolDataSnapshot(Actor pActor, string pKey)
            {
                _key = pKey;
                bool value = default;
                _exists = pActor.data.custom_data_bool != null &&
                    pActor.data.custom_data_bool.TryGetValue(pKey, out value);
                _value = value;
            }

            public void Restore(Actor pActor)
            {
                if (_exists) pActor.data.set(_key, _value);
                else pActor.data.removeBool(_key);
            }
        }

        private static void AnnounceFigure(Actor pActor,
            HistoricalFigureDef pDef)
        {
            try
            {
                var asset = AssetManager.world_log_library.get(LOG_ASSET_ID);
                if (asset == null) return;
                Kingdom k = pActor.kingdom;  // 早取一份引用,避免两次读到不同国
                // 国色 hex:用 ColorAsset.color_text(string 字段),与 AW3 现有 LineageArchiveWriter 一致。
                string colorHex = k?.getColor()?.color_text;
                if (string.IsNullOrEmpty(colorHex)) colorHex = "#FFFFFF";
                string localizedName = AncientWarfare3.ui.AW_L10n.Text(
                    pDef.NameLocaleKey, pDef.Key);
                string localizedDynasty = AncientWarfare3.ui.AW_L10n.Text(
                    pDef.DynastyLocaleKey, pDef.DynastyName);
                string localizedLabel =
                    HistoricalFigureSpawnRules.FormatLocalizedLabel(
                        localizedName, localizedDynasty);
                string coloredName = Toolbox.coloredString(
                    localizedLabel, colorHex);
                var msg = new WorldLogMessage(asset, coloredName) { unit = pActor };
                if (k != null) msg.kingdom = k;
                if (pActor.current_tile != null) msg.location = pActor.current_tile.pos;
                msg.add();
            }
            catch (Exception e)
            {
                ModClass.LogWarning("历史人物世界日志发送失败:" + e.Message);
            }
        }

        // ───────────────────────── 成为 king:套用国名 ─────────────────────────

        /// <summary>
        ///     历史人物成为某国国王(夺取/继承/创建都算)时,把预留国名(周/秦/…)写到那个国,
        ///     并记录 FigureState.kingdom_id/name。
        ///     由 AW_FigurePatch 钩 Kingdom.setKing 调用。
        /// </summary>
        public static void OnFigureKingBecame(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            int idx = FigureStateStore.IndexOfActor(pKing.data.id);
            if (idx < 0) return;                       // 不是历史人物
            var def = HistoricalFigureDef.Get(idx);
            if (def == null) return;

            if (!string.Equals(pKingdom.name, def.KingdomName,
                    StringComparison.Ordinal)) return;
            FigureStateStore.MarkKingdomApplied(idx, pKingdom.id, def.KingdomName);

            ModClass.LogInfo($"历史人物 {def.Key} 成为国王 → 国号 '{def.KingdomName}' 已提交");
        }

        public static string GetPreferredKingdomName(Actor pActor)
        {
            if (pActor?.data == null) return "";
            int index = FigureStateStore.IndexOfActor(pActor.data.id);
            HistoricalFigureDef definition = HistoricalFigureDef.Get(index);
            return definition?.KingdomName ?? "";
        }

        /// <summary>
        ///     历史人物改国名不再单独写一条专门历史记录。
        ///     建国、统治期和王朝记录仍由通用历史系统处理。
        /// </summary>
        // ───────────────────────── 死亡:解锁下一个 ─────────────────────────

        /// <summary>历史人物死亡 → 标记 dead,解锁严格顺序的下一个。由 AW_ActorDeathPatch 调用。</summary>
        public static void OnFigureDied(Actor pActor)
        {
            if (pActor?.data == null) return;
            int idx = FigureStateStore.IndexOfActor(pActor.data.id);
            if (idx < 0) return;
            FigureStateStore.MarkDead(idx);
            ModClass.LogInfo($"历史人物(序号 {idx})死亡,解锁下一位。");
        }
    }
}
