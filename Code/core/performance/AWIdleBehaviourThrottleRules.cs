using System;

namespace AncientWarfare3.core.performance
{
    public enum AWIdleBehaviourKind
    {
        None = 0,
        Socialize = 1,
        EmoteSearch = 2,
        Sleep = 3
    }

    public static class AWIdleBehaviourThrottleRules
    {
        public const double MaximumJitterSeconds = 0.5d;

        public static bool TryGetKind(string pTaskId,
            out AWIdleBehaviourKind pKind)
        {
            if (string.Equals(pTaskId,
                    "socialize_try_to_start_near_bonfire",
                    StringComparison.Ordinal) ||
                string.Equals(pTaskId, "socialize_try_to_start_immediate",
                    StringComparison.Ordinal))
            {
                pKind = AWIdleBehaviourKind.Socialize;
                return true;
            }
            if (string.Equals(pTaskId, "happy_laughing",
                    StringComparison.Ordinal) ||
                string.Equals(pTaskId, "singing", StringComparison.Ordinal))
            {
                pKind = AWIdleBehaviourKind.EmoteSearch;
                return true;
            }
            if (string.Equals(pTaskId, "decide_where_to_sleep",
                    StringComparison.Ordinal))
            {
                pKind = AWIdleBehaviourKind.Sleep;
                return true;
            }
            pKind = AWIdleBehaviourKind.None;
            return false;
        }

        public static bool IsEligibleCivilian(bool civilian, bool actorAlive,
            bool actorRekt, bool warrior, bool armyMember, bool king, bool boat,
            bool militaryMovementOwned)
        {
            return civilian && actorAlive && !actorRekt && !warrior &&
                   !armyMember && !king && !boat && !militaryMovementOwned;
        }

        public static double CooldownSeconds(AWIdleBehaviourKind pKind,
            double pRequestedSpeed)
        {
            if (!IsValidTime(pRequestedSpeed) || pRequestedSpeed < 0d)
                return 0d;
            bool fastest = pRequestedSpeed > 4d;
            bool faster = pRequestedSpeed > 2d;
            switch (pKind)
            {
                case AWIdleBehaviourKind.Socialize:
                    return fastest ? 8d : faster ? 4d : 2d;
                case AWIdleBehaviourKind.EmoteSearch:
                    return fastest ? 6d : faster ? 3d : 1.5d;
                case AWIdleBehaviourKind.Sleep:
                    return fastest ? 10d : faster ? 4d : 0d;
                default:
                    return 0d;
            }
        }

        public static double ResolveRequestedSpeed(bool cooperativeControl,
            double capturedSpeed, double nativeSpeed)
        {
            return cooperativeControl ? capturedSpeed : nativeSpeed;
        }

        public static double StableJitterSeconds(long actorId,
            AWIdleBehaviourKind pKind)
        {
            unchecked
            {
                ulong value = (ulong)actorId;
                value ^= (ulong)(int)pKind * 0x9E3779B97F4A7C15UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (value % 501UL) / 1000d;
            }
        }

        public static double NextEligibleAt(double now, long actorId,
            AWIdleBehaviourKind pKind, double pRequestedSpeed)
        {
            return now + CooldownSeconds(pKind, pRequestedSpeed) +
                   StableJitterSeconds(actorId, pKind);
        }

        public static bool IsValidTime(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
