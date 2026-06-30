using AncientWarfare3.core.lineage;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     全王国列表一行:旗帜(存档重建,亡国也能画)+ 国名(国家色)+ 存活/亡国标记。
    ///     点击 → 打开该国朝代分段历史(HistoryListWindow.OpenKingdom)。
    /// </summary>
    internal class KingdomRosterListItem : AbstractListWindowItem<KingdomArchiveInfo>
    {
        private Image _flagBg;
        private Image _flagIcon;
        private Text _label;
        private Button _button;
        private long _kingdomId = -1;

        public override void Setup(KingdomArchiveInfo pObject)
        {
            EnsureUi();
            _kingdomId = pObject.kingdom_id;

            // 旗帜:存档值重建(不引用活 Kingdom,亡国安全)。
            KingdomFlagBuilder.Build(pObject.banner_id, pObject.banner_icon_id, pObject.banner_background_id,
                pObject.color_text, pObject.color_id, _flagBg, _flagIcon);

            // 国名(国家色)+ 亡国标记。
            string mark = pObject.is_alive ? "" : "  [已亡]";
            _label.text = (string.IsNullOrEmpty(pObject.kingdom_name) ? "?" : pObject.kingdom_name) + mark;
            var color = KingdomFlagBuilder.ResolveColor(pObject.color_text, pObject.color_id);
            _label.color = color != null ? color.getColorText() : Color.white;
            if (!pObject.is_alive) _label.color *= new Color(0.7f, 0.7f, 0.7f, 1f); // 亡国名字偏暗
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>();
            if (rect == null) rect = gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220, 30);

            var le = gameObject.GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            le.minHeight = 30; le.preferredHeight = 30;

            var bg = gameObject.GetComponent<Image>();
            if (bg == null) bg = gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/buttonRed");
            bg.type = Image.Type.Sliced;
            bg.color = new Color(1f, 1f, 1f, 0.5f);

            _button = gameObject.GetComponent<Button>();
            if (_button == null) _button = gameObject.AddComponent<Button>();
            _button.onClick.AddListener(OnClick);

            // 左侧旗帜:背景 Image + 其上图标 Image(对齐原版 KingdomBanner 两层)。
            var flagObj = new GameObject("Flag", typeof(RectTransform), typeof(Image));
            flagObj.transform.SetParent(transform, false);
            var frect = flagObj.GetComponent<RectTransform>();
            frect.anchorMin = new Vector2(0f, 0.5f); frect.anchorMax = new Vector2(0f, 0.5f);
            frect.pivot = new Vector2(0f, 0.5f);
            frect.sizeDelta = new Vector2(24, 24); frect.anchoredPosition = new Vector2(4, 0);
            _flagBg = flagObj.GetComponent<Image>();
            _flagBg.preserveAspect = true;

            var iconObj = new GameObject("FlagIcon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(flagObj.transform, false);
            var irect = iconObj.GetComponent<RectTransform>();
            irect.anchorMin = Vector2.zero; irect.anchorMax = Vector2.one;
            irect.offsetMin = Vector2.zero; irect.offsetMax = Vector2.zero;
            _flagIcon = iconObj.GetComponent<Image>();
            _flagIcon.preserveAspect = true;
            _flagIcon.raycastTarget = false;

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero; trect.anchorMax = Vector2.one;
            trect.offsetMin = new Vector2(34, 0); trect.offsetMax = new Vector2(-4, 0);
            _label = textObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 11;
            _label.alignment = TextAnchor.MiddleLeft;
        }

        private void OnClick()
        {
            if (_kingdomId < 0) return;
            windows.HistoryListWindow.OpenKingdom(_kingdomId);
        }
    }
}
