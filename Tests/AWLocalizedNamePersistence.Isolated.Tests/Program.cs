using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;

internal static class Program
{
    private static int Main()
    {
        try
        {
            InsertAndUpdatePreserveIdentity();
            FailedInsertRollsBackTriggerWrites();
            FailedUpdatePreservesIdentityAndRollsBackTriggerWrites();
            RejectUnsupportedSqlSyntax();
            AncientWarfare3.Tests.IntegrationSourceTests.Run();
            NamingContextTests.Run();
            Console.WriteLine("Localized-name SQL, runtime context, and integration source tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { LineageArchiveManager.Instance.OperatingDB = null; }
    }

    private static void InsertAndUpdatePreserveIdentity()
    {
        using SQLiteConnection db = Open();
        True(AWLocalizedNamePersistence.Upsert(" unit ", 7, Identity("Original")),
            "new identity inserts with normalized meta type");
        long rowId = Scalar(db, "SELECT rowid FROM LocalizedNameIdentity");
        Execute(db, "CREATE TABLE Dependent (META_TYPE TEXT, OBJECT_ID INTEGER, " +
            "FOREIGN KEY(META_TYPE,OBJECT_ID) REFERENCES LocalizedNameIdentity(META_TYPE,OBJECT_ID) ON DELETE CASCADE)");
        Execute(db, "INSERT INTO Dependent VALUES ('Unit',7)");
        True(AWLocalizedNamePersistence.Upsert("Unit", 7, Identity("O'Brien 名")),
            "existing identity updates with parameterized text");
        Equal(rowId, Scalar(db, "SELECT rowid FROM LocalizedNameIdentity"), "update preserves rowid");
        Equal(1L, Scalar(db, "SELECT COUNT(*) FROM Dependent"), "update preserves dependent rows");
        True(AWLocalizedNamePersistence.TryLoad("Unit", 7, out var loaded), "updated identity loads");
        Equal("O'Brien 名", loaded.NativeName, "updated payload round trips");
        Equal("中文", loaded.ChineseName, "Chinese name round trips");
        Equal("Given", loaded.GivenName, "given name round trips");
        Equal("Family", loaded.FamilyComponent, "family component round trips");
        Equal("generator", loaded.GeneratorId, "generator id round trips");
        Equal(42L, loaded.CultureId, "culture id round trips");
        Equal(3, loaded.SchemaVersion, "schema version round trips");
        True(AWLocalizedNamePersistence.Upsert("Unit", 7, Identity("O'Brien 名")), "identical write remains successful");
        True(AWLocalizedNamePersistence.Upsert("City", 7, Identity("City")), "same object id in another category stays distinct");
        Equal(2L, Scalar(db, "SELECT COUNT(*) FROM LocalizedNameIdentity"), "one row per composite identity");
        True(!AWLocalizedNamePersistence.Upsert("unknown", 7, Identity("Bad")), "unsupported category is rejected");
        True(!AWLocalizedNamePersistence.Upsert("Unit", -1, Identity("Bad")), "negative identity is rejected");
        Equal(2L, Scalar(db, "SELECT COUNT(*) FROM LocalizedNameIdentity"), "invalid input cannot add rows");
    }

    private static void FailedInsertRollsBackTriggerWrites()
    {
        using SQLiteConnection db = Open();
        Execute(db, "CREATE TABLE Audit (VALUE TEXT)");
        Execute(db, "CREATE TRIGGER RejectInsert BEFORE INSERT ON LocalizedNameIdentity BEGIN " +
            "INSERT INTO Audit VALUES ('attempt'); SELECT RAISE(FAIL,'insert rejected'); END");
        True(!AWLocalizedNamePersistence.Upsert("Unit", 7, Identity("Rejected")), "failed insert reports failure");
        Equal(0L, Scalar(db, "SELECT COUNT(*) FROM LocalizedNameIdentity"), "failed insert leaves no identity");
        Equal(0L, Scalar(db, "SELECT COUNT(*) FROM Audit"), "failed insert rolls back trigger side effects");
        Execute(db, "DROP TRIGGER RejectInsert");
        True(AWLocalizedNamePersistence.Upsert("Unit", 7, Identity("Recovered")), "failed insert releases its transaction");
    }

    private static void FailedUpdatePreservesIdentityAndRollsBackTriggerWrites()
    {
        using SQLiteConnection db = Open();
        True(AWLocalizedNamePersistence.Upsert("Unit", 7, Identity("Original")), "seed identity inserts");
        long rowId = Scalar(db, "SELECT rowid FROM LocalizedNameIdentity");
        Execute(db, "CREATE TABLE Audit (VALUE TEXT)");
        Execute(db, "CREATE TRIGGER RejectUpdate BEFORE UPDATE ON LocalizedNameIdentity BEGIN " +
            "INSERT INTO Audit VALUES ('attempt'); SELECT RAISE(FAIL,'update rejected'); END");
        True(!AWLocalizedNamePersistence.Upsert("Unit", 7, Identity("Rejected")), "failed update reports failure");
        True(AWLocalizedNamePersistence.TryLoad("Unit", 7, out var loaded), "prior identity remains readable");
        Equal("Original", loaded.NativeName, "failed update preserves previous data");
        Equal(rowId, Scalar(db, "SELECT rowid FROM LocalizedNameIdentity"), "failed update preserves rowid");
        Equal(0L, Scalar(db, "SELECT COUNT(*) FROM Audit"), "failed update rolls back trigger side effects");
        Execute(db, "DROP TRIGGER RejectUpdate");
        True(AWLocalizedNamePersistence.Upsert("Unit", 7, Identity("Recovered")), "failed update releases its transaction");
    }

    private static void RejectUnsupportedSqlSyntax()
    {
        string source = File.ReadAllText(Path.Combine(FindRepository(), "Code", "core", "naming", "AWLocalizedNamePersistence.cs"));
        True(!source.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase), "bundled SQLite 3.9 cannot execute modern UPSERT syntax");
        True(!source.Contains("OR REPLACE", StringComparison.OrdinalIgnoreCase), "identity updates must not delete and replace dependent rows");
    }

    private static string FindRepository()
    {
        for (DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "Code"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static SQLiteConnection Open()
    {
        var db = new SQLiteConnection("Data Source=:memory:;Version=3;");
        db.Open();
        Execute(db, "PRAGMA foreign_keys=ON");
        LocalizedNameIdentitySchema.Ensure(db);
        LineageArchiveManager.Instance.OperatingDB = db;
        return db;
    }

    private static AWLocalizedNameIdentitySnapshot Identity(string native) =>
        new AWLocalizedNameIdentitySnapshot(native, "中文", "Given", "Family", "generator", 42, 3);
    private static void Execute(SQLiteConnection db, string sql)
    {
        using var command = new SQLiteCommand(sql, db);
        command.ExecuteNonQuery();
    }
    private static long Scalar(SQLiteConnection db, string sql)
    {
        using var command = new SQLiteCommand(sql, db);
        return Convert.ToInt64(command.ExecuteScalar());
    }
    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}
