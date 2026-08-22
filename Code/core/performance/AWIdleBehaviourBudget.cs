using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    /// <summary>
    /// Bounded, runtime-only reservations for cosmetic actions. A reservation
    /// is held while the actor remains in the corresponding vanilla task.
    /// </summary>
    public sealed class AWIdleBehaviourBudget
    {
        private const int SocializeLimit = 32;
        private const int EmoteLimit = 16;
        private const int SingingLimit = 16;

        private readonly object _sync = new object();
        private readonly Dictionary<AWIdleBehaviourKind, HashSet<long>>
            _active = new Dictionary<AWIdleBehaviourKind, HashSet<long>>();

        public static int Limit(AWIdleBehaviourKind pKind)
        {
            switch (pKind)
            {
                case AWIdleBehaviourKind.Socialize:
                    return SocializeLimit;
                case AWIdleBehaviourKind.EmoteSearch:
                    return EmoteLimit;
                case AWIdleBehaviourKind.Singing:
                    return SingingLimit;
                default:
                    return 0;
            }
        }

        public bool TryAcquire(long pActorId, AWIdleBehaviourKind pKind)
        {
            int limit = Limit(pKind);
            if (pActorId <= 0L || limit <= 0) return true;
            lock (_sync)
            {
                if (!_active.TryGetValue(pKind, out HashSet<long> actors))
                {
                    actors = new HashSet<long>();
                    _active[pKind] = actors;
                }
                if (actors.Contains(pActorId)) return true;
                if (actors.Count >= limit) return false;
                actors.Add(pActorId);
                return true;
            }
        }

        public void Release(long pActorId, AWIdleBehaviourKind pKind)
        {
            if (pActorId <= 0L) return;
            lock (_sync)
            {
                if (_active.TryGetValue(pKind, out HashSet<long> actors))
                    actors.Remove(pActorId);
            }
        }

        public void ReleaseAll(long pActorId)
        {
            if (pActorId <= 0L) return;
            lock (_sync)
            {
                foreach (HashSet<long> actors in _active.Values)
                    actors.Remove(pActorId);
            }
        }

        public void ReleaseExcept(long pActorId,
            AWIdleBehaviourKind pRetainedKind)
        {
            if (pActorId <= 0L) return;
            lock (_sync)
            {
                foreach (KeyValuePair<AWIdleBehaviourKind, HashSet<long>> item
                         in _active)
                {
                    if (item.Key != pRetainedKind) item.Value.Remove(pActorId);
                }
            }
        }

        public void Clear()
        {
            lock (_sync) _active.Clear();
        }
    }
}
