using System;

namespace AncientWarfare3.core.lineage
{
    public static class MandateCoreTooltipRules
    {
        public static string BuildPointedKingdomControlLine(string pKingdomName, float pControlRatio)
        {
            if (string.IsNullOrWhiteSpace(pKingdomName)) return "";
            int percent = (int)Math.Round(Math.Max(0f, pControlRatio) * 100f, MidpointRounding.AwayFromZero);
            return "\u6307\u5411\u56fd\u5bb6\u63a7\u5236\u7387: " + pKingdomName + " " + percent + "%";
        }
    }
}
