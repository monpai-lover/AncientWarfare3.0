using AncientWarfare3.content.figures;

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
            if (_initialized) return;
            _initialized = true;
            IsCatalogueAvailable = HistoricalFigureCardCatalog.IsValid;
            if (!IsCatalogueAvailable)
                ModClass.LogWarning("Historical card catalogue validation failed: " +
                    string.Join("; ", HistoricalFigureCardCatalog.ValidationIssues));
            Store.Load();
            HistoricalFigureCardAudioService.Initialize();
        }
    }
}
