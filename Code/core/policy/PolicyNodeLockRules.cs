using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class PolicyNodeLockRules
    {
        public const string CoreFabricationDecisionId = "aw_decision_fabricate_core";

        public static bool IsLocked(string lockedRaw, string nodeId)
        {
            if (string.IsNullOrEmpty(lockedRaw) || string.IsNullOrEmpty(nodeId)) return false;
            foreach (string part in Split(lockedRaw))
                if (part == nodeId) return true;
            return false;
        }

        public static string SetLocked(string lockedRaw, string nodeId, bool locked)
        {
            var set = new HashSet<string>(Split(lockedRaw));
            if (string.IsNullOrEmpty(nodeId)) return string.Join(";", set);
            if (locked) set.Add(nodeId);
            else set.Remove(nodeId);
            return string.Join(";", set);
        }

        public static bool ShouldAllowStart(string lockedRaw, string nodeId)
        {
            return !IsLocked(lockedRaw, nodeId);
        }

        public static bool ShouldClearCurrent(string lockedNodeId, string currentNodeId)
        {
            return !string.IsNullOrEmpty(lockedNodeId) && lockedNodeId == currentNodeId;
        }

        public static bool ShouldClearCoreFabrication(string lockedNodeId)
        {
            return lockedNodeId == CoreFabricationDecisionId;
        }

        private static IEnumerable<string> Split(string raw)
        {
            if (string.IsNullOrEmpty(raw)) yield break;
            foreach (string part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                yield return part;
        }
    }
}
