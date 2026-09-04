using AncientWarfare3.content.figures;
using NeoModLoader.General;

namespace AncientWarfare3.core.lineage
{
    /// <summary>One-time card runtime initialization shared by UI and APIs.</summary>
    public static class HistoricalFigureCardRuntimeService
    {
        private static readonly HistoricalFigureCardCollectionStore Store =
            new HistoricalFigureCardCollectionStore();
        private static bool _initialized;

        public static HistoricalFigureCardCollectionStore Collection
        {
            get
            {
                Initialize();
                return Store;
            }
        }

        public static bool IsCatalogueAvailable { get; private set; }

        public static void Initialize()
        {
            EnsureLocalization();
            if (_initialized) return;
            _initialized = true;
            IsCatalogueAvailable = HistoricalFigureCardCatalog.IsValid;
            if (!IsCatalogueAvailable)
                ModClass.LogWarning("Historical card catalogue validation failed: " +
                    string.Join("; ", HistoricalFigureCardCatalog.ValidationIssues));
            Store.Load();
            HistoricalFigureCardAudioService.Initialize();
        }

        private static void EnsureLocalization()
        {
            const string titleKey = "aw_historical_figure_cards_title";
            if (LocalizedTextManager.instance == null) return;
            try
            {
                if (!LocalizedTextManager.stringExists(titleKey))
                    LM.ApplyLocale(false);
                AddFallback("aw_historical_figure_cards Title", "历史人物抽卡");
                AddFallback("aw_historical_figure_cards_btn", "历史人物抽卡");
                AddFallback("aw_historical_figure_cards_btn Description",
                    "打开历史人物抽卡、仓库和部署界面");
                AddFallback(titleKey, "历史人物抽卡");
                AddFallback("aw_historical_figure_cards_role_monarch", "君主箱");
                AddFallback("aw_historical_figure_cards_role_minister", "大臣箱");
                AddFallback("aw_historical_figure_cards_recycle_title", "历史人物汰换");
                AddFallback("aw_historical_figure_cards_recycle_summary", "已选择 {0}/{1}");
                AddFallback("aw_historical_figure_cards_recycle_next", "下一品质：{0}");
                AddFallback("aw_historical_figure_cards_recycle_next_empty", "选择同品质卡片");
                AddFallback("aw_historical_figure_cards_recycle_reset", "重置");
                AddFallback("aw_historical_figure_cards_recycle_back", "返回仓库");
                AddFallback("aw_historical_figure_cards_recycle_insufficient", "持有数量不足");
                AddFallback("aw_historical_figure_cards_recycle_success", "获得：{0}  来源：{1}");
                AddFallback("aw_historical_figure_cards_recycle_source_missing", "没有可用的收藏品来源");
                AddFallback("aw_historical_figure_cards_recycle_output_missing", "没有可用的输出卡");
                AddFallback("aw_historical_figure_cards_recycle_persistence_failed", "仓库保存失败");
                AddFallback("aw_hist_card_minister_deployed", "进入官场候选池");
                AddFallback("aw_hist_card_military_deployed",
                    "\u4efb\u547d\u4e3a\u5927\u5c06\u5e76\u7edf\u9886\u519b\u961f");
                AddFallback("aw_historical_figure_cards_type_civil", "\u6587\u81e3");
                AddFallback("aw_historical_figure_cards_type_general", "\u6b66\u5c06");
                AddFallback("aw_historical_figure_cards_type_monarch", "\u541b\u4e3b");
                AddFallback("aw_historical_figure_cards_reveal_meta_extended",
                    "\u56fd\u53f7\uff1a{0}\n\u671d\u4ee3\uff1a{1}\n\u540d\u6c14\uff1a{2}\n\u7c7b\u578b\uff1a{3}\n\u6765\u6e90\uff1a{4}");
                LM.ApplyLocale();
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning("Historical card locale reapply failed: " +
                                    error.Message);
            }
            if (!LocalizedTextManager.stringExists(titleKey) &&
                !LocalizedTextManager.stringExists("aw_historical_figure_cards Title"))
                ModClass.LogWarning("Historical card locale missing: " + titleKey);
        }

        private static void AddFallback(string pKey, string pValue)
        {
            if (LocalizedTextManager.stringExists(pKey)) return;
            LM.AddToCurrentLocale(pKey, pValue);
        }
    }
}
