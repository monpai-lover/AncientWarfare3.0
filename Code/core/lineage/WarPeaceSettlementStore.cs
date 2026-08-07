using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal sealed class WarPeaceSettlementStore :
        IWarPeaceSettlementStore, IWarPeaceSettlementActionableStore,
        IWarPeaceSettlementExecutionGuardStore,
        IWarPeaceSettlementOrphanRecoveryStore
    {
        private const int MaximumTermsPerProposal = 128;
        private const int MaximumParticipantsPerProposal = 64;
        private const int MaximumExecutedCoalitionTerms = 256;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public bool TryCreate(WarPeaceSettlementDraft draft,
            IReadOnlyList<WarPeaceSettlementTerm> terms,
            out WarPeaceSettlementProposal proposal, out string reason)
        {
            proposal = null;
            reason = "";
            SQLiteConnection db = DB;
            if (db == null)
            {
                reason = "lineage_archive_unavailable";
                return false;
            }
            if (draft != null &&
                draft.Participants.Count > MaximumParticipantsPerProposal)
            {
                reason = "too_many_participants";
                return false;
            }
            SQLiteTransaction transaction = null;
            try
            {
                transaction = db.BeginTransaction(IsolationLevel.Serializable);
                long proposalId = TableIdAllocator.Next(db, transaction,
                    WarPeaceSettlementProposalTableItem.GetTableName(),
                    "PROPOSAL_ID");
                long nextTermId = TableIdAllocator.Next(db, transaction,
                    WarPeaceSettlementTermTableItem.GetTableName(), "TERM_ID");
                long nextParticipantId = TableIdAllocator.Next(db, transaction,
                    WarPeaceSettlementParticipantTableItem.GetTableName(),
                    "PARTICIPANT_ID");
                proposal = WarPeaceSettlementProposal.Create(proposalId,
                    draft, terms);
                proposal.CreatedYear = SafeYear();
                InsertProposal(db, transaction, proposal);
                for (int i = 0; i < proposal.Terms.Count; i++)
                {
                    proposal.Terms[i].TermId = nextTermId++;
                    InsertTerm(db, transaction, proposalId,
                        proposal.Terms[i]);
                }
                for (int i = 0; i < proposal.Participants.Count; i++)
                {
                    proposal.Participants[i].ParticipantId =
                        nextParticipantId++;
                    InsertParticipant(db, transaction, proposalId,
                        proposal.Participants[i]);
                }
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); }
                catch { }
                proposal = null;
                reason = "settlement_insert_failed:" +
                         error.GetType().Name;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public bool TryRead(long proposalId,
            out WarPeaceSettlementProposal proposal)
        {
            proposal = null;
            SQLiteConnection db = DB;
            if (db == null || proposalId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT WAR_ID," +
                    "REQUESTER_KINGDOM_ID,RESPONDER_KINGDOM_ID," +
                    "SCOPE_KIND,EXIT_ROOT_KINGDOM_ID," +
                    "SIGNED_WAR_SCORE,TOTAL_COST,PLAYER_INITIATED," +
                    "AUTOMATIC_EXHAUSTION_SETTLEMENT," +
                    "STATUS,RESPONSE_REASON,RECOVERY_ATTEMPTS," +
                    "CREATED_YEAR,RESPONSE_YEAR FROM " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " WHERE PROPOSAL_ID=@id LIMIT 1";
                command.Parameters.AddWithValue("@id", proposalId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                proposal = new WarPeaceSettlementProposal
                {
                    ProposalId = proposalId,
                    WarId = reader.GetInt64(0),
                    RequesterKingdomId = reader.GetInt64(1),
                    ResponderKingdomId = reader.GetInt64(2),
                    Scope = WarPeaceSettlementScopeRules.ParseScope(
                        reader.IsDBNull(3) ? "" : reader.GetString(3)),
                    ExitRootKingdomId = reader.IsDBNull(4)
                        ? -1
                        : reader.GetInt64(4),
                    SignedWarScore = reader.GetInt32(5),
                    TotalCost = reader.GetInt32(6),
                    PlayerInitiated = reader.GetInt32(7) != 0,
                    AutomaticExhaustionSettlement =
                        reader.GetInt32(8) != 0,
                    Status = ParseStatus(reader.GetString(9)),
                    ResponseReason = reader.IsDBNull(10)
                        ? ""
                        : reader.GetString(10),
                    RecoveryAttempts = reader.GetInt32(11),
                    CreatedYear = reader.IsDBNull(12)
                        ? -1
                        : reader.GetInt32(12),
                    ResponseYear = reader.IsDBNull(13)
                        ? -1
                        : reader.GetInt32(13)
                };
                reader.Close();
                ReadTerms(db, proposalId, proposal.Terms);
                if (!ReadParticipants(db, proposalId,
                        proposal.Participants))
                {
                    proposal = null;
                    return false;
                }
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("War peace settlement read failed: " +
                                    error.Message);
                proposal = null;
                return false;
            }
        }

        public bool TryCancelOneOrphanedPendingForKingdom(long kingdomId,
            out long cancelledProposalId)
        {
            return TryCancelOneOrphanedPendingForKingdom(kingdomId,
                SafeYear(), out cancelledProposalId);
        }

        internal bool TryCancelOneOrphanedPendingForKingdom(long kingdomId,
            int currentYear, out long cancelledProposalId)
        {
            cancelledProposalId = -1L;
            SQLiteConnection db = DB;
            if (db == null || kingdomId < 0 || currentYear < 0)
                return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = db.BeginTransaction(
                    IsolationLevel.Serializable);
                using (var select = new SQLiteCommand(db)
                       {
                           Transaction = transaction,
                           CommandText = "SELECT S.PROPOSAL_ID FROM " +
                               WarPeaceSettlementProposalTableItem.
                                   GetTableName() + " S WHERE " +
                               "(S.REQUESTER_KINGDOM_ID=@kingdom OR " +
                               "S.RESPONDER_KINGDOM_ID=@kingdom) AND " +
                               "S.STATUS='pending' AND " +
                               "S.CREATED_YEAR<@year AND NOT EXISTS " +
                               "(SELECT 1 FROM " +
                               DiplomacyProposalTableItem.GetTableName() +
                               " D WHERE D.DETAIL_ID=@prefix || " +
                               "S.PROPOSAL_ID AND D.STATUS IN " +
                               "('pending','processing')) ORDER BY " +
                               "S.PROPOSAL_ID LIMIT 1"
                       })
                {
                    select.Parameters.AddWithValue("@kingdom", kingdomId);
                    select.Parameters.AddWithValue("@year", currentYear);
                    select.Parameters.AddWithValue("@prefix",
                        WarPeaceSettlementValidationRules.DetailPrefix);
                    object value = select.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                    {
                        transaction.Commit();
                        return true;
                    }
                    cancelledProposalId = Convert.ToInt64(value);
                }

                using (var update = new SQLiteCommand(db)
                       {
                           Transaction = transaction,
                           CommandText = "UPDATE " +
                               WarPeaceSettlementProposalTableItem.
                                   GetTableName() +
                               " SET STATUS='cancelled'," +
                               "RESPONSE_REASON='outer_proposal_orphaned'," +
                               "RESPONSE_YEAR=@year WHERE PROPOSAL_ID=@id " +
                               "AND STATUS='pending'"
                       })
                {
                    update.Parameters.AddWithValue("@year", currentYear);
                    update.Parameters.AddWithValue("@id",
                        cancelledProposalId);
                    if (update.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        cancelledProposalId = -1L;
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
                ModClass.LogWarning("Peace orphan recovery failed: " +
                                    error.Message);
                cancelledProposalId = -1L;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public bool TryBackfillParticipants(long proposalId,
            IReadOnlyList<WarPeaceSettlementParticipantSnapshot>
                participants)
        {
            SQLiteConnection db = DB;
            if (db == null || proposalId < 0 || participants == null ||
                participants.Count == 0 ||
                participants.Count > MaximumParticipantsPerProposal)
                return false;
            var seen = new HashSet<long>();
            for (int i = 0; i < participants.Count; i++)
                if (participants[i] == null ||
                    participants[i].KingdomId < 0 ||
                    !seen.Add(participants[i].KingdomId)) return false;

            SQLiteTransaction transaction = null;
            try
            {
                transaction = db.BeginTransaction(
                    IsolationLevel.Serializable);
                using (var scope = new SQLiteCommand(db)
                       {
                           Transaction = transaction,
                           CommandText = "SELECT SCOPE_KIND FROM " +
                               WarPeaceSettlementProposalTableItem.
                                   GetTableName() +
                               " WHERE PROPOSAL_ID=@id LIMIT 1"
                       })
                {
                    scope.Parameters.AddWithValue("@id", proposalId);
                    object value = scope.ExecuteScalar();
                    if (value == null || value == DBNull.Value ||
                        WarPeaceSettlementScopeRules.ParseScope(
                            Convert.ToString(value)) !=
                        WarPeaceSettlementScopeKind.Coalition)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }

                using (var count = new SQLiteCommand(db)
                       {
                           Transaction = transaction,
                           CommandText = "SELECT COUNT(*) FROM " +
                               WarPeaceSettlementParticipantTableItem.
                                   GetTableName() +
                               " WHERE PROPOSAL_ID=@id"
                       })
                {
                    count.Parameters.AddWithValue("@id", proposalId);
                    if (Convert.ToInt32(count.ExecuteScalar()) > 0)
                    {
                        transaction.Commit();
                        return true;
                    }
                }

                long nextParticipantId = TableIdAllocator.Next(db,
                    transaction,
                    WarPeaceSettlementParticipantTableItem.GetTableName(),
                    "PARTICIPANT_ID");
                for (int i = 0; i < participants.Count; i++)
                {
                    WarPeaceSettlementParticipantSnapshot snapshot =
                        participants[i].Clone();
                    snapshot.ParticipantId = nextParticipantId++;
                    InsertParticipant(db, transaction, proposalId,
                        snapshot);
                }
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                ModClass.LogWarning(
                    "Legacy peace participant backfill failed: " +
                    error.Message);
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public bool TryReadActionableWinnerProposalForWar(long warId,
            long requesterKingdomId, long responderKingdomId,
            out long proposalId)
        {
            proposalId = -1;
            SQLiteConnection db = DB;
            if (db == null || warId < 0 || requesterKingdomId < 0 ||
                responderKingdomId < 0)
                return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT PROPOSAL_ID FROM " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND " +
                    "REQUESTER_KINGDOM_ID=@requester AND " +
                    "RESPONDER_KINGDOM_ID=@responder AND " +
                    "SIGNED_WAR_SCORE>0 AND STATUS IN " +
                    "('pending','accepted','executing','terms_applied'," +
                    "'executed') ORDER BY PROPOSAL_ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@war", warId);
                command.Parameters.AddWithValue("@requester",
                    requesterKingdomId);
                command.Parameters.AddWithValue("@responder",
                    responderKingdomId);
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;
                proposalId = Convert.ToInt64(value);
                return proposalId >= 0;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Actionable winner peace lookup failed: " +
                                    error.Message);
                proposalId = -1;
                return false;
            }
        }

        public bool HasActionableSettlement(long warId)
        {
            SQLiteConnection db = DB;
            if (db == null || warId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT 1 FROM " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND STATUS IN " +
                    "('accepted','executing','terms_applied') LIMIT 1";
                command.Parameters.AddWithValue("@war", warId);
                return command.ExecuteScalar() != null;
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Actionable peace execution guard lookup failed: " +
                    error.Message);
                return true;
            }
        }

        public bool TrySetStatus(long proposalId,
            WarPeaceSettlementStatus expected,
            WarPeaceSettlementStatus next, string reason)
        {
            SQLiteConnection db = DB;
            if (db == null || proposalId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "UPDATE " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " SET STATUS=@next,RESPONSE_REASON=@reason," +
                    "RESPONSE_YEAR=CASE WHEN @responded=1 THEN @year " +
                    "ELSE RESPONSE_YEAR END," +
                    "RESPONSE_TIME=CASE WHEN @responded=1 THEN @time " +
                    "ELSE RESPONSE_TIME END," +
                    "EXECUTED_TIME=CASE WHEN @executed=1 THEN @time " +
                    "ELSE EXECUTED_TIME END " +
                    "WHERE PROPOSAL_ID=@id AND STATUS=@expected";
                command.Parameters.AddWithValue("@next", StatusId(next));
                command.Parameters.AddWithValue("@reason", reason ?? "");
                command.Parameters.AddWithValue("@responded",
                    next == WarPeaceSettlementStatus.Accepted ||
                    next == WarPeaceSettlementStatus.Rejected ? 1 : 0);
                command.Parameters.AddWithValue("@executed",
                    next == WarPeaceSettlementStatus.Executed ? 1 : 0);
                command.Parameters.AddWithValue("@year", SafeYear());
                command.Parameters.AddWithValue("@time",
                    LineageService.CurTime());
                command.Parameters.AddWithValue("@id", proposalId);
                command.Parameters.AddWithValue("@expected",
                    StatusId(expected));
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("War peace status update failed: " +
                                    error.Message);
                return false;
            }
        }

        public bool TrySetTermApplyStatus(long proposalId, long termId,
            WarPeaceSettlementTermApplyStatus expected,
            WarPeaceSettlementTermApplyStatus next, string reason)
        {
            SQLiteConnection db = DB;
            if (db == null || proposalId < 0 || termId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "UPDATE " +
                    WarPeaceSettlementTermTableItem.GetTableName() +
                    " SET APPLY_STATUS=@next,APPLY_REASON=@reason," +
                    "APPLIED_TIME=CASE WHEN @applied=1 THEN @time " +
                    "ELSE APPLIED_TIME END WHERE PROPOSAL_ID=@proposal " +
                    "AND TERM_ID=@term AND APPLY_STATUS=@expected";
                command.Parameters.AddWithValue("@next",
                    ApplyStatusId(next));
                command.Parameters.AddWithValue("@reason", reason ?? "");
                command.Parameters.AddWithValue("@applied",
                    next == WarPeaceSettlementTermApplyStatus.Applied
                        ? 1 : 0);
                command.Parameters.AddWithValue("@time",
                    LineageService.CurTime());
                command.Parameters.AddWithValue("@proposal", proposalId);
                command.Parameters.AddWithValue("@term", termId);
                command.Parameters.AddWithValue("@expected",
                    ApplyStatusId(expected));
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("War peace term status update failed: " +
                                    error.Message);
                return false;
            }
        }

        public bool TryBeginTermApplication(long proposalId, long termId,
            WarPeaceTermExecutionBaseline baseline, string reason)
        {
            SQLiteConnection db = DB;
            if (db == null || proposalId < 0 || termId < 0 ||
                !baseline.Captured) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "UPDATE " +
                    WarPeaceSettlementTermTableItem.GetTableName() +
                    " SET APPLY_STATUS='applying'," +
                    "APPLY_REASON=@reason,BASELINE_CAPTURED=1," +
                    "SOURCE_AMOUNT_BEFORE=@source," +
                    "TARGET_AMOUNT_BEFORE=@target," +
                    "SOURCE_CITY_ID=@source_city," +
                    "TARGET_CITY_ID=@target_city WHERE " +
                    "PROPOSAL_ID=@proposal AND TERM_ID=@term AND " +
                    "APPLY_STATUS='pending'";
                command.Parameters.AddWithValue("@reason", reason ?? "");
                command.Parameters.AddWithValue("@source",
                    baseline.SourceAmount);
                command.Parameters.AddWithValue("@target",
                    baseline.TargetAmount);
                command.Parameters.AddWithValue("@source_city",
                    baseline.SourceCityId);
                command.Parameters.AddWithValue("@target_city",
                    baseline.TargetCityId);
                command.Parameters.AddWithValue("@proposal", proposalId);
                command.Parameters.AddWithValue("@term", termId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("War peace term begin failed: " +
                                    error.Message);
                return false;
            }
        }

        public bool TryHasExecutedCoalitionSettlement(long warId,
            out bool executed)
        {
            executed = false;
            SQLiteConnection db = DB;
            if (db == null || warId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT 1 FROM " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND SCOPE_KIND='coalition' AND " +
                    "STATUS IN " +
                    "('terms_applied','executed') LIMIT 1";
                command.Parameters.AddWithValue("@war", warId);
                executed = command.ExecuteScalar() != null;
                return true;
            }
            catch { return false; }
        }

        public bool HasExecutedCoalitionSettlement(long warId)
        {
            return TryHasExecutedCoalitionSettlement(warId,
                out bool executed) && executed;
        }

        public bool HasExecutedSettlement(long warId)
        {
            return HasExecutedCoalitionSettlement(warId);
        }

        public bool TryReadExecutedCoalitionTerms(long warId,
            out IReadOnlyList<WarPeaceSettlementTerm> terms)
        {
            terms = Array.Empty<WarPeaceSettlementTerm>();
            var result = new List<WarPeaceSettlementTerm>();
            SQLiteConnection db = DB;
            if (db == null || warId < 0) return false;
            try
            {
                if (!TryReadExecutedCoalitionProposalId(db, warId,
                        out long proposalId)) return false;
                if (proposalId < 0)
                {
                    terms = result;
                    return true;
                }
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT T.TERM_ID,T.POSITION," +
                    "T.TERM_KIND,T.COST,T.FROM_KINGDOM_ID," +
                    "T.TO_KINGDOM_ID,T.RESOURCE_ID,T.AMOUNT," +
                    "T.DURATION_YEARS,T.CITY_ID,T.CAPTIVE_ACTOR_ID," +
                    "T.CLAIM_ID,T.FROZEN_OCCUPATION," +
                    "T.CORE_OR_CLAIM_BASIS,T.APPLY_STATUS," +
                    "T.APPLY_REASON,T.BASELINE_CAPTURED," +
                    "T.SOURCE_AMOUNT_BEFORE,T.TARGET_AMOUNT_BEFORE," +
                    "T.SOURCE_CITY_ID,T.TARGET_CITY_ID," +
                    "T.WAR_GOAL_ID FROM " +
                    WarPeaceSettlementTermTableItem.GetTableName() +
                    " T WHERE PROPOSAL_ID=@proposal " +
                    "ORDER BY T.POSITION ASC," +
                    "T.TERM_ID ASC LIMIT @limit";
                command.Parameters.AddWithValue("@proposal", proposalId);
                command.Parameters.AddWithValue("@limit",
                    MaximumExecutedCoalitionTerms + 1);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (result.Count >= MaximumExecutedCoalitionTerms)
                    {
                        result.Clear();
                        return false;
                    }
                    result.Add(ReadTerm(reader));
                }
                terms = result;
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Executed peace terms read failed: " +
                                    error.Message);
                return false;
            }
        }

        private static bool TryReadExecutedCoalitionProposalId(
            SQLiteConnection db, long warId, out long proposalId)
        {
            proposalId = -1;
            using var command = new SQLiteCommand(db);
            command.CommandText = "SELECT PROPOSAL_ID FROM " +
                WarPeaceSettlementProposalTableItem.GetTableName() +
                " WHERE WAR_ID=@war AND SCOPE_KIND='coalition' AND " +
                "STATUS IN ('terms_applied','executed') " +
                "ORDER BY PROPOSAL_ID DESC LIMIT 1";
            command.Parameters.AddWithValue("@war", warId);
            object value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value) return true;
            proposalId = Convert.ToInt64(value);
            return proposalId >= 0;
        }

        public IReadOnlyList<WarPeaceSettlementTerm>
            ReadExecutedCoalitionTerms(long warId)
        {
            return TryReadExecutedCoalitionTerms(warId, out var terms)
                ? terms
                : Array.Empty<WarPeaceSettlementTerm>();
        }

        public IReadOnlyList<WarPeaceSettlementTerm> ReadExecutedTerms(
            long warId)
        {
            return ReadExecutedCoalitionTerms(warId);
        }

        public IReadOnlyList<long> ReadRecoveryCandidatesForKingdom(
            long kingdomId, int limit)
        {
            var result = new List<long>();
            SQLiteConnection db = DB;
            int bounded = Math.Max(1, Math.Min(8, limit));
            if (db == null || kingdomId < 0) return result;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT PROPOSAL_ID FROM " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " WHERE (REQUESTER_KINGDOM_ID=@kingdom OR " +
                    "RESPONDER_KINGDOM_ID=@kingdom) AND STATUS IN " +
                    "('executing','terms_applied') " +
                    "ORDER BY RECOVERY_ATTEMPTS ASC," +
                    "CASE STATUS WHEN 'executing' THEN 0 " +
                    "WHEN 'terms_applied' THEN 1 ELSE 2 END," +
                    "PROPOSAL_ID DESC LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", kingdomId);
                command.Parameters.AddWithValue("@limit", bounded);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(reader.GetInt64(0));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Peace recovery query failed: " +
                                    error.Message);
            }
            return result;
        }

        public bool TryMarkRecoveryAttempt(long proposalId)
        {
            SQLiteConnection db = DB;
            if (db == null || proposalId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "UPDATE " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " SET RECOVERY_ATTEMPTS=CASE WHEN RECOVERY_ATTEMPTS<" +
                    int.MaxValue + " THEN RECOVERY_ATTEMPTS+1 ELSE " +
                    "RECOVERY_ATTEMPTS END WHERE PROPOSAL_ID=@id AND " +
                    "STATUS IN ('executing','terms_applied','executed')";
                command.Parameters.AddWithValue("@id", proposalId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Peace recovery attempt update failed: " +
                                    error.Message);
                return false;
            }
        }

        public bool TryReadExecutedProposalForWar(long warId,
            out long proposalId)
        {
            proposalId = -1;
            SQLiteConnection db = DB;
            if (db == null || warId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT PROPOSAL_ID FROM " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND SCOPE_KIND='coalition' AND " +
                    "STATUS='executed' " +
                    "ORDER BY PROPOSAL_ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@war", warId);
                object value = command.ExecuteScalar();
                if (value == null || value == DBNull.Value) return false;
                proposalId = Convert.ToInt64(value);
                return proposalId >= 0;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Executed peace lookup failed: " +
                                    error.Message);
                proposalId = -1;
                return false;
            }
        }

        private static void InsertProposal(SQLiteConnection db,
            SQLiteTransaction transaction,
            WarPeaceSettlementProposal proposal)
        {
            using var command = new SQLiteCommand(db)
            {
                Transaction = transaction,
                CommandText = "INSERT INTO " +
                    WarPeaceSettlementProposalTableItem.GetTableName() +
                    " (PROPOSAL_ID,WAR_ID,REQUESTER_KINGDOM_ID," +
                    "RESPONDER_KINGDOM_ID,SCOPE_KIND," +
                    "EXIT_ROOT_KINGDOM_ID,SIGNED_WAR_SCORE,TOTAL_COST," +
                    "PLAYER_INITIATED,AUTOMATIC_EXHAUSTION_SETTLEMENT," +
                    "STATUS,RESPONSE_REASON," +
                    "RECOVERY_ATTEMPTS," +
                    "CREATED_YEAR,RESPONSE_YEAR,CREATED_TIME," +
                    "RESPONSE_TIME,EXECUTED_TIME) VALUES " +
                    "(@id,@war,@requester,@responder,@scope,@exitRoot," +
                    "@score,@cost,@player,@automaticExhaustion," +
                    "'pending','',0,@year,-1,@time,-1,-1)"
            };
            command.Parameters.AddWithValue("@id", proposal.ProposalId);
            command.Parameters.AddWithValue("@war", proposal.WarId);
            command.Parameters.AddWithValue("@requester",
                proposal.RequesterKingdomId);
            command.Parameters.AddWithValue("@responder",
                proposal.ResponderKingdomId);
            command.Parameters.AddWithValue("@scope",
                WarPeaceSettlementScopeRules.ScopeId(proposal.Scope));
            command.Parameters.AddWithValue("@exitRoot",
                proposal.ExitRootKingdomId);
            command.Parameters.AddWithValue("@score",
                proposal.SignedWarScore);
            command.Parameters.AddWithValue("@cost", proposal.TotalCost);
            command.Parameters.AddWithValue("@player",
                proposal.PlayerInitiated ? 1 : 0);
            command.Parameters.AddWithValue("@automaticExhaustion",
                proposal.AutomaticExhaustionSettlement ? 1 : 0);
            command.Parameters.AddWithValue("@year", proposal.CreatedYear);
            command.Parameters.AddWithValue("@time",
                LineageService.CurTime());
            command.ExecuteNonQuery();
        }

        private static void InsertParticipant(SQLiteConnection db,
            SQLiteTransaction transaction, long proposalId,
            WarPeaceSettlementParticipantSnapshot participant)
        {
            using var command = new SQLiteCommand(db)
            {
                Transaction = transaction,
                CommandText = "INSERT INTO " +
                    WarPeaceSettlementParticipantTableItem.GetTableName() +
                    " (PARTICIPANT_ID,PROPOSAL_ID,KINGDOM_ID,SIDE_KIND," +
                    "PARTICIPANT_ROLE,EXIT_PARENT_ID,VASSAL_RELATION_ID," +
                    "ENTRY_SOURCE_KIND,ENTRY_SOURCE_FINGERPRINT," +
                    "INCLUDED_IN_EXIT_GROUP) VALUES " +
                    "(@id,@proposal,@kingdom,@side,@role,@parent," +
                    "@relation,@source,@fingerprint,@included)"
            };
            command.Parameters.AddWithValue("@id", participant.ParticipantId);
            command.Parameters.AddWithValue("@proposal", proposalId);
            command.Parameters.AddWithValue("@kingdom", participant.KingdomId);
            command.Parameters.AddWithValue("@side", participant.SideKind ?? "");
            command.Parameters.AddWithValue("@role",
                participant.ParticipantRole ?? "");
            command.Parameters.AddWithValue("@parent", participant.ExitParentId);
            command.Parameters.AddWithValue("@relation",
                participant.VassalRelationId);
            command.Parameters.AddWithValue("@source",
                EntrySourceId(participant.EntrySourceKind));
            command.Parameters.AddWithValue("@fingerprint",
                string.IsNullOrEmpty(participant.EntrySourceFingerprint)
                    ? "unknown"
                    : participant.EntrySourceFingerprint);
            command.Parameters.AddWithValue("@included",
                participant.IncludedInExitGroup ? 1 : 0);
            command.ExecuteNonQuery();
        }

        private static void InsertTerm(SQLiteConnection db,
            SQLiteTransaction transaction, long proposalId,
            WarPeaceSettlementTerm term)
        {
            using var command = new SQLiteCommand(db)
            {
                Transaction = transaction,
                CommandText = "INSERT INTO " +
                    WarPeaceSettlementTermTableItem.GetTableName() +
                    " (TERM_ID,PROPOSAL_ID,POSITION,TERM_KIND,COST," +
                    "FROM_KINGDOM_ID,TO_KINGDOM_ID,RESOURCE_ID,AMOUNT," +
                    "DURATION_YEARS,CITY_ID,CAPTIVE_ACTOR_ID,CLAIM_ID," +
                    "FROZEN_OCCUPATION,CORE_OR_CLAIM_BASIS," +
                    "APPLY_STATUS,APPLY_REASON,APPLIED_TIME," +
                    "BASELINE_CAPTURED,SOURCE_AMOUNT_BEFORE," +
                    "TARGET_AMOUNT_BEFORE,SOURCE_CITY_ID," +
                    "TARGET_CITY_ID,WAR_GOAL_ID) VALUES " +
                    "(@id,@proposal,@position,@kind,@cost,@from,@to," +
                    "@resource,@amount,@duration,@city,@captive,@claim," +
                    "@occupation,@basis,'pending','',-1,0,-1,-1,-1,-1," +
                    "@warGoal)"
            };
            command.Parameters.AddWithValue("@id", term.TermId);
            command.Parameters.AddWithValue("@proposal", proposalId);
            command.Parameters.AddWithValue("@position", term.Position);
            command.Parameters.AddWithValue("@kind", term.Kind.ToString());
            command.Parameters.AddWithValue("@cost", term.Cost);
            command.Parameters.AddWithValue("@from", term.FromKingdomId);
            command.Parameters.AddWithValue("@to", term.ToKingdomId);
            command.Parameters.AddWithValue("@resource",
                term.ResourceId ?? "");
            command.Parameters.AddWithValue("@amount", term.Amount);
            command.Parameters.AddWithValue("@duration",
                term.DurationYears);
            command.Parameters.AddWithValue("@city", term.CityId);
            command.Parameters.AddWithValue("@captive",
                term.CaptiveActorId);
            command.Parameters.AddWithValue("@claim", term.ClaimId);
            command.Parameters.AddWithValue("@warGoal", term.WarGoalId);
            command.Parameters.AddWithValue("@occupation",
                term.FrozenOccupation ? 1 : 0);
            command.Parameters.AddWithValue("@basis",
                term.CoreOrClaimBasis ? 1 : 0);
            command.ExecuteNonQuery();
        }

        private static void ReadTerms(SQLiteConnection db, long proposalId,
            List<WarPeaceSettlementTerm> terms)
        {
            using var command = new SQLiteCommand(db);
            command.CommandText = "SELECT TERM_ID,POSITION,TERM_KIND," +
                "COST,FROM_KINGDOM_ID,TO_KINGDOM_ID,RESOURCE_ID,AMOUNT," +
                "DURATION_YEARS,CITY_ID,CAPTIVE_ACTOR_ID,CLAIM_ID," +
                "FROZEN_OCCUPATION,CORE_OR_CLAIM_BASIS,APPLY_STATUS," +
                "APPLY_REASON,BASELINE_CAPTURED,SOURCE_AMOUNT_BEFORE," +
                "TARGET_AMOUNT_BEFORE,SOURCE_CITY_ID,TARGET_CITY_ID," +
                "WAR_GOAL_ID FROM " +
                WarPeaceSettlementTermTableItem.GetTableName() +
                " WHERE PROPOSAL_ID=@id ORDER BY POSITION ASC," +
                "TERM_ID ASC LIMIT @limit";
            command.Parameters.AddWithValue("@id", proposalId);
            command.Parameters.AddWithValue("@limit",
                MaximumTermsPerProposal);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) terms.Add(ReadTerm(reader));
        }

        private static bool ReadParticipants(SQLiteConnection db,
            long proposalId,
            List<WarPeaceSettlementParticipantSnapshot> participants)
        {
            using var command = new SQLiteCommand(db);
            command.CommandText = "SELECT PARTICIPANT_ID,KINGDOM_ID," +
                "SIDE_KIND,PARTICIPANT_ROLE,EXIT_PARENT_ID," +
                "VASSAL_RELATION_ID,ENTRY_SOURCE_KIND," +
                "ENTRY_SOURCE_FINGERPRINT," +
                "INCLUDED_IN_EXIT_GROUP FROM " +
                WarPeaceSettlementParticipantTableItem.GetTableName() +
                " WHERE PROPOSAL_ID=@proposal ORDER BY PARTICIPANT_ID ASC " +
                "LIMIT @limit";
            command.Parameters.AddWithValue("@proposal", proposalId);
            command.Parameters.AddWithValue("@limit",
                MaximumParticipantsPerProposal + 1);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (participants.Count >= MaximumParticipantsPerProposal)
                {
                    participants.Clear();
                    return false;
                }
                participants.Add(new WarPeaceSettlementParticipantSnapshot
                {
                    ParticipantId = reader.GetInt64(0),
                    KingdomId = reader.GetInt64(1),
                    SideKind = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ParticipantRole = reader.IsDBNull(3)
                        ? ""
                        : reader.GetString(3),
                    ExitParentId = reader.IsDBNull(4) ? -1 : reader.GetInt64(4),
                    VassalRelationId = reader.IsDBNull(5)
                        ? -1
                        : reader.GetInt64(5),
                    EntrySourceKind = ParseEntrySource(
                        reader.IsDBNull(6) ? "" : reader.GetString(6)),
                    EntrySourceFingerprint = reader.IsDBNull(7)
                        ? "unknown"
                        : reader.GetString(7),
                    IncludedInExitGroup = reader.GetInt32(8) != 0
                });
            }
            return true;
        }

        private static WarPeaceSettlementTerm ReadTerm(
            SQLiteDataReader reader)
        {
            Enum.TryParse(reader.GetString(2), out WarPeaceTermKind kind);
            return new WarPeaceSettlementTerm
            {
                TermId = reader.GetInt64(0),
                Position = reader.GetInt32(1),
                Kind = kind,
                Cost = reader.GetInt32(3),
                FromKingdomId = reader.GetInt64(4),
                ToKingdomId = reader.GetInt64(5),
                ResourceId = reader.IsDBNull(6) ? "" :
                    reader.GetString(6),
                Amount = reader.GetInt32(7),
                DurationYears = reader.GetInt32(8),
                CityId = reader.GetInt64(9),
                CaptiveActorId = reader.GetInt64(10),
                ClaimId = reader.GetInt64(11),
                FrozenOccupation = reader.GetInt32(12) != 0,
                CoreOrClaimBasis = reader.GetInt32(13) != 0,
                ApplyStatus = ParseApplyStatus(reader.GetString(14)),
                ApplyReason = reader.IsDBNull(15) ? "" :
                    reader.GetString(15),
                BaselineCaptured = reader.GetInt32(16) != 0,
                SourceAmountBefore = reader.GetInt32(17),
                TargetAmountBefore = reader.GetInt32(18),
                SourceCityId = reader.GetInt64(19),
                TargetCityId = reader.GetInt64(20),
                WarGoalId = reader.IsDBNull(21) ? -1 : reader.GetInt64(21)
            };
        }

        private static string StatusId(WarPeaceSettlementStatus status)
        {
            return status switch
            {
                WarPeaceSettlementStatus.Accepted => "accepted",
                WarPeaceSettlementStatus.Rejected => "rejected",
                WarPeaceSettlementStatus.Executing => "executing",
                WarPeaceSettlementStatus.TermsApplied => "terms_applied",
                WarPeaceSettlementStatus.Executed => "executed",
                WarPeaceSettlementStatus.Cancelled => "cancelled",
                _ => "pending"
            };
        }

        private static WarPeaceSettlementStatus ParseStatus(string status)
        {
            return status switch
            {
                "accepted" => WarPeaceSettlementStatus.Accepted,
                "rejected" => WarPeaceSettlementStatus.Rejected,
                "executing" => WarPeaceSettlementStatus.Executing,
                "terms_applied" => WarPeaceSettlementStatus.TermsApplied,
                "executed" => WarPeaceSettlementStatus.Executed,
                "cancelled" => WarPeaceSettlementStatus.Cancelled,
                _ => WarPeaceSettlementStatus.Pending
            };
        }

        private static string ApplyStatusId(
            WarPeaceSettlementTermApplyStatus status)
        {
            return status switch
            {
                WarPeaceSettlementTermApplyStatus.Applying => "applying",
                WarPeaceSettlementTermApplyStatus.Applied => "applied",
                _ => "pending"
            };
        }

        private static WarPeaceSettlementTermApplyStatus ParseApplyStatus(
            string status)
        {
            return status switch
            {
                "applying" => WarPeaceSettlementTermApplyStatus.Applying,
                "applied" => WarPeaceSettlementTermApplyStatus.Applied,
                _ => WarPeaceSettlementTermApplyStatus.Pending
            };
        }

        private static string EntrySourceId(
            WarParticipantEntrySourceKind source)
        {
            return source switch
            {
                WarParticipantEntrySourceKind.MainBelligerent =>
                    "main_belligerent",
                WarParticipantEntrySourceKind.AllianceCall => "alliance_call",
                WarParticipantEntrySourceKind.FormalVassalObligation =>
                    "formal_vassal_obligation",
                WarParticipantEntrySourceKind.IndependentDeclaration =>
                    "independent_declaration",
                WarParticipantEntrySourceKind.ScriptedJoin => "scripted_join",
                WarParticipantEntrySourceKind.SeparatePeaceExit =>
                    "separate_peace_exit",
                _ => "unknown"
            };
        }

        private static WarParticipantEntrySourceKind ParseEntrySource(
            string source)
        {
            return source switch
            {
                "main_belligerent" =>
                    WarParticipantEntrySourceKind.MainBelligerent,
                "alliance_call" => WarParticipantEntrySourceKind.AllianceCall,
                "formal_vassal_obligation" =>
                    WarParticipantEntrySourceKind.FormalVassalObligation,
                "independent_declaration" =>
                    WarParticipantEntrySourceKind.IndependentDeclaration,
                "scripted_join" => WarParticipantEntrySourceKind.ScriptedJoin,
                "separate_peace_exit" =>
                    WarParticipantEntrySourceKind.SeparatePeaceExit,
                _ => WarParticipantEntrySourceKind.Unknown
            };
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }
    }
}
