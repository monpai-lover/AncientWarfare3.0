using System;

namespace AncientWarfare3.core.lineage
{
    internal static class FamilyTreePortraitIdentityRules
    {
        private const string NeutralPortraitColor = "#888888";

        public static int ResolveArchivedSkinId(int currentSkinId,
            bool hasCurrentSubspecies, int previousSkinId)
        {
            if (hasCurrentSubspecies && currentSkinId >= 0)
                return currentSkinId;
            return previousSkinId >= 0 ? previousSkinId : 0;
        }

        public static int ResolveArchivedSkinSet(bool hasCurrentSubspecies,
            int previousSkinSet)
        {
            return hasCurrentSubspecies ? 1 : previousSkinSet != 0 ? 1 : 0;
        }

        public static int ResolveEffectiveSkinId(int archivedSkinId,
            bool hasExactArchivedSkin, int currentSubspeciesSkinId)
        {
            if (hasExactArchivedSkin) return Math.Max(0, archivedSkinId);
            if (currentSubspeciesSkinId >= 0) return currentSubspeciesSkinId;
            return Math.Max(0, archivedSkinId);
        }

        public static int ResolveArchivedHeadId(int currentHeadId,
            long actorId, int headCount)
        {
            if (headCount <= 0) return currentHeadId;
            if (currentHeadId >= 0 && currentHeadId < headCount)
                return currentHeadId;

            long positiveId = actorId >= 0L ? actorId : -(actorId + 1L);
            return (int)((1L + positiveId * 100L) % headCount);
        }

        public static int ResolveRenderableHeadId(int archivedHeadId,
            long actorId, int headCount)
        {
            if (headCount <= 0) return -1;
            return ResolveArchivedHeadId(archivedHeadId, actorId, headCount);
        }

        public static string ResolvePortraitColorHex(string archivedColor)
        {
            string value = (archivedColor ?? string.Empty).Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
                value = value.Substring(1);
            if (value.Length != 6 && value.Length != 8)
                return NeutralPortraitColor;
            if (!uint.TryParse(value,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _))
                return NeutralPortraitColor;
            return "#" + value.ToUpperInvariant();
        }

        public static string ResolveCitizenTexturePath(string textureBase,
            string[] maleSkins, string[] femaleSkins, int sex,
            int archivedSkinId)
        {
            string[] skins = sex == 0 ? maleSkins : femaleSkins;
            if (skins == null || skins.Length == 0) return string.Empty;

            int index = archivedSkinId >= 0 && archivedSkinId < skins.Length
                ? archivedSkinId
                : 0;
            string skin = skins[index];
            if (string.IsNullOrWhiteSpace(skin)) return string.Empty;
            return (textureBase ?? string.Empty) + skin;
        }
    }
}
