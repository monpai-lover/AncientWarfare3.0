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
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;

        public static OfficialCareerAppointmentResult Appoint(Actor pActor, Kingdom pKingdom, string pLayer,
            string pOfficeId, string pSchoolId, City pCity)
        {
            SQLiteConnection db = DB;
            if (db == null || pActor?.data == null || pKingdom?.data == null)
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.CleanFailure,
                    OfficialCareerMutation.Started);

            try
            {
                return OfficialCareerPersistence.Appoint(db, new OfficialCareerAppointment
                {
                    ActorId = pActor.data.id,
                    ActorName = pActor.getName() ?? "",
                    KingdomId = pKingdom.id,
                    CityId = pCity?.data?.id ?? -1L,
                    Layer = pLayer ?? "",
                    OfficeId = pOfficeId ?? "",
                    SchoolId = pSchoolId ?? "",
                    Influence = CourtInfluenceRules.InfluenceWeight(pLayer,
                        ChronicleGate.IsImportant(pActor), GeneralService.GetMerit(pActor)),
                    AppointedYear = Date.getCurrentYear(),
                    AppointedTime = LineageService.CurTime()
                });
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning("Official career appointment failed: " + e.Message);
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.Unknown,
                    OfficialCareerMutation.Started);
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

        private static bool HasActive(SQLiteConnection pDb, string pTable,
            List<SimpleColumnConstraint> pConstraints)
        {
            return pDb.CheckKeyExist(pTable, pConstraints.ToArray());
        }
    }
}
