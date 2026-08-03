using System;
using System.Threading;

namespace AncientWarfare3.core.lineage
{
    public static class SyntheticLevySpawnScope
    {
        private static readonly AsyncLocal<int> Depth = new AsyncLocal<int>();

        public static bool IsActive => Depth.Value > 0;

        public static IDisposable Open()
        {
            Depth.Value = Depth.Value + 1;
            return new Token();
        }

        private sealed class Token : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Depth.Value = Math.Max(0, Depth.Value - 1);
            }
        }
    }
}
