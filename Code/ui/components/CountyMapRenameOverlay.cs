using AncientWarfare3.core.county;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.windows;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    internal sealed class CountyMapRenameOverlay : MonoBehaviour
    {
        private static CountyMapRenameOverlay _instance;
        private Text _name;
        private Button _rename;
        private TipButton _tip;
        private CanvasGroup _canvasGroup;
        private long _countyId = -1L;
        private long _revision = -1L;

        internal static void Attach(Transform pCanvasRoot)
        {
            if (_instance != null || pCanvasRoot == null) return;
            GameObject obj = new GameObject("CountyMapRenameOverlay",
                typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            obj.transform.SetParent(pCanvasRoot, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -52f);
            rect.sizeDelta = new Vector2(168f, 26f);
            AW_UIStyle.ApplyPanel(obj.GetComponent<Image>(), 0.9f);
            _instance = obj.AddComponent<CountyMapRenameOverlay>();
            _instance._canvasGroup = obj.GetComponent<CanvasGroup>();
            _instance.BuildUi();
            _instance.SetVisible(false);
        }

        private void BuildUi()
        {
            _name = new GameObject("CountyName", typeof(RectTransform),
                typeof(Text)).GetComponent<Text>();
            _name.transform.SetParent(transform, false);
            _name.font = LocalizedTextManager.current_font;
            _name.fontSize = 10;
            _name.alignment = TextAnchor.MiddleCenter;
            _name.color = Color.white;
            _name.raycastTarget = false;
            RectTransform nameRect = _name.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(6f, 2f);
            nameRect.offsetMax = new Vector2(-30f, -2f);

            GameObject button = new GameObject("RenameCounty",
                typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(TipButton));
            button.transform.SetParent(transform, false);
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-3f, 0f);
            buttonRect.sizeDelta = new Vector2(22f, 20f);
            AW_UIStyle.ApplyButton(button.GetComponent<Image>(), 0.96f);
            _rename = button.GetComponent<Button>();
            _rename.onClick.AddListener(OpenRename);
            _tip = button.GetComponent<TipButton>();
            _tip.showOnClick = false;
            _tip.type = AW_RawTooltip.TYPE;
            _tip.hoverAction = () => Tooltip.show(button, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = AW_L10n.Text("aw_county_rename_title",
                        "Rename County"),
                    tip_description = AW_L10n.Text(
                        "aw_county_rename_map_tip",
                        "Rename the selected county")
                });
            Image icon = new GameObject("Icon", typeof(RectTransform),
                typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(button.transform, false);
            icon.rectTransform.anchorMin = Vector2.zero;
            icon.rectTransform.anchorMax = Vector2.one;
            icon.rectTransform.offsetMin = new Vector2(3f, 3f);
            icon.rectTransform.offsetMax = new Vector2(-3f, -3f);
            icon.sprite = SpriteTextureLoader.getSprite("ui/icons/iconEdit") ??
                SpriteTextureLoader.getSprite("ui/icons/iconDocument");
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        private void Update()
        {
            bool visible = ScrollWindow.getCurrentWindow() == null &&
                HierarchicalVassalMapModeService.IsActive() &&
                HierarchicalVassalMapModeService.IsCityCountyLayer;
            if (!visible)
            {
                SetVisible(false);
                return;
            }
            long focused = HierarchicalVassalMapModeService.FocusedCountyId;
            long revision = CountyAdministrationStore.Revision;
            if (_countyId == focused && _revision == revision)
            {
                SetVisible(_countyId >= 0L);
                return;
            }
            CountyRecord county = focused >= 0L
                ? CountyAdministrationStore.FindById(focused)
                : null;
            _revision = revision;
            SetVisible(county != null);
            if (county == null)
            {
                _countyId = -1L;
                return;
            }
            _countyId = county.CountyId;
            _name.text = county.Name ?? string.Empty;
        }

        private void SetVisible(bool pVisible)
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = pVisible ? 1f : 0f;
            _canvasGroup.interactable = pVisible;
            _canvasGroup.blocksRaycasts = pVisible;
        }

        private void OpenRename()
        {
            CountyRecord county = CountyAdministrationStore.FindById(_countyId);
            if (county != null) CountyRenameWindow.Open(county.CountyId);
        }
    }
}
