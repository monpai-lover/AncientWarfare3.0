using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal static class StableLiveListSnapshotRules
    {
        internal static IReadOnlyList<T> TryCapture<T>(
            IReadOnlyList<T> pSource) where T : class
        {
            if (pSource == null) return null;
            try
            {
                int count = pSource.Count;
                if (count == 0) return Array.Empty<T>();
                var snapshot = new T[count];
                for (int index = 0; index < count; index++)
                {
                    if (pSource.Count != count) return null;
                    snapshot[index] = pSource[index];
                }
                if (pSource.Count != count) return null;
                for (int index = 0; index < count; index++)
                    if (!ReferenceEquals(snapshot[index], pSource[index]))
                        return null;
                return snapshot;
            }
            catch
            {
                return null;
            }
        }
    }
}
