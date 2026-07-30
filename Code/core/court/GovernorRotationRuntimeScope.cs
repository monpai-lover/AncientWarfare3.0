using System;

namespace AncientWarfare3.core.court
{
    internal static class GovernorRotationRuntimeScope
    {
        [ThreadStatic]
        private static int _depth;

        public static bool IsActive => _depth > 0;

        public static IDisposable Enter()
        {
            _depth++;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_depth > 0) _depth--;
            }
        }
    }
}
