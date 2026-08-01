using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    public sealed class AWNameGenerationContext
    {
        private readonly Dictionary<string, string> _parameters;
        private readonly Dictionary<string, string> _globalParameters;

        public AWNameGenerationContext(long pSeed,
            IReadOnlyDictionary<string, string> pParameters = null,
            IReadOnlyDictionary<string, string> pGlobalParameters = null)
        {
            Seed = pSeed;
            _parameters = Copy(pParameters);
            _globalParameters = Copy(pGlobalParameters);
        }

        public long Seed { get; }

        public IReadOnlyDictionary<string, string> Parameters => _parameters;

        internal Dictionary<string, string> CreateWorkingParameters()
        {
            return new Dictionary<string, string>(_parameters,
                StringComparer.Ordinal);
        }

        internal bool TryGetGlobal(string pKey, out string pValue)
        {
            return _globalParameters.TryGetValue(pKey, out pValue);
        }

        internal AWDeterministicNameRandom CreateRandom()
        {
            return new AWDeterministicNameRandom((ulong)Seed);
        }

        private static Dictionary<string, string> Copy(
            IReadOnlyDictionary<string, string> pSource)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (pSource == null) return result;
            foreach (KeyValuePair<string, string> pair in pSource)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                result[pair.Key] = pair.Value ?? string.Empty;
            }
            return result;
        }
    }

    internal struct AWDeterministicNameRandom
    {
        private ulong _state;

        public AWDeterministicNameRandom(ulong pSeed)
        {
            _state = pSeed;
        }

        public double NextUnit()
        {
            return (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
        }

        public int NextIndex(int pCount)
        {
            if (pCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(pCount));

            ulong bound = (ulong)pCount;
            ulong limit = ulong.MaxValue - ulong.MaxValue % bound;
            ulong value;
            do
            {
                value = NextUInt64();
            } while (value >= limit);

            return (int)(value % bound);
        }

        private ulong NextUInt64()
        {
            unchecked
            {
                ulong value = (_state += 0x9E3779B97F4A7C15UL);
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }
    }
}
