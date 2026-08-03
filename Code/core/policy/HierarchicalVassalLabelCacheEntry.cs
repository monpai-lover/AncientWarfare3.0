using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalLabelCacheEntry
    {
        internal HierarchicalVassalLabelCacheEntry(
            HierarchicalVassalLabelBuildResult pResult,
            IEnumerable<int> pBaselineZoneIds, string pSourceName,
            long pLayoutGeneration)
        {
            Result = pResult;
            ReplaceBaseline(pBaselineZoneIds);
            SourceName = pSourceName ?? string.Empty;
            LayoutGeneration = pLayoutGeneration;
        }

        internal HierarchicalVassalLabelBuildResult Result { get; private set; }

        internal HashSet<int> BaselineZoneIds { get; private set; }

        internal string SourceName { get; private set; }

        internal long LayoutGeneration { get; private set; }

        internal bool Published { get; private set; }

        internal bool Accept(HierarchicalVassalLabelBuildResult pResult,
            IEnumerable<int> pBaselineZoneIds, string pSourceName,
            long pLayoutGeneration)
        {
            bool changed = !HierarchicalVassalLabelResultRules.AreEquivalent(
                Result, pResult);
            ReplaceBaseline(pBaselineZoneIds);
            SourceName = pSourceName ?? string.Empty;
            LayoutGeneration = pLayoutGeneration;
            if (!changed) return false;
            Result = pResult;
            Published = false;
            return true;
        }

        internal void MarkPublished()
        {
            Published = true;
        }

        private void ReplaceBaseline(IEnumerable<int> pZoneIds)
        {
            BaselineZoneIds = pZoneIds switch
            {
                null => new HashSet<int>(),
                HashSet<int> owned => owned,
                _ => new HashSet<int>(pZoneIds)
            };
        }
    }
}
