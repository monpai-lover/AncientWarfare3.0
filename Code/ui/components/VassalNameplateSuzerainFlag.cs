using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class VassalNameplateSuzerainFlag : MonoBehaviour
    {
        private static readonly Dictionary<NameplateText, VassalNameplateSuzerainFlag> Instances =
            new Dictionary<NameplateText, VassalNameplateSuzerainFlag>();

        private GameObject _root;
        private Image _background;
        private Image _icon;
        private NameplateText _nameplate;
        private Text _nameText;
        private long _shownSuzerainId = -1L;
        private bool _hasRenderableFlag;

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
            if (!_hasRenderableFlag || _root == null)
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

            var layout = _root.AddComponent<LayoutElement>();
            layout.minWidth = flagSize;
            layout.minHeight = flagSize;
            layout.preferredWidth = flagSize;
            layout.preferredHeight = flagSize;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;

            _background = _root.AddComponent<Image>();
            _background.raycastTarget = false;

            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(_root.transform, false);
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            float iconInset = VassalNameplateFlagLayoutRules.IconInset;
            iconRect.offsetMin = new Vector2(iconInset, iconInset);
            iconRect.offsetMax = new Vector2(-iconInset, -iconInset);

            _icon = iconObject.AddComponent<Image>();
            _icon.raycastTarget = false;

            MoveBeforeNameText();
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
