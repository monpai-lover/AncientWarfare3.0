using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class WarPlotRedirectService
    {
        private static PlotTryToStart _originalNewWarStart;
        private static bool _installed;

        public static void Init()
        {
            PlotAsset asset = AssetManager.plots_library?.get(WarPlotRedirectRules.NewWarPlotId);
            if (asset == null) return;

            if (!_installed)
            {
                _originalNewWarStart = asset.try_to_start_advanced;
                _installed = true;
            }

            asset.try_to_start_advanced = TryStartNewWarAsDecision;
            SweepExistingPlots();
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!_installed) Init();
            if (!ShouldRedirectKingdom(pKingdom)) return;

            Actor king = pKingdom.king;
            Plot plot = king?.plot;
            if (!IsNewWarPlot(plot)) return;

            Kingdom target = plot.target_kingdom;
            WarDecisionAI.TryQueueFromVanillaWarPlot(king, target);
            CancelPlot(plot, king);
        }

        public static void SweepExistingPlots()
        {
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
                OnKingdomYear(kingdom);
        }

        public static bool TryConsumeActiveNewWarPlot(Actor pActor)
        {
            Plot plot = pActor?.plot;
            if (!IsNewWarPlot(plot)) return false;
            Kingdom kingdom = pActor.kingdom;
            if (!WarPlotRedirectRules.ShouldInterceptActiveNewWarPlot(
                    WarPlotRedirectRules.NewWarPlotId,
                    IsCivilKingdom(kingdom),
                    KingdomPolicyService.CanUsePolicySystem(kingdom),
                    WarDecisionService.IsAw3AllowedWarStart))
                return false;

            Kingdom target = plot.target_kingdom;
            WarDecisionAI.TryQueueFromVanillaWarPlot(pActor, target);
            CancelPlot(plot, pActor);
            return true;
        }

        private static bool TryStartNewWarAsDecision(Actor pActor, PlotAsset pPlotAsset, bool pForced)
        {
            Kingdom kingdom = pActor?.kingdom;
            if (!WarPlotRedirectRules.ShouldRedirectNewWarPlot(
                    pPlotAsset?.id,
                    IsCivilKingdom(kingdom),
                    KingdomPolicyService.CanUsePolicySystem(kingdom),
                    WarDecisionService.IsAw3AllowedWarStart))
            {
                return _originalNewWarStart?.Invoke(pActor, pPlotAsset, pForced) == true;
            }

            WarDecisionAI.TryQueueFromVanillaWarPlot(pActor, GetVanillaWarTarget(kingdom));
            return true;
        }

        private static Kingdom GetVanillaWarTarget(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            try { return DiplomacyHelpers.getWarTarget(pKingdom); }
            catch { return null; }
        }

        private static bool ShouldRedirectKingdom(Kingdom pKingdom)
        {
            return WarPlotRedirectRules.ShouldRedirectNewWarPlot(
                WarPlotRedirectRules.NewWarPlotId,
                IsCivilKingdom(pKingdom),
                KingdomPolicyService.CanUsePolicySystem(pKingdom),
                WarDecisionService.IsAw3AllowedWarStart);
        }

        private static bool IsNewWarPlot(Plot pPlot)
        {
            if (pPlot == null) return false;
            try
            {
                string id = pPlot.getAsset()?.id ?? pPlot.data?.plot_type_id ?? "";
                return id == WarPlotRedirectRules.NewWarPlotId;
            }
            catch { return false; }
        }

        private static bool IsCivilKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() && pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static void CancelPlot(Plot pPlot, Actor pActor)
        {
            try
            {
                World.world?.plots?.cancelPlot(pPlot);
                return;
            }
            catch { }

            try { pActor?.leavePlot(); }
            catch { }
        }
    }
}
