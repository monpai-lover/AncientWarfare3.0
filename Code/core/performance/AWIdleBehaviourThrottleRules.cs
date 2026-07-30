using System;

namespace AncientWarfare3.core.performance
{
    public enum AWIdleBehaviourKind
    {
        None = 0,
        Socialize = 1,
        EmoteSearch = 2
    }

    public static class AWIdleBehaviourThrottleRules
    {
        public const double SocializeCooldownSeconds = 2d;
        public const double EmoteSearchCooldownSeconds = 1.5d;
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
            pKind = AWIdleBehaviourKind.None;
            return false;
        }

        public static bool IsEligibleCivilian(bool actorAlive, bool actorRekt,
            bool warrior, bool king)
        {
            return actorAlive && !actorRekt && !warrior && !king;
        }

        public static double CooldownSeconds(AWIdleBehaviourKind pKind)
        {
            switch (pKind)
            {
                case AWIdleBehaviourKind.Socialize:
                    return SocializeCooldownSeconds;
                case AWIdleBehaviourKind.EmoteSearch:
                    return EmoteSearchCooldownSeconds;
                default:
                    return 0d;
            }
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
            AWIdleBehaviourKind pKind)
        {
            return now + CooldownSeconds(pKind) +
                   StableJitterSeconds(actorId, pKind);
        }

        public static bool IsValidTime(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
