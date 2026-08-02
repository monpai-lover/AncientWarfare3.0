using System;
using System.IO;

namespace AncientWarfare3.core.lineage
{
    internal static class AW3SaveDirectoryRegistry
    {
        private static readonly object Gate = new object();
        private static string _directory = string.Empty;

        public static bool Observe(string pDirectory)
        {
            if (string.IsNullOrWhiteSpace(pDirectory)) return false;
            try
            {
                string normalized = Path.GetFullPath(pDirectory);
                lock (Gate) _directory = normalized;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryGet(out string pDirectory)
        {
            lock (Gate) pDirectory = _directory;
            return !string.IsNullOrWhiteSpace(pDirectory);
        }

        public static void ClearForNewWorld()
        {
            lock (Gate) _directory = string.Empty;
        }
    }
}
