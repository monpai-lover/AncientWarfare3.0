using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class CentralPowerWindow : AbstractWindow<CentralPowerWindow>
    {
        private static readonly Vector2 DefaultSize = new Vector2(420f, 280f);
        private static readonly Vector2 MinimumSize = new Vector2(420f, 280f);
        private static readonly Vector2 MaximumSize = new Vector2(900f, 650f);
        private static long _kingdomId = -1L;
        private readonly List<CentralPowerVassalRowView> _rowPool = new();
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private RectTransform _rows;
        private Text _body;
        private Text _status;
        private Button _reform;
        private TipButton _reformTip;
        private int _tab;

        public static void Open(long pKingdomId)
        {
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null || mandate.id != pKingdomId) return;
            _kingdomId = mandate.id;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.CENTRAL_POWER);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.CENTRAL_POWER,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            WideWindowChrome.Attach(BackgroundTransform, () => _windowSize,
                size => { _windowSize = size; ApplyLayout(); },
                DefaultSize, MinimumSize, MaximumSize);
        }

        public override void OnNormalEnable() => Refresh();

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in ContentTransform.GetComponents<LayoutGroup>()) layout.enabled = false;
            var root = new GameObject("CentralPowerRoot", typeof(RectTransform));
            root.transform.SetParent(ContentTransform, false);
            _root = root.GetComponent<RectTransform>();
            string[] keys = { "aw_central_tab_power", "aw_central_tab_rites", "aw_central_tab_vassals" };
            string[] fallback = { "Central Power", "Rites", "Vassals" };
            for (int i = 0; i < 3; i++)
            {
                int tab = i;
                Button button = CreateButton(_root, "Tab" + i, keys[i], fallback[i], () => { _tab = tab; Refresh(); });
                Layout(button.GetComponent<RectTransform>(), 8f + i * 94f, 8f, 88f, 22f);
            }
            _status = CreateText(_root, "Status", 9, TextAnchor.UpperLeft);
            _body = CreateText(_root, "Body", 10, TextAnchor.UpperLeft);
            var rows = new GameObject("VassalRows", typeof(RectTransform));
            rows.transform.SetParent(_root, false);
            _rows = rows.GetComponent<RectTransform>();
            _reform = CreateButton(_root, "Reform", "aw_central_reform", "Reform", Reform);
            _reformTip = _reform.gameObject.AddComponent<TipButton>();
            _reformTip.type = AW_RawTooltip.TYPE;
        }

        private void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            Kingdom kingdom = MandateService.GetCurrentMandateKingdom();
            _kingdomId = kingdom?.id ?? -1L;
            foreach (CentralPowerVassalRowView row in _rowPool) row.gameObject.SetActive(false);
            _rows.gameObject.SetActive(_tab == 2);
            _reform.gameObject.SetActive(_tab == 0);
            if (kingdom?.data == null || kingdom.isRekt()) { _body.text = AW_L10n.Text("aw_central_unavailable", "Unavailable"); return; }
            if (_tab == 0) RenderCentral(kingdom);
            else if (_tab == 1) RenderRites(kingdom);
            else RenderVassals(kingdom);
        }

        private void RenderCentral(Kingdom pKingdom)
        {
            CentralizationSnapshot snapshot = CentralizationService.ReadSnapshot(pKingdom);
            string decisionId = CentralizationRules.DecisionIdForTargetLevel(
                snapshot.next_target_level);
            bool queued = MandateDecisionService.GetCurrent(pKingdom) == decisionId;
            float progress = queued ? MandateDecisionService.GetProgress(pKingdom) : 0f;
            _status.text = pKingdom.name + "  |  " + snapshot.effective_level + "/3";
            _body.text = AW_L10n.Text("aw_central_nominal", "Nominal") + ": " + snapshot.nominal_level +
                         "\n" + AW_L10n.Text("aw_central_phase_cap", "Phase cap") + ": " + snapshot.phase_cap +
                         "\n" + AW_L10n.Text("aw_central_tax_effect", "Tax") + ": x" + snapshot.effects.TaxMultiplier.ToString("0.00") +
                         "\n" + AW_L10n.Text("aw_central_manpower_effect", "Manpower") + ": x" + snapshot.effects.ManpowerMultiplier.ToString("0.00") +
                         "\n" + AW_L10n.Text("aw_central_unrest_effect", "Unrest reduction") + ": " + snapshot.effects.UnrestReduction +
                         "\n" + AW_L10n.Text("aw_central_mandate_progress", "Mandate decision progress") + ": " +
                         progress.ToString("0.0") + "/" + snapshot.reform_cost +
                         (snapshot.can_reform ? "" : "\n" + Reason(snapshot.block_reason));
            _reform.interactable = snapshot.can_reform && !queued;
            string tooltip = AW_L10n.Text("aw_central_reform_cost", "Reform cost") + ": " + snapshot.reform_cost +
                             "\n" + AW_L10n.Text("aw_central_required_tech", "Required technology") + ": " +
                             (string.IsNullOrEmpty(snapshot.required_tech_id) ? "-" : snapshot.required_tech_id) +
                             "\n" + AW_L10n.Text("aw_central_ready_year", "Ready year") + ": " + snapshot.reform_ready_year +
                             "\n" + AW_L10n.Text("aw_central_phase_cap", "Phase cap") + ": " + snapshot.phase_cap +
                             "\n" + AW_L10n.Text("aw_central_mandate_only", "Only the Mandate realm can centralize");
            _reformTip.hoverAction = () => Tooltip.show(_reformTip.gameObject, AW_RawTooltip.TYPE,
                new TooltipData { tip_name = AW_L10n.Text("aw_central_reform", "Reform"), tip_description = tooltip });
        }

        private void RenderRites(Kingdom pKingdom)
        {
            MandateRitesSnapshot rites = MandateRitesService.ReadSnapshot(pKingdom);
            _status.text = AW_L10n.Text("aw_ritual_total", "Ritual completeness") + " " + rites.total_points + "/2";
            _body.text = AW_L10n.Text("aw_ritual_policy_source", "Mandate rites policy") + ": " + rites.policy_points +
                         "\n" + AW_L10n.Text("aw_ritual_capital_temple_source", "Capital ancestral temple") + ": " + rites.temple_points +
                         "\n" + AW_L10n.Text("aw_ritual_sacrifice_source", "Permanent sacrifice points") + ": " + rites.permanent_points;
        }

        private void RenderVassals(Kingdom pKingdom)
        {
            CentralPowerVassalSummary summary = VassalService.ReadCentralPowerSummary(pKingdom);
            _status.text = AW_L10n.Text("aw_central_direct_vassals", "Direct vassals") + ": " + summary.direct_vassal_count;
            _body.text = summary.direct_vassal_count == 0
                ? AW_L10n.Text("aw_central_no_vassals", "No direct vassals")
                : AW_L10n.Text("aw_central_tribute_forecast", "Annual forecast") + ": " +
                  summary.forecast_political_tribute.ToString("0.0") + " / " + summary.forecast_gold_tribute;
            for (int i = 0; i < summary.vassals.Count; i++)
            {
                while (_rowPool.Count <= i) _rowPool.Add(CentralPowerVassalRowView.Create(_rows));
                _rowPool[i].Bind(summary.vassals[i]);
                _rowPool[i].Layout(4f + i * 24f, _windowSize.x - 28f);
                _rowPool[i].gameObject.SetActive(true);
            }
        }

        private void Reform()
        {
            Kingdom kingdom = MandateService.GetCurrentMandateKingdom();
            CentralizationSnapshot snapshot = CentralizationService.ReadSnapshot(kingdom);
            string decisionId = CentralizationRules.DecisionIdForTargetLevel(
                snapshot.next_target_level);
            bool queued = !string.IsNullOrEmpty(decisionId) &&
                          MandateDecisionService.ForceStart(kingdom, decisionId);
            _status.text = queued
                ? AW_L10n.Text("aw_central_reform_queued", "Central reform queued as a Mandate decision")
                : Reason(snapshot.block_reason);
            Refresh();
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            _root.anchorMin = new Vector2(0.5f, 0.5f); _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f); _root.sizeDelta = new Vector2(_windowSize.x - 24f, _windowSize.y - 54f);
            Layout(_status.rectTransform, 8f, 38f, _root.sizeDelta.x - 16f, 22f);
            Layout(_body.rectTransform, 8f, 64f, _root.sizeDelta.x - 16f, _root.sizeDelta.y - 104f);
            Layout(_rows, 0f, 94f, _root.sizeDelta.x, Mathf.Max(20f, _root.sizeDelta.y - 100f));
            Layout(_reform.GetComponent<RectTransform>(), _root.sizeDelta.x - 98f, 8f, 90f, 22f);
        }

        private static string Reason(string pReason) => AW_L10n.Text("aw_central_reason_" + (pReason ?? "unknown"), pReason ?? "Unavailable");
        private static Text CreateText(Transform pParent, string pName, int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>(); text.font = LocalizedTextManager.current_font; text.fontSize = pSize;
            text.alignment = pAnchor; text.color = Color.white; text.horizontalOverflow = HorizontalWrapMode.Wrap; return text;
        }
        private static Button CreateButton(Transform pParent, string pName, string pKey, string pFallback, UnityEngine.Events.UnityAction pAction)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>(); image.color = new Color(0.22f, 0.2f, 0.16f, 0.95f);
            Button button = obj.GetComponent<Button>(); button.onClick.AddListener(pAction);
            Text text = CreateText(obj.transform, "Text", 9, TextAnchor.MiddleCenter); text.text = AW_L10n.Text(pKey, pFallback);
            RectTransform tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            return button;
        }
        private static void Layout(RectTransform pRect, float pX, float pY, float pWidth, float pHeight)
        {
            if (pRect == null) return; pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f); pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY); pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }
    }
}
