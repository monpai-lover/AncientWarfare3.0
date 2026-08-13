using System;

namespace AncientWarfare3.core.grandstrategy
{
    public sealed class GrandStrategySiegeState
    {
        public GrandStrategySiegeState(long siegeId, long cityId,
            int defense, int maximumProgress)
        {
            SiegeId = siegeId;
            CityId = cityId;
            Defense = Math.Max(0, defense);
            MaximumProgress = Math.Max(1, maximumProgress);
        }

        public long SiegeId { get; }
        public long CityId { get; }
        public int Defense { get; internal set; }
        public int Progress { get; internal set; }
        public int MaximumProgress { get; }
        public bool Complete => Progress >= MaximumProgress;
    }

    public static class GrandStrategySiegeRules
    {
        public static GrandStrategySiegeState ResolveRound(
            GrandStrategySiegeState current, int engineers, int equipment,
            int officerSkill, int manpower, double supply, int technology,
            bool assault, int roll)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            int quality = Math.Max(0, engineers) / 5 +
                Math.Max(0, equipment) * 3 + Math.Max(0, officerSkill) * 2 +
                Math.Max(0, technology) * 2 + Math.Max(0, roll);
            int power = Math.Max(1, (int)Math.Round(
                Math.Max(0, manpower) * Math.Max(0.1, supply) / 100.0) + quality);
            int progress = assault ? power * 2 : power;
            var next = new GrandStrategySiegeState(current.SiegeId,
                current.CityId, current.Defense, current.MaximumProgress)
            {
                Progress = Math.Min(current.MaximumProgress,
                    current.Progress + progress),
                Defense = Math.Max(0, current.Defense -
                    Math.Max(1, progress / (assault ? 2 : 3)))
            };
            return next;
        }
    }
}
