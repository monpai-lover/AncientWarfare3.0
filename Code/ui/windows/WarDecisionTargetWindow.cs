using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal class WarDecisionTargetWindow : AbstractWindow<WarDecisionTargetWindow>
    {
        private const float WINDOW_W = 460f;
        private const float WINDOW_H = 380f;
        private const float PAD = 14f;
        private const float ROW_H = 48f;
        private const float GAP = 5f;

        private static long _kingdomId = -1;
        private readonly List<GameObject> _created = new List<GameObject>();

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.WAR_TARGETS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.WAR_TARGETS,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
            ConfigureWindow();
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void ConfigureWindow()
        {
            var bgRect = BackgroundTransform.GetComponent<RectTransform>();
            if (bgRect != null) bgRect.sizeDelta = new Vector2(WINDOW_W, WINDOW_H);

            Transform close = BackgroundTransform.parent != null ? BackgroundTransform.parent.Find("CloseBackground") : null;
            if (close != null) close.localPosition = new Vector3(WINDOW_W / 2f - 20f, WINDOW_H / 2f - 12f);

            Transform titleBg = BackgroundTransform.Find("TitleBackground");
            var titleRect = titleBg != null ? titleBg.GetComponent<RectTransform>() : null;
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(WINDOW_W * 0.58f, 30f);
                titleBg.localPosition = new Vector3(0, WINDOW_H / 2f - 16f);
            }

            var sw = GetComponent<ScrollWindow>();
            if (sw?.titleText != null)
            {
                sw.titleText.transform.localPosition = new Vector3(0, WINDOW_H / 2f - 16f);
                sw.titleText.text = AW_L10n.Text("aw_war_targets_title", "战争目标");
                var titleTextRect = sw.titleText.GetComponent<RectTransform>();
                if (titleTextRect != null) titleTextRect.sizeDelta = new Vector2(WINDOW_W * 0.52f, 28f);
            }

            Transform scroll = BackgroundTransform.Find("Scroll View");
            var scrollRect = scroll != null ? scroll.GetComponent<RectTransform>() : null;
            if (scrollRect != null)
            {
                scrollRect.sizeDelta = new Vector2(WINDOW_W - 32f, WINDOW_H - 62f);
                scroll.localPosition = new Vector3(0, -20f, 0);
            }

            Transform viewport = BackgroundTransform.Find("Scroll View/Viewport");
            var viewRect = viewport != null ? viewport.GetComponent<RectTransform>() : null;
            if (viewRect != null) viewRect.sizeDelta = new Vector2(WINDOW_W - 32f, WINDOW_H - 62f);
        }

        private void Refresh()
        {
            ConfigureWindow();
            ClearCreated();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            float rowWidth = WINDOW_W - 56f;
            float y = 8f;

            if (kingdom?.data == null || kingdom.isRekt())
            {
                CreateText("Missing", AW_L10n.Text("aw_policy_no_kingdom", "王国不存在"),
                    TopLeft(PAD, y), new Vector2(rowWidth, 28f), TextAnchor.MiddleCenter, 11, Color.white);
                SetContentHeight(80f);
                return;
            }

            CreateText("Header", kingdom.name + " " + AW_L10n.Text("aw_war_targets_desc", "宣战理由与战争目的"),
                TopLeft(PAD, y), new Vector2(rowWidth, 24f), TextAnchor.MiddleLeft, 11,
                kingdom.getColor().getColorText());
            y += 28f;

            List<WarTerritoryService.TargetReport> reports = WarTerritoryService.BuildTargetReports(kingdom);
            if (reports.Count == 0)
            {
                CreateText("Empty", AW_L10n.Text("aw_war_no_targets", "当前没有可用目标"),
                    TopLeft(PAD, y), new Vector2(rowWidth, 30f), TextAnchor.MiddleCenter, 10, Color.white);
                SetContentHeight(y + 42f);
                return;
            }

            foreach (WarTerritoryService.TargetReport report in reports)
            {
                BuildTargetRow(kingdom, report, y, rowWidth);
                y += ROW_H + GAP;
            }
            SetContentHeight(y + 12f);
        }

        private void BuildTargetRow(Kingdom pSource, WarTerritoryService.TargetReport pReport, float pY, float pWidth)
        {
            Kingdom target = pReport.target;
            var row = new GameObject("WarTarget_" + (target?.id ?? -1), typeof(RectTransform), typeof(Image), typeof(TipButton));
            row.transform.SetParent(ContentTransform, false);
            _created.Add(row);

            var rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(pWidth, ROW_H);
            rect.anchoredPosition = TopLeft(PAD, pY);

            AW_UIStyle.ApplyPanel(row.GetComponent<Image>(), 0.92f);
            SetTip(row, target?.name ?? "", WarTerritoryService.BuildTargetTooltip(pSource, pReport));

            CreateTextObject("Name", row.transform, target?.name ?? "?",
                new Vector2(8f, 23f), new Vector2(128f, 0f), TextAnchor.UpperLeft, 10,
                target?.getColor().getColorText() ?? Color.white);
            CreateTextObject("Stats", row.transform,
                "核" + pReport.core_count + " 强" + pReport.strong_claim_count +
                " 弱" + pReport.weak_claim_count + " 造" + pReport.pending_count,
                new Vector2(8f, 5f), new Vector2(150f, -22f), TextAnchor.LowerLeft, 8, Color.white);

            float x = 156f;
            AddActionButton(row.transform, "Core", "核", x, () =>
            {
                City city = pReport.fabrication_city;
                WarTerritoryService.CreateProject(pSource, target, city, WarTerritoryService.PROJECT_CORE,
                    "reclaim", "fabricate_core", 130.0);
                Refresh();
            }, "制造核心", "对目标城市制造永久核心。");
            x += 34f;
            AddActionButton(row.transform, "WeakClaim", "弱", x, () =>
            {
                City city = pReport.fabrication_city;
                WarTerritoryService.CreateProject(pSource, target, city, WarTerritoryService.PROJECT_WEAK_CLAIM,
                    WarDecisionService.WAR_NORMAL, "weak_claim", 95.0);
                Refresh();
            }, "制造弱宣称", "较快取得宣战理由，颜色为黄色，会过期。");
            x += 34f;
            AddActionButton(row.transform, "StrongClaim", "强", x, () =>
            {
                City city = pReport.fabrication_city;
                WarTerritoryService.CreateProject(pSource, target, city, WarTerritoryService.PROJECT_STRONG_CLAIM,
                    WarDecisionService.WAR_NORMAL, "strong_claim", 160.0);
                Refresh();
            }, "制造强宣称", "较慢取得更强宣称，颜色为绿色。");
            x += 36f;
            ApplyFabricationButtonState(row.transform, pReport);
            AddActionButton(row.transform, "Reclaim", "收", x, () =>
            {
                WarTerritoryService.TryDeclareReclaimWar(pSource, target);
                Refresh();
            }, "收复战争", pReport.can_reclaim ? "收复该国占据的核心城市。" : "没有可收复核心。", pReport.can_reclaim);
            x += 34f;
            AddActionButton(row.transform, "ClaimWar", "战", x, () =>
            {
                WarTerritoryService.TryDeclareClaimWar(pSource, target);
                Refresh();
            }, "按宣称宣战", pReport.can_press_claim ? "按强/弱宣称发动战争。" : "没有完成宣称。", pReport.can_press_claim);
            x += 34f;
            AddActionButton(row.transform, "VassalWar", "臣", x, () =>
            {
                WarTerritoryService.TryDeclareVassalWar(pSource, target);
                Refresh();
            }, "附庸战争", pReport.can_force_vassal ? "迫使目标成为附庸。" : "国力或关系条件不足。", pReport.can_force_vassal);
            x += 34f;
            AddActionButton(row.transform, "NoCb", "强", x, () =>
            {
                WarTerritoryService.TryDeclareNoCbWar(pSource, target);
                Refresh();
            }, "强宣", BuildNoCbTooltip(pSource), pReport.can_no_cb);
        }

        private string BuildNoCbTooltip(Kingdom pSource)
        {
            if (pSource?.data == null) return "无理由宣战。";
            int year = Date.getCurrentYear();
            pSource.data.get("aw_no_cb_penalty_until_year", out int until, -99999);
            if (year < until) return "强宣冷却至 " + until + " 年。";
            return "消耗政治点数并增加合法性、外交和叛乱风险惩罚。";
        }

        private void ApplyFabricationButtonState(Transform pRow, WarTerritoryService.TargetReport pReport)
        {
            if (pRow == null || pReport == null) return;
            ApplySingleFabricationButtonState(pRow.Find("Core"), pReport);
            ApplySingleFabricationButtonState(pRow.Find("WeakClaim"), pReport);
            ApplySingleFabricationButtonState(pRow.Find("StrongClaim"), pReport);
        }

        private void ApplySingleFabricationButtonState(Transform pButtonTransform,
            WarTerritoryService.TargetReport pReport)
        {
            if (pButtonTransform == null) return;
            GameObject obj = pButtonTransform.gameObject;
            bool enabled = pReport.can_fabricate;
            var image = obj.GetComponent<Image>();
            if (image != null) AW_UIStyle.ApplyButton(image, enabled ? 0.95f : 0.48f);
            var button = obj.GetComponent<Button>();
            if (button != null) button.interactable = enabled;
            Text text = obj.transform.Find("Text")?.GetComponent<Text>();
            if (text != null) text.color = enabled ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);
            string target = pReport.fabrication_city?.data?.name ?? "?";
            string desc = enabled
                ? "\u76ee\u6807\u63a5\u58e4\u57ce\u5e02\uff1a" + target
                : WarTerritoryService.FabricationReasonText(pReport.fabrication_reason);
            SetTip(obj, pButtonTransform.name, desc);
        }

        private void AddActionButton(Transform pParent, string pName, string pText, float pX, System.Action pAction,
            string pTipTitle, string pTipDesc, bool pEnabled = true)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(30f, 24f);
            rect.anchoredPosition = new Vector2(pX, 0f);

            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), pEnabled ? 0.95f : 0.48f);
            var button = obj.GetComponent<Button>();
            button.interactable = pEnabled;
            button.onClick.AddListener(() => pAction?.Invoke());

            Text text = CreateTextObject("Text", obj.transform, pText, Vector2.zero, Vector2.zero,
                TextAnchor.MiddleCenter, 9, pEnabled ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f));
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = 9;
            SetTip(obj, pTipTitle, pTipDesc);
        }

        private void CreateText(string pName, string pText, Vector2 pPos, Vector2 pSize,
            TextAnchor pAnchor, int pFontSize, Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(ContentTransform, false);
            _created.Add(obj);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = pSize;
            rect.anchoredPosition = pPos;
            SetupText(obj.GetComponent<Text>(), pText, pAnchor, pFontSize, pColor);
        }

        private Text CreateTextObject(string pName, Transform pParent, string pText, Vector2 pOffsetMin,
            Vector2 pOffsetMax, TextAnchor pAnchor, int pFontSize, Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = pOffsetMin;
            rect.offsetMax = pOffsetMax;
            Text text = obj.GetComponent<Text>();
            SetupText(text, pText, pAnchor, pFontSize, pColor);
            text.raycastTarget = false;
            return text;
        }

        private static void SetupText(Text pText, string pValue, TextAnchor pAnchor, int pFontSize, Color pColor)
        {
            pText.text = pValue ?? "";
            pText.font = LocalizedTextManager.current_font;
            pText.fontSize = pFontSize;
            pText.alignment = pAnchor;
            pText.color = pColor;
            pText.horizontalOverflow = HorizontalWrapMode.Wrap;
            pText.verticalOverflow = VerticalWrapMode.Overflow;
            pText.supportRichText = true;
        }

        private static void SetTip(GameObject pOwner, string pTitle, string pDesc)
        {
            var tip = pOwner.GetComponent<TipButton>() ?? pOwner.AddComponent<TipButton>();
            tip.enabled = true;
            tip.type = AW_RawTooltip.TYPE;
            tip.hoverAction = () =>
                Tooltip.show(pOwner, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = pTitle ?? "", tip_description = pDesc ?? "" });
        }

        private void SetContentHeight(float pHeight)
        {
            var contentRect = ContentTransform != null ? ContentTransform.GetComponent<RectTransform>() : null;
            if (contentRect == null) return;
            contentRect.sizeDelta = new Vector2(WINDOW_W - 56f, Mathf.Max(WINDOW_H - 70f, pHeight));
        }

        private void ClearCreated()
        {
            foreach (GameObject obj in _created)
                if (obj != null) Destroy(obj);
            _created.Clear();
        }

        private static Vector2 TopLeft(float pX, float pY)
        {
            return new Vector2(pX, -pY);
        }
    }
}
