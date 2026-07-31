using System;
using System.Data;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal static class VassalRelationConversionPersistence
    {
        public static bool TryConvert(SQLiteConnection pDb,
            string pTableName, long pVassalId, long pSuzerainId,
            int pReplacementTier, double pNow, long pWarId,
            out long pSourceRelationId, out long pReplacementRelationId,
            out string pReason)
        {
            pSourceRelationId = -1L;
            pReplacementRelationId = -1L;
            pReason = "invalid";
            if (pDb == null || pDb.State != ConnectionState.Open)
            {
                pReason = "conversion_database_unavailable";
                return false;
            }
            if (!IsSafeIdentifier(pTableName))
            {
                pReason = "conversion_table_invalid";
                return false;
            }
            if (pVassalId < 0L || pSuzerainId < 0L ||
                pVassalId == pSuzerainId ||
                pReplacementTier != VassalContractTierRules.Inner &&
                pReplacementTier != VassalContractTierRules.Outer)
            {
                pReason = "conversion_target_invalid";
                return false;
            }

            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                string vassalName;
                string vassalColor;
                string suzerainName;
                string suzerainColor;
                using (var select = new SQLiteCommand(
                    "SELECT RELATION_ID,VASSAL_NAME,VASSAL_COLOR," +
                    "SUZERAIN_NAME,SUZERAIN_COLOR FROM " + pTableName +
                    " WHERE VASSAL_ID=@vassal AND SUZERAIN_ID=@suzerain" +
                    " AND ACTIVE=1 AND CONTRACT_TIER=@tributary" +
                    " ORDER BY RELATION_ID LIMIT 2", pDb, transaction))
                {
                    select.Parameters.AddWithValue("@vassal", pVassalId);
                    select.Parameters.AddWithValue("@suzerain", pSuzerainId);
                    select.Parameters.AddWithValue("@tributary",
                        VassalContractTierRules.Tributary);
                    using SQLiteDataReader reader = select.ExecuteReader();
                    if (!reader.Read())
                    {
                        pReason = "source_relation_missing";
                        transaction.Rollback();
                        return false;
                    }
                    pSourceRelationId = reader.GetInt64(0);
                    vassalName = ReadString(reader, 1);
                    vassalColor = ReadString(reader, 2);
                    suzerainName = ReadString(reader, 3);
                    suzerainColor = ReadString(reader, 4);
                    if (reader.Read())
                    {
                        pReason = "source_relation_ambiguous";
                        transaction.Rollback();
                        return false;
                    }
                }

                using (var nextId = new SQLiteCommand(
                    "SELECT COALESCE(MAX(RELATION_ID),0)+1 FROM " +
                    pTableName, pDb, transaction))
                    pReplacementRelationId = Convert.ToInt64(
                        nextId.ExecuteScalar());

                using (var close = new SQLiteCommand(
                    "UPDATE " + pTableName +
                    " SET ACTIVE=0,END_TIME=@now," +
                    "END_REASON='internalized' WHERE RELATION_ID=@id" +
                    " AND ACTIVE=1", pDb, transaction))
                {
                    close.Parameters.AddWithValue("@now", pNow);
                    close.Parameters.AddWithValue("@id", pSourceRelationId);
                    if (close.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "source relation changed during conversion");
                }

                VassalEffectiveTerms terms =
                    VassalContractTierRules.TermsFor(pReplacementTier);
                using (var insert = new SQLiteCommand(
                    "INSERT INTO " + pTableName + "(" +
                    "RELATION_ID,VASSAL_ID,VASSAL_NAME,VASSAL_COLOR," +
                    "SUZERAIN_ID,SUZERAIN_NAME,SUZERAIN_COLOR," +
                    "RELATION_TYPE,AUTONOMY,TRIBUTE_RATE," +
                    "MILITARY_OBLIGATION,CONTRACT_TIER," +
                    "CREATED_BY_WAR_ID,START_TIME,END_TIME,ACTIVE," +
                    "ABSORBED,END_REASON) VALUES(" +
                    "@id,@vassal,@vassal_name,@vassal_color,@suzerain," +
                    "@suzerain_name,@suzerain_color,'internalized'," +
                    "@autonomy,@tribute,@military,@tier,@war,@now,-1,1," +
                    "0,'')", pDb, transaction))
                {
                    insert.Parameters.AddWithValue("@id",
                        pReplacementRelationId);
                    insert.Parameters.AddWithValue("@vassal", pVassalId);
                    insert.Parameters.AddWithValue("@vassal_name",
                        vassalName);
                    insert.Parameters.AddWithValue("@vassal_color",
                        vassalColor);
                    insert.Parameters.AddWithValue("@suzerain", pSuzerainId);
                    insert.Parameters.AddWithValue("@suzerain_name",
                        suzerainName);
                    insert.Parameters.AddWithValue("@suzerain_color",
                        suzerainColor);
                    insert.Parameters.AddWithValue("@autonomy",
                        terms.Autonomy);
                    insert.Parameters.AddWithValue("@tribute",
                        terms.TributeRate);
                    insert.Parameters.AddWithValue("@military",
                        terms.MilitaryObligation);
                    insert.Parameters.AddWithValue("@tier",
                        pReplacementTier);
                    insert.Parameters.AddWithValue("@war", pWarId);
                    insert.Parameters.AddWithValue("@now", pNow);
                    if (insert.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "replacement relation was not inserted");
                }
                transaction.Commit();
                pReason = "";
                return true;
            }
            catch
            {
                try { transaction?.Rollback(); }
                catch { }
                pSourceRelationId = -1L;
                pReplacementRelationId = -1L;
                pReason = "conversion_write_failed";
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static string ReadString(SQLiteDataReader pReader,
            int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : pReader.GetString(pIndex);
        }

        private static bool IsSafeIdentifier(string pValue)
        {
            if (string.IsNullOrEmpty(pValue)) return false;
            for (int index = 0; index < pValue.Length; index++)
            {
                char value = pValue[index];
                if (char.IsLetterOrDigit(value) || value == '_') continue;
                return false;
            }
            return true;
        }
    }
}
