using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public enum DiplomacyTreatyBreakOutcome
    {
        Failed = 0,
        NoActivePact = 1,
        Committed = 2
    }

    public readonly struct DiplomacyTreatyBreakRequest
    {
        public DiplomacyTreatyBreakRequest(long pRequesterId,
            string pRequesterName, long pResponderId, string pResponderName,
            int pCurrentYear, int pTruceUntilYear, double pEventTime,
            string pRequesterTitle, string pRequestYearPrefix,
            string pRequestStyle, string pRequestTone,
            bool pPlayerInitiated)
        {
            RequesterId = pRequesterId;
            RequesterName = pRequesterName ?? "";
            ResponderId = pResponderId;
            ResponderName = pResponderName ?? "";
            CurrentYear = pCurrentYear;
            TruceUntilYear = pTruceUntilYear;
            EventTime = pEventTime;
            RequesterTitle = pRequesterTitle ?? "";
            RequestYearPrefix = pRequestYearPrefix ?? "";
            RequestStyle = string.IsNullOrWhiteSpace(pRequestStyle)
                ? "peer"
                : pRequestStyle;
            RequestTone = string.IsNullOrWhiteSpace(pRequestTone)
                ? "neutral"
                : pRequestTone;
            PlayerInitiated = pPlayerInitiated;
        }

        public long RequesterId { get; }
        public string RequesterName { get; }
        public long ResponderId { get; }
        public string ResponderName { get; }
        public int CurrentYear { get; }
        public int TruceUntilYear { get; }
        public double EventTime { get; }
        public string RequesterTitle { get; }
        public string RequestYearPrefix { get; }
        public string RequestStyle { get; }
        public string RequestTone { get; }
        public bool PlayerInitiated { get; }
    }

    public static class DiplomacyTreatyPersistence
    {
        public static DiplomacyTreatyBreakOutcome BreakNonAggression(
            SQLiteConnection pDb, string pTableName,
            DiplomacyTreatyBreakRequest pRequest, out long pTruceProposalId)
        {
            pTruceProposalId = -1L;
            if (pDb == null || string.IsNullOrWhiteSpace(pTableName) ||
                pRequest.RequesterId < 0 || pRequest.ResponderId < 0 ||
                pRequest.RequesterId == pRequest.ResponderId ||
                pRequest.TruceUntilYear <= pRequest.CurrentYear)
                return DiplomacyTreatyBreakOutcome.Failed;

            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
                using (var close = new SQLiteCommand(pDb)
                       { Transaction = transaction })
                {
                    close.CommandText = "UPDATE " + pTableName +
                        " SET TREATY_UNTIL_YEAR=@expired," +
                        "RESPONSE_REASON='broken_by_party' WHERE " +
                        "PROPOSAL_TYPE='non_aggression' AND " +
                        "STATUS='accepted' AND TREATY_UNTIL_YEAR>=@year AND " +
                        "((REQUESTER_KINGDOM_ID=@a AND " +
                        "RESPONDER_KINGDOM_ID=@b) OR " +
                        "(REQUESTER_KINGDOM_ID=@b AND " +
                        "RESPONDER_KINGDOM_ID=@a))";
                    close.Parameters.AddWithValue("@expired",
                        pRequest.CurrentYear - 1);
                    close.Parameters.AddWithValue("@year",
                        pRequest.CurrentYear);
                    close.Parameters.AddWithValue("@a",
                        pRequest.RequesterId);
                    close.Parameters.AddWithValue("@b",
                        pRequest.ResponderId);
                    if (close.ExecuteNonQuery() <= 0)
                    {
                        transaction.Rollback();
                        return DiplomacyTreatyBreakOutcome.NoActivePact;
                    }
                }

                using (var next = new SQLiteCommand(pDb)
                       { Transaction = transaction })
                {
                    next.CommandText = "SELECT IFNULL(MAX(PROPOSAL_ID),0)+1 " +
                                       "FROM " + pTableName;
                    pTruceProposalId = Convert.ToInt64(next.ExecuteScalar());
                }

                using (var insert = new SQLiteCommand(pDb)
                       { Transaction = transaction })
                {
                    insert.CommandText = "INSERT INTO " + pTableName +
                        " (PROPOSAL_ID,REQUESTER_KINGDOM_ID,REQUESTER_NAME," +
                        "RESPONDER_KINGDOM_ID,RESPONDER_NAME,PROPOSAL_TYPE," +
                        "STATUS,WAR_ID,PLAYER_INITIATED,CREATED_YEAR," +
                        "EXPIRY_YEAR,RESPONSE_YEAR,TREATY_UNTIL_YEAR," +
                        "CREATED_TIME,RESPONSE_DUE_TIME,RESPONSE_TIME," +
                        "RESPONSE_REASON,REQUESTER_TITLE,RESPONDER_TITLE," +
                        "REQUEST_YEAR_PREFIX,RESPONSE_YEAR_PREFIX," +
                        "REQUEST_STYLE,REQUEST_TONE) VALUES " +
                        "(@id,@requester,@requesterName,@responder," +
                        "@responderName,'truce','accepted',-1,@player,@year," +
                        "@until,@year,@until,@time,@time,@time," +
                        "'non_aggression_broken',@title,'',@prefix,''," +
                        "@style,@tone)";
                    insert.Parameters.AddWithValue("@id", pTruceProposalId);
                    insert.Parameters.AddWithValue("@requester",
                        pRequest.RequesterId);
                    insert.Parameters.AddWithValue("@requesterName",
                        pRequest.RequesterName);
                    insert.Parameters.AddWithValue("@responder",
                        pRequest.ResponderId);
                    insert.Parameters.AddWithValue("@responderName",
                        pRequest.ResponderName);
                    insert.Parameters.AddWithValue("@player",
                        pRequest.PlayerInitiated ? 1 : 0);
                    insert.Parameters.AddWithValue("@year",
                        pRequest.CurrentYear);
                    insert.Parameters.AddWithValue("@until",
                        pRequest.TruceUntilYear);
                    insert.Parameters.AddWithValue("@time",
                        pRequest.EventTime);
                    insert.Parameters.AddWithValue("@title",
                        pRequest.RequesterTitle);
                    insert.Parameters.AddWithValue("@prefix",
                        pRequest.RequestYearPrefix);
                    insert.Parameters.AddWithValue("@style",
                        pRequest.RequestStyle);
                    insert.Parameters.AddWithValue("@tone",
                        pRequest.RequestTone);
                    if (insert.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "broken-pact truce insert failed");
                }
                transaction.Commit();
                return DiplomacyTreatyBreakOutcome.Committed;
            }
            catch
            {
                try { transaction.Rollback(); }
                catch { }
                pTruceProposalId = -1L;
                return DiplomacyTreatyBreakOutcome.Failed;
            }
        }
    }
}
