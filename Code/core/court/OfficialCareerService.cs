using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.court
{
    internal static class OfficialCareerService
    {
        private sealed class ActiveCareer
        {
            public long KingdomId;
            public long CityId;
            public string Layer = "";
            public string OfficeId = "";
        }

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;

        public static bool Appoint(Actor pActor, Kingdom pKingdom, string pLayer,
            string pOfficeId, string pSchoolId, City pCity)
        {
            SQLiteConnection db = DB;
            if (db == null || pActor?.data == null || pKingdom?.data == null) return false;

            try
            {
                string table = CourtOfficerTableItem.GetTableName();
                ActiveCareer active = ReadActive(db, table, pActor.data.id, pLayer);
                long cityId = pCity?.data?.id ?? -1L;
                bool insert = CourtOfficerRecordRules.ShouldInsertNewActiveRecord(
                    active != null,
                    active != null && active.KingdomId == pKingdom.id,
                    active != null && active.OfficeId == (pOfficeId ?? ""),
                    active != null && active.Layer == (pLayer ?? ""),
                    active != null && active.CityId == cityId);

                if (!insert)
                {
                    UpdateActiveSnapshot(db, table, pActor, pSchoolId, pLayer);
                    return false;
                }

                if (active != null) EndTrack(pActor.data.id, pLayer, "reassigned");

                double now = LineageService.CurTime();
                long id = TableIdAllocator.Next(db, table, "OFFICER_ID");
                float influence = CourtInfluenceRules.InfluenceWeight(pLayer,
                    ChronicleGate.IsImportant(pActor), GeneralService.GetMerit(pActor));
                db.Insert(table,
                    ColumnVal.Create("OFFICER_ID", id),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("ACTOR_ID", pActor.data.id),
                    ColumnVal.Create("ACTOR_NAME", pActor.getName() ?? ""),
                    ColumnVal.Create("CITY_ID", cityId),
                    ColumnVal.Create("LAYER", pLayer ?? ""),
                    ColumnVal.Create("OFFICE_ID", pOfficeId ?? ""),
                    ColumnVal.Create("SCHOOL_ID", pSchoolId ?? ""),
                    ColumnVal.Create("INFLUENCE", (double)influence),
                    ColumnVal.Create("APPOINTED_YEAR", Date.getCurrentYear()),
                    ColumnVal.Create("APPOINTED_TIME", now),
                    ColumnVal.Create("ENDED_YEAR", -1),
                    ColumnVal.Create("ENDED_TIME", -1d),
                    ColumnVal.Create("ACTIVE", CourtOfficerRecordRules.ActiveFlag(true)),
                    ColumnVal.Create("END_REASON", ""),
                    ColumnVal.Create("UPDATED_TIME", now));
                return true;
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning("Official career appointment failed: " + e.Message);
                return false;
            }
        }

        public static bool End(Actor pActor, string pReason)
        {
            return pActor?.data != null && End(pActor.data.id, pReason);
        }

        public static bool End(Actor pActor, string pLayer, string pOfficeId, string pReason)
        {
            if (pActor?.data == null) return false;
            return EndMatching(pActor.data.id, pLayer, pOfficeId, null, pReason);
        }

        public static bool End(long pActorId, string pReason)
        {
            return EndMatching(pActorId, null, null, null, pReason);
        }

        public static bool EndForKingdom(long pActorId, long pKingdomId, string pReason)
        {
            return EndMatching(pActorId, null, null, pKingdomId, pReason);
        }

        private static bool EndTrack(long pActorId, string pLayer, string pReason)
        {
            return EndMatching(pActorId, pLayer, null, null, pReason);
        }

        private static bool EndMatching(long pActorId, string pLayer, string pOfficeId,
            long? pKingdomId, string pReason)
        {
            SQLiteConnection db = DB;
            if (db == null || pActorId < 0) return false;
            try
            {
                string table = CourtOfficerTableItem.GetTableName();
                var constraints = new List<SimpleColumnConstraint>
                {
                    SimpleColumnConstraint.CreateEq("ACTOR_ID", pActorId),
                    SimpleColumnConstraint.CreateEq("ACTIVE", CourtOfficerRecordRules.ActiveFlag(true))
                };
                if (pLayer != null) constraints.Add(SimpleColumnConstraint.CreateEq("LAYER", pLayer));
                if (pOfficeId != null) constraints.Add(SimpleColumnConstraint.CreateEq("OFFICE_ID", pOfficeId));
                if (pKingdomId.HasValue)
                    constraints.Add(SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdomId.Value));
                if (!HasActive(db, table, constraints)) return false;

                double now = LineageService.CurTime();
                db.UpdateValue(table, constraints,
                    ColumnVal.Create("ACTIVE", CourtOfficerRecordRules.ActiveFlag(false)),
                    ColumnVal.Create("ENDED_YEAR", Date.getCurrentYear()),
                    ColumnVal.Create("ENDED_TIME", now),
                    ColumnVal.Create("END_REASON", pReason ?? ""),
                    ColumnVal.Create("UPDATED_TIME", now));
                return true;
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning("Official career close failed: " + e.Message);
                return false;
            }
        }

        private static ActiveCareer ReadActive(SQLiteConnection pDb, string pTable, long pActorId,
            string pLayer)
        {
            using var cmd = new SQLiteCommand(pDb);
            cmd.CommandText = "SELECT KINGDOM_ID, CITY_ID, LAYER, OFFICE_ID FROM " + pTable +
                              " WHERE ACTOR_ID=@aid AND ACTIVE=1 AND LAYER=@layer" +
                              " ORDER BY APPOINTED_TIME DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@aid", pActorId);
            cmd.Parameters.AddWithValue("@layer", pLayer ?? "");
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return new ActiveCareer
            {
                KingdomId = reader.IsDBNull(0) ? -1L : Convert.ToInt64(reader.GetValue(0)),
                CityId = reader.IsDBNull(1) ? -1L : Convert.ToInt64(reader.GetValue(1)),
                Layer = reader.IsDBNull(2) ? "" : reader.GetValue(2)?.ToString() ?? "",
                OfficeId = reader.IsDBNull(3) ? "" : reader.GetValue(3)?.ToString() ?? ""
            };
        }

        private static void UpdateActiveSnapshot(SQLiteConnection pDb, string pTable, Actor pActor,
            string pSchoolId, string pLayer)
        {
            float influence = CourtInfluenceRules.InfluenceWeight(pLayer,
                ChronicleGate.IsImportant(pActor), GeneralService.GetMerit(pActor));
            pDb.UpdateValue(pTable,
                new List<SimpleColumnConstraint>
                {
                    SimpleColumnConstraint.CreateEq("ACTOR_ID", pActor.data.id),
                    SimpleColumnConstraint.CreateEq("ACTIVE", CourtOfficerRecordRules.ActiveFlag(true)),
                    SimpleColumnConstraint.CreateEq("LAYER", pLayer ?? "")
                },
                ColumnVal.Create("ACTOR_NAME", pActor.getName() ?? ""),
                ColumnVal.Create("SCHOOL_ID", pSchoolId ?? ""),
                ColumnVal.Create("INFLUENCE", (double)influence),
                ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
        }

        private static bool HasActive(SQLiteConnection pDb, string pTable,
            List<SimpleColumnConstraint> pConstraints)
        {
            return pDb.CheckKeyExist(pTable, pConstraints.ToArray());
        }
    }
}
