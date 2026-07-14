using AncientWarfare3.core.schools;

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
        index.Remove(42);
        Equal(0, index.MemberCount("ru"),
            "death/close removes all buckets");

        index.SetLivingXiaCity(100, true);
        Equal(1, index.LivingXiaCityCount,
            "living Xia city enters the descent index");
        index.SetLivingXiaCity(100, false);
        Equal(0, index.LivingXiaCityCount,
            "destroyed or transferred Xia city leaves the descent index");
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
}
