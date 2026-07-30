using System;
using System.Globalization;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceTermIdentityRules
    {
        public static string Build(WarPeaceTermKind pKind,
            long pFromKingdomId, long pToKingdomId, long pCityId,
            long pCaptiveActorId, long pClaimId, int pAmount,
            int pDurationYears, string pResourceId)
        {
            return string.Concat(
                "term|", ((int)pKind).ToString(
                    CultureInfo.InvariantCulture),
                "|", pFromKingdomId.ToString(
                    CultureInfo.InvariantCulture),
                "|", pToKingdomId.ToString(
                    CultureInfo.InvariantCulture),
                "|", pCityId.ToString(CultureInfo.InvariantCulture),
                "|", pCaptiveActorId.ToString(
                    CultureInfo.InvariantCulture),
                "|", pClaimId.ToString(CultureInfo.InvariantCulture),
                "|", pAmount.ToString(CultureInfo.InvariantCulture),
                "|", pDurationYears.ToString(
                    CultureInfo.InvariantCulture),
                "|", Uri.EscapeDataString(pResourceId ?? string.Empty));
        }
    }
}
