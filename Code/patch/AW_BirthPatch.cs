using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     出生归档,两个互补钩点(经 BabyMaker 时序核实):
    ///
    ///     1. Postfix Actor.newCreature(Actor.cs:978,internal)——
    ///        所有 actor 创建的通用末尾步骤,但此时**父母尚未设**(BabyMaker 先 createBaby→newCreature
    ///        再 setParent1/2)。故这里只做基础初始化(单名+初始档案),不继承。覆盖世界 spawn / 奇迹的开国代。
    ///
    ///     2. Postfix BabyHelper.applyParentsMeta(BabyHelper.cs:50,public static,p1/p2/baby)——
    ///        繁殖出生时 setParent1/2 已完成、且直接给父母对象。这里做父系继承+亲子边+重算显示名。
    ///        被 BabyMaker 和孢子繁殖(SubspeciesTraitLibrary)共用,覆盖全部繁殖路径。
    ///
    ///     均 Postfix 注入,不接管原方法(Cultiway 哲学:优先 Postfix)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_BirthPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "newCreature")]
        public static void NewCreature_Postfix(Actor __instance)
        {
            if (__instance?.data == null) return;
            if (!LineageService.IsXia(__instance)) return;

            LineageService.OnActorBorn(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BabyHelper), nameof(BabyHelper.applyParentsMeta))]
        public static void ApplyParentsMeta_Postfix(Actor pParent1, Actor pParent2, Actor pBaby)
        {
            if (pBaby?.data == null) return;
            if (!LineageService.IsXia(pBaby)) return;

            LineageService.OnActorBornWithParents(pBaby, pParent1, pParent2);
        }
    }
}
