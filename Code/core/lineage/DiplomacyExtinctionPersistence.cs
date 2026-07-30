using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public static class DiplomacyExtinctionPersistence
    {
        public static bool CloseRealm(SQLiteConnection pDb,
            long pKingdomId, int pCurrentYear, double pEventTime)
        {
            if (pDb == null || pKingdomId < 0) return false;
            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
                Execute(pDb, transaction,
                    "UPDATE DiplomacyProposal SET STATUS='cancelled'," +
                    "RESPONSE_YEAR=@year,RESPONSE_TIME=@time," +
                    "RESPONSE_REASON='kingdom_fell' WHERE STATUS IN " +
                    "('pending','processing') AND " + RealmPredicate(
                        "REQUESTER_KINGDOM_ID", "RESPONDER_KINGDOM_ID",
                        "TARGET_KINGDOM_ID"), pKingdomId, pCurrentYear,
                    pEventTime);
                Execute(pDb, transaction,
                    "UPDATE DiplomacyProposal SET " +
                    "TREATY_UNTIL_YEAR=@expired," +
                    "RESPONSE_REASON='kingdom_fell' WHERE " +
                    "STATUS='accepted' AND TREATY_UNTIL_YEAR>=@year AND " +
                    RealmPredicate("REQUESTER_KINGDOM_ID",
                        "RESPONDER_KINGDOM_ID", "TARGET_KINGDOM_ID"),
                    pKingdomId, pCurrentYear, pEventTime);
                Execute(pDb, transaction,
                    "UPDATE DiplomaticOperation SET STATUS=4," +
                    "RESULT='realm_invalid' WHERE (STATUS IN (0,1) OR " +
                    "(STATUS=2 AND RESULT='network_active')) AND " +
                    RealmPredicate("SOURCE_KINGDOM_ID",
                        "TARGET_KINGDOM_ID"), pKingdomId, pCurrentYear,
                    pEventTime);
                Execute(pDb, transaction,
                    "UPDATE DiplomaticCoalition SET STATUS=1," +
                    "END_TIME=@time WHERE STATUS=0 AND " +
                    RealmPredicate("MEMBER_A_ID", "MEMBER_B_ID",
                        "TARGET_KINGDOM_ID"), pKingdomId, pCurrentYear,
                    pEventTime);
                Execute(pDb, transaction,
                    "UPDATE DiplomaticRelationModifier SET ACTIVE=0 " +
                    "WHERE ACTIVE=1 AND " + RealmPredicate(
                        "KINGDOM_A_ID", "KINGDOM_B_ID"), pKingdomId,
                    pCurrentYear, pEventTime);
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction.Rollback(); }
                catch { }
                return false;
            }
        }

        private static string RealmPredicate(params string[] pColumns)
        {
            var predicates = new string[pColumns.Length];
            for (int i = 0; i < pColumns.Length; i++)
                predicates[i] = pColumns[i] + "=@kingdom";
            return "(" + string.Join(" OR ", predicates) + ")";
        }

        private static void Execute(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pSql, long pKingdomId,
            int pCurrentYear, double pEventTime)
        {
            using var command = new SQLiteCommand(pSql, pDb)
            {
                Transaction = pTransaction
            };
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@year", pCurrentYear);
            command.Parameters.AddWithValue("@expired", pCurrentYear - 1);
            command.Parameters.AddWithValue("@time", pEventTime);
            command.ExecuteNonQuery();
        }
    }
}
