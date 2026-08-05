namespace AncientWarfare3.core.lineage
{
    internal static class ManualAllianceToolScope
    {
        private static int _depth;

        internal static bool IsActive => _depth > 0;

        internal static void Enter()
        {
            _depth++;
        }

        internal static void Exit()
        {
            if (_depth > 0) _depth--;
        }
    }
}
