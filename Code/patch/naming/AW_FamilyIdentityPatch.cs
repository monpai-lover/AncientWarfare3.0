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
        ///     开宗时同步宗族名。原版 <c>Clan.newClan</c> 末尾同样调
        ///     <c>generateName(MetaType.Clan, ...)</c> 随机取名 —— 不接管的话
        ///     新宗族一出生就顶着随机洋名，而我们在归档、王室认定、继承多处
        ///     读这个对象。
        ///
        ///     挂 manager 而不是 <c>Clan.newClan</c>：后者跑完时创建者还没被
        ///     设成族长，<c>getChief()</c> 取不到人，同步条件不成立。
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
