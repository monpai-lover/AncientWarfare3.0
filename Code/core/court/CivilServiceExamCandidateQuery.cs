using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal static class CivilServiceExamCandidateQuery
    {
        private const int MaxHostCityQueryCount = 128;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public static List<CivilServiceExamCandidateRecord> Build(
            Kingdom pKingdom, long pSessionId, int pYear,
            CivilServiceExamMode pMode)
        {
            var result = new List<CivilServiceExamCandidateRecord>(
                CivilServiceExamRules.CandidateLimit);
            if (DB == null || pKingdom?.data == null || pSessionId < 0L)
                return result;

            IReadOnlyList<long> actorIds = LoadIndexedLocalActorIds(
                pKingdom.id, pYear, pMode);
            var localFacts = new List<CivilServiceExamCandidateFacts>(
                CivilServiceExamRules.CandidateSourceLimit);
            var foreignFacts = new List<CivilServiceExamCandidateFacts>(
                CivilServiceExamRules.CandidateSourceLimit);
            var recordsByActorId =
                new Dictionary<long, CivilServiceExamCandidateRecord>();
            var seen = new HashSet<long>();
            double updatedTime = LineageService.CurTime();
            foreach (long actorId in actorIds)
            {
                if (localFacts.Count >=
                    CivilServiceExamRules.CandidateSourceLimit)
                    break;
                if (!seen.Add(actorId)) continue;
                if (!TryBuildCandidate(actorId, pKingdom, pSessionId,
                        pYear, pMode, pForeignResident: false, updatedTime,
                        out CivilServiceExamCandidateRecord record,
                        out CivilServiceExamCandidateFacts candidateFacts))
                    continue;
                recordsByActorId[actorId] = record;
                localFacts.Add(candidateFacts);
            }

            foreach (long actorId in LoadForeignResidentActorIds(pKingdom,
                         pYear, CivilServiceExamRules.CandidateSourceLimit))
            {
                if (foreignFacts.Count >=
                    CivilServiceExamRules.CandidateSourceLimit) break;
                if (!seen.Add(actorId)) continue;
                if (!TryBuildCandidate(actorId, pKingdom, pSessionId,
                        pYear, pMode, pForeignResident: true, updatedTime,
                        out CivilServiceExamCandidateRecord record,
                        out CivilServiceExamCandidateFacts candidateFacts))
                    continue;
                recordsByActorId[actorId] = record;
                foreignFacts.Add(candidateFacts);
            }

            IReadOnlyList<CivilServiceExamCandidateFacts> selected =
                CivilServiceExamRules.SelectCandidatesWithLocalPriority(
                    localFacts, foreignFacts,
                    CivilServiceExamRules.CandidateLimit);
            foreach (CivilServiceExamCandidateFacts candidate in selected)
                if (recordsByActorId.TryGetValue(candidate.ActorId,
                        out CivilServiceExamCandidateRecord record))
                    result.Add(record);
            return result;
        }

        public static int CountEligibleForInvitation(Kingdom pKingdom,
            int pYear, int pLimit)
        {
            if (DB == null || pKingdom?.data == null || pLimit <= 0)
                return 0;
            int limit = Math.Min(CivilServiceExamRules.CandidateLimit,
                pLimit);
            CivilServiceExamMode mode = CivilServiceExamRules.ResolveMode(
                MandateService.IsMandateKingdom(pKingdom),
                KingdomTitleService.IsEmperor(pKingdom));
            IReadOnlyList<long> actorIds = LoadIndexedLocalActorIds(
                pKingdom.id, pYear, mode);
            var seen = new HashSet<long>();
            int count = CountEligible(actorIds, seen, pKingdom, pYear, mode,
                pForeignResident: false, limit);
            if (count >= limit) return count;
            return count + CountEligible(LoadForeignResidentActorIds(pKingdom,
                    pYear,
                    CivilServiceExamRules.ForeignInvitationSourceLimit), seen,
                pKingdom, pYear, mode, pForeignResident: true,
                limit - count);
        }

        public static int CountEligibleLocalForExamPipeline(Kingdom pKingdom,
            int pYear, int pLimit)
        {
            if (DB == null || pKingdom?.data == null || pLimit <= 0)
                return 0;
            int limit = Math.Min(CivilServiceExamRules.CandidateLimit,
                pLimit);
            CivilServiceExamMode mode = CivilServiceExamRules.ResolveMode(
                MandateService.IsMandateKingdom(pKingdom),
                KingdomTitleService.IsEmperor(pKingdom));
            IReadOnlyList<long> actorIds = LoadIndexedLocalActorIds(
                pKingdom.id, pYear, mode);
            return CountEligible(actorIds, new HashSet<long>(), pKingdom,
                pYear, mode, pForeignResident: false, limit);
        }

        public static List<long> LoadForeignInvitationActorIds(
            Kingdom pKingdom, int pYear, int pLimit)
        {
            var result = new List<long>();
            if (DB == null || pKingdom?.data == null || pLimit <= 0)
                return result;
            int limit = Math.Min(
                CivilServiceExamRules.ForeignInvitationSourceLimit, pLimit);
            CivilServiceExamMode mode = CivilServiceExamRules.ResolveMode(
                MandateService.IsMandateKingdom(pKingdom),
                KingdomTitleService.IsEmperor(pKingdom));
            foreach (long actorId in LoadIndexedForeignInvitationActorIds(
                         pKingdom.id, pYear,
                         CivilServiceExamRules.ForeignInvitationSourceLimit))
            {
                Actor actor = FindActor(actorId);
                if (!IsEligibleForInvitation(actor, pKingdom, pYear, mode))
                    continue;
                result.Add(actorId);
                if (result.Count >= limit) break;
            }
            return result;
        }

        public static List<long> LoadQualifiedForeignResidentActorIds(
            Kingdom pKingdom, int pLimit)
        {
            var result = new List<long>();
            if (DB == null || pKingdom?.data == null || pLimit <= 0)
                return result;
            int limit = Math.Min(CivilServiceExamRules.CandidateSourceLimit,
                pLimit);
            int year = Date.getCurrentYear();
            List<long> cityIds = HostCityIds(pKingdom);
            List<long> qualifiedActorIds =
                CivilServiceForeignResidentQualificationQuery.Load(DB,
                    SchoolMembershipTableItem.GetTableName(),
                    ActorArchiveTableItem.GetTableName(),
                    SchoolAffiliationTableItem.GetTableName(),
                    CourtOfficerTableItem.GetTableName(),
                    CivilServiceExamCandidateTableItem.GetTableName(),
                    CivilServiceExamSessionTableItem.GetTableName(), cityIds,
                    pKingdom.id, year,
                    HistoricalSchoolLifecycleState.Resident.ToString(),
                    CivilServiceExamRules.CandidateSourceLimit);
            foreach (long actorId in qualifiedActorIds)
            {
                Actor actor = FindActor(actorId);
                if (!IsEligibleForeignResident(actor, pKingdom, year) ||
                    !HasHostIssuedQualification(actor, pKingdom)) continue;
                result.Add(actorId);
                if (result.Count >= limit) break;
            }
            return result;
        }

        public static bool HasHostIssuedQualification(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            CivilServiceQualificationRecord qualification =
                CivilServiceQualificationService.LoadOrRepair(pActor,
                    pKingdom);
            return qualification?.KingdomId == pKingdom.id &&
                   CivilServiceExamRules.IsFormalAppointmentQualification(
                       ParseQualification(qualification.Qualification));
        }

        public static int CountEducatedWithoutQualification(
            Kingdom pKingdom, int pLimit)
        {
            if (DB == null || pKingdom?.data == null || pLimit <= 0)
                return 0;
            int limit = Math.Min(64, pLimit);
            try
            {
                string memberships = SchoolMembershipTableItem.GetTableName();
                string archives = ActorArchiveTableItem.GetTableName();
                string candidates = CivilServiceExamCandidateTableItem.
                    GetTableName();
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT COUNT(*) FROM (SELECT DISTINCT M.ACTOR_ID FROM " +
                    memberships + " M JOIN " + archives +
                    " A ON A.ID=M.ACTOR_ID WHERE M.ACTIVE=1 AND " +
                    "M.START_YEAR<@year AND A.IS_ALIVE=1 AND A.SEX=0 AND " +
                    "A.KINGDOM_ID=@kingdom AND NOT EXISTS (SELECT 1 FROM " +
                    candidates + " C WHERE C.ACTOR_ID=M.ACTOR_ID AND " +
                    "C.KINGDOM_ID=@kingdom AND C.QUALIFICATION IN " +
                    "('juren','gongshi','jinshi')) ORDER BY M.ACTOR_ID " +
                    "LIMIT @limit)";
                command.Parameters.AddWithValue("@year", Date.getCurrentYear());
                command.Parameters.AddWithValue("@kingdom", pKingdom.id);
                command.Parameters.AddWithValue("@limit", limit);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? 0
                    : Math.Min(limit, Convert.ToInt32(value));
            }
            catch { return 0; }
        }

        private static List<long> LoadIndexedActorIds(long pKingdomId,
            string pSocialOrigin, int pYear, CivilServiceExamMode pMode)
        {
            return CivilServiceExamCandidatePoolQuery.LoadLocal(DB,
                SchoolMembershipTableItem.GetTableName(),
                ActorArchiveTableItem.GetTableName(),
                CourtOfficerTableItem.GetTableName(),
                OfficialCareerStateTableItem.GetTableName(),
                SchoolInstitutionTableItem.GetTableName(),
                CivilServiceExamCandidateTableItem.GetTableName(),
                CivilServiceExamSessionTableItem.GetTableName(), pKingdomId,
                pSocialOrigin ?? CivilServiceExamRules.CommonerOrigin,
                pYear, pMode == CivilServiceExamMode.Tribute,
                CivilServiceExamRules.CandidateSourceLimit);
        }

        private static IReadOnlyList<long> LoadIndexedLocalActorIds(
            long pKingdomId, int pYear, CivilServiceExamMode pMode)
        {
            IReadOnlyList<long>[] sources =
            {
                LoadIndexedActorIds(pKingdomId,
                    CivilServiceExamRules.NobleOrigin, pYear, pMode),
                LoadIndexedActorIds(pKingdomId,
                    CivilServiceExamRules.DeclinedNobleOrigin, pYear, pMode),
                LoadIndexedActorIds(pKingdomId,
                    CivilServiceExamRules.CommonerOrigin, pYear, pMode)
            };
            return CivilServiceExamRules.InterleaveCandidateSources(sources,
                CivilServiceExamRules.CandidateSourceLimit);
        }

        private static List<long> LoadForeignResidentActorIds(
            Kingdom pKingdom, int pYear, int pLimit)
        {
            if (DB == null || pKingdom?.data == null || pLimit <= 0)
                return new List<long>();
            List<long> cityIds = HostCityIds(pKingdom);
            CivilServiceExamMode mode = CivilServiceExamRules.ResolveMode(
                MandateService.IsMandateKingdom(pKingdom),
                KingdomTitleService.IsEmperor(pKingdom));
            return CivilServiceExamCandidatePoolQuery.LoadForeignResidents(
                DB, SchoolMembershipTableItem.GetTableName(),
                ActorArchiveTableItem.GetTableName(),
                SchoolAffiliationTableItem.GetTableName(),
                CourtOfficerTableItem.GetTableName(),
                CivilServiceExamCandidateTableItem.GetTableName(),
                CivilServiceExamSessionTableItem.GetTableName(), cityIds,
                pKingdom.id, pYear,
                HistoricalSchoolLifecycleState.Resident.ToString(),
                mode == CivilServiceExamMode.Tribute,
                Math.Min(CivilServiceExamRules.CandidateSourceLimit, pLimit));
        }

        private static List<long> LoadIndexedForeignInvitationActorIds(
            long pKingdomId, int pYear, int pLimit)
        {
            var result = new List<long>();
            if (DB == null || pLimit <= 0) return result;
            try
            {
                string memberships = SchoolMembershipTableItem.GetTableName();
                string archives = ActorArchiveTableItem.GetTableName();
                string affiliations = SchoolAffiliationTableItem.GetTableName();
                string officers = CourtOfficerTableItem.GetTableName();
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT R.ACTOR_ID FROM " + affiliations + " R " +
                    "JOIN " + memberships + " M ON M.ACTOR_ID=R.ACTOR_ID " +
                    "JOIN " + archives + " A ON A.ID=R.ACTOR_ID " +
                    "WHERE M.ACTIVE=1 AND M.START_YEAR<@year AND " +
                    "A.IS_ALIVE=1 AND A.SEX=0 AND " +
                    "R.HOME_KINGDOM_ID<>@kingdom AND " +
                    "R.SERVICE_KINGDOM_ID<0 AND R.LIFECYCLE_STATE IN " +
                    "(@home,@resident) AND NOT EXISTS (SELECT 1 FROM " +
                    officers + " O WHERE O.ACTOR_ID=R.ACTOR_ID AND " +
                    "O.ACTIVE=1) ORDER BY R.LIFECYCLE_STATE," +
                    "R.SERVICE_KINGDOM_ID,R.HOME_KINGDOM_ID," +
                    "R.ACTOR_ID LIMIT @limit";
                command.Parameters.AddWithValue("@year", pYear);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@home",
                    HistoricalSchoolLifecycleState.AtHome.ToString());
                command.Parameters.AddWithValue("@resident",
                    HistoricalSchoolLifecycleState.Resident.ToString());
                command.Parameters.AddWithValue("@limit", Math.Min(
                    CivilServiceExamRules.ForeignInvitationSourceLimit,
                    pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read() && result.Count < pLimit)
                    result.Add(Convert.ToInt64(reader.GetValue(0)));
            }
            catch { result.Clear(); }
            return result;
        }

        private static bool TryBuildCandidate(long pActorId,
            Kingdom pKingdom, long pSessionId, int pYear,
            CivilServiceExamMode pMode, bool pForeignResident,
            double pUpdatedTime,
            out CivilServiceExamCandidateRecord pRecord,
            out CivilServiceExamCandidateFacts pFacts)
        {
            pRecord = null;
            pFacts = null;
            Actor actor = FindActor(pActorId);
            if (actor?.data == null || !actor.isSexMale()) return false;
            bool eligible = pForeignResident
                ? IsEligibleForeignResident(actor, pKingdom, pYear)
                : IsEligibleLocal(actor, pKingdom, pYear);
            if (!eligible) return false;
            SchoolMembershipRecord membership =
                SchoolMembershipService.GetActive(pActorId);
            if (membership == null || !membership.Active) return false;
            CivilServiceQualificationRecord qualification =
                CivilServiceQualificationService.LoadOrRepair(actor, pKingdom);
            if (CivilServiceExamRules.HasEquivalentHostQualification(
                    pKingdom.id, qualification?.KingdomId ?? -1L,
                    ParseQualification(qualification?.Qualification), pMode))
                return false;
            City residence = pForeignResident
                ? HistoricalAffiliationService.ResidenceCity(actor)
                : actor.city;
            HistoricalSchoolAffiliationSnapshot affiliation =
                pForeignResident
                    ? HistoricalAffiliationService.Get(pActorId)
                    : null;
            long homeCityId = CivilServiceExamRules.
                ResolveCandidateHomeCityId(pForeignResident,
                    affiliation?.HometownCityId ?? -1L,
                    residence?.data?.id ?? -1L);
            City homeCity = homeCityId == residence?.data?.id
                ? residence
                : FindCity(homeCityId);
            string socialOrigin = ResolveSocialOrigin(actor);
            pRecord = new CivilServiceExamCandidateRecord
            {
                SessionId = pSessionId,
                KingdomId = pKingdom.id,
                ActorId = pActorId,
                ActorName = SafeName(actor),
                HomeCityId = homeCityId,
                HomeCityName = homeCity?.name ?? "",
                SocialOrigin = socialOrigin,
                SchoolId = membership.SchoolId ?? "",
                LocalGrade = OfficialCareerStateService.
                    EstimateLocalGradeFast(actor, pKingdom),
                Qualification = qualification?.Qualification ?? "none",
                EntryBonus = qualification?.EntryBonus ?? 0,
                UpdatedTime = pUpdatedTime
            };
            pFacts = new CivilServiceExamCandidateFacts(pActorId,
                socialOrigin, EducationScore(membership, pYear),
                SafeStat(actor, "intelligence"),
                CivilServiceExamRules.AgeFitness(SafeAge(actor)));
            return true;
        }

        private static int CountEligible(IEnumerable<long> pActorIds,
            HashSet<long> pSeen, Kingdom pKingdom, int pYear,
            CivilServiceExamMode pMode, bool pForeignResident, int pLimit)
        {
            if (pActorIds == null || pLimit <= 0) return 0;
            int count = 0;
            foreach (long actorId in pActorIds)
            {
                if (!pSeen.Add(actorId)) continue;
                if (!TryBuildCandidate(actorId, pKingdom, -1L, pYear, pMode,
                        pForeignResident, LineageService.CurTime(), out _,
                        out _)) continue;
                count++;
                if (count >= pLimit) break;
            }
            return count;
        }

        private static int EducationScore(SchoolMembershipRecord pMembership,
            int pYear)
        {
            if (pMembership == null) return 0;
            int years = Math.Max(0, pYear - pMembership.StartYear);
            return Math.Min(100, 60 + Math.Min(30, years * 5) +
                                  Math.Min(10,
                                      (int)Math.Max(0f,
                                          pMembership.Reputation / 10f)));
        }

        private static int SafeStat(Actor pActor, string pStat)
        {
            try
            {
                return (int)Math.Max(0f, Math.Min(100f,
                    pActor?.stats?[pStat] ?? 0f));
            }
            catch { return 0; }
        }

        private static int SafeAge(Actor pActor)
        {
            try { return Math.Max(0, pActor?.getAge() ?? 0); }
            catch { return 0; }
        }

        private static bool IsEligibleLocal(Actor pActor, Kingdom pKingdom,
            int pYear)
        {
            if (pActor?.data == null || !pActor.isSexMale() ||
                pActor.kingdom != pKingdom ||
                !pActor.isAlive() || pActor.isRekt() || !pActor.isAdult() ||
                pActor.isKing() || SlaveService.IsSlave(pActor) ||
                !HistoricalSchoolEducationService.IsEducated(pActor, pYear))
                return false;

            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            return layer != CourtOfficeLayer.Central ||
                   OfficialCareerStateService.OfficeGradeForOffice(office) > 20;
        }

        private static bool IsEligibleForeignResident(Actor pActor,
            Kingdom pKingdom, int pYear)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !pActor.isSexMale()) return false;
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            City residence = HistoricalAffiliationService.ResidenceCity(pActor);
            Kingdom home = HistoricalAffiliationService.HomeKingdom(pActor) ??
                           pActor.kingdom;
            bool hasOffice = HasCurrentOffice(pActor);
            return CivilServiceExamRules.IsEligibleForeignExamCandidate(
                adult: SafeAdult(pActor), alive: SafeAlive(pActor),
                slave: SlaveService.IsSlave(pActor), king: pActor.isKing(),
                heir: home?.data != null &&
                      HeirService.PeekRegisteredHeir(home) == pActor,
                prince: FeudatoryService.IsActivePrince(pActor),
                civilOffice: hasOffice || pActor.isCityLeader(),
                militaryOffice: GeneralService.IsGeneral(pActor),
                servingElsewhere: affiliation?.ServiceKingdomId >= 0L,
                resident: affiliation?.LifecycleState ==
                          HistoricalSchoolLifecycleState.Resident,
                residenceInHost: residence?.data != null &&
                                 residence.kingdom == pKingdom,
                foreignHome: affiliation?.HomeKingdomId >= 0L &&
                             affiliation.HomeKingdomId != pKingdom.id,
                educated: HistoricalSchoolEducationService.IsEducated(
                    pActor, pYear), equivalentQualification: false);
        }

        private static bool IsEligibleForInvitation(Actor pActor,
            Kingdom pKingdom, int pYear, CivilServiceExamMode pMode)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !pActor.isSexMale() ||
                pKingdom.capital?.data == null) return false;
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            Kingdom home = HistoricalAffiliationService.HomeKingdom(pActor) ??
                           pActor.kingdom;
            bool hasOffice = HasCurrentOffice(pActor) || pActor.isCityLeader();
            bool eligiblePerson = SafeAdult(pActor) && SafeAlive(pActor) &&
                !SlaveService.IsSlave(pActor) && !pActor.isKing() &&
                (home?.data == null ||
                 HeirService.PeekRegisteredHeir(home) != pActor) &&
                !FeudatoryService.IsActivePrince(pActor) && !hasOffice &&
                !GeneralService.IsGeneral(pActor) &&
                HistoricalSchoolEducationService.IsEducated(pActor, pYear);
            CivilServiceQualificationRecord qualification =
                CivilServiceQualificationService.LoadOrRepair(pActor,
                    pKingdom);
            eligiblePerson &= !CivilServiceExamRules.
                HasEquivalentHostQualification(pKingdom.id,
                    qualification?.KingdomId ?? -1L,
                    ParseQualification(qualification?.Qualification), pMode);
            bool movable = affiliation != null &&
                affiliation.ServiceKingdomId < 0L &&
                (affiliation.LifecycleState ==
                     HistoricalSchoolLifecycleState.AtHome ||
                 affiliation.LifecycleState ==
                     HistoricalSchoolLifecycleState.Resident);
            bool sourceAtWar = home?.data != null &&
                               IsAtWar(pKingdom, home);
            bool atDestination = affiliation?.ResidenceCityId ==
                                 pKingdom.capital.data.id ||
                                 affiliation?.DestinationCityId ==
                                 pKingdom.capital.data.id;
            return CivilServiceExamRules.CanInviteForeignScholar(
                eligiblePerson, movable,
                affiliation?.HomeKingdomId >= 0L &&
                affiliation.HomeKingdomId != pKingdom.id,
                sourceAtWar, atDestination);
        }

        private static bool HasCurrentOffice(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long currentKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string currentOffice, "");
            return currentKingdomId >= 0L ||
                   !string.IsNullOrEmpty(currentOffice);
        }

        private static bool IsAtWar(Kingdom pFirst, Kingdom pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null ||
                pFirst == pSecond) return false;
            try { return pFirst.isInWarWith(pSecond); }
            catch { return false; }
        }

        private static List<long> HostCityIds(Kingdom pKingdom)
        {
            var result = new List<long>(MaxHostCityQueryCount);
            if (pKingdom?.data == null) return result;
            var seen = new HashSet<long>();
            try
            {
                City capital = pKingdom.capital;
                if (capital?.data != null && !capital.isRekt() &&
                    capital.kingdom == pKingdom &&
                    seen.Add(capital.data.id))
                    result.Add(capital.data.id);
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom ||
                        !seen.Add(city.data.id)) continue;
                    result.Add(city.data.id);
                    if (result.Count >= MaxHostCityQueryCount) break;
                }
            }
            catch { result.Clear(); }
            result.Sort();
            return result;
        }

        private static Actor FindActor(long actorId)
        {
            try { return World.world?.units?.get(actorId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try
            {
                return pCityId >= 0L
                    ? World.world?.cities?.get(pCityId)
                    : null;
            }
            catch { return null; }
        }

        private static bool SafeAlive(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool SafeAdult(Actor pActor)
        {
            try { return pActor?.data != null && pActor.isAdult(); }
            catch { return false; }
        }

        private static string ResolveSocialOrigin(Actor pActor)
        {
            pActor.data.get(LineageKeys.LINEAGE_STATUS,
                out string currentStatus, LineageStatus.NONE);
            pActor.data.get(LineageKeys.EVER_NOBLE_BLOOD,
                out bool everNoble, false);
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            return CivilServiceExamRules.ResolveSocialOrigin(currentStatus,
                everNoble, lineageId);
        }

        private static string SafeName(Actor pActor)
        {
            try { return pActor?.getName() ?? ""; }
            catch { return ""; }
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
