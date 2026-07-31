using System;

namespace AncientWarfare3.core.performance
{
    internal static class AWSimulationTime
    {
        private static MapBox _boundWorld;
        private static MapStats _boundMapStats;
        private static int _boundWorldSeedId = -1;
        private static int _generation;
        private static bool _tickActive;
        private static double _committedTime;
        private static double _pendingTime;

        public static int Generation => _generation;
        public static int BoundWorldSeedId => _boundWorldSeedId;
        public static bool IsBound => _boundWorld != null;
        public static bool TickActive => _tickActive;
        public static double DiagnosticTime =>
            _tickActive ? _pendingTime : _committedTime;

        public static double Now
        {
            get
            {
                ValidateBoundWorld(_boundWorld);
                return _tickActive ? _pendingTime : _committedTime;
            }
        }

        public static float NowFloat => (float)Now;

        public static void BindWorld(MapBox pWorld)
        {
            if (pWorld?.map_stats == null)
                throw new InvalidOperationException(
                    "Cannot bind scheduler time before MapStats exists.");

            int worldSeedId = MapBox.current_world_seed_id;
            if (ReferenceEquals(_boundWorld, pWorld) &&
                ReferenceEquals(_boundMapStats, pWorld.map_stats) &&
                _boundWorldSeedId == worldSeedId)
            {
                SynchronizeFromWorld(pWorld);
                return;
            }

            _boundWorld = pWorld;
            _boundMapStats = pWorld.map_stats;
            _boundWorldSeedId = worldSeedId;
            _committedTime = pWorld.getCurWorldTime();
            _pendingTime = _committedTime;
            _tickActive = false;
            _generation++;
        }

        public static void UnbindWorld()
        {
            _tickActive = false;
            _pendingTime = _committedTime;
            _boundWorld = null;
            _boundMapStats = null;
            _boundWorldSeedId = -1;
        }

        public static void BeginTick(MapBox pWorld, float pDeltaTime)
        {
            ValidateBoundWorld(pWorld);
            if (_tickActive)
                throw new InvalidOperationException(
                    "The previous scheduler tick is still active.");

            _committedTime = pWorld.getCurWorldTime();
            _pendingTime = _committedTime + Math.Max(0f, pDeltaTime);
            _tickActive = true;
        }

        public static void CompleteTick(MapBox pWorld)
        {
            ValidateBoundWorld(pWorld);
            if (!_tickActive) return;

            _committedTime = pWorld.getCurWorldTime();
            _pendingTime = _committedTime;
            _tickActive = false;
        }

        public static void CancelTick()
        {
            if (_boundWorld != null && IsCurrentBoundWorld(_boundWorld))
                _committedTime = _boundWorld.getCurWorldTime();

            _tickActive = false;
            _pendingTime = _committedTime;
        }

        public static void SynchronizeFromWorld(MapBox pWorld)
        {
            ValidateBoundWorld(pWorld);
            if (_tickActive)
                throw new InvalidOperationException(
                    "Scheduler time cannot synchronize during an active tick.");

            _committedTime = pWorld.getCurWorldTime();
            _pendingTime = _committedTime;
        }

        private static void ValidateBoundWorld(MapBox pWorld)
        {
            if (pWorld == null || _boundWorld == null)
                throw new InvalidOperationException(
                    "No world is bound to scheduler time.");
            if (!ReferenceEquals(_boundWorld, pWorld) ||
                !IsCurrentBoundWorld(pWorld))
                throw new InvalidOperationException(
                    "Scheduler time does not match the current world.");
        }

        private static bool IsCurrentBoundWorld(MapBox pWorld)
        {
            return ReferenceEquals(_boundWorld, pWorld) &&
                   ReferenceEquals(_boundMapStats, pWorld.map_stats) &&
                   _boundWorldSeedId == MapBox.current_world_seed_id;
        }
    }
}
