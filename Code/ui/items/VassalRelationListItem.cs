using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using AncientWarfare3.ui.windows;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    internal sealed class VassalRelationListItem : AbstractListWindowItem<VassalRelationInfo>
    {
        private const float ROW_W = 260f;

        private Image _flagBg;
        private Image _flagIcon;
        private Text _label;
        private GameObject _absorbObj;
        private TipButton _absorbTip;
        private LayoutElement _layout;
        private TipButton _tip;
        private long _kingdomId = -1;
        private long _contextKingdomId = -1;

        public override void Setup(VassalRelationInfo pObject)
        {
            EnsureUi();
            ApplyLiveKingdomVisuals(pObject);
            _kingdomId = pObject.kingdom_id;
            _contextKingdomId = pObject.context_kingdom_id;
            KingdomFlagBuilder.Build(pObject.banner_id, pObject.banner_icon_id, pObject.banner_background_id,
                pObject.color_text, pObject.color_id, _flagBg, _flagIcon);

            _label.text = BuildText(pObject);
            ColorAsset color = KingdomFlagBuilder.ResolveColor(pObject.color_text, pObject.color_id);
            _label.color = color != null ? color.getColorText() : Color.white;

            var bg = gameObject.GetComponent<Image>();
            if (pObject.is_context) AW_UIStyle.ApplyPanel(bg, 0.95f);
            else AW_UIStyle.ApplyListRow(bg, pObject.is_chain_row ? 0.88f : 0.82f);
            SetTip(pObject);
            SetupAbsorbButton(pObject);
        }

        private void EnsureUi()
        {
            if (_label != null) return;

            var rect = gameObject.GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(ROW_W, 34);

            _layout = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            _layout.minHeight = 34;
            _layout.preferredHeight = 34;

            var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            AW_UIStyle.ApplyListRow(bg, 0.9f);

            var button = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            button.onClick.AddListener(OnClick);

            _tip = gameObject.GetComponent<TipButton>() ?? gameObject.AddComponent<TipButton>();

            var flagObj = new GameObject("Flag", typeof(RectTransform), typeof(Image));
            flagObj.transform.SetParent(transform, false);
            var frect = flagObj.GetComponent<RectTransform>();
            frect.anchorMin = new Vector2(0f, 0.5f);
            frect.anchorMax = new Vector2(0f, 0.5f);
            frect.pivot = new Vector2(0f, 0.5f);
            frect.sizeDelta = new Vector2(24, 24);
            frect.anchoredPosition = new Vector2(5, 0);
            _flagBg = flagObj.GetComponent<Image>();
            _flagBg.preserveAspect = true;

            var iconObj = new GameObject("FlagIcon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(flagObj.transform, false);
            var irect = iconObj.GetComponent<RectTransform>();
            irect.anchorMin = Vector2.zero;
            irect.anchorMax = Vector2.one;
            irect.offsetMin = Vector2.zero;
            irect.offsetMax = Vector2.zero;
            _flagIcon = iconObj.GetComponent<Image>();
            _flagIcon.preserveAspect = true;
            _flagIcon.raycastTarget = false;

            var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(transform, false);
            var trect = textObj.GetComponent<RectTransform>();
            trect.anchorMin = Vector2.zero;
            trect.anchorMax = Vector2.one;
            trect.offsetMin = new Vector2(34, 0);
            trect.offsetMax = new Vector2(-44, 0);
            _label = textObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 10;
            _label.alignment = TextAnchor.MiddleLeft;
            _label.supportRichText = true;
            _label.horizontalOverflow = HorizontalWrapMode.Wrap;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;

            _absorbObj = new GameObject("AbsorbButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
            _absorbObj.transform.SetParent(transform, false);
            var arect = _absorbObj.GetComponent<RectTransform>();
            arect.anchorMin = new Vector2(1f, 0.5f);
            arect.anchorMax = new Vector2(1f, 0.5f);
            arect.pivot = new Vector2(1f, 0.5f);
            arect.sizeDelta = new Vector2(36, 22);
            arect.anchoredPosition = new Vector2(-5, 0);
            AW_UIStyle.ApplyButton(_absorbObj.GetComponent<Image>(), 0.95f);
            _absorbObj.GetComponent<Button>().onClick.AddListener(OnClickAbsorbTarget);
            _absorbTip = _absorbObj.GetComponent<TipButton>();

            var absorbTextObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            absorbTextObj.transform.SetParent(_absorbObj.transform, false);
            var absorbTextRect = absorbTextObj.GetComponent<RectTransform>();
            absorbTextRect.anchorMin = Vector2.zero;
            absorbTextRect.anchorMax = Vector2.one;
            absorbTextRect.offsetMin = Vector2.zero;
            absorbTextRect.offsetMax = Vector2.zero;
            var absorbText = absorbTextObj.GetComponent<Text>();
            absorbText.font = LocalizedTextManager.current_font;
            absorbText.fontSize = 8;
            absorbText.alignment = TextAnchor.MiddleCenter;
            absorbText.color = Color.white;
            absorbText.raycastTarget = false;
            absorbText.text = AW_L10n.Text("aw_vassal_absorb_action", "\u76EE\u6807");
        }

        private static void ApplyLiveKingdomVisuals(VassalRelationInfo pObject)
        {
            Kingdom live = FindKingdom(pObject.kingdom_id);
            if (live?.data == null || live.isRekt()) return;
            pObject.kingdom_name = live.name ?? pObject.kingdom_name;
            pObject.color_text = HistoryColors.FromKingdom(live);
            pObject.color_id = live.data.color_id;
            pObject.banner_icon_id = live.data.banner_icon_id;
            pObject.banner_background_id = live.data.banner_background_id;
            try { pObject.banner_id = live.getActorAsset()?.banner_id ?? pObject.banner_id; }
            catch { }
        }

        private static string BuildText(VassalRelationInfo pObject)
        {
            string indent = new string(' ', Mathf.Clamp(pObject.depth, 0, 4) * 2);
            string name = HistoryText.Colored(Fallback(pObject.kingdom_name), pObject.color_text).Rich;
            string role = "[" + Fallback(pObject.role_label) + "]";
            string metrics = "  " + AW_L10n.Text("aw_city_short", "\u57CE") + pObject.cities +
                             " " + AW_L10n.Text("aw_army_short", "\u519B") + pObject.army;

            if (pObject.years >= 0)
                metrics += " " + AW_L10n.Text("aw_vassal_years_short", "\u81E3") + pObject.years +
                           AW_L10n.Text("aw_year_suffix", "\u5E74");

            if (pObject.total_vassals > 0)
                metrics += " " + AW_L10n.Text("aw_vassal_count_short", "\u9644") + pObject.total_vassals;

            return indent + role + " " + name + metrics;
        }

        private void SetTip(VassalRelationInfo pObject)
        {
            if (_tip == null) return;
            _tip.enabled = true;
            _tip.type = AW_RawTooltip.TYPE;
            string title = Fallback(pObject.kingdom_name);
            string desc = BuildTip(pObject);
            _tip.hoverAction = () =>
                Tooltip.show(gameObject, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = title, tip_description = desc });
        }

        private void SetupAbsorbButton(VassalRelationInfo pObject)
        {
            if (_absorbObj == null) return;
            bool visible = pObject.can_absorb_by_context && pObject.context_kingdom_id >= 0;
            _absorbObj.SetActive(visible);
            if (!visible || _absorbTip == null) return;

            _absorbTip.enabled = true;
            _absorbTip.type = AW_RawTooltip.TYPE;
            string target = Fallback(pObject.kingdom_name);
            _absorbTip.hoverAction = () =>
                Tooltip.show(_absorbObj, AW_RawTooltip.TYPE,
                    new TooltipData
                    {
                        tip_name = AW_L10n.Text("aw_vassal_absorb_action", "\u76EE\u6807"),
                        tip_description = AW_L10n.Text("aw_vassal_absorb_action_desc",
                            "\u9009\u62E9\u4E3A\u541E\u5E76\u9644\u5EB8\u51B3\u7B56\u76EE\u6807\uFF0C\u5B9E\u9645\u541E\u5E76\u5728\u56FD\u5BB6\u51B3\u7B56\u5B8C\u6210\u65F6\u6267\u884C") +
                                          "\n" + DecisionTargetTextRules.TargetLine(target)
                    });
        }

        private static string BuildTip(VassalRelationInfo pObject)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(AW_L10n.Text("aw_vassal_role", "\u8EAB\u4EFD:") + Fallback(pObject.role_label));
            sb.AppendLine(AW_L10n.Text("aw_city_label", "\u57CE\u5E02:") + pObject.cities);
            sb.AppendLine(AW_L10n.Text("aw_army_label", "\u519B\u529B:") + pObject.army);
            sb.AppendLine(AW_L10n.Text("aw_vassal_direct_count", "\u76F4\u5C5E\u9644\u5EB8:") + pObject.direct_vassals);
            sb.AppendLine(AW_L10n.Text("aw_vassal_total_count", "\u9644\u5EB8\u603B\u6570:") + pObject.total_vassals);

            if (pObject.suzerain_id >= 0)
            {
                string subject = string.IsNullOrEmpty(pObject.relation_subject_name)
                    ? Fallback(pObject.kingdom_name)
                    : pObject.relation_subject_name;
                sb.AppendLine(AW_L10n.Text("aw_vassal_relation", "\u81E3\u5C5E:") +
                              subject + " -> " + Fallback(pObject.suzerain_name));
                sb.AppendLine(AW_L10n.Text("aw_vassal_reason", "\u539F\u56E0:") +
                              Fallback(pObject.relation_reason_label));
                sb.AppendLine(AW_L10n.Text("aw_vassal_contract_tier", "\u5951\u7EA6:") +
                              ContractTierLabel(pObject.contract_tier));
                sb.AppendLine(AW_L10n.Text("aw_vassal_started", "\u5F00\u59CB:") + FormatDate(pObject.start_time));
                if (pObject.years >= 0)
                    sb.AppendLine(AW_L10n.Text("aw_vassal_years", "\u81E3\u5C5E\u5E74\u6570:") + pObject.years);
                sb.AppendLine(AW_L10n.Text("aw_vassal_autonomy", "\u81EA\u6CBB\u5EA6:") + pObject.autonomy);
                sb.AppendLine(AW_L10n.Text("aw_vassal_tribute", "\u8D21\u8D4B:") + pObject.tribute_rate);
                sb.AppendLine(AW_L10n.Text("aw_vassal_military", "\u519B\u5F79:") + pObject.military_obligation);
            }

            sb.Append(AW_L10n.Text("aw_click_to_inspect_kingdom", "\u70B9\u51FB\u8DF3\u8F6C\u8BE5\u56FD"));
            return sb.ToString();
        }

        private static string ContractTierLabel(int pTier)
        {
            switch (VassalContractTierRules.NormalizeTier(pTier))
            {
                case VassalContractTierRules.Inner:
                    return AW_L10n.Text("aw_vassal_contract_inner", "\u5185\u85E9");
                case VassalContractTierRules.Jimi:
                    return AW_L10n.Text("aw_vassal_contract_jimi", "\u7F81\u7E3B");
                case VassalContractTierRules.Tributary:
                    return AW_L10n.Text("aw_vassal_contract_tributary", "\u671D\u8D21\u56FD");
                default:
                    return AW_L10n.Text("aw_vassal_contract_outer", "\u5916\u85E9");
            }
        }

        private void OnClick()
        {
            if (_kingdomId < 0) return;
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom != null && !kingdom.isRekt())
                MetaType.Kingdom.getAsset().selectAndInspect(kingdom);
        }

        private void OnClickAbsorbTarget()
        {
            Kingdom context = FindKingdom(_contextKingdomId);
            Kingdom target = FindKingdom(_kingdomId);
            if (context == null || target == null) return;
            if (KingdomPolicyService.StartDecisionWithTarget(context, "aw_decision_absorb_vassal", target))
                VassalRelationWindow.Open(context.id);
        }

        private static Kingdom FindKingdom(long pId)
        {
            if (pId < 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom direct = World.world.kingdoms.get(pId);
                if (direct?.data != null && !direct.isRekt()) return direct;
            }
            catch { }

            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && !kingdom.isRekt() && kingdom.id == pId)
                    return kingdom;
            return null;
        }

        private static string FormatDate(double pTime)
        {
            return pTime > 0 ? HistoryWriter.FormatDate(pTime) : "?";
        }

        private static string Fallback(string pText)
        {
            return string.IsNullOrEmpty(pText) ? "?" : pText;
        }
    }
}
