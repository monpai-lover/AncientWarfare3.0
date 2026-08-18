using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_DragOrderElementPatch
    {
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(DragOrderElement), "Start")]
        private static IEnumerable<CodeInstruction> Start_Transpiler(
            IEnumerable<CodeInstruction> pInstructions)
        {
            MethodInfo addCanvas = GenericAddComponent(typeof(Canvas));
            MethodInfo addRaycaster =
                GenericAddComponent(typeof(GraphicRaycaster));
            MethodInfo getCanvas = AccessTools.Method(
                typeof(AW_DragOrderElementPatch),
                nameof(GetOrAddCanvas));
            MethodInfo getRaycaster = AccessTools.Method(
                typeof(AW_DragOrderElementPatch),
                nameof(GetOrAddGraphicRaycaster));
            if (addCanvas == null || addRaycaster == null ||
                getCanvas == null || getRaycaster == null)
            {
                throw new MissingMethodException(
                    "DragOrderElement component methods are unavailable.");
            }

            int canvasMatches = 0;
            int raycasterMatches = 0;
            foreach (CodeInstruction instruction in pInstructions)
            {
                MethodInfo replacement = null;
                if (instruction.Calls(addCanvas))
                {
                    canvasMatches++;
                    replacement = getCanvas;
                }
                else if (instruction.Calls(addRaycaster))
                {
                    raycasterMatches++;
                    replacement = getRaycaster;
                }

                if (replacement == null)
                {
                    yield return instruction;
                    continue;
                }

                var rewritten = new CodeInstruction(
                    OpCodes.Call,
                    replacement);
                rewritten.labels.AddRange(instruction.labels);
                rewritten.blocks.AddRange(instruction.blocks);
                yield return rewritten;
            }

            if (canvasMatches != 1 || raycasterMatches != 1)
            {
                throw new InvalidOperationException(
                    "DragOrderElement.Start component pattern changed: canvas=" +
                    canvasMatches + ", raycaster=" + raycasterMatches);
            }
        }

        private static Canvas GetOrAddCanvas(GameObject pObject)
        {
            return pObject.GetComponent<Canvas>() ??
                   pObject.AddComponent<Canvas>();
        }

        private static GraphicRaycaster GetOrAddGraphicRaycaster(
            GameObject pObject)
        {
            return pObject.GetComponent<GraphicRaycaster>() ??
                   pObject.AddComponent<GraphicRaycaster>();
        }

        private static MethodInfo GenericAddComponent(Type pComponentType)
        {
            MethodInfo[] methods = typeof(GameObject).GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name == nameof(GameObject.AddComponent) &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 0)
                {
                    return method.MakeGenericMethod(pComponentType);
                }
            }

            return null;
        }
    }
}
