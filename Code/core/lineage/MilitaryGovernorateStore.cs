using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
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
        public bool ReplacementAllowed;
    }

    internal static class MilitaryGovernorateStore
    {
        private const int MaximumDirectRead = 256;
        private const int RuntimeRestoreBatchLimit = 16;
        private const int RuntimeRestoreRepairBudget = 64;
        private const int MultiplayerSnapshotLimit = 4096;
        private const string RuntimeRestoreQueueKey =
            "military_governorate:runtime_restore";
        private static long _runtimeRestoreCursor = -1L;
        private static readonly HashSet<long> ReplicaSubjectIds =
            new HashSet<long>();
        private static readonly
            Dictionary<long, MilitaryGovernorateSnapshot> ReplicaSnapshotsBySubject =
                new Dictionary<long, MilitaryGovernorateSnapshot>();
        private static object _replicaProjectionWorld;
        private static long _replicaProjectionSessionRevision;

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
                        "COMMAND_NAME,CREATED_YEAR,SUCCESSION_STATE," +
                        "REPLACEMENT_ALLOWED,ACTIVE," +
                        "END_TIME,END_REASON) VALUES (@state,@relation," +
                        "@subject,@suzerain,@seat,@governor,-1,-1,@name," +
                        "@year,0,0,1,-1,'')";
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
                Project(pSubject, pStateId, -1L, false);
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
            if (pSubject?.data == null || pSubject.id < 0) return false;
            EnsureReplicaProjectionWorld();
            if (AW3MultiplayerReplicaScope.IsReplicaSession &&
                ReplicaSnapshotsBySubject.TryGetValue(pSubject.id,
                    out pSnapshot))
            {
                Project(pSubject, pSnapshot.StateId,
                    pSnapshot.SuccessorActorId,
                    pSnapshot.ReplacementAllowed);
                return true;
            }
            if (!Ready) return false;
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
                Project(pSubject, pSnapshot.StateId,
                    pSnapshot.SuccessorActorId,
                    pSnapshot.ReplacementAllowed);
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

        public static bool SetGovernor(long pStateId, long pActorId)
        {
            return UpdateId(pStateId, "GOVERNOR_ACTOR_ID", pActorId);
        }

        public static bool SetSuccessionState(long pStateId, int pState)
        {
            if (!Ready || pStateId < 0 || pState < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " SET SUCCESSION_STATE=@value WHERE STATE_ID=@state" +
                    " AND ACTIVE=1";
                command.Parameters.AddWithValue("@value", pState);
                command.Parameters.AddWithValue("@state", pStateId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Military governorate succession state update failed: " +
                    error.Message);
                return false;
            }
        }

        public static bool SetReplacementAllowed(long pStateId,
            bool pAllowed)
        {
            return UpdateInt(pStateId, "REPLACEMENT_ALLOWED",
                pAllowed ? 1 : 0);
        }

        public static bool SetCommandName(long pStateId, string pCommandName)
        {
            if (!Ready || pStateId < 0 ||
                string.IsNullOrWhiteSpace(pCommandName)) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " SET COMMAND_NAME=@name WHERE STATE_ID=@state AND ACTIVE=1";
                command.Parameters.AddWithValue("@name", pCommandName.Trim());
                command.Parameters.AddWithValue("@state", pStateId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Military governorate rename failed: " +
                                    error.Message);
                return false;
            }
        }

        public static bool CommitSuccession(long pStateId, long pGovernorId)
        {
            if (!Ready || pStateId < 0 || pGovernorId < 0) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction(
                    IsolationLevel.Serializable);
                using var command = new SQLiteCommand(DB)
                {
                    Transaction = transaction
                };
                command.CommandText = "UPDATE " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " SET GOVERNOR_ACTOR_ID=@governor,SUCCESSOR_ACTOR_ID=-1," +
                    "SUCCESSION_STATE=0,REPLACEMENT_ALLOWED=0 " +
                    "WHERE STATE_ID=@state AND ACTIVE=1";
                command.Parameters.AddWithValue("@governor", pGovernorId);
                command.Parameters.AddWithValue("@state", pStateId);
                if (command.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return false;
                }
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                ModClass.LogWarning(
                    "Military governorate succession commit failed: " +
                    error.Message);
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
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
                        " s ON s.RELATION_ID=r.RELATION_ID AND " +
                        "s.SUBJECT_KINGDOM_ID=r.VASSAL_ID AND " +
                        "s.SUZERAIN_KINGDOM_ID=r.SUZERAIN_ID WHERE " +
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
                Project(pSubject, snapshot.StateId,
                    snapshot.SuccessorActorId,
                    snapshot.ReplacementAllowed);
                return true;
            }
            ClearProjection(pSubject);
            return false;
        }

        public static List<MilitaryGovernorateSnapshot> ReadActiveBatch(
            long pAfterStateId, int pLimit)
        {
            var result = new List<MilitaryGovernorateSnapshot>();
            if (!Ready || pAfterStateId < -1L || pLimit <= 0) return result;
            int limit = Math.Min(pLimit, RuntimeRestoreRepairBudget);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = AuthoritySelectColumns + " FROM " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " WHERE ACTIVE=1 AND STATE_ID>@after" +
                    " ORDER BY STATE_ID LIMIT @limit";
                command.Parameters.AddWithValue("@after", pAfterStateId);
                command.Parameters.AddWithValue("@limit", limit);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadAuthority(reader));
                return result;
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    "Military governorate active batch read failed.", error);
            }
        }

        public static void EnqueueRuntimeRestore()
        {
            _runtimeRestoreCursor = -1L;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                RuntimeRestoreQueueKey, DeferredWorkClass.Runtime,
                ProcessRuntimeRestore);
        }

        public static List<MilitaryGovernorateSnapshot>
            CaptureAuthoritativeState()
        {
            var result = new List<MilitaryGovernorateSnapshot>();
            long cursor = -1L;
            while (result.Count < MultiplayerSnapshotLimit)
            {
                int remaining = MultiplayerSnapshotLimit - result.Count;
                List<MilitaryGovernorateSnapshot> batch = ReadActiveBatch(
                    cursor, Math.Min(RuntimeRestoreRepairBudget, remaining));
                if (batch.Count == 0) return result;
                result.AddRange(batch);
                cursor = batch[batch.Count - 1].StateId;
                if (batch.Count < Math.Min(RuntimeRestoreRepairBudget,
                        remaining)) return result;
            }
            if (ReadActiveBatch(cursor, 1).Count != 0)
                throw new InvalidOperationException(
                    "Military governorate snapshot exceeds its explicit limit.");
            return result;
        }

        public static void ApplyAuthoritativeProjection(Kingdom pSubject,
            long pStateId, long pRelationId, long pSuzerainKingdomId,
            long pSeatCityId, long pGovernorActorId,
            long pSuccessorActorId, string pCommandName,
            int pSuccessionState,
            bool pReplacementAllowed, bool pActive)
        {
            EnsureReplicaProjectionWorld();
            if (pSubject == null) return;
            if (!pActive)
            {
                bool trackedReplica = ReplicaSubjectIds.Remove(pSubject.id);
                ReplicaSnapshotsBySubject.Remove(pSubject.id);
                if (pSubject.data != null) ClearProjection(pSubject);
                if (trackedReplica)
                    ClearReplicaVassalProjection(pSubject);
                return;
            }
            if (pSubject.data == null) return;
            var snapshot = new MilitaryGovernorateSnapshot
            {
                StateId = pStateId,
                RelationId = pRelationId,
                SubjectKingdomId = pSubject.id,
                SuzerainKingdomId = pSuzerainKingdomId,
                SeatCityId = pSeatCityId,
                GovernorActorId = pGovernorActorId,
                SuccessorActorId = pSuccessorActorId,
                CommandName = pCommandName ?? "",
                SuccessionState = Math.Max(0, pSuccessionState),
                ReplacementAllowed = pReplacementAllowed
            };
            pSubject.data.set(LineageKeys.VASSAL_RELATION_ID, pRelationId);
            pSubject.data.set(LineageKeys.VASSAL_SUZERAIN_ID,
                pSuzerainKingdomId);
            Project(pSubject, pStateId, pSuccessorActorId,
                pReplacementAllowed);
            ReplicaSnapshotsBySubject[pSubject.id] = snapshot;
            ReplicaSubjectIds.Add(pSubject.id);
        }

        public static void RetainAuthoritativeProjections(
            IReadOnlyList<long> pSubjectIds)
        {
            EnsureReplicaProjectionWorld();
            var retained = new HashSet<long>();
            if (pSubjectIds != null)
                for (var index = 0; index < pSubjectIds.Count; index++)
                    if (pSubjectIds[index] >= 0)
                        retained.Add(pSubjectIds[index]);
            var stale = new List<long>();
            foreach (long subjectId in ReplicaSubjectIds)
                if (!retained.Contains(subjectId)) stale.Add(subjectId);
            stale.Sort();
            for (var index = 0; index < stale.Count; index++)
            {
                Kingdom subject = FindKingdom(stale[index]);
                ClearProjection(subject);
                ClearReplicaVassalProjection(subject);
                ReplicaSnapshotsBySubject.Remove(stale[index]);
                ReplicaSubjectIds.Remove(stale[index]);
            }
        }

        private static void ProcessRuntimeRestore()
        {
            int remaining = RuntimeRestoreRepairBudget;
            while (remaining > 0)
            {
                int requested = Math.Min(RuntimeRestoreBatchLimit, remaining);
                List<MilitaryGovernorateSnapshot> batch = ReadActiveBatch(
                    _runtimeRestoreCursor, requested);
                if (batch.Count == 0)
                {
                    _runtimeRestoreCursor = -1L;
                    return;
                }
                for (var index = 0; index < batch.Count; index++)
                {
                    MilitaryGovernorateSnapshot snapshot = batch[index];
                    try
                    {
                        RepairAndProject(snapshot);
                    }
                    catch (Exception error)
                    {
                        ModClass.LogWarning(
                            "Military governorate runtime restore failed for state " +
                            snapshot.StateId + ": " + error.Message);
                        EnqueueRuntimeRestoreRetry(snapshot.StateId);
                    }
                    finally
                    {
                        _runtimeRestoreCursor = snapshot.StateId;
                        remaining--;
                    }
                }
                if (batch.Count < requested)
                {
                    _runtimeRestoreCursor = -1L;
                    return;
                }
            }
            DeferredRuntimeWorkService.EnqueueCoalesced(
                RuntimeRestoreQueueKey, DeferredWorkClass.Runtime,
                ProcessRuntimeRestore);
        }

        private static void EnqueueRuntimeRestoreRetry(long pStateId)
        {
            if (pStateId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "military_governorate:runtime_restore_state", pStateId),
                DeferredWorkClass.Runtime,
                () => ProcessRuntimeRestoreState(pStateId));
        }

        private static void ProcessRuntimeRestoreState(long pStateId)
        {
            MilitaryGovernorateSnapshot snapshot = ReadActiveState(pStateId);
            if (snapshot == null) return;
            RepairAndProject(snapshot);
        }

        private static MilitaryGovernorateSnapshot ReadActiveState(
            long pStateId)
        {
            if (!Ready || pStateId < 0) return null;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = AuthoritySelectColumns + " FROM " +
                    MilitaryGovernorateStateTableItem.GetTableName() +
                    " WHERE ACTIVE=1 AND STATE_ID=@state LIMIT 1";
                command.Parameters.AddWithValue("@state", pStateId);
                using SQLiteDataReader reader = command.ExecuteReader();
                return reader.Read() ? ReadAuthority(reader) : null;
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    "Military governorate active state read failed.", error);
            }
        }

        private static void RepairAndProject(
            MilitaryGovernorateSnapshot pSnapshot)
        {
            Kingdom subject = FindKingdom(pSnapshot.SubjectKingdomId);
            if (!VassalService.TryReadActiveRelationIdentity(
                    pSnapshot.SubjectKingdomId,
                    out ActiveVassalRelationIdentity relation,
                    out bool relationExists))
                throw new InvalidOperationException(
                    "Military governorate relation repair read failed.");
            if (!IsLiveKingdom(subject))
            {
                EndInvalid(pSnapshot, subject, relation, relationExists,
                    "missing_subject_kingdom");
                return;
            }
            Kingdom suzerain = FindKingdom(pSnapshot.SuzerainKingdomId);
            if (!IsLiveKingdom(suzerain) || suzerain == subject)
            {
                EndInvalid(pSnapshot, subject, relation, relationExists,
                    "missing_suzerain_kingdom");
                return;
            }
            if (!relationExists || relation.Ambiguous ||
                relation.RelationId != pSnapshot.RelationId ||
                relation.VassalId != subject.id ||
                relation.SuzerainId != suzerain.id ||
                relation.SubjectKind != VassalSubjectKind.MilitaryGovernorate)
            {
                EndInvalid(pSnapshot, subject, relation, relationExists,
                    "missing_vassal_relation");
                return;
            }

            City seat = FindCity(pSnapshot.SeatCityId);
            if (!IsOwnedLiveCity(seat, subject))
            {
                seat = subject.capital;
                if (!IsOwnedLiveCity(seat, subject))
                {
                    EndInvalid(pSnapshot, subject, relation, relationExists,
                        "missing_seat_city");
                    return;
                }
                if (!SetSeat(pSnapshot.StateId, seat.id))
                    throw new InvalidOperationException(
                        "Military governorate seat repair failed.");
            }

            Actor governor = FindActor(pSnapshot.GovernorActorId);
            if (!IsLivingMember(governor, subject) || subject.king != governor)
            {
                governor = subject.king;
                if (!IsLivingMember(governor, subject))
                {
                    EndInvalid(pSnapshot, subject, relation, relationExists,
                        "missing_governor_actor");
                    return;
                }
                if (!SetGovernor(pSnapshot.StateId, governor.getID()))
                    throw new InvalidOperationException(
                        "Military governorate ruler repair failed.");
            }

            long successorId = pSnapshot.SuccessorActorId;
            if (successorId >= 0 &&
                !IsLivingMember(FindActor(successorId), subject))
            {
                const string reason = "missing_successor_actor";
                if (!SetSuccessor(pSnapshot.StateId, -1L))
                    throw new InvalidOperationException(
                        "Military governorate " + reason + " repair failed.");
                successorId = -1L;
            }
            Project(subject, pSnapshot.StateId, successorId,
                pSnapshot.ReplacementAllowed);
        }

        private static void EndInvalid(MilitaryGovernorateSnapshot pSnapshot,
            Kingdom pSubject, ActiveVassalRelationIdentity pRelation,
            bool pRelationExists, string pReason)
        {
            bool relationEnded = false;
            bool stateEnded = false;
            if (pRelationExists &&
                pRelation.SubjectKind ==
                    VassalSubjectKind.MilitaryGovernorate &&
                !pRelation.Ambiguous &&
                pRelation.RelationId == pSnapshot.RelationId &&
                pRelation.VassalId == pSnapshot.SubjectKingdomId &&
                pRelation.SuzerainId == pSnapshot.SuzerainKingdomId)
            {
                relationEnded = TryEndWithRelation(pSnapshot.StateId,
                    pSnapshot.RelationId, pReason, false,
                    out long closedSuzerainId, out int closedContractTier);
                stateEnded = relationEnded;
                if (!relationEnded)
                    throw new InvalidOperationException(
                        "Military governorate relation repair failed: " +
                        pReason);
                VassalService.ClearEndedMilitaryGovernorateRelationProjection(
                    pSubject, pSnapshot.RelationId, closedSuzerainId,
                    closedContractTier);
                ClearProjection(pSubject);
            }
            else if (pRelationExists &&
                     (pRelation.Ambiguous ||
                      pRelation.RelationId != pSnapshot.RelationId ||
                      pRelation.VassalId != pSnapshot.SubjectKingdomId ||
                      pRelation.SuzerainId !=
                          pSnapshot.SuzerainKingdomId))
            {
                if (!TryEndStateAndDowngradeMilitaryRelations(
                        pSnapshot.StateId, pSnapshot.RelationId,
                        pSnapshot.SubjectKingdomId, pReason))
                    throw new InvalidOperationException(
                        "Military governorate mismatched relation repair failed: " +
                        pReason);
                stateEnded = true;
            }

            if (!stateEnded && !End(pSnapshot.StateId, pReason))
                throw new InvalidOperationException(
                    "Military governorate invalid-state repair failed: " +
                    pReason);
            if (!relationEnded && stateEnded)
                ClearProjection(pSubject);
        }

        private static bool TryEndStateAndDowngradeMilitaryRelations(
            long pStateId, long pExpectedRelationId, long pSubjectId,
            string pReason)
        {
            if (!Ready || pStateId < 0 || pExpectedRelationId < 0 ||
                pSubjectId < 0) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction(
                    IsolationLevel.Serializable);
                using (var verify = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    verify.CommandText = "SELECT 1 FROM " +
                        MilitaryGovernorateStateTableItem.GetTableName() +
                        " WHERE STATE_ID=@state AND RELATION_ID=@relation" +
                        " AND SUBJECT_KINGDOM_ID=@subject AND ACTIVE=1 LIMIT 1";
                    verify.Parameters.AddWithValue("@state", pStateId);
                    verify.Parameters.AddWithValue("@relation",
                        pExpectedRelationId);
                    verify.Parameters.AddWithValue("@subject", pSubjectId);
                    if (verify.ExecuteScalar() == null)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }

                double now = LineageService.CurTime();
                using (var relations = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    relations.CommandText = "UPDATE " +
                        VassalRelationTableItem.GetTableName() +
                        " SET SUBJECT_KIND=@ordinary" +
                        " WHERE VASSAL_ID=@subject" +
                        " AND ACTIVE=1 AND END_TIME<0 AND SUBJECT_KIND=@military";
                    relations.Parameters.AddWithValue("@ordinary",
                        (int)VassalSubjectKind.Ordinary);
                    relations.Parameters.AddWithValue("@subject", pSubjectId);
                    relations.Parameters.AddWithValue("@military",
                        (int)VassalSubjectKind.MilitaryGovernorate);
                    relations.ExecuteNonQuery();
                }
                using (var state = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    state.CommandText = "UPDATE " +
                        MilitaryGovernorateStateTableItem.GetTableName() +
                        " SET ACTIVE=0,END_TIME=@time,END_REASON=@reason" +
                        " WHERE STATE_ID=@state AND RELATION_ID=@relation" +
                        " AND ACTIVE=1";
                    state.Parameters.AddWithValue("@time", now);
                    state.Parameters.AddWithValue("@reason", pReason ?? "");
                    state.Parameters.AddWithValue("@state", pStateId);
                    state.Parameters.AddWithValue("@relation",
                        pExpectedRelationId);
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
                    "Military governorate mismatched relation end failed: " +
                    error.Message);
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static bool SetSeat(long pStateId, long pCityId)
        {
            return UpdateId(pStateId, "SEAT_CITY_ID", pCityId);
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt();
        }

        private static bool IsOwnedLiveCity(City pCity, Kingdom pKingdom)
        {
            return pCity?.data != null && pCity.isAlive() &&
                   !pCity.isRekt() && pCity.kingdom == pKingdom;
        }

        private static bool IsLivingMember(Actor pActor, Kingdom pKingdom)
        {
            return pActor?.data != null && pActor.isAlive() &&
                   !pActor.isRekt() && pActor.kingdom == pKingdom;
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static void ClearReplicaVassalProjection(Kingdom pSubject)
        {
            if (pSubject?.data == null) return;
            pSubject.data.set(LineageKeys.VASSAL_RELATION_ID, -1L);
            pSubject.data.set(LineageKeys.VASSAL_SUZERAIN_ID, -1L);
        }

        private static void EnsureReplicaProjectionWorld()
        {
            long sessionRevision = AW3MultiplayerReplicaScope.SessionRevision;
            if (ReferenceEquals(_replicaProjectionWorld, World.world) &&
                _replicaProjectionSessionRevision == sessionRevision) return;
            ReplicaSubjectIds.Clear();
            ReplicaSnapshotsBySubject.Clear();
            _replicaProjectionWorld = World.world;
            _replicaProjectionSessionRevision = sessionRevision;
        }

        private const string AuthoritySelectColumns =
            "SELECT STATE_ID,RELATION_ID,SUBJECT_KINGDOM_ID," +
            "SUZERAIN_KINGDOM_ID,SEAT_CITY_ID,GOVERNOR_ACTOR_ID," +
            "SUCCESSOR_ACTOR_ID,COMMAND_NAME,CREATED_YEAR," +
            "SUCCESSION_STATE,REPLACEMENT_ALLOWED";

        private static MilitaryGovernorateSnapshot ReadAuthority(
            SQLiteDataReader pReader)
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
                CommandName = pReader.IsDBNull(7) ? "" : pReader.GetString(7),
                CreatedYear = pReader.IsDBNull(8) ? -1 : pReader.GetInt32(8),
                SuccessionState = pReader.IsDBNull(9)
                    ? 0
                    : Math.Max(0, pReader.GetInt32(9)),
                ReplacementAllowed = !pReader.IsDBNull(10) &&
                                     pReader.GetInt32(10) != 0
            };
        }

        private static bool UpdateId(long pStateId, string pColumn,
            long pValue)
        {
            if (!Ready || pStateId < 0 ||
                (pColumn != "SUCCESSOR_ACTOR_ID" &&
                 pColumn != "GOVERNOR_ACTOR_ID" &&
                 pColumn != "SEAT_CITY_ID" &&
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

        private static bool UpdateInt(long pStateId, string pColumn,
            int pValue)
        {
            if (!Ready || pStateId < 0 ||
                pColumn != "REPLACEMENT_ALLOWED") return false;
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
                ModClass.LogWarning("Military governorate integer update failed: " +
                                    error.Message);
                return false;
            }
        }

        private const string SelectColumns =
            "SELECT STATE_ID,RELATION_ID,SUBJECT_KINGDOM_ID," +
            "SUZERAIN_KINGDOM_ID,SEAT_CITY_ID,GOVERNOR_ACTOR_ID," +
            "SUCCESSOR_ACTOR_ID,EXPEDITIONARY_ARMY_ID,COMMAND_NAME," +
            "CREATED_YEAR,SUCCESSION_STATE,REPLACEMENT_ALLOWED";

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
                SuccessionState = pReader.GetInt32(10),
                ReplacementAllowed = !pReader.IsDBNull(11) &&
                                     pReader.GetInt32(11) != 0
            };
        }

        private static void Project(Kingdom pSubject, long pStateId,
            long pSuccessorActorId, bool pReplacementAllowed)
        {
            if (pSubject == null) return;
            pSubject.data.set(LineageKeys.MILITARY_GOVERNORATE_SUBJECT_KIND,
                (int)VassalSubjectKind.MilitaryGovernorate);
            pSubject.data.set(LineageKeys.MILITARY_GOVERNORATE_STATE_ID,
                pStateId);
            pSubject.data.set(
                LineageKeys.MILITARY_GOVERNORATE_SUCCESSOR_ACTOR_ID,
                pSuccessorActorId);
            pSubject.data.set(
                LineageKeys.MILITARY_GOVERNORATE_REPLACEMENT_ALLOWED,
                pReplacementAllowed);
        }

        private static void ClearProjection(Kingdom pSubject)
        {
            if (pSubject == null) return;
            pSubject.data.set(LineageKeys.MILITARY_GOVERNORATE_SUBJECT_KIND,
                (int)VassalSubjectKind.Ordinary);
            pSubject.data.set(LineageKeys.MILITARY_GOVERNORATE_STATE_ID, -1L);
            pSubject.data.set(
                LineageKeys.MILITARY_GOVERNORATE_SUCCESSOR_ACTOR_ID, -1L);
            pSubject.data.set(
                LineageKeys.MILITARY_GOVERNORATE_REPLACEMENT_ALLOWED,
                false);
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
