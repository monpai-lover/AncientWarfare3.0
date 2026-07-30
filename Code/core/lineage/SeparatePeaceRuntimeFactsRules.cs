using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct SeparatePeaceFrozenCityFacts
    {
        public SeparatePeaceFrozenCityFacts(long pCityId,
            long pHomeKingdomId, bool controllerOpposesExitRoot)
        {
            CityId = pCityId;
            HomeKingdomId = pHomeKingdomId;
            ControllerOpposesExitRoot = controllerOpposesExitRoot;
        }

        public long CityId { get; }
        public long HomeKingdomId { get; }
        public bool ControllerOpposesExitRoot { get; }
    }

    public readonly struct SeparatePeaceParticipantPowerFacts
    {
        public SeparatePeaceParticipantPowerFacts(long pKingdomId,
            bool sameSide, bool includedInExitGroup, long power)
        {
            KingdomId = pKingdomId;
            SameSide = sameSide;
            IncludedInExitGroup = includedInExitGroup;
            Power = power;
        }

        public long KingdomId { get; }
        public bool SameSide { get; }
        public bool IncludedInExitGroup { get; }
        public long Power { get; }
    }

    public static class SeparatePeaceRuntimeFactsRules
    {
        public static float OccupiedCityRatio(long exitRootKingdomId,
            IReadOnlyList<long> liveHomeCityIds,
            IReadOnlyList<SeparatePeaceFrozenCityFacts> frozen)
        {
            if (exitRootKingdomId < 0) return 0f;
            var homeCities = new HashSet<long>();
            var occupiedCities = new HashSet<long>();
            if (liveHomeCityIds != null)
                for (int i = 0; i < liveHomeCityIds.Count; i++)
                    if (liveHomeCityIds[i] >= 0)
                        homeCities.Add(liveHomeCityIds[i]);
            if (frozen != null)
                for (int i = 0; i < frozen.Count; i++)
                {
                    SeparatePeaceFrozenCityFacts city = frozen[i];
                    if (city.CityId < 0 ||
                        city.HomeKingdomId != exitRootKingdomId) continue;
                    homeCities.Add(city.CityId);
                    if (city.ControllerOpposesExitRoot)
                        occupiedCities.Add(city.CityId);
                }
            return homeCities.Count == 0
                ? 0f
                : occupiedCities.Count / (float)homeCities.Count;
        }

        public static int ParticipantExhaustion(int sideExhaustion,
            int mobilizationBaseline, int currentMilitaryPotential)
        {
            int side = ClampPercent(sideExhaustion);
            if (mobilizationBaseline <= 0) return side;
            int current = Math.Max(0, currentMilitaryPotential);
            int depletion = ClampPercent((int)Math.Round(
                Math.Max(0, mobilizationBaseline - current) * 100d /
                mobilizationBaseline));
            return Math.Max(side, depletion);
        }

        public static long ExitGroupPower(
            IReadOnlyList<SeparatePeaceParticipantPowerFacts> participants)
        {
            long result = 0L;
            if (participants == null) return result;
            for (int i = 0; i < participants.Count; i++)
            {
                SeparatePeaceParticipantPowerFacts participant =
                    participants[i];
                if (!participant.SameSide ||
                    !participant.IncludedInExitGroup) continue;
                result = AddSaturating(result, Math.Max(1L,
                    participant.Power));
            }
            return result;
        }

        public static float ExitGroupPowerShare(
            IReadOnlyList<SeparatePeaceParticipantPowerFacts> participants)
        {
            long total = 0L;
            if (participants == null) return 0f;
            for (int i = 0; i < participants.Count; i++)
                if (participants[i].SameSide)
                    total = AddSaturating(total,
                        Math.Max(1L, participants[i].Power));
            if (total <= 0L) return 0f;
            return Math.Min(1f, ExitGroupPower(participants) /
                (float)total);
        }

        private static int ClampPercent(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }

        private static long AddSaturating(long left, long right)
        {
            if (right <= 0L) return left;
            return left > long.MaxValue - right
                ? long.MaxValue
                : left + right;
        }
    }
}
