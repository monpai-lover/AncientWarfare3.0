using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    internal class MandateDecisionWindow : AbstractListWindow<MandateDecisionWindow, WarDecisionTargetRow>
    {
        private static long _kingdomId = -1;
        private bool _commandPending;
        private bool _commandRefreshRequested;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.MANDATE_DECISIONS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.MANDATE_DECISIONS,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
        }

        private void OnDestroy()
        {
            AW3MultiplayerCommandFacade.Changed -= OnCommandStateChanged;
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (!_commandRefreshRequested) return;
            _commandRefreshRequested = false;
            _commandPending = false;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandStateChanged()
        {
            _commandRefreshRequested = true;
        }

        public void Refresh()
        {
            ClearList();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt())
            {
                AddText(AW_L10n.Text("aw_policy_no_kingdom", "\u738B\u56FD\u4E0D\u5B58\u5728"), true, true);
                return;
            }

            AddText(AW_L10n.Text("aw_mandate_decisions_title", "\u5929\u671D\u51B3\u8BAE"),
                true, false,
                AW_L10n.Text("aw_mandate_decisions_title", "\u5929\u671D\u51B3\u8BAE"),
                AW_L10n.Text("aw_mandate_decisions_desc", "\u9009\u62E9\u5929\u547D\u738B\u671D\u72EC\u7ACB\u63A8\u8FDB\u7684\u51B3\u8BAE\u3002"));

            bool? sacrificeQualified = null;
            foreach (MandateDecisionDef def in MandateDecisionService.All)
            {
                if (def.SacrificeLevel.HasValue && !sacrificeQualified.HasValue)
                    sacrificeQualified = MandateSacrificeService.IsQualified(kingdom);
                AddDecisionRow(kingdom, def, sacrificeQualified);
            }
        }

        private void AddDecisionRow(Kingdom pKingdom, MandateDecisionDef pDef,
            bool? pSacrificeQualified)
        {
            bool current = MandateDecisionService.GetCurrent(pKingdom) == pDef.Id;
            bool canRun = MandateDecisionService.CanRun(pKingdom, pDef);
            float progress = current ? MandateDecisionService.GetProgress(pKingdom) : 0f;
            string name = AW_L10n.Text(pDef.NameKey, pDef.FallbackName);
            string desc = AW_L10n.Text(pDef.DescKey, pDef.FallbackDesc);
            string stats = (current
                    ? AW_L10n.Text("aw_policy_current", "\u8FDB\u884C\u4E2D")
                    : (canRun ? AW_L10n.Text("aw_policy_available", "\u53EF\u7814\u53D1") : AW_L10n.Text("aw_policy_locked", "\u672A\u89E3\u9501"))) +
                "  " + AW_L10n.Text("aw_policy_progress", "\u8FDB\u5EA6") + ": " +
                Mathf.FloorToInt(progress) + "/" +
                Mathf.CeilToInt(MandateDecisionService.GetCost(pKingdom, pDef));
            string qualification = "";
            if (pDef.SacrificeLevel.HasValue)
            {
                string qualificationValue = pSacrificeQualified == true
                    ? AW_L10n.Text("aw_mandate_sacrifice_qualified", "\u5408\u683C")
                    : AW_L10n.Text("aw_mandate_sacrifice_unqualified", "\u4E0D\u5408\u683C");
                qualification = "\n" +
                    AW_L10n.Text("aw_mandate_sacrifice_qualification", "\u793C\u5B98\u8D44\u683C") +
                    ": " + qualificationValue;
            }
            string yearlyLabel = pDef.SacrificeLevel.HasValue
                ? AW_L10n.Text("aw_mandate_sacrifice_yearly_spend", "\u672C\u5E74\u6295\u5165")
                : AW_L10n.Text("aw_policy_yearly_gain", "\u5E74\u589E\u957F");

            AddItemToList(new WarDecisionTargetRow
            {
                text = name,
                stats = stats,
                button_text = AW_L10n.Text("aw_mandate_decision_select", "\u9009\u62E9"),
                icon_path = pDef.IconPath,
                enabled = canRun && !_commandPending,
                tooltip_title = name,
                tooltip_desc = desc + "\n" +
                               AW_L10n.Text("aw_policy_cost", "\u9700\u6C42") + ": " +
                               Mathf.CeilToInt(MandateDecisionService.GetCost(pKingdom, pDef)) +
                               "\n" + yearlyLabel + ": " +
                               MandateDecisionService.EstimateYearlyGain(pKingdom, pDef).ToString("0.0") +
                               qualification,
                action = () =>
                {
                    if (_commandPending) return;
                    AW3CommandResult result =
                        AW3MultiplayerCommandFacade.DispatchFromUi(
                        AW3CommandRequest.StartMandateDecision(
                            pKingdom.id, pDef.Id));
                    _commandPending = result.Status ==
                                      AW3CommandStatus.Pending;
                    if (_commandPending)
                    {
                        Refresh();
                        return;
                    }
                    MandateDynastyWindow.Open();
                }
            });
        }

        private void AddText(string pText, bool pHeader, bool pDim, string pTipTitle = "", string pTipDesc = "")
        {
            AddItemToList(new WarDecisionTargetRow
            {
                text = pText ?? "",
                is_header = pHeader,
                dim = pDim,
                enabled = !pDim,
                tooltip_title = pTipTitle ?? "",
                tooltip_desc = pTipDesc ?? ""
            });
        }

        protected override AbstractListWindowItem<WarDecisionTargetRow> CreateItemPrefab()
        {
            var obj = new GameObject("MandateDecisionListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<WarDecisionTargetListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
