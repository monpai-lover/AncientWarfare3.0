using System;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.content.figures;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     把游戏钩子里的原始信号转成编年史事件(含防重复 / 仅入谱贵族 判断),
    ///     避免各 patch 文件塞业务逻辑。HistoryWriter 负责落库,本类负责"要不要记 + 记什么"。
    /// </summary>
    public static class ChronicleEvents
    {
        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        // setKing:新王就位 → 国家换君 + 人物成王。新王==旧记录则跳过(用 data 上的标记防同王重复)。
        public static void OnKingChanged(Kingdom pKingdom, Actor pNewKing)
        {
            if (pKingdom?.data == null || pNewKing?.data == null) return;
            if (!KingdomArchiveWriter.IsArchivable(pKingdom)) return;

            // 防重复:记录上次为该国登记的王 id,相同则跳过。
            pKingdom.data.get(LineageKeys.CHRONICLE_LAST_KING_ID, out long lastKingId, -1L);
            if (lastKingId == pNewKing.data.id)
            {
                double retryStart = World.world.getCurWorldTime();
                double persistedRetryStart = -1d;
                string retryError = "";
                bool retrySettled = MandateAccessionCoordinator.TrySettle(
                    () => ReignRecordWriter.TryTransitionReign(
                        pKingdom, pNewKing, retryStart,
                        out persistedRetryStart, out retryError),
                    () => pKingdom.king?.data?.id == pNewKing.data.id,
                    () => MandateService.OnRulerSucceeded(
                        pKingdom, pNewKing),
                    () => ReignRecordWriter.ProjectCurrentReignStart(
                        pKingdom, pNewKing, persistedRetryStart));
                if (!retrySettled && !string.IsNullOrEmpty(retryError))
                    ModClass.LogWarning("Ruler accession retry failed: " +
                                        retryError);
                return;
            }
            pNewKing.data.get(LineageKeys.SHI_ID, out long newShiId, -1L);
            double accessionTime = World.world.getCurWorldTime();
            double persistedStartTime = -1d;
            string transitionError = "";
            bool settled = MandateAccessionCoordinator.TrySettle(
                () => ReignRecordWriter.TryTransitionReign(
                    pKingdom, pNewKing, accessionTime,
                    out persistedStartTime, out transitionError),
                () =>
                {
                    if (!EnsureInitialStateNameForRuler(
                            pKingdom, pNewKing))
                    {
                        WarnStateNameProjection(pKingdom, pNewKing);
                        return false;
                    }
                    DynastyTransitionStatus dynastyStatus =
                        DynastyRecordWriter.TryOnKingChanged(
                            pKingdom, pNewKing);
                    if (!DynastyTransitionRules.TryResolve(dynastyStatus,
                            out _))
                        return false;
                    if (!DynastyRecordWriter.TryReadCurrentDynastyState(
                            pKingdom.id,
                            out DynastyStateNamePersistence.
                                CurrentDynastyState dynastyState,
                            out string dynastyStateError))
                    {
                        ModClass.LogWarning(
                            "Current dynasty state read failed: " +
                            dynastyStateError);
                        return false;
                    }
                    long currentDynastyId = dynastyState.Exists
                        ? dynastyState.DynastyId
                        : -1L;
                    long openReignDynastyId = -1L;
                    if (currentDynastyId >= 0L &&
                        !ReignRecordWriter.TryReadCurrentReignDynasty(
                            pKingdom, pNewKing, out openReignDynastyId,
                            out string dynastyReadError))
                    {
                        ModClass.LogWarning(
                            "Reign dynasty read failed: " +
                            dynastyReadError);
                        return false;
                    }
                    DynastyReignProjectionDisposition dynastyDisposition =
                        DynastyTransitionRules.ResolveReignProjection(
                            dynastyStatus, currentDynastyId,
                            openReignDynastyId);
                    if (dynastyDisposition ==
                        DynastyReignProjectionDisposition.Failure)
                        return false;
                    if (dynastyDisposition ==
                        DynastyReignProjectionDisposition.Reconcile &&
                        !ReignRecordWriter.TryProjectCurrentReignDynasty(
                            pKingdom, pNewKing, currentDynastyId,
                            out string dynastyProjectionError))
                    {
                        ModClass.LogWarning(
                            "Reign dynasty projection failed: " +
                            dynastyProjectionError);
                        return false;
                    }
                    string boundStateName = newShiId >= 0L
                        ? StateNameService.GetBoundStateName(newShiId)
                        : "";
                    bool stateNamePending = StateNameRules.ShouldRetryDynasticStateName(
                            dynastyState.Exists, dynastyState.ShiId,
                            newShiId, dynastyState.StateName,
                            boundStateName,
                            isEmpireRank:
                                KingdomTitleService.IsEmperor(pKingdom),
                            isActiveMandate: MandateService.IsMandateKingdom(pKingdom));
                    bool projected = ProjectDynasticStateNameForRuler(
                        pKingdom, pNewKing, newShiId, boundStateName,
                        stateNamePending);
                    if (!projected)
                        WarnStateNameProjection(pKingdom, pNewKing);
                    return projected;
                },
                () => pKingdom.king?.data?.id == pNewKing.data.id,
                () => MandateService.OnRulerSucceeded(
                    pKingdom, pNewKing),
                () => ReignRecordWriter.ProjectCurrentReignStart(
                    pKingdom, pNewKing, persistedStartTime));
            if (!settled)
            {
                if (!string.IsNullOrEmpty(transitionError))
                    ModClass.LogWarning("Ruler accession persistence failed: " +
                                        transitionError);
                return;
            }
            RecordPreviousKingLostThrone(
                pKingdom, lastKingId, pNewKing.data.id);
            string kingName = pNewKing.getName();

            // 国家·换君
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RULE_CHANGE,
                HistoryText.Actor(pNewKing, kingName) + H("aw_hist_king_ascended"));
            KingdomArchiveWriter.Upsert(pKingdom);

            // 人物·成王(仅入谱贵族)
            if (ChronicleGate.IsNobleActor(pNewKing))
                HistoryWriter.RecordPerson(pNewKing.data.id, pKingdom, kingName, PersonEvent.BECOME_KING,
                    HistoryText.Actor(pNewKing, kingName) + H("aw_hist_person_ascended_prefix") +
                    HistoryText.Kingdom(pKingdom) + H("aw_hist_person_ascended_suffix"),
                    ChronicleCategory.HONOR);

            if (HeirTitleRules.IsImperialOrMandate(pKingdom) &&
                !RepublicGovernmentService.IsRepublic(pKingdom))
                YearNameService.TryStartAccessionEra(pKingdom, pNewKing);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
            RecordAccessionBook(pKingdom, pNewKing);
            FamilyTreeProjectionRevision.Advance(
                FamilyTreeProjectionChange.RulerAccession);
        }

        private static void RecordAccessionBook(Kingdom pKingdom,
            Actor pNewKing)
        {
            bool republic = RepublicGovernmentService.IsRepublic(pKingdom);
            if (!CeremonialHistoryRules.ShouldWriteAccessionBook(republic,
                    pNewKing.isAlive())) return;

            HistoryText text = H("aw_hist_accession_book_prefix") +
                               HistoryText.Actor(pNewKing) +
                               H("aw_hist_accession_book_mid") +
                               HistoryText.Kingdom(pKingdom) +
                               H("aw_hist_accession_book_suffix");
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.ACCESSION_BOOK, text,
                HistoryTarget.Actor(pNewKing));
            HistoryWriter.RecordPerson(pNewKing.data.id, pKingdom,
                pNewKing.getName(), PersonEvent.ACCESSION_BOOK, text,
                ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnFeudatoryEstablished(Kingdom pKingdom,
            Actor pPrince, City pSeat, int pCityCount)
        {
            if (pKingdom?.data == null || pPrince?.data == null ||
                pSeat?.data == null)
                return;
            HistoryText person = HistoryText.Actor(pPrince) +
                                 H("aw_hist_feudatory_became_prince") +
                                 HistoryText.City(pSeat, pKingdom);
            HistoryWriter.RecordPerson(pPrince.data.id, pKingdom,
                pPrince.getName(), "feudatory_established", person,
                ChronicleCategory.HONOR, HistoryTarget.City(pSeat));

            HistoryText kingdom = HistoryText.Kingdom(pKingdom) +
                                  H("aw_hist_feudatory_granted") +
                                  HistoryText.Actor(pPrince) +
                                  H("aw_hist_feudatory_as_prince_at") +
                                  HistoryText.City(pSeat, pKingdom) +
                                  H("aw_hist_feudatory_city_count") +
                                  HistoryText.PlainText(pCityCount.ToString());
            HistoryWriter.RecordKingdom(pKingdom, "feudatory_established",
                kingdom, HistoryTarget.Actor(pPrince));

            HistoryText city = HistoryText.City(pSeat, pKingdom) +
                               H("aw_hist_feudatory_seat_became") +
                               HistoryText.Actor(pPrince) +
                               H("aw_hist_feudatory_seat_suffix");
            HistoryWriter.RecordCity(pSeat, pKingdom,
                "feudatory_established", city, HistoryTarget.Actor(pPrince));
        }

        public static void OnFeudatoryInherited(Kingdom pKingdom,
            Actor pOldPrince, Actor pNewPrince, City pSeat, string pReason)
        {
            if (pKingdom?.data == null || pNewPrince?.data == null ||
                pSeat?.data == null) return;
            HistoryText person = HistoryText.Actor(pNewPrince) +
                                 H("aw_hist_feudatory_inherited_person") +
                                 HistoryText.City(pSeat, pKingdom) +
                                 H("aw_hist_feudatory_inherited_suffix");
            HistoryWriter.RecordPerson(pNewPrince.data.id, pKingdom,
                pNewPrince.getName(), PersonEvent.FEUDATORY_INHERITED, person,
                ChronicleCategory.HONOR, HistoryTarget.City(pSeat));

            HistoryText kingdom = HistoryText.City(pSeat, pKingdom) +
                                  H("aw_hist_feudatory_line_passed");
            if (pOldPrince?.data != null)
                kingdom += HistoryText.Actor(pOldPrince) +
                           H("aw_hist_feudatory_line_to");
            kingdom += HistoryText.Actor(pNewPrince) +
                       H("aw_hist_feudatory_inherited_suffix");
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.FEUDATORY_INHERITED, kingdom,
                HistoryTarget.Actor(pNewPrince));
        }

        public static void OnFeudatoryAbolished(Kingdom pKingdom,
            Actor pLastPrince, City pSeat, string pReason)
        {
            if (pKingdom?.data == null) return;
            HistoryText kingdom = HistoryText.Kingdom(pKingdom) +
                                  H(pReason == "revocation_abolish"
                                      ? "aw_hist_feudatory_revoked_prefix"
                                      : "aw_hist_feudatory_abolished_prefix");
            if (pSeat?.data != null)
                kingdom += HistoryText.City(pSeat, pKingdom);
            kingdom += H(pReason == "revocation_abolish"
                ? "aw_hist_feudatory_revoked_suffix"
                : "aw_hist_feudatory_abolished_suffix");
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.FEUDATORY_ABOLISHED, kingdom,
                pLastPrince?.data != null
                    ? HistoryTarget.Actor(pLastPrince)
                    : HistoryTarget.Kingdom(pKingdom));
            if (pLastPrince?.data != null &&
                pReason == "revocation_abolish")
                HistoryWriter.RecordPerson(pLastPrince.data.id, pKingdom,
                    pLastPrince.getName(), "feudatory_revoked",
                    HistoryText.Actor(pLastPrince) +
                    H("aw_hist_feudatory_prince_revoked"),
                    ChronicleCategory.HONOR,
                    pSeat?.data != null
                        ? HistoryTarget.City(pSeat)
                        : HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnFeudatoryRelocated(Kingdom pKingdom,
            Actor pPrince, City pOldSeat, City pNewSeat, int pCityCount,
            int pIntensity)
        {
            if (pKingdom?.data == null || pPrince?.data == null ||
                pNewSeat?.data == null)
                return;
            HistoryText content = HistoryText.Kingdom(pKingdom) +
                                  H("aw_hist_feudatory_relocated_prefix") +
                                  HistoryText.Actor(pPrince);
            if (pOldSeat?.data != null)
                content += H("aw_hist_feudatory_relocated_from") +
                           HistoryText.City(pOldSeat, pKingdom);
            content += H("aw_hist_feudatory_relocated_to") +
                       HistoryText.City(pNewSeat, pKingdom) +
                       H("aw_hist_feudatory_city_count") +
                       HistoryText.PlainText(pCityCount.ToString()) +
                       H("aw_hist_feudatory_revocation_intensity") +
                       HistoryText.PlainText(pIntensity.ToString());
            HistoryWriter.RecordKingdom(pKingdom, "feudatory_relocated",
                content, HistoryTarget.Actor(pPrince));
            HistoryWriter.RecordPerson(pPrince.data.id, pKingdom,
                pPrince.getName(), "feudatory_relocated",
                HistoryText.Actor(pPrince) +
                H("aw_hist_feudatory_prince_relocated") +
                HistoryText.City(pNewSeat, pKingdom),
                ChronicleCategory.HONOR, HistoryTarget.City(pNewSeat));
        }

        public static void OnFeudatoryCityReclaimed(Kingdom pKingdom,
            Actor pPrince, City pCity, int pIntensity)
        {
            if (pKingdom?.data == null || pCity?.data == null) return;
            HistoryText content = HistoryText.Kingdom(pKingdom) +
                                  H("aw_hist_feudatory_reclaimed_prefix") +
                                  HistoryText.City(pCity, pKingdom) +
                                  H("aw_hist_feudatory_reclaimed_suffix") +
                                  H("aw_hist_feudatory_revocation_intensity") +
                                  HistoryText.PlainText(pIntensity.ToString());
            HistoryWriter.RecordKingdom(pKingdom,
                "feudatory_city_reclaimed", content,
                pPrince?.data != null
                    ? HistoryTarget.Actor(pPrince)
                    : HistoryTarget.City(pCity));
            if (pPrince?.data != null)
                HistoryWriter.RecordPerson(pPrince.data.id, pKingdom,
                    pPrince.getName(), "feudatory_city_reclaimed",
                    HistoryText.Actor(pPrince) +
                    H("aw_hist_feudatory_prince_lost_city") +
                    HistoryText.City(pCity, pKingdom),
                    ChronicleCategory.HONOR, HistoryTarget.City(pCity));
        }

        public static void OnFeudatoryJingnanStarted(Kingdom pKingdom,
            Actor pPrince, City pSeat, int pRisk)
        {
            if (pKingdom?.data == null || pPrince?.data == null) return;
            HistoryText content = HistoryText.Actor(pPrince) +
                                  H("aw_hist_jingnan_started_mid");
            if (pSeat?.data != null)
                content += HistoryText.City(pSeat, pKingdom);
            content += H("aw_hist_jingnan_risk") +
                       HistoryText.PlainText(pRisk.ToString());
            HistoryWriter.RecordKingdom(pKingdom, "jingnan_started",
                content, HistoryTarget.Actor(pPrince));
            HistoryWriter.RecordPerson(pPrince.data.id, pKingdom,
                pPrince.getName(), "jingnan_started", content,
                ChronicleCategory.WAR, HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnFeudatoryJingnanSuppressed(Kingdom pKingdom,
            Actor pPrince, City pSeat)
        {
            if (pKingdom?.data == null || pPrince?.data == null) return;
            HistoryText content = HistoryText.Actor(pPrince) +
                                  H("aw_hist_jingnan_suppressed_mid");
            if (pSeat?.data != null)
                content += HistoryText.City(pSeat, pKingdom);
            HistoryWriter.RecordKingdom(pKingdom, "jingnan_suppressed",
                content, HistoryTarget.Actor(pPrince));
            HistoryWriter.RecordPerson(pPrince.data.id, pKingdom,
                pPrince.getName(), "jingnan_suppressed", content,
                ChronicleCategory.WAR, HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnFeudatoryJingnanVictory(Kingdom pKingdom,
            Actor pPrince)
        {
            if (pKingdom?.data == null || pPrince?.data == null) return;
            HistoryText content = HistoryText.Actor(pPrince) +
                                  H("aw_hist_jingnan_victory_mid") +
                                  HistoryText.Kingdom(pKingdom) +
                                  H("aw_hist_jingnan_victory_suffix");
            HistoryWriter.RecordKingdom(pKingdom, "jingnan_victory",
                content, HistoryTarget.Actor(pPrince));
            HistoryWriter.RecordPerson(pPrince.data.id, pKingdom,
                pPrince.getName(), "jingnan_victory", content,
                ChronicleCategory.WAR, HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnFeudatoryJingnanStalemate(Kingdom pKingdom,
            Kingdom pClaimant, Actor pPrince)
        {
            if (pKingdom?.data == null || pClaimant?.data == null) return;
            HistoryText content = (pPrince?.data != null
                                      ? HistoryText.Actor(pPrince)
                                      : HistoryText.Kingdom(pClaimant)) +
                                  H("aw_hist_jingnan_stalemate_mid") +
                                  HistoryText.Kingdom(pClaimant) +
                                  H("aw_hist_jingnan_stalemate_suffix");
            HistoryWriter.RecordKingdom(pKingdom, "jingnan_stalemate",
                content, HistoryTarget.Kingdom(pClaimant));
            HistoryWriter.RecordKingdom(pClaimant, "jingnan_stalemate",
                content, HistoryTarget.Kingdom(pKingdom));
            if (pPrince?.data != null)
                HistoryWriter.RecordPerson(pPrince.data.id, pClaimant,
                    pPrince.getName(), "jingnan_stalemate", content,
                    ChronicleCategory.WAR, HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnFavorOrderSuccession(Kingdom pKingdom,
            Actor pPrince, City pAffectedCity, FeudatoryFavorAction pAction,
            int pAutonomy)
        {
            if (pKingdom?.data == null || pPrince?.data == null) return;
            HistoryText content = HistoryText.Kingdom(pKingdom) +
                                  H("aw_hist_favor_order_applied");
            if (pAction == FeudatoryFavorAction.ReclaimCity &&
                pAffectedCity?.data != null)
                content += H("aw_hist_favor_order_reclaimed") +
                           HistoryText.City(pAffectedCity, pKingdom) +
                           H("aw_hist_favor_order_reclaimed_suffix");
            else
                content += H("aw_hist_favor_order_autonomy") +
                           HistoryText.PlainText(pAutonomy.ToString());
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.FEUDATORY_FAVOR, content,
                HistoryTarget.Actor(pPrince));
            HistoryWriter.RecordPerson(pPrince.data.id, pKingdom,
                pPrince.getName(), PersonEvent.FEUDATORY_FAVOR,
                HistoryText.Actor(pPrince) +
                H("aw_hist_favor_order_prince_record"),
                ChronicleCategory.HONOR,
                pAffectedCity?.data != null
                    ? HistoryTarget.City(pAffectedCity)
                    : HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnNobleRankGranted(Kingdom pKingdom,
            Actor pGrantor, Actor pRecipient, string pTitle)
        {
            if (pRecipient?.data == null || string.IsNullOrEmpty(pTitle))
                return;
            HistoryText content = H("aw_hist_edict_noble_grant_prefix") +
                                  HistoryText.Actor(pRecipient) +
                                  H("aw_hist_edict_noble_grant_as") +
                                  HistoryText.PlainText(pTitle);
            if (pGrantor?.data != null)
                content += H("aw_hist_edict_noble_grant_by") +
                           HistoryText.Actor(pGrantor);
            content += H("aw_hist_edict_noble_grant_suffix");
            HistoryWriter.RecordPerson(pRecipient.data.id, pKingdom,
                pRecipient.getName(), PersonEvent.NOBLE_RANK_GRANTED, content,
                ChronicleCategory.HONOR,
                pGrantor?.data != null
                    ? HistoryTarget.Actor(pGrantor)
                    : HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCourtSurnameGranted(Kingdom pKingdom,
            Actor pRuler, Actor pRecipient, long pOldShiId, long pRoyalShiId,
            string pOldShiName, string pRoyalShiName)
        {
            if (pKingdom?.data == null || pRuler?.data == null ||
                pRecipient?.data == null || pRoyalShiId < 0) return;
            HistoryText content = H("aw_hist_court_surname_edict") +
                                  HistoryText.Actor(pRecipient) +
                                  H("aw_hist_court_surname_from") +
                                  ShiLabel(pOldShiName) +
                                  H("aw_hist_court_surname_to") +
                                  ShiLabel(pRoyalShiName) +
                                  H("aw_hist_court_surname_by") +
                                  HistoryText.Actor(pRuler) +
                                  H("aw_hist_court_surname_suffix");
            HistoryTarget target = HistoryTarget.From("shi", pRoyalShiId);
            HistoryWriter.RecordPerson(pRecipient.data.id, pKingdom,
                pRecipient.getName(), "court_surname_granted", content,
                ChronicleCategory.HONOR, target);
            HistoryWriter.RecordKingdom(pKingdom,
                "court_surname_granted", content, target);
        }

        public static void OnCourtLineageExpelled(Kingdom pKingdom,
            Actor pRuler, Actor pRecipient, long pOldShiId, long pNewShiId,
            string pOldShiName, string pNewShiName)
        {
            if (pKingdom?.data == null || pRuler?.data == null ||
                pRecipient?.data == null || pNewShiId < 0) return;
            HistoryText content = H("aw_hist_court_expulsion_edict") +
                                  HistoryText.Actor(pRecipient) +
                                  H("aw_hist_court_expulsion_from") +
                                  ShiLabel(pOldShiName) +
                                  H("aw_hist_court_expulsion_to") +
                                  ShiLabel(pNewShiName) +
                                  H("aw_hist_court_expulsion_by") +
                                  HistoryText.Actor(pRuler) +
                                  H("aw_hist_court_expulsion_suffix");
            HistoryTarget target = HistoryTarget.From("shi", pNewShiId);
            HistoryWriter.RecordPerson(pRecipient.data.id, pKingdom,
                pRecipient.getName(), "court_lineage_expelled", content,
                ChronicleCategory.HONOR, target);
            HistoryWriter.RecordKingdom(pKingdom,
                "court_lineage_expelled", content, target);
        }

        private static HistoryText ShiLabel(string pName)
        {
            return HistoryText.PlainText(
                string.IsNullOrWhiteSpace(pName)
                    ? T("aw_hist_unknown_shi")
                    : pName + T("aw_hist_shi_suffix"));
        }

        public static void OnNobleRankInherited(Kingdom pKingdom,
            Actor pPreviousHolder, Actor pSuccessor, string pTitle)
        {
            if (pSuccessor?.data == null || string.IsNullOrEmpty(pTitle))
                return;
            HistoryText content = HistoryText.Actor(pSuccessor) +
                                  H("aw_hist_noble_rank_inherited_title") +
                                  HistoryText.PlainText(pTitle);
            if (pPreviousHolder?.data != null)
                content += H("aw_hist_noble_rank_inherited_from") +
                           HistoryText.Actor(pPreviousHolder);
            HistoryWriter.RecordPerson(pSuccessor.data.id, pKingdom,
                pSuccessor.getName(), PersonEvent.NOBLE_RANK_INHERITED,
                content, ChronicleCategory.HONOR,
                pPreviousHolder?.data != null
                    ? HistoryTarget.Actor(pPreviousHolder)
                    : HistoryTarget.Kingdom(pKingdom));
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.NOBLE_RANK_INHERITED, content,
                HistoryTarget.Actor(pSuccessor));
        }

        public static void OnNobleRankExtinct(Kingdom pKingdom,
            Actor pLastHolder, string pTitle)
        {
            if (pLastHolder?.data == null || string.IsNullOrEmpty(pTitle))
                return;
            HistoryText content = HistoryText.Actor(pLastHolder) +
                                  H("aw_hist_noble_rank_extinct_prefix") +
                                  HistoryText.PlainText(pTitle) +
                                  H("aw_hist_noble_rank_extinct_suffix");
            HistoryWriter.RecordPerson(pLastHolder.data.id, pKingdom,
                pLastHolder.getName(), PersonEvent.NOBLE_RANK_EXTINCT,
                content, ChronicleCategory.HONOR,
                HistoryTarget.Kingdom(pKingdom));
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.NOBLE_RANK_EXTINCT, content,
                HistoryTarget.Actor(pLastHolder));
        }

        public static void OnGreatRoyalGrant(Kingdom pKingdom,
            Actor pEmperor, int pGrantedCount)
        {
            if (pKingdom?.data == null || pGrantedCount <= 0) return;
            HistoryText content = HistoryText.Kingdom(pKingdom) +
                                  H("aw_hist_edict_great_royal_grant_prefix") +
                                  HistoryText.PlainText(
                                      pGrantedCount.ToString()) +
                                  H("aw_hist_edict_great_royal_grant_suffix");
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.GREAT_ROYAL_GRANT, content,
                pEmperor?.data != null
                    ? HistoryTarget.Actor(pEmperor)
                    : HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnNobleRemarried(Kingdom pKingdom,
            Actor pNoble, Actor pSpouse)
        {
            if (pKingdom?.data == null || pNoble?.data == null ||
                pSpouse?.data == null) return;
            HistoryText content = HistoryText.Actor(pNoble) +
                                  H("aw_hist_noble_remarried") +
                                  HistoryText.Actor(pSpouse);
            HistoryWriter.RecordPerson(pNoble.data.id, pKingdom,
                pNoble.getName(), PersonEvent.NOBLE_REMARRIAGE, content,
                ChronicleCategory.BOND,
                HistoryTarget.Actor(pSpouse));
            HistoryWriter.RecordPerson(pSpouse.data.id, pKingdom,
                pSpouse.getName(), PersonEvent.NOBLE_REMARRIAGE, content,
                ChronicleCategory.BOND,
                HistoryTarget.Actor(pNoble));
        }

        private static bool EnsureInitialStateNameForRuler(Kingdom pKingdom,
            Actor pRuler)
        {
            if (!LineageService.IsXiaKingdom(pKingdom) &&
                !XiaizationService.UsesXiaizedInstitutionSystem(pKingdom)) return true;
            pRuler.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (shiId < 0) return true;
            ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(shiId);
            long currentShiId =
                DynastyRecordWriter.GetCurrentDynastyShiId(pKingdom.id);
            string preferredStateName =
                HistoricalFigureService.GetPreferredKingdomName(pRuler);
            bool hasHistoricalPreferredName =
                StateNameRules.IsValid(preferredStateName);
            if (StateNameRules.ShouldSkipInitialStateBinding(
                    currentShiId >= 0, hasHistoricalPreferredName) ||
                !StateNameRules.IsValid(pKingdom.name)) return true;
            StateNameCommitResult initial =
                StateNameService.EnsureBoundStateName(
                    pKingdom, pRuler, shiId, -1L,
                    branch?.origin_kingdom_id ?? pKingdom.id,
                    preferredStateName);
            if (initial.Success && hasHistoricalPreferredName)
            {
                bool projected = StateNameService.ProjectCommittedStateName(pKingdom, initial);
                if (projected)
                    HistoricalFigureService.OnFigureKingBecame(
                        pKingdom, pRuler);
                return projected;
            }
            // Initial accession only gives the Shi a durable state-name binding.
            // Projecting it here bypasses the empire/new-dynasty gate below and
            // can rename an unrelated lower-rank realm to the branch's old state.
            return initial.Success;
        }

        private static bool ProjectDynasticStateNameForRuler(
            Kingdom pKingdom, Actor pRuler, long pShiId,
            string pBoundStateName, bool pStateNamePending)
        {
            if (pShiId < 0 || !pStateNamePending) return true;
            bool projected = StateNameService.ProjectExistingStateName(
                pKingdom, pShiId, pBoundStateName);
            if (!projected) return false;
            bool dynastySynced = DynastyRecordWriter.UpdateCurrentStateName(
                pKingdom.id, pBoundStateName);
            HistoricalFigureService.OnFigureKingBecame(pKingdom, pRuler);
            return dynastySynced;
        }

        private static void WarnStateNameProjection(Kingdom pKingdom,
            Actor pRuler)
        {
            ModClass.LogWarning(
                "State-name projection did not complete for kingdom " +
                pKingdom.id + "; continuing ruler chronicle for actor " +
                pRuler.data.id);
        }

        private static void RecordPreviousKingLostThrone(Kingdom pKingdom, long pPreviousKingId, long pNewKingId)
        {
            Actor previous = pPreviousKingId < 0 ? null : World.world?.units?.get(pPreviousKingId);
            bool alive = previous?.data != null && !previous.isRekt() && previous.isAlive();
            if (!FormerRulerRecordRules.ShouldRecordLostThrone(pPreviousKingId, pNewKingId, alive)) return;

            string name = previous.getName();
            HistoryText text = HistoryText.Actor(previous, name) + H("aw_hist_lost_throne");
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ABDICATE, text, HistoryTarget.Actor(previous));
            HistoryWriter.RecordPerson(previous.data.id, pKingdom, name,
                PersonEvent.ABDICATE, text, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));
        }

        // 建国
        internal static void OnCollateralRestoration(Kingdom pKingdom, Actor pPreviousKing, Actor pNewKing,
            ShiBranchInfo pRestoredBranch)
        {
            if (pKingdom?.data == null || pNewKing?.data == null) return;

            string label = BuildRestoredShiLabel(pRestoredBranch);
            string color = pRestoredBranch?.origin_kingdom_color;
            if (string.IsNullOrEmpty(color)) color = HistoryColors.FromKingdom(pKingdom);

            HistoryText text = HistoryText.Actor(pNewKing) +
                               H("aw_hist_collateral_restore_mid") +
                               HistoryText.Colored(label, color) +
                               H("aw_hist_collateral_restore_suffix");
            if (pPreviousKing?.data != null)
                text += H("aw_hist_previous_ruler_prefix") + HistoryText.Actor(pPreviousKing) +
                        H("aw_hist_paren_close");

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COLLATERAL_RESTORE, text,
                HistoryTarget.Actor(pNewKing));
            HistoryWriter.RecordPerson(pNewKing.data.id, pKingdom, pNewKing.getName(),
                PersonEvent.COLLATERAL_RESTORE, text, ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));

            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID, out long periodId, -1L);
            if (periodId >= 0)
            {
                pKingdom.data.get(LineageKeys.MANDATE_VALUE, out int mandateValue, 0);
                MandateService.RecordMandateEvent("succession_collateral_restore", pKingdom, pNewKing,
                    pNewKing.city, 0, mandateValue, text.Plain);
            }
        }

        // 异姓入继:找不到男系同姓后裔时,新君以真实身份入继大统(不伪造父系),明确记入史册。
        internal static void OnNonAgnaticSuccession(Kingdom pKingdom, Actor pPreviousKing, Actor pNewKing)
        {
            if (pKingdom?.data == null || pNewKing?.data == null) return;

            HistoryText text = HistoryText.Actor(pNewKing) + H("aw_hist_nonagnatic_succession");
            if (pPreviousKing?.data != null)
                text += H("aw_hist_previous_ruler_prefix") + HistoryText.Actor(pPreviousKing) +
                        H("aw_hist_paren_close");

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COLLATERAL_RESTORE, text,
                HistoryTarget.Actor(pNewKing));
            HistoryWriter.RecordPerson(pNewKing.data.id, pKingdom, pNewKing.getName(),
                PersonEvent.COLLATERAL_RESTORE, text, ChronicleCategory.SOCIAL, HistoryTarget.Kingdom(pKingdom));
        }

        private static string BuildRestoredShiLabel(ShiBranchInfo pBranch)
        {
            if (pBranch == null) return T("aw_hist_old_shi") + "\u6c0f";
            string city = pBranch.origin_city_name ?? "";
            string clan = pBranch.clan_name ?? "";
            if (string.IsNullOrEmpty(clan)) clan = T("aw_hist_old_shi");
            return city + clan + "\u6c0f";
        }

        public static void OnCourtFounded(Kingdom pKingdom, bool pOfficial)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_FOUNDED,
                HistoryText.Kingdom(pKingdom) +
                (pOfficial ? H("aw_hist_court_founded_official") : H("aw_hist_court_founded_primitive")),
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCourtTierUpgraded(Kingdom pKingdom, string pTier)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_TIER_UPGRADED,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_edict_court_tier_mid") +
                HistoryText.PlainText(CourtTierName(pTier)) +
                H("aw_hist_edict_court_tier_suffix"),
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCourtInstitutionReformed(Kingdom pKingdom,
            string pPrevious, string pNext)
        {
            if (pKingdom?.data == null ||
                !CourtInstitutionRules.IsUpgrade(pPrevious, pNext)) return;
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.COURT_INSTITUTION_REFORMED,
                HistoryText.Kingdom(pKingdom) +
                H("aw_hist_edict_court_institution_mid") +
                HistoryText.PlainText(
                    CourtInstitutionService.InstitutionName(pNext)) +
                H("aw_hist_edict_court_institution_suffix"),
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCourtAuxiliaryLawChanged(Kingdom pKingdom,
            Actor pRuler, CourtAuxiliaryLawKind pKind, int pPrevious,
            int pNext)
        {
            if (pKingdom?.data == null) return;
            HistoryText text = HistoryText.Kingdom(pKingdom) +
                               H("aw_hist_court_auxiliary_law_changed_mid") +
                               HistoryText.PlainText(
                                   AuxiliaryLawKindName(pKind)) +
                               H("aw_hist_court_auxiliary_law_from") +
                               HistoryText.PlainText(
                                   AuxiliaryLawValueName(pKind, pPrevious)) +
                               H("aw_hist_court_auxiliary_law_to") +
                               HistoryText.PlainText(
                                   AuxiliaryLawValueName(pKind, pNext));
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.COURT_AUXILIARY_LAW_CHANGED, text,
                pRuler?.data != null
                    ? HistoryTarget.Actor(pRuler)
                    : HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnInheritanceLawChanged(Kingdom pKingdom,
            Actor pRuler, InheritanceLaw? pPrevious,
            InheritanceLaw? pNext)
        {
            if (pKingdom?.data == null) return;
            HistoryText text = HistoryText.Kingdom(pKingdom) +
                               H("aw_hist_inheritance_law_changed_mid") +
                               HistoryText.PlainText(
                                   InheritanceLawControlName(pPrevious)) +
                               H("aw_hist_inheritance_law_changed_to") +
                               HistoryText.PlainText(
                                   InheritanceLawControlName(pNext));
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.INHERITANCE_LAW_CHANGED, text,
                pRuler?.data != null
                    ? HistoryTarget.Actor(pRuler)
                    : HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnSuccessionDisputeStarted(Kingdom pOriginal,
            Kingdom pRival, Actor pSuccessor, Actor pClaimant,
            string pOriginalDisplay, string pRivalDisplay)
        {
            if (pOriginal?.data == null || pRival?.data == null ||
                pClaimant?.data == null) return;
            HistoryText originalName = FrozenKingdomName(pOriginal,
                pOriginalDisplay);
            HistoryText rivalName = FrozenKingdomName(pRival,
                pRivalDisplay);
            HistoryText text = HistoryText.Actor(pClaimant) +
                               H("aw_hist_succession_dispute_claimed_mid") +
                               originalName +
                               H("aw_hist_succession_dispute_founded_mid") +
                               rivalName;
            HistoryWriter.RecordKingdom(pOriginal,
                KingdomEvent.SUCCESSION_DISPUTE_STARTED, text,
                HistoryTarget.Actor(pClaimant));
            HistoryWriter.RecordKingdom(pRival,
                KingdomEvent.SUCCESSION_DISPUTE_STARTED, text,
                HistoryTarget.Kingdom(pOriginal));
            HistoryWriter.RecordPerson(pClaimant.data.id, pRival,
                pClaimant.getName(), PersonEvent.SUCCESSION_DISPUTE_STARTED,
                text, ChronicleCategory.WAR,
                HistoryTarget.Kingdom(pOriginal));
            if (pSuccessor?.data != null)
                HistoryWriter.RecordPerson(pSuccessor.data.id, pOriginal,
                    pSuccessor.getName(),
                    PersonEvent.SUCCESSION_DISPUTE_STARTED, text,
                    ChronicleCategory.WAR,
                    HistoryTarget.Actor(pClaimant));
        }

        public static void OnHeirDesignated(Kingdom pKingdom, Actor pRuler,
            Actor pHeir, string pMode)
        {
            if (pKingdom?.data == null || pHeir?.data == null) return;
            string title = T(HeirTitleRules.TitleKey(
                HeirTitleRules.IsImperialOrMandate(pKingdom), pMode));
            HistoryText ruler = pRuler?.data != null
                ? HistoryText.Actor(pRuler)
                : HistoryText.Kingdom(pKingdom);
            HistoryText text = ruler + H("aw_hist_heir_designated_mid") +
                               HistoryText.Actor(pHeir) +
                               H("aw_hist_heir_designated_as") +
                               HistoryText.PlainText(title);
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.HEIR_DESIGNATED, text,
                HistoryTarget.Actor(pHeir));
            HistoryWriter.RecordPerson(pHeir.data.id, pKingdom,
                pHeir.getName(), PersonEvent.HEIR_DESIGNATED, text,
                ChronicleCategory.HONOR,
                pRuler?.data != null
                    ? HistoryTarget.Actor(pRuler)
                    : HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnSuccessionDisputeResolved(Kingdom pOriginal,
            Actor pSuccessor, Actor pClaimant, bool pClaimantWon,
            string pOriginalDisplay, string pRivalDisplay)
        {
            if (pOriginal?.data == null) return;
            Actor winner = pClaimantWon ? pClaimant : pSuccessor;
            HistoryText text = FrozenKingdomName(pOriginal,
                                   pOriginalDisplay) +
                               H("aw_hist_succession_dispute_defeated_mid") +
                               FrozenPlainName(pRivalDisplay) +
                               H(pClaimantWon
                                   ? "aw_hist_succession_claimant_won_suffix"
                                   : "aw_hist_succession_successor_won_suffix");
            HistoryWriter.RecordKingdom(pOriginal,
                KingdomEvent.SUCCESSION_DISPUTE_RESOLVED, text,
                winner?.data != null
                    ? HistoryTarget.Actor(winner)
                    : HistoryTarget.Kingdom(pOriginal));
            if (winner?.data != null)
                HistoryWriter.RecordPerson(winner.data.id, pOriginal,
                    winner.getName(),
                    PersonEvent.SUCCESSION_DISPUTE_RESOLVED, text,
                    ChronicleCategory.WAR,
                    HistoryTarget.Kingdom(pOriginal));
        }

        public static void OnSuccessionPermanentSplit(Kingdom pOriginal,
            Kingdom pRival, Actor pSuccessor, Actor pClaimant,
            string pOriginalDisplay, string pRivalDisplay)
        {
            if (pOriginal?.data == null || pRival?.data == null) return;
            HistoryText text = FrozenKingdomName(pOriginal,
                                   pOriginalDisplay) +
                               H("aw_hist_succession_split_with_mid") +
                               FrozenKingdomName(pRival, pRivalDisplay) +
                               H("aw_hist_succession_split_suffix");
            HistoryWriter.RecordKingdom(pOriginal,
                KingdomEvent.SUCCESSION_PERMANENT_SPLIT, text,
                HistoryTarget.Kingdom(pRival));
            HistoryWriter.RecordKingdom(pRival,
                KingdomEvent.SUCCESSION_PERMANENT_SPLIT, text,
                HistoryTarget.Kingdom(pOriginal));
            foreach (Actor actor in new[] { pSuccessor, pClaimant })
                if (actor?.data != null)
                    HistoryWriter.RecordPerson(actor.data.id,
                        actor.kingdom, actor.getName(),
                        PersonEvent.SUCCESSION_PERMANENT_SPLIT, text,
                        ChronicleCategory.WAR,
                        HistoryTarget.Kingdom(pOriginal));
        }

        public static void OnSuccessionReunified(Kingdom pOriginal,
            Actor pWinner, string pOriginalDisplay, string pRivalDisplay)
        {
            if (pOriginal?.data == null) return;
            HistoryText text = (pWinner?.data != null
                                   ? HistoryText.Actor(pWinner)
                                   : FrozenKingdomName(pOriginal,
                                       pOriginalDisplay)) +
                               H("aw_hist_succession_reunified_mid") +
                               FrozenPlainName(pOriginalDisplay) +
                               H("aw_hist_succession_reunified_and_mid") +
                               FrozenPlainName(pRivalDisplay) +
                               H("aw_hist_succession_reunified_suffix");
            HistoryWriter.RecordKingdom(pOriginal,
                KingdomEvent.SUCCESSION_REUNIFIED, text,
                pWinner?.data != null
                    ? HistoryTarget.Actor(pWinner)
                    : HistoryTarget.Kingdom(pOriginal));
            if (pWinner?.data != null)
                HistoryWriter.RecordPerson(pWinner.data.id, pOriginal,
                    pWinner.getName(), PersonEvent.SUCCESSION_REUNIFIED,
                    text, ChronicleCategory.WAR,
                    HistoryTarget.Kingdom(pOriginal));
        }

        private static HistoryText FrozenKingdomName(Kingdom pKingdom,
            string pDisplayName)
        {
            return HistoryText.Colored(
                string.IsNullOrEmpty(pDisplayName)
                    ? pKingdom?.name ?? T("aw_unknown_kingdom")
                    : pDisplayName,
                pKingdom?.data == null
                    ? ""
                    : HistoryColors.FromKingdom(pKingdom));
        }

        private static HistoryText FrozenPlainName(string pDisplayName)
        {
            return HistoryText.PlainText(string.IsNullOrEmpty(pDisplayName)
                ? T("aw_unknown_kingdom")
                : pDisplayName);
        }

        public static void OnBorderPetitionApproved(Kingdom pSuzerain,
            Kingdom pRequesterKingdom, Actor pRequester, Kingdom pTarget,
            string pReasonLabel)
        {
            if (pSuzerain?.data == null || pTarget?.data == null) return;
            HistoryText requester = pRequester?.data != null
                ? HistoryText.Actor(pRequester)
                : pRequesterKingdom?.data != null
                    ? HistoryText.Kingdom(pRequesterKingdom)
                    : HistoryText.Kingdom(pSuzerain);
            HistoryText text = HistoryText.Kingdom(pSuzerain) +
                               H("aw_hist_border_petition_source_mid") +
                               requester +
                               H("aw_hist_border_petition_target_mid") +
                               HistoryText.Kingdom(pTarget) +
                               H("aw_hist_border_petition_reason_mid") +
                               HistoryText.PlainText(pReasonLabel ?? "") +
                               H("aw_hist_border_petition_suffix");
            HistoryWriter.RecordKingdom(pSuzerain,
                KingdomEvent.BORDER_PETITION_APPROVED, text,
                HistoryTarget.Kingdom(pTarget));
        }

        private static string AuxiliaryLawKindName(
            CourtAuxiliaryLawKind pKind)
        {
            return T(pKind switch
            {
                CourtAuxiliaryLawKind.Term => "aw_court_aux_law_term",
                CourtAuxiliaryLawKind.BorderCommand =>
                    "aw_court_aux_law_border",
                CourtAuxiliaryLawKind.AppointmentCulture =>
                    "aw_court_aux_law_appointment",
                CourtAuxiliaryLawKind.Conscription =>
                    "aw_court_aux_law_conscription",
                _ => ""
            });
        }

        private static string InheritanceLawControlName(
            InheritanceLaw? pLaw)
        {
            if (!pLaw.HasValue)
                return T("aw_inheritance_control_automatic");
            return T(pLaw.Value switch
            {
                InheritanceLaw.MilitaryAcclaim =>
                    "aw_inheritance_law_military",
                InheritanceLaw.CivilAcclaim =>
                    "aw_inheritance_law_civil",
                _ => "aw_inheritance_law_primogeniture"
            });
        }

        private static string AuxiliaryLawValueName(
            CourtAuxiliaryLawKind pKind, int pValue)
        {
            string key = pKind switch
            {
                CourtAuxiliaryLawKind.Term => pValue switch
                {
                    (int)CourtTermLaw.Lifetime => "aw_court_term_lifetime",
                    (int)CourtTermLaw.FixedThreeYears =>
                        "aw_court_term_three",
                    (int)CourtTermLaw.FixedNineYears =>
                        "aw_court_term_nine",
                    _ => "aw_court_term_dynamic"
                },
                CourtAuxiliaryLawKind.BorderCommand => pValue switch
                {
                    (int)CourtBorderCommandLaw.Discretionary =>
                        "aw_court_border_discretionary",
                    (int)CourtBorderCommandLaw.Centralized =>
                        "aw_court_border_centralized",
                    _ => "aw_court_border_petition"
                },
                CourtAuxiliaryLawKind.AppointmentCulture => pValue switch
                {
                    (int)CourtAppointmentCultureLaw.MeritOnly =>
                        "aw_court_appointment_merit",
                    (int)CourtAppointmentCultureLaw.XiaCentered =>
                        "aw_court_appointment_centered",
                    _ => "aw_court_appointment_preference"
                },
                _ => pValue switch
                {
                    (int)CourtConscriptionLaw.Limited =>
                        "aw_court_conscription_limited",
                    (int)CourtConscriptionLaw.Expanded =>
                        "aw_court_conscription_expanded",
                    (int)CourtConscriptionLaw.FullMobilization =>
                        "aw_court_conscription_full",
                    _ => "aw_court_conscription_standard"
                }
            };
            return T(key);
        }

        private static string CourtTierName(string pTier)
        {
            switch (pTier ?? "")
            {
                case CourtTier.SanShengLiuBu:
                    return AW_L10n.Text("aw_court_tier_sanshengliubu", "Three Departments and Six Ministries");
                case CourtTier.SanGongJiuQing:
                    return AW_L10n.Text("aw_court_tier_sangongjiuqing", "Three Excellencies and Nine Ministers");
                case CourtTier.EasternZhou:
                    return AW_L10n.Text("aw_court_tier_easternzhou", "Eastern Zhou Six Ministers");
                default:
                    return AW_L10n.Text("aw_court_button_locked", "Court Locked");
            }
        }

        public static void OnCourtOfficerAppointed(Actor pActor, Kingdom pKingdom, string pOfficeId, string pSchoolId)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;
            string name = pActor.getName();
            int rank = OfficialCareerStateService.ReadRankFast(pActor);
            string rankKey = OfficialCareerRankRules.RankNameKey(rank);
            string rankFallback =
                OfficialCareerRankRules.RankFallbackEnglish(rank);
            HistoryText text = HistoryText.Actor(pActor, name) +
                               H("aw_hist_court_entered_as") +
                               HistoryText.PlainText(CourtOfficeName(
                                   pKingdom, pOfficeId)) +
                               H("aw_hist_court_school_mid") +
                               HistoryText.PlainText(CourtSchoolName(pSchoolId));
            if (CourtService.HasNineRankSystem(pKingdom))
                text += HistoryText.PlainText(AW_L10n.Text(
                            "aw_hist_court_rank_mid", ", at official rank ")) +
                        HistoryText.PlainText(AW_L10n.Text(rankKey,
                            rankFallback));

            HistoryWriter.RecordPerson(pActor.data.id, pKingdom, name,
                PersonEvent.COURT_OFFICER_APPOINTED, text, ChronicleCategory.CAREER,
                HistoryTarget.Kingdom(pKingdom));

            if (ChronicleGate.IsImportant(pActor))
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_OFFICER_APPOINTED, text,
                    HistoryTarget.Actor(pActor));
        }

        public static void OnCivilServiceExamOpened(Kingdom pKingdom,
            int pCycleYear, string pMode, int pCandidateCount)
        {
            if (pKingdom?.data == null) return;
            HistoryText text = HistoryText.Kingdom(pKingdom) +
                               H("aw_hist_civil_service_exam_opened_mid") +
                               HistoryText.PlainText(CivilServiceModeName(pMode)) +
                               H("aw_hist_civil_service_exam_opened_year_mid") +
                               HistoryText.PlainText(pCycleYear.ToString()) +
                               H("aw_hist_civil_service_exam_opened_candidates_mid") +
                               HistoryText.PlainText(Math.Max(0,
                                   pCandidateCount).ToString()) +
                               H("aw_hist_civil_service_exam_opened_suffix");
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.CIVIL_SERVICE_EXAM_OPENED, text,
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCivilServiceQualification(Kingdom pKingdom,
            long pActorId, string pActorName, string pQualification,
            int pCycleYear)
        {
            if (pKingdom?.data == null || pActorId < 0L ||
                string.IsNullOrEmpty(pActorName) ||
                string.IsNullOrEmpty(pQualification) ||
                pQualification == "none") return;
            HistoryText actor = SnapshotActor(pActorId, pActorName, pKingdom);
            HistoryText text = actor +
                               H("aw_hist_civil_service_qualified_mid") +
                               HistoryText.PlainText(
                                   CivilServiceQualificationName(
                                       pQualification)) +
                               H("aw_hist_civil_service_qualified_year_mid") +
                               HistoryText.PlainText(pCycleYear.ToString()) +
                               H("aw_hist_civil_service_qualified_suffix");
            HistoryWriter.RecordPerson(pActorId, pKingdom, pActorName,
                PersonEvent.CIVIL_SERVICE_QUALIFIED, text,
                ChronicleCategory.CAREER, HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCivilServiceTopRanked(Kingdom pKingdom,
            long pActorId, string pActorName, int pRank, string pRankTitle,
            int pCycleYear)
        {
            if (pKingdom?.data == null || pActorId < 0L ||
                string.IsNullOrEmpty(pActorName) || pRank < 1 || pRank > 3)
                return;
            HistoryText actor = SnapshotActor(pActorId, pActorName, pKingdom);
            HistoryText text = actor +
                               H("aw_hist_civil_service_top_ranked_mid") +
                               HistoryText.PlainText(CivilServiceRankName(
                                   pRankTitle, pRank)) +
                               H("aw_hist_civil_service_top_ranked_year_mid") +
                               HistoryText.PlainText(pCycleYear.ToString()) +
                               H("aw_hist_civil_service_top_ranked_suffix");
            HistoryWriter.RecordPerson(pActorId, pKingdom, pActorName,
                PersonEvent.CIVIL_SERVICE_TOP_RANKED, text,
                ChronicleCategory.CAREER, HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCivilServiceExamCompleted(Kingdom pKingdom,
            int pCycleYear, string pMode)
        {
            if (pKingdom?.data == null) return;
            HistoryText text = HistoryText.Kingdom(pKingdom) +
                               H("aw_hist_civil_service_exam_completed_mid") +
                               HistoryText.PlainText(CivilServiceModeName(pMode)) +
                               H("aw_hist_civil_service_exam_completed_year_mid") +
                               HistoryText.PlainText(pCycleYear.ToString()) +
                               H("aw_hist_civil_service_exam_completed_suffix");
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.CIVIL_SERVICE_EXAM_COMPLETED, text,
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCivilServiceFirstAppointment(Actor pActor,
            Kingdom pKingdom, string pOfficeId, string pQualification)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;
            HistoryText text = HistoryText.Actor(pActor) +
                               H("aw_hist_civil_service_first_appointment_mid") +
                               HistoryText.PlainText(
                                   CivilServiceQualificationName(
                                       pQualification)) +
                               H("aw_hist_civil_service_first_appointment_office_mid") +
                               HistoryText.PlainText(CourtOfficeName(
                                   pKingdom, pOfficeId)) +
                               H("aw_hist_civil_service_first_appointment_suffix");
            HistoryWriter.RecordPerson(pActor.data.id, pKingdom,
                pActor.getName(), PersonEvent.CIVIL_SERVICE_FIRST_APPOINTMENT,
                text, ChronicleCategory.CAREER,
                HistoryTarget.Kingdom(pKingdom));
        }

        private static HistoryText SnapshotActor(long pActorId,
            string pActorName, Kingdom pKingdom)
        {
            return HistoryText.Reference(pActorName,
                HistoryColors.FromKingdom(pKingdom), "actor", pActorId);
        }

        private static string CivilServiceModeName(string pMode)
        {
            return string.Equals(pMode, "imperial_exam",
                    StringComparison.Ordinal)
                ? AW_L10n.Text("aw_civil_service_mode_imperial",
                    "Imperial Examination")
                : AW_L10n.Text("aw_civil_service_mode_tribute",
                    "Tribute Examination");
        }

        private static string CivilServiceQualificationName(string pValue)
        {
            return pValue switch
            {
                "juren" => AW_L10n.Text(
                    "aw_civil_service_qualification_juren", "Juren"),
                "gongshi" => AW_L10n.Text(
                    "aw_civil_service_qualification_gongshi", "Gongshi"),
                "jinshi" => AW_L10n.Text(
                    "aw_civil_service_qualification_jinshi", "Jinshi"),
                _ => AW_L10n.Text(
                    "aw_civil_service_qualification_none", "Unqualified")
            };
        }

        private static string CivilServiceRankName(string pTitle, int pRank)
        {
            string key = pTitle switch
            {
                "zhuangyuan" => "aw_civil_service_rank_zhuangyuan",
                "bangyan" => "aw_civil_service_rank_bangyan",
                "tanhua" => "aw_civil_service_rank_tanhua",
                _ => pRank == 1 ? "aw_civil_service_rank_zhuangyuan" :
                    pRank == 2 ? "aw_civil_service_rank_bangyan" :
                    "aw_civil_service_rank_tanhua"
            };
            string fallback = pRank == 1 ? "Principal Graduate" :
                pRank == 2 ? "Second Graduate" : "Third Graduate";
            return AW_L10n.Text(key, fallback);
        }

        public static void OnOfficialRankPromoted(Actor pActor,
            Kingdom pKingdom, int pTrack, int pPreviousRank, int pNextRank,
            string pOfficeId)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !OfficialCareerBiographyRules.ShouldRecordRankAdvance(
                    CourtService.HasNineRankSystem(pKingdom),
                    persistenceCommitted: true, pPreviousRank, pNextRank)) return;

            string trackTitle = AW_L10n.Text(
                OfficialCareerRankRules.TrackTitleKey(pTrack),
                OfficialCareerRankRules.TrackTitleFallbackEnglish(pTrack));
            string rankTitle = AW_L10n.Text(
                OfficialCareerRankRules.RankNameKey(pNextRank),
                OfficialCareerRankRules.RankFallbackEnglish(pNextRank));
            string formalRank = string.Format(AW_L10n.Text(
                    "aw_court_joint_rank_format", "{0} · {1}"),
                trackTitle, rankTitle);
            string office = CourtOfficeName(pKingdom, pOfficeId);

            HistoryText text = H("aw_hist_official_edict_prefix") +
                               HistoryText.Actor(pActor) +
                               H(pPreviousRank <= OfficialCareerRankRules.Unranked
                                   ? "aw_hist_official_rank_grant_mid"
                                   : "aw_hist_official_edict_mid") +
                               HistoryText.PlainText(formalRank);
            if (!string.IsNullOrEmpty(office))
                text += H("aw_hist_official_edict_office_mid") +
                        HistoryText.PlainText(office);
            text += H(pPreviousRank <= OfficialCareerRankRules.Unranked
                ? "aw_hist_official_rank_grant_suffix"
                : "aw_hist_official_edict_suffix");
            HistoryWriter.RecordPerson(pActor.data.id, pKingdom,
                pActor.getName(), PersonEvent.OFFICIAL_APPOINTMENT_EDICT,
                text, ChronicleCategory.CAREER,
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCourtFactionDominant(Kingdom pKingdom, string pSchoolId)
        {
            if (pKingdom?.data == null || string.IsNullOrEmpty(pSchoolId)) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_FACTION_DOMINANT,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_court_dominant_mid") +
                HistoryText.PlainText(CourtSchoolName(pSchoolId)) + H("aw_hist_court_dominant_suffix"),
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCourtOfficerDismissed(Actor pActor, Kingdom pKingdom, string pOfficeId, string pReason)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;
            string name = pActor.getName();
            HistoryText text = HistoryText.Actor(pActor, name) +
                               H("aw_hist_court_dismissed_mid") +
                               HistoryText.PlainText(CourtOfficeName(
                                   pKingdom, pOfficeId));
            HistoryWriter.RecordPerson(pActor.data.id, pKingdom, name,
                PersonEvent.COURT_OFFICER_DISMISSED, text, ChronicleCategory.CAREER,
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnOfficialPetition(Kingdom pKingdom, Actor pActor,
            int pMoneyCost, float pFavor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            HistoryText text = HistoryText.Actor(pActor) +
                               H("aw_hist_court_petition_mid") +
                               HistoryText.PlainText(pMoneyCost.ToString()) +
                               H("aw_hist_court_petition_favor_mid") +
                               HistoryText.PlainText(pFavor.ToString("0.#"));
            HistoryWriter.RecordPerson(pActor.data.id, pKingdom,
                pActor.getName(), "court_petition", text,
                ChronicleCategory.SOCIAL, HistoryTarget.Kingdom(pKingdom));
            HistoryWriter.RecordKingdom(pKingdom, "court_petition", text,
                HistoryTarget.Actor(pActor));
        }

        public static void OnCourtReformEvent(Kingdom pKingdom, string pDominantSchool)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_REFORM_EVENT,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_edict_court_reform_mid") +
                HistoryText.PlainText(CourtSchoolName(pDominantSchool)) +
                H("aw_hist_edict_court_reform_suffix"),
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnCourtCityBureau(Kingdom pKingdom, string pCityName, string pSchoolId)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.COURT_CITY_BUREAU,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_court_bureau_mid") +
                HistoryText.PlainText(pCityName ?? "") + H("aw_hist_court_bureau_suffix") +
                HistoryText.PlainText(CourtSchoolName(pSchoolId)),
                HistoryTarget.Kingdom(pKingdom));
        }

        private static string CourtOfficeName(Kingdom pKingdom,
            string pOfficeId)
        {
            return CourtInstitutionService.OfficeName(pKingdom, pOfficeId);
        }

        private static string CourtSchoolName(string pSchoolId)
        {
            switch (pSchoolId ?? "")
            {
                case CourtSchoolId.Ru: return AW_L10n.Text("aw_court_school_ru", "Ru");
                case CourtSchoolId.Legalist: return AW_L10n.Text("aw_court_school_fa", "Legalist");
                case CourtSchoolId.Dao: return AW_L10n.Text("aw_court_school_dao", "Dao");
                case CourtSchoolId.Mohist: return AW_L10n.Text("aw_court_school_mo", "Mohist");
                case CourtSchoolId.Military: return AW_L10n.Text("aw_court_school_bing", "Military");
                case CourtSchoolId.Diplomat: return AW_L10n.Text("aw_court_school_zongheng", "Diplomat");
                case CourtSchoolId.Agrarian: return AW_L10n.Text("aw_court_school_nong", "Agrarian");
                case CourtSchoolId.YinYang: return AW_L10n.Text("aw_court_school_yinyang", "Yin-Yang");
                case CourtSchoolId.Logician: return AW_L10n.Text("aw_court_school_ming", "Logician");
                case CourtSchoolId.Medical: return AW_L10n.Text("aw_court_school_medical", "Medical");
                case CourtSchoolId.Syncretist: return AW_L10n.Text("aw_court_school_syncretist", "Syncretist");
                case CourtSchoolId.Merchant: return AW_L10n.Text("aw_court_school_merchant", "Merchant");
                case CourtSchoolId.Craftsman: return AW_L10n.Text("aw_court_school_craftsman", "Craftsman");
                case CourtSchoolId.Historian: return AW_L10n.Text("aw_court_school_historian", "Historian");
                case CourtSchoolId.PrimitiveMinister: return AW_L10n.Text("aw_court_school_primitive", "Eastern Zhou Courtier");
                default:
                    return string.IsNullOrEmpty(pSchoolId)
                        ? AW_L10n.Text("aw_court_school_none", "No school")
                        : pSchoolId;
            }
        }

        public static void OnKingdomFounded(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!KingdomArchiveWriter.IsArchivable(pKingdom)) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.FOUND,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_kingdom_founded_suffix"));
            KingdomArchiveWriter.Upsert(pKingdom); // 建国快照(名/旗/颜色/建国时间)
        }

        // 亡国
        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!KingdomArchiveWriter.IsArchivable(pKingdom)) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.DESTROYED,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_kingdom_destroyed_suffix"));
            Actor king = pKingdom.king;
            bool wasMandateKingdom = MandateService.IsMandateKingdom(pKingdom);
            FormerKingService.OnKingdomDestroyed(pKingdom, king, wasMandateKingdom);
            KingdomArchiveWriter.EnsureRow(pKingdom);
            KingdomArchiveWriter.MarkDestroyed(pKingdom);
            RulerAppellationService.RemoveKingdom(pKingdom.id);
            MandateService.OnKingdomDestroyed(pKingdom);
            // 结构表：关闭该国所有开着的 reign / dynasty / era（kingdom_fell）
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom, "kingdom_fell", king);
            if (king?.data != null)
                PosthumousTitleService.OnReignEnded(pKingdom, king, "kingdom_fell", reign);
            DynastyRecordWriter.CloseOpenDynasty(pKingdom.id, DynastyRecordWriter.END_REASON_KINGDOM_FELL);
            EraRecordWriter.CloseOpenEra(pKingdom.id);
        }

        // 驾崩:在位君主死亡。国家史 + 关 reign + 评谥。
        public static void OnKingDied(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom, "died", pKing);
            PosthumousTitleService.OnReignEnded(pKingdom, pKing, "died", reign);
        }

        // 退位:君主主动让位(仍在世)。国家史 + 人物史 + 关 reign + 评谥。
        public static void OnAbdicate(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            string name = pKing.getName();
            if (SlaveKingAbdicationService.TryConsumeReason(pKing.data.id, out string slaveReason))
            {
                HistoryText text = H("aw_hist_slave_abdicated_prefix") +
                                   HistoryText.PlainText(SlaveService.ReasonLabel(slaveReason)) +
                                   H("aw_hist_paren_close");
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ABDICATE,
                    HistoryText.Actor(pKing, name) + text);
                HistoryWriter.RecordPerson(pKing.data.id, pKingdom, name,
                    PersonEvent.ABDICATE, HistoryText.Actor(pKing, name) + text, ChronicleCategory.HONOR);
                ReignRecordWriter.ReignInfo slaveReign =
                    ReignRecordWriter.CloseOpenReign(pKingdom, "abdicated", pKing);
                PosthumousTitleService.OnReignEnded(pKingdom, pKing, "abdicated", slaveReign);
                return;
            }
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ABDICATE,
                HistoryText.Actor(pKing, name) + H("aw_hist_abdicated"));
            if (ChronicleGate.IsNobleActor(pKing))
                HistoryWriter.RecordPerson(pKing.data.id, pKingdom, name,
                    PersonEvent.ABDICATE, HistoryText.Actor(pKing, name) + H("aw_hist_abdicated"), ChronicleCategory.HONOR);
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.CloseOpenReign(pKingdom, "abdicated", pKing);
            PosthumousTitleService.OnReignEnded(pKingdom, pKing, "abdicated", reign);
        }

        // 建城:City.newCityEvent(纯新建城,不含读档)。记一条 found 事件作城市史起点。
        public static void OnCityFounded(City pCity)
        {
            if (pCity?.data == null) return;
            Kingdom kingdom = pCity.kingdom;                 // 建城者所属国(newCityEvent 时已设)
            string cityName = pCity.data.name;
            HistoryText kingdomPart = kingdom != null
                ? H("aw_hist_belongs_to_prefix") + HistoryText.Kingdom(kingdom) + H("aw_hist_belongs_to_suffix")
                : HistoryText.PlainText("");
            HistoryWriter.RecordCity(pCity, kingdom, CityEvent.CITY_FOUND,
                HistoryText.City(pCity, kingdom, cityName) + H("aw_hist_city_founded_prefix") +
                kingdomPart + H("aw_hist_city_founded_suffix"));
            KingdomArchiveWriter.Upsert(kingdom);
            WarTerritoryService.EnsureCore(kingdom, pCity, "founded", "自建城市");
        }

        // 城市易主:仅当"旧国非空 且 旧国 != 新国"(真易主),且非读档回填。
        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom, bool pFromLoad)
        {
            if (pFromLoad) return;                                  // 读档回填不记
            if (pCity?.data == null) return;
            if (pOldKingdom == null) return;                        // 初次归属不记
            if (pNewKingdom == null) return;
            if (pOldKingdom == pNewKingdom) return;                 // 无变化不记

            bool oldArchivable = KingdomArchiveWriter.IsArchivable(pOldKingdom);
            bool newArchivable = KingdomArchiveWriter.IsArchivable(pNewKingdom);
            if (!oldArchivable && !newArchivable) return;

            string oldName = pOldKingdom.name;
            string newName = pNewKingdom.name;
            if (!oldArchivable || !newArchivable)
            {
                if (oldArchivable)
                {
                    HistoryWriter.RecordCity(pCity, pOldKingdom, CityEvent.CITY_TRANSFER,
                        HistoryText.City(pCity, pOldKingdom) + H("aw_hist_city_left_owner") +
                        HistoryText.Kingdom(pOldKingdom, oldName) + H("aw_hist_city_became_unowned"));
                    HistoryWriter.RecordKingdom(pOldKingdom, KingdomEvent.CITY_LOST,
                        H("aw_hist_lost_city_prefix") + HistoryText.City(pCity, pOldKingdom) +
                        H("aw_hist_city_abandoned_note"));
                    KingdomArchiveWriter.Upsert(pOldKingdom);
                }
                else
                {
                    HistoryWriter.RecordCity(pCity, pNewKingdom, CityEvent.CITY_TRANSFER,
                        HistoryText.City(pCity, pNewKingdom) + H("aw_hist_city_joined") +
                        HistoryText.Kingdom(pNewKingdom, newName));
                    HistoryWriter.RecordKingdom(pNewKingdom, KingdomEvent.CITY_GAINED,
                        H("aw_hist_gained_city_prefix") + HistoryText.City(pCity, pNewKingdom) +
                        H("aw_hist_city_former_unowned_note"));
                    KingdomArchiveWriter.Upsert(pNewKingdom);
                }
                return;
            }
            HistoryWriter.RecordCity(pCity, pNewKingdom, CityEvent.CITY_TRANSFER,
                HistoryText.City(pCity, pNewKingdom) + H("aw_hist_city_transfer_from") +
                HistoryText.Kingdom(pOldKingdom, oldName) + H("aw_hist_city_transfer_to") +
                HistoryText.Kingdom(pNewKingdom, newName));

            // 国家视角(批2):旧国失城、新国得城(同一信号,双国各记 KingdomHistory)。
            HistoryWriter.RecordKingdom(pOldKingdom, KingdomEvent.CITY_LOST,
                H("aw_hist_lost_city_prefix") + HistoryText.City(pCity, pOldKingdom) +
                H("aw_hist_city_to_kingdom_prefix") + HistoryText.Kingdom(pNewKingdom, newName) + ")");
            HistoryWriter.RecordKingdom(pNewKingdom, KingdomEvent.CITY_GAINED,
                H("aw_hist_gained_city_prefix") + HistoryText.City(pCity, pNewKingdom) +
                H("aw_hist_city_from_kingdom_prefix") + HistoryText.Kingdom(pOldKingdom, oldName) + ")");
            KingdomArchiveWriter.Upsert(pOldKingdom);
            KingdomArchiveWriter.Upsert(pNewKingdom);
        }

        // 战争开始:给双方各记一条 war_start 国家史(由 AW_WarPatch 分别传入自身国)。
        public static void OnWarStart(Kingdom pSelf, string pOpponentName, string pWarType)
        {
            OnWarStart(pSelf, null, pOpponentName, pWarType);
        }

        public static void OnWarStart(Kingdom pSelf, Kingdom pOpponent, string pOpponentName, string pWarType)
        {
            if (pSelf?.data == null) return;
            string label = string.IsNullOrEmpty(pWarType) ? "" : "（" + WarDisplayLabelRules.Label(pWarType) + "）";
            HistoryWriter.RecordKingdom(pSelf, KingdomEvent.WAR_START,
                H("aw_hist_war_with_prefix") + HistoryText.Kingdom(pOpponent, pOpponentName) +
                H("aw_hist_war_started") + HistoryText.PlainText(label));
        }

        // 战争结束:给双方各记一条 war_end 国家史。
        public static void OnWarEnd(Kingdom pSelf, string pOpponentName, string pResult)
        {
            OnWarEnd(pSelf, null, pOpponentName, HistoryText.PlainText(pResult));
        }

        public static void OnWarEnd(Kingdom pSelf, Kingdom pOpponent, string pOpponentName, HistoryText pResult)
        {
            if (pSelf?.data == null) return;
            HistoryWriter.RecordKingdom(pSelf, KingdomEvent.WAR_END,
                H("aw_hist_war_with_prefix") + HistoryText.Kingdom(pOpponent, pOpponentName) +
                H("aw_hist_war_ended_mid") + pResult);
            SlaveService.FlushPendingWarSlaveCaptures(pSelf);
        }

        // ───────────────────────── 人物事件(批1) ─────────────────────────

        /// <summary>父母得子:给贵族父/母各记一条"喜得子/女"。baby 出生已由谱系系统处理,此处只记父母视角。</summary>
        public static void OnHadChild(Actor pParent1, Actor pParent2, Actor pBaby)
        {
            if (pBaby?.data == null) return;
            string babyName = pBaby.getName();
            string kind = pBaby.isSexMale() ? T("aw_hist_son") : T("aw_hist_daughter");
            RecordParentHadChild(pParent1, pBaby, babyName, kind);
            RecordParentHadChild(pParent2, pBaby, babyName, kind);
        }

        private static void RecordParentHadChild(Actor pParent, Actor pBaby, string pBabyName, string pKind)
        {
            if (!ChronicleGate.IsNobleActor(pParent)) return;
            HistoryWriter.RecordPerson(pParent.data.id, pParent.kingdom, pParent.getName(),
                PersonEvent.HAD_CHILD,
                HistoryText.Actor(pParent) + H("aw_hist_had_child") + HistoryText.PlainText(pKind + " ") +
                HistoryText.Actor(pBaby, pBabyName),
                ChronicleCategory.LIFE);
        }

        /// <summary>封城主。</summary>
        public static void OnBecomeLeader(Actor pActor)
        {
            if (!ChronicleGate.IsNobleActor(pActor) && !LineageService.HasOriginalClan(pActor)) return;
            string name = pActor.getName();
            City city = pActor.city;
            string cityName = city?.data != null ? city.data.name : T("aw_unknown_city");
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.BECOME_LEADER,
                HistoryText.Actor(pActor, name) + H("aw_hist_enfeoffed_leader_prefix") +
                HistoryText.City(city, pActor.kingdom, cityName) + H("aw_hist_city_leader_suffix"),
                ChronicleCategory.HONOR);
        }

        /// <summary>成为家主(氏族族长)。</summary>
        public static void OnBecomeClanChief(Actor pActor)
        {
            if (!ChronicleGate.IsNobleActor(pActor) && !LineageService.HasOriginalClan(pActor)) return;
            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.BECOME_CLAN_CHIEF, HistoryText.Actor(pActor, name) + H("aw_hist_became_clan_chief"), ChronicleCategory.CLAN);
        }

        /// <summary>被逐出氏族。</summary>
        public static void OnExiledFromClan(Actor pActor)
        {
            if (pActor?.data == null || !LineageService.IsXia(pActor)) return;
            pActor.data.set(LineageKeys.CHRONICLE_LAST_ORIGINAL_CLAN_ID, -1L);
            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.EXILED_CLAN, HistoryText.Actor(pActor, name) + H("aw_hist_exiled_from_clan"), ChronicleCategory.CLAN);
        }

        /// <summary>Original WorldBox clan membership, independent from AW lineage.</summary>
        public static void OnJoinedOriginalClan(Actor pActor, Clan pClan)
        {
            if (pActor?.data == null || pClan?.data == null) return;
            if (!LineageService.IsXia(pActor)) return;
            if (AncientWarfare3.core.db.LineageArchiveManager.Instance.OperatingDB == null) return;

            long clanId = pClan.data.id;
            pActor.data.get(LineageKeys.CHRONICLE_LAST_ORIGINAL_CLAN_ID, out long lastClanId, -1L);
            if (lastClanId == clanId) return;
            pActor.data.set(LineageKeys.CHRONICLE_LAST_ORIGINAL_CLAN_ID, clanId);

            string name = pActor.getName();
            string clanName = string.IsNullOrEmpty(pClan.data.name) ? T("aw_hist_clan_fallback") : pClan.data.name;
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.JOINED_CLAN,
                HistoryText.Actor(pActor, name) + H("aw_hist_joined_clan") +
                HistoryText.ClanName(clanName, pClan, pActor.kingdom),
                ChronicleCategory.CLAN,
                HistoryTarget.Actor(pActor));
        }

        /// <summary>发动叛乱:人物记一条 + 原属国国家史记一条。</summary>
        public static void OnRebellion(Actor pActor, Kingdom pOldKingdom)
        {
            string name = pActor != null ? pActor.getName() : T("aw_hist_someone");
            if (ChronicleGate.IsNobleActor(pActor))
                HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                    PersonEvent.REBELLION, HistoryText.Actor(pActor, name) + H("aw_hist_rebelled"), ChronicleCategory.WAR);
            if (pOldKingdom?.data != null)
                HistoryWriter.RecordKingdom(pOldKingdom, KingdomEvent.REBELLION,
                    HistoryText.Actor(pActor, name) + H("aw_hist_rebelled_in_realm"));
        }

        /// <summary>入伍(成为战士)。仅贵族。</summary>
        public static void OnEnlisted(Actor pActor)
        {
            if (!ChronicleGate.IsNobleActor(pActor)) return;
            string name = pActor.getName();
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom, name,
                PersonEvent.ENLISTED, HistoryText.Actor(pActor, name) + H("aw_hist_enlisted"), ChronicleCategory.WAR);
        }

        /// <summary>
        ///     重要击杀:凶手是贵族 → 给凶手记一条;被杀者是王/城主/名人 → 额外给被杀者所属国国家史记一条。
        /// </summary>
        public static void OnEnslaved(Actor pActor, string pReason, Kingdom pKingdom, City pCity,
            bool pForceNationalRecord = false)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            string reason = SlaveService.ReasonLabel(pReason);
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.ENSLAVED,
                HistoryText.Actor(pActor, name) + H("aw_hist_enslaved") + HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                ChronicleCategory.SOCIAL,
                HistoryTarget.Actor(pActor));

            if (kingdom?.data != null && (pForceNationalRecord || IsNationalSlaveEvent(pActor)))
                HistoryWriter.RecordKingdom(kingdom, KingdomEvent.ENSLAVED,
                    HistoryText.Actor(pActor, name) + H("aw_hist_captured_as_slave") +
                    HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                    HistoryTarget.Actor(pActor));

            if (city?.data != null && SlaveChronicleRules.ShouldRecordIndividualCityEnslavement(pReason))
                HistoryWriter.RecordCity(city, kingdom, CityEvent.ENSLAVED,
                    HistoryText.Actor(pActor, name) + H("aw_hist_in_city") + HistoryText.City(city, kingdom) +
                    H("aw_hist_registered_slave") + HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                    HistoryTarget.Actor(pActor));
        }

        public static void OnCapturedRulerEnslaved(Actor pActor, string pReason, Kingdom pFormerKingdom,
            Kingdom pCaptorKingdom, City pCaptorCity, Actor pCaptor)
        {
            if (pActor?.data == null || pFormerKingdom?.data == null) return;
            string name = pActor.getName();
            string reason = SlaveService.ReasonLabel(pReason);
            HistoryText captor = pCaptorKingdom?.data != null
                ? HistoryText.Kingdom(pCaptorKingdom)
                : H("aw_hist_enemy_realm");
            HistoryText text = HistoryText.Actor(pActor, name) +
                               H("aw_hist_as_former_ruler_mid") +
                               HistoryText.Kingdom(pFormerKingdom) +
                               H("aw_hist_ruler_of_former_kingdom") +
                               captor +
                               H("aw_hist_captured_enslaved") +
                               HistoryText.PlainText(reason) + H("aw_hist_paren_close");
            if (pCaptor?.data != null)
                text += H("aw_hist_captor_prefix") + HistoryText.Actor(pCaptor);
            if (pCaptorCity?.data != null)
                text += H("aw_hist_placed_in_city") +
                        HistoryText.City(pCaptorCity, pCaptorKingdom);

            HistoryWriter.RecordKingdom(pFormerKingdom, KingdomEvent.ENSLAVED, text,
                HistoryTarget.Actor(pActor));
            HistoryWriter.RecordPerson(pActor.data.id, pFormerKingdom, name,
                PersonEvent.ENSLAVED, text, ChronicleCategory.HONOR,
                HistoryTarget.Kingdom(pFormerKingdom));
        }

        public static void OnImportantCaptiveExecuted(Actor pActor, string pReason, Kingdom pFormerKingdom,
            Kingdom pCaptorKingdom, City pCaptorCity, Actor pCaptor, string pDominantSchool)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            string reason = SlaveService.ReasonLabel(pReason);
            HistoryText captorKingdom = pCaptorKingdom?.data != null
                ? HistoryText.Kingdom(pCaptorKingdom)
                : H("aw_hist_enemy_realm");
            HistoryText school = HistoryText.PlainText(CaptiveTreatmentRules.SchoolLabel(pDominantSchool));
            HistoryText text = HistoryText.Actor(pActor, name) +
                               H("aw_hist_captive_executed_mid") +
                               HistoryText.PlainText(reason) + H("aw_hist_paren_close") +
                               H("aw_hist_captive_executed_school_mid") + school +
                               H("aw_hist_captive_executed_school_suffix");
            if (pCaptor?.data != null)
                text += H("aw_hist_captor_prefix") + HistoryText.Actor(pCaptor);
            if (pCaptorCity?.data != null)
                text += H("aw_hist_placed_in_city") + HistoryText.City(pCaptorCity, pCaptorKingdom);

            Kingdom personContext = pFormerKingdom?.data != null ? pFormerKingdom : pCaptorKingdom;
            HistoryWriter.RecordPerson(pActor.data.id, personContext, name,
                PersonEvent.CAPTIVE_EXECUTED, text, ChronicleCategory.HONOR,
                pCaptorKingdom?.data != null ? HistoryTarget.Kingdom(pCaptorKingdom) : HistoryTarget.Actor(pActor));

            if (pFormerKingdom?.data != null)
            {
                HistoryText formerText = HistoryText.Actor(pActor, name) +
                                         H("aw_hist_captive_executed_former_mid") +
                                         captorKingdom +
                                         H("aw_hist_captive_executed_former_suffix");
                HistoryWriter.RecordKingdom(pFormerKingdom, KingdomEvent.CAPTIVE_EXECUTED, formerText,
                    HistoryTarget.Actor(pActor));
            }

            if (pCaptorKingdom?.data != null)
            {
                HistoryWriter.RecordKingdom(pCaptorKingdom, KingdomEvent.CAPTIVE_EXECUTED,
                    captorKingdom + H("aw_hist_captive_executed_captor_mid") +
                    HistoryText.Actor(pActor, name) +
                    H("aw_hist_captive_executed_captor_suffix") + school,
                    HistoryTarget.Actor(pActor));
            }
        }

        public static void OnFreedSlave(Actor pActor, string pReason, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            string reason = SlaveService.ReasonLabel(pReason);
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.FREED_SLAVE,
                HistoryText.Actor(pActor, name) + H("aw_hist_freed_slave") +
                HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                ChronicleCategory.SOCIAL,
                HistoryTarget.Actor(pActor));

            if (kingdom?.data != null && IsNationalSlaveEvent(pActor))
                HistoryWriter.RecordKingdom(kingdom, KingdomEvent.FREED_SLAVE,
                    HistoryText.Actor(pActor, name) + H("aw_hist_freed_slave_short") +
                    HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                    HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.FREED_SLAVE,
                    HistoryText.Actor(pActor, name) + H("aw_hist_in_city") + HistoryText.City(city, kingdom) +
                    H("aw_hist_city_freed_slave") + HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                    HistoryTarget.Actor(pActor));
        }

        public static void OnImportantCaptiveReleasedAsNoble(Actor pActor, string pReason, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            string reason = SlaveService.ReasonLabel(pReason);
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryText message = HistoryText.Actor(pActor, name) +
                                  H("aw_hist_captive_noble_released") +
                                  HistoryText.PlainText(reason) + H("aw_hist_paren_close");
            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.FREED_SLAVE, message, ChronicleCategory.HONOR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.FREED_SLAVE,
                    HistoryText.Actor(pActor, name) + H("aw_hist_in_city") + HistoryText.City(city, kingdom) +
                    H("aw_hist_captive_noble_city_released") +
                    HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                    HistoryTarget.Actor(pActor));
        }

        public static void OnRetiredSoldier(Actor pActor, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.RETIRED_SOLDIER,
                HistoryText.Actor(pActor, name) + H("aw_hist_retired_soldier"),
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.RETIRED_SOLDIER,
                    HistoryText.Actor(pActor, name) + H("aw_hist_retired_from_city") +
                    HistoryText.City(city, kingdom) + H("aw_hist_retired_from_city_suffix"),
                    HistoryTarget.Actor(pActor));
        }

        public static void OnSlaveEnlisted(Actor pActor, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.SLAVE_ENLISTED,
                HistoryText.Actor(pActor, name) + H("aw_hist_slave_enlisted"),
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.SLAVE_ENLISTED,
                    HistoryText.Actor(pActor, name) + H("aw_hist_in_city") +
                    HistoryText.City(city, kingdom) + H("aw_hist_slave_army_joined"),
                    HistoryTarget.Actor(pActor));
        }

        public static void OnRoyalMedicalCure(Actor pPhysician, Actor pPatient, Kingdom pKingdom)
        {
            if (pPhysician?.data == null || pPatient?.data == null || pKingdom?.data == null) return;
            string patientName = pPatient.getName();
            HistoryText text = HistoryText.Actor(pPatient, patientName) +
                               H("aw_hist_royal_medical_cure_mid") +
                               HistoryText.Actor(pPhysician, pPhysician.getName()) +
                               H("aw_hist_royal_medical_cure_suffix");
            HistoryWriter.RecordPerson(pPatient.data.id, pKingdom, patientName,
                PersonEvent.ROYAL_MEDICAL_CURE, text, ChronicleCategory.SOCIAL,
                HistoryTarget.Actor(pPhysician));
        }

        internal static void OnSlaveEnlisted(ChronicleActorSnapshot pSnapshot)
        {
            if (pSnapshot == null || pSnapshot.actor_id < 0) return;
            HistoryWriter.RecordDeferredPerson(pSnapshot.context, pSnapshot.person,
                pSnapshot.actor_id, pSnapshot.actor_name, PersonEvent.SLAVE_ENLISTED,
                pSnapshot.ActorText() + H("aw_hist_slave_enlisted"), ChronicleCategory.WAR,
                HistoryTarget.Actor(pSnapshot.actor_id));
            if (pSnapshot.city_id >= 0)
                HistoryWriter.RecordDeferredCity(pSnapshot.context, pSnapshot.city_id,
                    pSnapshot.city_name, CityEvent.SLAVE_ENLISTED,
                    pSnapshot.ActorText() + H("aw_hist_in_city") + pSnapshot.CityText() +
                    H("aw_hist_slave_army_joined"), HistoryTarget.Actor(pSnapshot.actor_id));
        }

        public static void OnSlaveMerit(Actor pActor, int pPoints, int pTotal, Kingdom pKingdom, City pCity)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;
            HistoryText meritText = H("aw_hist_merit_prefix") + HistoryText.PlainText(pPoints.ToString()) +
                                    H("aw_hist_merit_mid") + HistoryText.PlainText(pTotal.ToString()) +
                                    H("aw_hist_merit_suffix");

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.SLAVE_MERIT,
                HistoryText.Actor(pActor, name) + meritText,
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.SLAVE_MERIT,
                    HistoryText.Actor(pActor, name) + H("aw_hist_slave_soldier") + meritText,
                    HistoryTarget.Actor(pActor));
        }

        public static void OnSlaveArmyFormed(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.SLAVE_ARMY_FORMED,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_slave_army_formed"));

            if (pCity?.data != null)
                HistoryWriter.RecordCity(pCity, pKingdom, CityEvent.SLAVE_ARMY_FORMED,
                    HistoryText.City(pCity, pKingdom) + H("aw_hist_slave_army_formed"));
        }

        public static void OnSlaveLaborStarted(Kingdom pKingdom, City pCity, int pSlaveCount)
        {
            if (pCity?.data != null)
                HistoryWriter.RecordCity(pCity, pKingdom, CityEvent.SLAVE_LABOR_STARTED,
                    HistoryText.City(pCity, pKingdom) + H("aw_hist_slave_labor_started_prefix") +
                    HistoryText.PlainText(pSlaveCount.ToString()) + H("aw_hist_people_count"));
        }

        public static void OnWarSlavesCaptured(Kingdom pKingdom, City pCity, string pCityName, int pSlaveCount)
        {
            if (pSlaveCount <= 0) return;
            bool hasCity = IsHistoryCityValid(pCity);
            HistoryText cityText = HistoryText.City(pCity, pKingdom,
                string.IsNullOrEmpty(pCityName) ? T("aw_unknown_city") : pCityName);
            if (pKingdom?.data != null)
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ENSLAVED,
                    HistoryText.Kingdom(pKingdom) + H("aw_hist_war_slave_capture_prefix") +
                    HistoryText.PlainText(pSlaveCount.ToString()) + H("aw_hist_war_slave_capture_mid") + cityText,
                    hasCity ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pKingdom));

            if (hasCity)
                HistoryWriter.RecordCity(pCity, pKingdom, CityEvent.ENSLAVED,
                    cityText + H("aw_hist_city_war_slave_capture_prefix") +
                    HistoryText.PlainText(pSlaveCount.ToString()) + H("aw_hist_city_war_slave_capture_suffix"));
        }

        private static bool IsHistoryCityValid(City pCity)
        {
            try { return pCity?.data != null && pCity.data.id >= 0; }
            catch { return false; }
        }

        public static void OnRoyalGuardFormed(Kingdom pKingdom, string pGuardName)
        {
            if (pKingdom?.data == null) return;
            string guardName = string.IsNullOrEmpty(pGuardName) ? T("aw_hist_royal_guard_default") : pGuardName;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.ROYAL_GUARD_FORMED,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_royal_guard_founded") + HistoryText.PlainText(guardName),
                HistoryTarget.Kingdom(pKingdom));
        }

        public static void OnRoyalGuardAppointed(Actor pActor, Kingdom pKingdom, City pCity,
            string pGuardName, bool pCaptain)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;
            string guardName = string.IsNullOrEmpty(pGuardName) ? T("aw_hist_royal_guard_default") : pGuardName;
            HistoryText role = pCaptain ? H("aw_hist_royal_guard_captain") : H("aw_hist_royal_guard_member");

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.ROYAL_GUARD_APPOINTED,
                HistoryText.Actor(pActor, name) + HistoryText.PlainText(" ") + role + HistoryText.PlainText(guardName),
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.ROYAL_GUARD_APPOINTED,
                    HistoryText.Actor(pActor, name) + H("aw_hist_royal_guard_in_city_mid") +
                    HistoryText.City(city, kingdom) + HistoryText.PlainText(" ") + role + HistoryText.PlainText(guardName),
                    HistoryTarget.Actor(pActor));
        }

        internal static void OnRoyalGuardAppointed(ChronicleActorSnapshot pSnapshot,
            string pGuardName, bool pCaptain)
        {
            if (pSnapshot == null || pSnapshot.actor_id < 0) return;
            string guardName = string.IsNullOrEmpty(pGuardName) ? T("aw_hist_royal_guard_default") : pGuardName;
            HistoryText role = pCaptain ? H("aw_hist_royal_guard_captain") : H("aw_hist_royal_guard_member");
            HistoryWriter.RecordDeferredPerson(pSnapshot.context, pSnapshot.person,
                pSnapshot.actor_id, pSnapshot.actor_name, PersonEvent.ROYAL_GUARD_APPOINTED,
                pSnapshot.ActorText() + HistoryText.PlainText(" ") + role + HistoryText.PlainText(guardName),
                ChronicleCategory.WAR, HistoryTarget.Actor(pSnapshot.actor_id));
            if (pSnapshot.city_id >= 0)
                HistoryWriter.RecordDeferredCity(pSnapshot.context, pSnapshot.city_id,
                    pSnapshot.city_name, CityEvent.ROYAL_GUARD_APPOINTED,
                    pSnapshot.ActorText() + H("aw_hist_royal_guard_in_city_mid") + pSnapshot.CityText() +
                    HistoryText.PlainText(" ") + role + HistoryText.PlainText(guardName),
                    HistoryTarget.Actor(pSnapshot.actor_id));
        }

        public static void OnRoyalGuardDismissed(Actor pActor, Kingdom pKingdom, City pCity, string pReason)
        {
            if (pActor?.data == null) return;
            string name = pActor.getName();
            Kingdom kingdom = pKingdom ?? pActor.kingdom ?? pCity?.kingdom;
            City city = pCity ?? pActor.city;
            string reason = RoyalGuardReasonLabel(pReason);

            HistoryWriter.RecordPerson(pActor.data.id, kingdom, name,
                PersonEvent.ROYAL_GUARD_DISMISSED,
                HistoryText.Actor(pActor, name) + H("aw_hist_left_royal_guard") +
                HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                ChronicleCategory.WAR,
                HistoryTarget.Actor(pActor));

            if (city?.data != null)
                HistoryWriter.RecordCity(city, kingdom, CityEvent.ROYAL_GUARD_DISMISSED,
                    HistoryText.Actor(pActor, name) + H("aw_hist_in_city") + HistoryText.City(city, kingdom) +
                    H("aw_hist_left_royal_guard") + HistoryText.PlainText(reason) + H("aw_hist_paren_close"),
                    HistoryTarget.Actor(pActor));
        }

        internal static void OnRoyalGuardDismissed(ChronicleActorSnapshot pSnapshot, string pReason)
        {
            if (pSnapshot == null || pSnapshot.actor_id < 0) return;
            string reason = RoyalGuardReasonLabel(pReason);
            HistoryWriter.RecordDeferredPerson(pSnapshot.context, pSnapshot.person,
                pSnapshot.actor_id, pSnapshot.actor_name, PersonEvent.ROYAL_GUARD_DISMISSED,
                pSnapshot.ActorText() + H("aw_hist_left_royal_guard") +
                HistoryText.PlainText(reason) + H("aw_hist_paren_close"), ChronicleCategory.WAR,
                HistoryTarget.Actor(pSnapshot.actor_id));
            if (pSnapshot.city_id >= 0)
                HistoryWriter.RecordDeferredCity(pSnapshot.context, pSnapshot.city_id,
                    pSnapshot.city_name, CityEvent.ROYAL_GUARD_DISMISSED,
                    pSnapshot.ActorText() + H("aw_hist_in_city") + pSnapshot.CityText() +
                    H("aw_hist_left_royal_guard") + HistoryText.PlainText(reason) +
                    H("aw_hist_paren_close"), HistoryTarget.Actor(pSnapshot.actor_id));
        }

        private static bool IsNationalSlaveEvent(Actor pActor)
        {
            return pActor != null && (pActor.isKing() || pActor.isCityLeader() || ChronicleGate.IsImportant(pActor));
        }

        private static string RoyalGuardReasonLabel(string pReason)
        {
            return pReason switch
            {
                "died" => T("aw_hist_guard_reason_died"),
                "no_king" => T("aw_hist_guard_reason_no_king"),
                "no_noble_captain" => T("aw_hist_guard_reason_no_noble_captain"),
                "over_limit" => T("aw_hist_guard_reason_over_limit"),
                "invalid" => T("aw_hist_guard_reason_invalid"),
                "enslaved" => T("aw_hist_guard_reason_enslaved"),
                "became_leader" => T("aw_hist_guard_reason_became_leader"),
                _ => string.IsNullOrEmpty(pReason) ? T("aw_hist_guard_reason_left") : pReason
            };
        }

        public static void OnImportantKill(Actor pKiller, Actor pDead, Kingdom pDeadPrevKingdom)
        {
            if (pKiller?.data == null || pDead?.data == null) return;
            bool deadImportant = ChronicleGate.IsImportant(pDead);

            // 凶手视角(凶手贵族才记;或被杀者是重要人物也值得给贵族凶手记)。
            if (ChronicleGate.IsNobleActor(pKiller) && (deadImportant || ChronicleGate.IsImportant(pKiller)))
            {
                string kname = pKiller.getName();
                HistoryWriter.RecordPerson(pKiller.data.id, pKiller.kingdom, kname,
                    PersonEvent.IMPORTANT_KILL,
                    HistoryText.Actor(pKiller, kname) + H("aw_hist_killed") + HistoryText.Actor(pDead),
                    ChronicleCategory.WAR);
            }

            // 被杀重要人物 → 国家史留痕。
            if (deadImportant && pDeadPrevKingdom?.data != null)
                HistoryWriter.RecordKingdom(pDeadPrevKingdom, KingdomEvent.NOTABLE_DEATH,
                    HistoryText.Actor(pDead) + H("aw_hist_was_killed_by") +
                    HistoryText.Actor(pKiller) + H("aw_hist_was_killed_by_suffix"));
        }

        // 恋爱双向去重:同一对(min_max id)本会话只记一次。
        private static readonly System.Collections.Generic.HashSet<string> _loverPairs =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>坠入爱河:双方各记一条(贵族门槛),同一对去重。</summary>
        public static void OnBecameLovers(Actor pA, Actor pB)
        {
            if (pA?.data == null || pB?.data == null) return;
            long a = pA.data.id, b = pB.data.id;
            string key = (a < b ? a + "_" + b : b + "_" + a);
            if (!_loverPairs.Add(key)) return; // 已记过这一对

            RecordLover(pA, pB);
            RecordLover(pB, pA);
        }

        private static void RecordLover(Actor pSelf, Actor pOther)
        {
            if (!ChronicleGate.IsNobleActor(pSelf)) return;
            string name = pSelf.getName();
            HistoryWriter.RecordPerson(pSelf.data.id, pSelf.kingdom, name,
                PersonEvent.FELL_IN_LOVE,
                HistoryText.Actor(pSelf, name) + H("aw_hist_fell_in_love") +
                HistoryText.Actor(pOther) + H("aw_hist_fell_in_love_suffix"),
                ChronicleCategory.BOND);
        }

        /// <summary>
        ///     牵绊离世:死者的在世父母 / 配偶 / 子女中,贵族者各记一条"痛失至亲"。
        ///     在 Die_Prefix 调用(死者数据仍完整),死者本身可平民。
        /// </summary>
        public static void OnBondDeath(Actor pDead)
        {
            if (pDead?.data == null) return;
            if (!DeathBondRules.ShouldRecordBondDeathForParentsAndLover(pDeadIsTraceable: true)) return;
            string deadName = pDead.getName();

            // 配偶
            Actor lover = pDead.hasLover() ? pDead.lover : null;
            RecordBondDeath(lover, pDead, deadName, T("aw_hist_partner"));

            // 父母(用 data 上的 parent id 取,已验证字段;避免依赖 getParents 的具体返回类型)
            RecordBondDeath(GetUnit(pDead.data.parent_id_1), pDead, deadName, T("aw_hist_relative"));
            RecordBondDeath(GetUnit(pDead.data.parent_id_2), pDead, deadName, T("aw_hist_relative"));

            // 子女
            foreach (Actor child in GetChildren(pDead))
                RecordBondDeath(child, pDead, deadName, T("aw_hist_relative"));
        }

        private static Actor GetUnit(long pId)
        {
            return pId > 0 ? World.world.units.get(pId) : null;
        }

        private static void RecordBondDeath(Actor pMourner, Actor pDead, string pDeadName, string pRelation)
        {
            if (pMourner == null || pMourner == pDead) return;
            if (pMourner.isRekt() || !pMourner.isAlive()) return; // 悼念者须在世
            if (!ChronicleGate.IsNobleActor(pMourner)) return;
            string name = pMourner.getName();
            HistoryWriter.RecordPerson(pMourner.data.id, pMourner.kingdom, name,
                PersonEvent.BOND_DEATH,
                HistoryText.Actor(pMourner, name) + H("aw_hist_lost_bond") +
                HistoryText.PlainText(pRelation + " ") + HistoryText.Actor(pDead, pDeadName),
                ChronicleCategory.BOND);
        }

        // 取子女:遍历死者所在世界单位,找 parent 是死者的(数量小,死亡时一次性)。
        private static System.Collections.Generic.IEnumerable<Actor> GetChildren(Actor pParent)
        {
            var result = new System.Collections.Generic.List<Actor>();
            if (pParent?.data == null) return result;

            bool actorChildrenAvailable = TryCollectActorChildren(pParent, result);
            if (!DeathBondRules.ShouldUseWorldScanForChildren(
                    pCanUseActorChildrenList: actorChildrenAvailable,
                    pDeadIsImportant: IsImportantForBondDeathChildFallback(pParent)))
                return result;

            Bench.bench(CityMaintenanceBenchmarkRules.DeathBondChildScan, CityMaintenanceBenchmarkRules.Group);
            long pid = pParent.data.id;
            foreach (Actor a in World.world.units)
            {
                if (a?.data == null || a == pParent) continue;
                if (a.data.parent_id_1 == pid || a.data.parent_id_2 == pid) result.Add(a);
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.DeathBondChildScan, CityMaintenanceBenchmarkRules.Group);
            return result;
        }

        private static bool TryCollectActorChildren(Actor pParent, System.Collections.Generic.List<Actor> pResult)
        {
            if (pParent?.data == null || pResult == null) return false;
            try
            {
                foreach (Actor child in pParent.getChildren(pOnlyCurrentFamily: false))
                {
                    if (child?.data == null || child == pParent) continue;
                    pResult.Add(child);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsImportantForBondDeathChildFallback(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (ChronicleGate.IsImportant(pActor) || ChronicleGate.IsNobleActor(pActor)) return true;
            try { return pActor.isArmyGroupLeader(); }
            catch { return false; }
        }
    }
}
