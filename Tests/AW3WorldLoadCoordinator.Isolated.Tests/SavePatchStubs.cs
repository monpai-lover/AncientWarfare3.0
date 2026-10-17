public sealed class SavedMap { }
public sealed class SaveManager
{
    public void saveWorldToDirectory() { }
    public void loadWorld(string path, bool allowFallback) { }
    public void loadData(SavedMap map, string path) { }
}

public sealed class TestBuildingManager
{
    public readonly List<Building> Items = new();
    public List<Building> getSimpleList() => Items;
}
public sealed class Building
{
    public TestEntityData data = new();
    public Kingdom kingdom;
    public bool Removed;
    public bool isRemoved() => Removed;
    public bool isOnRemove() => false;
    public void removeBuildingFinal() => Removed = true;
}
public sealed class TestEntityData { public long id; }
public sealed class Kingdom
{
    public TestEntityData data = new();
    public KingdomAsset asset;
    public ActorAsset Founder;
    public readonly List<Actor> units = new();
    public ActorAsset getActorAsset() => Founder;
}
public sealed class Actor
{
    public TestEntityData data = new();
    public ActorAsset asset;
    public bool isAlive() => true;
}
public sealed class ActorAsset
{
    public string kingdom_id_wild;
    public string kingdom_id_civilization;
}
public sealed class KingdomAsset { }
public static class AssetManager
{
    public static readonly TestKingdomAssets kingdoms = new();
}
public sealed class TestKingdomAssets
{
    public readonly Dictionary<string, KingdomAsset> Items = new();
    public bool has(string id) => Items.ContainsKey(id);
    public KingdomAsset get(string id) => Items[id];
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(params object[] arguments) { }
    }
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
    public sealed class HarmonyFinalizer : Attribute { }
}

namespace AncientWarfare3.content { internal sealed class UnusedContent { } }
namespace AncientWarfare3.ui.windows
{
    internal static class HistoricalFigureDrawWindow
    {
        public static int ResetCalls;
        public static void ResetTransientState() => ResetCalls++;
    }
}
namespace AncientWarfare3.core.policy { internal sealed class UnusedPolicy { } }
namespace AncientWarfare3.core.presentation { internal sealed class UnusedPresentation { } }

namespace AncientWarfare3.core.asyncwork
{
    internal static partial class AWAsyncWorldLifecycle
    {
        public static bool SaveBarrierActive;
        public static int SaveBarrierExitCalls;
        public static bool TryEnterSaveBarrier(TimeSpan timeout, Action pending, out string error)
        {
            error = SaveBarrierActive ? "async save barrier is already active" : string.Empty;
            if (SaveBarrierActive) return false;
            SaveBarrierActive = true;
            return true;
        }
        public static void ExitSaveBarrier()
        {
            SaveBarrierExitCalls++;
            SaveBarrierActive = false;
        }
    }
}

namespace AncientWarfare3.patch
{
    internal static class AW_XiaWatchTowerKingdomPatch
    {
        public static Action RestoreAction;
        public static void RestoreCityWatchTowerKingdoms() => RestoreAction?.Invoke();
    }
    internal static class AW_FramePrioritySchedulerPatch
    {
        public static void DrainSimulationToSaveBoundary() { }
    }
}

namespace AncientWarfare3.core.db
{
    internal sealed class LineageArchiveManager
    {
        public static readonly LineageArchiveManager Instance = new();
        public object OperatingDB => this;
        public bool TryExportLineageArchive(string folder, out string error)
        { error = string.Empty; return true; }
    }
    internal static class LineageArchivePragmaService
    {
        public static bool CheckpointForSave(object database) => true;
    }
    internal static class HistoricalWriteService
    {
        public static bool FlushForSave(TimeSpan timeout, out string error)
        { error = string.Empty; return true; }
    }
}

namespace AncientWarfare3.core.lineage
{
    internal static class DeferredRuntimeWorkService
    {
        public static Exception FlushError;
        public static void FlushPersistent()
        { if (FlushError != null) throw FlushError; }
    }
    internal static class NobleRankService
    { public static bool FlushPendingDeathSuccessionsForSave() => true; }
    internal static class ActorDeathArchiveService
    {
        public static int PendingCount => 0;
        public static bool FlushForSave(TimeSpan timeout, out string error)
        { error = string.Empty; return true; }
    }
    internal static class ActorDeathArchiveRules
    { public static int ResolveSaveTimeoutSeconds(int seconds, int count) => seconds; }
    internal static class SyntheticMobilizationLedgerService
    {
        public static bool TryWriteSnapshot(string folder, out string error)
        { error = string.Empty; return true; }
    }
    internal static class ArmyRtsPlanSnapshotService
    {
        public static void PublishToSave(string folder) { }
        public static void ObserveLoadDirectory(string folder) { }
        public static void OnNewWorldGenerated() { }
    }
}

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolDescentService
    { public static bool FlushPendingDescentsForSave() => true; }
    internal static class SchoolMembershipService
    { public static bool FlushDeathRetriesForSave() => true; }
    internal static class HistoricalSchoolWriteBufferService
    {
        public static bool FlushForSave() => true;
        public static int Count => 0;
    }
    internal static class HistoricalSchoolActivityQueue
    { public static bool FlushPendingPersistenceForSave() => true; }
    internal static class HistoricalSchoolRuntime
    { public static bool FlushPendingStateForSave() => true; }
}

namespace AncientWarfare3.core.multiplayer
{
    internal static class AW3SaveDirectoryRegistry
    {
        public static void Observe(string folder) { }
        public static void ClearForNewWorld() { }
    }
}

namespace AncientWarfare3.core.court
{
    internal static class SocialStandingService
    {
        public static int ClearCalls;
        public static void ClearRuntime() => ClearCalls++;
    }
    internal static partial class DeJureRegionStore
    {
        public static Exception PublishError;
        public static void PublishToSave(string folder)
        { if (PublishError != null) throw PublishError; }
        public static void ObserveLoadDirectory(string folder) { }
        public static void ClearForNewWorld() { }
    }
}

namespace AncientWarfare3.core.county
{
    internal static partial class CountyAdministrationStore
    {
        public static void PublishToSave(string folder) { }
        public static void ObserveLoadDirectory(string folder) { }
        public static void ClearForNewWorld() { }
    }
}
