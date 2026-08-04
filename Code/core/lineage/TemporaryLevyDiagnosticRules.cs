using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class TemporaryLevyDiagnosticSampler
    {
        private sealed class Entry
        {
            public string Signature = string.Empty;
            public double NextSampleTime;
            public long Sequence;
        }

        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly double _sampleIntervalSeconds;
        private readonly int _maximumOperations;
        private long _sequence;

        public TemporaryLevyDiagnosticSampler(double sampleIntervalSeconds,
            int maximumOperations)
        {
            _sampleIntervalSeconds = double.IsNaN(sampleIntervalSeconds) ||
                                     double.IsInfinity(sampleIntervalSeconds)
                ? 0d
                : Math.Max(0d, sampleIntervalSeconds);
            _maximumOperations = Math.Max(1, maximumOperations);
        }

        public int Count => _entries.Count;

        public bool ShouldLog(bool diagnosticsEnabled, string operationKey,
            string signature, double currentTime)
        {
            if (!diagnosticsEnabled)
            {
                Clear();
                return false;
            }
            if (string.IsNullOrWhiteSpace(operationKey)) return false;
            double now = double.IsNaN(currentTime) ||
                         double.IsInfinity(currentTime)
                ? 0d
                : Math.Max(0d, currentTime);
            string currentSignature = signature ?? string.Empty;
            if (!_entries.TryGetValue(operationKey, out Entry entry))
            {
                EvictIfFull();
                _entries[operationKey] = new Entry
                {
                    Signature = currentSignature,
                    NextSampleTime = now + _sampleIntervalSeconds,
                    Sequence = NextSequence()
                };
                return true;
            }

            entry.Sequence = NextSequence();
            if (!string.Equals(entry.Signature, currentSignature,
                    StringComparison.Ordinal))
            {
                entry.Signature = currentSignature;
                entry.NextSampleTime = now + _sampleIntervalSeconds;
                return true;
            }
            if (now < entry.NextSampleTime) return false;
            entry.NextSampleTime = now + _sampleIntervalSeconds;
            return true;
        }

        public void Clear()
        {
            _entries.Clear();
            _sequence = 0L;
        }

        private void EvictIfFull()
        {
            if (_entries.Count < _maximumOperations) return;
            string oldestKey = null;
            long oldestSequence = long.MaxValue;
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (pair.Value.Sequence >= oldestSequence) continue;
                oldestSequence = pair.Value.Sequence;
                oldestKey = pair.Key;
            }
            if (oldestKey != null) _entries.Remove(oldestKey);
        }

        private long NextSequence()
        {
            if (_sequence < long.MaxValue) _sequence++;
            return _sequence;
        }
    }

    public static class TemporaryLevyDiagnosticRules
    {
        public static bool ShouldWriteRecoveryDiagnostic(
            bool diagnosticsEnabled, int pendingDemand)
        {
            return diagnosticsEnabled && pendingDemand > 0;
        }

        public static bool ShouldWriteRecoveryRequestDiagnostic(
            bool diagnosticsEnabled, int requestedDemand)
        {
            return diagnosticsEnabled && requestedDemand > 0;
        }

        public static string RecoveryCandidateBreakdown(int alreadyWarrior,
            int ineligible, int viable, int enlistFailures)
        {
            return "already_warrior=" + System.Math.Max(0, alreadyWarrior) +
                   " ineligible=" + System.Math.Max(0, ineligible) +
                   " viable=" + System.Math.Max(0, viable) +
                   " enlist_failed=" + System.Math.Max(0, enlistFailures);
        }

        public static string RecoveryIneligibilityBreakdown(int notResident,
            int notLivingAdult, int wrongProfession, int reservePolicy,
            int slavePolicy, int protectedIdentity, int nativeEligibility,
            int ageLimit, int capacity)
        {
            return "not_resident=" + System.Math.Max(0, notResident) +
                   " not_living_adult=" + System.Math.Max(0, notLivingAdult) +
                   " wrong_profession=" + System.Math.Max(0, wrongProfession) +
                   " reserve_policy=" + System.Math.Max(0, reservePolicy) +
                   " slave_policy=" + System.Math.Max(0, slavePolicy) +
                   " protected_identity=" + System.Math.Max(0, protectedIdentity) +
                   " native_eligibility=" + System.Math.Max(0, nativeEligibility) +
                   " age_limit=" + System.Math.Max(0, ageLimit) +
                   " capacity=" + System.Math.Max(0, capacity);
        }
    }
}
