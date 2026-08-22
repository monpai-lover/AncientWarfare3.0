using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CourtOfficeHistoryWindow :
        AbstractWindow<CourtOfficeHistoryWindow>
    {
        private const float ContentInsetX = 30f;
        private static readonly Vector2 DefaultSize = new(520f, 340f);
        private static readonly Vector2 MinimumSize = new(420f, 260f);
        private static readonly Vector2 MaximumSize = new(900f, 680f);

        private static long _kingdomId = -1L;
        private static long _cityId = -1L;
        private static string _officeLayer = "";
        private static string _officeId = "";

        private readonly List<CourtOfficeHistoryRow> _rowPool = new();
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private Text _header;
        private Text _empty;
        private WideWindowChrome _chrome;
        private ScrollRect _scrollRect;
        private Scrollbar _scrollbar;
        private RectTransform _historyViewport;
        private RectTransform _historyContent;

        internal static void Open(long pKingdomId, long pCityId,
            string pOfficeLayer, string pOfficeId)
        {
            if (pKingdomId < 0L || string.IsNullOrWhiteSpace(pOfficeLayer) ||
                string.IsNullOrWhiteSpace(pOfficeId)) return;
            _kingdomId = pKingdomId;
            _cityId = pCityId;
            _officeLayer = pOfficeLayer;
            _officeId = pOfficeId;
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.COURT_OFFICE_HISTORY);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.COURT_OFFICE_HISTORY,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize =>
                {
                    _windowSize = pSize;
                    ApplyLayout();
                    Refresh();
                }, DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            var root = new GameObject("CourtOfficeHistoryRoot",
                typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();
            var viewportObject = new GameObject("CourtOfficeHistoryViewport",
                typeof(RectTransform), typeof(Image), typeof(Mask),
                typeof(ScrollRect));
            viewportObject.transform.SetParent(_root, false);
            _historyViewport = viewportObject.GetComponent<RectTransform>();
            viewportObject.GetComponent<Image>().color =
                new Color(.035f, .032f, .027f, .98f);
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;
            var contentObject = new GameObject("CourtOfficeHistoryContent",
                typeof(RectTransform));
            contentObject.transform.SetParent(_historyViewport, false);
            _historyContent = contentObject.GetComponent<RectTransform>();
            _historyContent.anchorMin = _historyContent.anchorMax =
                new Vector2(0f, 1f);
            _historyContent.pivot = new Vector2(0f, 1f);
            _historyContent.anchoredPosition = Vector2.zero;
            _scrollRect = viewportObject.GetComponent<ScrollRect>();
            _scrollRect.viewport = _historyViewport;
            _scrollRect.content = _historyContent;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 28f;
            _scrollbar = CreateScrollbar(_root, _scrollRect);
            _header = CreateText(_historyContent, "Header", 11,
                TextAnchor.MiddleLeft);
            _empty = CreateText(_historyContent, "Empty", 10,
                TextAnchor.MiddleCenter);
            _empty.color = new Color(0.78f, 0.78f, 0.74f, 1f);
        }

        private void Refresh()
        {
            EnsureUi();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            string officeName = kingdom?.data == null
                ? _officeId
                : CourtInstitutionService.OfficeName(kingdom, _officeId);
            string cityName = ResolveCityName(kingdom, _cityId);
            _header.text = officeName +
                           (string.IsNullOrWhiteSpace(cityName)
                               ? ""
                               : "  |  " + cityName);

            var scope = new OfficialCareerHistoryScope(_kingdomId, _cityId,
                _officeLayer, _officeId);
            IReadOnlyList<OfficialCareerHistoryRow> rows =
                OfficialCareerHistoryReadService.Read(scope, 96);
            for (int i = 0; i < rows.Count; i++)
            {
                while (_rowPool.Count <= i)
                    _rowPool.Add(CourtOfficeHistoryRow.Create(_historyContent));
                OfficialCareerHistoryRow row = rows[i];
                string range = OfficialCareerHistoryRules.YearRange(row,
                    AW_L10n.Text("aw_court_history_to_present", "Present"),
                    AW_L10n.Text("aw_court_history_unknown_year", "Unknown"));
                string reason = string.IsNullOrWhiteSpace(row.EndReason)
                    ? AW_L10n.Text("aw_court_history_end_unknown", "Ended")
                    : AW_L10n.Text("aw_court_history_end_" + row.EndReason,
                        row.EndReason);
                _rowPool[i].Bind(row, officeName,
                    string.IsNullOrWhiteSpace(row.CityName)
                        ? cityName
                        : row.CityName,
                    range, reason);
            }
            for (int i = rows.Count; i < _rowPool.Count; i++)
                _rowPool[i].Unbind();
            _empty.gameObject.SetActive(rows.Count == 0);
            _empty.text = AW_L10n.Text("aw_court_history_empty",
                "No recorded incumbents");
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            float contentWidth = Mathf.Max(1f, _windowSize.x - 42f);
            float viewportHeight = Mathf.Max(1f, _windowSize.y - 58f);
            float rowsHeight = 36f + _rowPool.Count *
                CourtOfficeHistoryRow.Height;
            float contentHeight = Mathf.Max(viewportHeight, rowsHeight);
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_court_office_history", "Office History");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            RectTransform viewport = ContentTransform?.parent as RectTransform;
            if (viewport != null)
                viewport.sizeDelta = new Vector2(contentWidth,
                    viewportHeight);
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeRect = nativeScroll?.GetComponent<RectTransform>();
            if (nativeRect != null)
            {
                nativeRect.sizeDelta = new Vector2(contentWidth, viewportHeight);
                nativeRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeComponent = nativeScroll?.GetComponent<ScrollRect>();
            if (nativeComponent != null)
            {
                nativeComponent.horizontal = false;
                nativeComponent.vertical = false;
            }
            Transform nativeBar = BackgroundTransform?.Find(
                "Scroll View/Scrollbar Vertical");
            if (nativeBar != null)
                foreach (Graphic graphic in nativeBar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(contentWidth, viewportHeight);
            Layout(_historyViewport, 6f, 6f,
                Mathf.Max(1f, contentWidth - 22f),
                Mathf.Max(1f, viewportHeight - 12f));
            Layout(_scrollbar.GetComponent<RectTransform>(),
                Mathf.Max(0f, contentWidth - 14f), 6f, 10f,
                Mathf.Max(1f, viewportHeight - 12f));
            _historyContent.sizeDelta = new Vector2(
                Mathf.Max(1f, contentWidth - 22f), contentHeight);
            Layout(_header.rectTransform, 8f, 5f,
                contentWidth - 38f, 24f);
            Layout(_empty.rectTransform, 8f, 42f,
                contentWidth - 38f, 40f);
            for (int i = 0; i < _rowPool.Count; i++)
                _rowPool[i].Layout(34f +
                    i * CourtOfficeHistoryRow.Height, contentWidth - 22f);
            _chrome?.RepositionResizeHandle();
        }

        private static Scrollbar CreateScrollbar(Transform pParent,
            ScrollRect pScroll)
        {
            var barObject = new GameObject("CourtOfficeHistoryScrollbar",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barObject.transform.SetParent(pParent, false);
            Image track = barObject.GetComponent<Image>();
            track.color = new Color(0.08f, 0.075f, 0.065f, 0.98f);
            var slidingObject = new GameObject("Sliding Area",
                typeof(RectTransform));
            slidingObject.transform.SetParent(barObject.transform, false);
            RectTransform sliding = slidingObject.GetComponent<RectTransform>();
            sliding.anchorMin = Vector2.zero;
            sliding.anchorMax = Vector2.one;
            sliding.offsetMin = new Vector2(1f, 1f);
            sliding.offsetMax = new Vector2(-1f, -1f);
            var handleObject = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handleObject.transform.SetParent(sliding, false);
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.82f, 0.68f, 0.28f, 0.95f);
            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = scrollbar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return scrollbar;
        }

        private static string ResolveCityName(Kingdom pKingdom,
            long pCityId)
        {
            if (pKingdom?.data == null || pCityId < 0L) return "";
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data?.id == pCityId)
                        return city.data.name ?? "";
            }
            catch { }
            return "";
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.raycastTarget = false;
            return text;
        }

        private static void Layout(RectTransform pRect, float pX,
            float pY, float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth), pHeight);
        }
    }
}
