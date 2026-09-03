using System;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     与原版对齐的即位收尾。
    ///
    ///     原版的正确流程在 <c>KingdomBehCheckKing.makeKingAndMoveToCapital</c>：
    ///     <code>
    ///     stopBeingWarrior() → city.removeLeader() → joinCity(capital)
    ///       → setKing() → WorldLog.logNewKing(kingdom)
    ///     </code>
    ///     我们自己的即位路径（年度兜底、宫廷政变、复辟、靖难）多数只裸调
    ///     <c>setKing</c>，于是新君既不迁都城、也不卸武职，**世界日志里更是
    ///     根本不出现即位消息** —— 玩家看到王位换了人却没有任何提示。
    ///
    ///     身份落库那一侧已经由 <c>AW_HeirPatch</c> 的 <c>setKing</c> 前后置
    ///     补丁负责，这里只补「礼仪 + 日志」，不与之重复。
    ///
    ///     每一步单独 try/catch：迁城、卸职这类动作会被容量、住房、别的规则
    ///     挡下来，挡下来也不该让即位本身失败 —— 王位空悬比少迁一次城糟得多。
    /// </summary>
    internal static class KingAccessionCeremonyService
    {
        /// <summary>
        ///     让 <paramref name="pActor"/> 按原版流程即位为
        ///     <paramref name="pKingdom"/> 的君主。
        /// </summary>
        /// <param name="pReason">仅用于诊断日志。</param>
        /// <returns>王位确实落到此人身上返回 true。</returns>
        internal static bool Install(Kingdom pKingdom, Actor pActor,
            string pReason)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pActor.isRekt() || !pActor.isAlive()) return false;

            PrepareForThrone(pKingdom, pActor);
            try { pKingdom.setKing(pActor); }
            catch (Exception error)
            {
                ModClass.LogWarning("[AW3] 即位失败(" + (pReason ?? "") +
                                    "): " + error.Message);
                return false;
            }
            if (pKingdom.king != pActor) return false;
            AnnounceAccession(pKingdom);
            return true;
        }

        /// <summary>
        ///     即位前的身份剥离与迁居，逐条对应原版
        ///     <c>makeKingAndMoveToCapital</c> 的前三步。
        /// </summary>
        private static void PrepareForThrone(Kingdom pKingdom, Actor pActor)
        {
            try
            {
                if (pActor.hasCity())
                {
                    pActor.stopBeingWarrior();
                    // 原版在这里卸城主:国君不兼任一城之长,
                    // 不卸的话都城面板上会同时挂着他两个身份。
                    if (pActor.isCityLeader())
                        pActor.city?.removeLeader();
                }
            }
            catch { }
            try
            {
                City capital = pKingdom.capital;
                if (capital?.data != null && !capital.isRekt() &&
                    pActor.city != capital)
                    pActor.joinCity(capital);
            }
            catch { }
        }

        /// <summary>
        ///     世界日志里的即位消息。原版由
        ///     <c>makeKingAndMoveToCapital</c> 末尾发出，我们绕过了那条路径，
        ///     所以必须自己补 —— 这正是玩家反馈「没有 WorldLogMessage」的那条。
        /// </summary>
        private static void AnnounceAccession(Kingdom pKingdom)
        {
            try { WorldLog.logNewKing(pKingdom); }
            catch { }
        }
    }
}
