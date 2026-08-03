using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    public sealed class AWIdleBehaviourThrottleGate
    {
        private readonly Dictionary<long, ActorCooldownState> _actorCooldowns =
            new Dictionary<long, ActorCooldownState>();

        public bool TryBeginScan(long actorId, AWIdleBehaviourKind kind,
            double now)
        {
            if (actorId <= 0L || kind == AWIdleBehaviourKind.None ||
                !AWIdleBehaviourThrottleRules.IsValidTime(now)) return true;
            _actorCooldowns.TryGetValue(actorId,
                out ActorCooldownState cooldowns);
            if (!cooldowns.TryBeginScan(actorId, kind, now)) return false;
            _actorCooldowns[actorId] = cooldowns;
            return true;
        }

        public void RemoveActor(long actorId)
        {
            if (actorId <= 0L) return;
            _actorCooldowns.Remove(actorId);
        }

        public void Clear()
        {
            _actorCooldowns.Clear();
        }

        private struct ActorCooldownState
        {
            private const byte SocializeActive = 1;
            private const byte EmoteSearchActive = 2;

            private byte _activeKinds;
            private double _socializeNextEligibleAt;
            private double _emoteSearchNextEligibleAt;

            public bool TryBeginScan(long actorId, AWIdleBehaviourKind kind,
                double now)
            {
                switch (kind)
                {
                    case AWIdleBehaviourKind.Socialize:
                        if ((_activeKinds & SocializeActive) != 0 &&
                            now < _socializeNextEligibleAt) return false;
                        _socializeNextEligibleAt =
                            AWIdleBehaviourThrottleRules.NextEligibleAt(
                                now, actorId, kind);
                        _activeKinds |= SocializeActive;
                        return true;
                    case AWIdleBehaviourKind.EmoteSearch:
                        if ((_activeKinds & EmoteSearchActive) != 0 &&
                            now < _emoteSearchNextEligibleAt) return false;
                        _emoteSearchNextEligibleAt =
                            AWIdleBehaviourThrottleRules.NextEligibleAt(
                                now, actorId, kind);
                        _activeKinds |= EmoteSearchActive;
                        return true;
                    default:
                        return true;
                }
            }
        }
    }
}
