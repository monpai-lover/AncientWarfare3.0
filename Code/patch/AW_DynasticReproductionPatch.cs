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
            if (SyntheticLevyService.IsSynthetic(pParent1) ||
                SyntheticLevyService.IsSynthetic(pParent2))
            {
                __result = null;
                return false;
            }
            if (pForcedSexType != ActorSex.None ||
                !NobleHeirPregnancyService.IsActiveLoverHeirBirth(
                    pParent1, pParent2)) return true;
            pForcedSexType =
                DynasticLoverConceptionRules.RollMakesMale(
                    Randy.randomInt(0, 100))
                    ? ActorSex.Male
                    : ActorSex.Female;
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
        }
    }
}
