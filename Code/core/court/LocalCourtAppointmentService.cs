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

        private sealed class ActiveLocalOfficer
        {
            public long ActorId;
            public string OfficeId = "";
        }

        internal static bool ReconcileCity(Kingdom pKingdom, City pCity,
            int pCapacity, int pYear, out IReadOnlyList<long> pOfficerActorIds)
        {
            pOfficerActorIds = Array.Empty<long>();
            if (DB == null || pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom) return false;
            List<string> desiredSeats = DesiredSeats(pKingdom, pCity,
                Math.Max(0, pCapacity));
            if (!TryLoadActive(pKingdom.id, pCity.data.id,
                    out List<ActiveLocalOfficer> active)) return false;

            string rootOffice = desiredSeats.Count == 0
                ? string.Empty
                : desiredSeats[0];
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
                if (IsFixedOffice(pKingdom, pCity, row.OfficeId))
                {
                    retainedCounts.TryGetValue(row.OfficeId,
                        out int fixedCount);
                    retainedCounts[row.OfficeId] = fixedCount + 1;
                    continue;
                }
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
                if (rootCount == 0 && CourtService.TryAssignLocalOfficer(
                        pCity.leader, pKingdom, pCity, rootOffice))
                    retainedCounts[rootOffice] = 1;
            }

            List<Actor> candidates = LoadAllCandidates(pKingdom,
                LoadCandidates(pKingdom, pCity));
            long leaderNativeCityId = NativeCityId(pCity.leader);
            for (int seatIndex = 1; seatIndex < desiredSeats.Count; seatIndex++)
            {
                string officeId = desiredSeats[seatIndex];
                int requiredBefore = desiredSeats.Take(seatIndex + 1)
                    .Count(id => id == officeId);
                retainedCounts.TryGetValue(officeId, out int current);
                if (current >= requiredBefore) continue;
                Actor candidate = SelectCandidate(candidates, pKingdom,
                    leaderNativeCityId, officeId,
                    pAllowVacancyPromotion: false);
                bool vacancyFallback = candidate == null;
                if (vacancyFallback)
                    candidate = SelectCandidate(candidates, pKingdom,
                        leaderNativeCityId, officeId,
                        pAllowVacancyPromotion: true);
                if (candidate == null) continue;
                if (CourtService.TryAssignLocalOfficer(candidate, pKingdom,
                        pCity, officeId, vacancyFallback))
                    retainedCounts[officeId] = current + 1;
                candidates.Remove(candidate);
            }

            if (!TryLoadActive(pKingdom.id, pCity.data.id, out active))
                return false;
            pOfficerActorIds = active.Select(row => row.ActorId).Distinct()
                .OrderBy(id => id).ToArray();
            return true;
        }

        private static List<string> DesiredSeats(Kingdom pKingdom, City pCity,
            int pCapacity)
        {
            var result = new List<string>(pCapacity);
            if (pCapacity <= 0) return result;
            if (CustomCourtRuntime.TryGetLocalTemplate(pKingdom, pCity,
                    out CustomLocalCourtTemplate local))
            {
                List<CustomCourtOffice> offices = (local.Offices ??
                    new List<CustomCourtOffice>()).Where(office =>
                    office != null && office.Layer == CourtOfficeLayer.City &&
                    !CustomCourtTemplateRules.IsFixedChiefOffice(office))
                    .ToList();
                IReadOnlyDictionary<string, int> ranks =
                    CustomCourtHierarchyLayoutRules.BuildRanks(offices,
                        local.Edges);
                bool templateControlsCapacity = CustomCourtRuntime.
                    HasCustomLocalTemplates(pKingdom);
                foreach (CustomCourtOffice office in offices.OrderBy(office =>
                             ranks.TryGetValue(office.Id, out int rank)
                                 ? rank
                                 : int.MaxValue)
                             .ThenBy(office => office.Grade)
                             .ThenBy(office => office.Id,
                                 StringComparer.Ordinal))
                    for (int slot = 0;
                         slot < Math.Max(1, office.Slots) &&
                         (templateControlsCapacity || result.Count < pCapacity);
                         slot++)
                        result.Add(office.Id);
                return result;
            }

            string leaderOffice = CourtService.ResolveCityOffice(pKingdom,
                pCity);
            for (int slot = 0; slot < pCapacity; slot++)
            {
                string office = LocalCourtOfficeRules.OfficeForSlot(slot,
                    leaderOffice);
                if (!string.IsNullOrEmpty(office)) result.Add(office);
            }
            return result;
        }

        private static bool IsFixedOffice(Kingdom pKingdom, City pCity,
            string pOfficeId)
        {
            return CustomCourtRuntime.TryGetLocalTemplate(pKingdom, pCity,
                    out CustomLocalCourtTemplate template) &&
                (template.Offices ?? new List<CustomCourtOffice>()).Any(
                    office => office != null && office.Id == pOfficeId &&
                        CustomCourtTemplateRules.IsFixedChiefOffice(office));
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

            int inspected = 0;
            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                {
                    if (++inspected > CandidateScanLimit) break;
                    if (CanUseCandidate(actor, pKingdom,
                            pFromLocalWaitingPool: false)) result.Add(actor);
                }
            }
            catch { result.Clear(); }
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
            IReadOnlyList<Actor> pWaitingCandidates)
        {
            var result = new List<Actor>();
            var actorIds = new HashSet<long>();
            foreach (Actor actor in pWaitingCandidates ?? Array.Empty<Actor>())
            {
                if (actor?.data == null || !actorIds.Add(actor.data.id))
                    continue;
                result.Add(actor);
            }

            int inspected = 0;
            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                {
                    if (++inspected > CandidateScanLimit) break;
                    if (actor?.data == null || actorIds.Contains(actor.data.id) ||
                        !CanUseCandidateFacts(actor, pKingdom)) continue;
                    actorIds.Add(actor.data.id);
                    result.Add(actor);
                }
            }
            catch { }
            return result;
        }

        private static Actor SelectCandidate(IReadOnlyList<Actor> pCandidates,
            Kingdom pKingdom, long pLeaderNativeCityId, string pOfficeId,
            bool pAllowVacancyPromotion)
        {
            Actor best = null;
            int bestScore = int.MinValue;
            foreach (Actor actor in pCandidates)
            {
                if (!CanUseCandidateFacts(actor, pKingdom) ||
                    !CivilServiceQualificationService.
                        CanReceiveFormalCivilAppointment(actor, pKingdom,
                            CourtOfficeLayer.City, pOfficeId,
                            pAllowVacancyPromotion,
                            pAllowLocalLowerQualification: true)) continue;
                actor.data.get(LineageKeys.OFFICER_MERIT,
                    out float merit, 0f);
                int score = LocalOfficialCandidateRules.Score(
                    MainAbility(actor), (int)Math.Max(0f, merit),
                    pLeaderNativeCityId >= 0L &&
                    NativeCityId(actor) == pLeaderNativeCityId);
                if (best == null || score > bestScore ||
                    score == bestScore && actor.data.id < best.data.id)
                {
                    best = actor;
                    bestScore = score;
                }
            }
            return best;
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
