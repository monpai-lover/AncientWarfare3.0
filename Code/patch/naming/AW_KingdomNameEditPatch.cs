using System;
using AncientWarfare3.core.naming;
using HarmonyLib;

namespace AncientWarfare3.patch.naming
{
    [HarmonyPatch]
    internal static class AW_KingdomNameEditPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomWindow), "onNameChange")]
        private static void KingdomNameEdit_Prefix(KingdomWindow __instance)
        {
            AWLocalizedKingdomNameService.BeginEdit(
                __instance?.meta_object);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), "onNameChange")]
        private static void KingdomNameEdit_Postfix(KingdomWindow __instance,
            string pInput, bool __result)
        {
            Kingdom kingdom = __instance?.meta_object;
            if (AWLocalizedKingdomRenameRules.ShouldCommitManualEdit(
                    __result,
                    AWLocalizedKingdomNameService.IsEditing(kingdom)))
                AWLocalizedKingdomNameService.CommitEdit(
                    kingdom, pInput);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(KingdomWindow), "onNameChange")]
        private static Exception KingdomNameEdit_Finalizer(
            Exception __exception)
        {
            AWLocalizedKingdomNameService.EndEdit();
            return __exception;
        }
    }
}
