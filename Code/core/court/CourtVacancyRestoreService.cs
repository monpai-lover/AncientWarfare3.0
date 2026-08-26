using System;

namespace AncientWarfare3.core.court
{
    internal sealed class CourtVacancyRestoreGenerationGate
    {
        private int _generation;
        private bool _completed;

        internal int BeginGeneration()
        {
            unchecked { _generation++; }
            _completed = false;
            return _generation;
        }

        internal bool TryComplete(int pGeneration)
        {
            if (_completed || pGeneration != _generation) return false;
            _completed = true;
            return true;
        }
    }

#if !AW3_RULES_TESTS
    internal static class CourtVacancyRestoreService
    {
        private static readonly CourtVacancyRestoreGenerationGate Gate =
            new CourtVacancyRestoreGenerationGate();

        internal static int BeginGeneration()
        {
            CourtVacancyReconciliationService.ClearRuntime();
            return Gate.BeginGeneration();
        }

        internal static void RebuildRuntime(int pGeneration)
        {
            if (!Gate.TryComplete(pGeneration) ||
                World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                CourtVacancyReconciliationService.RefreshKingdomDefinitions(
                    kingdom);
            }
        }
    }
#endif
}
