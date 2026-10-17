using System.Reflection;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.patch;

internal static class SavePatchTests
{
    public static void Run()
    {
        NestedSaveCannotReleaseSnapshotBarrier();
        RejectedNestedSaveCannotReleaseOuterBarrier();
        SuccessfulSaveReleasesBarrierOnce();
        EngineFailureReleasesBarrier();
        PreparationFailureReleasesBarrier();
        ExportFailureReleasesBarrier();
        BranchSaveRepairsPreserveRecoverableBuildings();
        BranchRepairFailureCannotReleaseOuterBarrier();
        NewWorldClearsBranchIdentityCaches();
        Console.WriteLine("Save patch ownership and branch integration tests passed (9 scenarios).");
    }

    private static void NestedSaveCannotReleaseSnapshotBarrier()
    {
        Reset();
        using (AW_SavePatch.EnterMultiplayerSnapshotSave())
        {
            AW_SavePatch.SaveWorldToDirectory_Prefix(out bool ownsBarrier);
            True(!ownsBarrier, "nested snapshot save owns no additional barrier");
            AW_SavePatch.SaveWorldToDirectory_Postfix(string.Empty, ownsBarrier);
            FinalizeSave(null, ownsBarrier);
            True(AWAsyncWorldLifecycle.SaveBarrierActive,
                "nested save finalizer must preserve the outer snapshot barrier");
            Equal(0, AWAsyncWorldLifecycle.SaveBarrierExitCalls,
                "nested save does not exit the outer barrier");
        }
        Equal(1, AWAsyncWorldLifecycle.SaveBarrierExitCalls,
            "snapshot disposal releases its barrier once");
    }

    private static void RejectedNestedSaveCannotReleaseOuterBarrier()
    {
        Reset();
        AW_SavePatch.SaveWorldToDirectory_Prefix(out bool outer);
        try
        {
            bool inner = false;
            Exception failure = null;
            try { AW_SavePatch.SaveWorldToDirectory_Prefix(out inner); }
            catch (InvalidOperationException error) { failure = error; }
            True(failure != null, "nested ordinary save is blocked by the existing barrier");
            True(!inner, "rejected nested invocation never acquires ownership");
            True(ReferenceEquals(failure, FinalizeSave(failure, inner)),
                "nested save preserves the original rejection");
            True(AWAsyncWorldLifecycle.SaveBarrierActive,
                "rejected inner prefix and finalizer preserve the outer barrier");
            Equal(0, AWAsyncWorldLifecycle.SaveBarrierExitCalls,
                "rejected nested save does not release another invocation's barrier");
        }
        finally { FinalizeSave(null, outer); }
        Equal(1, AWAsyncWorldLifecycle.SaveBarrierExitCalls,
            "outer invocation releases its own barrier");
    }

    private static void SuccessfulSaveReleasesBarrierOnce()
    {
        Reset();
        AW_SavePatch.SaveWorldToDirectory_Prefix(out bool ownsBarrier);
        True(ownsBarrier && AWAsyncWorldLifecycle.SaveBarrierActive,
            "ordinary save enters the barrier before serialization");
        AW_SavePatch.SaveWorldToDirectory_Postfix(string.Empty, ownsBarrier);
        FinalizeSave(null, ownsBarrier);
        Equal(1, AWAsyncWorldLifecycle.SaveBarrierExitCalls,
            "empty-folder early return and finalizer release once");
        True(!AWAsyncWorldLifecycle.SaveBarrierActive, "successful save leaves no barrier");
    }

    private static void EngineFailureReleasesBarrier()
    {
        Reset();
        AW_SavePatch.SaveWorldToDirectory_Prefix(out bool ownsBarrier);
        var failure = new InvalidOperationException("injected engine save failure");
        True(ReferenceEquals(failure, FinalizeSave(failure, ownsBarrier)),
            "engine save errors are returned unchanged");
        Equal(1, AWAsyncWorldLifecycle.SaveBarrierExitCalls,
            "engine failure releases the barrier without a postfix");
    }

    private static void PreparationFailureReleasesBarrier()
    {
        Reset();
        DeferredRuntimeWorkService.FlushError = new InvalidOperationException("injected preparation failure");
        bool ownsBarrier = false;
        Exception failure = null;
        try { AW_SavePatch.SaveWorldToDirectory_Prefix(out ownsBarrier); }
        catch (InvalidOperationException error) { failure = error; }
        finally { DeferredRuntimeWorkService.FlushError = null; }
        True(failure != null && ownsBarrier, "failed preparation acquired and unwound its own barrier");
        FinalizeSave(failure, ownsBarrier);
        Equal(1, AWAsyncWorldLifecycle.SaveBarrierExitCalls,
            "failed preparation and finalizer release once");
        AW_SavePatch.SaveWorldToDirectory_Prefix(out bool next);
        FinalizeSave(null, next);
        True(!AWAsyncWorldLifecycle.SaveBarrierActive,
            "the next save can acquire and release the barrier after preparation failure");
    }

    private static void ExportFailureReleasesBarrier()
    {
        Reset();
        AW_SavePatch.SaveWorldToDirectory_Prefix(out bool ownsBarrier);
        var expected = new InvalidOperationException("injected sidecar export failure");
        DeJureRegionStore.PublishError = expected;
        Exception failure = null;
        try { AW_SavePatch.SaveWorldToDirectory_Postfix("test-save", ownsBarrier); }
        catch (InvalidOperationException error) { failure = error; }
        finally
        {
            DeJureRegionStore.PublishError = null;
            FinalizeSave(failure, ownsBarrier);
        }
        True(ReferenceEquals(expected, failure), "sidecar failures remain visible to the save caller");
        Equal(1, AWAsyncWorldLifecycle.SaveBarrierExitCalls,
            "failed sidecar export releases its barrier once");
    }

    private static Exception FinalizeSave(Exception error, bool state) =>
        (Exception)typeof(AW_SavePatch).GetMethod("SaveWorldToDirectory_Finalizer",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { error, state });

    private static void BranchSaveRepairsPreserveRecoverableBuildings()
    {
        Reset();
        var asset = new KingdomAsset();
        AssetManager.kingdoms.Items["restored"] = asset;
        var kingdom = new Kingdom { Founder = new ActorAsset { kingdom_id_civilization = "restored" } };
        World.world.kingdoms.Add(kingdom);
        var restorable = new Building();
        var invalid = new Building();
        World.world.buildings.Items.AddRange(new[] { restorable, invalid });
        AW_XiaWatchTowerKingdomPatch.RestoreAction = () => restorable.kingdom = kingdom;
        AW_SavePatch.SaveWorldToDirectory_Prefix(out bool state);
        try
        {
            True(!restorable.Removed && ReferenceEquals(restorable.kingdom, kingdom),
                "b repair restores inferred watchtower ownership before purging invalid buildings");
            True(invalid.Removed, "unrecoverable null-kingdom building is removed before serialization");
            True(ReferenceEquals(kingdom.asset, asset), "b repair resolves a missing kingdom asset before serialization");
        }
        finally { FinalizeSave(null, state); }
    }

    private static void BranchRepairFailureCannotReleaseOuterBarrier()
    {
        Reset();
        AW_SavePatch.SaveWorldToDirectory_Prefix(out bool outer);
        var expected = new InvalidOperationException("watchtower repair failed");
        AW_XiaWatchTowerKingdomPatch.RestoreAction = () => throw expected;
        bool inner = true;
        Exception failure = null;
        try
        {
            try { AW_SavePatch.SaveWorldToDirectory_Prefix(out inner); }
            catch (Exception error) { failure = error; }
            True(ReferenceEquals(expected, failure) && !inner,
                "b repair failure occurs after invocation ownership was initialized");
            FinalizeSave(failure, inner);
            True(AWAsyncWorldLifecycle.SaveBarrierActive && AWAsyncWorldLifecycle.SaveBarrierExitCalls == 0,
                "repair failure cannot release another save invocation's barrier");
        }
        finally
        {
            AW_XiaWatchTowerKingdomPatch.RestoreAction = null;
            FinalizeSave(null, outer);
        }
    }

    private static void NewWorldClearsBranchIdentityCaches()
    {
        Reset();
        int clan = ClanMembershipSyncService.ClearCalls;
        int family = FamilyIdentitySyncService.ClearCalls;
        int standing = SocialStandingService.ClearCalls;
        int draw = AncientWarfare3.ui.windows.HistoricalFigureDrawWindow.ResetCalls;
        AW_SavePatch.GenerateNewMap_Postfix();
        Equal(clan + 1, ClanMembershipSyncService.ClearCalls, "new world clears clan identity state");
        Equal(family + 1, FamilyIdentitySyncService.ClearCalls, "new world clears family identity state");
        Equal(standing + 1, SocialStandingService.ClearCalls, "new world clears social standing state");
        Equal(draw + 1, AncientWarfare3.ui.windows.HistoricalFigureDrawWindow.ResetCalls,
            "new world clears historical draw transient state");
    }

    private static void Reset()
    {
        True(!AWAsyncWorldLifecycle.SaveBarrierActive, "previous save must leave no active barrier");
        AWAsyncWorldLifecycle.SaveBarrierExitCalls = 0;
        AW_XiaWatchTowerKingdomPatch.RestoreAction = null;
        World.world.buildings.Items.Clear();
        World.world.kingdoms.Clear();
        World.world.kingdoms_wild.Clear();
        AssetManager.kingdoms.Items.Clear();
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Equal(int expected, int actual, string message) =>
        True(expected == actual, $"{message}: expected {expected}, actual {actual}");
}
