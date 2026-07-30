namespace AncientWarfare3.core.performance
{
    internal static class AWJobBatchSizeOwnership
    {
        private static int _nativeBatchSize;
        private static bool _owned;

        public static bool IsOwned => _owned;

        public static void Acquire()
        {
            if (!_owned)
            {
                _nativeBatchSize = JobConst.MAX_ELEMENTS;
                _owned = true;
            }
            JobConst.MAX_ELEMENTS =
                AWPerformanceSettings.SimulationBatchSize;
        }

        public static void Release()
        {
            if (!_owned) return;
            JobConst.MAX_ELEMENTS = _nativeBatchSize;
            _nativeBatchSize = 0;
            _owned = false;
        }
    }
}
