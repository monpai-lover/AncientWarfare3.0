using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal static class LocalCourtAppointmentService
    {
        private const int CandidateScanLimit = 96;
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private sealed class CandidateScanState
        {
            public int Cursor;
        }
        private static readonly Dictionary<string, CandidateScanState>
            CandidateScans = new Dictionary<string, CandidateScanState>(
                StringComparer.Ordinal);
        private static readonly Dictionary<string, int> AppointmentFailures =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private sealed class ActiveLocalOfficer
        {
            public long ActorId;
            public string OfficeId = "";
        }

        internal static bool ReconcileCity(Kingdom pKingdom, City pCity,
            int pCapacity, int pYear, out IReadOnlyList<long> pOfficerActorIds)
        {
            return ReconcileCity(pKingdom, pCity, pCapacity, pYear,
                out pOfficerActorIds, out _);
        }

        internal static bool ReconcileCity(Kingdom pKingdom, City pCity,
            int pCapacity, int pYear, out IReadOnlyList<long> pOfficerActorIds,
            out bool pHasVacancy)
        {
            pOfficerActorIds = Array.Empty<long>();
            pHasVacancy = false;
            if (DB == null || pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom) return false;
            List<string> desiredSeats = DesiredSeats(pKingdom, pCity,
                Math.Max(0, pCapacity));
            if (!TryLoadActive(pKingdom.id, pCity.data.id,
                    out List<ActiveLocalOfficer> active)) return false;

            string rootOffice = desiredSeats.Count == 0
                ? string.Empty
                : desiredSeats[0];
            if (!HasLiveCityLeader(pCity) && !string.IsNullOrEmpty(rootOffice))
            {
                if (TryRepairCityLeader(pKingdom, pCity, rootOffice, active))
                {
                    if (!TryLoadActive(pKingdom.id, pCity.data.id,
                            out active)) return false;
                }
            }
            Actor cityLeader = pCity.leader;
            if (!string.IsNullOrEmpty(rootOffice) && cityLeader?.data != null)
            {
                ActiveLocalOfficer rootRow = active.FirstOrDefault(row =>
                    row.OfficeId == rootOffice);
                if (rootRow != null && rootRow.ActorId != cityLeader.data.id)
                {
                    EndAppointment(FindActor(rootRow.ActorId), rootRow,
                        pKingdom, "city_leader_mismatch");
                    if (!TryLoadActive(pKingdom.id, pCity.data.id,
                            out active)) return false;
                }
            }
            var desiredCounts = desiredSeats.GroupBy(id => id,
                    StringComparer.Ordinal).ToDictionary(group => group.Key,
                    group => group.Count(), StringComparer.Ordinal);
            var retainedCounts = new Dictionary<string, int>(
                StringComparer.Ordinal);
            foreach (ActiveLocalOfficer row in active)
            {
                Actor actor = FindActor(row.ActorId);
                bool live = actor?.data != null && actor.isAlive() &&
                            !actor.isRekt() &&
                            CourtAffiliationResolver.CanServe(actor, pKingdom,
                                CourtOfficeLayer.City);
                desiredCounts.TryGetValue(row.OfficeId, out int allowed);
                retainedCounts.TryGetValue(row.OfficeId, out int retained);
                bool surplus = retained >= allowed;
                bool subordinateTermDue = row.OfficeId != rootOffice && live &&
                    IsTermDue(actor, pYear);
                if (!live || allowed == 0 || surplus || subordinateTermDue)
                {
                    EndAppointment(actor, row, pKingdom,
                        !live ? "invalid" : subordinateTermDue
                            ? "term_expired"
                            : "local_office_reformed");
                    continue;
                }
                retainedCounts[row.OfficeId] = retained + 1;
            }

            if (!string.IsNullOrEmpty(rootOffice) && pCity.leader?.data != null)
            {
                retainedCounts.TryGetValue(rootOffice, out int rootCount);
                if (rootCount == 0 && ShouldAttempt(rootOffice, pKingdom,
                        pCity, pYear))
                {
                    bool committed = CourtService.TryAssignLocalOfficer(
                        pCity.leader, pKingdom, pCity, rootOffice);
                    if (committed)
                    {
                        ClearFailure(rootOffice, pKingdom, pCity);
                        retainedCounts[rootOffice] = 1;
                    }
                    else
                        RecordFailure(rootOffice, pKingdom, pCity, pYear);
                }
            }

            List<Actor> candidates = null;
            long leaderNativeCityId = NativeCityId(pCity.leader);
            for (int seatIndex = 1; seatIndex < desiredSeats.Count; seatIndex++)
            {
                string officeId = desiredSeats[seatIndex];
                if (!ShouldAttempt(officeId, pKingdom, pCity, pYear))
                    continue;
                int requiredBefore = desiredSeats.Take(seatIndex + 1)
                    .Count(id => id == officeId);
                retainedCounts.TryGetValue(officeId, out int current);
                if (current >= requiredBefore) continue;
                candidates ??= LoadAllCandidates(pKingdom, pCity,
                    LoadCandidates(pKingdom, pCity));
                Actor candidate = SelectCandidate(candidates, pKingdom,
                    pCity, leaderNativeCityId, officeId,
                    pAllowVacancyPromotion: false);
                bool vacancyFallback = candidate == null;
                if (vacancyFallback)
                    candidate = SelectCandidate(candidates, pKingdom,
                        pCity, leaderNativeCityId, officeId,
                        pAllowVacancyPromotion: true);
                if (candidate == null) continue;
                bool committed = CourtService.TryAssignLocalOfficer(candidate,
                    pKingdom, pCity, officeId, vacancyFallback);
                if (committed)
                {
                    ClearFailure(officeId, pKingdom, pCity);
                    retainedCounts[officeId] = current + 1;
                }
                else
                {
                    RecordFailure(officeId, pKingdom, pCity, pYear);
                    // A failed persistence transaction is commonly caused by
                    // a stale duplicate row. Trying another candidate in the
                    // same pass only repeats the expensive transaction and
                    // can amplify a frame spike.
                    break;
                }
                candidates.Remove(candidate);
            }

            if (!TryLoadActive(pKingdom.id, pCity.data.id, out active))
                return false;
            pOfficerActorIds = active.Select(row => row.ActorId).Distinct()
                .OrderBy(id => id).ToArray();
            var finalCounts = active.GroupBy(row => row.OfficeId,
                StringComparer.Ordinal).ToDictionary(group => group.Key,
                group => group.Count(), StringComparer.Ordinal);
            foreach (string seat in desiredSeats)
            {
                finalCounts.TryGetValue(seat, out int filled);
                if (filled <= 0)
                {
                    pHasVacancy = true;
                    break;
                }
                finalCounts[seat] = filled - 1;
            }
            return true;
        }

        private static List<string> DesiredSeats(Kingdom pKingdom, City pCity,
            int pCapacity)
        {
            return LocalChiefOfficeResolver.ResolveOrderedSeats(pKingdom,
                pCity, pCapacity).ToList();
        }

        private static bool TryLoadActive(long pKingdomId, long pCityId,
            out List<ActiveLocalOfficer> pRows)
        {
            pRows = new List<ActiveLocalOfficer>();
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ACTOR_ID,OFFICE_ID FROM " +
                    CourtOfficerTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom AND CITY_ID=@city " +
                    "AND LAYER=@layer AND ACTIVE=1 " +
                    "ORDER BY APPOINTED_TIME,OFFICER_ID LIMIT 64";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@layer",
                    CourtOfficeLayer.City);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    pRows.Add(new ActiveLocalOfficer
                    {
                        ActorId = Convert.ToInt64(reader.GetValue(0)),
                        OfficeId = reader.IsDBNull(1)
                            ? ""
                            : Convert.ToString(reader.GetValue(1)) ?? ""
                    });
                return true;
            }
            catch
            {
                pRows.Clear();
                return false;
            }
        }

        private static List<Actor> LoadCandidates(Kingdom pKingdom,
            City pCity)
        {
            var result = new List<Actor>();
            if (CivilServiceQualificationService.HasExaminationSystem(
                    pKingdom))
            {
                var cityIds = new List<long>();
                try
                {
                    foreach (City city in pKingdom.getCities())
                        if (city?.data != null && !city.isRekt() &&
                            city.kingdom == pKingdom)
                            cityIds.Add(city.data.id);
                }
                catch { return result; }
                if (!CivilServiceWaitingPoolQuery.TryLoadLocalActorIds(DB,
                        CivilServiceExamCandidateTableItem.GetTableName(),
                        CivilServiceExamSessionTableItem.GetTableName(),
                        ActorArchiveTableItem.GetTableName(),
                        CourtOfficerTableItem.GetTableName(),
                        SchoolAffiliationTableItem.GetTableName(), pKingdom.id,
                        cityIds, CandidateScanLimit,
                        out IReadOnlyList<long> actorIds)) return result;
                foreach (long actorId in actorIds)
                {
                    Actor actor = FindActor(actorId);
                    if (CanUseCandidate(actor, pKingdom,
                            pFromLocalWaitingPool: true)) result.Add(actor);
                }
                return result;
            }

            return result;
        }

        private static bool CanUseCandidate(Actor pActor, Kingdom pKingdom,
            bool pFromLocalWaitingPool)
        {
            if (!CanUseCandidateFacts(pActor, pKingdom)) return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string officeId, "");
            pActor.data.get(LineageKeys.CIVIL_SERVICE_QUALIFICATION,
                out string qualification, "none");
            return LocalOfficialCandidateRules.CanEnter(
                    pActor.isAlive() && !pActor.isRekt(), pActor.isAdult(),
                    SlaveService.IsSlave(pActor),
                    !string.IsNullOrEmpty(officeId), pActor.isKing(),
                    HeirService.PeekRegisteredHeir(pKingdom) == pActor,
                    CivilServiceQualificationService.HasExaminationSystem(
                        pKingdom), qualification,
                    participatedAndFailedHigherStage:
                    pFromLocalWaitingPool &&
                    string.Equals(qualification, "none",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static bool CanUseCandidateFacts(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !pActor.isSexMale() || pActor.hasTrait("madness") ||
                pActor.isCityLeader() ||
                !CourtAffiliationResolver.CanServe(pActor, pKingdom,
                    CourtOfficeLayer.City) ||
                !RoyalGuardOfficeRules.CanAppearInOfficeCandidateList(
                    RoyalGuardService.IsRoyalGuard(pActor)) ||
                !RoyalAsylumRules.CanPerformProtectedRole(
                    RoyalAsylumService.IsActive(pActor))) return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string officeId, "");
            return LocalOfficialCandidateRules.CanEnter(
                pActor.isAlive() && !pActor.isRekt(), pActor.isAdult(),
                SlaveService.IsSlave(pActor),
                !string.IsNullOrEmpty(officeId), pActor.isKing(),
                HeirService.PeekRegisteredHeir(pKingdom) == pActor,
                examinationEnabled: false, qualification: "none",
                participatedAndFailedHigherStage: false);
        }

        private static List<Actor> LoadAllCandidates(Kingdom pKingdom,
            City pCity, IReadOnlyList<Actor> pWaitingCandidates,
            bool pFullScan = false)
        {
            var result = new List<Actor>();
            var actorIds = new HashSet<long>();
            foreach (Actor actor in pWaitingCandidates ?? Array.Empty<Actor>())
            {
                if (actor?.data == null || !actorIds.Add(actor.data.id))
                    continue;
                result.Add(actor);
            }

            List<Actor> units;
            try
            {
                units = pKingdom.getUnits()?.Where(actor => actor?.data != null)
                    .ToList() ?? new List<Actor>();
            }
            catch
            {
                units = new List<Actor>();
            }
            if (units.Count == 0) return result;

            string scanKey = pKingdom.id + ":" + (pCity?.data?.id ?? -1L);
            if (!CandidateScans.TryGetValue(scanKey,
                    out CandidateScanState scan))
            {
                scan = new CandidateScanState();
                CandidateScans[scanKey] = scan;
            }
            int start = scan.Cursor % units.Count;
            int inspected = pFullScan ? units.Count :
                Math.Min(CandidateScanLimit, units.Count);
            for (int offset = 0; offset < inspected; offset++)
            {
                Actor actor = units[(start + offset) % units.Count];
                if (actor?.data == null || actorIds.Contains(actor.data.id) ||
                    !CanUseCandidateFacts(actor, pKingdom)) continue;
                actorIds.Add(actor.data.id);
                result.Add(actor);
            }
            scan.Cursor = (start + inspected) % units.Count;
            return result;
        }

        private static bool TryRepairCityLeader(Kingdom pKingdom, City pCity,
            string pRootOffice, IReadOnlyList<ActiveLocalOfficer> pActive)
        {
            Actor candidate = null;
            ActiveLocalOfficer rootRow = pActive?.FirstOrDefault(row =>
                row != null && row.OfficeId == pRootOffice);
            if (rootRow != null)
            {
                Actor projected = FindActor(rootRow.ActorId);
                if (IsUsableLeader(projected, pKingdom)) candidate = projected;
            }
            if (candidate == null)
            {
                List<Actor> candidates = LoadAllCandidates(pKingdom, pCity,
                    LoadCandidates(pKingdom, pCity), pFullScan: true);
                candidate = SelectCandidate(candidates, pKingdom, pCity,
                    NativeCityId(pCity.leader), pRootOffice,
                    pAllowVacancyPromotion: true);
            }
            if (candidate == null) return false;

            if (rootRow != null && rootRow.ActorId != candidate.data.id)
            {
                Actor stale = FindActor(rootRow.ActorId);
                if (stale?.data != null && stale.isAlive() && !stale.isRekt())
                    CourtService.TryDismissOfficer(stale, pKingdom,
                        "city_leader_rebound");
            }
            return ManualLocalChiefAppointmentService.TryAppoint(
                pKingdom, pCity, candidate,
                () => rootRow != null && rootRow.ActorId == candidate.data.id
                    ? true
                    : CourtService.TryAssignLocalOfficer(candidate, pKingdom,
                        pCity, pRootOffice, pVacancyPromotion: true));
        }

        private static bool HasLiveCityLeader(City pCity)
        {
            Actor leader = pCity?.leader;
            return leader?.data != null && leader.isAlive() &&
                   !leader.isRekt() && leader.city == pCity &&
                   leader.kingdom == pCity.kingdom && leader.isCityLeader();
        }

        private static bool IsUsableLeader(Actor pActor, Kingdom pKingdom)
        {
            return pActor?.data != null && pActor.isAlive() &&
                   !pActor.isRekt() && pActor.kingdom == pKingdom &&
                   !pActor.isKing() && !pActor.isCityLeader();
        }

        internal static void ClearRuntime()
        {
            CandidateScans.Clear();
            AppointmentFailures.Clear();
        }

        private static string FailureKey(string pOfficeId, Kingdom pKingdom,
            City pCity)
        {
            return (pKingdom?.id ?? -1L) + ":" +
                   (pCity?.data?.id ?? -1L) + ":" + (pOfficeId ?? "");
        }

        private static bool ShouldAttempt(string pOfficeId, Kingdom pKingdom,
            City pCity, int pYear)
        {
            return !AppointmentFailures.TryGetValue(
                FailureKey(pOfficeId, pKingdom, pCity), out int failureYear) ||
                CourtAppointmentFailureBackoffRules.ShouldAttempt(
                    failureYear, pYear);
        }

        private static void RecordFailure(string pOfficeId, Kingdom pKingdom,
            City pCity, int pYear)
        {
            AppointmentFailures[FailureKey(pOfficeId, pKingdom, pCity)] = pYear;
        }

        private static void ClearFailure(string pOfficeId, Kingdom pKingdom,
            City pCity)
        {
            AppointmentFailures.Remove(FailureKey(pOfficeId, pKingdom, pCity));
        }

        private static Actor SelectCandidate(IReadOnlyList<Actor> pCandidates,
            Kingdom pKingdom, City pCity, long pLeaderNativeCityId,
            string pOfficeId, bool pAllowVacancyPromotion)
        {
            Actor best = null;
            int bestScore = int.MinValue;
            int bestTier = int.MaxValue;
            int officeGrade = OfficialCareerStateService.OfficeGradeForOffice(
                pKingdom, CourtOfficeLayer.City, pOfficeId, pCity);
            bool lowOffice = LocalLowOfficeVacancyRules.IsLowestLocalGrade(
                officeGrade);
            foreach (Actor actor in pCandidates)
            {
                if (!CanUseCandidateFacts(actor, pKingdom) ||
                    !CivilServiceQualificationService.
                        CanReceiveFormalCivilAppointment(actor, pKingdom,
                            CourtOfficeLayer.City, pOfficeId,
                            pAllowVacancyPromotion,
                            pAllowLocalLowerQualification: true,
                            pCity: pCity)) continue;
                actor.data.get(LineageKeys.OFFICER_MERIT,
                    out float merit, 0f);
                bool formalLocalQualification =
                    HasFormalLocalQualification(actor, pKingdom);
                int score = LocalOfficialCandidateRules.Score(
                    MainAbility(actor), (int)Math.Max(0f, merit),
                    pLeaderNativeCityId >= 0L &&
                    NativeCityId(actor) == pLeaderNativeCityId);
                int tier = lowOffice
                    ? (int)LocalLowOfficeVacancyRules.CandidateTier(
                        formalLocalQualification,
                        HasClanOrShi(actor))
                    : 0;
                if (lowOffice && pAllowVacancyPromotion)
                {
                    int resolvedRank =
                        OfficialCareerRankRules.ResolveLocalVacancyPromotionRank(
                            OfficialCareerStateService.ReadRankFast(actor),
                            officeGrade, CourtService.HasNineRankSystem(
                                pKingdom),
                            formalLocalQualification ||
                            LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback(
                                isCityLayer: true, officeGrade: officeGrade,
                                vacancyPromotion: true),
                            vacancyPromotion: true);
                    score += Math.Max(0, resolvedRank);
                }
                if (best == null || tier < bestTier ||
                    tier == bestTier && (score > bestScore ||
                    score == bestScore && actor.data.id < best.data.id))
                {
                    best = actor;
                    bestTier = tier;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool HasFormalLocalQualification(Actor pActor,
            Kingdom pKingdom)
        {
            CivilServiceQualificationRecord qualification =
                CivilServiceQualificationService.LoadOrRepair(pActor,
                    pKingdom);
            return qualification != null &&
                   LocalOfficialCandidateRules.IsLocalQualification(
                       qualification.Qualification);
        }

        private static bool HasClanOrShi(Actor pActor)
        {
            if (pActor?.data == null) return false;
            try
            {
                if (pActor.hasClan() && pActor.clan?.data != null)
                    return true;
            }
            catch { }
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            return shiId >= 0L;
        }

        private static bool IsTermDue(Actor pActor, int pYear)
        {
            pActor.data.get(LineageKeys.OFFICER_TERM_END_YEAR,
                out int termEndYear, int.MaxValue);
            return termEndYear >= 0 && termEndYear <= pYear;
        }

        private static void EndAppointment(Actor pActor,
            ActiveLocalOfficer pRow, Kingdom pKingdom, string pReason)
        {
            if (pActor?.data != null && pActor.isAlive() && !pActor.isRekt() &&
                CourtService.TryDismissOfficer(pActor, pKingdom, pReason))
                return;
            OfficialCareerService.EndForOffice(pRow.ActorId, pKingdom.id,
                CourtOfficeLayer.City, pRow.OfficeId, pReason);
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static long NativeCityId(Actor pActor)
        {
            if (pActor?.data == null) return -1L;
            pActor.data.get(LineageKeys.OFFICER_NATIVE_CITY_ID,
                out long cityId, -1L);
            return cityId;
        }

        private static int MainAbility(Actor pActor)
        {
            try
            {
                return (int)Math.Max(Math.Max(
                        pActor.stats?["intelligence"] ?? 0f,
                        pActor.stats?["stewardship"] ?? 0f),
                    Math.Max(pActor.stats?["warfare"] ?? 0f,
                        pActor.stats?["diplomacy"] ?? 0f));
            }
            catch { return 0; }
        }
    }
}
