using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch.naming
{
    /// <summary>
    ///     让小家庭（原版 <c>Family</c>）的名字跟着成员的氏走。
    ///
    ///     原版 <c>Family.newFamily</c> 末尾调 <c>generateName</c>，走
    ///     <c>NameGenerator</c> 按物种随机取名，完全不认我们的氏/姓 ——
    ///     「姬发」成家之后小家庭顶着随机洋名，家谱树和城市面板里两套名字对不上。
    ///
    ///     两个钩点：
    ///     1. <c>FamilyManager.newFamily</c> Postfix —— 建家当时同步一次。
    ///        挂在 manager 而不是 <c>Family.newFamily</c>：后者跑完时
    ///        <c>setFamily</c> 还没执行，<c>family.units</c> 是空的，
    ///        取不到锚点。
    ///     2. 个人改氏时同步 —— 见
    ///        <see cref="AncientWarfare3.core.lineage.VisibleSurnameRenameService"/>
    ///        那条路已有的调用点。
    ///
    ///     只改显示名，不碰原版的分家行为、人数上限和 <c>original_family</c> 链。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_FamilyIdentityPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(FamilyManager), nameof(FamilyManager.newFamily))]
        private static void NewFamily_Postfix(Family __result, Actor pActor)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__result?.data == null) return;
            // 建家者优先;他没有氏就退回家里其他有氏的人。
            Actor anchor = pActor?.data != null
                ? pActor
                : FamilyIdentitySyncService.ResolveAnchor(__result);
            FamilyIdentitySyncService.SyncFamilyName(__result, anchor);
        }

        /// <summary>
        ///     开宗时补一次宗族命名。
        ///
        ///     <para>
        ///     主链路是 <see cref="AncientWarfare3.patch.AW_ClanNamePatch"/> 在
        ///     <c>Clan.newClan</c> 后置里调的 <c>RenameClanByLeader</c>，
        ///     它拼「本源城名 + 氏 + 氏」。这里挂在外层的
        ///     <c>ClanManager.newClan</c> 上，跑在它之后 ——
        ///     <c>Clan.newClan</c> 结束时 <c>setClan</c> 还没执行，
        ///     建族者的 <c>CLAN_NAME</c> 若此刻才就绪，主链路那趟会跳过。
        ///     </para>
        ///
        ///     <para>
        ///     两趟都走同一个 <c>RenameClanByLeader</c>，它自身幂等
        ///     （名字相同直接返回），所以重复调用无害。绝不能在这里另拼一套
        ///     名字：本类的 <c>ResolveFamilyName</c> 只给裸氏，那是小家庭的
        ///     规格，用在宗族上会把「乐安国宣氏」覆写成「蓟」。
        ///     </para>
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ClanManager), nameof(ClanManager.newClan))]
        private static void NewClan_Postfix(Clan __result, Actor pFounder)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__result?.data == null || pFounder?.data == null) return;
            FamilyIdentitySyncService.SyncClanName(pFounder);
        }
    }
}
