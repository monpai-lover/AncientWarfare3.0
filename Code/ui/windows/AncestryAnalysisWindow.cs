using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    internal class AncestryAnalysisWindow : AbstractListWindow<AncestryAnalysisWindow, AncestryRow>
    {
        private static long _actorId = -1;

        public static void Open(long pActorId)
        {
            _actorId = pActorId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.ANCESTRY);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.ANCESTRY,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            ClearList();
            if (_actorId < 0) return;

            AncestryReport report = AncestryAnalysisService.BuildReport(_actorId);
            AddHeader(report.actor_name);
            AddLine(AW_L10n.Text("aw_ancestry_identity", "\u8EAB\u4EFD") + ": " + IdentityLabel(report.identity));

            if (report.noble_blood.has_noble_blood)
            {
                string origin = string.IsNullOrEmpty(report.noble_blood.origin_name)
                    ? "#" + report.noble_blood.origin_actor_id
                    : report.noble_blood.origin_name;
                AddLine(AW_L10n.Text("aw_ancestry_noble_blood", "\u8D35\u65CF\u8840\u8109") + ": " +
                        origin + " +" + report.noble_blood.distance);
            }
            else
            {
                AddLine(AW_L10n.Text("aw_ancestry_no_noble_blood", "\u672A\u53D1\u73B0\u8D35\u65CF\u8840\u8109"),
                    pDim: true);
            }

            AddLine(AW_L10n.Text("aw_ancestry_depth", "\u8FFD\u6EAF\u4EE3\u6570") + ": " + report.max_depth);
            AddLine(AW_L10n.Text("aw_ancestry_known", "\u5DF2\u8BC6\u522B\u7956\u5148") + ": " +
                    report.known_ancestors);
            AddLine(AW_L10n.Text("aw_ancestry_unknown", "\u672A\u77E5\u7956\u6E90") + ": " +
                    report.unknown_percent.ToString("0.0") + "%");

            AddHeader(AW_L10n.Text("aw_ancestry_genetic_section", "\u9057\u4F20\u4E9A\u79CD\u7956\u6E90"));
            AddLine(AW_L10n.Text("aw_ancestry_autosomal", "\u5E38\u67D3\u8272\u4F53\u7956\u6E90") + ": " +
                    report.autosomal_summary);
            AddLine(AW_L10n.Text("aw_ancestry_paternal_marker", "\u7236\u7CFB\u6807\u8BB0") + ": " +
                    FormatMarker(report.paternal_marker));
            AddLine(AW_L10n.Text("aw_ancestry_maternal_marker", "\u6BCD\u7CFB\u6807\u8BB0") + ": " +
                    FormatMarker(report.maternal_marker));
            AddContributionRows(report.genetic_contributions);

            AddHeader(AW_L10n.Text("aw_ancestry_social_section", "\u793E\u4F1A\u8C31\u7CFB\u7956\u6E90"));
            AddContributionRows(report.contributions);
        }

        private void AddContributionRows(System.Collections.Generic.List<AncestryContribution> pRows)
        {
            if (pRows == null || pRows.Count == 0)
            {
                AddLine(AW_L10n.Text("aw_ancestry_no_traceable", "\u6682\u65E0\u53EF\u8FFD\u6EAF\u7956\u6E90"),
                    pDim: true);
                return;
            }

            foreach (AncestryContribution c in pRows)
            {
                string text = c.label + "  " + c.percent.ToString("0.0") + "%";
                if (!string.IsNullOrEmpty(c.source_actor_name))
                    text += "  " + c.source_actor_name;
                AddLine(text, pDim: c.kind == "unknown",
                    pTipTitle: c.label,
                    pTipDesc: BuildContributionTooltip(c));
            }
        }

        private static string BuildContributionTooltip(AncestryContribution pContribution)
        {
            if (pContribution == null) return "";
            string source = pContribution.source_actor_id >= 0
                ? pContribution.source_actor_name + " #" + pContribution.source_actor_id
                : "";
            return AW_L10n.Text("aw_policy_status", "\u72B6\u6001") + ": " + pContribution.kind +
                   "\n" + AW_L10n.Text("aw_ancestry_unknown", "\u672A\u77E5\u7956\u6E90") + ": " +
                   pContribution.percent.ToString("0.0") + "%" +
                   (string.IsNullOrEmpty(source) ? "" : "\n" + source);
        }

        private void AddHeader(string pText)
        {
            AddItemToList(new AncestryRow
            {
                text = pText ?? "",
                is_header = true,
                tooltip_title = pText ?? "",
                tooltip_desc = ""
            });
        }

        private void AddLine(string pText, bool pDim = false, string pTipTitle = "", string pTipDesc = "")
        {
            AddItemToList(new AncestryRow
            {
                text = pText ?? "",
                dim = pDim,
                tooltip_title = pTipTitle ?? "",
                tooltip_desc = pTipDesc ?? ""
            });
        }

        protected override AbstractListWindowItem<AncestryRow> CreateItemPrefab()
        {
            var obj = new GameObject("AncestryListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<AncestryListItem>();
            obj.SetActive(false);
            return item;
        }

        private static string FormatMarker(AncestryMarker pMarker)
        {
            if (pMarker == null || !pMarker.known)
                return AW_L10n.Text("aw_ancestry_marker_unknown", "\u672A\u77E5");

            string source = string.IsNullOrEmpty(pMarker.source_actor_name)
                ? "#" + pMarker.source_actor_id
                : pMarker.source_actor_name;
            return pMarker.label + " (" + source + " +" + pMarker.distance + ")";
        }

        private static string IdentityLabel(string pIdentity)
        {
            switch (pIdentity)
            {
                case LineageStatus.NOBLE: return AW_L10n.Text("aw_role_noble", "\u8D35\u65CF");
                case LineageStatus.COMMON: return AW_L10n.Text("aw_role_common_lineage", "\u6709\u6C0F\u5E73\u6C11");
                case LineageStatus.SLAVE: return AW_L10n.Text("aw_role_slave", "\u5974\u96B6");
                default: return AW_L10n.Text("aw_role_common", "\u5E73\u6C11");
            }
        }
    }
}
