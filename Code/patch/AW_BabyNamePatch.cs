using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     修"所有贵族女性姓名顺序错(显示姓+名,应名+姓)"。
    ///
    ///     根因:出生命名走 AW_BirthPatch 的 applyParentsMeta Postfix → OnActorBornWithParents → ApplyDisplayName,
    ///     但 BabyMaker.makeBaby 在 `applyParentsMeta`(=那个钩点)**之后** 才设 baby 性别(BabyMaker.cs:224-240)。
    ///     此刻 data.sex 还是默认 Male → isSexMale() 恒 true → 女性也走男性"姓+名"分支,显示名固化错误。
    ///
    ///     修:Postfix BabyMaker.makeBaby(整个出生完成、**性别已最终确定**后)对 Xia baby 重算一次显示名,
    ///     走正确性别分支(女=名+姓)。makeBaby 是有性繁殖收口;ApplyDisplayName 幂等;末尾 setName 在 name 非空时
    ///     不触发 generateNewName,与 AW_NameProtectPatch 不冲突。继承/亲子边/单名仍由 applyParentsMeta 钩负责。
    /// </summary>
    [HarmonyPatch]
    public static class AW_BabyNamePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(ActorManager),
            nameof(ActorManager.createBabyActorFromData))]
        public static void CreateBabyActorFromData_Postfix(
            City pCity, Actor __result)
        {
            // Vanilla receives pCity but ignores it until applyParentsMeta. In
            // Large mode this leaves a newborn in its default/wild kingdom
            // while the actor batch continues; establish the explicit birth
            // affiliation at the creation boundary.
            ActorBirthAffiliationService.Reconcile(__result, pCity);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BabyMaker), nameof(BabyMaker.makeBaby))]
        public static void MakeBaby_Postfix(Actor pParent1, Actor pParent2, Actor __result)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__result?.data == null) return;
            ActorBirthAffiliationService.Reconcile(__result, pParent1, pParent2);
            if (!LineageService.IsXia(__result) &&
                !LineageService.UsesAwLineageSystem(__result) &&
                !LineageService.HasTraceableArchive(__result)) return;
            LineageService.ApplyDisplayName(__result); // 性别已定 → 重算走正确性别分支
            LineageService.RefreshArchivedIdentity(__result);
            HeirService.RefreshForNewRoyalChild(__result, pParent1, pParent2);
            DynasticTitleService.OnChildBorn(__result, pParent1, pParent2);
        }
    }
}
