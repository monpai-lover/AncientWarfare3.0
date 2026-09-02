using System.Globalization;

namespace AncientWarfare3.core.historyapi
{
    internal static class AW3HistoryEventIdentityRules
    {
        public static string Build(string domain, string source, long recordId)
        {
            return (domain ?? "") + "|" + (source ?? "") + "|" +
                recordId.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildProjection(string domain, string source,
            string projectionKey)
        {
            return (domain ?? "") + "|" + (source ?? "") + "|" +
                (projectionKey ?? "");
        }
    }
}
