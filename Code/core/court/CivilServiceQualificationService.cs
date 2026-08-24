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
                Project(actor, record);
            }
        }

        public static bool CanReceiveFormalCivilAppointment(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId,
            bool pAllowVacancyPromotion = false,
            CivilServiceQualificationRecord pQualification = null,
            bool pQualificationsCaptured = false,
            bool pAllowLocalLowerQualification = false,
            City pCity = null)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            bool examinationSystem = HasExaminationSystem(pKingdom);
            bool nineRankSystem = CourtService.HasNineRankSystem(pKingdom);
            if (!examinationSystem && !nineRankSystem) return true;
            if (IsAppointmentExempt(pActor, pKingdom, pLayer, pOfficeId))
                return true;
            int officeGrade = OfficialCareerStateService.OfficeGradeForOffice(
                pKingdom, pLayer, pOfficeId, pCity);
            bool regionalGovernor = OfficialCareerStateService.
                IsRegionalGovernorSeat(pKingdom, pLayer, pOfficeId, pCity);
            bool localLeaderQualificationBypass = pAllowLocalLowerQualification &&
                pLayer == CourtOfficeLayer.City && pActor.isCityLeader();
            int currentRank = OfficialCareerStateService.ReadRankFast(pActor);
            bool hasCareerRank = nineRankSystem && currentRank >
                OfficialCareerRankRules.Unranked;
            if (!hasCareerRank && !HistoricalSchoolEducationService.CanAppoint(
                    pActor, pKingdom, pLayer, pOfficeId) &&
                !localLeaderQualificationBypass) return false;
            bool allowUnqualifiedLocalFallback =
                LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback(
                    pLayer == CourtOfficeLayer.City, officeGrade,
                    pAllowVacancyPromotion &&
                    pAllowLocalLowerQualification);
            CivilServiceQualificationRecord qualification = examinationSystem
                ? pQualificationsCaptured
                    ? pQualification
                    : LoadOrRepair(pActor, pKingdom)
                : null;
            bool higherStageFailure = pAllowLocalLowerQualification &&
                HasFailedHigherStage(pActor, pKingdom);
            bool hasFormalQualification = !examinationSystem ||
                LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                    qualification?.Qualification ?? "none",
                    higherStageFailure, pAllowLocalLowerQualification);
            bool hasLegacyCredential = examinationSystem &&
                CivilServiceLegacyTransitionService.HasUsableCredential(
                    pActor, pKingdom, pLayer, pOfficeId);
            bool appointmentQualificationEligible = hasFormalQualification ||
                hasLegacyCredential || hasCareerRank ||
                allowUnqualifiedLocalFallback || localLeaderQualificationBypass;
            if (!appointmentQualificationEligible)
                return false;

            if (currentRank <= OfficialCareerRankRules.Unranked)
                currentRank = pLayer == CourtOfficeLayer.City
                    ? OfficialCareerRankRules.ResolveInitialLocalAppointmentRank(
                        OfficialCareerRankRules.Unranked, officeGrade,
                        hasNineRankSystem: true, hasFormalQualification: true,
                        qualification?.EntryBonus ?? 0, regionalGovernor)
                    : OfficialCareerRankRules.ResolveInitialAppointmentRank(
                        OfficialCareerRankRules.Unranked, officeGrade,
                        hasNineRankSystem: true, hasFormalQualification: true,
                        qualification?.EntryBonus ?? 0);
            bool hasLowerService = HasRequiredServiceHistory(pActor,
                pKingdom, requiredOfficeGrade: 30);
            bool hasMiddleService = HasRequiredServiceHistory(pActor,
                pKingdom, requiredOfficeGrade: 20);
            pActor.data.get(LineageKeys.OFFICER_LAST_KAOKE,
                out int evaluation, -1);
            bool passingEvaluation = evaluation >= 0 && evaluation <= 2;
            bool rankEligible = pLayer == CourtOfficeLayer.City
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
            if (DB == null || pActor?.data == null ||
                pKingdom?.data == null) return false;
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
                    if (OfficialCareerRankRules.IsRequiredServiceGrade(
                            grade, requiredOfficeGrade)) return true;
                }
            }
            catch { }
            return false;
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
