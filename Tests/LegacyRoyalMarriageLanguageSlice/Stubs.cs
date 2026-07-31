public sealed class LocalizedTextManager
{
    public static LocalizedTextManager instance;
    public string language;
}

namespace AncientWarfare3.core.lineage
{
    public readonly struct HistoryText
    {
        public readonly string Plain;

        private HistoryText(string pPlain)
        {
            Plain = pPlain ?? "";
        }

        public static HistoryText PlainText(string pText)
        {
            return new HistoryText(pText);
        }
    }
}
