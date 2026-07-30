using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct DiplomaticRelationModifierLoad
    {
        public DiplomaticRelationModifierLoad(int value, int validUntilYear)
        {
            Value = value;
            ValidUntilYear = validUntilYear;
        }

        public int Value { get; }
        public int ValidUntilYear { get; }
    }

    public delegate DiplomaticRelationModifierLoad
        DiplomaticRelationModifierLoader(DiplomacyKingdomPair pair,
            int currentYear);

    public sealed class DiplomaticRelationModifierCache
    {
        private readonly Dictionary<DiplomacyKingdomPair, CacheEntry>
            _entries = new Dictionary<DiplomacyKingdomPair, CacheEntry>();

        public int Read(long pKingdomA, long pKingdomB, int pCurrentYear,
            DiplomaticRelationModifierLoader pLoader)
        {
            DiplomacyKingdomPair pair = DiplomacyConversationRules
                .NormalizePair(pKingdomA, pKingdomB);
            if (_entries.TryGetValue(pair, out CacheEntry cached) &&
                pCurrentYear <= cached.ValidUntilYear)
                return cached.Value;
            DiplomaticRelationModifierLoad loaded = pLoader != null
                ? pLoader(pair, pCurrentYear)
                : new DiplomaticRelationModifierLoad(0, int.MaxValue);
            _entries[pair] = new CacheEntry(loaded.Value,
                Math.Max(pCurrentYear, loaded.ValidUntilYear));
            return loaded.Value;
        }

        public void Invalidate(long pKingdomA, long pKingdomB)
        {
            _entries.Remove(DiplomacyConversationRules.NormalizePair(
                pKingdomA, pKingdomB));
        }

        public void Clear()
        {
            _entries.Clear();
        }

        private readonly struct CacheEntry
        {
            public CacheEntry(int value, int validUntilYear)
            {
                Value = value;
                ValidUntilYear = validUntilYear;
            }

            public int Value { get; }
            public int ValidUntilYear { get; }
        }
    }
}
