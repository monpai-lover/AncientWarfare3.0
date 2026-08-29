using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.presentation;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_MandateCapitalBuildingSpritePatch
    {
        private const string CAPITAL_PATH =
            "buildings/civ_main/Xia_MandateCapital/";

        private static readonly Dictionary<string, CapitalSpriteCatalog>
            Catalogs = new Dictionary<string, CapitalSpriteCatalog>();
        private static readonly HashSet<string> MissingCatalogs =
            new HashSet<string>();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Building), nameof(Building.calculateMainSprite))]
        public static void CalculateMainSprite_Postfix(Building __instance,
            ref Sprite __result)
        {
            if (__result == null || __instance?.asset == null) return;

            City city = __instance.current_tile?.zone?.city;
            Kingdom kingdom = city?.kingdom;
            bool isXiaBuilding = __instance.asset.civ_kingdom ==
                                 XiaArchitecture.ID;
            bool isMandateKingdom =
                MandateService.IsRuntimeMandateKingdom(kingdom);
            bool isKingdomCapital = kingdom != null &&
                                    kingdom.capital == city;
            if (!MandateCapitalBuildingTextureRules.ShouldUseCapitalTexture(
                    isXiaBuilding, isMandateKingdom, isKingdomCapital))
                return;

            if (!TryGetCatalog(__instance.asset, out CapitalSpriteCatalog catalog))
                return;
            if (catalog.TryResolve(__instance, __result,
                    out Sprite capitalSprite))
                __result = capitalSprite;
        }

        private static bool TryGetCatalog(BuildingAsset pAsset,
            out CapitalSpriteCatalog pCatalog)
        {
            string id = pAsset.id;
            if (Catalogs.TryGetValue(id, out pCatalog)) return true;
            if (MissingCatalogs.Contains(id)) return false;

            Sprite[] sprites = SpriteTextureLoader.getSpriteList(
                CAPITAL_PATH + id, true);
            if (sprites == null || sprites.Length == 0)
            {
                MissingCatalogs.Add(id);
                pCatalog = null;
                return false;
            }

            pCatalog = new CapitalSpriteCatalog(pAsset, sprites);
            if (pCatalog.IsEmpty)
            {
                MissingCatalogs.Add(id);
                pCatalog = null;
                return false;
            }
            Catalogs[id] = pCatalog;
            return true;
        }

        private sealed class CapitalSpriteCatalog
        {
            private readonly Dictionary<string,
                SortedDictionary<int, List<Sprite>>> _sprites =
                new Dictionary<string, SortedDictionary<int, List<Sprite>>>(
                    StringComparer.Ordinal);
            private readonly Dictionary<string, int[]> _variantIndices =
                new Dictionary<string, int[]>(StringComparer.Ordinal);

            public bool IsEmpty => _sprites.Count == 0;

            public CapitalSpriteCatalog(BuildingAsset pAsset, Sprite[] pSprites)
            {
                foreach (Sprite sprite in pSprites)
                {
                    if (sprite == null || !TryParseSpriteName(sprite.name,
                            out string category, out int variant, out _))
                        continue;
                    if (category != "main" && category != "ruin" &&
                        category != "construction")
                        continue;

                    if (!_sprites.TryGetValue(category,
                            out SortedDictionary<int, List<Sprite>> variants))
                    {
                        variants = new SortedDictionary<int, List<Sprite>>();
                        _sprites[category] = variants;
                    }
                    if (!variants.TryGetValue(variant, out List<Sprite> frames))
                    {
                        frames = new List<Sprite>();
                        variants[variant] = frames;
                    }
                    frames.Add(sprite);

                    if (pAsset.shadow)
                        DynamicSpriteCreator.createBuildingShadow(pAsset, sprite,
                            category == "construction");
                }

                foreach (SortedDictionary<int, List<Sprite>> variants in
                         _sprites.Values)
                    foreach (List<Sprite> frames in variants.Values)
                        frames.Sort(CompareFrames);
                foreach (KeyValuePair<string,
                             SortedDictionary<int, List<Sprite>>> pair in _sprites)
                    _variantIndices[pair.Key] =
                        new List<int>(pair.Value.Keys).ToArray();
            }

            public bool TryResolve(Building pBuilding, Sprite pOriginal,
                out Sprite pResult)
            {
                pResult = null;
                string category = ResolveCategory(pOriginal.name);
                if (category == null)
                    return false;
                if (!_sprites.TryGetValue(category,
                        out SortedDictionary<int, List<Sprite>> variants) ||
                    variants.Count == 0 ||
                    !_variantIndices.TryGetValue(category,
                        out int[] available))
                    return false;

                int requested = category == "construction"
                    ? 0
                    : pBuilding.animData_index;
                int resolved =
                    MandateCapitalBuildingTextureRules.ResolveVariantIndex(
                        available, requested);
                if (resolved < 0 || !variants.TryGetValue(resolved,
                        out List<Sprite> frames) || frames.Count == 0)
                    return false;

                pResult = category == "main" && frames.Count > 1
                    ? AnimationHelper.getSpriteFromList(pBuilding.GetHashCode(),
                        frames, pBuilding.asset.animation_speed)
                    : frames[0];
                return pResult != null;
            }

            private static string ResolveCategory(string pName)
            {
                if (string.IsNullOrEmpty(pName)) return null;
                if (pName.StartsWith("main_", StringComparison.Ordinal))
                    return "main";
                if (pName.StartsWith("ruin_", StringComparison.Ordinal))
                    return "ruin";
                if (pName.StartsWith("construction_",
                        StringComparison.Ordinal))
                    return "construction";
                return null;
            }

            private static bool TryParseSpriteName(string pName,
                out string pCategory, out int pVariant, out int pFrame)
            {
                pCategory = string.Empty;
                pVariant = -1;
                pFrame = -1;
                if (string.IsNullOrWhiteSpace(pName)) return false;

                string[] parts = pName.Split('_');
                if (parts.Length < 2 ||
                    !int.TryParse(parts[1].Trim(), out pVariant))
                    return false;
                pCategory = parts[0];
                if (parts.Length >= 3)
                    int.TryParse(parts[2].Trim(), out pFrame);
                return true;
            }

            private static int CompareFrames(Sprite pLeft, Sprite pRight)
            {
                TryParseSpriteName(pLeft?.name, out _, out _, out int leftFrame);
                TryParseSpriteName(pRight?.name, out _, out _, out int rightFrame);
                int frameOrder = leftFrame.CompareTo(rightFrame);
                return frameOrder != 0
                    ? frameOrder
                    : string.CompareOrdinal(pLeft?.name, pRight?.name);
            }
        }
    }
}
