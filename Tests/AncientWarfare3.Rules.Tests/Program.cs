using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}

static void True(bool value, string name)
{
    if (!value) throw new InvalidOperationException($"{name}: expected true");
}

Equal(RoyalLineageSourceKind.Self,
    RoyalLineageResolutionRules.Resolve(true, true, true, true, true),
    "self branch wins");
Equal(RoyalLineageSourceKind.Father,
    RoyalLineageResolutionRules.Resolve(false, true, true, true, true),
    "father branch precedes royal and sibling");
Equal(RoyalLineageSourceKind.CurrentRoyal,
    RoyalLineageResolutionRules.Resolve(false, false, true, true, true),
    "related current royal precedes sibling");
Equal(RoyalLineageSourceKind.Sibling,
    RoyalLineageResolutionRules.Resolve(false, false, true, false, true),
    "unrelated current royal is ignored");
Equal(RoyalLineageSourceKind.Create,
    RoyalLineageResolutionRules.Resolve(false, false, false, false, false),
    "new branch is last resort");
True(RoyalLineageResolutionRules.SharesKnownFather(17, 17),
    "brothers with the same known father are related");
Equal(false, RoyalLineageResolutionRules.SharesKnownFather(-1, -1),
    "unknown parents never create a false sibling relation");
Equal(false, RoyalLineageResolutionRules.SharesKnownFather(17, 18),
    "different fathers remain separate");

True(XiaNameRepairRules.IsInvalidGeneratedMetaName("NAME"), "NAME is invalid");
True(XiaNameRepairRules.IsInvalidGeneratedMetaName("#NO_NAME#"), "#NO_NAME# is invalid");
True(XiaNameRepairRules.IsInvalidGeneratedMetaName("无名"), "anonymous shi is invalid");
True(XiaNameRepairRules.IsInvalidGeneratedMetaName("无名氏"), "anonymous clan is invalid");
Equal(false, XiaNameRepairRules.IsInvalidGeneratedMetaName("孔氏"), "historical clan is valid");

True(HistoricalSchoolActivityQueueRules.CanEnqueue(7, 8, false),
    "eighth lecture may be queued");
Equal(false, HistoricalSchoolActivityQueueRules.CanEnqueue(8, 8, false),
    "ninth lecture is rejected");
Equal(false, HistoricalSchoolActivityQueueRules.CanEnqueue(1, 8, true),
    "duplicate operation is rejected");
True(HistoricalSchoolActivityQueueRules.CanAdvance(0, 0.25, 1.0),
    "first transition within frame budget advances");
Equal(false, HistoricalSchoolActivityQueueRules.CanAdvance(1, 0.25, 1.0),
    "second transition in one frame is rejected");
Equal(false, HistoricalSchoolActivityQueueRules.CanAdvance(0, 1.0, 1.0),
    "elapsed frame budget is strict");
True(HistoricalSchoolActivityQueueRules.CanActivate(3, 4),
    "debate may start below the concurrent activity limit");
Equal(false, HistoricalSchoolActivityQueueRules.CanActivate(4, 4),
    "debate start is deferred at the concurrent activity limit");
Equal(false,
    HistoricalSchoolActivityQueueRules.ActorYearKey(10, 42) ==
    HistoricalSchoolActivityQueueRules.ActorYearKey(11, 42),
    "debate actor occupancy resets in a new year");
True(HistoricalSchoolActivityQueueRules.ShouldCancelInterrupted(false, false, 121, 120),
    "interrupted activity is cancelled after its grace period");
Equal(false, HistoricalSchoolActivityQueueRules.ShouldCancelInterrupted(true, false,
    121, 120), "ready activity waits for persistence rather than cancelling");
Equal(false, HistoricalSchoolActivityQueueRules.ShouldCancelInterrupted(false, true,
    121, 120), "actor still running its task is not cancelled");
Equal(false, HistoricalSchoolActivityQueueRules.ShouldFlushForSave(false),
    "unfinished movement is not forced to settle during save");
True(HistoricalSchoolActivityQueueRules.ShouldFlushForSave(true),
    "completed activity is flushed before save");
Equal(false, HistoricalSchoolActivityQueueRules.IsPersistenceResolved(
        HistoricalSchoolTeachingPersistenceOutcome.Unknown),
    "unknown persistence blocks save and remains queued");
True(HistoricalSchoolActivityQueueRules.IsPersistenceResolved(
        HistoricalSchoolTeachingPersistenceOutcome.Committed),
    "new commit resolves pending activity");
True(HistoricalSchoolActivityQueueRules.IsPersistenceResolved(
        HistoricalSchoolTeachingPersistenceOutcome.Replayed),
    "idempotent replay resolves pending activity");
True(HistoricalSchoolActivityQueueRules.IsPersistenceResolved(
        HistoricalSchoolTeachingPersistenceOutcome.CleanFailure),
    "clean failure resolves and clears invalid activity");

var occupiedVenues = new HashSet<int>();
True(HistoricalSchoolVenueRules.TrySelect(101, 8, occupiedVenues, out int firstVenue),
    "first venue is selected");
True(HistoricalSchoolVenueRules.TrySelect(101, 8, new HashSet<int>(),
    out int repeatedVenue), "stable venue can be selected again");
Equal(firstVenue, repeatedVenue, "same operation key selects the same free venue");
occupiedVenues.Add(firstVenue);
True(HistoricalSchoolVenueRules.TrySelect(101, 8, occupiedVenues, out int secondVenue),
    "occupied first venue is skipped");
Equal(false, firstVenue == secondVenue, "venue claims are distinct");
Equal(false, HistoricalSchoolVenueRules.TrySelect(101, 1,
    new HashSet<int> { 0 }, out _), "full venue set rejects a claim");

var slots = new HistoricalSchoolActiveMasterSlots();
True(slots.TryReserve("ru", "confucius"), "school slot can be reserved");
Equal(false, slots.TryReserve("ru", "mencius"),
    "second master in the same school is blocked");
True(slots.TryActivate("ru", "confucius", 42), "matching reservation activates");
True(slots.TryAttachActor("ru", "confucius", 42),
    "reconciliation attachment is idempotent for the active master");
Equal(false, slots.TryRelease("ru", "mencius", 42),
    "stale master cannot release an active slot");
Equal(false, slots.TryRelease("ru", "confucius", 99),
    "wrong actor cannot release an active slot");
True(slots.TryRelease("ru", "confucius", 42),
    "committed matching death releases the school");
True(slots.TryReserve("ru", "mencius"), "next master may reserve after release");

var pendingSlots = new HistoricalSchoolActiveMasterSlots();
True(pendingSlots.TryReserve("dao", "laozi"),
    "pending school master can reserve its school");
True(pendingSlots.TryAttachActor("dao", "laozi", 77),
    "created actor attaches to the pending reservation");
True(pendingSlots.TryRelease("dao", "laozi", 77),
    "clean descent failure releases the attached reservation");

var restoredSlots = new HistoricalSchoolActiveMasterSlots();
True(restoredSlots.TryRestoreActive("mo", "mozi", 88),
    "load reconstructs a persisted living master");
True(restoredSlots.TryRestoreActive("mo", "mozi", 88),
    "load reconstruction is idempotent for the same actor");
Equal(false, restoredSlots.TryRestoreActive("mo", "qinhuazi", 99),
    "load blocks a second living master in the same school");

SchoolRuntimePerformanceTests.Run();

Console.WriteLine("Rule tests passed.");
