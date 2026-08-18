using System.Data.SQLite;
using AncientWarfare3.core.court;

internal static class OfficialCareerHistorySqlTests
{
    internal static void Run()
    {
        using var db = new SQLiteConnection(
            "Data Source=:memory:;Version=3;New=True;");
        db.Open();
        Execute(db, "CREATE TABLE CourtOfficer (" +
            "OFFICER_ID INTEGER PRIMARY KEY,KINGDOM_ID INTEGER," +
            "ACTOR_ID INTEGER,ACTOR_NAME TEXT,CITY_ID INTEGER," +
            "LAYER TEXT,OFFICE_ID TEXT,SCHOOL_ID TEXT,INFLUENCE REAL," +
            "APPOINTED_YEAR INTEGER,APPOINTED_TIME REAL," +
            "INSTITUTION_AT_APPOINTMENT TEXT," +
            "RANK_AT_APPOINTMENT INTEGER," +
            "LOCAL_GRADE_AT_APPOINTMENT INTEGER,IS_ACTING INTEGER," +
            "ENDED_YEAR INTEGER,ENDED_TIME REAL,ACTIVE INTEGER," +
            "END_REASON TEXT,UPDATED_TIME REAL)");
        Insert(db, 1, 7, 101, "前任甲", 3, "city",
            "granary_officer", 110, 6600d, 118, 0, "term_expired");
        Insert(db, 2, 7, 102, "前任乙", 3, "city",
            "granary_officer", 119, 7140d, 129, 0, "transferred");
        Insert(db, 3, 7, 91, "张三", 3, "city",
            "granary_officer", 130, 7800d, -1, 1, "");
        Insert(db, 4, 7, 99, "无关官员", 3, "city",
            "constable", 131, 7860d, -1, 1, "");

        IReadOnlyList<OfficialCareerHistoryRow> rows =
            OfficialCareerHistoryQuery.Read(db,
                new OfficialCareerHistoryScope(7, 3, "city",
                    "granary_officer"), limit: 32);

        Equal(3, rows.Count, "all terms for one city office");
        Equal("张三", rows[0].ActorName,
            "stored name snapshot survives without a live actor");
        True(rows[0].IsCurrent, "current row is retained");
        Equal(2L, rows[1].OfficerId,
            "history is ordered by appointment identity descending");

        Console.WriteLine("Office history SQL tests passed.");
    }

    private static void Insert(SQLiteConnection pDb, long pOfficerId,
        long pKingdomId, long pActorId, string pActorName, long pCityId,
        string pLayer, string pOfficeId, int pStartYear, double pStartTime,
        int pEndYear, int pActive, string pEndReason)
    {
        using var command = new SQLiteCommand(
            "INSERT INTO CourtOfficer VALUES (@id,@kingdom,@actor,@name," +
            "@city,@layer,@office,'ru',0,@startYear,@startTime,'zhou'," +
            "6,5,0,@endYear,-1,@active,@reason,@startTime)", pDb);
        command.Parameters.AddWithValue("@id", pOfficerId);
        command.Parameters.AddWithValue("@kingdom", pKingdomId);
        command.Parameters.AddWithValue("@actor", pActorId);
        command.Parameters.AddWithValue("@name", pActorName);
        command.Parameters.AddWithValue("@city", pCityId);
        command.Parameters.AddWithValue("@layer", pLayer);
        command.Parameters.AddWithValue("@office", pOfficeId);
        command.Parameters.AddWithValue("@startYear", pStartYear);
        command.Parameters.AddWithValue("@startTime", pStartTime);
        command.Parameters.AddWithValue("@endYear", pEndYear);
        command.Parameters.AddWithValue("@active", pActive);
        command.Parameters.AddWithValue("@reason", pEndReason);
        command.ExecuteNonQuery();
    }

    private static void Execute(SQLiteConnection pDb, string pSql)
    {
        using var command = new SQLiteCommand(pSql, pDb);
        command.ExecuteNonQuery();
    }

    private static void True(bool pValue, string pMessage)
    {
        if (!pValue) throw new InvalidOperationException(pMessage);
    }

    private static void Equal<T>(T pExpected, T pActual, string pMessage)
    {
        if (!EqualityComparer<T>.Default.Equals(pExpected, pActual))
            throw new InvalidOperationException(
                $"{pMessage}: expected {pExpected}, got {pActual}");
    }
}
