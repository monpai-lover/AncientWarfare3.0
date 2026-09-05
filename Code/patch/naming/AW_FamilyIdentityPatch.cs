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
    ///     宗族（Clan）命名完全由中文名模组负责，不在这里干预。
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
            Actor anchor = pActor?.data != null
                ? pActor
                : FamilyIdentitySyncService.ResolveAnchor(__result);
            FamilyIdentitySyncService.SyncFamilyName(__result, anchor);
        }
    }
}
