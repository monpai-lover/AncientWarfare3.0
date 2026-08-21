using AncientWarfare3.core.lineage;

True(ZhuluCapitalBreakthroughRules.ShouldTrigger(true, true, false, false),
    "a captured enemy capital triggers zhulu expansion");
True(ZhuluCapitalBreakthroughRules.ShouldTrigger(true, false, true, false),
    "a captured de jure seat triggers zhulu expansion");
False(ZhuluCapitalBreakthroughRules.ShouldTrigger(true, false, false, false),
    "an ordinary city does not trigger zhulu expansion");
False(ZhuluCapitalBreakthroughRules.ShouldTrigger(false, true, false, false),
    "ordinary wars do not trigger zhulu expansion");
False(ZhuluCapitalBreakthroughRules.ShouldTrigger(true, true, true, true),
    "a processed breakthrough is idempotent");
True(ZhuluCapitalBreakthroughRules.ShouldTransferCity(true, false, false, false, true),
    "enemy participant cities are transferred");
False(ZhuluCapitalBreakthroughRules.ShouldTransferCity(true, true, false, false, true),
    "attacker cities stay with their owner");
False(ZhuluCapitalBreakthroughRules.ShouldTransferCity(true, false, true, false, true),
    "friendly participant cities stay with their owner");
False(ZhuluCapitalBreakthroughRules.ShouldTransferCity(false, false, false, false, true),
    "unrelated countries are excluded");
False(ZhuluCapitalBreakthroughRules.ShouldTransferCity(true, false, false, false, false),
    "invalid cities are excluded");
var ids = ZhuluCapitalBreakthroughRules.MergeCityIds(
    new[] { 4L, 2L, 4L }, new[] { 7L, 2L, 9L }, 2L);
Equal("4,7,9", string.Join(",", ids),
    "region and direct neighbors merge without duplicates or seat");
Console.WriteLine("Zhulu capital breakthrough rules passed.");

static void True(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void False(bool value, string message)
{
    if (value) throw new InvalidOperationException(message);
}

static void Equal(string expected, string actual, string message)
{
    if (expected != actual)
        throw new InvalidOperationException(message +
            " expected=" + expected + " actual=" + actual);
}
