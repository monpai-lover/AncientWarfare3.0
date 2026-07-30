using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    public sealed class AWIdleBehaviourThrottleGate
    {
        private readonly Dictionary<ActorTaskKey, double> _nextEligibleAt =
            new Dictionary<ActorTaskKey, double>();
        private readonly List<ActorTaskKey> _removals =
            new List<ActorTaskKey>(2);

        public bool TryBeginScan(long actorId, AWIdleBehaviourKind kind,
            double now)
        {
            if (actorId <= 0L || kind == AWIdleBehaviourKind.None ||
                !AWIdleBehaviourThrottleRules.IsValidTime(now)) return true;
            var key = new ActorTaskKey(actorId, kind);
            if (_nextEligibleAt.TryGetValue(key, out double nextEligibleAt) &&
                now < nextEligibleAt) return false;
            _nextEligibleAt[key] = AWIdleBehaviourThrottleRules.NextEligibleAt(
                now, actorId, kind);
            return true;
        }

        public void RemoveActor(long actorId)
        {
            if (actorId <= 0L || _nextEligibleAt.Count == 0) return;
            _removals.Clear();
            foreach (ActorTaskKey key in _nextEligibleAt.Keys)
                if (key.ActorId == actorId) _removals.Add(key);
            for (int i = 0; i < _removals.Count; i++)
                _nextEligibleAt.Remove(_removals[i]);
            _removals.Clear();
        }

        private readonly struct ActorTaskKey : IEquatable<ActorTaskKey>
        {
            public ActorTaskKey(long pActorId, AWIdleBehaviourKind pKind)
            {
                ActorId = pActorId;
                Kind = pKind;
            }

            public long ActorId { get; }
            public AWIdleBehaviourKind Kind { get; }

            public bool Equals(ActorTaskKey other)
            {
                return ActorId == other.ActorId && Kind == other.Kind;
            }

            public override bool Equals(object obj)
            {
                return obj is ActorTaskKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)ActorId * 397) ^ (int)(ActorId >> 32) ^
                           (int)Kind;
                }
            }
        }
    }
}
