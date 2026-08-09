using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    public sealed class MilitaryGovernorateSnapshot
    {
        public long StateId = -1;
        public long RelationId = -1;
        public long SubjectKingdomId = -1;
        public long SuzerainKingdomId = -1;
        public long SeatCityId = -1;
        public long GovernorActorId = -1;
        public long SuccessorActorId = -1;
        public long ExpeditionaryArmyId = -1;
        public string CommandName = "";
        public int CreatedYear = -1;
        public int SuccessionState;
    }

    internal static class MilitaryGovernorateStore
    {
        private const int MaximumDirectRead = 256;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
            LineageArchiveManager.Instance.InitializeSuccessful;

        public static bool TryCreate(long pRelationId, Kingdom pSubject,
            Kingdom pSuzerain, City pSeat, Actor pGovernor,
            string pCommandName, int pCreatedYear, out long pStateId)
        {
            pStateId = -1;
            if (!Ready || pRelationId < 0 || pSubject == null ||
                pSuzerain == null || pSeat == null || pGovernor == null ||
                pSubject.id < 0 || pSuzerain.id < 0 || pSeat.id < 0 ||
                pGovernor.getID() < 0)
                return false;

            SQLiteConnection db = DB;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = db.BeginTransaction(IsolationLevel.Serializable);
                using (var relation = new SQLiteCommand(db)
                       { Transaction = transaction })
                {
                    relation.CommandText = "UPDATE " +
                        VassalRelationTableItem.GetTableName() +
                        " SET SUBJECT_KIND=@kind WHERE RELATION_ID=@relation" +
                        " AND VASSAL_ID=@subject AND SUZERAIN_ID=@suzerain" +
                        " AND ACTIVE=1";
                    relation.Parameters.AddWithValue("@kind",
                        (int)VassalSubjectKind.MilitaryGovernorate);
                    relation.Parameters.AddWithValue("@relation", pRelationId);
                    relation.Parameters.AddWithValue("@subject", pSubject.id);
                    relation.Parameters.AddWithValue("@suzerain", pSuzerain.id);
                    if (relation.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }

                pStateId = TableIdAllocator.Next(db, transaction,
                    MilitaryGovernorateStateTableItem.GetTableName(),
                    "STATE_ID");
                using (var insert = new SQLiteCommand(db)
                       { Transaction = transaction })
                {
                    insert.CommandText = "INSERT INTO " +
                        MilitaryGovernorateStateTableItem.GetTableName() +
                        " (STATE_ID,RELATION_ID,SUBJECT_KINGDOM_ID," +
                        "SUZERAIN_KINGDOM_ID,SEAT_CITY_ID,GOVERNOR_ACTOR_ID," +
                        "SUCCESSOR_ACTOR_ID,EXPEDITIONARY_ARMY_ID," +
                        "COMMAND_NAME,CREATED_YEAR,SUCCESSION_STATE,ACTIVE," +
                        "END_TIME,END_REASON) VALUES (@state,@relation," +
                        "@subject,@suzerain,@seat,@governor,-1,-1,@name," +
                        "@year,0,1,-1,'')";
                    insert.Parameters.AddWithValue("@state", pStateId);
                    insert.Parameters.AddWithValue("@relation", pRelationId);
                    insert.Parameters.AddWithValue("@subject", pSubject.id);
                    insert.Parameters.AddWithValue("@suzerain", pSuzerain.id);
                    insert.Parameters.AddWithValue("@seat", pSeat.id);
                    insert.Parameters.AddWithValue("@governor",
                        pGovernor.getID());
                    insert.Parameters.AddWithValue("@name", pCommandName ?? "");
                    insert.Parameters.AddWithValue("@year", pCreatedYear);
                    if (insert.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        pStateId = -1;
                        return false;
                    }
                }

                transaction.Commit();
                Project(pSubject, pStateId);
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                ModClass.LogWarning("Military governorate creation persistence failed: " +
                                    error.Message);
                pStateId = -1;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public static bool TryGetActive(Kingdom pSubject,
            out MilitaryGovernorateSnapshot pSnapshot)
        {
            pSnapshot = null;
            if (!Ready || pSubject == null || pSubject.id < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = SelectColumns + " FROM " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " WHERE SUBJECT_KINGDOM_ID=@subject AND ACTIVE=1" +
                    " ORDER BY STATE_ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@subject", pSubject.id);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pSnapshot = Read(reader);
                Project(pSubject, pSnapshot.StateId);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Military governorate state read failed: " +
                                    error.Message);
                pSnapshot = null;
                return false;
            }
        }

        public static List<MilitaryGovernorateSnapshot> GetDirectActive(
            Kingdom pSuzerain, int pLimit)
        {
            var result = new List<MilitaryGovernorateSnapshot>();
            if (!Ready || pSuzerain == null || pSuzerain.id < 0 || pLimit <= 0)
                return result;
            int limit = Math.Min(pLimit, MaximumDirectRead);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = SelectColumns + " FROM " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " WHERE SUZERAIN_KINGDOM_ID=@suzerain AND ACTIVE=1" +
                    " ORDER BY STATE_ID LIMIT @limit";
                command.Parameters.AddWithValue("@suzerain", pSuzerain.id);
                command.Parameters.AddWithValue("@limit", limit);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(Read(reader));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Military governorate children read failed: " +
                                    error.Message);
            }
            return result;
        }

        public static bool SetSuccessor(long pStateId, long pActorId)
        {
            return UpdateId(pStateId, "SUCCESSOR_ACTOR_ID", pActorId);
        }

        public static bool SetExpeditionaryArmy(long pStateId, long pArmyId)
        {
            return UpdateId(pStateId, "EXPEDITIONARY_ARMY_ID", pArmyId);
        }

        public static bool End(long pStateId, string pReason)
        {
            if (!Ready || pStateId < 0) return false;
            long subjectId = -1;
            try
            {
                using (var read = new SQLiteCommand(DB))
                {
                    read.CommandText = "SELECT SUBJECT_KINGDOM_ID FROM " +
                        MilitaryGovernorateStateTableItem.GetTableName() +
                        " WHERE STATE_ID=@state AND ACTIVE=1 LIMIT 1";
                    read.Parameters.AddWithValue("@state", pStateId);
                    object value = read.ExecuteScalar();
                    if (value == null || value == DBNull.Value) return false;
                    subjectId = Convert.ToInt64(value);
                }
                using var update = new SQLiteCommand(DB);
                update.CommandText = "UPDATE " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " SET ACTIVE=0,END_TIME=@time,END_REASON=@reason" +
                    " WHERE STATE_ID=@state AND ACTIVE=1";
                update.Parameters.AddWithValue("@time", LineageService.CurTime());
                update.Parameters.AddWithValue("@reason", pReason ?? "");
                update.Parameters.AddWithValue("@state", pStateId);
                if (update.ExecuteNonQuery() != 1) return false;
                ClearProjection(FindKingdom(subjectId));
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Military governorate state end failed: " +
                                    error.Message);
                return false;
            }
        }

        public static bool TryEndWithRelation(long pStateId,
            long pRelationId, string pReason, bool pAbsorbed,
            out long pSuzerainId, out int pContractTier)
        {
            pSuzerainId = -1L;
            pContractTier = VassalContractTierRules.Outer;
            if (!Ready || pStateId < 0 || pRelationId < 0) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction(
                    IsolationLevel.Serializable);
                using (var read = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    read.CommandText = "SELECT r.SUZERAIN_ID," +
                        "r.CONTRACT_TIER FROM " +
                        VassalRelationTableItem.GetTableName() + " r JOIN " +
                        MilitaryGovernorateStateTableItem.GetTableName() +
                        " s ON s.RELATION_ID=r.RELATION_ID WHERE " +
                        "r.RELATION_ID=@relation AND r.ACTIVE=1 AND " +
                        "r.END_TIME<0 AND s.STATE_ID=@state AND s.ACTIVE=1 " +
                        "LIMIT 1";
                    read.Parameters.AddWithValue("@relation", pRelationId);
                    read.Parameters.AddWithValue("@state", pStateId);
                    using SQLiteDataReader reader = read.ExecuteReader();
                    if (!reader.Read())
                    {
                        transaction.Rollback();
                        return false;
                    }
                    pSuzerainId = reader.GetInt64(0);
                    pContractTier = reader.IsDBNull(1)
                        ? VassalContractTierRules.Outer
                        : VassalContractTierRules.NormalizeTier(
                            (int)reader.GetInt64(1));
                }

                double now = LineageService.CurTime();
                using (var relation = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    relation.CommandText = "UPDATE " +
                        VassalRelationTableItem.GetTableName() +
                        " SET END_TIME=@time,ACTIVE=0,ABSORBED=@absorbed," +
                        "END_REASON=@reason WHERE RELATION_ID=@relation " +
                        "AND ACTIVE=1 AND END_TIME<0";
                    relation.Parameters.AddWithValue("@time", now);
                    relation.Parameters.AddWithValue("@absorbed",
                        pAbsorbed ? 1 : 0);
                    relation.Parameters.AddWithValue("@reason", pReason ?? "");
                    relation.Parameters.AddWithValue("@relation", pRelationId);
                    if (relation.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                using (var state = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    state.CommandText = "UPDATE " +
                        MilitaryGovernorateStateTableItem.GetTableName() +
                        " SET ACTIVE=0,END_TIME=@time,END_REASON=@reason " +
                        "WHERE STATE_ID=@state AND RELATION_ID=@relation " +
                        "AND ACTIVE=1";
                    state.Parameters.AddWithValue("@time", now);
                    state.Parameters.AddWithValue("@reason", pReason ?? "");
                    state.Parameters.AddWithValue("@state", pStateId);
                    state.Parameters.AddWithValue("@relation", pRelationId);
                    if (state.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                ModClass.LogWarning(
                    "Military governorate relation end failed: " +
                    error.Message);
                pSuzerainId = -1L;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public static bool RestoreProjection(Kingdom pSubject)
        {
            if (pSubject == null) return false;
            if (TryGetActive(pSubject, out MilitaryGovernorateSnapshot snapshot))
            {
                Project(pSubject, snapshot.StateId);
                return true;
            }
            ClearProjection(pSubject);
            return false;
        }

        private static bool UpdateId(long pStateId, string pColumn,
            long pValue)
        {
            if (!Ready || pStateId < 0 ||
                (pColumn != "SUCCESSOR_ACTOR_ID" &&
                 pColumn != "EXPEDITIONARY_ARMY_ID"))
                return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " SET " + pColumn + "=@value WHERE STATE_ID=@state" +
                    " AND ACTIVE=1";
                command.Parameters.AddWithValue("@value", pValue);
                command.Parameters.AddWithValue("@state", pStateId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Military governorate state update failed: " +
                                    error.Message);
                return false;
            }
        }

        private const string SelectColumns =
            "SELECT STATE_ID,RELATION_ID,SUBJECT_KINGDOM_ID," +
            "SUZERAIN_KINGDOM_ID,SEAT_CITY_ID,GOVERNOR_ACTOR_ID," +
            "SUCCESSOR_ACTOR_ID,EXPEDITIONARY_ARMY_ID,COMMAND_NAME," +
            "CREATED_YEAR,SUCCESSION_STATE";

        private static MilitaryGovernorateSnapshot Read(SQLiteDataReader pReader)
        {
            return new MilitaryGovernorateSnapshot
            {
                StateId = pReader.GetInt64(0),
                RelationId = pReader.GetInt64(1),
                SubjectKingdomId = pReader.GetInt64(2),
                SuzerainKingdomId = pReader.GetInt64(3),
                SeatCityId = pReader.GetInt64(4),
                GovernorActorId = pReader.GetInt64(5),
                SuccessorActorId = pReader.GetInt64(6),
                ExpeditionaryArmyId = pReader.GetInt64(7),
                CommandName = pReader.IsDBNull(8) ? "" : pReader.GetString(8),
                CreatedYear = pReader.GetInt32(9),
                SuccessionState = pReader.GetInt32(10)
            };
        }

        private static void Project(Kingdom pSubject, long pStateId)
        {
            if (pSubject == null) return;
            pSubject.data.set(LineageKeys.MILITARY_GOVERNORATE_SUBJECT_KIND,
                (int)VassalSubjectKind.MilitaryGovernorate);
            pSubject.data.set(LineageKeys.MILITARY_GOVERNORATE_STATE_ID,
                pStateId);
        }

        private static void ClearProjection(Kingdom pSubject)
        {
            if (pSubject == null) return;
            pSubject.data.set(LineageKeys.MILITARY_GOVERNORATE_SUBJECT_KIND,
                (int)VassalSubjectKind.Ordinary);
            pSubject.data.set(LineageKeys.MILITARY_GOVERNORATE_STATE_ID, -1L);
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
