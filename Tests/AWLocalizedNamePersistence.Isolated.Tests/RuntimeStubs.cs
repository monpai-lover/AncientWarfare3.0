using System.Data.SQLite;

internal sealed partial class BaseSystemData
{
    internal string name = "";
    internal void get<T>(string key, out T value, T fallback) => value = fallback;
    internal void set<T>(string key, T value) { }
}

internal sealed partial class World
{
    internal static World world = new World();
    internal double getCurWorldTime() => 123.5d;
}

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameService
    {
        internal static void ProjectStored(BaseSystemData data) { }
    }
}

namespace AncientWarfare3.core.db
{
    internal static class LocalizedNameIdentityTableItem
    {
        internal static string GetTableName() => LocalizedNameIdentitySchema.TableName;
    }

    internal sealed class LineageArchiveManager
    {
        internal static LineageArchiveManager Instance = new LineageArchiveManager();
        internal bool IsOperational => OperatingDB != null;
        internal SQLiteConnection OperatingDB;
    }
}

namespace AncientWarfare3.Tests
{
    internal static class AssertEx
    {
        internal static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
