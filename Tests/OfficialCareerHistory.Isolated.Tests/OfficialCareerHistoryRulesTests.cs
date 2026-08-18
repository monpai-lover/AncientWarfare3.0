using AncientWarfare3.core.court;

internal static class OfficialCareerHistoryRulesTests
{
    internal static void Run()
    {
        var current = new OfficialCareerHistoryRow(7, 11, 91, 3,
            "city", "granary_officer", "张三", 120, -1, true, "");
        Equal("120—至今",
            OfficialCareerHistoryRules.YearRange(current, "至今", "未知"),
            "active term range");

        OfficialCareerHistoryRow ended = current.WithEnd(127,
            "term_expired");
        Equal("120—127",
            OfficialCareerHistoryRules.YearRange(ended, "至今", "未知"),
            "closed term range");
        True(OfficialCareerHistoryRules.IsNewer(ended, current),
            "ended row ordering uses appointment identity");

        var unknown = new OfficialCareerHistoryRow(7, 12, 92, -1,
            "central", "chancellor", "李四", -1, -1, false, "dismissed");
        Equal("未知—未知",
            OfficialCareerHistoryRules.YearRange(unknown, "至今", "未知"),
            "missing years use the localized unknown label");

        var cityScope = new OfficialCareerHistoryScope(7, 3, "city",
            "granary_officer");
        True(cityScope.HasCity, "city office scope retains its city filter");
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
