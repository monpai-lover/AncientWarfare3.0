using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_DynasticReproductionPatch
    {
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(BabyHelper), nameof(BabyHelper.canMakeBabies))]
        private static IEnumerable<CodeInstruction>
            CanMakeBabies_Transpiler(
                IEnumerable<CodeInstruction> pInstructions)
        {
            MethodInfo vanillaLimit = AccessTools.Method(typeof(Actor),
                nameof(Actor.hasReachedOffspringLimit));
            MethodInfo targetedLimit = AccessTools.Method(
                typeof(AW_DynasticReproductionPatch),
                nameof(ReachedPersonalOffspringLimit));
            if (vanillaLimit == null || targetedLimit == null)
                throw new MissingMethodException(
                    "BabyHelper personal offspring gate missing");

            int matches = 0;
            foreach (CodeInstruction instruction in pInstructions)
            {
                if (!instruction.Calls(vanillaLimit))
                {
                    yield return instruction;
                    continue;
                }

                matches++;
                var replacement = new CodeInstruction(OpCodes.Call,
                    targetedLimit);
                replacement.labels.AddRange(instruction.labels);
                replacement.blocks.AddRange(instruction.blocks);
                yield return replacement;
            }

            if (matches != 1)
                throw new InvalidOperationException(
                    "BabyHelper.canMakeBabies offspring pattern changed: " +
                    matches);
        }

        private static bool ReachedPersonalOffspringLimit(Actor pActor)
        {
            if (pActor == null) return true;
            if (!pActor.hasReachedOffspringLimit()) return false;
            Actor partner = pActor.lover;
            return !DynasticMaleLineContinuityService.NeedsContinuation(
                       pActor) &&
                   !DynasticMaleLineContinuityService.NeedsContinuation(
                       partner);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BabyMaker), nameof(BabyMaker.makeBaby))]
        private static bool MakeBaby_Prefix(Actor pParent1,
            Actor pParent2, ref Actor __result,
            ref ActorSex pForcedSexType)
        {
            // pParent2 == null 是神迹/孢子等单亲路径（调用方链裸解引用返回值，
            // 设 __result=null 会直接崩）；只拦截两亲都在场的有性繁殖。
            if (pParent2 != null &&
                (SyntheticLevyService.IsSynthetic(pParent1) ||
                 SyntheticLevyService.IsSynthetic(pParent2)))
            {
                __result = null;
                return false;
            }
            if (pForcedSexType != ActorSex.None) return true;

            // 托管求子路径：双方恋人已激活 heir-birth 请求。
            if (NobleHeirPregnancyService.IsActiveLoverHeirBirth(
                    pParent1, pParent2))
            {
                pForcedSexType =
                    DynasticLoverConceptionRules.RollMakesMale(
                        Randy.randomInt(0, 100))
                        ? ActorSex.Male
                        : ActorSex.Female;
                return true;
            }

            // 广域贵族路径：国王/继承人/封地王子且无在世儿子，同样 70% 生男。
            if (ShouldApplyBroadMalePreference(pParent1, pParent2))
            {
                pForcedSexType =
                    DynasticBirthSexRules.RollMakesMale(
                        Randy.randomInt(0, 100))
                        ? ActorSex.Male
                        : ActorSex.Female;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BabyMaker), nameof(BabyMaker.makeBaby))]
        private static void MakeBaby_Postfix(Actor pParent1,
            Actor pParent2, Actor __result)
        {
            NobleHeirPregnancyService.OnLoverHeirChildBorn(
                __result, pParent1, pParent2);
            DynasticLivingSonIndexService.OnChildBorn(
                __result, pParent1, pParent2);

            // 生了女儿且父方仍缺继承人 → 立刻请求再孕，不等下一个年度周期。
            if (__result?.data != null && !__result.isSexMale())
            {
                TryRequestRetryForParent(pParent1);
                if (pParent2 != pParent1) TryRequestRetryForParent(pParent2);
            }
        }

        private static bool ShouldApplyBroadMalePreference(
            Actor pParent1, Actor pParent2)
        {
            // 从两亲中找出父方（男性），按他的身份决定是否触发偏好。
            Actor father = pParent1?.isSexMale() == true ? pParent1
                : pParent2?.isSexMale() == true ? pParent2
                : null;
            if (father?.data == null) return false;

            bool usesDynasticSystem;
            try
            {
                usesDynasticSystem =
                    LineageService.IsNativeXiaCultureActor(father) ||
                    LineageService.UsesAwLineageSystem(father);
            }
            catch { return false; }
            if (!usesDynasticSystem) return false;

            bool isKing = false;
            try { isKing = father.isKing(); } catch { }
            bool isHeir = false;
            try
            {
                isHeir = father.kingdom?.data != null &&
                         HeirService.IsCurrentHeir(father.kingdom, father);
            }
            catch { }
            bool isPrince = false;
            try { isPrince = FeudatoryService.IsActivePrince(father); }
            catch { }
            bool isSuccessor = false;
            try
            {
                isSuccessor = FeudatoryService.TryGetBySuccessor(
                    father.data.id, out FeudatorySnapshot _);
            }
            catch { }
            bool holdsPrinceTitle = false;
            try
            {
                NobleTitleSnapshot t = NobleRankService.ReadHot(father);
                holdsPrinceTitle = t.IsActive && t.Style == NobleTitleStyle.Male;
            }
            catch { }
            bool hasLivingSon =
                DynasticLivingSonIndexService.HasLivingSon(father);

            return DynasticBirthSexRules.ShouldPreferMale(usesDynasticSystem,
                isKing, isHeir, isPrince, isSuccessor, holdsPrinceTitle,
                hasLivingSon);
        }

        private static void TryRequestRetryForParent(Actor pParent)
        {
            if (pParent?.data == null || !pParent.isSexMale()) return;
            try
            {
                if (DynasticMaleLineContinuityService.NeedsContinuation(
                        pParent))
                    NobleHeirPregnancyService.RequestForHolder(pParent);
            }
            catch { }
        }
    }
}
