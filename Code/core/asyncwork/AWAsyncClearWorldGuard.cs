using System;

namespace AncientWarfare3.core.asyncwork
{
    internal static class AWAsyncClearWorldGuard
    {
        [ThreadStatic]
        private static bool _cleanupAllowed;

        public static bool CleanupAllowed => _cleanupAllowed;

        public static void BeginInvocation()
        {
            _cleanupAllowed = false;
        }

        public static void Grant()
        {
            _cleanupAllowed = true;
        }

        public static void EndInvocation()
        {
            _cleanupAllowed = false;
        }
    }
}
