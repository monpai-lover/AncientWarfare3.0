namespace AncientWarfare3.core.performance
{
    internal static class CityManagerMutationScope
    {
        private static int _cityUpdateDepth;

        internal static bool IsCityUpdateActive => _cityUpdateDepth > 0;

        internal static void EnterCityUpdate()
        {
            _cityUpdateDepth++;
        }

        internal static void ExitCityUpdate()
        {
            if (_cityUpdateDepth > 0) _cityUpdateDepth--;
        }

        internal static void Reset()
        {
            _cityUpdateDepth = 0;
        }
    }
}
