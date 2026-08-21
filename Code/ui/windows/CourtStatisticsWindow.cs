using AncientWarfare3.core.court;
using AncientWarfare3.ui;
using AncientWarfare3.ui.components;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CourtStatisticsWindow : AbstractWindow<CourtStatisticsWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(470f, 360f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);
        private static long _kingdomId = -1L;
        private static long _cityId = -1L;
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private Text _scope;
        private Text _body;
        private Button _back;
        private WideWindowChrome _chrome;

        internal static void OpenForCourt(long pKingdomId, long pCityId)
        {
            _kingdomId = pKingdomId;
            _cityId = pCityId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.COURT_STATISTICS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.COURT_STATISTICS,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size => { _windowSize = size; ApplyLayout(); },
                DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable() => Refresh();

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            var root = new GameObject("CourtStatisticsRoot", typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();
            _scope = CreateText(_root, "Scope", 11, TextAnchor.UpperLeft);
            _body = CreateText(_root, "Body", 10, TextAnchor.UpperLeft);
            _back = CreateButton(_root, "Back", "aw_back_to_court",
                "返回官场", BackToCourt);
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            CourtStatisticsSnapshot snapshot =
                CourtStatisticsService.BuildForCourt(kingdom, _cityId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                _scope.text = AW_L10n.Text("aw_court_statistics_title",
                    "人口与经济统计");
                _body.text = AW_L10n.Text("aw_court_statistics_unavailable",
                    "统计不可用");
                return;
            }

            _scope.text = kingdom.name + " | " + ScopeName(snapshot.Scope);
            string record = snapshot.HasEconomyRecord
                ? string.Format(AW_L10n.Text("aw_court_statistics_records",
                    "经济记录：{0}/{1}"),
                    snapshot.EconomyRecordCityCount, snapshot.CityCount)
                : AW_L10n.Text("aw_court_statistics_no_record",
                    "暂无年度经济记录");
            string fallback = string.IsNullOrEmpty(snapshot.FallbackReason)
                ? string.Empty
                : "\n" + AW_L10n.Text("aw_court_statistics_fallback",
                    "州范围不可用，当前显示本郡");
            _body.text =
                Metric("aw_court_statistics_population", "人口",
                    snapshot.Population.ToString()) + "\n" +
                Metric("aw_court_statistics_city_count", "城市数",
                    snapshot.CityCount.ToString()) + "\n" +
                Metric("aw_court_statistics_tax", "税值",
                    snapshot.TaxValue.ToString("0.0")) + "\n" +
                Metric("aw_court_statistics_policy", "政策点",
                    snapshot.PolicyPoints.ToString("0.0")) + "\n" +
                Metric("aw_court_statistics_technology", "科技点",
                    snapshot.TechnologyPoints.ToString("0.0")) + "\n" +
                Metric("aw_court_statistics_manpower", "人力",
                    snapshot.Manpower.ToString("0.0")) + "\n" +
                Metric("aw_court_statistics_food", "粮食稳定",
                    snapshot.FoodStability.ToString("0.0")) + "\n" +
                Metric("aw_court_statistics_unrest", "治安风险",
                    snapshot.UnrestRisk.ToString("0.0")) + "\n\n" + record +
                fallback;
        }

        private void BackToCourt()
        {
            if (_cityId >= 0L) CourtWindow.OpenCity(_kingdomId, _cityId);
            else CourtWindow.Open(_kingdomId);
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find("CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_court_statistics_title",
                    "人口与经济统计");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
                window.titleText.raycastTarget = false;
            }
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect = nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(width, height);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect scroll = nativeScroll?.GetComponent<ScrollRect>();
            if (scroll != null) { scroll.horizontal = false; scroll.vertical = false; }
            RectTransform viewport = ContentTransform?.parent as RectTransform;
            if (viewport != null) viewport.sizeDelta = new Vector2(width, height);
            RectTransform content = ContentTransform as RectTransform;
            if (content != null) content.sizeDelta = new Vector2(width, height);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);
            Layout(_scope.rectTransform, 8f, 10f, width - 16f, 28f);
            Layout(_body.rectTransform, 8f, 42f, width - 16f,
                Mathf.Max(40f, height - 84f));
            Layout(_back.GetComponent<RectTransform>(), width - 100f, 8f,
                92f, 22f);
            _chrome?.RepositionResizeHandle();
        }

        private static string ScopeName(CourtStatisticsScope pScope)
        {
            return pScope switch
            {
                CourtStatisticsScope.National => AW_L10n.Text(
                    "aw_court_statistics_national", "Nation"),
                CourtStatisticsScope.Region => AW_L10n.Text(
                    "aw_court_statistics_region", "State"),
                _ => AW_L10n.Text("aw_court_statistics_city", "Prefecture")
            };
        }

        private static string Metric(string pKey, string pFallback,
            string pValue)
        {
            return AW_L10n.Text(pKey, pFallback) + ": " + pValue;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(Transform pParent, string pName,
            string pKey, string pFallback, UnityEngine.Events.UnityAction pAction)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image),
                typeof(Button));
            obj.transform.SetParent(pParent, false);
            obj.GetComponent<Image>().color = new Color(0.22f, 0.2f, 0.16f, 0.95f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(pAction);
            Text text = CreateText(obj.transform, "Text", 9,
                TextAnchor.MiddleCenter);
            text.text = AW_L10n.Text(pKey, pFallback);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }
    }
}
