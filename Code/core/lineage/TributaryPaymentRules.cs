using System;

namespace AncientWarfare3.core.lineage
{
    public static class TributaryPaymentRules
    {
        public static float PowerRatio(float tributaryPower,
            float suzerainPower)
        {
            float tributary = Normalize(tributaryPower);
            float suzerain = Normalize(suzerainPower);
            if (suzerain <= 0f)
                return tributary <= 0f ? 0f : float.PositiveInfinity;
            return tributary / suzerain;
        }

        public static int FactorPercent(float tributaryPower,
            float suzerainPower)
        {
            float ratio = PowerRatio(tributaryPower, suzerainPower);
            if (ratio >= 1.25f) return 0;
            if (ratio >= 1f) return 25;
            if (ratio >= .75f) return 50;
            if (ratio >= .5f) return 75;
            return 100;
        }

        public static int ScaleGold(int baseRequest, int factorPercent,
            int available)
        {
            int request = Math.Max(0, baseRequest);
            int factor = Math.Max(0, Math.Min(100, factorPercent));
            int stock = Math.Max(0, available);
            if (request == 0 || factor == 0 || stock == 0) return 0;
            int scaled = (int)Math.Floor(request * factor / 100d);
            return Math.Min(stock, Math.Max(1, scaled));
        }

        public static float ScalePolitical(float baseRequest,
            int factorPercent, float available)
        {
            float factor = Math.Max(0, Math.Min(100, factorPercent)) /
                           100f;
            return Math.Min(Normalize(available),
                Normalize(baseRequest) * factor);
        }

        public static bool IsPaid(float political, int gold)
        {
            return Normalize(political) > 0f || gold > 0;
        }

        public static string EndReason(int factor, float political,
            int gold)
        {
            if (factor <= 0) return "tribute_refused_power";
            return IsPaid(political, gold) ? "" : "tribute_unpaid";
        }

        private static float Normalize(float value)
        {
            return float.IsNaN(value) || value < 0f ? 0f : value;
        }
    }
}
