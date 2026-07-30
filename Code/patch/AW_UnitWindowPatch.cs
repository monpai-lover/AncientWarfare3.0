using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;
using AncientWarfare3.ui.windows;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     Injects AW social identity and lineage rows into the unit stats window.
    ///     Identity is independent from lineage: slaves can exist without LINEAGE_ID.
    /// </summary>
    [HarmonyPatch]
    public static class AW_UnitWindowPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(UnitWindow), nameof(UnitWindow.showStatsRows))]
        public static void ShowStatsRows_Postfix(UnitWindow __instance)
        {
            ArmyRtsOperationRowRefreshAdapter.Clear(__instance);
            var actor = __instance.actor;
            if (actor == null || actor.data == null) return;
            string appellation = "";
            Kingdom actorKingdom = actor.kingdom;
            if (actorKingdom?.data != null && actorKingdom.king == actor)
                appellation = RulerAppellationService.GetFullLivingAppellation(
                    actorKingdom);
            else
                appellation = RulerAppellationService.GetPosthumousAppellation(
                    actor.data.id);
            if (!string.IsNullOrEmpty(appellation))
                ShowRawRow(__instance, "aw_ruler_appellation", appellation);
            if (actorKingdom?.king != actor)
            {
                string dynasticTitle =
                    DynasticTitleService.ResolveLivingTitle(actor);
                if (!string.IsNullOrEmpty(dynasticTitle))
                    ShowRawRow(__instance, "aw_social_title_label",
                        dynasticTitle);
            }
            ShowOfficialCareerRow(__instance, actor);
            ShowArmyRtsOperationRow(__instance, actor);
            if (RoyalAsylumService.IsActive(actor))
            {
                City hostCity = RoyalAsylumService.ResolveHostCity(actor);
                string hostName = hostCity?.data?.name;
                if (string.IsNullOrWhiteSpace(hostName))
                    hostName = AW_L10n.Text("aw_unknown_city", "Unknown city");
                ShowRawRow(__instance, "aw_royal_asylum_host", hostName);
            }
            if (!LineageService.IsXia(actor)) return;

            actor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            if (SlaveService.IsSlave(actor))
                status = LineageStatus.SLAVE;

            actor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            bool hasLineage = lineageId >= 0;
            if (!hasLineage && status == LineageStatus.NONE) return;

            actor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            actor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);

            ShowRawRow(__instance, "aw_identity", IdentityText(status));

            if (HistoricalSchoolDescentService.IsCanonicalMaster(actor))
            {
                HistoricalSchoolMasterDefinition master =
                    HistoricalSchoolDescentService.DefinitionFor(actor);
                if (master != null)
                {
                    string familyValue = master.FamilyEvidence ==
                        HistoricalMasterFamilyEvidence.Unknown
                            ? AW_L10n.Text("aw_family_name_unknown", "Unknown")
                            : master.CanonicalFamilyName;
                    KeyValueField familyRow = ShowRawRow(__instance, "aw_family_name",
                        familyValue);
                    if (familyRow != null && master.FamilyEvidence !=
                        HistoricalMasterFamilyEvidence.Unknown)
                    {
                        string knownFamily = master.CanonicalFamilyName;
                        familyRow.on_click_value = () =>
                            ShiBranchListWindow.OpenFor(knownFamily);
                    }
                    if (!string.IsNullOrEmpty(master.CanonicalShiName) && shiId >= 0)
                    {
                        KeyValueField shiRow = ShowRawRow(__instance, "aw_clan_name",
                            master.CanonicalShiName);
                        if (shiRow != null)
                        {
                            long branchId = shiId;
                            shiRow.on_click_value = () =>
                                FamilyTreeWindow.OpenBigTree(branchId);
                        }
                    }
                }
                return;
            }

            bool integrated = IsKingdomIntegrated(actor);

            bool isNoble = status == LineageStatus.NOBLE;
            if (hasLineage && !integrated && isNoble && !string.IsNullOrEmpty(family))
            {
                var kvf = ShowRawRow(__instance, "aw_family_name", family);
                if (kvf != null)
                {
                    string f = family;
                    kvf.on_click_value = () => ShiBranchListWindow.OpenFor(f);
                }
            }

            if (hasLineage && (isNoble || integrated) && !string.IsNullOrEmpty(clan) && shiId >= 0)
            {
                var kvf = ShowRawRow(__instance, "aw_clan_name", clan);
                if (kvf != null)
                {
                    long s = shiId;
                    kvf.on_click_value = () => FamilyTreeWindow.OpenBigTree(s);
                }
            }
        }

        private static KeyValueField ShowRawRow(UnitWindow pWindow, string pId, string pValue)
        {
            return pWindow.showStatRow(pId, pValue);
        }

        private static void ShowArmyRtsOperationRow(UnitWindow pWindow,
            Actor pActor)
        {
            if (pWindow == null ||
                !ArmyRtsOperationRowRefreshAdapter.TryCompose(pActor,
                    out string operation)) return;
            KeyValueField row = ShowRawRow(pWindow,
                "aw_army_rts_operation", operation);
            if (row?.value == null) return;
            row.value.horizontalOverflow = HorizontalWrapMode.Wrap;
            row.value.verticalOverflow = VerticalWrapMode.Overflow;
            row.value.resizeTextForBestFit = true;
            row.value.resizeTextMinSize = 7;
            row.value.resizeTextMaxSize = 9;
            KeyValueField taskRow = pWindow.getStatRow("task");
            ArmyRtsOperationRowRefreshAdapter.Bind(pWindow, pActor, row,
                taskRow);
        }

        private static void ShowOfficialCareerRow(UnitWindow pWindow,
            Actor pActor)
        {
            if (pWindow == null || pActor?.data == null) return;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string officeId,
                "");
            bool activeGeneral = GeneralService.IsActiveGeneralFast(pActor);
            bool usesGeneralFallback = string.IsNullOrWhiteSpace(officeId) &&
                                       activeGeneral;
            officeId = OfficialCareerRankRules.ResolveDisplayedOfficeId(
                officeId, activeGeneral, CourtPyramidRoleId.General);
            if (string.IsNullOrEmpty(officeId)) return;
            Kingdom courtKingdom = ResolveCourtKingdom(pActor);
            if (courtKingdom?.data == null ||
                !CourtService.HasNineRankSystem(courtKingdom)) return;

            int rank = OfficialCareerStateService.ReadRankFast(pActor);
            if (rank <= OfficialCareerRankRules.Unranked) return;
            pActor.data.get(LineageKeys.OFFICER_TRACK, out int track,
                OfficialCareerRankRules.CivilTrack);
            track = OfficialCareerRankRules.ResolveDisplayedTrack(track,
                usesGeneralFallback);
            pActor.data.get(LineageKeys.OFFICER_MERIT, out float merit, 0f);

            string namedRank = AW_L10n.Text(
                OfficialCareerRankRules.NamedRankKey(track, rank),
                OfficialCareerRankRules.NamedRankFallbackEnglish(track, rank));
            string grade = AW_L10n.Text(
                OfficialCareerRankRules.RankNameKey(rank),
                OfficialCareerRankRules.RankFallbackEnglish(rank));
            string office = CourtInstitutionService.OfficeName(courtKingdom,
                officeId);
            if (officeId == CourtOfficeId.Governor &&
                !string.IsNullOrWhiteSpace(pActor.city?.data?.name))
                office = pActor.city.data.name + " " + office;
            string trackTitle = AW_L10n.Text(
                OfficialCareerRankRules.TrackTitleKey(track),
                OfficialCareerRankRules.TrackTitleFallbackEnglish(track));
            string meritTitle = merit > 0f
                ? string.Format(AW_L10n.Text("aw_court_joint_merit",
                    "Merit {0}"), merit.ToString("0.##"))
                : "";
            string nobleTitle = NobleRankService.GetDisplayTitle(pActor);
            string compactTitle = OfficialCareerRankRules.ComposeCareerTitle(
                namedRank, grade, office, compact: true);
            string fullTitle = OfficialCareerRankRules.ComposeCareerTitle(
                namedRank, grade, office, compact: false, track: trackTitle,
                merit: meritTitle, nobleTitle: nobleTitle);

            KeyValueField row = ShowRawRow(pWindow, "aw_court_joint_title",
                compactTitle);
            if (row?.value == null) return;
            float rowHeight = OfficialCareerRankRules.UnitWindowCareerRowHeight();
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect?.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                rowHeight);
            LayoutElement layout = row.GetComponent<LayoutElement>() ??
                                   row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = rowHeight;
            layout.preferredHeight = rowHeight;
            row.value.horizontalOverflow = HorizontalWrapMode.Wrap;
            row.value.verticalOverflow = VerticalWrapMode.Overflow;
            row.value.resizeTextForBestFit = true;
            row.value.resizeTextMinSize =
                OfficialCareerRankRules.UnitWindowCareerMinimumFontSize();
            row.value.resizeTextMaxSize = 9;
            row.on_hover_value = () => Tooltip.show(
                row, AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = AW_L10n.Text("aw_court_joint_title",
                        "Full style"),
                    tip_description = fullTitle
                });
            row.on_hover_value_out = Tooltip.hideTooltip;
        }

        private static Kingdom ResolveCourtKingdom(Actor pActor)
        {
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long courtKingdomId, -1L);
            if (courtKingdomId >= 0)
            {
                try
                {
                    Kingdom courtKingdom = World.world?.kingdoms?.get(
                        courtKingdomId);
                    if (courtKingdom?.data != null) return courtKingdom;
                }
                catch { }
            }
            return pActor.kingdom;
        }

        private static bool IsKingdomIntegrated(Actor pActor)
        {
            var kingdom = pActor.kingdom;
            if (kingdom == null || kingdom.data == null) return false;
            kingdom.data.get(LineageKeys.KINGDOM_INTEGRATED, out bool integrated, false);
            return integrated;
        }

        private static string IdentityText(string pStatus)
        {
            if (pStatus == LineageStatus.NOBLE)
                return AW_L10n.Text("aw_identity_noble", "Noble");
            if (pStatus == LineageStatus.COMMON)
                return AW_L10n.Text("aw_identity_common", "Commoner");
            if (pStatus == LineageStatus.SLAVE)
                return AW_L10n.Text("aw_identity_slave", "Slave");
            return AW_L10n.Text("aw_identity_none", "None");
        }
    }
}
