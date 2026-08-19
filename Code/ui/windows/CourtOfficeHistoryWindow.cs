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
            _header = CreateText(_root, "Header", 11,
                TextAnchor.MiddleLeft);
            _empty = CreateText(_root, "Empty", 10,
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
                    _rowPool.Add(CourtOfficeHistoryRow.Create(_root));
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
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            float rowsHeight = 36f + _rowPool.Count *
                CourtOfficeHistoryRow.Height;
            _root.sizeDelta = new Vector2(contentWidth,
                Mathf.Max(viewportHeight, rowsHeight));
            Layout(_header.rectTransform, 8f, 5f,
                contentWidth - 16f, 24f);
            Layout(_empty.rectTransform, 8f, 42f,
                contentWidth - 16f, 40f);
            for (int i = 0; i < _rowPool.Count; i++)
                _rowPool[i].Layout(34f +
                    i * CourtOfficeHistoryRow.Height, contentWidth);
            _chrome?.RepositionResizeHandle();
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
