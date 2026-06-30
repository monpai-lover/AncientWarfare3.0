using System;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.content.figures
{
    /// <summary>
    ///     历史人物降临(姬发/嬴政/刘邦/曹丕/司马炎)—— AW2 SpecialFigure 的新版重做。
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

        /// <summary>天命国系统未做 —— 留桩,恒 false(不阻断生成)。日后接天命系统替换实现。</summary>
        public static bool HasMandateKingdom() => false;

        // ───────────────────────── 生成 ─────────────────────────

        /// <summary>由 AW_FigurePatch 在新单位出生(Actor.newCreature Postfix)后调用。门槛检查 + 掷骰 + 降临。</summary>
        public static void TrySpawnOn(Actor pActor)
        {
            // —— cheap guard,按代价从低到高 ——
            if (!Enabled) return;
            if (pActor?.data == null || !pActor.isAlive()) return;
            if (!LineageService.IsXia(pActor)) return;
            if (!pActor.isKingdomCiv()) return;                 // 必须文明单位
            if (pActor.hasTrait(TRAIT_FIGURE) || pActor.hasTrait(TRAIT_FIRST)) return;

            // —— 存活互斥 + 天命国 ——
            if (FigureStateStore.AnyAliveFigure()) return;      // 已有 figure 存活
            if (HasMandateKingdom()) return;                    // 留桩=false

            // —— 严格顺序:取当前应生成的那个人 ——
            int idx = FigureStateStore.NextSpawnableIndex();
            if (idx < 0) return;                                // 没有可生成的(前一个还活着 / 全生成完)
            var def = HistoricalFigureDef.Get(idx);
            if (def == null) return;

            // —— 合流门:刘邦起需世上已有夏人国完成姓氏合流 ——
            if (def.RequiresIntegration && !AnyXiaKingdomIntegrated()) return;

            // —— 掷骰(私有 Random) ——
            if (Rng.NextDouble() >= def.Chance) return;

            ApplyFigure(pActor, def, idx);
        }

        /// <summary>世上是否有任意夏人国家完成姓氏合流(合流门用)。</summary>
        private static bool AnyXiaKingdomIntegrated()
        {
            if (World.world?.kingdoms == null) return false;
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k == null || !k.isCiv()) continue;
                if (LineageService.IsKingdomIntegrated(k)) return true;
            }
            return false;
        }

        /// <summary>降临:设属性 + 注入预设姓氏 + 标记持久化 + 发世界日志。</summary>
        private static void ApplyFigure(Actor pActor, HistoricalFigureDef pDef, int pIndex)
        {
            // 1) 基础属性:满血 1500、收藏、figure+first 特质。
            pActor.addTrait(TRAIT_FIGURE);
            pActor.addTrait(TRAIT_FIRST);
            pActor.setHealth(FIGURE_HEALTH);
            pActor.data.favorite = true;

            // 2) 注入预设姓氏(不走随机):先手建姓族+氏支拿 id,再 set 字段,再晋升。
            //    必须先 set LINEAGE_ID,EnsureLineageForNoble 见已有谱系即跳过随机生成(LineageService.cs:199)。
            long lineageId = LineageIdAllocator.NextLineageId();
            long shiId = LineageIdAllocator.NextShiId();
            LineageService.InsertLineageGroup(lineageId, pDef.FamilyName, pActor);
            LineageService.InsertShiBranch(shiId, lineageId, pDef.ClanName, pActor, ShiSourceType.SPECIAL_FIGURE);

            pActor.data.set(LineageKeys.LINEAGE_ID, lineageId);
            pActor.data.set(LineageKeys.SHI_ID, shiId);
            pActor.data.set(LineageKeys.FAMILY_NAME, pDef.FamilyName);
            pActor.data.set(LineageKeys.CLAN_NAME, pDef.ClanName);
            pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME, pDef.FamilyName);
            pActor.data.set(LineageKeys.GIVEN_NAME, pDef.GivenName); // 名:发/政/邦/丕/炎

            // 3) 晋升为贵族(距离归零、guizu、状态 noble、ApplyDisplayName 拼回全名"姬发"、归档)。
            LineageService.OnActorPromoted(pActor, NobleTrigger.Figure);

            // 4) 持久化生成状态(随存档,根治重复生成)。
            FigureStateStore.MarkSpawned(pIndex, pDef.Key, pActor.data.id, LineageService.CurTime());

            // 5) 世界日志公告:特殊人物$ren$降临这个世界($ren$=带国色的人名)。
            AnnounceFigure(pActor);

            ModClass.LogInfo($"历史人物降临:{pDef.Key}(序号 {pIndex},国名预留 {pDef.KingdomName})");
        }

        private static void AnnounceFigure(Actor pActor)
        {
            try
            {
                var asset = AssetManager.world_log_library.get(LOG_ASSET_ID);
                if (asset == null) return;
                Kingdom k = pActor.kingdom;  // 早取一份引用,避免两次读到不同国
                // 国色 hex:用 ColorAsset.color_text(string 字段),与 AW3 现有 LineageArchiveWriter 一致。
                string colorHex = k?.getColor()?.color_text;
                if (string.IsNullOrEmpty(colorHex)) colorHex = "#FFFFFF";
                string coloredName = Toolbox.coloredString(pActor.getName(), colorHex);
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
        ///     并记录到历史系统(FigureState.kingdom_id/name + RecordKingdomRename 钩子)。
        ///     由 AW_FigurePatch 钩 Kingdom.setKing 调用。
        /// </summary>
        public static void OnFigureKingBecame(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            int idx = FigureStateStore.IndexOfActor(pKing.data.id);
            if (idx < 0) return;                       // 不是历史人物
            var def = HistoricalFigureDef.Get(idx);
            if (def == null) return;

            string oldName = pKingdom.name;
            if (oldName == def.KingdomName) return;     // 已是目标国名,免重复

            RecordKingdomRename(pKingdom, oldName, def.KingdomName, pKing);
            pKingdom.setName(def.KingdomName);          // 套用预留国名(用 setName 走正规 setter,非直改 data)
            FigureStateStore.MarkKingdomApplied(idx, pKingdom.id, def.KingdomName);

            ModClass.LogInfo($"历史人物 {def.Key} 成为国王 → 国家 '{oldName}' 改名为 '{def.KingdomName}'");
        }

        /// <summary>
        ///     王国改名历史记录钩子(留接口)——日后天命国/编年史系统接入时在此落历史事件。
        ///     当前仅 log;不阻断改名。
        /// </summary>
        private static void RecordKingdomRename(Kingdom pKingdom, string pOldName, string pNewName, Actor pKing)
        {
            // TODO(天命国/编年史系统):把"<pKing> 受天命,改 <pOldName> 为 <pNewName>"写入历史。
        }

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
