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

        /// <summary>
        ///     已发布的图集快照。**只读**,发布后永不修改 —— 换目录时整份替换。
        ///
        ///     vanilla 的 <c>BuildingManager.precalculateRenderDataParallel</c> 在
        ///     <c>Parallel.For</c> 里调 <c>Building.calculateMainSprite</c>,所以我们的
        ///     Postfix 跑在**工作线程**上。原来这里是一个普通 Dictionary,多个工作
        ///     线程同时 miss 就会并发写它,把内部桶数组撕开,报成
        ///     <c>Dictionary.TryInsert</c> 里的 NullReferenceException。
        ///
        ///     值为 null 表示「查过,没有这套贴图」,与「没查过」区分开,免得每帧
        ///     重新排队。
        /// </summary>
        private static volatile Dictionary<string, CapitalSpriteCatalog>
            _published = new Dictionary<string, CapitalSpriteCatalog>(
                StringComparer.Ordinal);

        private static readonly object PendingGate = new object();
        private static readonly HashSet<string> Pending =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        ///     在主线程上把排队的图集建出来并发布。
        ///
        ///     必须在主线程:图集构造要调 <c>SpriteTextureLoader.getSpriteList</c>
        ///     (内部是 <c>Resources.LoadAll</c>,还写它自己的无锁静态缓存)和
        ///     <c>DynamicSpriteCreator.createBuildingShadow</c>(造纹理并写进共享
        ///     图集)—— 这两个都是 Unity 主线程专用,在工作线程上调是未定义行为。
        ///     挂在并行渲染预计算之前,让工作线程这一帧就能读到。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildingManager),
            "precalculateRenderDataParallel")]
        public static void PrecalculateRenderDataParallel_Prefix()
        {
            DrainPendingCatalogs();
        }

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

        /// <summary>
        ///     只读查表。可能跑在工作线程上,所以这里不加载、不建图集、不写任何
        ///     共享状态 —— 没命中就排队,这一帧照原样渲染,下一帧生效。
        /// </summary>
        private static bool TryGetCatalog(BuildingAsset pAsset,
            out CapitalSpriteCatalog pCatalog)
        {
            pCatalog = null;
            string id = pAsset.id;
            if (string.IsNullOrEmpty(id)) return false;

            Dictionary<string, CapitalSpriteCatalog> published = _published;
            if (published.TryGetValue(id, out pCatalog)) return pCatalog != null;

            lock (PendingGate) Pending.Add(id);
            return false;
        }

        private static void DrainPendingCatalogs()
        {
            string[] requested;
            lock (PendingGate)
            {
                if (Pending.Count == 0) return;
                requested = new string[Pending.Count];
                Pending.CopyTo(requested);
                Pending.Clear();
            }

            Dictionary<string, CapitalSpriteCatalog> published = _published;
            var next = new Dictionary<string, CapitalSpriteCatalog>(published,
                StringComparer.Ordinal);
            bool changed = false;
            for (int index = 0; index < requested.Length; index++)
            {
                string id = requested[index];
                if (string.IsNullOrEmpty(id) || next.ContainsKey(id)) continue;
                next[id] = BuildCatalog(id);
                changed = true;
            }
            // 整份替换。读者要么看到旧表要么看到新表,两份都是构造完整的,
            // 不会读到半建好的桶数组。
            if (changed) _published = next;
        }

        private static CapitalSpriteCatalog BuildCatalog(string pId)
        {
            BuildingAsset asset;
            try { asset = AssetManager.buildings.get(pId); }
            catch { asset = null; }
            if (asset == null) return null;

            Sprite[] sprites;
            try
            {
                sprites = SpriteTextureLoader.getSpriteList(
                    CAPITAL_PATH + pId, true);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Mandate capital sprites failed for " +
                                    pId + ": " + error.Message);
                return null;
            }
            if (sprites == null || sprites.Length == 0) return null;

            try
            {
                var catalog = new CapitalSpriteCatalog(asset, sprites);
                return catalog.IsEmpty ? null : catalog;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Mandate capital catalog failed for " +
                                    pId + ": " + error.Message);
                return null;
            }
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
