using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.court;

internal static class LocalGovernmentWaitingPoolSqlTests
{
    public static void Run()
    {
        LocalPoolIncludesLowerQualifiedAndHigherStageFailuresOnly();
    }

    private static void LocalPoolIncludesLowerQualifiedAndHigherStageFailuresOnly()
    {
        using var db = new SQLiteConnection("Data Source=:memory:");
        db.Open();
        Execute(db, "CREATE TABLE Candidate (ID INTEGER PRIMARY KEY," +
                    "SESSION_ID INTEGER,KINGDOM_ID INTEGER,ACTOR_ID INTEGER," +
                    "QUALIFICATION TEXT,METROPOLITAN_RESULT TEXT," +
                    "PALACE_RESULT TEXT,NATIONAL_RESULT TEXT)");
        Execute(db, "CREATE TABLE Session (ID INTEGER PRIMARY KEY," +
                    "KINGDOM_ID INTEGER,STATUS TEXT,CYCLE_YEAR INTEGER)");
        Execute(db, "CREATE TABLE Archive (ID INTEGER PRIMARY KEY," +
                    "KINGDOM_ID INTEGER,IS_ALIVE INTEGER,SEX INTEGER," +
                    "STATUS TEXT)");
        Execute(db, "CREATE TABLE Officer (ACTOR_ID INTEGER,ACTIVE INTEGER)");
        Execute(db, "CREATE TABLE Affiliation (ACTOR_ID INTEGER," +
                    "HOME_KINGDOM_ID INTEGER,RESIDENCE_CITY_ID INTEGER," +
                    "LIFECYCLE_STATE TEXT,SERVICE_KINGDOM_ID INTEGER)");
        Execute(db, "INSERT INTO Session VALUES (1,7,'completed',100)");
        for (long actor = 1; actor <= 5; actor++)
        {
            Execute(db, "INSERT INTO Archive VALUES (" + actor +
                        ",7,1,0,'')");
        }
        InsertCandidate(db, 1, "juren", "passed", "pending", "pending");
        InsertCandidate(db, 2, "gongshi", "passed", "pending", "pending");
        InsertCandidate(db, 3, "none", "failed", "pending", "pending");
        InsertCandidate(db, 4, "none", "pending", "pending", "pending");
        InsertCandidate(db, 5, "jinshi", "passed", "passed", "pending");
        Execute(db, "INSERT INTO Officer VALUES (5,1)");

        True(CivilServiceWaitingPoolQuery.TryLoadLocalActorIds(db,
            "Candidate", "Session", "Archive", "Officer", "Affiliation",
            7, Array.Empty<long>(), 16, out IReadOnlyList<long> actorIds),
            "local waiting-pool query succeeds");
        SequenceEqual(new long[] { 1, 2, 3 }, actorIds,
            "local pool includes juren and higher-stage failures only");
    }

    private static void InsertCandidate(SQLiteConnection pDb, long pActorId,
        string pQualification, string pMetro, string pPalace,
        string pNational)
    {
        Execute(pDb, "INSERT INTO Candidate VALUES (" + pActorId +
                     ",1,7," + pActorId + ",'" + pQualification + "','" +
                     pMetro + "','" + pPalace + "','" + pNational + "')");
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

    private static void SequenceEqual(IReadOnlyList<long> pExpected,
        IReadOnlyList<long> pActual, string pMessage)
    {
        if (pActual == null || pExpected.Count != pActual.Count)
            throw new InvalidOperationException(pMessage);
        for (int index = 0; index < pExpected.Count; index++)
            if (pExpected[index] != pActual[index])
                throw new InvalidOperationException(pMessage);
    }
}
