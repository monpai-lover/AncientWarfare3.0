using System;

namespace AncientWarfare3.core.lineage
{
    public enum SpyClaimKind
    {
        Weak = 0,
        Strong = 1
    }

    public readonly struct SpyNetworkAccrual
    {
        public SpyNetworkAccrual(int points, int lastAccrualYear)
        {
            Points = points;
            LastAccrualYear = lastAccrualYear;
        }

        public int Points { get; }
        public int LastAccrualYear { get; }
    }

    public static class SpyNetworkPointRules
    {
        public const int PointsPerYear = 12;
        public const int MaximumPoints = 200;
        public const int WeakClaimCost = 40;
        public const int StrongClaimCost = 100;

        public static SpyNetworkAccrual Accrue(int storedPoints,
            int lastAccrualYear, int currentYear)
        {
            int points = Math.Max(0, Math.Min(MaximumPoints, storedPoints));
            int lastYear = lastAccrualYear < 0 ? currentYear : lastAccrualYear;
            int elapsed = Math.Max(0, currentYear - lastYear);
            long accrued = (long)elapsed * PointsPerYear;
            points = (int)Math.Min(MaximumPoints, points + accrued);
            return new SpyNetworkAccrual(points,
                Math.Max(lastYear, currentYear));
        }

        public static int Cost(SpyClaimKind kind)
        {
            return kind == SpyClaimKind.Strong
                ? StrongClaimCost
                : WeakClaimCost;
        }

        public static string PurchaseKey(SpyClaimKind kind, long cityId,
            int purchaseYear)
        {
            return (kind == SpyClaimKind.Strong ? "strong:" : "weak:") +
                   cityId + ":" + Math.Max(0, purchaseYear);
        }

        public static string PurchaseReason(bool activeNetwork,
            bool targetCityOwned, bool canFabricate, int currentPoints,
            SpyClaimKind kind)
        {
            if (!activeNetwork) return "spy_network_required";
            if (!targetCityOwned) return "target_city_changed";
            if (!canFabricate) return "fabrication_unavailable";
            return currentPoints < Cost(kind)
                ? "insufficient_spy_points"
                : "";
        }
    }
}
