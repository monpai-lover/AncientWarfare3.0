namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     <see cref="MandateWarPeaceRules"/> 的运行时取数壳，
    ///     写法与 <see cref="ZhuluPeaceGuard"/> 一致。
    /// </summary>
    internal static class MandateWarPeaceGuard
    {
        /// <summary>
        ///     这场战争是否禁止「战功/战争目标」型自动收局。
        ///     军事消灭与厌战和谈不受影响。
        /// </summary>
        public static bool BlocksScoreAndGoalSettlement(War pWar)
        {
            bool active = false;
            string type = "";
            try
            {
                active = pWar?.data != null && !pWar.hasEnded();
                type = pWar?.getAsset()?.id ?? "";
            }
            catch { return false; }
            return MandateWarPeaceRules.BlocksOrdinarySettlement(type,
                active);
        }
    }
}
