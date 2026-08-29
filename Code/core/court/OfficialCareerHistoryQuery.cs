using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.court
{
    public static class OfficialCareerHistoryQuery
    {
        private const int MaximumRows = 128;

        public static IReadOnlyList<OfficialCareerHistoryRow> Read(
            SQLiteConnection pDb, OfficialCareerHistoryScope pScope,
            int limit = 64)
        {
            int boundedLimit = Math.Min(MaximumRows, Math.Max(0, limit));
            var rows = new List<OfficialCareerHistoryRow>(boundedLimit);
            if (pDb == null || !pScope.IsValid || boundedLimit == 0)
                return rows;

            string cityPredicate = pScope.HasCity
                ? " AND (CITY_ID=@city OR CITY_ID<0)"
                : "";
            string countyPredicate = pScope.HasCounty
                ? " AND IFNULL(COUNTY_ID,-1)=@county"
                : "";
            using var command = new SQLiteCommand(
                "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,CITY_ID," +
                "IFNULL(COUNTY_ID,-1),IFNULL(LAYER,'')," +
                "IFNULL(OFFICE_ID,'')," +
                "IFNULL(ACTOR_NAME,''),APPOINTED_YEAR,ENDED_YEAR,ACTIVE," +
                "IFNULL(END_REASON,''),APPOINTED_TIME," +
                "IFNULL(RANK_AT_APPOINTMENT,0)," +
                "IFNULL(LOCAL_GRADE_AT_APPOINTMENT,0) FROM " +
                CourtOfficerTableItem.GetTableName() + " WHERE " +
                "KINGDOM_ID=@kingdom AND LAYER=@layer AND " +
                "OFFICE_ID=@office" + cityPredicate + countyPredicate +
                " ORDER BY " +
                "APPOINTED_YEAR DESC,APPOINTED_TIME DESC," +
                "OFFICER_ID DESC LIMIT @limit", pDb);
            command.Parameters.AddWithValue("@kingdom", pScope.KingdomId);
            command.Parameters.AddWithValue("@layer", pScope.Layer);
            command.Parameters.AddWithValue("@office", pScope.OfficeId);
            if (pScope.HasCity)
                command.Parameters.AddWithValue("@city", pScope.CityId);
            if (pScope.HasCounty)
                command.Parameters.AddWithValue("@county", pScope.CountyId);
            command.Parameters.AddWithValue("@limit", boundedLimit);

            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new OfficialCareerHistoryRow(
                    kingdomId: ReadLong(reader, 1),
                    officerId: ReadLong(reader, 0),
                    actorId: ReadLong(reader, 2),
                    cityId: ReadLong(reader, 3),
                    layer: ReadString(reader, 5),
                    officeId: ReadString(reader, 6),
                    actorName: ReadString(reader, 7),
                    startYear: ReadInt(reader, 8, -1),
                    endYear: ReadInt(reader, 9, -1),
                    isCurrent: ReadInt(reader, 10, 0) == 1,
                    endReason: ReadString(reader, 11),
                    appointedTime: ReadDouble(reader, 12, -1d),
                    rankId: ReadInt(reader, 13, 0).ToString(),
                    grade: ReadInt(reader, 14, 0),
                    countyId: ReadLong(reader, 4)));
            }
            return rows;
        }

        private static long ReadLong(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal)
                ? -1L
                : Convert.ToInt64(pReader.GetValue(pOrdinal));
        }

        private static int ReadInt(SQLiteDataReader pReader, int pOrdinal,
            int pFallback)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pFallback
                : Convert.ToInt32(pReader.GetValue(pOrdinal));
        }

        private static double ReadDouble(SQLiteDataReader pReader,
            int pOrdinal, double pFallback)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pFallback
                : Convert.ToDouble(pReader.GetValue(pOrdinal));
        }

        private static string ReadString(SQLiteDataReader pReader,
            int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal)
                ? ""
                : Convert.ToString(pReader.GetValue(pOrdinal)) ?? "";
        }
    }
}
