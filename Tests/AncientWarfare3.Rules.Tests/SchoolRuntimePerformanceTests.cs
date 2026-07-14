using AncientWarfare3.core.schools;
using AncientWarfare3.core.court;

internal static class SchoolRuntimePerformanceTests
{
    public static void Run()
    {
        var years = new HistoricalSchoolSchedulerState();
        True(years.EnqueueYear(73), "first year token is accepted");
        True(years.EnqueueYear(75), "newer year coalesces pending work");
        Equal(75, years.PendingYear, "latest pending year wins");
        Equal(75, years.TakePendingYear(), "pending year is consumed once");
        Equal(-1, years.TakePendingYear(), "empty scheduler stays empty");
        Equal(false, years.EnqueueYear(75),
            "consumed year cannot be requeued by another kingdom");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) years.HasPendingWork();
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before,
            "idle scheduler query allocates zero bytes");

        Equal(HistoricalSchoolRevisionMask.None,
            HistoricalSchoolRevisionRules.ClassifyAffiliation(4, 4, true, true, -1, -1),
            "identical affiliation does not invalidate");
        Equal(HistoricalSchoolRevisionMask.Residence,
            HistoricalSchoolRevisionRules.ClassifyAffiliation(4, 9, true, true, -1, -1),
            "actual residence move invalidates residence only");
        Equal(HistoricalSchoolRevisionMask.Presence,
            HistoricalSchoolRevisionRules.ClassifyAffiliation(4, 4, true, false, -1, -1),
            "travel departure invalidates presence");
        Equal(HistoricalSchoolRevisionMask.Service,
            HistoricalSchoolRevisionRules.ClassifyAffiliation(4, 4, true, true, -1, 8),
            "appointment invalidates service only");

        Equal(HistoricalSchoolStanding.Teacher,
            HistoricalSchoolStandingRules.ResolvePromotion(
                HistoricalSchoolStanding.Disciple, 3, 10f),
            "three-year reputation-ten disciple becomes teacher");
        Equal(HistoricalSchoolStanding.Disciple,
            HistoricalSchoolStandingRules.ResolvePromotion(
                HistoricalSchoolStanding.Disciple, 2, 30f),
            "membership age cannot be skipped");
        True(HistoricalSchoolStandingRules.CanConvert(30, 18, 5, 0.45f, false),
            "conversion is available after loyalty and teacher absence");
        Equal(false,
            HistoricalSchoolStandingRules.CanConvert(29, 18, 5, 0.45f, false),
            "twelve-year loyalty is strict");
        Equal(false,
            HistoricalSchoolStandingRules.CanConvert(40, 18, 5, 0.45f, true),
            "busy member cannot convert");

        var leaderCandidates = new[]
        {
            new HistoricalSchoolLeaderCandidate(
                11, 8, HistoricalSchoolStanding.Teacher, true),
            new HistoricalSchoolLeaderCandidate(
                9, 8, HistoricalSchoolStanding.Teacher, true),
            new HistoricalSchoolLeaderCandidate(
                5, 3, HistoricalSchoolStanding.Disciple, true),
            new HistoricalSchoolLeaderCandidate(
                2, 1, HistoricalSchoolStanding.Teacher, false)
        };
        Equal(9L,
            HistoricalSchoolStandingRules.SelectLeaderActorId(leaderCandidates),
            "senior available teacher wins with actor id tie-break");

        int fairCursor = -1;
        var visitedSchools = new HashSet<int>();
        for (int slot = 0; slot < 14; slot++)
        {
            fairCursor = HistoricalSchoolStandingRules.NextFairIndex(fairCursor, 14);
            True(visitedSchools.Add(fairCursor),
                "fair school cursor does not repeat before full coverage");
        }
        Equal(14, visitedSchools.Count,
            "two eight-slot years can cover all fourteen schools");
        Equal(0, HistoricalSchoolStandingRules.NextFairIndex(fairCursor, 14),
            "fair school cursor repeats only after full coverage");

        True(FormalAffiliationTransferRules.Allows(42, 7, 11, 42, 7, 11),
            "exact committed transfer is allowed");
        Equal(false,
            FormalAffiliationTransferRules.Allows(42, 7, 11, 43, 7, 11),
            "another actor cannot borrow a permit");
        Equal(false,
            FormalAffiliationTransferRules.Allows(42, 7, 11, 42, 7, 12),
            "another city cannot borrow a permit");

        var index = new HistoricalSchoolRuntimeIndex();
        index.Upsert(new HistoricalSchoolIndexEntry(
            42,
            "ru",
            7,
            HistoricalSchoolStanding.Disciple,
            true,
            false,
            -1));
        Equal(1, index.MemberCount("ru"), "school member is indexed once");
        Equal(1, index.ResidentCount(7, "ru"),
            "present resident is indexed by city");
        index.Upsert(new HistoricalSchoolIndexEntry(
            42,
            "ru",
            9,
            HistoricalSchoolStanding.Teacher,
            true,
            true,
            -1));
        Equal(0, index.ResidentCount(7, "ru"),
            "old residence bucket is removed");
        Equal(1, index.TeacherCount("ru"),
            "promotion updates teacher bucket");
        index.Upsert(new HistoricalSchoolIndexEntry(
            43,
            "mo",
            9,
            HistoricalSchoolStanding.Teacher,
            true,
            false,
            -1,
            pTravelBucket: 2,
            pTravelEligible: true));
        Equal(1, index.TravelEligibleCount(2),
            "resident teacher enters the eligible travel bucket before departure");
        Equal(43L, index.TravelEligibleIds(2)[0],
            "eligible travel bucket returns the resident teacher");
        index.Remove(42);
        Equal(0, index.MemberCount("ru"),
            "death/close removes all buckets");
        index.Remove(43);
        Equal(0, index.TravelEligibleCount(2),
            "membership close removes the eligible travel bucket entry");

        index.Upsert(new HistoricalSchoolIndexEntry(
            50, "mo", 9, HistoricalSchoolStanding.Teacher,
            true, false, -1));
        index.Upsert(new HistoricalSchoolIndexEntry(
            51, "mo", 9, HistoricalSchoolStanding.Disciple,
            true, false, -1,
            pTeacherActorId: 50, pDirectDiscipleship: true));
        index.Upsert(new HistoricalSchoolIndexEntry(
            52, "fa", 10, HistoricalSchoolStanding.Teacher,
            false, false, 7));
        Equal(1, index.DirectDiscipleCount(50),
            "direct disciple count is maintained by membership projection");
        Equal(50L, index.ResidentTeacherIds(9, "mo")[0],
            "present city-school teacher bucket returns the teacher");
        Equal(2, index.ResidentIds(9).Length,
            "city residence bucket joins schools without a world scan");
        Equal(9L, index.PresentCityIds()[0],
            "present city ids are available without rebuilding an annual map");
        Equal(52L, index.ServingActorIds()[0],
            "serving actor ids are available without affiliation snapshots");
        index.Remove(51);
        Equal(0, index.DirectDiscipleCount(50),
            "closing a disciple removes the teacher's direct count");
        index.Remove(50);
        index.Remove(52);

        index.SetLivingXiaCity(100, true);
        Equal(1, index.LivingXiaCityCount,
            "living Xia city enters the descent index");
        index.SetLivingXiaCity(100, false);
        Equal(0, index.LivingXiaCityCount,
            "destroyed or transferred Xia city leaves the descent index");

        var leases = new HistoricalSchoolTaskLeaseBook();
        var firstLease = new HistoricalSchoolTaskLease(
            42, "lecture:1", "aw_school_lecture", "ru", 7, "venue:1", 10, 20);
        True(leases.TryAcquire(firstLease), "task lease can be acquired");
        Equal(false, leases.TryAcquire(firstLease),
            "duplicate actor task lease is rejected");
        Equal(false, leases.TryRelease(42, "stale", out _),
            "stale completion cannot release current lease");
        True(leases.TryRelease(42, "lecture:1", out HistoricalSchoolTaskLease released),
            "exact completion releases current lease");
        Equal("venue:1", released.VenueKey, "released lease preserves venue owner");

        var expiringLease = new HistoricalSchoolTaskLease(
            43, "debate:1", "aw_school_debate", "mo", 8, "venue:2", 11, 15);
        True(leases.TryAcquire(expiringLease), "expiring lease can be acquired");
        Equal(false, leases.TryExpireOne(14, out _),
            "lease does not expire before its deadline");
        True(leases.TryExpireOne(15, out HistoricalSchoolTaskLease expired),
            "lease expires at its deadline");
        Equal(43L, expired.ActorId, "expiry returns the exact actor lease");

        True(leases.TryAcquire(new HistoricalSchoolTaskLease(
            44, "debate:2", "aw_school_debate", "fa", 9, "venue:3", 16, 30)),
            "death-release lease can be acquired");
        True(leases.TryReleaseActor(44, out _), "death releases actor lease");
        leases.TryAcquire(new HistoricalSchoolTaskLease(
            45, "lecture:2", "aw_school_lecture", "dao", 10, "venue:4", 20, 40));
        leases.Clear();
        Equal(0, leases.Count, "clear removes every task lease");

        leases.TryExpireOne(100, out _);
        long leaseAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) leases.TryExpireOne(100, out _);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - leaseAllocatedBefore,
            "empty activity scheduler allocates zero bytes");

        var cityCache = new HistoricalSchoolFixedLru<long, int>(128);
        for (int city = 0; city < 128; city++) cityCache.Set(city, city);
        True(cityCache.TryGet(0, out _), "LRU access refreshes oldest city");
        cityCache.Set(128, 128);
        Equal(false, cityCache.ContainsKey(1), "LRU evicts the least recent city");
        True(cityCache.ContainsKey(0), "recently accessed city remains cached");
        True(cityCache.Remove(0), "city destruction removes its cache entry");
        Equal(127, cityCache.Count, "city cache remains at fixed capacity after removal");

        var cityStamp = new HistoricalSchoolCityCacheStamp(
            pIdentityToken: 17, pKingdomId: 4, pZoneCount: 12,
            pCenterX: 80, pCenterY: 40);
        True(cityStamp.Matches(17, 4, 12, 80, 40),
            "unchanged city identity and geometry keep a cache entry valid");
        Equal(false, cityStamp.Matches(17, 5, 12, 80, 40),
            "city transfer invalidates its cache entry");
        Equal(false, cityStamp.Matches(17, 4, 13, 80, 40),
            "city zone growth invalidates its cache entry");
        Equal(false, cityStamp.Matches(18, 4, 12, 80, 40),
            "reused city id cannot reuse another city object's cache entry");

        Equal(HistoricalSchoolVenueSourceKind.Academy,
            HistoricalSchoolVenueRules.SelectSource(
                pAcademyAvailable: true, pPublicAvailable: true, pLocalAvailable: true),
            "academy venue has first priority");
        Equal(HistoricalSchoolVenueSourceKind.PublicCity,
            HistoricalSchoolVenueRules.SelectSource(
                pAcademyAvailable: false, pPublicAvailable: true, pLocalAvailable: true),
            "public city venue is the first fallback");
        Equal(HistoricalSchoolVenueSourceKind.Local,
            HistoricalSchoolVenueRules.SelectSource(
                pAcademyAvailable: false, pPublicAvailable: false, pLocalAvailable: true),
            "bounded local venue is the final fallback");
        Equal(false,
            HistoricalSchoolVenueRules.IsPublicCandidate(
                pInsideCity: true, pWalkable: true, pCityCenter: true),
            "city center is never a universal public venue");
        Equal(false,
            HistoricalSchoolVenueRules.IsIdleRoamCandidate(
                pInsideResidenceCity: true, pWalkable: true,
                pCityCenter: false, pBorderZone: false, pDistanceSquared: 35),
            "idle roam never chooses a tile nearer than six tiles");
        True(HistoricalSchoolVenueRules.IsIdleRoamCandidate(
                pInsideResidenceCity: true, pWalkable: true,
                pCityCenter: false, pBorderZone: false, pDistanceSquared: 36),
            "idle roam accepts the six-tile boundary");
        True(HistoricalSchoolVenueRules.IsIdleRoamCandidate(
                pInsideResidenceCity: true, pWalkable: true,
                pCityCenter: false, pBorderZone: false, pDistanceSquared: 324),
            "idle roam accepts the eighteen-tile boundary");
        Equal(false,
            HistoricalSchoolVenueRules.IsIdleRoamCandidate(
                pInsideResidenceCity: true, pWalkable: true,
                pCityCenter: false, pBorderZone: false, pDistanceSquared: 325),
            "idle roam remains within eighteen tiles");
        Equal(false,
            HistoricalSchoolVenueRules.IsIdleRoamCandidate(
                pInsideResidenceCity: true, pWalkable: true,
                pCityCenter: false, pBorderZone: true, pDistanceSquared: 100),
            "idle roam never chooses a residence border zone");

        var rosterRevisions = new TestSchoolRevisionSource();
        rosterRevisions.SetSchool("ru", pStructure: 3, pScore: 5, pActivity: 7);
        rosterRevisions.SetSchool("bing", pStructure: 11, pScore: 13, pActivity: 17);
        rosterRevisions.SetCity(41, 19);
        rosterRevisions.SetCity(43, 23);
        rosterRevisions.SetCity(47, 29);
        HistoricalSchoolRosterRevisionStamp rosterStamp =
            HistoricalSchoolRosterRevisionStamp.Capture("ru",
                new long[] { 43, 41, 43 }, rosterRevisions);
        Equal(2, rosterStamp.CityCount,
            "roster revision stamp deduplicates represented cities");
        True(rosterStamp.IsCurrent("ru", rosterRevisions),
            "unchanged selected school roster stays current");
        rosterRevisions.SetSchool("bing", pStructure: 31, pScore: 37, pActivity: 41);
        rosterRevisions.SetCity(47, 43);
        True(rosterStamp.IsCurrent("ru", rosterRevisions),
            "another school and unrelated city do not invalidate the roster");
        rosterRevisions.SetSchool("ru", pStructure: 3, pScore: 6, pActivity: 7);
        Equal(false, rosterStamp.IsCurrent("ru", rosterRevisions),
            "selected-school score change invalidates the roster");
        rosterRevisions.SetSchool("ru", pStructure: 3, pScore: 5, pActivity: 7);
        rosterRevisions.SetCity(43, 24);
        Equal(false, rosterStamp.IsCurrent("ru", rosterRevisions),
            "represented-city residence change invalidates the roster");
        rosterRevisions.SetCity(43, 23);
        Equal(false, rosterStamp.IsCurrent("bing", rosterRevisions),
            "switching the selected school invalidates the roster");
        rosterStamp.IsCurrent("ru", rosterRevisions);
        long rosterAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int rosterIndex = 0; rosterIndex < 10_000; rosterIndex++)
            rosterStamp.IsCurrent("ru", rosterRevisions);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - rosterAllocatedBefore,
            "unchanged roster revision checks allocate zero bytes");

        var dirtyCities = new CitySchoolDirtyQueue();
        True(dirtyCities.Mark(1), "first dirty city enters the queue");
        Equal(false, dirtyCities.Mark(1), "duplicate dirty city is coalesced");
        dirtyCities.Mark(2);
        dirtyCities.Mark(3);
        Equal(2, dirtyCities.RequeueFront(new long[] { 3, 2, 3 }),
            "front requeue deduplicates the requested cities");
        True(dirtyCities.TryDequeue(out long firstDirty),
            "dirty queue dequeues its first city");
        Equal(3L, firstDirty, "front requeue preserves requested order");
        True(dirtyCities.Remove(1), "dirty city can be removed before processing");
        True(dirtyCities.TryDequeue(out long secondDirty),
            "dirty queue dequeues the remaining city");
        Equal(2L, secondDirty, "dirty queue remains FIFO after removal");
        Equal(false, dirtyCities.TryDequeue(out long emptyDirty),
            "empty dirty queue reports no work");
        Equal(-1L, emptyDirty, "empty dirty queue returns the sentinel id");
        dirtyCities.TryDequeue(out _);
        long dirtyAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int dirtyIndex = 0; dirtyIndex < 10_000; dirtyIndex++)
            dirtyCities.TryDequeue(out _);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - dirtyAllocatedBefore,
            "empty dirty queue allocates zero bytes");
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }

    private static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }

    private sealed class TestSchoolRevisionSource : IHistoricalSchoolRevisionSource
    {
        private readonly Dictionary<string, long[]> _schools =
            new Dictionary<string, long[]>(StringComparer.Ordinal);
        private readonly Dictionary<long, long> _cities = new Dictionary<long, long>();

        public void SetSchool(string pSchoolId, long pStructure, long pScore, long pActivity)
        {
            _schools[pSchoolId] = new long[] { pStructure, pScore, pActivity };
        }

        public void SetCity(long pCityId, long pRevision)
        {
            _cities[pCityId] = pRevision;
        }

        public long StructureRevision(string pSchoolId) => School(pSchoolId, 0);
        public long ScoreRevision(string pSchoolId) => School(pSchoolId, 1);
        public long ActivityRevision(string pSchoolId) => School(pSchoolId, 2);

        public long ResidenceRevisionForCity(long pCityId)
        {
            return _cities.TryGetValue(pCityId, out long revision) ? revision : 0L;
        }

        private long School(string pSchoolId, int pIndex)
        {
            return pSchoolId != null && _schools.TryGetValue(pSchoolId, out long[] values)
                ? values[pIndex]
                : 0L;
        }
    }
}
