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
