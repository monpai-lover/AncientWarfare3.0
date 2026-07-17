using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal sealed class OfficialCareerStateView
    {
        public long ActorId;
        public long KingdomId;
        public long CityId;
        public int Rank = 1;
        public int Track;
        public string OfficeId = "";
        public float Merit;
        public int MeritCap = 1;
        public int TermEndYear = -1;
        public int LastEvaluation = 2;
        public int EvaluationModifierUntil = -1;
        public int Seniority;
        public int LastPopulationSnapshot = -1;
    }

    internal static class OfficialCareerStateService
    {
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
            public bool Evaluated;
        }

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;

        public static bool ProjectAppointment(Actor pActor, Kingdom pKingdom,
            string pLayer, string pOfficeId, City pCity)
        {
            if (DB == null || pActor?.data == null || pKingdom?.data == null) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                OfficialCareerStateView existing = ReadState(pActor.data.id, transaction);
                int officeGrade = OfficeGradeForOffice(pOfficeId);
                int meritCap = OfficialCareerRankRules.MeritCap(officeGrade);
                int age = SafeAge(pActor);
                int rank = existing?.Rank ?? OfficialCareerRankRules.EntryRank(
                    pActor.isCityLeader() || GeneralService.IsActiveGeneralFast(pActor),
                    !string.IsNullOrEmpty(SchoolMembershipService.GetSchool(pActor.data.id)),
                    age, IsRoyal(pActor, pKingdom), ChronicleGate.IsNobleActor(pActor));
                int track = existing?.Track ?? OfficialCareerRankRules.ResolveTrack(
                    IsMilitaryOffice(pLayer, pOfficeId),
                    GeneralService.IsActiveGeneralFast(pActor));
                float merit = Math.Min(existing?.Merit ?? 0f, meritCap);
                int lastEvaluation = existing?.LastEvaluation ?? 2;
                int termEndYear = existing?.TermEndYear ??
                                  Date.getCurrentYear() + OfficialCareerRankRules.TermLength(
                                      age, lastEvaluation, pActor.data.id, Date.getCurrentYear());
                Upsert(pActor, pKingdom, pCity, pOfficeId, rank, track, merit,
                    meritCap, termEndYear, lastEvaluation, existing, transaction);
                transaction.Commit();
                ProjectHotState(pActor, rank, track, merit, meritCap, termEndYear,
                    lastEvaluation, existing?.EvaluationModifierUntil ?? -1,
                    existing?.Seniority ?? 0, existing?.LastPopulationSnapshot ?? -1);
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

        public static bool ClearCurrentOffice(Actor pActor, long pKingdomId,
            string pOfficeId)
        {
            if (DB == null || pActor?.data == null) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                OfficialCareerStateView state = ReadState(pActor.data.id, transaction);
                if (state == null || state.KingdomId != pKingdomId ||
                    (!string.IsNullOrEmpty(pOfficeId) && state.OfficeId != pOfficeId))
                {
                    transaction.Rollback();
                    return false;
                }

                float merit = state.Merit;
                using var command = new SQLiteCommand(DB) { Transaction = transaction };
                command.CommandText = "UPDATE " + OfficialCareerStateTableItem.GetTableName() +
                    " SET KINGDOM_ID=-1,CITY_ID=-1,OFFICE_ID='',MERIT=@merit," +
                    "MERIT_CAP=1,UPDATED_TIME=@time WHERE ACTOR_ID=@actor";
                command.Parameters.AddWithValue("@merit", merit);
                command.Parameters.AddWithValue("@time", LineageService.CurTime());
                command.Parameters.AddWithValue("@actor", pActor.data.id);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("official state clear did not affect one row");
                transaction.Commit();
                ProjectHotState(pActor, state.Rank, state.Track, merit, 1,
                    state.TermEndYear, state.LastEvaluation,
                    state.EvaluationModifierUntil, state.Seniority,
                    state.LastPopulationSnapshot);
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
            if (pActor?.data == null) return OfficialCareerRankRules.MinimumRank;
            pActor.data.get(LineageKeys.OFFICER_RANK, out int rank,
                OfficialCareerRankRules.MinimumRank);
            return OfficialCareerRankRules.ClampRank(rank);
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
            foreach (OfficialCareerStateView state in states.Values)
            {
                Actor actor = World.world?.units?.get(state.ActorId);
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
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
                    Rank = state.Rank,
                    Merit = merit,
                    TermEndYear = state.TermEndYear,
                    LastEvaluation = state.LastEvaluation,
                    EvaluationModifierUntil = state.EvaluationModifierUntil,
                    Seniority = state.Seniority,
                    LastPopulationSnapshot = currentPopulation
                };

                if (state.TermEndYear <= year)
                    EvaluateDueOfficial(mutation, economy, year);
                mutations.Add(mutation);
            }

            if (!CommitAnnualMutations(mutations)) return;
            bool influenceChanged = false;
            foreach (AnnualMutation mutation in mutations)
            {
                ProjectHotState(mutation.Actor, mutation.Rank, mutation.State.Track,
                    mutation.Merit, mutation.State.MeritCap, mutation.TermEndYear,
                    mutation.LastEvaluation, mutation.EvaluationModifierUntil,
                    mutation.Seniority, mutation.LastPopulationSnapshot);
                influenceChanged |= mutation.Rank != mutation.PreviousRank ||
                                    Math.Abs(mutation.Merit - mutation.State.Merit) > 0.0001f;
                if (mutation.Evaluated)
                    RecordEvaluation(pKingdom, mutation);
            }
            if (influenceChanged) CourtDirectionService.MarkDirty(pKingdom);
            pKingdom.data.set(LineageKeys.OFFICIAL_CAREER_LAST_YEAR, year);
        }

        private static void EvaluateDueOfficial(AnnualMutation pMutation,
            EconomyView pEconomy, int pYear)
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
            pMutation.TermEndYear = pYear + OfficialCareerRankRules.TermLength(
                SafeAge(actor), grade, state.ActorId, pYear);
            pMutation.Evaluated = true;
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
                    "UPDATED_TIME=@time WHERE ACTOR_ID=@actor AND KINGDOM_ID=@kingdom";
                command.Parameters.Add("@rank", System.Data.DbType.Int32);
                command.Parameters.Add("@merit", System.Data.DbType.Double);
                command.Parameters.Add("@term", System.Data.DbType.Int32);
                command.Parameters.Add("@evaluation", System.Data.DbType.Int32);
                command.Parameters.Add("@modifier", System.Data.DbType.Int32);
                command.Parameters.Add("@seniority", System.Data.DbType.Int32);
                command.Parameters.Add("@population", System.Data.DbType.Int32);
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
                            pMutation.PreviousRank + "->" + pMutation.Rank;
            HistoryWriter.RecordPerson(pMutation.State.ActorId, pKingdom,
                pMutation.Actor.getName(), PersonEvent.OFFICIAL_EVALUATION,
                HistoryText.Actor(pMutation.Actor) + " " + HistoryText.PlainText(detail),
                ChronicleCategory.HONOR, HistoryTarget.Kingdom(pKingdom));
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

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }

        private static void Upsert(Actor pActor, Kingdom pKingdom, City pCity,
            string pOfficeId, int pRank, int pTrack, float pMerit, int pMeritCap,
            int pTermEndYear, int pLastEvaluation, OfficialCareerStateView pExisting,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            if (pExisting == null)
            {
                command.CommandText = "INSERT INTO " + OfficialCareerStateTableItem.GetTableName() +
                    " (ACTOR_ID,ACTOR_NAME,KINGDOM_ID,CITY_ID,RANK,TRACK,OFFICE_ID," +
                    "MERIT,MERIT_CAP,TERM_END_YEAR,LAST_KAOKE,KAOKE_MOD_UNTIL," +
                    "SENIORITY,LAST_POP_SNAPSHOT,UPDATED_TIME) VALUES " +
                    "(@actor,@name,@kingdom,@city,@rank,@track,@office,@merit,@cap," +
                    "@term,@evaluation,-1,0,-1,@time)";
            }
            else
            {
                command.CommandText = "UPDATE " + OfficialCareerStateTableItem.GetTableName() +
                    " SET ACTOR_NAME=@name,KINGDOM_ID=@kingdom,CITY_ID=@city,RANK=@rank," +
                    "TRACK=@track,OFFICE_ID=@office,MERIT=@merit,MERIT_CAP=@cap," +
                    "TERM_END_YEAR=@term,LAST_KAOKE=@evaluation,UPDATED_TIME=@time " +
                    "WHERE ACTOR_ID=@actor";
            }
            command.Parameters.AddWithValue("@actor", pActor.data.id);
            command.Parameters.AddWithValue("@name", pActor.getName() ?? "");
            command.Parameters.AddWithValue("@kingdom", pKingdom.id);
            command.Parameters.AddWithValue("@city", pCity?.data?.id ?? -1L);
            command.Parameters.AddWithValue("@rank", pRank);
            command.Parameters.AddWithValue("@track", pTrack);
            command.Parameters.AddWithValue("@office", pOfficeId ?? "");
            command.Parameters.AddWithValue("@merit", pMerit);
            command.Parameters.AddWithValue("@cap", pMeritCap);
            command.Parameters.AddWithValue("@term", pTermEndYear);
            command.Parameters.AddWithValue("@evaluation", pLastEvaluation);
            command.Parameters.AddWithValue("@time", LineageService.CurTime());
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("official state upsert did not affect one row");
        }

        private static OfficialCareerStateView ReadState(long pActorId,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = SelectColumns() + " WHERE ACTOR_ID=@actor LIMIT 1";
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadView(reader) : null;
        }

        private static string SelectColumns()
        {
            return "SELECT ACTOR_ID,KINGDOM_ID,CITY_ID,RANK,TRACK,IFNULL(OFFICE_ID,'')," +
                   "MERIT,MERIT_CAP,TERM_END_YEAR,LAST_KAOKE,KAOKE_MOD_UNTIL," +
                   "SENIORITY,LAST_POP_SNAPSHOT FROM " +
                   OfficialCareerStateTableItem.GetTableName();
        }

        private static OfficialCareerStateView ReadView(SQLiteDataReader pReader)
        {
            return new OfficialCareerStateView
            {
                ActorId = Long(pReader, 0, -1L),
                KingdomId = Long(pReader, 1, -1L),
                CityId = Long(pReader, 2, -1L),
                Rank = OfficialCareerRankRules.ClampRank(Int(pReader, 3, 1)),
                Track = Int(pReader, 4, OfficialCareerRankRules.CivilTrack),
                OfficeId = Text(pReader, 5),
                Merit = Float(pReader, 6, 0f),
                MeritCap = Int(pReader, 7, 1),
                TermEndYear = Int(pReader, 8, -1),
                LastEvaluation = Int(pReader, 9, 2),
                EvaluationModifierUntil = Int(pReader, 10, -1),
                Seniority = Int(pReader, 11, 0),
                LastPopulationSnapshot = Int(pReader, 12, -1)
            };
        }

        private static void ProjectHotState(Actor pActor, int pRank, int pTrack,
            float pMerit, int pMeritCap, int pTermEndYear, int pLastEvaluation,
            int pModifierUntil, int pSeniority, int pLastPopulation)
        {
            pActor.data.set(LineageKeys.OFFICER_RANK, pRank);
            pActor.data.set(LineageKeys.OFFICER_TRACK, pTrack);
            pActor.data.set(LineageKeys.OFFICER_MERIT, pMerit);
            pActor.data.set(LineageKeys.OFFICER_MERIT_CAP, pMeritCap);
            pActor.data.set(LineageKeys.OFFICER_TERM_END_YEAR, pTermEndYear);
            pActor.data.set(LineageKeys.OFFICER_LAST_KAOKE, pLastEvaluation);
            pActor.data.set(LineageKeys.OFFICER_KAOKE_MOD_UNTIL, pModifierUntil);
            pActor.data.set(LineageKeys.OFFICER_SENIORITY, pSeniority);
            pActor.data.set(LineageKeys.OFFICER_LAST_POP_SNAPSHOT, pLastPopulation);
        }

        internal static int OfficeGradeForOffice(string pOfficeId)
        {
            if (pOfficeId == CourtOfficeId.Chancellor || pOfficeId == CourtOfficeId.Marshal ||
                pOfficeId == CourtOfficeId.Censor || pOfficeId == CourtOfficeId.Zhongshu ||
                pOfficeId == CourtOfficeId.Menxia || pOfficeId == CourtOfficeId.Shangshu)
                return 10;
            if (pOfficeId == CourtOfficeId.Justice || pOfficeId == CourtOfficeId.Steward ||
                pOfficeId == CourtOfficeId.Erudite || pOfficeId == CourtOfficeId.Libu ||
                pOfficeId == CourtOfficeId.Hubu || pOfficeId == CourtOfficeId.Ribu ||
                pOfficeId == CourtOfficeId.Bingbu || pOfficeId == CourtOfficeId.Xingbu ||
                pOfficeId == CourtOfficeId.Gongbu)
                return 20;
            return string.IsNullOrEmpty(pOfficeId) ? 0 : 30;
        }

        private static bool IsMilitaryOffice(string pLayer, string pOfficeId)
        {
            return pLayer == CourtOfficeLayer.Military || pOfficeId == CourtOfficeId.Marshal ||
                   pOfficeId == CourtOfficeId.Bingbu || pOfficeId == CourtPyramidRoleId.General;
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
