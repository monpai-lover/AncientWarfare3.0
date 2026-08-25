using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly Dictionary<string, LabelNode> RuntimeNodes =
            new Dictionary<string, LabelNode>();
        private static readonly Dictionary<NativeLabelIdentity, string>
            NativeLabelKeys =
                new Dictionary<NativeLabelIdentity, string>();
        private static readonly List<string> NativeEvictionKeys =
            new List<string>();
        private static GameObject _root;
        private static bool _activeKnown;
        private static bool _active;
        private static bool _resolutionModeKnown;
        private static bool _lastMinimapMode;
        private static bool _reportedFailure;

        internal static void MarkDirty()
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.MarkDirty(false);
        }

        internal static void ForceRebuild()
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.MarkDirty(true);
        }

        internal static void MarkViewChanged()
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.MarkViewChanged();
        }

        internal static void RequestRefresh()
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.RequestRefresh();
        }

        internal static void RefreshMapFont()
        {
            HierarchicalVassalMapFontLoader.Reset();
            LabelNode.ResetMapFont();
            foreach (LabelNode node in Nodes)
                node.RefreshMapFont();
            foreach (LabelNode node in RuntimeNodes.Values)
                node.RefreshMapFont();
            RequestRefresh();
        }

        internal static void MarkCityDirty(City pCity)
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.MarkCityDirty(pCity);
        }

        internal static void MarkCityGeometryDirty(City pCity)
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.MarkCityGeometryDirty(pCity);
        }

        internal static void MarkCityZoneGeometryDirty(City pCity,
            TileZone pZone)
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.MarkCityZoneGeometryDirty(
                pCity, pZone);
        }

        internal static void EvictCity(long pCityId)
        {
            HierarchicalVassalMapLabelRuntime.EvictCity(pCityId);
        }

        internal static void EvictNativeCity(long pCityId)
        {
            EvictNativeLabels((pCountry, _, pEntityId) =>
                !pCountry && pEntityId == pCityId);
        }

        internal static string GetNativeLabelKey(bool pCountry,
            long pFocusId, long pEntityId)
        {
            var identity = new NativeLabelIdentity(pCountry, pFocusId,
                pEntityId);
            if (NativeLabelKeys.TryGetValue(identity, out string key))
                return key;
            key = "native:" + (pCountry ? "country:" : "city:") +
                  pFocusId.ToString(CultureInfo.InvariantCulture) + ":" +
                  pEntityId.ToString(CultureInfo.InvariantCulture);
            NativeLabelKeys.Add(identity, key);
            return key;
        }

        internal static void EvictKingdom(long pKingdomId)
        {
            HierarchicalVassalMapLabelRuntime.EvictKingdom(pKingdomId);
        }

        internal static void EvictNativeKingdom(long pKingdomId)
        {
            EvictNativeLabels((pCountry, pFocusId, pEntityId) =>
                pFocusId == pKingdomId ||
                pCountry && pEntityId == pKingdomId);
        }

        private static void EvictNativeLabels(
            Func<bool, long, long, bool> pShouldEvict)
        {
            if (pShouldEvict == null) return;
            NativeEvictionKeys.Clear();
            foreach (string key in RuntimeNodes.Keys)
            {
                if (!TryParseNativeLabelKey(key, out bool country,
                        out long focusId, out long entityId) ||
                    !pShouldEvict(country, focusId, entityId)) continue;
                NativeEvictionKeys.Add(key);
            }
            for (int index = 0; index < NativeEvictionKeys.Count; index++)
                RemoveRuntimeLabel(NativeEvictionKeys[index]);
            NativeEvictionKeys.Clear();
        }

        private static bool TryParseNativeLabelKey(string pKey,
            out bool pCountry, out long pFocusId, out long pEntityId)
        {
            pCountry = false;
            pFocusId = -1L;
            pEntityId = -1L;
            const string countryPrefix = "native:country:";
            const string cityPrefix = "native:city:";
            int valueStart;
            if (pKey?.StartsWith(countryPrefix,
                    StringComparison.Ordinal) == true)
            {
                pCountry = true;
                valueStart = countryPrefix.Length;
            }
            else if (pKey?.StartsWith(cityPrefix,
                         StringComparison.Ordinal) == true)
            {
                valueStart = cityPrefix.Length;
            }
            else return false;

            int separator = pKey.IndexOf(':', valueStart);
            if (separator <= valueStart || separator >= pKey.Length - 1)
                return false;
            return long.TryParse(pKey.Substring(valueStart,
                    separator - valueStart), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out pFocusId) &&
                long.TryParse(pKey.Substring(separator + 1),
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out pEntityId);
        }

        internal static void MarkKingdomDirty(Kingdom pKingdom)
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.MarkKingdomDirty(pKingdom);
        }

        internal static void MarkHierarchyDirty()
        {
            _reportedFailure = false;
            HierarchicalVassalMapLabelRuntime.MarkHierarchyDirty();
        }

        internal static void ObserveResolutionMode(bool pMinimap)
        {
            if (_resolutionModeKnown && _lastMinimapMode == pMinimap) return;
            _resolutionModeKnown = true;
            _lastMinimapMode = pMinimap;
            try
            {
                for (int index = 0; index < Nodes.Count; index++)
                    Nodes[index]?.RefreshSortingLayer(pMinimap);
            }
            catch { }
        }

        internal static bool NeedsProcessFrame =>
            HierarchicalVassalMapLabelRuntime.NeedsProcessFrame;

        internal static int RuntimeNodeCountForDiagnostics =>
            RuntimeNodes.Count;

        internal static void ObserveMapModeActive(bool pActive)
        {
            if (_activeKnown && _active == pActive) return;
            _reportedFailure = false;
            _activeKnown = true;
            _active = pActive;
            HierarchicalVassalMapLabelRuntime.ObserveMapModeActive(pActive);
            if (pActive)
            {
                EnsureRoot();
                SetRootActive(true);
                return;
            }
            SetRootActive(false);
        }

        internal static void CancelUnpublishedJobs()
        {
            HierarchicalVassalMapLabelRuntime.CancelUnpublishedJobs();
        }

        internal static void ProcessFrame()
        {
            if (_reportedFailure) return;
            try
            {
                if (!Config.game_loaded)
                {
                    SetRootActive(false);
                    return;
                }

                if (_active)
                {
                    EnsureRoot();
                    SetRootActive(true);
                }
                else
                {
                    SetRootActive(false);
                }
                HierarchicalVassalMapLabelRuntime.ProcessFrame();
            }
            catch (Exception error)
            {
                try
                {
                    HierarchicalVassalMapLabelRuntime.
                        RecoverFromProcessFailure();
                }
                catch { }
                if (!_active) SetRootActive(false);
                _reportedFailure = !HierarchicalVassalMapLabelRuntime.
                    CanRetryProcessFailure;
                if (_reportedFailure) return;
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
            HierarchicalVassalMapLabelRuntime.Reset();
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
            }
            _root = null;
            Nodes.Clear();
            RuntimeNodes.Clear();
            NativeLabelKeys.Clear();
            NativeEvictionKeys.Clear();
            _activeKnown = false;
            _active = false;
            _resolutionModeKnown = false;
            _lastMinimapMode = false;
            _reportedFailure = false;
        }

        private static void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("AW3_HierarchicalVassalMapLabels");
            if (World.world != null)
                _root.transform.SetParent(World.world.transform, false);
        }

        internal static void ApplyRuntimeLabel(string pKey, string pText,
            HierarchicalVassalMapModeLabelPlacement pPlacement,
            int pCountryLabelGap, bool pCountry, Kingdom pKingdom,
            City pCity)
        {
            if (string.IsNullOrWhiteSpace(pKey) ||
                string.IsNullOrWhiteSpace(pText)) return;
            EnsureRoot();
            if (!RuntimeNodes.TryGetValue(pKey, out LabelNode node))
            {
                node = new LabelNode(_root.transform, Nodes.Count);
                RuntimeNodes[pKey] = node;
                Nodes.Add(node);
            }

            float size = HierarchicalVassalMapModeRules.
                ResolveRenderedLabelSize(pPlacement.Size, pCountry);
            Color color = pCountry
                ? ResolveCountryLabelColor(pKingdom)
                : ResolveCityLabelColor(pCity?.kingdom ?? pKingdom);
            Color outlineColor = pCountry
                ? ResolveCountryOutlineColor(pKingdom)
                : ResolveCityOutlineColor(pCity?.kingdom ?? pKingdom);
            float centerOffset = pCountry ? TileCenterOffset : 0f;
            node.Apply(pText, new Vector3(
                    pPlacement.Centroid.x + centerOffset,
                    pPlacement.Centroid.y + centerOffset,
                    pCountry ? LabelZ : LabelZ - 0.02f),
                size, color, outlineColor, pCountry, pPlacement.Angle,
                pCountryLabelGap, pCity);
        }

        internal static void HideRuntimeLabelsExcept(
            ISet<string> pActiveKeys)
        {
            foreach (KeyValuePair<string, LabelNode> pair in RuntimeNodes)
                if (pActiveKeys == null || !pActiveKeys.Contains(pair.Key))
                    pair.Value.SetActive(false);
        }

        internal static void HideNativeLabelsExcept(ISet<string> pActiveKeys)
        {
            foreach (KeyValuePair<string, LabelNode> pair in RuntimeNodes)
                if (TryParseNativeLabelKey(pair.Key, out _, out _, out _) &&
                    (pActiveKeys == null || !pActiveKeys.Contains(pair.Key)))
                    pair.Value.SetActive(false);
        }

        internal static void KeepNativeLabelsVisible()
        {
            // Empty clicks are not navigation. Re-show already-built native
            // entries after the engine's zone redraw without changing focus.
            foreach (KeyValuePair<string, LabelNode> pair in RuntimeNodes)
                if (TryParseNativeLabelKey(pair.Key, out _, out _, out _))
                    pair.Value.SetActive(true);
        }

        internal static bool ShowRuntimeLabel(string pKey)
        {
            if (string.IsNullOrWhiteSpace(pKey) ||
                !RuntimeNodes.TryGetValue(pKey, out LabelNode node))
                return false;
            node.SetActive(true);
            return true;
        }

        internal static void RefreshRuntimeLabelStyle(string pKey,
            bool pCountry, Kingdom pKingdom, City pCity)
        {
            if (string.IsNullOrWhiteSpace(pKey) ||
                !RuntimeNodes.TryGetValue(pKey, out LabelNode node)) return;
            Color color = pCountry
                ? ResolveCountryLabelColor(pKingdom)
                : ResolveCityLabelColor(pCity?.kingdom ?? pKingdom);
            Color outlineColor = pCountry
                ? ResolveCountryOutlineColor(pKingdom)
                : ResolveCityOutlineColor(pCity?.kingdom ?? pKingdom);
            node.RefreshStyle(color, outlineColor);
        }

        internal static void RemoveRuntimeLabel(string pKey)
        {
            if (string.IsNullOrWhiteSpace(pKey) ||
                !RuntimeNodes.TryGetValue(pKey, out LabelNode node)) return;
            RuntimeNodes.Remove(pKey);
            if (TryParseNativeLabelKey(pKey, out bool country,
                    out long focusId, out long entityId))
                NativeLabelKeys.Remove(new NativeLabelIdentity(country,
                    focusId, entityId));
            Nodes.Remove(node);
            node.Destroy();
        }

        private readonly struct NativeLabelIdentity :
            IEquatable<NativeLabelIdentity>
        {
            private readonly bool _country;
            private readonly long _focusId;
            private readonly long _entityId;

            internal NativeLabelIdentity(bool pCountry, long pFocusId,
                long pEntityId)
            {
                _country = pCountry;
                _focusId = pFocusId;
                _entityId = pEntityId;
            }

            public bool Equals(NativeLabelIdentity pOther)
            {
                return _country == pOther._country &&
                       _focusId == pOther._focusId &&
                       _entityId == pOther._entityId;
            }

            public override bool Equals(object pObject)
            {
                return pObject is NativeLabelIdentity other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _country ? 1 : 0;
                    hash = hash * 397 ^ _focusId.GetHashCode();
                    return hash * 397 ^ _entityId.GetHashCode();
                }
            }
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
            private const float CityOutlineThicknessFactor = 0.08f;
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
            private bool _country;
            private bool _hasLayout;
            private City _city;
            private string _lastValue = string.Empty;
            private Vector3 _lastPosition;
            private float _lastSize;
            private float _lastAngle;
            private int _lastCountryLabelGap;
            private Color _lastColor;
            private Color _lastOutlineColor;
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
                bool pCountry, float pAngle, int pCountryLabelGap, City pCity)
            {
                _root.SetActive(true);
                _city = pCity;
                string value = pValue?.Trim() ?? string.Empty;
                if (HasEquivalentLayout(value, pPosition, pSize, pCountry,
                        pAngle, pCountryLabelGap))
                {
                    RefreshStyle(pColor, pOutlineColor);
                    return;
                }
                _hasLayout = true;
                _lastValue = value;
                _lastPosition = pPosition;
                _lastSize = pSize;
                _lastAngle = pAngle;
                _lastCountryLabelGap = pCountryLabelGap;
                _lastColor = pColor;
                _lastOutlineColor = pOutlineColor;
                _country = pCountry;
                _root.transform.position = pPosition;
                _root.transform.rotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;
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
                ApplySortingLayer(pCountry,
                    _resolutionModeKnown && _lastMinimapMode);
                _root.transform.rotation = Quaternion.Euler(0f, 0f, pAngle);
                ApplyBackground(pColor, pOutlineColor, pCountry, pSize,
                    value);
            }

            private bool HasEquivalentLayout(string pValue,
                Vector3 pPosition, float pSize, bool pCountry, float pAngle,
                int pCountryLabelGap)
            {
                return _hasLayout && _country == pCountry &&
                       string.Equals(_lastValue, pValue,
                           StringComparison.Ordinal) &&
                       Mathf.Abs(_lastPosition.x - pPosition.x) <
                           HierarchicalVassalLabelResultRules.
                               PositionThreshold &&
                       Mathf.Abs(_lastPosition.y - pPosition.y) <
                           HierarchicalVassalLabelResultRules.
                               PositionThreshold &&
                       Mathf.Abs(_lastPosition.z - pPosition.z) < 0.001f &&
                       Mathf.Abs(_lastSize - pSize) <
                           HierarchicalVassalLabelResultRules.SizeThreshold &&
                       AngleDistance(_lastAngle, pAngle) <
                            HierarchicalVassalLabelResultRules.AngleThreshold &&
                       _lastCountryLabelGap == pCountryLabelGap;
            }

            private static float AngleDistance(float pLeft, float pRight)
            {
                float distance = Mathf.Abs(pLeft - pRight) % 360f;
                return distance > 180f ? 360f - distance : distance;
            }

            private static bool ColorsEqual(Color pLeft, Color pRight)
            {
                return Mathf.Abs(pLeft.r - pRight.r) < 0.001f &&
                       Mathf.Abs(pLeft.g - pRight.g) < 0.001f &&
                       Mathf.Abs(pLeft.b - pRight.b) < 0.001f &&
                       Mathf.Abs(pLeft.a - pRight.a) < 0.001f;
            }

            internal void RefreshStyle(Color pColor, Color pOutlineColor)
            {
                _root.SetActive(true);
                if (ColorsEqual(_lastColor, pColor) &&
                    ColorsEqual(_lastOutlineColor, pOutlineColor)) return;
                _lastColor = pColor;
                _lastOutlineColor = pOutlineColor;
                _text.color = pColor;
                for (int index = 0; index < _outlines.Length; index++)
                    _outlines[index].color = pOutlineColor;
                if (_secondText != null)
                {
                    _secondText.color = pColor;
                    for (int index = 0; index < _secondOutlines.Length;
                         index++)
                        _secondOutlines[index].color = pOutlineColor;
                }
                if (_background == null || _country) return;
                bool isCapital = _city != null &&
                                 _city.kingdom?.capital == _city;
                Color backgroundColor;
                if (isCapital)
                {
                    backgroundColor = new Color(1f, 0.95f, 0.5f, 0.35f);
                }
                else
                {
                    backgroundColor = Color.Lerp(pOutlineColor,
                        Color.black, 0.7f);
                    backgroundColor.a = 0.2f;
                }
                _background.color = backgroundColor;
                _background.color = backgroundColor;
            }

            internal void RefreshSortingLayer(bool pMinimap)
            {
                ApplySortingLayer(_country, pMinimap);
            }

            internal void RefreshMapFont()
            {
                RefreshFont(_text);
                for (int index = 0; index < _outlines.Length; index++)
                    RefreshFont(_outlines[index]);
                if (_secondText == null) return;
                RefreshFont(_secondText);
                for (int index = 0; index < _secondOutlines.Length; index++)
                    RefreshFont(_secondOutlines[index]);
            }

            internal static void ResetMapFont()
            {
                _mapFont = null;
                _mapFontResolved = false;
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
                int outlinePassCount = HierarchicalVassalMapModeRules.
                    GetLabelOutlinePassCount(pCountry);
                for (int index = 0; index < pOutlines.Length; index++)
                {
                    TextMesh outline = pOutlines[index];
                    if (index >= outlinePassCount)
                    {
                        outline.gameObject.SetActive(false);
                        continue;
                    }
                    RefreshFont(outline);
                    outline.gameObject.SetActive(true);
                    outline.text = pValue;
                    outline.characterSize = renderedCharacterSize;
                    outline.fontStyle = pText.fontStyle;
                    outline.color = pOutlineColor;
                    float angle = index * Mathf.PI * 2f /
                        Math.Max(1, outlinePassCount);
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

            private void ApplySortingLayer(bool pCountry, bool pMinimap)
            {
                int layerId = pCountry
                    ? SortingLayer.NameToID(
                        HierarchicalVassalMapModeRules.
                            ResolveCountryLabelSortingLayer(pMinimap))
                    : SortingLayer.NameToID("MapOverlay");
                int outlineOrder = pCountry
                    ? HierarchicalVassalMapModeRules.
                        ResolveCountryLabelSortingOrder(pMinimap)
                    : CityOutlineSortingOrder;
                int textOrder = pCountry
                    ? HierarchicalVassalMapModeRules.
                        ResolveCountryLabelSortingOrder(pMinimap) + 1
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
                        ? HierarchicalVassalMapModeRules.
                            ResolveCountryLabelSortingOrder(pMinimap) - 1
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
                if (_background != null)
                    _background.enabled = pActive && !_country;
            }

            internal void Destroy()
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
            }

            private void ApplyBackground(Color pColor, Color pOutlineColor,
                bool pCountry, float pSize, string pValue)
            {
                if (_background == null) return;
                // Country names are intentionally integrated directly into
                // the terrain. City names get a small readability plate.
                _background.enabled = !pCountry;
                if (pCountry) return;
                bool isCapital = _city != null &&
                                 _city.kingdom?.capital == _city;
                Color backgroundColor;
                if (isCapital)
                {
                    backgroundColor = new Color(1f, 0.95f, 0.5f, 0.35f);
                }
                else
                {
                    backgroundColor = Color.Lerp(pOutlineColor,
                        Color.black, 0.7f);
                    backgroundColor.a = 0.2f;
                }
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
                if (HierarchicalVassalMapFontSettings.UseBundledFont)
                {
                    Font bundled = HierarchicalVassalMapFontLoader.TryLoad(16);
                    if (bundled != null) return _mapFont = bundled;
                }
                Font selected = HierarchicalVassalMapFontSettings.
                    TryCreateSelectedFont(16);
                if (selected != null) return _mapFont = selected;
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
