using System.Reflection;
using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class VassalNameplateSuzerainFlag : MonoBehaviour
    {
        private static readonly FieldInfo NameTextField =
            AccessTools.Field(typeof(NameplateText), "_text_name");

        private GameObject _root;
        private RectTransform _rect;
        private Image _background;
        private Image _icon;
        private LayoutElement _layout;
        private NameplateText _nameplate;
        private long _shownSuzerainId = -1L;

        public static void Apply(NameplateText pNameplate, Kingdom pKingdom)
        {
            if (pNameplate == null) return;

            Kingdom suzerain = VassalService.GetSuzerain(pKingdom);
            if (!ShouldShow(pNameplate, pKingdom, suzerain))
            {
                Hide(pNameplate);
                return;
            }

            var marker = pNameplate.GetComponent<VassalNameplateSuzerainFlag>();
            if (marker == null) marker = pNameplate.gameObject.AddComponent<VassalNameplateSuzerainFlag>();
            marker.Show(pNameplate, suzerain);
        }

        public static void Hide(NameplateText pNameplate)
        {
            var marker = pNameplate != null ? pNameplate.GetComponent<VassalNameplateSuzerainFlag>() : null;
            if (marker != null) marker.Hide();
        }

        private static bool ShouldShow(NameplateText pNameplate, Kingdom pKingdom, Kingdom pSuzerain)
        {
            return pNameplate != null &&
                   pNameplate.is_full &&
                   pKingdom?.data != null &&
                   pSuzerain?.data != null &&
                   pKingdom != pSuzerain &&
                   !pKingdom.isRekt() &&
                   !pSuzerain.isRekt();
        }

        private void Show(NameplateText pNameplate, Kingdom pSuzerain)
        {
            if (pSuzerain?.data == null)
            {
                Hide();
                return;
            }

            EnsureCreated(pNameplate);
            if (_root == null || _background == null || _icon == null)
            {
                Hide();
                return;
            }

            MoveBeforeNameText();
            if (_shownSuzerainId != pSuzerain.id)
            {
                _shownSuzerainId = pSuzerain.id;
                LoadFlag(pSuzerain);
            }

            if (!_root.activeSelf) _root.SetActive(true);
        }

        private void Hide()
        {
            _shownSuzerainId = -1L;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        private void EnsureCreated(NameplateText pNameplate)
        {
            _nameplate = pNameplate;
            if (_root != null) return;

            Transform parent = pNameplate.layout_group != null
                ? pNameplate.layout_group.transform
                : pNameplate.transform;

            _root = new GameObject("aw_suzerain_flag", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            _root.transform.SetParent(parent, false);
            _root.SetActive(false);

            _rect = _root.GetComponent<RectTransform>();
            float flagSize = VassalNameplateFlagLayoutRules.FlagSize;
            _rect.sizeDelta = new Vector2(flagSize, flagSize);
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0.5f);

            _layout = _root.GetComponent<LayoutElement>();
            _layout.minWidth = flagSize;
            _layout.minHeight = flagSize;
            _layout.preferredWidth = flagSize;
            _layout.preferredHeight = flagSize;
            _layout.flexibleWidth = 0f;
            _layout.flexibleHeight = 0f;

            _background = _root.GetComponent<Image>();
            _background.raycastTarget = false;

            var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(_root.transform, false);
            var iconRect = iconObj.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            float iconInset = VassalNameplateFlagLayoutRules.IconInset;
            iconRect.offsetMin = new Vector2(iconInset, iconInset);
            iconRect.offsetMax = new Vector2(-iconInset, -iconInset);

            _icon = iconObj.GetComponent<Image>();
            _icon.raycastTarget = false;

            MoveBeforeNameText();
        }

        private void MoveBeforeNameText()
        {
            if (_root == null || _nameplate == null) return;

            var text = NameTextField?.GetValue(_nameplate) as Text;
            Transform parent = _root.transform.parent;
            if (text == null || text.transform.parent != parent) return;

            int textIndex = text.transform.GetSiblingIndex();
            int rootIndex = _root.transform.GetSiblingIndex();
            int targetIndex = rootIndex < textIndex ? textIndex - 1 : textIndex;
            if (rootIndex == targetIndex) return;
            _root.transform.SetSiblingIndex(Mathf.Max(0, targetIndex));
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

            if (background == null && icon == null) Hide();
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
