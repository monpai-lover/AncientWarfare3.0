using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class MandateCycleWindow : AbstractWindow<MandateCycleWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(580f, 360f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);
        private static readonly MandatePhase[] PhaseOrder =
        {
            MandatePhase.Renewal,
            MandatePhase.Golden,
            MandatePhase.Decline,
            MandatePhase.Chaos
        };

        private readonly List<PhaseRow> _phaseRows = new List<PhaseRow>();
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private RectTransform _viewport;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Scrollbar _scrollbar;
        private Text _summary;
        private Text _metrics;
        private Text _phaseHeader;
        private WideWindowChrome _chrome;

        public static void Open()
        {
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.MANDATE_CYCLE);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.MANDATE_CYCLE,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                size =>
                {
                    _windowSize = size;
                    ApplyLayout();
                },
                DefaultSize, MinimumSize, MaximumSize);
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

            var rootObject = new GameObject("MandateCycleRoot",
                typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();

            var viewportObject = new GameObject("MandateCycleViewport",
                typeof(RectTransform), typeof(Image), typeof(Mask),
                typeof(ScrollRect));
            viewportObject.transform.SetParent(_root, false);
            _viewport = viewportObject.GetComponent<RectTransform>();
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0.055f, 0.052f, 0.045f, 0.96f);
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("MandateCycleContent",
                typeof(RectTransform));
            contentObject.transform.SetParent(_viewport, false);
            _content = contentObject.GetComponent<RectTransform>();
            _content.anchorMin = _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.anchoredPosition = Vector2.zero;

            _scroll = viewportObject.GetComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 22f;

            _scrollbar = CreateScrollbar(_root, _scroll);
            _summary = CreateText(_content, "CycleSummary", 11,
                TextAnchor.UpperLeft);
            _metrics = CreateText(_content, "CycleMetrics", 9,
                TextAnchor.UpperLeft);
            _phaseHeader = CreateText(_content, "CyclePhases", 10,
                TextAnchor.MiddleLeft);
            _phaseHeader.color = new Color(0.93f, 0.78f, 0.42f, 1f);

            for (int i = 0; i < PhaseOrder.Length; i++)
                _phaseRows.Add(CreatePhaseRow(_content, PhaseOrder[i]));
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();

            MandatePhase phase = MandatePhaseService.CurrentPhase;
            int year = Date.getCurrentYear();
            int duration = Mathf.Max(0,
                year - MandatePhaseService.PhaseSinceYear);
            MandateReport report = MandateService.ReadReport();
            MandateRebelReport rebels = MandateRebelService.ReadReport();
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            int unstableFeudatories = CountUnstableFeudatories(mandate);

            _summary.text = AW_L10n.Text("aw_mandate_cycle_current",
                                "Current Phase") + ": " + PhaseText(phase) +
                            "    " +
                            AW_L10n.Text("aw_mandate_cycle_duration",
                                "Duration") + ": " + duration +
                            AW_L10n.Text("aw_mandate_cycle_years", " years");

            string mandateSummary = report.active
                ? AW_L10n.Text("aw_mandate_value", "Mandate") + ": " +
                  report.mandate_value + "    " +
                  AW_L10n.Text("aw_mandate_authority", "Authority") +
                  ": " + report.imperial_authority + "    " +
                  AW_L10n.Text("aw_mandate_core_control", "Core Control") +
                  ": " + Mathf.RoundToInt(report.core_control * 100f) + "%"
                : AW_L10n.Text("aw_mandate_none", "No Mandate dynasty");
            _metrics.text = AW_L10n.Text("aw_mandate_cycle_metrics",
                                    "Realm Conditions") + "\n" +
                            mandateSummary + "\n" +
                            AW_L10n.Text("aw_mandate_catalyst",
                                "Chaos Catalyst") + ": " +
                            MandatePhaseService.CatalystScore + "    " +
                            AW_L10n.Text("aw_mandate_cycle_claimants",
                                "Contenders") + ": " + rebels.active_count +
                            "    " +
                            AW_L10n.Text(
                                "aw_mandate_cycle_unstable_feudatories",
                                "Unstable Feudatories") + ": " +
                            unstableFeudatories;
            _phaseHeader.text = AW_L10n.Text("aw_mandate_cycle_phase_order",
                "Phase Cycle");

            for (int i = 0; i < _phaseRows.Count; i++)
            {
                PhaseRow row = _phaseRows[i];
                bool current = row.Phase == phase;
                row.Background.color = current
                    ? new Color(0.34f, 0.25f, 0.10f, 0.98f)
                    : new Color(0.11f, 0.105f, 0.09f, 0.96f);
                string marker = current
                    ? "[" + AW_L10n.Text(
                        "aw_mandate_cycle_current_marker", "Current") + "] "
                    : "";
                row.Text.text = marker + PhaseText(row.Phase) + "\n" +
                                AW_L10n.Text(EffectKey(row.Phase),
                                    EffectFallback(row.Phase));
                row.Text.color = current
                    ? new Color(1f, 0.90f, 0.58f, 1f)
                    : new Color(0.88f, 0.86f, 0.80f, 1f);
            }
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            float contentWidth = Mathf.Max(1f, _windowSize.x - 42f);
            float contentHeight = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;

            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            RectTransform titleRect = BackgroundTransform?
                .Find("TitleBackground")?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * 0.52f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_mandate_cycle_title", "Dynastic Cycle");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
                window.titleText.raycastTarget = false;
            }

            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect nativeScrollComponent =
                nativeScroll?.GetComponent<ScrollRect>();
            if (nativeScrollComponent != null)
            {
                nativeScrollComponent.horizontal = false;
                nativeScrollComponent.vertical = false;
            }
            Transform nativeScrollbar = BackgroundTransform?.Find(
                "Scroll View/Scrollbar Vertical");
            if (nativeScrollbar != null)
                foreach (Graphic graphic in
                         nativeScrollbar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }

            RectTransform nativeViewport = ContentTransform?.parent as RectTransform;
            if (nativeViewport != null)
                nativeViewport.sizeDelta = new Vector2(contentWidth,
                    contentHeight);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(contentWidth,
                    contentHeight);

            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(contentWidth, contentHeight);

            Layout(_viewport, 6f, 6f, Mathf.Max(1f, contentWidth - 20f),
                Mathf.Max(1f, contentHeight - 12f));
            Layout(_scrollbar.GetComponent<RectTransform>(),
                Mathf.Max(0f, contentWidth - 12f), 6f, 8f,
                Mathf.Max(1f, contentHeight - 12f));

            float innerWidth = Mathf.Max(1f, _viewport.sizeDelta.x - 8f);
            float fixedHeight = 354f;
            _content.sizeDelta = new Vector2(innerWidth,
                Mathf.Max(_viewport.sizeDelta.y, fixedHeight));
            Layout(_summary.rectTransform, 8f, 8f,
                Mathf.Max(1f, innerWidth - 16f), 28f);
            Layout(_metrics.rectTransform, 8f, 40f,
                Mathf.Max(1f, innerWidth - 16f), 64f);
            Layout(_phaseHeader.rectTransform, 8f, 106f,
                Mathf.Max(1f, innerWidth - 16f), 22f);
            for (int i = 0; i < _phaseRows.Count; i++)
                Layout(_phaseRows[i].Rect, 8f, 132f + i * 55f,
                    Mathf.Max(1f, innerWidth - 16f), 49f);
            _chrome?.RepositionResizeHandle();
        }

        private static int CountUnstableFeudatories(Kingdom pMandate)
        {
            if (pMandate?.data == null) return 0;
            IReadOnlyList<FeudatorySnapshot> rows =
                FeudatoryService.GetByKingdom(pMandate.id);
            int count = 0;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Autonomy >= 70 && rows[i].Loyalty <= 30)
                    count++;
            return count;
        }

        private static string PhaseText(MandatePhase pPhase)
        {
            return AW_L10n.Text(MandatePhaseRules.LocalizationKey(pPhase),
                pPhase switch
                {
                    MandatePhase.Renewal => "Territorial Expansion",
                    MandatePhase.Decline => "Political Tension",
                    MandatePhase.Chaos => "Warring Contenders",
                    _ => "Political Clarity"
                });
        }

        private static string EffectKey(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Renewal => "aw_mandate_cycle_effect_renewal",
                MandatePhase.Decline => "aw_mandate_cycle_effect_decline",
                MandatePhase.Chaos => "aw_mandate_cycle_effect_chaos",
                _ => "aw_mandate_cycle_effect_golden"
            };
        }

        private static string EffectFallback(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Renewal =>
                    "Founding expansion: stronger armies and slower occupation.",
                MandatePhase.Decline =>
                    "Political tension: weaker armies and rising local unrest.",
                MandatePhase.Chaos =>
                    "Warring contenders: Mandate contests and restoration unlock.",
                _ => "Political clarity: stable rule and maximum centralization."
            };
        }

        private static PhaseRow CreatePhaseRow(Transform pParent,
            MandatePhase pPhase)
        {
            var rowObject = new GameObject("Phase_" + pPhase,
                typeof(RectTransform), typeof(Image));
            rowObject.transform.SetParent(pParent, false);
            Image background = rowObject.GetComponent<Image>();
            background.color = new Color(0.11f, 0.105f, 0.09f, 0.96f);
            Text text = CreateText(rowObject.transform, "Text", 9,
                TextAnchor.UpperLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(7f, 4f);
            text.rectTransform.offsetMax = new Vector2(-7f, -4f);
            return new PhaseRow(pPhase,
                rowObject.GetComponent<RectTransform>(), background, text);
        }

        private static Scrollbar CreateScrollbar(Transform pParent,
            ScrollRect pScroll)
        {
            var barObject = new GameObject("MandateCycleScrollbar",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barObject.transform.SetParent(pParent, false);
            Image track = barObject.GetComponent<Image>();
            track.color = new Color(0.08f, 0.075f, 0.065f, 0.96f);

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
            handleImage.color = new Color(0.72f, 0.58f, 0.28f, 0.98f);

            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = scrollbar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return scrollbar;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var textObject = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            textObject.transform.SetParent(pParent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
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

        private sealed class PhaseRow
        {
            public PhaseRow(MandatePhase pPhase, RectTransform pRect,
                Image pBackground, Text pText)
            {
                Phase = pPhase;
                Rect = pRect;
                Background = pBackground;
                Text = pText;
            }

            public MandatePhase Phase { get; }
            public RectTransform Rect { get; }
            public Image Background { get; }
            public Text Text { get; }
        }
    }
}
