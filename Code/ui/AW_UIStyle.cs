using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui
{
    internal static class AW_UIStyle
    {
        private static Sprite _listRowSprite;
        private static Sprite _buttonSprite;
        private static Sprite _panelSprite;

        public static void ApplyListRow(Image pImage, float pAlpha = 1f)
        {
            if (pImage == null) return;
            pImage.sprite = ListRowSprite();
            pImage.type = Image.Type.Sliced;
            pImage.color = new Color(1f, 1f, 1f, pAlpha);
            pImage.raycastTarget = true;
        }

        public static void ApplyButton(Image pImage, float pAlpha = 1f)
        {
            if (pImage == null) return;
            pImage.sprite = ButtonSprite();
            pImage.type = Image.Type.Sliced;
            pImage.color = new Color(1f, 1f, 1f, pAlpha);
            pImage.raycastTarget = true;
        }

        public static void ApplyPanel(Image pImage, float pAlpha = 1f)
        {
            if (pImage == null) return;
            pImage.sprite = PanelSprite();
            pImage.type = Image.Type.Sliced;
            pImage.color = new Color(1f, 1f, 1f, pAlpha);
            pImage.raycastTarget = true;
        }

        private static Sprite ListRowSprite()
        {
            if (_listRowSprite != null) return _listRowSprite;
            try
            {
                _listRowSprite = Resources.Load<ListWindow>("windows/list_kingdoms")
                    ?._list_element_prefab?.GetComponent<Image>()?.sprite;
            }
            catch { _listRowSprite = null; }
            if (_listRowSprite == null) _listRowSprite = SpriteTextureLoader.getSprite("ui/special/button");
            if (_listRowSprite == null) _listRowSprite = PanelSprite();
            return _listRowSprite;
        }

        private static Sprite ButtonSprite()
        {
            if (_buttonSprite != null) return _buttonSprite;
            _buttonSprite = SpriteTextureLoader.getSprite("ui/special/button");
            if (_buttonSprite == null) _buttonSprite = ListRowSprite();
            return _buttonSprite;
        }

        private static Sprite PanelSprite()
        {
            if (_panelSprite != null) return _panelSprite;
            _panelSprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
            return _panelSprite;
        }
    }
}
