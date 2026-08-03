using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.presentation;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ArmyMapInformationMinimapPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawArmies")]
        private static void DrawArmies_Postfix(QuantumSpriteAsset pAsset)
        {
            if (pAsset?.group_system == null ||
                ArmyRtsRuntimeMode.Current != ArmyRtsMode.On ||
                !AWPerformanceSettings.ShowArmyMapInformation) return;

            Kingdom selected = SelectedMetas.selected_kingdom;
            if (selected?.data == null || selected.isRekt()) return;

            // drawArmies has already created these native p_mapArmy flags.
            // Reuse their built-in TextMesh so no second flag is rendered.
            QuantumSprite[] flags = pAsset.group_system.getAll();
            int activeCount = pAsset.group_system.countActive();
            int flagIndex = 0;
            var armies = World.world?.armies?.list;
            if (armies == null) return;

            for (int index = 0; index < armies.Count &&
                                flagIndex < activeCount; index++)
            {
                Army army = armies[index];
                if (!TryGetNativeFlagCaptain(army, out Actor captain)) continue;

                QuantumSpriteWithText flag = flags[flagIndex++] as
                    QuantumSpriteWithText;
                if (flag?.text == null || captain.kingdom != selected) continue;

                if (!ArmyMapInformationService.TryPopulateNativeFlagText(
                        army, captain, flag))
                    flag.text.gameObject.SetActive(false);
            }
        }

        private static bool TryGetNativeFlagCaptain(Army pArmy,
            out Actor pCaptain)
        {
            pCaptain = null;
            try
            {
                if (pArmy?.data == null || !pArmy.hasCaptain()) return false;
                pCaptain = pArmy.getCaptain();
                return pCaptain?.data != null && !pCaptain.isRekt() &&
                       !pCaptain.isInMagnet() &&
                       pCaptain.current_zone?.visible == true &&
                       pCaptain.isKingdomCiv();
            }
            catch
            {
                pCaptain = null;
                return false;
            }
        }
    }
}
