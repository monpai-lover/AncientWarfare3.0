using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.court
{
    internal sealed class OfficialCareerStateView
    {
        public long ActorId;
        public long KingdomId;
        public long CityId;
        public long NativeCityId = -1L;
        public long PreviousCityId = -1L;
        public int WaitingSinceYear = -1;
        public int Rank = OfficialCareerRankRules.Unranked;
        public int Track;
        public string OfficeId = "";
        public float Merit;
        public int MeritCap = 1;
        public int TermEndYear = -1;
        public int LastEvaluation = 2;
        public int EvaluationModifierUntil = -1;
        public int Seniority;
        public int LastPopulationSnapshot = -1;
        public int LocalGrade = NineRankRules.Unranked;
        public int LocalGradeReviewYear = -1;
    }

    internal sealed class OfficialCareerAppointmentProjection
    {
        public Actor Actor;
        public Kingdom Kingdom;
        public OfficialCareerStateView State;
        public int PreviousRank = OfficialCareerRankRules.Unranked;
        public bool NineRankSystem;
    }

    internal static class OfficialCareerStateService
    {
        private const int MaximumGovernorRollbackRepairAttempts = 3;

        private sealed class EconomyView
        {
            public float Tax;
            public float Food;
            public float Unrest;
        }

        private sealed class AnnualMutation
        {
            public OfficialCareerStateView State;
            public Actor Actor;
            public int PreviousRank;
            public int Rank;
            public float Merit;
            public int TermEndYear;
            public int LastEvaluation;
            public int EvaluationModifierUntil;
            public int Seniority;
            public int LastPopulationSnapshot;
            public int LocalGrade;
            public int LocalGradeReviewYear;
            public bool Evaluated;
            public bool RotationDue;
        }

        private sealed class GovernorRotationRuntimeAssignment
        {
            public AnnualMutation Mutation;
            public Actor Actor;
            public City FormerCity;
            public long FormerCityId;
            public City DestinationCity;
            public long DestinationCityId;
        }

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;

        public static bool RecordNobleRewardYear(Actor pActor,
            long pKingdomId, int pYear)
        {
            if (DB == null || pActor?.data == null || pKingdomId < 0L)
                return false;
            bool recorded = OfficialCareerRewardCooldownPersistence.TryRecord(
                DB, OfficialCareerStateTableItem.GetTableName(),
                pActor.data.id, pKingdomId, pYear, LineageService.CurTime());
            if (!recorded)
                ModClass.LogWarning(
                    "Official reward cooldown projection failed");
            return recorded;
        }

        public static bool ProjectAppointment(Actor pActor, Kingdom pKingdom,
            string pLayer, string pOfficeId, City pCity,
            bool pActing = false)
        {
            if (DB == null || pActor?.data == null || pKingdom?.data == null) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                OfficialCareerAppointmentProjection projection = StageAppointment(
                    DB, transaction, pActor, pKingdom, pLayer, pOfficeId, pCity,
                    pActing);
                transaction.Commit();
                PublishAppointment(projection);
                return true;
            }
            catch (Exception e)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Official career state projection failed: " + e.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static bool RestoreAppointmentProjection(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId, City pCity,
            bool pActing)
        {
            if (DB == null || pActor?.data == null ||
                pKingdom?.data == null) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                OfficialCareerAppointmentProjection projection = StageAppointment(
                    DB, transaction, pActor, pKingdom, pLayer, pOfficeId,
                    pCity, pActing);
                transaction.Commit();
                OfficialCareerStateView state = projection?.State;
                if (state == null) return false;
                ProjectHotState(pActor, state.Rank, state.Track, state.Merit,
                    state.MeritCap, state.TermEndYear, state.LastEvaluation,
                    state.EvaluationModifierUntil, state.Seniority,
                    state.LastPopulationSnapshot, state.NativeCityId,
                    state.PreviousCityId, state.WaitingSinceYear,
                    state.LocalGrade, state.LocalGradeReviewYear);
                return true;
            }
            catch (Exception e)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning(
                    "Official career restore projection failed: " + e.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static OfficialCareerAppointmentProjection StageAppointment(
            SQLiteConnection pDb, SQLiteTransaction pTransaction, Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId, City pCity,
            bool pActing = false, bool pVacancyPromotion = false,
            bool pAllowLocalLowerQualification = false)
        {
            if (pDb == null || pTransaction == null || pActor?.data == null ||
                pKingdom?.data == null)
                throw new ArgumentException("invalid official appointment state stage");

            OfficialCareerStateView existing = ReadState(pDb, pActor.data.id,
                pTransaction);
            int previousRank = existing?.Rank ?? OfficialCareerRankRules.Unranked;
            int currentYear = Date.getCurrentYear();
            long targetCityId = pCity?.data?.id ?? -1L;
            long nativeCityId = ResolveNativeCityId(pActor, existing);
            long previousCityId = existing?.PreviousCityId ?? -1L;
            if (existing?.CityId >= 0 && existing.CityId != targetCityId)
                previousCityId = existing.CityId;
            int officeGrade = OfficeGradeForOffice(pKingdom, pLayer,
                pOfficeId, pCity);
            int meritCap = OfficialCareerRankRules.MeritCap(officeGrade);
            int age = SafeAge(pActor);
            bool nineRank = CourtService.HasNineRankSystem(pKingdom);
            bool reviewGrade = nineRank && (existing == null ||
                NineRankRules.ShouldReview(existing.LocalGradeReviewYear,
                    currentYear));
            int localGrade = !nineRank
                ? NineRankRules.Unranked
                : reviewGrade
                    ? ResolveLocalGrade(pActor, existing, currentYear)
                    : existing?.LocalGrade ?? NineRankRules.Unranked;
            int localGradeReviewYear = !nineRank
                ? -1
                : reviewGrade
                    ? currentYear
                    : existing?.LocalGradeReviewYear ?? -1;
            int rank = ResolveAppointmentRankFast(pActor, pKingdom,
                pLayer, pOfficeId, pActing, pVacancyPromotion, existing,
                localGrade, pAllowLocalLowerQualification, pCity);
            int track = OfficialCareerRankRules.ResolveTrack(
                IsMilitaryOffice(pLayer, pOfficeId),
                GeneralService.IsActiveGeneralFast(pActor));
            float merit = Math.Min(existing?.Merit ?? 0f, meritCap);
            int lastEvaluation = existing?.LastEvaluation ?? 2;
            bool freshTerm = existing == null || existing.TermEndYear <= currentYear ||
                             existing.CityId != targetCityId ||
                             existing.OfficeId != (pOfficeId ?? "");
            int termEndYear = pActing
                ? pYearAfter(currentYear)
                : freshTerm && pLayer == CourtOfficeLayer.City
                    ? currentYear + LocalOfficialTermRules.TermLength(
                        MainAttribute(pActor), (int)Math.Max(0f, merit), age,
                        pActor.data.id, currentYear)
                : pOfficeId == CourtOfficeId.WestMayor
                    ? ResolveWesternMayorTermEndYear(pKingdom, currentYear)
                : freshTerm
                    ? pLayer == CourtOfficeLayer.Central &&
                      CourtService.IsWesternElective(pKingdom)
                        ? WesternCourtElectionRules.TermEndYear(currentYear)
                        : CourtAuxiliaryLawService.ResolveTermEndYear(pKingdom,
                            age, lastEvaluation, pActor.data.id, currentYear)
                    : existing.TermEndYear;
            int waitingSinceYear = pActing ? currentYear : -1;
            var state = new OfficialCareerStateView
            {
                ActorId = pActor.data.id,
                KingdomId = pKingdom.id,
                CityId = targetCityId,
                NativeCityId = nativeCityId,
                PreviousCityId = previousCityId,
                WaitingSinceYear = waitingSinceYear,
                Rank = rank,
                Track = track,
                OfficeId = pOfficeId ?? "",
                Merit = merit,
                MeritCap = meritCap,
                TermEndYear = termEndYear,
                LastEvaluation = lastEvaluation,
                EvaluationModifierUntil = existing?.EvaluationModifierUntil ?? -1,
                Seniority = existing?.Seniority ?? 0,
                LastPopulationSnapshot = existing?.LastPopulationSnapshot ?? -1,
                LocalGrade = localGrade,
                LocalGradeReviewYear = localGradeReviewYear
            };
            Upsert(pDb, pActor, pKingdom, pCity, pOfficeId, rank, track, merit,
                meritCap, termEndYear, lastEvaluation, nativeCityId,
                previousCityId, waitingSinceYear, localGrade,
                localGradeReviewYear, existing, pTransaction);
            return new OfficialCareerAppointmentProjection
            {
                Actor = pActor,
                Kingdom = pKingdom,
                State = state,
                PreviousRank = previousRank,
                NineRankSystem = nineRank
            };
        }

        internal static bool ExtendTermEndYear(Actor pActor, Kingdom pKingdom,
            int pTermEndYear)
        {
            if (DB == null || pActor?.data == null || pKingdom?.data == null ||
                pTermEndYear < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    OfficialCareerStateTableItem.GetTableName() +
                    " SET TERM_END_YEAR=@term,UPDATED_TIME=@time " +
                    "WHERE ACTOR_ID=@actor AND KINGDOM_ID=@kingdom";
                command.Parameters.AddWithValue("@term", pTermEndYear);
                command.Parameters.AddWithValue("@time", LineageService.CurTime());
                command.Parameters.AddWithValue("@actor", pActor.data.id);
                command.Parameters.AddWithValue("@kingdom", pKingdom.id);
                if (command.ExecuteNonQuery() != 1) return false;
                pActor.data.set(LineageKeys.OFFICER_TERM_END_YEAR, pTermEndYear);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Guest officer term extension failed: " +
                                    error.Message);
                return false;
            }
        }

        internal static void PublishAppointment(
            OfficialCareerAppointmentProjection pProjection)
        {
            Actor actor = pProjection?.Actor;
            Kingdom kingdom = pProjection?.Kingdom;
            OfficialCareerStateView state = pProjection?.State;
            if (actor?.data == null || kingdom?.data == null || state == null) return;
            ProjectHotState(actor, state.Rank, state.Track, state.Merit,
                state.MeritCap, state.TermEndYear, state.LastEvaluation,
                state.EvaluationModifierUntil, state.Seniority,
                state.LastPopulationSnapshot, state.NativeCityId,
                state.PreviousCityId, state.WaitingSinceYear, state.LocalGrade,
                state.LocalGradeReviewYear);
            try
            {
                if (OfficialCareerBiographyRules.ShouldRecordRankAdvance(
                        pProjection.NineRankSystem, persistenceCommitted: true,
                        pProjection.PreviousRank, state.Rank))
                    ChronicleEvents.OnOfficialRankPromoted(actor, kingdom,
                        state.Track, pProjection.PreviousRank, state.Rank,
                        state.OfficeId);
            }
            catch { }
        }

        public static bool ClearCurrentOffice(Actor pActor, long pKingdomId,
            string pOfficeId)
        {
            if (DB == null || pActor?.data == null) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                OfficialCareerStateView state = StageClearCurrentOffice(DB,
                    transaction, pActor.data.id, pKingdomId, pOfficeId,
                    Date.getCurrentYear(), LineageService.CurTime());
                transaction.Commit();
                PublishClearedCurrentOffice(pActor, state);
                return true;
            }
            catch (Exception e)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Official career state clear failed: " + e.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static OfficialCareerStateView StageClearCurrentOffice(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            long pActorId, long pKingdomId, string pOfficeId,
            int pCurrentYear, double pUpdatedTime)
        {
            if (pDb == null || pTransaction == null || pActorId < 0L)
                return null;
            OfficialCareerStateView state = ReadState(pDb, pActorId,
                pTransaction);
            if (state == null || state.KingdomId != pKingdomId ||
                !string.IsNullOrEmpty(pOfficeId) && state.OfficeId != pOfficeId)
                return null;

            bool cityLeader = CourtCityOfficeRules.IsCityLeaderOffice(
                state.OfficeId);
            long previousCityId = cityLeader && state.CityId >= 0L
                ? state.CityId
                : state.PreviousCityId;
            int waitingSinceYear = cityLeader ? pCurrentYear : -1;
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction
            };
            command.CommandText = "UPDATE " +
                OfficialCareerStateTableItem.GetTableName() +
                " SET KINGDOM_ID=-1,CITY_ID=-1,OFFICE_ID='',MERIT=@merit," +
                "MERIT_CAP=1,TERM_END_YEAR=-1,PREVIOUS_CITY_ID=@previous," +
                "WAITING_SINCE_YEAR=@waiting,UPDATED_TIME=@time " +
                "WHERE ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@merit", state.Merit);
            command.Parameters.AddWithValue("@previous", previousCityId);
            command.Parameters.AddWithValue("@waiting", waitingSinceYear);
            command.Parameters.AddWithValue("@time", pUpdatedTime);
            command.Parameters.AddWithValue("@actor", pActorId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "official state clear did not affect one row");
            state.KingdomId = -1L;
            state.CityId = -1L;
            state.OfficeId = "";
            state.MeritCap = 1;
            state.TermEndYear = -1;
            state.PreviousCityId = previousCityId;
            state.WaitingSinceYear = waitingSinceYear;
            return state;
        }

        internal static void PublishClearedCurrentOffice(Actor pActor,
            OfficialCareerStateView pState)
        {
            if (pActor?.data == null || pState == null ||
                pActor.data.id != pState.ActorId) return;
            ProjectHotState(pActor, pState.Rank, pState.Track, pState.Merit,
                pState.MeritCap, pState.TermEndYear, pState.LastEvaluation,
                pState.EvaluationModifierUntil, pState.Seniority,
                pState.LastPopulationSnapshot, pState.NativeCityId,
                pState.PreviousCityId, pState.WaitingSinceYear,
                pState.LocalGrade, pState.LocalGradeReviewYear);
        }

        public static Dictionary<long, OfficialCareerStateView> LoadKingdomStates(
            long pKingdomId)
        {
            var result = new Dictionary<long, OfficialCareerStateView>();
            if (DB == null || pKingdomId < 0) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = SelectColumns() +
                    " WHERE KINGDOM_ID=@kingdom ORDER BY ACTOR_ID";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    OfficialCareerStateView state = ReadView(reader);
                    result[state.ActorId] = state;
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Official career state batch read failed: " + e.Message);
            }
            return result;
        }

        public static int ReadRankFast(Actor pActor)
        {
            if (pActor?.data == null) return OfficialCareerRankRules.Unranked;
            pActor.data.get(LineageKeys.OFFICER_RANK, out int rank,
                OfficialCareerRankRules.Unranked);
            return OfficialCareerRankRules.ClampRank(rank);
        }

        public static bool TryApplyManualRankChange(Actor pActor,
            Kingdom pKingdom, int pDelta, bool pSpeciallyAuthorized,
            out int pPreviousRank, out int pNextRank)
        {
            pPreviousRank = OfficialCareerRankRules.Unranked;
            pNextRank = OfficialCareerRankRules.Unranked;
            if (DB == null || pActor?.data == null ||
                pKingdom?.data == null || pActor.isRekt() ||
                !pActor.isAlive() ||
                !CourtService.HasNineRankSystem(pKingdom)) return false;

            SQLiteTransaction transaction = null;
            OfficialCareerStateView state = null;
            try
            {
                transaction = DB.BeginTransaction();
                state = ReadState(pActor.data.id, transaction);
                if (state == null || state.KingdomId != pKingdom.id)
                {
                    transaction.Rollback();
                    return false;
                }

                pPreviousRank = OfficialCareerRankRules.ClampRank(state.Rank);
                pNextRank = OfficialCareerRankRules.ApplyManualChange(
                    pPreviousRank, pDelta,
                    CourtService.HasNineRankSystem(pKingdom),
                    pSpeciallyAuthorized);
                if (pNextRank == pPreviousRank)
                {
                    transaction.Rollback();
                    return false;
                }

                using var update = new SQLiteCommand(DB)
                    { Transaction = transaction };
                update.CommandText = "UPDATE " +
                    OfficialCareerStateTableItem.GetTableName() +
                    " SET RANK=@next,UPDATED_TIME=@time" +
                    " WHERE ACTOR_ID=@actor AND KINGDOM_ID=@kingdom" +
                    " AND RANK=@previous";
                update.Parameters.AddWithValue("@next", pNextRank);
                update.Parameters.AddWithValue("@time", LineageService.CurTime());
                update.Parameters.AddWithValue("@actor", pActor.data.id);
                update.Parameters.AddWithValue("@kingdom", pKingdom.id);
                update.Parameters.AddWithValue("@previous", state.Rank);
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "manual official rank change did not affect one row");
                transaction.Commit();
            }
            catch (Exception exception)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Manual official rank change failed: " +
                                    exception.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            ProjectHotState(pActor, pNextRank, state.Track, state.Merit,
                state.MeritCap, state.TermEndYear, state.LastEvaluation,
                state.EvaluationModifierUntil, state.Seniority,
                state.LastPopulationSnapshot, state.NativeCityId,
                state.PreviousCityId, state.WaitingSinceYear,
                state.LocalGrade, state.LocalGradeReviewYear);
            try
            {
                if (pNextRank > pPreviousRank)
                    ChronicleEvents.OnOfficialRankPromoted(pActor, pKingdom,
                        state.Track, pPreviousRank, pNextRank,
                        state.OfficeId);
                LineageService.ArchiveActor(pActor, pAlive: true);
            }
            catch { }
            return true;
        }

        public static float ReadMeritFast(Actor pActor)
        {
            if (pActor?.data == null) return 0f;
            pActor.data.get(LineageKeys.OFFICER_MERIT, out float merit, 0f);
            return Math.Max(0f, merit);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (DB == null || pKingdom?.data == null || pKingdom.isRekt()) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.OFFICIAL_CAREER_LAST_YEAR,
                out int lastYear, -1);
            if (lastYear == year) return;

            Dictionary<long, OfficialCareerStateView> states =
                LoadKingdomStates(pKingdom.id);
            if (states.Count == 0)
            {
                pKingdom.data.set(LineageKeys.OFFICIAL_CAREER_LAST_YEAR, year);
                return;
            }

            Dictionary<long, EconomyView> economies = LoadEconomies(pKingdom.id);
            var mutations = new List<AnnualMutation>(states.Count);
            var orderedStates = new List<OfficialCareerStateView>(states.Values);
            orderedStates.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            int realmCityCount = CountLiveCities(pKingdom);
            bool nineRankSystem = CourtService.HasNineRankSystem(pKingdom);
            CourtTermLaw termLaw = CourtAuxiliaryLawService.GetTermLaw(pKingdom);
            int lifetimeMigrations = 0;
            int petitions = 0;
            foreach (OfficialCareerStateView state in orderedStates)
            {
                Actor actor = World.world?.units?.get(state.ActorId);
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
                actor.data.get(LineageKeys.COURT_LAYER,
                    out string runtimeLayer, "");
                bool localOffice = runtimeLayer == CourtOfficeLayer.City ||
                    runtimeLayer == CourtOfficeLayer.County ||
                    LocalCourtOfficeRules.IsLocalOffice(state.OfficeId);
                bool cityLeaderOffice = localOffice && actor.isCityLeader() ||
                    CourtCityOfficeRules.IsCityLeaderOffice(state.OfficeId);
                if (!cityLeaderOffice &&
                    state.WaitingSinceYear >= 0 &&
                    state.TermEndYear <= year &&
                    CourtService.TryExpireActingCentralOfficial(actor,
                        pKingdom, state.OfficeId))
                    continue;
                if (cityLeaderOffice &&
                    state.WaitingSinceYear >= 0 &&
                    state.TermEndYear <= year &&
                    CourtService.TryExpireActingCityGovernor(actor, pKingdom,
                        state.CityId))
                    continue;
                bool westernElectiveCentral =
                    CourtService.IsWesternElectiveCentralOffice(pKingdom,
                        state.OfficeId);
                int effectiveTermEndYear = westernElectiveCentral
                    ? CourtService.ResolveWesternElectiveTermEndYear(pKingdom,
                        state.OfficeId, state.ActorId, year)
                    : state.TermEndYear;
                if (westernElectiveCentral && effectiveTermEndYear <= year)
                {
                    if (CourtService.TryExpireWesternElectiveCentralOfficial(
                            actor, pKingdom, state.OfficeId))
                        WesternCourtElectionService.EnqueueVacancy(pKingdom,
                            state.OfficeId, state.ActorId);
                    continue;
                }
                if (petitions <
                    CourtPetitionRules.MaximumPetitionsPerKingdomYear &&
                    CourtPetitionService.TryPetition(actor, state, pKingdom,
                        year))
                    petitions++;
                EconomyView economy = state.CityId >= 0 &&
                                      economies.TryGetValue(state.CityId, out EconomyView found)
                    ? found
                    : null;
                float annualMerit = AnnualMerit(actor, state, economy);
                if (state.EvaluationModifierUntil >= year)
                    annualMerit *= OfficialCareerRankRules.EvaluationMeritMultiplier(
                        state.LastEvaluation);
                float merit = OfficialCareerRankRules.ApplyMerit(state.Merit,
                    annualMerit, state.MeritCap);
                int currentPopulation = CurrentPopulation(state.CityId);
                var mutation = new AnnualMutation
                {
                    State = state,
                    Actor = actor,
                    PreviousRank = state.Rank,
                    Rank = nineRankSystem
                        ? state.Rank
                        : OfficialCareerRankRules.Unranked,
                    Merit = merit,
                    TermEndYear = effectiveTermEndYear,
                    LastEvaluation = state.LastEvaluation,
                    EvaluationModifierUntil = state.EvaluationModifierUntil,
                    Seniority = state.Seniority,
                    LastPopulationSnapshot = currentPopulation,
                    LocalGrade = nineRankSystem
                        ? state.LocalGrade
                        : NineRankRules.Unranked,
                    LocalGradeReviewYear = nineRankSystem
                        ? state.LocalGradeReviewYear
                        : -1
                };

                if (nineRankSystem &&
                    state.Rank <= OfficialCareerRankRules.Unranked)
                {
                    mutation.LocalGrade = ResolveLocalGrade(actor, mutation,
                        year);
                    mutation.LocalGradeReviewYear = year;
                    mutation.Rank = ResolveEntryRank(actor, pKingdom,
                        mutation.LocalGrade, SafeAge(actor));
                }

                bool migratedLocalTerm = localOffice &&
                    (state.TermEndYear < 0 ||
                     state.TermEndYear == int.MaxValue);
                if (migratedLocalTerm)
                    mutation.TermEndYear = year +
                        LocalOfficialTermRules.TermLength(
                            MainAttribute(actor), (int)Math.Max(0f, merit),
                            SafeAge(actor), state.ActorId, year);

                bool migratedLifetime = !localOffice &&
                    !westernElectiveCentral &&
                    state.OfficeId != CourtOfficeId.WestMayor &&
                    termLaw != CourtTermLaw.Lifetime &&
                    state.TermEndYear == int.MaxValue &&
                    lifetimeMigrations <
                    CourtAuxiliaryLawRules.MaximumLifetimeMigrationsPerYear;
                if (migratedLifetime)
                {
                    mutation.TermEndYear =
                        CourtAuxiliaryLawService.ResolveTermEndYear(pKingdom,
                            SafeAge(actor), state.LastEvaluation,
                            state.ActorId, year);
                    lifetimeMigrations++;
                }

                bool termDue = !migratedLocalTerm && !migratedLifetime &&
                               mutation.TermEndYear <= year;
                bool westernMayorDue = termDue &&
                                       state.OfficeId == CourtOfficeId.WestMayor;
                if (termDue && localOffice)
                {
                    // The city bureau still owns rotation/refill, but the
                    // outgoing official must receive the same performance
                    // review as central officials before that handoff.
                    if (nineRankSystem &&
                        mutation.Rank > OfficialCareerRankRules.Unranked)
                        EvaluateDueOfficial(mutation, economy, year, pKingdom,
                            pPreserveTerm: true);
                    else
                        RenewDueOfficial(mutation, year, pKingdom);
                }
                else if (westernMayorDue && realmCityCount <= 1)
                    mutation.TermEndYear =
                        WesternMayorTermRules.RetryTermEndYear(year);
                else if (westernMayorDue)
                {
                    // The shared cycle advances only after the full batch commits.
                }
                else if (termDue && nineRankSystem &&
                    mutation.Rank > OfficialCareerRankRules.Unranked)
                    EvaluateDueOfficial(mutation, economy, year, pKingdom);
                else if (termDue)
                    RenewDueOfficial(mutation, year, pKingdom);
                if (nineRankSystem &&
                    NineRankRules.ShouldReview(state.LocalGradeReviewYear, year))
                {
                    mutation.LocalGrade = ResolveLocalGrade(actor, mutation, year);
                    mutation.LocalGradeReviewYear = year;
                }
                mutation.RotationDue =
                    OfficialCirculationRules.ShouldRotateLocalLeader(
                        localOffice, cityLeaderOffice, termDue,
                        realmCityCount);
                mutations.Add(mutation);
            }

            List<AnnualMutation> dueRotations;
            List<GovernorRotationRuntimeAssignment> rotationPlan =
                PrepareGovernorRotationPlan(pKingdom, mutations,
                    out dueRotations);
            if (dueRotations.Count > 0 && rotationPlan == null)
                foreach (AnnualMutation dueRotation in dueRotations)
                    dueRotation.TermEndYear = pYearAfter(year);

            if (!CommitAnnualMutations(mutations)) return;
            bool influenceChanged = false;
            foreach (AnnualMutation mutation in mutations)
            {
                ProjectHotState(mutation.Actor, mutation.Rank, mutation.State.Track,
                    mutation.Merit, mutation.State.MeritCap, mutation.TermEndYear,
                    mutation.LastEvaluation, mutation.EvaluationModifierUntil,
                    mutation.Seniority, mutation.LastPopulationSnapshot,
                    mutation.State.NativeCityId, mutation.State.PreviousCityId,
                    mutation.State.WaitingSinceYear, mutation.LocalGrade,
                    mutation.LocalGradeReviewYear);
                influenceChanged |= mutation.Rank != mutation.PreviousRank ||
                                    Math.Abs(mutation.Merit - mutation.State.Merit) > 0.0001f;
                if (mutation.Evaluated)
                    RecordEvaluation(pKingdom, mutation);
                else if (OfficialCareerBiographyRules.ShouldRecordRankAdvance(
                             nineRankSystem, persistenceCommitted: true,
                             mutation.PreviousRank, mutation.Rank))
                    ChronicleEvents.OnOfficialRankPromoted(mutation.Actor,
                        pKingdom, mutation.State.Track, mutation.PreviousRank,
                        mutation.Rank, mutation.State.OfficeId);
            }
            if (rotationPlan?.Count > 0)
                ProcessDueGovernorRotations(pKingdom, rotationPlan, year);
            if (influenceChanged) CourtDirectionService.MarkDirty(pKingdom);
            pKingdom.data.set(LineageKeys.OFFICIAL_CAREER_LAST_YEAR, year);
        }

        private static void EvaluateDueOfficial(AnnualMutation pMutation,
            EconomyView pEconomy, int pYear, Kingdom pKingdom,
            bool pPreserveTerm = false)
        {
            Actor actor = pMutation.Actor;
            OfficialCareerStateView state = pMutation.State;
            int population = pMutation.LastPopulationSnapshot;
            bool positiveGrowth = state.LastPopulationSnapshot >= 0 &&
                                  population > state.LastPopulationSnapshot;
            bool negativeGrowth = state.LastPopulationSnapshot >= 0 &&
                                  population < state.LastPopulationSnapshot;
            int mainAttribute = MainAttribute(actor);
            bool privileged = ChronicleGate.IsNobleActor(actor);
            int grade = OfficialCareerRankRules.EvaluationGrade(mainAttribute,
                privileged, state.Rank >= 17, positiveGrowth, negativeGrowth,
                OfficialCareerRankRules.DeterministicRoll(state.ActorId, pYear, 17));
            int delta = OfficialCareerRankRules.RankDelta(grade, privileged,
                OfficialCareerRankRules.DeterministicRoll(state.ActorId, pYear, 29));
            pMutation.Rank = OfficialCareerRankRules.ApplyAutomaticRankChange(
                state.Rank, delta);
            pMutation.Merit = OfficialCareerRankRules.ApplyMerit(pMutation.Merit,
                OfficialCareerRankRules.EvaluationMeritAdjustment(grade), state.MeritCap);
            pMutation.LastEvaluation = grade;
            pMutation.EvaluationModifierUntil = pYear + 4;
            pMutation.Seniority = state.Seniority + Math.Max(0, 5 - grade);
            if (!pPreserveTerm)
                pMutation.TermEndYear =
                    CourtAuxiliaryLawService.ResolveTermEndYear(pKingdom,
                        SafeAge(actor), grade, state.ActorId, pYear);
            pMutation.Evaluated = true;
        }

        private static void RenewDueOfficial(AnnualMutation pMutation,
            int pYear, Kingdom pKingdom)
        {
            if (pMutation?.Actor?.data == null || pMutation.State == null)
                return;
            pMutation.TermEndYear =
                CourtAuxiliaryLawService.ResolveTermEndYear(pKingdom,
                    SafeAge(pMutation.Actor), pMutation.LastEvaluation,
                    pMutation.State.ActorId, pYear);
        }

        private static float AnnualMerit(Actor pActor,
            OfficialCareerStateView pState, EconomyView pEconomy)
        {
            if (pState.Track == OfficialCareerRankRules.MilitaryTrack)
            {
                pActor.data.get(LineageKeys.GENERAL_MERIT, out int generalMerit, 0);
                return OfficialCareerRankRules.AnnualMilitaryMerit(generalMerit,
                    GeneralService.CountPersonalPower(pActor));
            }
            return OfficialCareerRankRules.AnnualCivilMerit(pEconomy?.Tax ?? 0f,
                pEconomy?.Food ?? 0f, pEconomy?.Unrest ?? 0f);
        }

        private static Dictionary<long, EconomyView> LoadEconomies(long pKingdomId)
        {
            var result = new Dictionary<long, EconomyView>();
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT CITY_ID,TAX_VALUE,FOOD_STABILITY,UNREST_RISK FROM " + CityEconomyStateTableItem.GetTableName() + " WHERE KINGDOM_ID=@kingdom";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result[Long(reader, 0, -1L)] = new EconomyView
                    {
                        Tax = Float(reader, 1, 0f),
                        Food = Float(reader, 2, 0f),
                        Unrest = Float(reader, 3, 0f)
                    };
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Official career economy batch read failed: " + e.Message);
            }
            return result;
        }

        private static bool CommitAnnualMutations(List<AnnualMutation> pMutations)
        {
            if (pMutations == null || pMutations.Count == 0) return true;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                using var command = new SQLiteCommand(DB) { Transaction = transaction };
                command.CommandText = "UPDATE " + OfficialCareerStateTableItem.GetTableName() +
                    " SET RANK=@rank,MERIT=@merit,TERM_END_YEAR=@term," +
                    "LAST_KAOKE=@evaluation,KAOKE_MOD_UNTIL=@modifier," +
                    "SENIORITY=@seniority,LAST_POP_SNAPSHOT=@population," +
                    "LOCAL_GRADE=@grade,LOCAL_GRADE_REVIEW_YEAR=@grade_year," +
                    "UPDATED_TIME=@time WHERE ACTOR_ID=@actor AND KINGDOM_ID=@kingdom";
                command.Parameters.Add("@rank", System.Data.DbType.Int32);
                command.Parameters.Add("@merit", System.Data.DbType.Double);
                command.Parameters.Add("@term", System.Data.DbType.Int32);
                command.Parameters.Add("@evaluation", System.Data.DbType.Int32);
                command.Parameters.Add("@modifier", System.Data.DbType.Int32);
                command.Parameters.Add("@seniority", System.Data.DbType.Int32);
                command.Parameters.Add("@population", System.Data.DbType.Int32);
                command.Parameters.Add("@grade", System.Data.DbType.Int32);
                command.Parameters.Add("@grade_year", System.Data.DbType.Int32);
                command.Parameters.Add("@time", System.Data.DbType.Double);
                command.Parameters.Add("@actor", System.Data.DbType.Int64);
                command.Parameters.Add("@kingdom", System.Data.DbType.Int64);
                double now = LineageService.CurTime();
                foreach (AnnualMutation mutation in pMutations)
                {
                    command.Parameters["@rank"].Value = mutation.Rank;
                    command.Parameters["@merit"].Value = mutation.Merit;
                    command.Parameters["@term"].Value = mutation.TermEndYear;
                    command.Parameters["@evaluation"].Value = mutation.LastEvaluation;
                    command.Parameters["@modifier"].Value = mutation.EvaluationModifierUntil;
                    command.Parameters["@seniority"].Value = mutation.Seniority;
                    command.Parameters["@population"].Value = mutation.LastPopulationSnapshot;
                    command.Parameters["@grade"].Value = mutation.LocalGrade;
                    command.Parameters["@grade_year"].Value =
                        mutation.LocalGradeReviewYear;
                    command.Parameters["@time"].Value = now;
                    command.Parameters["@actor"].Value = mutation.State.ActorId;
                    command.Parameters["@kingdom"].Value = mutation.State.KingdomId;
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "official annual update did not affect one row");
                }
                transaction.Commit();
                return true;
            }
            catch (Exception e)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Official career annual transaction failed: " + e.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        private static void RecordEvaluation(Kingdom pKingdom,
            AnnualMutation pMutation)
        {
            string grade = HistoryLocalizationRules.Text(
                "aw_hist_official_kaoke_" + pMutation.LastEvaluation);
            string detail = HistoryLocalizationRules.Text("aw_hist_official_evaluated") +
                            grade + HistoryLocalizationRules.Text("aw_hist_official_rank_change") +
                            AW_L10n.Text(
                                OfficialCareerRankRules.RankNameKey(
                                    pMutation.PreviousRank),
                                OfficialCareerRankRules.RankFallbackEnglish(
                                    pMutation.PreviousRank)) + "->" +
                            AW_L10n.Text(
                                OfficialCareerRankRules.RankNameKey(
                                    pMutation.Rank),
                                OfficialCareerRankRules.RankFallbackEnglish(
                                    pMutation.Rank));
            HistoryWriter.RecordPerson(pMutation.State.ActorId, pKingdom,
                pMutation.Actor.getName(), PersonEvent.OFFICIAL_EVALUATION,
                HistoryText.Actor(pMutation.Actor) + " " + HistoryText.PlainText(detail),
                ChronicleCategory.CAREER, HistoryTarget.Kingdom(pKingdom));
            ChronicleEvents.OnOfficialRankPromoted(pMutation.Actor, pKingdom,
                pMutation.State.Track, pMutation.PreviousRank, pMutation.Rank,
                pMutation.State.OfficeId);
        }

        private static List<GovernorRotationRuntimeAssignment>
            PrepareGovernorRotationPlan(Kingdom pKingdom,
                List<AnnualMutation> pMutations,
                out List<AnnualMutation> pDue)
        {
            List<AnnualMutation> candidates = pMutations == null
                ? new List<AnnualMutation>()
                : pMutations
                .Where(p => p?.RotationDue == true && p.Actor?.data != null)
                .OrderBy(p => p.State.TermEndYear)
                .ThenBy(p => p.State.ActorId)
                .ToList();
            List<AnnualMutation> mayorCycle = candidates.Where(p =>
                p.State.OfficeId == CourtOfficeId.WestMayor).ToList();
            pDue = mayorCycle.Count > 0
                ? mayorCycle
                : candidates.Take(
                    OfficialCirculationRules.MaximumRotationsPerKingdomYear)
                    .ToList();
            if (pKingdom?.data == null || pDue.Count == 0)
                return new List<GovernorRotationRuntimeAssignment>();

            List<City> cities = LiveCities(pKingdom);
            if (cities.Count <= 1) return null;
            var facts = new List<GovernorRotationFacts>(pDue.Count);
            foreach (AnnualMutation mutation in pDue)
            {
                City former = cities.FirstOrDefault(p =>
                    p.data.id == mutation.State.CityId);
                if (former?.leader != mutation.Actor) return null;
                facts.Add(new GovernorRotationFacts(mutation.State.ActorId,
                    former.data.id, mutation.State.NativeCityId,
                    mutation.State.TermEndYear));
            }

            if (!OfficialCirculationRules.TryBuildRotationPlan(facts,
                    out IReadOnlyList<GovernorRotationAssignment> purePlan))
                return null;
            var cityById = cities.ToDictionary(p => p.data.id);
            var mutationByActor = pDue.ToDictionary(p => p.State.ActorId);
            var result = new List<GovernorRotationRuntimeAssignment>(purePlan.Count);
            foreach (GovernorRotationAssignment assignment in purePlan)
            {
                if (!mutationByActor.TryGetValue(assignment.ActorId,
                        out AnnualMutation mutation) ||
                    !cityById.TryGetValue(assignment.CurrentCityId,
                        out City former) ||
                    !cityById.TryGetValue(assignment.DestinationCityId,
                        out City destination)) return null;
                result.Add(new GovernorRotationRuntimeAssignment
                {
                    Mutation = mutation,
                    Actor = mutation.Actor,
                    FormerCity = former,
                    FormerCityId = former.data.id,
                    DestinationCity = destination,
                    DestinationCityId = destination.data.id
                });
            }
            return ValidateGovernorRotationPlan(pKingdom, result)
                ? result
                : null;
        }

        private static bool ValidateGovernorRotationPlan(Kingdom pKingdom,
            IReadOnlyList<GovernorRotationRuntimeAssignment> pPlan)
        {
            if (pKingdom?.data == null || pPlan == null || pPlan.Count < 2)
                return false;
            var actors = new HashSet<long>();
            var sources = new HashSet<long>();
            var destinations = new HashSet<long>();
            foreach (GovernorRotationRuntimeAssignment item in pPlan)
            {
                if (item?.Actor?.data == null || item.Actor.isRekt() ||
                    !item.Actor.isAlive() || item.Actor.kingdom != pKingdom ||
                    item.FormerCity?.data == null ||
                    item.DestinationCity?.data == null ||
                    item.FormerCity.kingdom != pKingdom ||
                    item.DestinationCity.kingdom != pKingdom ||
                    item.FormerCity.data.id != item.FormerCityId ||
                    item.DestinationCity.data.id != item.DestinationCityId ||
                    item.FormerCity.leader != item.Actor ||
                    item.FormerCity == item.DestinationCity ||
                    item.FormerCity.isGettingCaptured() ||
                    item.DestinationCity.isGettingCaptured() ||
                    !actors.Add(item.Actor.data.id) ||
                    !sources.Add(item.FormerCity.data.id) ||
                    !destinations.Add(item.DestinationCity.data.id))
                    return false;
            }
            return sources.SetEquals(destinations);
        }

        private static void ProcessDueGovernorRotations(Kingdom pKingdom,
            List<GovernorRotationRuntimeAssignment> pPlan, int pYear)
        {
            if (!ValidateGovernorRotationPlan(pKingdom, pPlan)) return;
            bool mayorCycle = pPlan.Any(p =>
                p.Mutation.State.OfficeId == CourtOfficeId.WestMayor);
            int nextMayorCycleEndYear = -1;
            if (mayorCycle)
            {
                pKingdom.data.get(LineageKeys.WESTERN_MAYOR_CYCLE_END_YEAR,
                    out int sharedCycleEndYear, -1);
                nextMayorCycleEndYear =
                    WesternMayorTermRules.AdvanceExpiredCycleEndYear(
                        pYear, sharedCycleEndYear);
                foreach (GovernorRotationRuntimeAssignment item in pPlan)
                    if (item.Mutation.State.OfficeId == CourtOfficeId.WestMayor)
                        item.Mutation.TermEndYear = nextMayorCycleEndYear;
            }
            if (!CommitGovernorRotationPersistence(pKingdom, pPlan))
            {
                bool deferred = ScheduleGovernorRotationRetry(pKingdom, pPlan,
                    pYear);
                ModClass.LogWarning(
                    "Governor circulation persistence rejected batch" +
                    (deferred ? "" : "; retry defer persistence failed"));
                return;
            }

            if (mayorCycle)
                pKingdom.data.set(LineageKeys.WESTERN_MAYOR_CYCLE_END_YEAR,
                    nextMayorCycleEndYear);
            using (GovernorRotationRuntimeScope.Enter())
                PublishCommittedGovernorRotation(pPlan);
        }

        private static void PublishCommittedGovernorRotation(
            IReadOnlyList<GovernorRotationRuntimeAssignment> pPlan)
        {
            if (pPlan == null) return;
            foreach (GovernorRotationRuntimeAssignment item in pPlan)
            {
                FreezeNativeCityFast(item.Actor);
                ProjectCommittedGovernorState(item);
                ProjectCommittedGovernorCourtCity(item);
                ProjectCommittedGovernorPreviousCity(item);
                ReconcileCommittedGovernorRuntime(item);
            }
        }

        private static void ProjectCommittedGovernorState(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                if (pItem?.Mutation?.State == null)
                    throw new InvalidOperationException("missing career state");
                pItem.Mutation.State.PreviousCityId = pItem.FormerCityId;
                pItem.Mutation.State.CityId = pItem.DestinationCityId;
                pItem.Mutation.State.TermEndYear =
                    pItem.Mutation.TermEndYear;
                pItem.Actor.data.set(LineageKeys.OFFICER_TERM_END_YEAR,
                    pItem.Mutation.TermEndYear);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Committed governor State projection failed: " + e.Message);
            }
        }

        private static void ProjectCommittedGovernorCourtCity(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                if (pItem?.Actor?.data == null)
                    throw new InvalidOperationException("missing live actor");
                pItem.Actor.data.set(LineageKeys.COURT_CITY_ID,
                    pItem.DestinationCityId);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Committed governor court-city projection failed: " +
                    e.Message);
            }
        }

        private static void ProjectCommittedGovernorPreviousCity(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                if (pItem?.Actor?.data == null)
                    throw new InvalidOperationException("missing live actor");
                pItem.Actor.data.set(LineageKeys.OFFICER_PREVIOUS_CITY_ID,
                    pItem.FormerCityId);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Committed governor previous-city projection failed: " +
                    e.Message);
            }
        }

        private static void ReconcileCommittedGovernorRuntime(
            GovernorRotationRuntimeAssignment pItem)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                TryRemoveCommittedFormerLeader(pItem);
                TryMoveCommittedGovernor(pItem);
                TryAssignCommittedDestinationLeader(pItem);
                if (ValidateCommittedGovernorRuntime(pItem))
                {
                    CityGovernorPlacementService.OnCommittedAssignment(
                        pItem.DestinationCity, pItem.Actor);
                    return;
                }
            }
            ModClass.LogWarning(
                "Committed governor live reconciliation remained incomplete: actor=" +
                (pItem?.Actor?.data?.id ?? -1L));
        }

        private static void TryRemoveCommittedFormerLeader(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                if (pItem?.FormerCity?.data == null)
                    throw new InvalidOperationException("missing former city");
                if (pItem.FormerCity.leader == pItem.Actor)
                    pItem.FormerCity.removeLeader();
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Committed governor former-leader cleanup failed: " +
                    e.Message);
            }
        }

        private static void TryMoveCommittedGovernor(
            GovernorRotationRuntimeAssignment pItem)
        {
            if (pItem?.Actor?.data == null ||
                pItem.DestinationCity?.data == null)
            {
                ModClass.LogWarning(
                    "Committed governor move failed: missing live identity");
                return;
            }
            if (pItem.Actor.city == pItem.DestinationCity) return;
            try { pItem.Actor.stopBeingWarrior(); }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Committed governor military release failed: " + e.Message);
            }
            try { pItem.Actor.joinCity(pItem.DestinationCity); }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Committed governor destination move failed: " + e.Message);
            }
        }

        private static void TryAssignCommittedDestinationLeader(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                if (pItem?.DestinationCity?.data == null ||
                    pItem.Actor?.data == null)
                    throw new InvalidOperationException(
                        "missing destination leader identity");
                if (pItem.DestinationCity.leader != pItem.Actor)
                    pItem.DestinationCity.setLeader(pItem.Actor, pNew: true);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Committed governor destination leader failed: " +
                    e.Message);
            }
        }

        private static bool ValidateCommittedGovernorRuntime(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                return pItem?.Actor?.data != null &&
                       pItem.DestinationCity?.data != null &&
                       pItem.Actor.city == pItem.DestinationCity &&
                       pItem.DestinationCity.leader == pItem.Actor &&
                       (pItem.FormerCity?.data == null ||
                        pItem.FormerCity.leader != pItem.Actor);
            }
            catch { return false; }
        }

        private static bool CommitGovernorRotationPersistence(Kingdom pKingdom,
            IReadOnlyList<GovernorRotationRuntimeAssignment> pPlan)
        {
            if (DB == null || pKingdom?.data == null || pPlan == null)
                return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                double now = LineageService.CurTime();
                foreach (GovernorRotationRuntimeAssignment item in pPlan)
                {
                    using var career = new SQLiteCommand(DB)
                        { Transaction = transaction };
                    career.CommandText = "UPDATE " +
                        OfficialCareerStateTableItem.GetTableName() +
                        " SET CITY_ID=@destination,PREVIOUS_CITY_ID=@former," +
                        "TERM_END_YEAR=@term," +
                        "UPDATED_TIME=@time WHERE ACTOR_ID=@actor AND " +
                        "KINGDOM_ID=@kingdom AND CITY_ID=@former AND OFFICE_ID=@office";
                    AddRotationParameters(career, item, pKingdom.id, now);
                    career.Parameters.AddWithValue("@office",
                        item.Mutation.State.OfficeId);
                    if (career.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }

                    using var court = new SQLiteCommand(DB)
                        { Transaction = transaction };
                    court.CommandText = "UPDATE " +
                        CourtOfficerTableItem.GetTableName() +
                        " SET CITY_ID=@destination,UPDATED_TIME=@time " +
                        "WHERE ACTOR_ID=@actor AND KINGDOM_ID=@kingdom AND " +
                        "CITY_ID=@former AND LAYER=@layer AND OFFICE_ID=@office " +
                        "AND ACTIVE=1";
                    AddRotationParameters(court, item, pKingdom.id, now);
                    court.Parameters.AddWithValue("@layer", CourtOfficeLayer.City);
                    court.Parameters.AddWithValue("@office",
                        item.Mutation.State.OfficeId);
                    if (court.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction?.Rollback(); } catch { }
                return false;
            }
            finally { try { transaction?.Dispose(); } catch { } }
        }

        private static void AddRotationParameters(SQLiteCommand pCommand,
            GovernorRotationRuntimeAssignment pItem, long pKingdomId,
            double pTime)
        {
            pCommand.Parameters.AddWithValue("@destination",
                pItem.DestinationCityId);
            pCommand.Parameters.AddWithValue("@former",
                pItem.FormerCityId);
            pCommand.Parameters.AddWithValue("@time", pTime);
            pCommand.Parameters.AddWithValue("@actor", pItem.Actor.data.id);
            pCommand.Parameters.AddWithValue("@kingdom", pKingdomId);
            pCommand.Parameters.AddWithValue("@term",
                pItem.Mutation.TermEndYear);
        }

        private static bool RestoreGovernorRotation(
            IReadOnlyList<GovernorRotationRuntimeAssignment> pPlan)
        {
            if (pPlan == null || pPlan.Count == 0) return false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                foreach (GovernorRotationRuntimeAssignment item in pPlan)
                    TryRemoveTentativeDestinationLeader(item);
                foreach (GovernorRotationRuntimeAssignment item in pPlan)
                    TryRestoreGovernorActorCity(item);
                foreach (GovernorRotationRuntimeAssignment item in pPlan)
                    TryRestoreGovernorFormerLeader(item);
                if (ValidateRestoredGovernorRotation(pPlan)) return true;
            }
            return false;
        }

        private static void TryRemoveTentativeDestinationLeader(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                if (pItem?.DestinationCity?.leader == pItem.Actor)
                    pItem.DestinationCity.removeLeader();
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Governor rollback destination cleanup failed: " + e.Message);
            }
        }

        private static void TryRestoreGovernorActorCity(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                if (pItem?.Actor?.data == null || pItem.FormerCity?.data == null)
                    throw new InvalidOperationException(
                        "missing former actor or city");
                if (pItem.Actor.city != pItem.FormerCity)
                    pItem.Actor.joinCity(pItem.FormerCity);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Governor rollback actor-city repair failed: " + e.Message);
            }
        }

        private static void TryRestoreGovernorFormerLeader(
            GovernorRotationRuntimeAssignment pItem)
        {
            try
            {
                if (pItem?.Actor?.data == null || pItem.FormerCity?.data == null)
                    throw new InvalidOperationException(
                        "missing former leader identity");
                if (pItem.FormerCity.leader != pItem.Actor)
                    pItem.FormerCity.setLeader(pItem.Actor, pNew: true);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Governor rollback former-leader repair failed: " +
                    e.Message);
            }
        }

        private static bool ValidateRestoredGovernorRotation(
            IReadOnlyList<GovernorRotationRuntimeAssignment> pPlan)
        {
            if (pPlan == null || pPlan.Count == 0) return false;
            foreach (GovernorRotationRuntimeAssignment item in pPlan)
                if (item?.Actor?.data == null || item.FormerCity?.data == null ||
                    item.Actor.city != item.FormerCity ||
                    item.FormerCity.leader != item.Actor)
                    return false;
            return true;
        }

        private static void ScheduleGovernorRotationRuntimeRepair(
            Kingdom pKingdom,
            IReadOnlyList<GovernorRotationRuntimeAssignment> pPlan,
            int pAttempt)
        {
            if (pKingdom?.data == null || pPlan == null || pPlan.Count == 0 ||
                pAttempt < 0 ||
                pAttempt >= MaximumGovernorRollbackRepairAttempts) return;
            long kingdomId = pKingdom.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "governor_rotation_rollback", kingdomId),
                DeferredWorkClass.Runtime,
                () => RepairGovernorRotationRuntime(pKingdom, pPlan, pAttempt));
        }

        private static void RepairGovernorRotationRuntime(Kingdom pKingdom,
            IReadOnlyList<GovernorRotationRuntimeAssignment> pPlan,
            int pAttempt)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            using (GovernorRotationRuntimeScope.Enter())
                if (RestoreGovernorRotation(pPlan)) return;
            int nextAttempt = pAttempt + 1;
            if (nextAttempt < MaximumGovernorRollbackRepairAttempts)
            {
                ScheduleGovernorRotationRuntimeRepair(pKingdom, pPlan,
                    nextAttempt);
                return;
            }
            ModClass.LogWarning(
                "Governor circulation runtime rollback exhausted bounded repairs: kingdom=" +
                pKingdom.id + " assignments=" + pPlan.Count);
        }

        private static bool ScheduleGovernorRotationRetry(Kingdom pKingdom,
            IReadOnlyList<GovernorRotationRuntimeAssignment> pPlan, int pYear)
        {
            if (DB == null || pKingdom?.data == null || pPlan == null ||
                pPlan.Count == 0) return false;
            foreach (GovernorRotationRuntimeAssignment item in pPlan)
                if (item?.Actor?.data == null || item.FormerCity?.data == null)
                    return false;

            int retryYear = pYearAfter(pYear);
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                double now = LineageService.CurTime();
                foreach (GovernorRotationRuntimeAssignment item in pPlan)
                {
                    using var command = new SQLiteCommand(DB) { Transaction = transaction };
                    command.CommandText = "UPDATE " +
                        OfficialCareerStateTableItem.GetTableName() +
                        " SET TERM_END_YEAR=@year,UPDATED_TIME=@time " +
                        "WHERE ACTOR_ID=@actor AND KINGDOM_ID=@kingdom AND " +
                        "CITY_ID=@city AND OFFICE_ID=@office";
                    command.Parameters.AddWithValue("@year", retryYear);
                    command.Parameters.AddWithValue("@time", now);
                    command.Parameters.AddWithValue("@actor", item.Actor.data.id);
                    command.Parameters.AddWithValue("@kingdom", pKingdom.id);
                    command.Parameters.AddWithValue("@city",
                        item.FormerCityId);
                    command.Parameters.AddWithValue("@office",
                        item.Mutation.State.OfficeId);
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "governor retry did not affect one career row");
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Governor circulation retry defer failed: " +
                                    e.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            foreach (GovernorRotationRuntimeAssignment item in pPlan)
            {
                try
                {
                    item.Actor.data.set(LineageKeys.OFFICER_TERM_END_YEAR,
                        retryYear);
                }
                catch (Exception e)
                {
                    ModClass.LogWarning(
                        "Committed governor retry projection failed: " +
                        e.Message);
                }
            }
            return true;
        }

        private static int pYearAfter(int pYear)
        {
            return pYear >= int.MaxValue ? int.MaxValue : pYear + 1;
        }

        private static int ResolveWesternMayorTermEndYear(Kingdom pKingdom,
            int pCurrentYear)
        {
            pKingdom.data.get(LineageKeys.WESTERN_MAYOR_CYCLE_END_YEAR,
                out int sharedCycleEndYear, -1);
            int result = WesternMayorTermRules.AppointmentTermEndYear(
                pCurrentYear, sharedCycleEndYear);
            if (sharedCycleEndYear < 0)
                pKingdom.data.set(
                    LineageKeys.WESTERN_MAYOR_CYCLE_END_YEAR, result);
            return result;
        }

        private static int CountLiveCities(Kingdom pKingdom)
        {
            return LiveCities(pKingdom).Count;
        }

        private static List<City> LiveCities(Kingdom pKingdom)
        {
            var result = new List<City>();
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && !city.isRekt()) result.Add(city);
            }
            catch { }
            return result;
        }

        private static int CurrentPopulation(long pCityId)
        {
            if (pCityId < 0) return -1;
            try { return World.world?.cities?.get(pCityId)?.getPopulationPeople() ?? -1; }
            catch { return -1; }
        }

        private static int MainAttribute(Actor pActor)
        {
            return (int)Math.Max(Math.Max(SafeStat(pActor, "intelligence"),
                    SafeStat(pActor, "stewardship")),
                Math.Max(SafeStat(pActor, "warfare"), SafeStat(pActor, "diplomacy")));
        }

        internal static int EstimateLocalGradeFast(Actor pActor,
            Kingdom pKingdom)
        {
            return EstimateLocalGradeFast(pActor, pKingdom,
                CourtService.HasNineRankSystem(pKingdom));
        }

        internal static int EstimateLocalGradeFast(Actor pActor,
            Kingdom pKingdom, bool pNineRankSystem)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !pNineRankSystem)
                return NineRankRules.Unranked;
            pActor.data.get(LineageKeys.OFFICER_LOCAL_GRADE,
                out int persisted, -1);
            pActor.data.get(LineageKeys.OFFICER_LOCAL_GRADE_REVIEW_YEAR,
                out int reviewYear, -1);
            int year = Date.getCurrentYear();
            if (persisted >= NineRankRules.HighestGrade &&
                persisted <= NineRankRules.LowestGrade &&
                !NineRankRules.ShouldReview(reviewYear, year))
                return persisted;
            return ResolveLocalGrade(pActor, pState: null, year);
        }

        internal static int EstimateAppointmentRankFast(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !CourtService.HasNineRankSystem(pKingdom))
                return OfficialCareerRankRules.Unranked;
            pActor.data.get(LineageKeys.OFFICER_RANK, out int existing, -1);
            if (existing > 0) return OfficialCareerRankRules.ClampRank(existing);
            return ResolveEntryRank(pActor, pKingdom,
                EstimateLocalGradeFast(pActor, pKingdom), SafeAge(pActor));
        }

        internal static int ResolveAppointmentRankFast(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId,
            bool pActing = false, bool pVacancyPromotion = false,
            bool pAllowLocalLowerQualification = false, City pCity = null)
        {
            int localGrade = EstimateLocalGradeFast(pActor, pKingdom);
            return ResolveAppointmentRankFast(pActor, pKingdom, pLayer,
                pOfficeId, pActing, pVacancyPromotion, pExisting: null,
                localGrade, pAllowLocalLowerQualification, pCity);
        }

        private static int ResolveAppointmentRankFast(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId, bool pActing,
            bool pVacancyPromotion, OfficialCareerStateView pExisting,
            int pLocalGrade, bool pAllowLocalLowerQualification = false,
            City pCity = null)
        {
            if (pActor?.data == null || pKingdom?.data == null)
                return OfficialCareerRankRules.Unranked;
            bool hasNineRankSystem = CourtService.HasNineRankSystem(pKingdom);
            if (!hasNineRankSystem)
                return OfficialCareerRankRules.Unranked;
            int existingRank = pExisting?.Rank ?? OfficialCareerRankRules.Unranked;
            if (existingRank <= OfficialCareerRankRules.Unranked)
            {
                pActor.data.get(LineageKeys.OFFICER_RANK,
                    out existingRank, OfficialCareerRankRules.Unranked);
            }
            if (pActing)
                return OfficialCareerRankRules.ResolveActingAppointmentRank(
                    existingRank, hasNineRankSystem);
            bool examinationSystem = CivilServiceQualificationService.
                HasExaminationSystem(pKingdom);
            bool appointmentExempt = CivilServiceQualificationService.
                IsAppointmentExempt(pActor, pKingdom, pLayer, pOfficeId);
            if (!examinationSystem)
            {
                int appointmentOfficeGrade = OfficeGradeForOffice(pKingdom,
                    pLayer, pOfficeId, pCity);
                bool regionalGovernorSeat = IsRegionalGovernorSeat(pKingdom,
                    pLayer, pOfficeId, pCity);
                return pLayer == CourtOfficeLayer.City ||
                    pLayer == CourtOfficeLayer.County
                    ? pVacancyPromotion
                        ? OfficialCareerRankRules.
                            ResolveLocalVacancyPromotionRank(existingRank,
                                appointmentOfficeGrade,
                                hasNineRankSystem: true,
                                hasFormalQualification: true,
                                vacancyPromotion: true,
                                regionalGovernor: regionalGovernorSeat)
                        : OfficialCareerRankRules.
                            ResolveInitialLocalAppointmentRank(existingRank,
                                appointmentOfficeGrade,
                                hasNineRankSystem: true,
                                hasFormalQualification: true, entryBonus: 0,
                                regionalGovernor: regionalGovernorSeat)
                    : pVacancyPromotion
                        ? OfficialCareerRankRules.ResolveVacancyPromotionRank(
                            existingRank, appointmentOfficeGrade,
                            hasNineRankSystem: true,
                            hasFormalQualification: true,
                            vacancyPromotion: true)
                        : OfficialCareerRankRules.ResolveInitialAppointmentRank(
                            existingRank, appointmentOfficeGrade,
                            hasNineRankSystem: true,
                            hasFormalQualification: true, entryBonus: 0);
            }
            if (appointmentExempt)
            {
                return existingRank > OfficialCareerRankRules.Unranked
                    ? OfficialCareerRankRules.ClampRank(existingRank)
                    : ResolveEntryRank(pActor, pKingdom, pLocalGrade,
                        SafeAge(pActor));
            }

            CivilServiceQualificationRecord qualification =
                CivilServiceQualificationService.LoadOrRepair(pActor, pKingdom);
            bool hasFormalQualification = CivilServiceExamRules.
                IsFormalAppointmentQualification(
                    qualification?.Qualification);
            bool localOffice = pLayer == CourtOfficeLayer.City ||
                pLayer == CourtOfficeLayer.County;
            bool hasLocalQualification = pAllowLocalLowerQualification &&
                localOffice && (pActor.isCityLeader() ||
                    LocalOfficialCandidateRules.AcceptsAppointmentQualification(
                        qualification?.Qualification ?? "none",
                        CivilServiceQualificationService.HasFailedHigherStage(
                            pActor, pKingdom),
                        allowLocalLowerQualification: true));
            bool hasLegacyCredential = CivilServiceLegacyTransitionService.
                HasUsableCredential(pActor, pKingdom, pLayer, pOfficeId);
            int officeGrade = OfficeGradeForOffice(pKingdom, pLayer,
                pOfficeId, pCity);
            bool regionalGovernor = IsRegionalGovernorSeat(pKingdom, pLayer,
                pOfficeId, pCity);
            bool allowUnqualifiedLocalFallback =
                (pLayer == CourtOfficeLayer.County
                    ? LocalLowOfficeVacancyRules.CanUseCountyFallback(
                        true, officeGrade,
                        pVacancyPromotion && pAllowLocalLowerQualification)
                    : LocalLowOfficeVacancyRules.CanUseUnqualifiedFallback(
                        localOffice, officeGrade,
                        pVacancyPromotion && pAllowLocalLowerQualification));
            bool hasAppointmentQualification = hasFormalQualification ||
                hasLocalQualification || hasLegacyCredential ||
                allowUnqualifiedLocalFallback;
            if (pVacancyPromotion)
            {
                if (localOffice)
                    return OfficialCareerRankRules.
                        ResolveLocalVacancyPromotionRank(existingRank,
                            officeGrade, hasNineRankSystem: true,
                            hasAppointmentQualification,
                            vacancyPromotion: true,
                            qualification?.EntryBonus ?? 0,
                            regionalGovernor);
                return OfficialCareerRankRules.ResolveVacancyPromotionRank(
                    existingRank, officeGrade, hasNineRankSystem: true,
                    hasAppointmentQualification, vacancyPromotion: true,
                    qualification?.EntryBonus ?? 0);
            }
            return localOffice
                ? OfficialCareerRankRules.ResolveInitialLocalAppointmentRank(
                    existingRank, officeGrade, hasNineRankSystem: true,
                    hasAppointmentQualification,
                    qualification?.EntryBonus ?? 0, regionalGovernor)
                : OfficialCareerRankRules.ResolveInitialAppointmentRank(
                    existingRank, officeGrade, hasNineRankSystem: true,
                    hasAppointmentQualification,
                    qualification?.EntryBonus ?? 0);
        }

        private static int ResolveEntryRank(Actor pActor, Kingdom pKingdom,
            int pLocalGrade, int pAge)
        {
            int rank = OfficialCareerRankRules.EntryRank(
                pActor.isCityLeader() ||
                GeneralService.IsActiveGeneralFast(pActor),
                !string.IsNullOrEmpty(
                    SchoolMembershipService.GetSchool(pActor.data.id)),
                pAge, IsRoyal(pActor, pKingdom),
                ChronicleGate.IsNobleActor(pActor));
            return OfficialCareerRankRules.ApplyEntryRankBonus(rank,
                NineRankRules.EntryRankBonus(pLocalGrade));
        }

        private static int ResolveLocalGrade(Actor pActor,
            OfficialCareerStateView pState, int pYear)
        {
            int evaluation = pState?.LastEvaluation ?? 2;
            float meritRatio = pState == null || pState.MeritCap <= 0
                ? 0f
                : pState.Merit / pState.MeritCap;
            return ResolveLocalGrade(pActor, evaluation, meritRatio, pYear);
        }

        private static int ResolveLocalGrade(Actor pActor,
            AnnualMutation pMutation, int pYear)
        {
            float meritRatio = pMutation.State.MeritCap <= 0
                ? 0f
                : pMutation.Merit / pMutation.State.MeritCap;
            return ResolveLocalGrade(pActor, pMutation.LastEvaluation,
                meritRatio, pYear);
        }

        private static int ResolveLocalGrade(Actor pActor,
            int pEvaluation, float pMeritRatio, int pYear)
        {
            SchoolMembershipRecord membership = pActor?.data == null
                ? null
                : SchoolMembershipService.GetActive(pActor.data.id);
            int schoolYears = membership == null
                ? 0
                : Math.Max(0, pYear - membership.StartYear);
            return NineRankRules.ResolveGrade(
                ChronicleGate.IsNobleActor(pActor), MainAttribute(pActor),
                schoolYears, membership?.Reputation ?? 0f,
                membership == null ? 0 : (int)membership.Standing,
                pEvaluation, pMeritRatio);
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }

        private static void Upsert(SQLiteConnection pDb, Actor pActor,
            Kingdom pKingdom, City pCity,
            string pOfficeId, int pRank, int pTrack, float pMerit, int pMeritCap,
            int pTermEndYear, int pLastEvaluation, long pNativeCityId,
            long pPreviousCityId, int pWaitingSinceYear, int pLocalGrade,
            int pLocalGradeReviewYear, OfficialCareerStateView pExisting,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            if (pExisting == null)
            {
                command.CommandText = "INSERT INTO " + OfficialCareerStateTableItem.GetTableName() +
                    " (ACTOR_ID,ACTOR_NAME,KINGDOM_ID,CITY_ID,NATIVE_CITY_ID," +
                    "PREVIOUS_CITY_ID,WAITING_SINCE_YEAR,RANK,TRACK,OFFICE_ID," +
                    "MERIT,MERIT_CAP,TERM_END_YEAR,LAST_KAOKE,KAOKE_MOD_UNTIL," +
                    "SENIORITY,LAST_POP_SNAPSHOT,LOCAL_GRADE,LOCAL_GRADE_REVIEW_YEAR," +
                    "UPDATED_TIME) VALUES " +
                    "(@actor,@name,@kingdom,@city,@native,@previous,@waiting,@rank," +
                    "@track,@office,@merit,@cap,@term,@evaluation,-1,0,-1,@grade," +
                    "@grade_year,@time)";
            }
            else
            {
                command.CommandText = "UPDATE " + OfficialCareerStateTableItem.GetTableName() +
                    " SET ACTOR_NAME=@name,KINGDOM_ID=@kingdom,CITY_ID=@city,RANK=@rank," +
                    "TRACK=@track,OFFICE_ID=@office,MERIT=@merit,MERIT_CAP=@cap," +
                    "TERM_END_YEAR=@term,LAST_KAOKE=@evaluation,NATIVE_CITY_ID=@native," +
                    "PREVIOUS_CITY_ID=@previous,WAITING_SINCE_YEAR=@waiting," +
                    "LOCAL_GRADE=@grade,LOCAL_GRADE_REVIEW_YEAR=@grade_year," +
                    "UPDATED_TIME=@time " +
                    "WHERE ACTOR_ID=@actor";
            }
            command.Parameters.AddWithValue("@actor", pActor.data.id);
            command.Parameters.AddWithValue("@name", pActor.getName() ?? "");
            command.Parameters.AddWithValue("@kingdom", pKingdom.id);
            command.Parameters.AddWithValue("@city", pCity?.data?.id ?? -1L);
            command.Parameters.AddWithValue("@native", pNativeCityId);
            command.Parameters.AddWithValue("@previous", pPreviousCityId);
            command.Parameters.AddWithValue("@waiting", pWaitingSinceYear);
            command.Parameters.AddWithValue("@rank", pRank);
            command.Parameters.AddWithValue("@track", pTrack);
            command.Parameters.AddWithValue("@office", pOfficeId ?? "");
            command.Parameters.AddWithValue("@merit", pMerit);
            command.Parameters.AddWithValue("@cap", pMeritCap);
            command.Parameters.AddWithValue("@term", pTermEndYear);
            command.Parameters.AddWithValue("@evaluation", pLastEvaluation);
            command.Parameters.AddWithValue("@grade", NineRankRules.ClampGrade(pLocalGrade));
            command.Parameters.AddWithValue("@grade_year", pLocalGradeReviewYear);
            command.Parameters.AddWithValue("@time", LineageService.CurTime());
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("official state upsert did not affect one row");
        }

        private static OfficialCareerStateView ReadState(long pActorId,
            SQLiteTransaction pTransaction)
        {
            return ReadState(DB, pActorId, pTransaction);
        }

        private static OfficialCareerStateView ReadState(SQLiteConnection pDb,
            long pActorId, SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = SelectColumns() + " WHERE ACTOR_ID=@actor LIMIT 1";
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadView(reader) : null;
        }

        private static string SelectColumns()
        {
            return "SELECT ACTOR_ID,KINGDOM_ID,CITY_ID,RANK,TRACK,IFNULL(OFFICE_ID,'')," +
                   "MERIT,MERIT_CAP,TERM_END_YEAR,LAST_KAOKE,KAOKE_MOD_UNTIL," +
                   "SENIORITY,LAST_POP_SNAPSHOT,NATIVE_CITY_ID,PREVIOUS_CITY_ID," +
                   "WAITING_SINCE_YEAR,LOCAL_GRADE,LOCAL_GRADE_REVIEW_YEAR FROM " +
                   OfficialCareerStateTableItem.GetTableName();
        }

        private static OfficialCareerStateView ReadView(SQLiteDataReader pReader)
        {
            return new OfficialCareerStateView
            {
                ActorId = Long(pReader, 0, -1L),
                KingdomId = Long(pReader, 1, -1L),
                CityId = Long(pReader, 2, -1L),
                Rank = OfficialCareerRankRules.ClampRank(Int(pReader, 3,
                    OfficialCareerRankRules.Unranked)),
                Track = Int(pReader, 4, OfficialCareerRankRules.CivilTrack),
                OfficeId = Text(pReader, 5),
                Merit = Float(pReader, 6, 0f),
                MeritCap = Int(pReader, 7, 1),
                TermEndYear = Int(pReader, 8, -1),
                LastEvaluation = Int(pReader, 9, 2),
                EvaluationModifierUntil = Int(pReader, 10, -1),
                Seniority = Int(pReader, 11, 0),
                LastPopulationSnapshot = Int(pReader, 12, -1),
                NativeCityId = Long(pReader, 13, -1L),
                PreviousCityId = Long(pReader, 14, -1L),
                WaitingSinceYear = Int(pReader, 15, -1),
                LocalGrade = NineRankRules.ClampGrade(Int(pReader, 16,
                    NineRankRules.Unranked)),
                LocalGradeReviewYear = Int(pReader, 17, -1)
            };
        }

        private static void ProjectHotState(Actor pActor, int pRank, int pTrack,
            float pMerit, int pMeritCap, int pTermEndYear, int pLastEvaluation,
            int pModifierUntil, int pSeniority, int pLastPopulation,
            long pNativeCityId, long pPreviousCityId, int pWaitingSinceYear,
            int pLocalGrade, int pLocalGradeReviewYear)
        {
            pActor.data.set(LineageKeys.OFFICER_RANK,
                OfficialCareerRankRules.ClampRank(pRank));
            pActor.data.set(LineageKeys.OFFICER_TRACK, pTrack);
            pActor.data.set(LineageKeys.OFFICER_MERIT, pMerit);
            pActor.data.set(LineageKeys.OFFICER_MERIT_CAP, pMeritCap);
            pActor.data.set(LineageKeys.OFFICER_TERM_END_YEAR, pTermEndYear);
            pActor.data.set(LineageKeys.OFFICER_LAST_KAOKE, pLastEvaluation);
            pActor.data.set(LineageKeys.OFFICER_KAOKE_MOD_UNTIL, pModifierUntil);
            pActor.data.set(LineageKeys.OFFICER_SENIORITY, pSeniority);
            pActor.data.set(LineageKeys.OFFICER_LAST_POP_SNAPSHOT, pLastPopulation);
            pActor.data.set(LineageKeys.OFFICER_NATIVE_CITY_ID, pNativeCityId);
            pActor.data.set(LineageKeys.OFFICER_PREVIOUS_CITY_ID, pPreviousCityId);
            pActor.data.set(LineageKeys.OFFICER_WAITING_SINCE_YEAR, pWaitingSinceYear);
            pActor.data.set(LineageKeys.OFFICER_LOCAL_GRADE,
                NineRankRules.ClampGrade(pLocalGrade));
            pActor.data.set(LineageKeys.OFFICER_LOCAL_GRADE_REVIEW_YEAR,
                pLocalGradeReviewYear);
        }

        internal static void FreezeNativeCityFast(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.OFFICER_NATIVE_CITY_ID,
                out long existing, -1L);
            if (existing >= 0) return;
            long cityId = pActor.city?.data?.id ?? pActor.data.cityID;
            if (cityId >= 0)
                pActor.data.set(LineageKeys.OFFICER_NATIVE_CITY_ID, cityId);
        }

        private static long ResolveNativeCityId(Actor pActor,
            OfficialCareerStateView pExisting)
        {
            if (pExisting?.NativeCityId >= 0) return pExisting.NativeCityId;
            pActor.data.get(LineageKeys.OFFICER_NATIVE_CITY_ID,
                out long hotNative, -1L);
            if (hotNative >= 0) return hotNative;
            return pActor.city?.data?.id ?? pActor.data.cityID;
        }

        internal static int OfficeGradeForOffice(string pOfficeId)
        {
            CourtOfficeDefinition definition =
                CourtProfileRegistry.FindOfficeAcrossProfiles(pOfficeId);
            if (definition != null) return definition.Grade;
            return string.IsNullOrEmpty(pOfficeId) ? 0 : 30;
        }

        internal static int OfficeGradeForOffice(Kingdom pKingdom,
            string pLayer, string pOfficeId, City pCity = null)
        {
            if (string.IsNullOrEmpty(pOfficeId)) return 0;
            if (pLayer == CourtOfficeLayer.City &&
                CustomCourtRuntime.TryGetLocalTemplate(pKingdom, pCity,
                    out CustomLocalCourtTemplate local))
            {
                CustomCourtOffice office = (local.Offices ??
                    new List<CustomCourtOffice>()).FirstOrDefault(item =>
                    item != null && item.Id == pOfficeId);
                if (office != null) return office.Grade;
            }
            if (pLayer != CourtOfficeLayer.City &&
                CustomCourtRuntime.TryGetSnapshot(pKingdom,
                    out CustomCourtTemplate snapshot))
            {
                CustomCourtOffice office = CustomCourtTemplateRules.FindOffice(
                    snapshot, pOfficeId);
                if (office != null) return office.Grade;
            }
            CourtOfficeDefinition definition = CourtProfileRegistry.FindOffice(
                pKingdom, pOfficeId) ??
                CourtProfileRegistry.FindOfficeAcrossProfiles(pOfficeId);
            return definition?.Grade ?? 30;
        }

        internal static bool IsRegionalGovernorSeat(Kingdom pKingdom,
            string pLayer, string pOfficeId, City pCity)
        {
            if (pLayer != CourtOfficeLayer.City || pKingdom?.data == null ||
                pCity?.data == null || pCity.kingdom != pKingdom ||
                string.IsNullOrEmpty(pOfficeId)) return false;
            if (!string.Equals(CourtService.ResolveCityOffice(pKingdom, pCity),
                    pOfficeId, StringComparison.Ordinal)) return false;
            if (!RegionalGovernmentAggregationService.TryFindRegion(pKingdom,
                    pCity.data.id, out RegionalGovernmentReadModel region))
                return false;
            return region.EffectiveSeatCityId == pCity.data.id;
        }

        private static bool IsMilitaryOffice(string pLayer, string pOfficeId)
        {
            return pLayer == CourtOfficeLayer.Military ||
                   CourtProfileRegistry.IsMilitaryOfficeAcrossProfiles(pOfficeId) ||
                   pOfficeId == CourtPyramidRoleId.General;
        }

        private static bool IsRoyal(Actor pActor, Kingdom pKingdom)
        {
            long kingId = pKingdom?.king?.data?.id ?? -1L;
            return kingId >= 0 && (pActor.data.parent_id_1 == kingId ||
                                   pActor.data.parent_id_2 == kingId);
        }

        private static int SafeAge(Actor pActor)
        {
            try { return Math.Max(0, pActor?.getAge() ?? 0); }
            catch { return 0; }
        }

        private static long Long(SQLiteDataReader pReader, int pIndex, long pFallback)
        {
            return pReader.IsDBNull(pIndex) ? pFallback : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        private static int Int(SQLiteDataReader pReader, int pIndex, int pFallback)
        {
            return pReader.IsDBNull(pIndex) ? pFallback : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static float Float(SQLiteDataReader pReader, int pIndex, float pFallback)
        {
            return pReader.IsDBNull(pIndex) ? pFallback : Convert.ToSingle(pReader.GetValue(pIndex));
        }

        private static string Text(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : pReader.GetValue(pIndex)?.ToString() ?? "";
        }
    }
}
