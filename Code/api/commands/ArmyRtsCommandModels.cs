using System;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.api.commands
{
    public enum ArmyRtsCommandKind
    {
        SetRallyPoint = 0,
        SetTargetCity = 1,
        SetPosture = 2,
        CancelOrder = 3
    }

    public readonly struct ArmyPlayerOrderInterruptionFacts
    {
        public ArmyPlayerOrderInterruptionFacts(bool targetExists,
            bool routeImpossible, int supply, int rosterLossPercent)
        {
            TargetExists = targetExists;
            RouteImpossible = routeImpossible;
            Supply = Math.Max(0, Math.Min(100, supply));
            RosterLossPercent = Math.Max(0,
                Math.Min(100, rosterLossPercent));
        }

        public bool TargetExists { get; }
        public bool RouteImpossible { get; }
        public int Supply { get; }
        public int RosterLossPercent { get; }
    }

    public static class ArmyRtsCommandRules
    {
        public static bool IsLegalCityTarget(bool rally,
            bool cityHasOwner, bool cityOwnedByCommander,
            bool hasApplicableWar)
        {
            if (!cityHasOwner || !hasApplicableWar) return false;
            return rally
                ? cityOwnedByCommander
                : !cityOwnedByCommander;
        }

        public static bool ShouldInterruptPlayerOrder(bool playerOrder,
            ArmyPlayerOrderInterruptionFacts facts)
        {
            return playerOrder &&
                   (!facts.TargetExists || facts.RouteImpossible ||
                    facts.Supply <= ArmyLogisticsRules.CriticalSupply ||
                    facts.RosterLossPercent >=
                    ArmyRtsRules.CatastrophicLossPercent);
        }
    }

    public sealed class ArmyRtsCommand
    {
        private ArmyRtsCommand(ArmyRtsCommandKind kind, long kingdomId,
            long armyId, long cityId, ArmyRtsPosture posture)
        {
            if (!Enum.IsDefined(typeof(ArmyRtsCommandKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (kingdomId < 0L)
                throw new ArgumentOutOfRangeException(nameof(kingdomId));
            if (armyId < 0L)
                throw new ArgumentOutOfRangeException(nameof(armyId));
            if (cityId < -1L)
                throw new ArgumentOutOfRangeException(nameof(cityId));
            if (!Enum.IsDefined(typeof(ArmyRtsPosture), posture))
                throw new ArgumentOutOfRangeException(nameof(posture));
            Kind = kind;
            KingdomId = kingdomId;
            ArmyId = armyId;
            CityId = cityId;
            Posture = posture;
        }

        public ArmyRtsCommandKind Kind { get; }
        public long KingdomId { get; }
        public long ArmyId { get; }
        public long CityId { get; }
        public ArmyRtsPosture Posture { get; }

        public static ArmyRtsCommand SetRallyPoint(long kingdomId,
            long armyId, long cityId)
        {
            return WithCity(ArmyRtsCommandKind.SetRallyPoint, kingdomId,
                armyId, cityId);
        }

        public static ArmyRtsCommand SetTargetCity(long kingdomId,
            long armyId, long cityId)
        {
            return WithCity(ArmyRtsCommandKind.SetTargetCity, kingdomId,
                armyId, cityId);
        }

        public static ArmyRtsCommand SetPosture(long kingdomId,
            long armyId, ArmyRtsPosture posture)
        {
            return new ArmyRtsCommand(ArmyRtsCommandKind.SetPosture,
                kingdomId, armyId, -1L, posture);
        }

        public static ArmyRtsCommand CancelOrder(long kingdomId,
            long armyId)
        {
            return new ArmyRtsCommand(ArmyRtsCommandKind.CancelOrder,
                kingdomId, armyId, -1L, ArmyRtsPosture.Automatic);
        }

        private static ArmyRtsCommand WithCity(ArmyRtsCommandKind kind,
            long kingdomId, long armyId, long cityId)
        {
            if (cityId < 0L)
                throw new ArgumentOutOfRangeException(nameof(cityId));
            return new ArmyRtsCommand(kind, kingdomId, armyId, cityId,
                ArmyRtsPosture.Automatic);
        }
    }
}
