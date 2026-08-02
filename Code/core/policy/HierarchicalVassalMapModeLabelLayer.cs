using System;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapModeLabelLayer
    {
        private const float LabelZ = -0.45f;
        private const float CountryLabelMinSize =
            HierarchicalVassalMapModeRules.SmallTerritoryMinimumLabelSize;
        private const float CountryLabelMaxSize =
            HierarchicalVassalMapModeRules.MaximumLabelSize;
        private const float TileCenterOffset = 0.5f;

        private static readonly List<LabelNode> Nodes =
            new List<LabelNode>();
        private static GameObject _root;
        private static bool _dirty = true;
        private static bool _reportedFailure;
        private static bool _rebuildActive;
        private static HierarchicalVassalMapModeSnapshot _rebuildSnapshot;
        private static IReadOnlyList<HierarchicalVassalKingdomSnapshot>
            _rebuildEntries;
        private static List<City> _rebuildCities;
        private static Dictionary<long, City> _rebuildCityCandidates;
        private static bool _rebuildCitiesReady;
        private static int _rebuildCityZoneIndex;
        private static int _rebuildEntryIndex;
        private static int _rebuildCityIndex;
        private static int _rebuildNodeIndex;

        internal static void MarkDirty()
        {
            _dirty = true;
        }

        internal static void ProcessFrame()
        {
            try
            {
                if (!Config.game_loaded ||
                    !HierarchicalVassalMapModeService.IsActive())
                {
                    SetRootActive(false);
                    return;
                }

                EnsureRoot();
                SetRootActive(true);
                HierarchicalVassalMapModeService.RefreshIfWorldChanged();
                HierarchicalVassalMapModeService.ProcessFrame();
                if (_dirty && !_rebuildActive &&
                    HierarchicalVassalMapModeService.IsSnapshotReady)
                    BeginRebuild();
                if (_rebuildActive) ProcessRebuild();
            }
            catch (Exception error)
            {
                SetRootActive(false);
                if (_reportedFailure) return;
                _reportedFailure = true;
                try
                {
                    ModClass.LogWarning(
                        "Hierarchical map labels failed: " + error.Message);
                }
                catch { }
            }
        }

        internal static void Reset()
        {
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
            }
            _root = null;
            Nodes.Clear();
            _dirty = true;
            _reportedFailure = false;
            _rebuildActive = false;
            _rebuildSnapshot = null;
            _rebuildEntries = null;
            _rebuildCities = null;
            _rebuildCityCandidates = null;
            _rebuildCitiesReady = false;
            _rebuildCityZoneIndex = 0;
            _rebuildEntryIndex = 0;
            _rebuildCityIndex = 0;
            _rebuildNodeIndex = 0;
        }

        private static void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("AW3_HierarchicalVassalMapLabels");
            if (World.world != null)
                _root.transform.SetParent(World.world.transform, false);
        }

        private static void BeginRebuild()
        {
            HierarchicalVassalMapModeSnapshot snapshot =
                HierarchicalVassalMapModeService.BuildVisibleSnapshot();
            if (snapshot == null)
            {
                _dirty = true;
                return;
            }

            _dirty = false;
            _rebuildActive = true;
            _rebuildSnapshot = snapshot;
            _rebuildEntries = snapshot.Entries;
            _rebuildCities = null;
            _rebuildCityCandidates = HierarchicalVassalMapModeService.
                IsCityLayer
                ? new Dictionary<long, City>()
                : null;
            _rebuildCitiesReady = !HierarchicalVassalMapModeService.
                IsCityLayer;
            _rebuildCityZoneIndex = 0;
            _rebuildEntryIndex = 0;
            _rebuildCityIndex = 0;
            _rebuildNodeIndex = 0;
        }

        private static void ProcessRebuild()
        {
            if (!_rebuildActive) return;
            int budget = HierarchicalVassalMapModeSchedulingRules.
                ClampLabelBudget(
                    HierarchicalVassalMapModeSchedulingRules.MaximumLabelBudget);
            bool cityLayer = HierarchicalVassalMapModeService.IsCityLayer;
            while (budget-- > 0)
            {
                if (!cityLayer && _rebuildEntries != null &&
                    _rebuildEntryIndex < _rebuildEntries.Count)
                {
                    HierarchicalVassalKingdomSnapshot entry =
                        _rebuildEntries[_rebuildEntryIndex++];
                    Kingdom kingdom = GetKingdom(entry?.KingdomId ?? -1L);
                    if (entry != null && kingdom != null &&
                        !string.IsNullOrWhiteSpace(entry.DisplayName))
                    {
                        Color labelColor = ResolveCountryLabelColor(kingdom);
                        Color outlineColor = ResolveCountryOutlineColor(kingdom);
                        Vector3 position = new Vector3(
                            entry.Centroid.x + TileCenterOffset,
                            entry.Centroid.y + TileCenterOffset, LabelZ);
                        float size = Mathf.Clamp(entry.LabelSize,
                            CountryLabelMinSize, CountryLabelMaxSize);
                        string label = string.IsNullOrWhiteSpace(
                            entry.LabelDisplayName)
                            ? entry.DisplayName
                            : entry.LabelDisplayName;
                        UseNode(ref _rebuildNodeIndex, label, position,
                            size, labelColor, outlineColor, true,
                            entry.LabelAngle, entry.CountryLabelGap);
                    }
                    continue;
                }

                if (cityLayer && _rebuildCities != null &&
                    _rebuildCitiesReady &&
                    _rebuildCityIndex < _rebuildCities.Count)
                {
                    DrawOneCityLabel(_rebuildCities[_rebuildCityIndex++],
                        _rebuildSnapshot, ref _rebuildNodeIndex);
                    continue;
                }

                if (cityLayer && !_rebuildCitiesReady)
                {
                    ProcessCityCandidateBudget();
                    return;
                }

                CompleteRebuild();
                return;
            }
        }

        private static void CompleteRebuild()
        {
            for (int index = _rebuildNodeIndex; index < Nodes.Count; index++)
                Nodes[index].SetActive(false);
            _rebuildActive = false;
            _rebuildSnapshot = null;
            _rebuildEntries = null;
            _rebuildCities = null;
            _rebuildCityCandidates = null;
            _rebuildCitiesReady = false;
        }

        private static void ProcessCityCandidateBudget()
        {
            if (_rebuildSnapshot?.DrawableZones == null ||
                _rebuildCityCandidates == null) return;
            const int zoneBudget = 256;
            int processed = 0;
            while (_rebuildCityZoneIndex < _rebuildSnapshot.DrawableZones.Count &&
                   processed++ < zoneBudget)
            {
                City city = _rebuildSnapshot.DrawableZones[
                    _rebuildCityZoneIndex++]?.city;
                if (city?.data == null || city.isRekt() ||
                    string.IsNullOrWhiteSpace(city.data.name)) continue;
                if (!_rebuildCityCandidates.ContainsKey(city.id))
                    _rebuildCityCandidates.Add(city.id, city);
            }
            if (_rebuildCityZoneIndex < _rebuildSnapshot.DrawableZones.Count)
                return;

            _rebuildCities = new List<City>(_rebuildCityCandidates.Values);
            _rebuildCities.Sort((pLeft, pRight) =>
                pLeft.id.CompareTo(pRight.id));
            _rebuildCitiesReady = true;
        }

        private static void DrawOneCityLabel(City pCity,
            HierarchicalVassalMapModeSnapshot pSnapshot,
            ref int pNodeIndex)
        {
            if (pCity?.zones == null || pSnapshot?.ZoneToKingdomId == null)
                return;
            HierarchicalVassalMapModeCityCacheEntry cached =
                HierarchicalVassalMapModeCityCache.Get(pCity);
            if (cached == null || cached.LandTiles.Count == 0) return;

            bool visible = false;
            for (int zoneIndex = 0; zoneIndex < cached.VisibleZones.Count;
                 zoneIndex++)
            {
                TileZone zone = cached.VisibleZones[zoneIndex];
                if (zone != null && zone.id >= 0 &&
                    pSnapshot.ZoneToKingdomId.ContainsKey(zone.id))
                {
                    visible = true;
                    break;
                }
            }
            if (!visible) return;

            HierarchicalVassalMapModeGeometryMetrics metrics = cached.Metrics;
            float size = HierarchicalVassalMapModeGeometry.
                CalculateCityLabelSize(metrics.Area);
            Color color = ResolveCityLabelColor(pCity.kingdom);
            Color outlineColor = ResolveCityOutlineColor(pCity.kingdom);
            UseNode(ref pNodeIndex, pCity.data.name,
                new Vector3(metrics.Centroid.x + TileCenterOffset,
                    metrics.Centroid.y + TileCenterOffset,
                    LabelZ - 0.02f), size, color, outlineColor, false,
                metrics.Angle, 0);
        }

        private static void DrawCityLabels(
            HierarchicalVassalMapModeSnapshot pSnapshot,
            ref int pNodeIndex)
        {
            if (pSnapshot?.ZoneToKingdomId == null ||
                pSnapshot.DrawableZones == null) return;

            // Derive the city set from the visible snapshot instead of
            // scanning every kingdom. This keeps focused hierarchy views
            // bounded to their own terrain while still rendering every
            // visible city.
            var visibleCities = new Dictionary<long, City>();
            for (int zoneIndex = 0;
                 zoneIndex < pSnapshot.DrawableZones.Count; zoneIndex++)
            {
                TileZone zone = pSnapshot.DrawableZones[zoneIndex];
                City city = zone?.city;
                if (city?.data == null || city.isRekt() ||
                    string.IsNullOrWhiteSpace(city.data.name)) continue;
                if (!visibleCities.ContainsKey(city.id))
                    visibleCities.Add(city.id, city);
            }

            var cities = new List<City>(visibleCities.Values);
            cities.Sort((pLeft, pRight) => pLeft.id.CompareTo(pRight.id));
            for (int cityIndex = 0; cityIndex < cities.Count; cityIndex++)
            {
                City city = cities[cityIndex];
                if (city?.zones == null) continue;

                bool visible = false;
                HierarchicalVassalMapModeCityCacheEntry cached =
                    HierarchicalVassalMapModeCityCache.Get(city);
                if (cached == null) continue;
                for (int zoneIndex = 0; zoneIndex < cached.VisibleZones.Count;
                     zoneIndex++)
                {
                    TileZone zone = cached.VisibleZones[zoneIndex];
                    if (zone != null && zone.id >= 0 &&
                        pSnapshot.ZoneToKingdomId.ContainsKey(zone.id))
                        visible = true;
                }

                if (!visible || cached.LandTiles.Count == 0) continue;

                HierarchicalVassalMapModeGeometryMetrics metrics =
                    cached.Metrics;
                float size =
                    HierarchicalVassalMapModeGeometry.CalculateCityLabelSize(
                        metrics.Area);
                Color color = ResolveCityLabelColor(city.kingdom);
                Color outlineColor = ResolveCityOutlineColor(city.kingdom);
                UseNode(ref pNodeIndex, city.data.name,
                    new Vector3(metrics.Centroid.x + TileCenterOffset,
                        metrics.Centroid.y + TileCenterOffset,
                        LabelZ - 0.02f),
                    size, color, outlineColor, false,
                    metrics.Angle, 0);
            }
        }

        private static void UseNode(ref int pIndex, string pText,
            Vector3 pPosition, float pSize, Color pColor, Color pOutlineColor,
            bool pCountry, float pAngle, int pCountryLabelGap)
        {
            LabelNode node;
            if (pIndex >= Nodes.Count)
            {
                node = new LabelNode(_root.transform, pIndex);
                Nodes.Add(node);
            }
            else
            {
                node = Nodes[pIndex];
            }

            node.Apply(pText, pPosition, pSize, pColor, pOutlineColor,
                pCountry, pAngle, pCountryLabelGap);
            pIndex++;
        }

        private static Kingdom GetKingdom(long pKingdomId)
        {
            if (pKingdomId < 0L || World.world?.kingdoms == null) return null;
            try
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                    if (kingdom?.id == pKingdomId) return kingdom;
            }
            catch { }
            return null;
        }

        private static Color ResolveCountryLabelColor(Kingdom pKingdom)
        {
            Color color = new Color(0.45f, 0.42f, 0.36f, 0.94f);
            try
            {
                if (pKingdom?.getColor() != null)
                    color = pKingdom.getColor().getColorMain();
            }
            catch { }
            color.r = HierarchicalVassalMapModeRules.
                DarkenCountryColorChannel(color.r);
            color.g = HierarchicalVassalMapModeRules.
                DarkenCountryColorChannel(color.g);
            color.b = HierarchicalVassalMapModeRules.
                DarkenCountryColorChannel(color.b);
            color.a = 0.98f;
            return color;
        }

        private static Color ResolveCountryOutlineColor(Kingdom pKingdom)
        {
            return Color.white;
        }

        private static Color ResolveCityLabelColor(Kingdom pKingdom)
        {
            return Color.white;
        }

        private static Color ResolveCityOutlineColor(Kingdom pKingdom)
        {
            Color color = new Color(0.18f, 0.18f, 0.18f, 0.98f);
            try
            {
                ColorAsset asset = pKingdom?.getColor();
                if (asset != null) color = asset.getColorMainSecond();
            }
            catch { }
            color.a = 0.98f;
            return color;
        }

        private static void SetRootActive(bool pActive)
        {
            if (_root != null && _root.activeSelf != pActive)
                _root.SetActive(pActive);
        }

        private sealed class LabelNode
        {
            private const int OutlinePassCount = 8;
            private const float CountryOutlineThicknessFactor = 0.05f;
            private const float CityOutlineThicknessFactor = 1.0f;
            private const int CountryOutlineSortingOrder = 0;
            private const int CountryTextSortingOrder = 1;
            private const int CityBackgroundSortingOrder = 899;
            private const int CityOutlineSortingOrder = 900;
            private const int CityTextSortingOrder = 901;
            private static Font _mapFont;
            private static bool _mapFontResolved;
            private static Sprite _backgroundSprite;
            private static Texture2D _backgroundTexture;
            private readonly GameObject _root;
            private readonly SpriteRenderer _background;
            private readonly TextMesh[] _outlines;
            private readonly TextMesh _text;
            private TextMesh[] _secondOutlines;
            private TextMesh _secondText;

            internal LabelNode(Transform pParent, int pIndex)
            {
                _root = new GameObject("aw3_map_label_" + pIndex);
                _root.transform.SetParent(pParent, false);
                _background = CreateBackground(_root.transform);
                _outlines = new TextMesh[OutlinePassCount];
                for (int index = 0; index < _outlines.Length; index++)
                    _outlines[index] = CreateText(_root.transform,
                        "outline_" + index, 900);
                _text = CreateText(_root.transform, "text", 901);
            }

            internal void Apply(string pValue, Vector3 pPosition,
                float pSize, Color pColor, Color pOutlineColor,
                bool pCountry, float pAngle, int pCountryLabelGap)
            {
                _root.SetActive(true);
                _root.transform.position = pPosition;
                _root.transform.rotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;
                string value = pValue?.Trim() ?? string.Empty;
                bool splitCountry = pCountry && value.Length == 2 &&
                                    pCountryLabelGap > 0;
                if (splitCountry)
                {
                    ApplySplitCountryLabel(value, pSize, pColor,
                        pOutlineColor, pCountryLabelGap);
                }
                else
                {
                    SetSecondGlyphActive(false);
                    ApplyGlyph(_text, _outlines, value, Vector3.zero, pSize,
                        pColor, pOutlineColor, pCountry);
                }
                ApplySortingLayer(pCountry);
                _root.transform.rotation = Quaternion.Euler(0f, 0f, pAngle);
                ApplyBackground(pColor, pOutlineColor, pCountry, pSize,
                    value);
            }

            private void ApplySplitCountryLabel(string pValue, float pSize,
                Color pColor, Color pOutlineColor, int pCountryLabelGap)
            {
                EnsureSecondGlyph();
                SetSecondGlyphActive(true);
                float offset = HierarchicalVassalMapModeGeometry.
                    CalculateCountryGlyphCenterOffset(pSize,
                        pCountryLabelGap);
                ApplyGlyph(_text, _outlines, pValue[0].ToString(),
                    new Vector3(-offset, 0f, 0f), pSize, pColor,
                    pOutlineColor, true);
                ApplyGlyph(_secondText, _secondOutlines,
                    pValue[1].ToString(), new Vector3(offset, 0f, 0f), pSize,
                    pColor, pOutlineColor, true);
            }

            private static void ApplyGlyph(TextMesh pText,
                TextMesh[] pOutlines, string pValue, Vector3 pCenter,
                float pSize, Color pColor, Color pOutlineColor,
                bool pCountry)
            {
                RefreshFont(pText);
                pText.gameObject.SetActive(true);
                pText.transform.localPosition = pCenter;
                pText.text = pValue;
                const float probeCharacterSize = 1f;
                pText.characterSize = probeCharacterSize;
                // Both hierarchy levels are map labels rather than body text;
                // bold glyphs stay legible over terrain at the smallest zoom.
                pText.fontStyle = FontStyle.Normal;
                pText.color = pColor;
                Renderer textRenderer = pText.GetComponent<Renderer>();
                Vector3 measuredBounds = textRenderer == null
                    ? Vector3.zero
                    : textRenderer.bounds.size;
                float renderedCharacterSize =
                    HierarchicalVassalMapModeGeometry.
                        CalculateRenderedCharacterSize(
                            pSize, pValue, measuredBounds.x,
                            measuredBounds.y, probeCharacterSize);
                pText.characterSize = renderedCharacterSize;
                float outlineThickness = Mathf.Max(0.025f,
                    pSize * (pCountry
                        ? CountryOutlineThicknessFactor
                        : CityOutlineThicknessFactor));
                for (int index = 0; index < pOutlines.Length; index++)
                {
                    TextMesh outline = pOutlines[index];
                    RefreshFont(outline);
                    outline.gameObject.SetActive(true);
                    outline.text = pValue;
                    outline.characterSize = renderedCharacterSize;
                    outline.fontStyle = pText.fontStyle;
                    outline.color = pOutlineColor;
                    float angle = index * Mathf.PI * 2f /
                        pOutlines.Length;
                    outline.transform.localPosition = pCenter + new Vector3(
                        Mathf.Cos(angle) * outlineThickness,
                        Mathf.Sin(angle) * outlineThickness, 0.02f);
                }
            }

            private void EnsureSecondGlyph()
            {
                if (_secondText != null) return;
                _secondOutlines = new TextMesh[OutlinePassCount];
                for (int index = 0; index < _secondOutlines.Length; index++)
                    _secondOutlines[index] = CreateText(_root.transform,
                        "second_outline_" + index,
                        CountryOutlineSortingOrder);
                _secondText = CreateText(_root.transform, "second_text",
                    CountryTextSortingOrder);
            }

            private void SetSecondGlyphActive(bool pActive)
            {
                if (_secondText == null) return;
                _secondText.gameObject.SetActive(pActive);
                for (int index = 0; index < _secondOutlines.Length; index++)
                    _secondOutlines[index].gameObject.SetActive(pActive);
            }

            private void ApplySortingLayer(bool pCountry)
            {
                int layerId = pCountry
                    ? SortingLayer.NameToID("EffectsBack")
                    : SortingLayer.NameToID("MapOverlay");
                int outlineOrder = pCountry
                    ? CountryOutlineSortingOrder
                    : CityOutlineSortingOrder;
                int textOrder = pCountry
                    ? CountryTextSortingOrder
                    : CityTextSortingOrder;
                ApplySorting(_text, layerId, textOrder);
                for (int index = 0; index < _outlines.Length; index++)
                    ApplySorting(_outlines[index], layerId, outlineOrder);
                if (_secondText != null)
                {
                    ApplySorting(_secondText, layerId, textOrder);
                    for (int index = 0; index < _secondOutlines.Length;
                         index++)
                        ApplySorting(_secondOutlines[index], layerId,
                            outlineOrder);
                }
                if (_background != null)
                {
                    _background.sortingLayerID = layerId;
                    _background.sortingOrder = pCountry
                        ? CountryOutlineSortingOrder - 1
                        : CityBackgroundSortingOrder;
                }
            }

            private static void ApplySorting(TextMesh pText, int pLayerId,
                int pSortingOrder)
            {
                Renderer renderer = pText?.GetComponent<Renderer>();
                if (renderer == null) return;
                renderer.sortingLayerID = pLayerId;
                renderer.sortingOrder = pSortingOrder;
            }

            internal void SetActive(bool pActive)
            {
                if (_root.activeSelf != pActive) _root.SetActive(pActive);
                if (_background != null) _background.enabled = pActive;
            }

            private void ApplyBackground(Color pColor, Color pOutlineColor,
                bool pCountry, float pSize, string pValue)
            {
                if (_background == null) return;
                // Country names are intentionally integrated directly into
                // the terrain. City names get a small readability plate.
                _background.enabled = !pCountry;
                if (pCountry) return;
                Color backgroundColor = Color.Lerp(pOutlineColor,
                    Color.black, 0.7f);
                backgroundColor.a = 0.2f;
                _background.color = backgroundColor;

                float width = pSize * Mathf.Max(1f,
                    (pValue?.Length ?? 1) * 0.86f);
                // Keep the plate's height tied to the glyph height. The
                // previous renderer-bounds projection enlarged it vertically
                // whenever a label was rotated.
                float height = pSize * 1.16f;
                float backgroundSizePadding = Mathf.Max(0.12f,
                    pSize * 0.1f);
                _background.transform.localPosition = new Vector3(0f, 0f,
                    0.04f);
                _background.transform.localScale = new Vector3(
                    width + backgroundSizePadding * 2f,
                    height + backgroundSizePadding * 2f, 1f);
            }

            private static SpriteRenderer CreateBackground(
                Transform pParent)
            {
                GameObject objectForBackground = new GameObject("background");
                objectForBackground.transform.SetParent(pParent, false);
                SpriteRenderer renderer = objectForBackground.AddComponent<
                    SpriteRenderer>();
                renderer.sprite = ResolveBackgroundSprite();
                renderer.sortingOrder = 899;
                try
                {
                    renderer.sortingLayerID =
                        SortingLayer.NameToID("MapOverlay");
                }
                catch { }
                renderer.enabled = false;
                return renderer;
            }

            private static Sprite ResolveBackgroundSprite()
            {
                if (_backgroundSprite != null) return _backgroundSprite;
                try
                {
                    _backgroundTexture = new Texture2D(1, 1,
                        TextureFormat.RGBA32, false, true);
                    _backgroundTexture.name = "AW3_MapLabelBackground";
                    _backgroundTexture.SetPixel(0, 0, Color.white);
                    _backgroundTexture.Apply(false, true);
                    _backgroundSprite = Sprite.Create(
                        _backgroundTexture,
                        new Rect(0f, 0f, 1f, 1f),
                        new Vector2(0.5f, 0.5f), 1f);
                }
                catch { }
                return _backgroundSprite;
            }

            private static TextMesh CreateText(Transform pParent,
                string pName, int pSortingOrder)
            {
                GameObject objectForText = new GameObject(pName);
                objectForText.transform.SetParent(pParent, false);
                TextMesh text = objectForText.AddComponent<TextMesh>();
                text.font = ResolveMapFont();
                if (text.font == null)
                    text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.fontSize = 100;
                text.characterSize = CountryLabelMinSize;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.richText = false;
                MeshRenderer renderer = objectForText.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sortingOrder = pSortingOrder;
                    try
                    {
                        renderer.sortingLayerID =
                            SortingLayer.NameToID("MapOverlay");
                    }
                    catch { }
                }
                return text;
            }

            private static void RefreshFont(TextMesh pText)
            {
                if (pText == null) return;
                Font font = ResolveMapFont();
                if (font == null) return;
                pText.font = font;
                Renderer renderer = pText.GetComponent<Renderer>();
                if (renderer != null && pText.font.material != null)
                    renderer.sharedMaterial = pText.font.material;
            }

            private static Font ResolveMapFont()
            {
                if (_mapFontResolved) return _mapFont;
                _mapFontResolved = true;
                Font bundled = HierarchicalVassalMapFontLoader.TryLoad(16);
                if (bundled != null) return _mapFont = bundled;
                try
                {
                    Font font = Font.CreateDynamicFontFromOSFont(
                        new[] { "SimSun", "Songti SC", "\u5b8b\u4f53" }, 16);
                    if (font != null) return _mapFont = font;
                }
                catch { }
                try
                {
                    Font font = Resources.Load<Font>(
                        "Fonts/NotoSansCJKsc-Bold");
                    if (font != null) return _mapFont = font;
                }
                catch { }
                return _mapFont = LocalizedTextManager.current_font;
            }
        }
    }
}
