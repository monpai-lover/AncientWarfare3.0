using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

const string simplifiedLegacy = "\u7532\u4e0e\u4e59";
const string simplifiedComplete =
    "\u7532\u4e0e\u4e59\u7f14\u7ed3\u5a5a\u76df";
const string traditionalLegacy = "\u7532\u8207\u4e59";
const string traditionalComplete =
    "\u7532\u8207\u4e59\u7de0\u7d50\u5a5a\u76df";

Equal("A married B",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", "A married B", "cz"),
    "English history stays English under simplified Chinese UI");
Equal(simplifiedComplete,
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", simplifiedLegacy, "en"),
    "simplified Chinese history gets its own suffix under English UI");
Equal(traditionalComplete,
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", traditionalLegacy, "cz"),
    "traditional Chinese history gets its own suffix under simplified UI");
Equal(simplifiedComplete,
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", simplifiedComplete, "ch"),
    "simplified suffix is not duplicated after a language switch");
Equal(traditionalComplete,
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", traditionalComplete, "cz"),
    "traditional suffix is not duplicated after a language switch");
Equal("A wed B",
    WarDisplayLabelRules.NormalizeHistoryContent(
        "royal_marriage", "A wed B", "cz"),
    "unknown legacy format fails closed");
Equal(simplifiedLegacy,
    WarDisplayLabelRules.NormalizeHistoryContent(
        "war_start", simplifiedLegacy, "cz"),
    "non-marriage history is untouched");

Console.WriteLine("AW3 legacy royal-marriage language rules passed.");

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(
            name + ": expected " + expected + ", got " + actual);
}
