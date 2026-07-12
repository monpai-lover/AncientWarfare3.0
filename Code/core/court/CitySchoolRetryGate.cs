using System;

namespace AncientWarfare3.core.court
{
    public sealed class CitySchoolRetryGate
    {
        public const int InitialDelayTicks = 8;
        public const int MaxDelayTicks = 256;

        private int _attempts;
        private long _tick;
        private long _nextAttemptTick;

        public bool AdvanceAndCanAttempt()
        {
            if (_tick < long.MaxValue) _tick++;
            return _tick >= _nextAttemptTick;
        }

        public int RecordFailure()
        {
            _attempts = Math.Min(31, _attempts + 1);
            int delay = DelayForAttempt(_attempts);
            _nextAttemptTick = _tick > long.MaxValue - delay
                ? long.MaxValue
                : _tick + delay;
            return delay;
        }

        public void RecordSuccess()
        {
            _attempts = 0;
            _nextAttemptTick = 0L;
        }

        public void Clear()
        {
            _attempts = 0;
            _tick = 0L;
            _nextAttemptTick = 0L;
        }

        private static int DelayForAttempt(int pAttempts)
        {
            int shift = Math.Min(5, Math.Max(0, pAttempts - 1));
            return Math.Min(MaxDelayTicks, InitialDelayTicks << shift);
        }
    }
}
