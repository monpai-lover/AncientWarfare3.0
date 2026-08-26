using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.core.db;
using AncientWarfare3.core.county;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class LocalCourtAppointmentService
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private sealed class ActiveLocalOfficer
        {
            public long ActorId;
            public string OfficeId = "";
        }

        internal static CourtVacancyOutcome TryFillRegisteredLocalVacancy(
            Kingdom pKingdom, City pCity, CourtVacancyKey pVacancy,
            CourtCandidateSession pSession)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom ||
                pSession == null || pVacancy.KingdomId != pKingdom.id ||
                pVacancy.CityId != pCity.data.id)
                return CourtVacancyOutcome.Invalid;

            int year = Date.getCurrentYear();
            if (pVacancy.Layer == CourtOfficeLayer.County)
            {
                if (pVacancy.OfficeId != CourtOfficeId.CountyMagistrate ||
                    pVacancy.CountyId < 0L ||
                    LoadCountyOfficerIds(pKingdom.id, pCity.data.id)
                        .Contains(pVacancy.CountyId))
                    return CourtVacancyOutcome.Invalid;
                Actor countyCandidate = pSession.Actors
                    .Where(actor => pSession.IsAvailable(actor, pVacancy))
                    .Where(actor => actor?.data != null &&
                        CanUseCandidateFacts(actor, pKingdom))
                    .Where(actor => CivilServiceQualificationService.
                        CanReceiveFormalCivilAppointment(actor, pKingdom,
                            CourtOfficeLayer.County,
                            CourtOfficeId.CountyMagistrate, true,
                            pAllowLocalLowerQualification: true,
                            pCity: pCity))
                    .OrderByDescending(MainAbility)
                    .ThenBy(actor => actor.data.id)
                    .FirstOrDefault();
                if (countyCandidate == null)
                    return CourtVacancyOutcome.NoCandidate;
                return CourtService.TryAssignCountyMagistrate(countyCandidate,
                        pKingdom, pCity, pVacancy.CountyId, true)
                    ? CourtVacancyOutcome.Filled
                    : CourtVacancyOutcome.TechnicalFailure;
            }

            if (pVacancy.Layer != CourtOfficeLayer.City ||
                string.IsNullOrEmpty(pVacancy.OfficeId))
                return CourtVacancyOutcome.Invalid;
            if (!TryLoadActive(pKingdom.id, pCity.data.id,
                    out List<ActiveLocalOfficer> active))
                return CourtVacancyOutcome.TechnicalFailure;
            int desiredSeats = DesiredSeats(pKingdom, pCity,
                    ResolveCurrentCapacity(pKingdom, pCity))
                .Count(officeId => officeId == pVacancy.OfficeId);
            int occupiedSeats = active.Count(row =>
                row.OfficeId == pVacancy.OfficeId);
            if (desiredSeats <= occupiedSeats)
                return CourtVacancyOutcome.Invalid;

            long leaderNativeCityId = NativeCityId(pCity.leader);
            Actor candidate = SelectCandidate(pSession.Actors, pKingdom,
                pCity, leaderNativeCityId, pVacancy.OfficeId,
                pAllowVacancyPromotion: true);
            if (candidate == null)
                return CourtVacancyOutcome.NoCandidate;
            if (pVacancy.IsLocalChief)
            {
                bool committed = ManualLocalChiefAppointmentService.TryAppoint(
                    pKingdom, pCity, candidate, () =>
                        CourtService.TryAssignLocalOfficer(candidate, pKingdom,
                            pCity, pVacancy.OfficeId, true));
                return committed ? CourtVacancyOutcome.Filled :
                    CourtVacancyOutcome.TechnicalFailure;
            }
            return CourtService.TryAssignLocalOfficer(candidate, pKingdom,
                    pCity, pVacancy.OfficeId, true)
                ? CourtVacancyOutcome.Filled
                : CourtVacancyOutcome.TechnicalFailure;
        }

        internal static IReadOnlyList<CourtVacancyKey> DiscoverVacancies(
            Kingdom pKingdom, City pCity, int pCapacity, int pYear)
        {
            var result = new List<CourtVacancyKey>();
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom) return result;
            if (!TryLoadActive(pKingdom.id, pCity.data.id,
                    out List<ActiveLocalOfficer> active)) return result;
            IReadOnlyList<string> seats = DesiredSeats(pKingdom, pCity,
                Math.Max(0, pCapacity));
            var occupied = active.GroupBy(row => row.OfficeId,
                    StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(),
                    StringComparer.Ordinal);
            foreach (string officeId in seats)
            {
                occupied.TryGetValue(officeId, out int count);
                if (count > 0)
                {
                    occupied[officeId] = count - 1;
                    continue;
                }
                result.Add(new CourtVacancyKey(pKingdom.id, pCity.data.id,
                    -1L, CourtOfficeLayer.City, officeId,
                    pIsLocalChief: officeId == CourtService.ResolveCityOffice(
                        pKingdom, pCity)));
            }
            HashSet<long> occupiedCounties = LoadCountyOfficerIds(
                pKingdom.id, pCity.data.id);
            foreach (CountyRecord county in CountyAdministrationService.
                         CountiesForCity(pCity.data.id))
            {
                if (county == null || !county.Active || county.CountyId < 0L)
                    continue;
                if (occupiedCounties.Contains(county.CountyId)) continue;
                result.Add(new CourtVacancyKey(pKingdom.id, pCity.data.id,
                    county.CountyId, CourtOfficeLayer.County,
                    CourtOfficeId.CountyMagistrate));
            }
            return result;
        }

        private static HashSet<long> LoadCountyOfficerIds(long pKingdomId,
            long pCityId)
        {
            var result = new HashSet<long>();
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT COUNTY_ID FROM " +
                    CourtOfficerTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom AND CITY_ID=@city " +
                    "AND LAYER=@layer AND OFFICE_ID=@office AND ACTIVE=1";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@layer", CourtOfficeLayer.County);
                command.Parameters.AddWithValue("@office",
                    CourtOfficeId.CountyMagistrate);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read() && !reader.IsDBNull(0))
                    result.Add(Convert.ToInt64(reader.GetValue(0)));
            }
            catch { }
            return result;
        }

        private static List<string> DesiredSeats(Kingdom pKingdom, City pCity,
            int pCapacity)
        {
            return LocalChiefOfficeResolver.ResolveOrderedSeats(pKingdom,
                pCity, pCapacity).ToList();
        }

        private static int ResolveCurrentCapacity(Kingdom pKingdom,
            City pCity)
        {
            try
            {
                return CourtRules.CityOfficeSlots(
                    pCity.getPopulationPeople(), pCity.countZones(),
                    pKingdom.capital == pCity);
            }
            catch { return 0; }
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

        internal static void ClearRuntime()
        {
            OfficerCandidateCatalog.ClearRuntime();
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
            bool regionalGovernor = OfficialCareerStateService.
                IsRegionalGovernorSeat(pKingdom, CourtOfficeLayer.City,
                    pOfficeId, pCity);
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
                            vacancyPromotion: true,
                            regionalGovernor: regionalGovernor);
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
