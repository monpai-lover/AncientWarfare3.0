using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.grandstrategy
{
    public sealed class GrandStrategyKingdomLedger
    {
        private readonly HashSet<string> _committedTransactions =
            new HashSet<string>(StringComparer.Ordinal);

        public GrandStrategyKingdomLedger(long kingdomId, int total)
        {
            if (kingdomId < 0) throw new ArgumentOutOfRangeException(nameof(kingdomId));
            if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
            KingdomId = kingdomId;
            AvailableManpower = total;
        }

        public long KingdomId { get; }
        public int AvailableManpower { get; internal set; }
        public int RaisedManpower { get; internal set; }
        public int WoundedManpower { get; internal set; }
        public int DispersedManpower { get; internal set; }
        public int PermanentDeaths { get; internal set; }
        public int Prisoners { get; internal set; }
        public int AccountedManpower => AvailableManpower + RaisedManpower +
            WoundedManpower + DispersedManpower + PermanentDeaths + Prisoners;

        internal bool HasCommitted(string key)
        {
            return !string.IsNullOrEmpty(key) && _committedTransactions.Contains(key);
        }

        internal void Commit(string key)
        {
            if (!string.IsNullOrEmpty(key)) _committedTransactions.Add(key);
        }
    }

    public enum GrandStrategyTroopType
    {
        Infantry = 0,
        Spearmen = 1,
        Archers = 2,
        Cavalry = 3,
        Engineers = 4
    }

    public enum GrandStrategyArmyTask
    {
        Rally = 0,
        March = 1,
        Pursue = 2,
        Siege = 3,
        Follow = 4,
        Retreat = 5,
        Disband = 6
    }

    public sealed class GrandStrategyTroopComposition
    {
        private readonly int[] _counts = new int[5];

        public int this[GrandStrategyTroopType type]
        {
            get { return _counts[(int)type]; }
            internal set { _counts[(int)type] = Math.Max(0, value); }
        }

        public int TotalStrength
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _counts.Length; i++) total += _counts[i];
                return total;
            }
        }

        public GrandStrategyTroopComposition Clone()
        {
            var copy = new GrandStrategyTroopComposition();
            for (int i = 0; i < _counts.Length; i++) copy._counts[i] = _counts[i];
            return copy;
        }
    }

    public sealed class GrandStrategyArmy
    {
        public GrandStrategyArmy(long id, long kingdomId, long warId,
            GrandStrategyTroopComposition composition)
        {
            if (id < 0 || kingdomId < 0 || warId < 0)
                throw new ArgumentOutOfRangeException();
            Id = id;
            KingdomId = kingdomId;
            WarId = warId;
            Composition = composition ?? throw new ArgumentNullException(nameof(composition));
        }

        public long Id { get; }
        public long KingdomId { get; }
        public long WarId { get; }
        public GrandStrategyTroopComposition Composition { get; }
        public GrandStrategyArmyTask Task { get; internal set; } = GrandStrategyArmyTask.Rally;
        public bool Disbanded { get; internal set; }
        public int Revision { get; internal set; }
        public int PositionTileId { get; internal set; } = -1;
        public int TotalStrength => Composition.TotalStrength;
    }
}
