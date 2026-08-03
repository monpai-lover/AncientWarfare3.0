using AncientWarfare3.ui.items;
using NeoModLoader.api;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    /// <summary>AW3 contributor thank-you leaderboard.</summary>
    internal sealed class SupporterLeaderboardWindow :
        AbstractListWindow<SupporterLeaderboardWindow, SupporterLeaderboardEntry>
    {
        private Image _sponsorQr;
        private Texture2D _sponsorTexture;
        private Sprite _sponsorSprite;

        public static void Open()
        {
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.SUPPORTERS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.SUPPORTERS,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            // Use the native list window chrome and scroll behaviour.
            CreateSponsorQr();
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            ClearList();
            foreach (SupporterLeaderboardEntry entry in
                     SupporterLeaderboardData.Read())
                AddItemToList(entry);
        }

        private void CreateSponsorQr()
        {
            if (_sponsorQr != null) return;

            string modFolder = null;
            try
            {
                modFolder = ModClass.Instance?.GetDeclaration()?.FolderPath;
            }
            catch
            {
                // The list remains usable when the optional image is unavailable.
            }

            string path = string.IsNullOrEmpty(modFolder)
                ? null
                : Path.Combine(modFolder, "sponsor_qr.jpg");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                _sponsorTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(_sponsorTexture, bytes, false))
                {
                    Destroy(_sponsorTexture);
                    _sponsorTexture = null;
                    return;
                }

                _sponsorTexture.filterMode = FilterMode.Bilinear;
                _sponsorSprite = Sprite.Create(_sponsorTexture,
                    new Rect(0f, 0f, _sponsorTexture.width, _sponsorTexture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                var imageObject = new GameObject("SponsorQr",
                    typeof(RectTransform), typeof(Image));
                imageObject.transform.SetParent(BackgroundTransform, false);
                RectTransform rect = imageObject.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(168f, 168f);
                rect.anchoredPosition = new Vector2(200f, -34f);
                _sponsorQr = imageObject.GetComponent<Image>();
                _sponsorQr.sprite = _sponsorSprite;
                _sponsorQr.preserveAspect = true;
                _sponsorQr.raycastTarget = false;
            }
            catch
            {
                if (_sponsorTexture != null) Destroy(_sponsorTexture);
                _sponsorTexture = null;
                _sponsorSprite = null;
            }
        }

        protected override AbstractListWindowItem<SupporterLeaderboardEntry>
            CreateItemPrefab()
        {
            var obj = new GameObject("SupporterLeaderboardListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<SupporterLeaderboardListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
