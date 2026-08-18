using System.Globalization;

namespace AncientWarfare3.core.lineage
{
    public static class HeirDesignationHistoryRules
    {
        public static string ProjectionKey(long pKingdomId,
            long pRulerId, long pHeirId, string pMode)
        {
            string mode = string.IsNullOrWhiteSpace(pMode)
                ? SuccessionMode.NONE
                : pMode.Trim();
            return "heir_designated:" +
                   pKingdomId.ToString(CultureInfo.InvariantCulture) + ":" +
                   pRulerId.ToString(CultureInfo.InvariantCulture) + ":" +
                   pHeirId.ToString(CultureInfo.InvariantCulture) + ":" +
                   mode;
        }

        public static string PersonProjectionKey(string pKingdomProjectionKey)
        {
            return (pKingdomProjectionKey ?? string.Empty) + ":person";
        }
    }
}
