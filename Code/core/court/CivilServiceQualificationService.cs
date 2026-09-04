using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    /// <summary>
    ///     一名候选的任职资历,两级一次读出。
    ///
    ///     <see cref="CivilServiceQualificationService.HasRequiredServiceHistory"/>
    ///     的 SQL **根本没用到 requiredOfficeGrade** —— 它取该 actor 的任职历史,
    ///     再在 C# 里按级筛。严格通道每名候选要问 grade 30 和 grade 20 两次,
    ///     等于把同一条查询发两遍。
    /// </summary>
    internal readonly struct CourtServiceHistory
    {
        internal CourtServiceHistory(bool pHasLowerService,
            bool pHasMiddleService)
        {
            HasLowerService = pHasLowerService;
            HasMiddleService = pHasMiddleService;
        }

        internal bool HasLowerService { get; }
        internal bool HasMiddleService { get; }
    }

    internal static class CivilServiceQualificationService
    {
        internal const string TechnologyId =
            "aw_tech_civil_service_examination";

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool _rebuildPending;
        private static long _rebuildAfterCandidateId;

        public static bool HasExaminationSystem(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   KingdomPolicyService.IsCompleted(pKingdom,
                       PolicyNodeKind.Tech, TechnologyId);
        }

        public static void ClearRuntime()
        {
            _rebuildPending = false;
            _rebuildAfterCandidateId = 0L;
        }

        public static void RebuildRuntimeProjections()
        {
            _rebuildAfterCandidateId = 0L;
            _rebuildPending = DB != null;
        }

        public static void ProcessRuntimeRebuild()
        {
            if (!_rebuildPending || DB == null) return;
            List<CivilServiceQualificationRecord> page =
                CivilServiceExamPersistence.LoadLatestQualificationsPage(DB,
                    _rebuildAfterCandidateId,
                    CivilServiceExamRules.AuthorityCandidateBudget);
            if (page.Count == 0)
            {
                _rebuildPending = false;
                return;
            }

            foreach (CivilServiceQualificationRecord record in page)
            {
                _rebuildAfterCandidateId = Math.Max(_rebuildAfterCandidateId,
                    record.CandidateId);
                Actor actor;
                Kingdom kingdom;
                try
                {
                    actor = World.world?.units?.get(record.ActorId);
                    kingdom = World.world?.kingdoms?.get(record.KingdomId);
                }
                catch
                {
                    actor = null;
                    kingdom = null;
                }
                if (actor?.data == null || kingdom?.data == null ||
                    actor.kingdom != kingdom || !actor.isAlive() ||
                    actor.isRekt()) continue;
                actor.data.get(LineageKeys.CIVIL_SERVICE_QUALIFICATION,
                    out string previousQualification, "none");
                Project(actor, record);
                actor.data.get(LineageKeys.CIVIL_SERVICE_QUALIFICATION,
                    out string currentQualification, "none");
                if (!string.Equals(previousQualification,
                        currentQualification, StringComparison.OrdinalIgnoreCase))
                    CourtVacancyReconciliationService.CandidatePoolChanged(
                        kingdom);
            }
        }

        public static bool CanReceiveFormalCivilAppointment(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId,
            bool pAllowVacancyPromotion = false,
            CivilServiceQualificationRecord pQualification = null,
            bool pQualificationsCaptured = false,
            bool pAllowLocalLowerQualification = false,
            City pCity = null,
            CourtCandidateSession pServiceHistorySession = null,
            CourtAppointmentContext pContext = default)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            // 继承人不得出任官员，避免即位前已被任命导致卸任延迟或继承人身份混乱。
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (HistoricalFigureCardIdentityService.IsMinisterCardActor(pActor))
            {
                pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                    out string currentOffice, "");
                bool safeMinister = pActor.kingdom == pKingdom &&
                    pActor.isAlive() && !pActor.isRekt() && !pActor.isKing() &&
                    !pActor.isCityLeader() && !SlaveService.IsSlave(pActor) &&
                    string.IsNullOrEmpty(currentOffice);
                if (safeMinister) return true;
            }
            // 与候选人无关的四问由调用方在循环外算好传进来。没传就现算,
            // 单点调用的老路径行为不变。
            bool examinationSystem = pContext.Valid
                ? pContext.ExaminationSystem
                : HasExaminationSystem(pKingdom);
            bool nineRankSystem = pContext.Valid
                ? pContext.NineRankSystem
                : CourtService.HasNineRankSystem(pKingdom);
            if (!examinationSystem && !nineRankSystem) return true;
            if (IsAppointmentExempt(pActor, pKingdom, pLayer, pOfficeId))
                return true;
            int officeGrade = pContext.Valid
                ? pContext.OfficeGrade
                : OfficialCareerStateService.OfficeGradeForOffice(
                    pKingdom, pLayer, pOfficeId, pCity);
            bool regionalGovernor = pContext.Valid
                ? pContext.RegionalGovernor
                : OfficialCareerStateService.IsRegionalGovernorSeat(
                    pKingdom, pLayer, pOfficeId, pCity);
            bool countyLayer = pLayer == CourtOfficeLayer.County;
            bool localLayer = pLayer == CourtOfficeLayer.City || countyLayer;
            bool localLeaderQualificationBypass = pAllowLocalLowerQualification &&
                localLayer && pActor.isCityLeader();
            int currentRank = OfficialCareerStateService.ReadRankFast(pActor);
            bool hasCareerRank = nineRankSystem && currentRank >
                OfficialCareerRankRules.Unranked;
            if (!hasCareerRank && !HistoricalSchoolEducationService.CanAppoint(
                    pActor, pKingdom, pLayer, pOfficeId) &&
                !localLeaderQualificationBypass) return false;
            bool allowUnqualifiedLocalFallback =
                LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback(
                    localLayer, officeGrade,
                    pAllowVacancyPromotion &&
                    pAllowLocalLowerQualification);
            // 补缺时,这三个分支任意一个成立就已经让 appointmentQualificationEligible
            // 为真,而函数末尾在 pAllowVacancyPromotion 为真时返回的正是
            // ShouldUseVacancyFallback(officeVacant, false, eligible) —— 即 true。
            // 也就是说下面那些查询一个都改不了结果。县令补缺(officeGrade 30 +
            // 空缺晋升)全程走 allowUnqualifiedLocalFallback,这条提前返回把它
            // 每名候选约 5 次 SQLite 查询直接降到 0。
            if (pAllowVacancyPromotion &&
                (hasCareerRank || allowUnqualifiedLocalFallback ||
                 localLeaderQualificationBypass)) return true;
            CivilServiceQualificationRecord qualification = examinationSystem
                ? pQualificationsCaptured
                    ? pQualification
                    : LoadOrRepair(pActor, pKingdom)
                : null;
            // 没有科举制时 hasFormalQualification 恒为真,
            // AcceptsAppointmentQualification 根本不会被调用 —— 这个 JOIN 查询
            // 的结果无人使用,不能为每名候选白付一次。
            bool higherStageFailure = examinationSystem &&
                pAllowLocalLowerQualification &&
                HasFailedHigherStage(pActor, pKingdom);
            bool hasFormalQualification = !examinationSystem ||
                LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                    qualification?.Qualification ?? "none",
                    higherStageFailure, pAllowLocalLowerQualification);
            bool hasLegacyCredential = !hasFormalQualification &&
                examinationSystem &&
                CivilServiceLegacyTransitionService.HasUsableCredential(
                    pActor, pKingdom, pLayer, pOfficeId);
            bool appointmentQualificationEligible = hasFormalQualification ||
                hasLegacyCredential || hasCareerRank ||
                allowUnqualifiedLocalFallback || localLeaderQualificationBypass;
            if (!appointmentQualificationEligible)
                return false;
            // 同上:末尾是 strictEligible || (pAllowVacancyPromotion && eligible)。
            // 后半已经成立时,strictEligible 的取值无关紧要,而算它要两次
            // HasRequiredServiceHistory(每次一条最多 64 行的查询)。
            if (pAllowVacancyPromotion) return true;

            if (currentRank <= OfficialCareerRankRules.Unranked)
                currentRank = localLayer
                    ? OfficialCareerRankRules.ResolveInitialLocalAppointmentRank(
                        OfficialCareerRankRules.Unranked, officeGrade,
                        hasNineRankSystem: true, hasFormalQualification: true,
                        qualification?.EntryBonus ?? 0, regionalGovernor)
                    : OfficialCareerRankRules.ResolveInitialAppointmentRank(
                        OfficialCareerRankRules.Unranked, officeGrade,
                        hasNineRankSystem: true, hasFormalQualification: true,
                        qualification?.EntryBonus ?? 0);
            // 严格通道现在真的会跑(局部层补回了 strict-first),所以这两问必须
            // 能由调用方按轮记忆喂进来 —— 否则每名候选两条 SQL。
            //
            // 收的是 session 而不是**算好的值**:上面 146 / 176 两处提前返回在
            // pAllowVacancyPromotion 为真时必定命中,这一行根本到不了。参数在
            // C# 里是**先算后传**的,所以传 pSession.ServiceHistory(actor, ...)
            // 等于给兜底通道的每名候选白发一条 SQL —— 而兜底池是两三千人。
            // 改前的兜底通道一条都不发,这是净新增的开销。
            CourtServiceHistory serviceHistory =
                pServiceHistorySession?.ServiceHistory(pActor, pKingdom) ??
                LoadServiceHistory(pActor, pKingdom);
            bool hasLowerService = serviceHistory.HasLowerService;
            bool hasMiddleService = serviceHistory.HasMiddleService;
            pActor.data.get(LineageKeys.OFFICER_LAST_KAOKE,
                out int evaluation, -1);
            bool passingEvaluation = evaluation >= 0 && evaluation <= 2;
            bool rankEligible = localLayer
                ? currentRank >= OfficialCareerRankRules.
                    RequiredRankForLocalOfficeGrade(officeGrade,
                        regionalGovernor)
                : currentRank >= OfficialCareerRankRules.
                    RequiredRankForOfficeGrade(officeGrade);
            bool strictEligible = rankEligible &&
                OfficialCareerRankRules.CanEnterOffice(currentRank,
                    officeGrade, hasLowerService, hasMiddleService,
                    passingEvaluation);
            if (strictEligible) return true;
            return CivilServiceExamRules.ShouldUseVacancyFallback(
                officeVacant: pAllowVacancyPromotion,
                strictCandidateFound: false,
                appointmentQualificationEligible);
        }

        internal static Dictionary<long, CivilServiceQualificationRecord>
            CaptureManualAppointmentQualifications(Kingdom pKingdom,
                IReadOnlyList<long> pActorIds)
        {
            if (pKingdom?.data == null || pActorIds == null ||
                pActorIds.Count == 0)
                return new Dictionary<long, CivilServiceQualificationRecord>();
            return CivilServiceExamPersistence.LoadLatestQualificationsForActors(
                DB, pKingdom.id, pActorIds);
        }

        internal static bool HasFormalQualification(Actor pActor,
            Kingdom pKingdom)
        {
            CivilServiceQualificationRecord qualification = LoadOrRepair(
                pActor, pKingdom);
            return qualification != null &&
                   CivilServiceExamRules.IsFormalAppointmentQualification(
                       ParseQualification(qualification.Qualification));
        }

        public static CivilServiceQualificationRecord LoadOrRepair(
            Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return null;
            pActor.data.get(LineageKeys.CIVIL_SERVICE_QUALIFICATION,
                out string qualification, "none");
            pActor.data.get(LineageKeys.CIVIL_SERVICE_ISSUING_KINGDOM_ID,
                out long issuingKingdomId, -1L);
            pActor.data.get(LineageKeys.CIVIL_SERVICE_SESSION_ID,
                out long sessionId, -1L);
            pActor.data.get(LineageKeys.CIVIL_SERVICE_RESULT_YEAR,
                out int resultYear, -1);
            pActor.data.get(LineageKeys.CIVIL_SERVICE_ENTRY_BONUS,
                out int entryBonus, 0);
            if (issuingKingdomId == pKingdom.id && sessionId >= 0L &&
                resultYear >= 0 &&
                ParseQualification(qualification) !=
                CivilServiceQualification.None)
            {
                return new CivilServiceQualificationRecord
                {
                    ActorId = pActor.data.id,
                    KingdomId = issuingKingdomId,
                    SessionId = sessionId,
                    Qualification = qualification,
                    ResultYear = resultYear,
                    EntryBonus = entryBonus
                };
            }

            CivilServiceQualificationRecord repaired =
                CivilServiceExamPersistence.LoadLatestQualification(DB,
                    pActor.data.id, pKingdom.id);
            Project(pActor, repaired);
            return repaired;
        }

        public static bool HasRequiredServiceHistory(Actor pActor,
            Kingdom pKingdom, int requiredOfficeGrade)
        {
            CourtServiceHistory history = LoadServiceHistory(pActor, pKingdom);
            return requiredOfficeGrade == 20
                ? history.HasMiddleService
                : history.HasLowerService;
        }

        /// <summary>
        ///     一条查询同时回答 grade 30 与 grade 20 两问。严格通道对每名候选
        ///     都要问这两级,而底层 SQL 对两者完全相同。
        /// </summary>
        internal static CourtServiceHistory LoadServiceHistory(Actor pActor,
            Kingdom pKingdom)
        {
            bool lower = false;
            bool middle = false;
            if (DB == null || pActor?.data == null ||
                pKingdom?.data == null)
                return new CourtServiceHistory(false, false);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT OFFICE_ID,LAYER,CITY_ID FROM " +
                    CourtOfficerTableItem.GetTableName() +
                    " WHERE ACTOR_ID=@actor AND KINGDOM_ID=@kingdom " +
                    "AND IFNULL(IS_ACTING,0)=0 " +
                    "ORDER BY APPOINTED_TIME DESC,OFFICER_ID DESC LIMIT 64";
                command.Parameters.AddWithValue("@actor", pActor.data.id);
                command.Parameters.AddWithValue("@kingdom", pKingdom.id);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string office = reader.IsDBNull(0)
                        ? ""
                        : Convert.ToString(reader.GetValue(0)) ?? "";
                    string layer = reader.IsDBNull(1)
                        ? ""
                        : Convert.ToString(reader.GetValue(1)) ?? "";
                    long cityId = reader.IsDBNull(2)
                        ? -1L
                        : Convert.ToInt64(reader.GetValue(2));
                    City city = cityId < 0
                        ? null
                        : World.world?.cities?.get(cityId);
                    int grade = OfficialCareerStateService.OfficeGradeForOffice(
                        pKingdom, layer, office, city);
                    if (!lower && OfficialCareerRankRules.
                            IsRequiredServiceGrade(grade, 30)) lower = true;
                    if (!middle && OfficialCareerRankRules.
                            IsRequiredServiceGrade(grade, 20)) middle = true;
                    if (lower && middle) break;
                }
            }
            catch { }
            return new CourtServiceHistory(lower, middle);
        }

        public static bool IsAppointmentExempt(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId)
        {
            if (pActor?.data == null) return false;
            if (pLayer == CourtOfficeLayer.Military ||
                pOfficeId == CourtOfficeId.SiMa ||
                pOfficeId == CourtOfficeId.Marshal ||
                pOfficeId == CourtOfficeId.Bingbu ||
                pOfficeId == CourtPyramidRoleId.General) return true;
            if (pActor.isKing() ||
                HeirService.PeekRegisteredHeir(pKingdom) == pActor ||
                FeudatoryService.IsActivePrince(pActor) ||
                HistoricalSchoolDescentService.IsCanonicalMaster(pActor))
                return true;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long currentKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER,
                out string currentLayer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string currentOffice, "");
            return currentKingdomId == pKingdom?.id &&
                   currentLayer == (pLayer ?? "") &&
                   currentOffice == (pOfficeId ?? "");
        }

        internal static bool HasFailedHigherStage(Actor pActor,
            Kingdom pKingdom)
        {
            if (DB == null || pActor?.data == null ||
                pKingdom?.data == null) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT 1 FROM " +
                    CivilServiceExamCandidateTableItem.GetTableName() +
                    " C JOIN " +
                    CivilServiceExamSessionTableItem.GetTableName() +
                    " S ON S.ID=C.SESSION_ID WHERE C.ACTOR_ID=@actor " +
                    "AND C.KINGDOM_ID=@kingdom AND S.KINGDOM_ID=@kingdom " +
                    "AND S.STATUS='completed' AND (" +
                    "C.METROPOLITAN_RESULT='failed' OR " +
                    "C.PALACE_RESULT='failed' OR " +
                    "C.NATIONAL_RESULT='failed') " +
                    "ORDER BY S.CYCLE_YEAR DESC,C.ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@actor", pActor.data.id);
                command.Parameters.AddWithValue("@kingdom", pKingdom.id);
                return command.ExecuteScalar() != null;
            }
            catch { return false; }
        }

        private static void Project(Actor pActor,
            CivilServiceQualificationRecord pQualification)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.CIVIL_SERVICE_QUALIFICATION,
                pQualification?.Qualification ?? "none");
            pActor.data.set(LineageKeys.CIVIL_SERVICE_ISSUING_KINGDOM_ID,
                pQualification?.KingdomId ?? -1L);
            pActor.data.set(LineageKeys.CIVIL_SERVICE_SESSION_ID,
                pQualification?.SessionId ?? -1L);
            pActor.data.set(LineageKeys.CIVIL_SERVICE_RESULT_YEAR,
                pQualification?.ResultYear ?? -1);
            pActor.data.set(LineageKeys.CIVIL_SERVICE_ENTRY_BONUS,
                pQualification?.EntryBonus ?? 0);
        }

        private static CivilServiceQualification ParseQualification(
            string pValue)
        {
            if (string.Equals(pValue, "jinshi",
                    StringComparison.OrdinalIgnoreCase))
                return CivilServiceQualification.Jinshi;
            if (string.Equals(pValue, "gongshi",
                    StringComparison.OrdinalIgnoreCase))
                return CivilServiceQualification.Gongshi;
            if (string.Equals(pValue, "juren",
                    StringComparison.OrdinalIgnoreCase))
                return CivilServiceQualification.Juren;
            return CivilServiceQualification.None;
        }
    }
}
