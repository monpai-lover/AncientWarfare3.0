using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class VassalNameplateSuzerainFlag : MonoBehaviour
    {
        private const float MarkerWidth = 20f;
        private static readonly Dictionary<NameplateText, VassalNameplateSuzerainFlag> Instances =
            new Dictionary<NameplateText, VassalNameplateSuzerainFlag>();

        private GameObject _root;
        private Image _background;
        private Image _icon;
        private LayoutElement _layout;
        private GameObject _militaryMarkerRoot;
        private Text _militaryMarker;
        private NameplateText _nameplate;
        private Text _nameText;
        private long _shownSuzerainId = -1L;
        private bool _hasRenderableFlag;
        private bool _showMilitaryMarker;

        public static void Attach(NameplateText pNameplate)
        {
            if (pNameplate == null || Instances.ContainsKey(pNameplate)) return;

            VassalNameplateSuzerainFlag marker =
                pNameplate.gameObject.AddComponent<VassalNameplateSuzerainFlag>();
            marker.Initialize(pNameplate);
            Instances[pNameplate] = marker;
        }

        public static void Apply(NameplateText pNameplate, Kingdom pKingdom)
        {
            if (pNameplate == null ||
                !Instances.TryGetValue(pNameplate, out VassalNameplateSuzerainFlag marker)) return;

            long suzerainId = VassalService.GetSuzerainId(pKingdom);
            Kingdom suzerain = null;
            if (suzerainId >= 0)
            {
                try { suzerain = World.world?.kingdoms?.get(suzerainId); }
                catch { suzerain = null; }
            }

            bool kingdomValid = pKingdom?.data != null && !pKingdom.isRekt();
            bool suzerainValid = suzerain?.data != null && !suzerain.isRekt();
            bool militaryGovernorate = VassalService.GetSubjectKind(pKingdom) ==
                                       VassalSubjectKind.MilitaryGovernorate;
            marker.SetMilitaryMarker(VassalNameplateFlagStateRules.
                ShouldShowMilitaryGovernorateMarker(pNameplate.is_full,
                    kingdomValid, militaryGovernorate));
            VassalNameplateFlagAction action = VassalNameplateFlagStateRules.Resolve(
                pNameplate.is_full, kingdomValid, pKingdom?.id ?? -1L, suzerainId,
                suzerainValid, marker._shownSuzerainId);

            switch (action)
            {
                case VassalNameplateFlagAction.Reload:
                    marker.Reload(suzerain);
                    break;
                case VassalNameplateFlagAction.ShowCached:
                    marker.ShowCached();
                    break;
                default:
                    marker.Hide();
                    break;
            }
        }

        public static void Hide(NameplateText pNameplate)
        {
            if (pNameplate != null &&
                Instances.TryGetValue(pNameplate, out VassalNameplateSuzerainFlag marker))
                marker.Hide();
        }

        private void Initialize(NameplateText pNameplate)
        {
            _nameplate = pNameplate;
            _nameText = FindNameText(pNameplate);
        }

        private void OnDestroy()
        {
            if (_nameplate != null &&
                Instances.TryGetValue(_nameplate, out VassalNameplateSuzerainFlag marker) &&
                ReferenceEquals(marker, this))
                Instances.Remove(_nameplate);
        }

        private void Reload(Kingdom pSuzerain)
        {
            if (pSuzerain?.data == null)
            {
                Hide();
                return;
            }

            EnsureCreated();
            if (_root == null || _background == null || _icon == null)
            {
                Hide();
                return;
            }

            _shownSuzerainId = pSuzerain.id;
            LoadFlag(pSuzerain);
            ShowCached();
        }

        private void ShowCached()
        {
            if (!_hasRenderableFlag && !_showMilitaryMarker || _root == null)
            {
                Hide();
                return;
            }

            if (!_root.activeSelf) _root.SetActive(true);
        }

        private void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        private void EnsureCreated()
        {
            if (_root != null || _nameplate == null) return;

            Transform parent = _nameplate.layout_group != null
                ? _nameplate.layout_group.transform
                : _nameplate.transform;

            _root = new GameObject("aw_suzerain_flag", typeof(RectTransform));
            _root.transform.SetParent(parent, false);
            _root.SetActive(false);

            var rect = (RectTransform)_root.transform;
            float flagSize = VassalNameplateFlagLayoutRules.FlagSize;
            rect.sizeDelta = new Vector2(flagSize, flagSize);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            _layout = _root.AddComponent<LayoutElement>();
            _layout.minWidth = flagSize;
            _layout.minHeight = flagSize;
            _layout.preferredWidth = flagSize;
            _layout.preferredHeight = flagSize;
            _layout.flexibleWidth = 0f;
            _layout.flexibleHeight = 0f;

            var flagObject = new GameObject("Flag", typeof(RectTransform),
                typeof(Image));
            flagObject.transform.SetParent(_root.transform, false);
            var flagRect = (RectTransform)flagObject.transform;
            flagRect.anchorMin = new Vector2(0f, 0.5f);
            flagRect.anchorMax = new Vector2(0f, 0.5f);
            flagRect.pivot = new Vector2(0f, 0.5f);
            flagRect.sizeDelta = new Vector2(flagSize, flagSize);
            flagRect.anchoredPosition = Vector2.zero;

            _background = flagObject.GetComponent<Image>();
            _background.raycastTarget = false;

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(flagObject.transform, false);
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            float iconInset = VassalNameplateFlagLayoutRules.IconInset;
            iconRect.offsetMin = new Vector2(iconInset, iconInset);
            iconRect.offsetMax = new Vector2(-iconInset, -iconInset);

            _icon = iconObject.AddComponent<Image>();
            _icon.raycastTarget = false;

            var markerObject = new GameObject("MilitaryGovernorateMarker",
                typeof(RectTransform), typeof(Image));
            _militaryMarkerRoot = markerObject;
            markerObject.transform.SetParent(_root.transform, false);
            var markerRect = (RectTransform)markerObject.transform;
            markerRect.anchorMin = new Vector2(0f, 0.5f);
            markerRect.anchorMax = new Vector2(0f, 0.5f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.sizeDelta = new Vector2(18f, 11f);
            markerRect.anchoredPosition = new Vector2(flagSize + 2f, 0f);
            Image markerBackground = markerObject.GetComponent<Image>();
            markerBackground.raycastTarget = false;
            markerBackground.color = new Color(0.28f, 0.08f, 0.06f, 0.94f);

            var markerTextObject = new GameObject("Text",
                typeof(RectTransform), typeof(Text));
            markerTextObject.transform.SetParent(markerObject.transform,
                false);
            var markerTextRect = (RectTransform)markerTextObject.transform;
            markerTextRect.anchorMin = Vector2.zero;
            markerTextRect.anchorMax = Vector2.one;
            markerTextRect.offsetMin = Vector2.zero;
            markerTextRect.offsetMax = Vector2.zero;
            _militaryMarker = markerTextObject.GetComponent<Text>();
            _militaryMarker.raycastTarget = false;
            _militaryMarker.font = _nameText != null
                ? _nameText.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
            _militaryMarker.fontStyle = FontStyle.Bold;
            _militaryMarker.fontSize = 9;
            _militaryMarker.resizeTextForBestFit = true;
            _militaryMarker.resizeTextMinSize = 6;
            _militaryMarker.resizeTextMaxSize = 9;
            _militaryMarker.alignment = TextAnchor.MiddleCenter;
            _militaryMarker.color = Color.white;
            _militaryMarker.text = AW_L10n.Text(
                "aw_military_governorate_marker_short", "M");
            var outline = markerTextObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            markerObject.SetActive(_showMilitaryMarker);

            MoveBeforeNameText();
        }

        private void SetMilitaryMarker(bool pShow)
        {
            _showMilitaryMarker = pShow;
            if (pShow) EnsureCreated();
            if (_layout != null)
            {
                float flagSize = VassalNameplateFlagLayoutRules.FlagSize;
                float width = pShow ? flagSize + MarkerWidth : flagSize;
                _layout.minWidth = width;
                _layout.preferredWidth = width;
            }
            if (pShow) RefreshMilitaryMarkerVisual();
            if (_militaryMarkerRoot != null &&
                _militaryMarkerRoot.activeSelf != pShow)
                _militaryMarkerRoot.SetActive(pShow);
        }

        private void RefreshMilitaryMarkerVisual()
        {
            if (_militaryMarker == null) return;
            _militaryMarker.font = _nameText?.font ??
                LocalizedTextManager.current_font ??
                Resources.GetBuiltinResource<Font>("Arial.ttf");
            _militaryMarker.text = AW_L10n.Text(
                "aw_military_governorate_marker_short", "M");
        }

        private void MoveBeforeNameText()
        {
            if (_root == null) return;

            Transform parent = _root.transform.parent;
            if (_nameText != null && _nameText.transform.parent == parent)
            {
                _root.transform.SetSiblingIndex(Mathf.Max(0,
                    _nameText.transform.GetSiblingIndex()));
                return;
            }
            _root.transform.SetAsFirstSibling();
        }

        private static Text FindNameText(NameplateText pNameplate)
        {
            Transform layout = pNameplate?.layout_group?.transform;
            if (layout == null) return null;
            Text[] texts = layout.GetComponentsInChildren<Text>(true);
            for (var index = 0; index < texts.Length; index++)
                if (texts[index] != null &&
                    texts[index].transform.parent == layout)
                    return texts[index];
            return texts.Length > 0 ? texts[0] : null;
        }

        private void LoadFlag(Kingdom pSuzerain)
        {
            Sprite background = null;
            Sprite icon = null;
            ColorAsset color = null;

            try { background = pSuzerain.getElementBackground(); } catch { background = null; }
            try { icon = pSuzerain.getElementIcon(); } catch { icon = null; }
            try { color = GetDirectKingdomColor(pSuzerain); } catch { color = null; }

            _background.enabled = background != null;
            _background.sprite = background;
            if (color != null) _background.color = color.getColorMainSecond();

            _icon.enabled = icon != null;
            _icon.sprite = icon;
            if (color != null) _icon.color = color.getColorBanner();

            _hasRenderableFlag = background != null || icon != null;
        }

        private static ColorAsset GetDirectKingdomColor(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            try
            {
                int colorId = pKingdom.data.color_id;
                if (colorId >= 0)
                    return AssetManager.kingdom_colors_library.getColorByIndex(colorId);
            }
            catch { }

            try { return pKingdom.getColor(); }
            catch { return null; }
        }
    }
}
