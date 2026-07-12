using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public sealed class CitySchoolRetryScheduler
    {
        public const int InitialDelayTicks = 8;
        public const int MaxDelayTicks = 256;

        private sealed class RetryState
        {
            public long CityId;
            public int Attempts;
            public long DueTick;
            public bool Waiting;
        }

        private readonly Dictionary<long, RetryState> _states =
            new Dictionary<long, RetryState>();
        private readonly SortedDictionary<long, List<long>> _due =
            new SortedDictionary<long, List<long>>();
        private long _tick;

        public int Count => _states.Count;

        public bool Contains(long pCityId)
        {
            return pCityId >= 0 && _states.ContainsKey(pCityId);
        }

        public int ScheduleFailure(long pCityId)
        {
            if (pCityId < 0) return 0;
            if (!_states.TryGetValue(pCityId, out RetryState state))
            {
                state = new RetryState { CityId = pCityId };
                _states[pCityId] = state;
            }

            state.Attempts = Math.Min(31, state.Attempts + 1);
            int delay = DelayForAttempt(state.Attempts);
            state.DueTick = _tick > long.MaxValue - delay
                ? long.MaxValue
                : _tick + delay;
            state.Waiting = true;
            if (!_due.TryGetValue(state.DueTick, out List<long> dueAtTick))
            {
                dueAtTick = new List<long>();
                _due[state.DueTick] = dueAtTick;
            }
            dueAtTick.Add(pCityId);
            return delay;
        }

        public long[] AdvanceAndTakeDue()
        {
            if (_tick < long.MaxValue) _tick++;
            if (_due.Count == 0) return Array.Empty<long>();

            List<long> ready = null;
            while (_due.Count > 0)
            {
                KeyValuePair<long, List<long>> first;
                using (SortedDictionary<long, List<long>>.Enumerator enumerator =
                       _due.GetEnumerator())
                {
                    enumerator.MoveNext();
                    first = enumerator.Current;
                }
                if (first.Key > _tick) break;
                _due.Remove(first.Key);
                foreach (long cityId in first.Value)
                {
                    if (!_states.TryGetValue(cityId, out RetryState state) ||
                        !state.Waiting || state.DueTick != first.Key) continue;
                    state.Waiting = false;
                    if (ready == null) ready = new List<long>();
                    ready.Add(state.CityId);
                }
            }
            return ready?.ToArray() ?? Array.Empty<long>();
        }

        public bool Forget(long pCityId)
        {
            return pCityId >= 0 && _states.Remove(pCityId);
        }

        public void Clear()
        {
            _states.Clear();
            _due.Clear();
            _tick = 0L;
        }

        private static int DelayForAttempt(int pAttempts)
        {
            int shift = Math.Min(5, Math.Max(0, pAttempts - 1));
            return Math.Min(MaxDelayTicks, InitialDelayTicks << shift);
        }
    }
}
