using UnityEngine;

namespace AncientWarfare3.core.performance
{
    internal static class AWCursorPresentationLifecycle
    {
        private static float _nextDiagnosticAt;

        internal static void ClearCursorPowerPool()
        {
            if (!AWPerformanceSettings.EnableFramePriorityScheduler ||
                AssetManager.quantum_sprites == null)
            {
                return;
            }

            QuantumSpriteAsset cursorAsset =
                AssetManager.quantum_sprites.get("cursor_power");
            cursorAsset?.group_system?.clearFull();
            LogSelection(cursorAsset);
        }

        internal static void Reset()
        {
            _nextDiagnosticAt = 0f;
            QuantumSpriteAsset cursorAsset =
                AssetManager.quantum_sprites?.get("cursor_power");
            cursorAsset?.group_system?.clearFull();
        }

        private static void LogSelection(QuantumSpriteAsset pCursorAsset)
        {
            if (!AWPerformanceSettings.EnableSchedulerDiagnostics ||
                Time.unscaledTime < _nextDiagnosticAt)
            {
                return;
            }

            _nextDiagnosticAt = Time.unscaledTime + 1f;
            PowerButton button =
                PowerButtonSelector.instance?.selectedButton;
            string powerId = button?.godPower?.id ?? "<none>";
            string iconId = button?.icon?.sprite?.name ?? "<none>";
            int activeCount =
                pCursorAsset?.group_system?.countActive() ?? 0;
            ModClass.LogInfo(
                "[AW3 FramePriority] cursor power=" + powerId +
                " icon=" + iconId +
                " active=" + activeCount +
                " frame=" + Time.frameCount);
        }
    }
}
