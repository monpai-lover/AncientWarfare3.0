using System.Collections.Generic;
using System.Linq;
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
            Kingdom actorKingdom = actor.kingdom;
            string appellation = CeremonialTitleResolver.Resolve(actor);
            if (!string.IsNullOrWhiteSpace(appellation))
            {
                string kingdomColor = actorKingdom?.getColor()?.color_text ?? "";
                KeyValueField row = __instance.showStatRow(
                    "aw_ruler_appellation", appellation, kingdomColor);
                MoveRowToTop(__instance, row);
            }
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
            if (!LineageService.IsXia(actor) &&
                !LineageService.UsesAwLineageSystem(actor)) return;

            actor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            if (SlaveService.IsSlave(actor))
                status = LineageStatus.SLAVE;
            else if (NobleIdentityService.IsNobleActor(actor))
                status = LineageStatus.NOBLE;

            actor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            bool hasLineage = lineageId >= 0;
            if (!hasLineage && status == LineageStatus.NONE) return;

            actor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            actor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            actor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);

            ShowRawRow(__instance, "aw_identity",
                SocialStandingText(actor, status));

            // 史载真实双亲。放在宗师分支之前 —— 那个分支末尾直接 return,
            // 开国君主与学派宗师都要显示这两行。
            ShowHistoricalParentRows(__instance, actor, shiId);

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

        /// <summary>
        ///     历史人物(开国君主/学派宗师)的史载双亲。只显示确有记载的 —— 诸子与
        ///     多数割据君主的家世不可考,那种情况不占行。点击跳该人物的家族树,
        ///     合成祖先节点就在他上方。
        /// </summary>
        private static void ShowHistoricalParentRows(UnitWindow pWindow,
            Actor pActor, long pShiId)
        {
            if (pWindow == null || pActor?.data == null) return;
            pActor.data.get(LineageKeys.HISTORICAL_FATHER_NAME,
                out string father, "");
            pActor.data.get(LineageKeys.HISTORICAL_MOTHER_NAME,
                out string mother, "");
            if (string.IsNullOrWhiteSpace(father) &&
                string.IsNullOrWhiteSpace(mother)) return;

            long centerId = pActor.data.id;
            long backShiId = pShiId;
            if (!string.IsNullOrWhiteSpace(father))
            {
                KeyValueField row = ShowRawRow(pWindow,
                    "aw_historical_father", father);
                if (row != null)
                    row.on_click_value = () =>
                        FamilyTreeWindow.OpenFamilyTree(centerId, backShiId);
            }
            if (!string.IsNullOrWhiteSpace(mother))
            {
                KeyValueField row = ShowRawRow(pWindow,
                    "aw_historical_mother", mother);
                if (row != null)
                    row.on_click_value = () =>
                        FamilyTreeWindow.OpenFamilyTree(centerId, backShiId);
            }
        }

        private static KeyValueField ShowRawRow(UnitWindow pWindow, string pId, string pValue)
        {
            return pWindow.showStatRow(pId, pValue);
        }

        private static void MoveRowToTop(UnitWindow pWindow, KeyValueField pRow)
        {
            if (pWindow == null || pRow == null) return;
            pRow.transform.SetAsFirstSibling();
            List<KeyValueField> rows = pWindow.stats_rows_container?.stats_rows;
            if (rows == null) return;
            rows.Remove(pRow);
            rows.Insert(0, pRow);
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
            if (string.IsNullOrEmpty(officeId))
            {
                OfficialCareerReadModel last = OfficialCareerService
                    .LoadCareer(pActor.data.id)
                    .FirstOrDefault(c => !c.IsCurrent);
                if (last == null) return;
                string former = AW_L10n.Text("aw_career_former_prefix", "前");
                string name = AW_L10n.Text(
                    CourtInstitutionRules.OfficeLocalizationKey(
                        last.InstitutionAtAppointment, last.OfficeId),
                    last.OfficeId);
                ShowRawRow(pWindow, "aw_former_office_label", former + name);
                return;
            }
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
            string office = ResolveStyleOfficeName(pActor, courtKingdom,
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

        /// <summary>
        ///     结衔里那一段官职名。
        ///
        ///     君主的 officeId 是 <c>CourtPyramidRules.King</c>（"king"），
        ///     那是**朝廷金字塔的层级 id，不是真官职** —— 官职表里没有它的
        ///     定义，本地化里也没有 <c>aw_court_office_king</c>，于是
        ///     <c>OfficeName</c> 原样把 "king" 吐回来，玩家在结衔里直接看到
        ///     一个英文 id。君主这一格应当走礼制称呼。
        /// </summary>
        private static string ResolveStyleOfficeName(Actor pActor,
            Kingdom pCourtKingdom, string pOfficeId)
        {
            if (!string.Equals(pOfficeId, CourtPyramidRoleId.King,
                    System.StringComparison.Ordinal))
                return CourtInstitutionService.OfficeName(pCourtKingdom,
                    pOfficeId);
            string ceremonial = null;
            try { ceremonial = CeremonialTitleResolver.Resolve(pActor); }
            catch { }
            return string.IsNullOrWhiteSpace(ceremonial)
                ? AW_L10n.Text("aw_court_office_king", "King")
                : ceremonial;
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

        /// <summary>
        ///     人物面板的「身份」行。奴籍优先，其余按门第走与科举名单
        ///     同一个判定（<see cref="SocialStandingService"/>）—— 早先这行
        ///     只看 <c>LINEAGE_STATUS</c> 加「有爵位即贵族」，和科举那边对不上，
        ///     而且几乎人人都显示贵族。
        /// </summary>
        private static string SocialStandingText(Actor pActor, string pStatus)
        {
            if (pStatus == LineageStatus.SLAVE)
                return AW_L10n.Text("aw_identity_slave", "Slave");
            switch (SocialStandingService.Resolve(pActor))
            {
                case CivilServiceExamRules.NobleOrigin:
                    return AW_L10n.Text("aw_civil_service_origin_noble",
                        "Noble");
                case CivilServiceExamRules.GentryOrigin:
                    return AW_L10n.Text("aw_civil_service_origin_gentry",
                        "Gentry");
                case CivilServiceExamRules.DeclinedNobleOrigin:
                    return AW_L10n.Text("aw_civil_service_origin_declined",
                        "Declined House");
                default:
                    return AW_L10n.Text("aw_civil_service_origin_commoner",
                        "Commoner");
            }
        }
    }
}
