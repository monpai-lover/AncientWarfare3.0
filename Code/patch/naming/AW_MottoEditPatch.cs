using AncientWarfare3.core.naming;
using HarmonyLib;

namespace AncientWarfare3.patch.naming
{
    [HarmonyPatch]
    internal static class AW_MottoEditPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomWindow), "applyInputMotto")]
        private static void KingdomMottoEdit_Postfix(string pInput)
        {
            AWLocalizedMottoService.CommitEdit(
                SelectedMetas.selected_kingdom?.data, pInput);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ClanWindow), "applyInputMotto")]
        private static void ClanMottoEdit_Postfix(string pInput)
        {
            AWLocalizedMottoService.CommitEdit(
                SelectedMetas.selected_clan?.data, pInput);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AllianceWindow), "applyInputMotto")]
        private static void AllianceMottoEdit_Postfix(string pInput)
        {
            AWLocalizedMottoService.CommitEdit(
                SelectedMetas.selected_alliance?.data, pInput);
        }
    }
}
