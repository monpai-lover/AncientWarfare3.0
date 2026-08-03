using System;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameProjectionRefreshScope
    {
        [ThreadStatic] private static int _suppressDepth;

        internal static bool ShouldRefreshAutomatically(bool isEditing)
        {
            return !isEditing && _suppressDepth <= 0;
        }

        internal static void Suppress(Action pAction)
        {
            if (pAction == null) return;
            _suppressDepth++;
            try
            {
                pAction();
            }
            finally
            {
                _suppressDepth--;
            }
        }
    }
}
