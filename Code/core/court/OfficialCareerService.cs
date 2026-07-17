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
        private sealed class CareerRecord
        {
            public long OfficerId;
            public long KingdomId;
            public long ActorId;
            public long CityId;
            public string Layer = "";
            public string OfficeId = "";
            public int AppointedYear;
            public double AppointedTime;
            public int EndedYear;
            public double EndedTime;
            public bool Active;
            public string EndReason = "";
        }

        private sealed class PlaceName
        {
            public string Name = "";
            public string Color = "";
        }

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
                return OfficialCareerPersistence.Appoint(db, PrepareAppointment(pActor,
                    pKingdom, pLayer, pOfficeId, pSchoolId, pCity,
                    Date.getCurrentYear(), LineageService.CurTime()));
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning("Official career appointment failed: " + e.Message);
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.Unknown,
                    OfficialCareerMutation.Started);
            }
        }

        internal static OfficialCareerAppointment PrepareAppointment(Actor pActor,
            Kingdom pKingdom, string pLayer, string pOfficeId, string pSchoolId, City pCity,
            int pAppointedYear, double pAppointedTime)
        {
            if (pActor?.data == null || pKingdom?.data == null) return null;
            return new OfficialCareerAppointment
            {
                ActorId = pActor.data.id,
                ActorName = pActor.getName() ?? "",
                KingdomId = pKingdom.id,
                CityId = pCity?.data?.id ?? -1L,
                Layer = pLayer ?? "",
                OfficeId = pOfficeId ?? "",
                SchoolId = pSchoolId ?? "",
                Influence = CourtInfluenceRules.InfluenceWeight(pLayer,
                    ChronicleGate.IsImportant(pActor),
                    OfficialCareerStateService.ReadMeritFast(pActor),
                    OfficialCareerStateService.ReadRankFast(pActor)),
                AppointedYear = pAppointedYear,
                AppointedTime = pAppointedTime
            };
        }

        public static List<OfficialCareerReadModel> LoadCareer(long pActorId)
        {
            var result = new List<OfficialCareerReadModel>();
            if (pActorId < 0) return result;

            SQLiteConnection db = DB;
            if (db == null)
            {
                AncientWarfare3.ModClass.LogWarning(
                    "Official career load failed: archive database unavailable");
                return result;
            }

            try
            {
                List<CareerRecord> records = ReadCareerRecords(db, pActorId);
                var kingdoms = new Dictionary<long, PlaceName>();
                var cities = new Dictionary<long, string>();
                foreach (CareerRecord record in records)
                {
                    if (!kingdoms.TryGetValue(record.KingdomId, out PlaceName kingdom))
                    {
                        kingdom = ResolveKingdomName(db, record.KingdomId);
                        kingdoms[record.KingdomId] = kingdom;
                    }

                    string cityName = "";
                    if (record.CityId >= 0 && !cities.TryGetValue(record.CityId, out cityName))
                    {
                        cityName = ResolveCityName(db, record.CityId);
                        cities[record.CityId] = cityName;
                    }

                    result.Add(new OfficialCareerReadModel(record.OfficerId, record.KingdomId,
                        record.ActorId, record.CityId, record.Layer, record.OfficeId,
                        record.AppointedYear, record.AppointedTime, record.EndedYear,
                        record.EndedTime, record.Active, record.EndReason, kingdom.Name,
                        kingdom.Color, cityName));
                }
                return result;
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning(
                    "Official career load failed: " + e.Message);
                return new List<OfficialCareerReadModel>();
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

        private static List<CareerRecord> ReadCareerRecords(SQLiteConnection pDb,
            long pActorId)
        {
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,CITY_ID,LAYER," +
                "OFFICE_ID,APPOINTED_YEAR,APPOINTED_TIME,ENDED_YEAR,ENDED_TIME,ACTIVE," +
                "IFNULL(END_REASON,'') FROM " + CourtOfficerTableItem.GetTableName() +
                " WHERE ACTOR_ID=@actor " +
                "ORDER BY APPOINTED_TIME ASC, OFFICER_ID ASC";
            command.Parameters.AddWithValue("@actor", pActorId);

            var records = new List<CareerRecord>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(new CareerRecord
                {
                    OfficerId = ReadLong(reader, 0, -1L),
                    KingdomId = ReadLong(reader, 1, -1L),
                    ActorId = ReadLong(reader, 2, -1L),
                    CityId = ReadLong(reader, 3, -1L),
                    Layer = ReadText(reader, 4),
                    OfficeId = ReadText(reader, 5),
                    AppointedYear = ReadInt(reader, 6, -1),
                    AppointedTime = ReadDouble(reader, 7, -1d),
                    EndedYear = ReadInt(reader, 8, -1),
                    EndedTime = ReadDouble(reader, 9, -1d),
                    Active = ReadInt(reader, 10, 0) == 1,
                    EndReason = ReadText(reader, 11)
                });
            }
            return records;
        }

        private static PlaceName ResolveKingdomName(SQLiteConnection pDb, long pKingdomId)
        {
            if (pKingdomId < 0) return new PlaceName();
            try
            {
                Kingdom live = World.world?.kingdoms?.get(pKingdomId);
                if (live != null && !live.isRekt())
                {
                    return new PlaceName
                    {
                        Name = live.name ?? "",
                        Color = HistoryColors.FromKingdom(live)
                    };
                }
            }
            catch { }

            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "SELECT IFNULL(KINGDOM_NAME,'')," +
                    "IFNULL(COLOR_TEXT,'') FROM " + KingdomArchiveTableItem.GetTableName() +
                    " WHERE KINGDOM_ID=@kingdom LIMIT 1";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return new PlaceName();
                return new PlaceName
                {
                    Name = ReadText(reader, 0),
                    Color = HistoryColors.Normalize(ReadText(reader, 1))
                };
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning(
                    "Official career kingdom name lookup failed: " + e.Message);
                return new PlaceName();
            }
        }

        private static string ResolveCityName(SQLiteConnection pDb, long pCityId)
        {
            if (pCityId < 0) return "";
            try
            {
                City live = World.world?.cities?.get(pCityId);
                if (live?.data != null && live.isAlive()) return live.data.name ?? "";
            }
            catch { }

            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "SELECT IFNULL(SUBJECT_NAME,'') FROM " +
                    CityHistoryTableItem.GetTableName() +
                    " WHERE CITY_ID=@city ORDER BY WORLD_TIME ASC, EVENT_ID ASC LIMIT 1";
                command.Parameters.AddWithValue("@city", pCityId);
                object value = command.ExecuteScalar();
                string name = value == null || value == DBNull.Value ? "" : value.ToString();
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning(
                    "Official career city history lookup failed: " + e.Message);
            }

            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "SELECT IFNULL(CITY_NAME,'') FROM " +
                    ActorArchiveTableItem.GetTableName() +
                    " WHERE CITY_ID=@city AND IFNULL(CITY_NAME,'')<>'' " +
                    "ORDER BY IS_ALIVE DESC, BIRTH_TIME ASC, ID ASC LIMIT 1";
                command.Parameters.AddWithValue("@city", pCityId);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? "" : value.ToString();
            }
            catch (Exception e)
            {
                AncientWarfare3.ModClass.LogWarning(
                    "Official career city archive lookup failed: " + e.Message);
                return "";
            }
        }

        private static long ReadLong(SQLiteDataReader pReader, int pOrdinal, long pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToInt64(pReader.GetValue(pOrdinal));
        }

        private static int ReadInt(SQLiteDataReader pReader, int pOrdinal, int pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToInt32(pReader.GetValue(pOrdinal));
        }

        private static double ReadDouble(SQLiteDataReader pReader, int pOrdinal,
            double pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToDouble(pReader.GetValue(pOrdinal));
        }

        private static string ReadText(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal)
                ? ""
                : pReader.GetValue(pOrdinal)?.ToString() ?? "";
        }
    }
}
