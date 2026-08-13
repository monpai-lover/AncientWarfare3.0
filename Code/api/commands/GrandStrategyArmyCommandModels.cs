namespace AncientWarfare3.api.commands
{
    public enum GrandStrategyArmyCommandKind
    {
        Move = 0,
        Rally = 1,
        Pursue = 2,
        Siege = 3,
        Follow = 4,
        Merge = 5,
        Split = 6,
        Retreat = 7,
        Disband = 8,
        AssignCommander = 9,
        Assault = 10
    }

    public sealed class GrandStrategyArmyCommand
    {
        public GrandStrategyArmyCommand(long armyId, long kingdomId,
            long worldGeneration, long clientSequence, int expectedRevision,
            GrandStrategyArmyCommandKind kind, int targetTileId = -1,
            long targetId = -1, int amount = 0)
        {
            ArmyId = armyId;
            KingdomId = kingdomId;
            WorldGeneration = worldGeneration;
            ClientSequence = clientSequence;
            ExpectedRevision = expectedRevision;
            Kind = kind;
            TargetTileId = targetTileId;
            TargetId = targetId;
            Amount = amount;
        }

        public long ArmyId { get; }
        public long KingdomId { get; }
        public long WorldGeneration { get; }
        public long ClientSequence { get; }
        public int ExpectedRevision { get; }
        public GrandStrategyArmyCommandKind Kind { get; }
        public int TargetTileId { get; }
        public long TargetId { get; }
        public int Amount { get; }
    }

    public sealed class GrandStrategyArmyCommandResult
    {
        public bool Accepted { get; internal set; }
        public bool Duplicate { get; internal set; }
        public string Error { get; internal set; } = string.Empty;
    }
}
