using AncientWarfare3.core.lineage;

internal static class ImperialHaremRankRulesTests
{
    internal static void Run()
    {
        Equal("empress", RulerHouseholdRankRules.SeatCode(0),
            "first imperial seat");
        Equal("consort_kang", RulerHouseholdRankRules.SeatCode(9),
            "last imperial seat");
        Equal("aw_household_rank_consort_de",
            RulerHouseholdRankRules.TitleKey("consort_de"),
            "stable rank codes map to localization keys");
        Equal(9,
            RulerHouseholdRules.ConsortCapacity(
                RulerHouseholdRealmTier.Empire),
            "imperial active consort capacity");
        True(RulerHouseholdRules.IsCandidateClassEligible(
                RulerHouseholdCandidateClass.Commoner,
                RulerHouseholdKind.Consort),
            "qualified commoner consort");
        False(RulerHouseholdRules.IsCandidateClassEligible(
                RulerHouseholdCandidateClass.Commoner,
                RulerHouseholdKind.PrincipalWife),
            "commoner is not principal wife by default");
        True(RulerHouseholdRankRules.KeepsSeatAfterAge(36),
            "age does not revoke rank");
        Equal("", RulerHouseholdRankRules.NextEmptySeat(
                new HashSet<string> { "empress" }, pPrincipal: true),
            "principal wife cannot fall through into a consort seat");

        int commoner = RulerHouseholdRankRules.ConsortScore(
            attributeScore: 92, lineagePriority: 0, noble: false);
        int noble = RulerHouseholdRankRules.ConsortScore(
            attributeScore: 61, lineagePriority: 0, noble: true);
        True(commoner > noble,
            "attributes outrank noble status for consorts");

        RulerHouseholdRankMigrationEntry[] rows =
        {
            Legacy(30, RulerHouseholdKind.PrincipalWife, 100),
            Legacy(10, RulerHouseholdKind.Consort, 101),
            Legacy(20, RulerHouseholdKind.Consort, 102)
        };
        IReadOnlyList<RulerHouseholdRankMigrationEntry> first =
            RulerHouseholdRankMigrationService.AssignLegacy(rows);
        IReadOnlyList<RulerHouseholdRankMigrationEntry> second =
            RulerHouseholdRankMigrationService.AssignLegacy(first);
        Equal("empress", first[0].RankCode,
            "principal wife gets empress");
        Equal("consort_de", first[1].RankCode,
            "oldest consort gets first seat");
        Equal(first[1].RankCode, second[1].RankCode,
            "migration is idempotent");
        False(second.Any(pRow => pRow.NeedsWrite),
            "idempotent migration performs no writes");

        RulerHouseholdRankMigrationEntry[] crowded = Enumerable.Range(1, 11)
            .Select(pId => Legacy(pId, RulerHouseholdKind.Consort,
                100 + pId))
            .ToArray();
        IReadOnlyList<RulerHouseholdRankMigrationEntry> normalized =
            RulerHouseholdRankMigrationService.AssignLegacy(crowded);
        Equal(2, normalized.Count(pRow => pRow.Closed),
            "consorts beyond nine fixed seats close deterministically");
        True(normalized.Where(pRow => pRow.Closed).All(pRow =>
                pRow.EndReason == "legacy_harem_over_capacity"),
            "over-capacity rows retain a migration reason");
    }

    private static void True(bool pValue, string pMessage)
    {
        if (!pValue) throw new InvalidOperationException(pMessage);
    }

    private static void False(bool pValue, string pMessage)
    {
        True(!pValue, pMessage);
    }

    private static void Equal<T>(T pExpected, T pActual, string pMessage)
    {
        if (!EqualityComparer<T>.Default.Equals(pExpected, pActual))
        {
            throw new InvalidOperationException(
                $"{pMessage}: expected {pExpected}, got {pActual}");
        }
    }

    private static RulerHouseholdRankMigrationEntry Legacy(long pId,
        RulerHouseholdKind pKind, int pStartYear)
    {
        return new RulerHouseholdRankMigrationEntry(pId, pKind, "",
            pStartYear, pStartYear * 60d, active: true);
    }
}
