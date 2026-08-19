using System;
using System.Globalization;

namespace AncientWarfare3.core.policy
{
    internal readonly struct HierarchicalVassalLabelCacheKey : IEquatable<HierarchicalVassalLabelCacheKey>
    {
        internal readonly long WorldGeneration;
        internal readonly string Layer;
        internal readonly long HierarchyFocus;
        internal readonly long EntityId;

        internal HierarchicalVassalLabelCacheKey(long pWorldGeneration,
            string pLayer, long pHierarchyFocus, long pEntityId)
        {
            WorldGeneration = pWorldGeneration;
            Layer = pLayer ?? string.Empty;
            HierarchyFocus = pHierarchyFocus;
            EntityId = pEntityId;
        }

        public bool Equals(HierarchicalVassalLabelCacheKey pOther)
        {
            return WorldGeneration == pOther.WorldGeneration &&
                string.Equals(Layer, pOther.Layer,
                    StringComparison.Ordinal) &&
                HierarchyFocus == pOther.HierarchyFocus &&
                EntityId == pOther.EntityId;
        }

        public override bool Equals(object pObject)
        {
            return pObject is HierarchicalVassalLabelCacheKey other &&
                Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = WorldGeneration.GetHashCode();
                hash = hash * 397 + StringComparer.Ordinal.GetHashCode(Layer);
                hash = hash * 397 + HierarchyFocus.GetHashCode();
                return hash * 397 + EntityId.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "world:" + WorldGeneration + ":" + Layer + ":" +
                HierarchyFocus + ":" + EntityId;
        }

        internal bool MatchesEntity(string pLayer, long pEntityId)
        {
            return string.Equals(Layer, pLayer,
                       StringComparison.Ordinal) &&
                   EntityId == pEntityId;
        }

        internal bool HasFocus(long pFocus)
        {
            return HierarchyFocus == pFocus;
        }

        internal bool HasScope(long pWorldGeneration, string pLayer,
            long pFocus)
        {
            return WorldGeneration == pWorldGeneration &&
                string.Equals(Layer, pLayer,
                    StringComparison.Ordinal) &&
                HierarchyFocus == pFocus;
        }

        internal bool HasPrefix(long pWorldGeneration, string pLayer,
            long pFocus)
        {
            return HasScope(pWorldGeneration, pLayer, pFocus);
        }

        internal static bool TryParse(string pText,
            out HierarchicalVassalLabelCacheKey pKey)
        {
            pKey = default;
            if (string.IsNullOrEmpty(pText)) return false;
            string[] parts = pText.Split(':');
            if (parts.Length != 5 || parts[0] != "world" ||
                (parts[2] != "country" && parts[2] != "city" &&
                 parts[2] != "region") ||
                !long.TryParse(parts[1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long world) ||
                !long.TryParse(parts[3], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long focus) ||
                !long.TryParse(parts[4], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long entity))
                return false;
            pKey = new HierarchicalVassalLabelCacheKey(world, parts[2],
                focus, entity);
            return true;
        }
    }
}
