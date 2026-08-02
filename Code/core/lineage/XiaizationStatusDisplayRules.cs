namespace AncientWarfare3.core.lineage
{
    public static class XiaizationStatusDisplayRules
    {
        public static bool ShouldShow(bool nativeXia)
        {
            return !nativeXia;
        }

        public static string Format(string prefix, int level,
            string levelLabel)
        {
            return (prefix ?? "") + "：" +
                   System.Math.Max(0, level) + "级 · " +
                   (levelLabel ?? "");
        }
    }
}
