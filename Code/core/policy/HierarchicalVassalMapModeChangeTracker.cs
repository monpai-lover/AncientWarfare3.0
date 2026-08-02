using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    /// <summary>
    /// Tracks map-mode invalidations without making the render path hash the
    /// complete world every few frames.  The fallback cursor samples a small
    /// number of kingdom shape signatures periodically so missed lifecycle
    /// callbacks cannot leave labels stale forever.
    /// </summary>
    internal sealed class HierarchicalVassalMapModeChangeTracker
    {
        private readonly HashSet<long> _dirtyKingdoms =
            new HashSet<long>();
        private readonly HashSet<long> _dirtyCities =
            new HashSet<long>();
        private readonly Dictionary<long, long> _knownSignatures =
            new Dictionary<long, long>();
        private bool _allDirty = true;
        private int _fallbackCursor;
        private long _generation = 1L;
        private long _lastFallbackTimestamp;

        public long Generation => _generation;

        public bool HasDirtyWork => _allDirty || _dirtyKingdoms.Count > 0 ||
                                     _dirtyCities.Count > 0;

        public void MarkAll()
        {
            _allDirty = true;
            _dirtyKingdoms.Clear();
            _dirtyCities.Clear();
            _knownSignatures.Clear();
            _generation++;
        }

        public void MarkKingdom(long pKingdomId)
        {
            if (pKingdomId < 0L) return;
            _dirtyKingdoms.Add(pKingdomId);
            _generation++;
        }

        public void MarkCity(long pCityId)
        {
            if (pCityId < 0L) return;
            _dirtyCities.Add(pCityId);
            _generation++;
        }

        public void MarkHierarchy()
        {
            _allDirty = true;
            _generation++;
        }

        public bool AdvanceFallback(IReadOnlyList<Kingdom> pKingdoms,
            int pBudget)
        {
            if (!ShouldRunFallback()) return ConsumeExplicitDirty();

            bool changed = ConsumeExplicitDirty();
            int budget = HierarchicalVassalMapModeInvalidationRules.
                ClampFallbackBudget(pBudget);
            if (pKingdoms == null || pKingdoms.Count == 0 || budget <= 0)
                return changed;

            int processed = 0;
            while (processed < budget && pKingdoms.Count > 0)
            {
                if (_fallbackCursor >= pKingdoms.Count)
                    _fallbackCursor = 0;
                Kingdom kingdom = pKingdoms[_fallbackCursor++];
                processed++;
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                long signature = ComputeKingdomSignature(kingdom);
                if (_knownSignatures.TryGetValue(kingdom.id,
                        out long previous) && previous == signature) continue;
                bool hadBaseline = _knownSignatures.Count > 0;
                _knownSignatures[kingdom.id] = signature;
                // The first pass only establishes a cheap baseline.  It must
                // not cause a full label rebuild every two seconds while a
                // large world is being covered by the cursor.
                if (hadBaseline)
                {
                    _generation++;
                    changed = true;
                }
            }
            return changed;
        }

        public void Reset()
        {
            _dirtyKingdoms.Clear();
            _dirtyCities.Clear();
            _knownSignatures.Clear();
            _allDirty = true;
            _fallbackCursor = 0;
            _generation = 1L;
            _lastFallbackTimestamp = 0L;
        }

        private bool ConsumeExplicitDirty()
        {
            bool changed = HasDirtyWork;
            _allDirty = false;
            _dirtyKingdoms.Clear();
            _dirtyCities.Clear();
            return changed;
        }

        private bool ShouldRunFallback()
        {
            long now = Stopwatch.GetTimestamp();
            double elapsed = _lastFallbackTimestamp == 0L
                ? double.PositiveInfinity
                : (now - _lastFallbackTimestamp) /
                  (double)Stopwatch.Frequency;
            if (!HierarchicalVassalMapModeInvalidationRules.IsFallbackDue(
                    elapsed,
                    HierarchicalVassalMapModeInvalidationRules.
                        FallbackIntervalSeconds)) return false;
            _lastFallbackTimestamp = now;
            return true;
        }

        private static long ComputeKingdomSignature(Kingdom pKingdom)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                Add(ref hash, pKingdom.id);
                try { Add(ref hash, VassalService.GetSuzerainId(pKingdom)); }
                catch { Add(ref hash, -1L); }
                Add(ref hash, pKingdom.name ?? string.Empty);
                int cityCount = 0;
                try
                {
                    foreach (City ignored in pKingdom.getCities())
                        cityCount++;
                }
                catch { return hash; }
                Add(ref hash, cityCount);
                try
                {
                    foreach (City city in pKingdom.getCities())
                    {
                        if (city?.data == null || city.isRekt()) continue;
                        Add(ref hash, city.id);
                        Add(ref hash, city.kingdom?.id ?? -1L);
                        Add(ref hash, city.zones?.Count ?? 0);
                        if (city.zones == null) continue;
                        for (int index = 0; index < city.zones.Count; index++)
                        {
                            TileZone zone = city.zones[index];
                            if (zone == null) continue;
                            Add(ref hash, zone.id);
                            Add(ref hash, zone.tiles?.Length ?? 0);
                        }
                    }
                }
                catch { }
                return hash;
            }
        }

        private static void Add(ref long pHash, long pValue)
        {
            unchecked
            {
                pHash ^= pValue;
                pHash *= 1099511628211L;
            }
        }

        private static void Add(ref long pHash, int pValue)
        {
            Add(ref pHash, (long)pValue);
        }

        private static void Add(ref long pHash, string pValue)
        {
            string value = pValue ?? string.Empty;
            Add(ref pHash, value.Length);
            for (int index = 0; index < value.Length; index++)
                Add(ref pHash, value[index]);
        }
    }
}
