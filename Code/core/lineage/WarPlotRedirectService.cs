using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class WarPlotRedirectService
    {
        private static PlotTryToStart _originalNewWarStart;
        private static PlotTryToStart _originalAllianceCreateStart;
        private static PlotTryToStart _originalAllianceJoinStart;
        private static PlotTryToStart _originalAllianceDestroyStart;
        private static PlotTryToStart _originalStopWarStart;
        private static bool _installed;

        public static void Init()
        {
            if (!_installed)
            {
                Install(WarPlotRedirectRules.NewWarPlotId,
                    TryStartNewWarAsDecision, ref _originalNewWarStart);
                Install(WarPlotRedirectRules.AllianceCreatePlotId,
                    TryStartAllianceProposal, ref _originalAllianceCreateStart);
                Install(WarPlotRedirectRules.AllianceJoinPlotId,
                    TryStartAllianceJoinProposal,
                    ref _originalAllianceJoinStart);
                Install(WarPlotRedirectRules.AllianceDestroyPlotId,
                    TryStartAllianceEndProposal,
                    ref _originalAllianceDestroyStart);
                Install(WarPlotRedirectRules.StopWarPlotId,
                    TryStartPeaceProposal, ref _originalStopWarStart);
                _installed = true;
            }
            SweepExistingPlots();
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!_installed) Init();
            if (!ShouldRedirectKingdom(pKingdom)) return;

            Actor king = pKingdom.king;
            Plot plot = king?.plot;
            string plotId = PlotId(plot);
            if (!WarPlotRedirectRules.IsManagedDiplomacyPlot(plotId)) return;
            if (plotId == WarPlotRedirectRules.NewWarPlotId)
                WarDecisionAI.TryQueueFromVanillaWarPlot(king,
                    plot.target_kingdom);
            else
                RedirectActivePlot(king, plotId);
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

        private static bool TryStartAllianceProposal(Actor pActor,
            PlotAsset pPlotAsset, bool pForced)
        {
            Kingdom requester = pActor?.kingdom;
            if (!IsCivilKingdom(requester))
                return _originalAllianceCreateStart?.Invoke(
                    pActor, pPlotAsset, pForced) == true;
            Kingdom responder = null;
            try { responder = DiplomacyHelpers.getAllianceTarget(requester); }
            catch { }
            return TryCreateAiProposal(requester, responder,
                DiplomacyProposalType.Alliance, -1L);
        }

        private static bool TryStartAllianceJoinProposal(Actor pActor,
            PlotAsset pPlotAsset, bool pForced)
        {
            Kingdom requester = pActor?.kingdom;
            if (!IsCivilKingdom(requester))
                return _originalAllianceJoinStart?.Invoke(
                    pActor, pPlotAsset, pForced) == true;
            return TryCreateAiProposal(requester,
                FindAllianceSponsor(requester),
                DiplomacyProposalType.Alliance, -1L);
        }

        private static bool TryStartAllianceEndProposal(Actor pActor,
            PlotAsset pPlotAsset, bool pForced)
        {
            Kingdom requester = pActor?.kingdom;
            if (!IsCivilKingdom(requester))
                return _originalAllianceDestroyStart?.Invoke(
                    pActor, pPlotAsset, pForced) == true;
            return TryCreateAiProposal(requester,
                FindOtherAllianceMember(requester),
                DiplomacyProposalType.EndAlliance, -1L);
        }

        private static bool TryStartPeaceProposal(Actor pActor,
            PlotAsset pPlotAsset, bool pForced)
        {
            Kingdom requester = pActor?.kingdom;
            if (!IsCivilKingdom(requester))
                return _originalStopWarStart?.Invoke(
                    pActor, pPlotAsset, pForced) == true;
            War war = FindPeaceWar(requester);
            return TryCreateAiProposal(requester,
                FindWarOpponent(war, requester),
                DiplomacyProposalType.Peace, war?.data?.id ?? -1L);
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

        private static string PlotId(Plot pPlot)
        {
            if (pPlot == null) return "";
            try { return pPlot.getAsset()?.id ?? pPlot.data?.plot_type_id ?? ""; }
            catch { return ""; }
        }

        private static void RedirectActivePlot(Actor pActor, string pPlotId)
        {
            switch (pPlotId)
            {
                case WarPlotRedirectRules.AllianceCreatePlotId:
                    TryStartAllianceProposal(pActor,
                        AssetManager.plots_library?.get(pPlotId), false);
                    break;
                case WarPlotRedirectRules.AllianceJoinPlotId:
                    TryStartAllianceJoinProposal(pActor,
                        AssetManager.plots_library?.get(pPlotId), false);
                    break;
                case WarPlotRedirectRules.AllianceDestroyPlotId:
                    TryStartAllianceEndProposal(pActor,
                        AssetManager.plots_library?.get(pPlotId), false);
                    break;
                case WarPlotRedirectRules.StopWarPlotId:
                    TryStartPeaceProposal(pActor,
                        AssetManager.plots_library?.get(pPlotId), false);
                    break;
            }
        }

        private static void Install(string pPlotId, PlotTryToStart pRedirect,
            ref PlotTryToStart pOriginal)
        {
            PlotAsset asset = AssetManager.plots_library?.get(pPlotId);
            if (asset == null) return;
            pOriginal = asset.try_to_start_advanced;
            asset.try_to_start_advanced = pRedirect;
        }

        private static bool TryCreateAiProposal(Kingdom pRequester,
            Kingdom pResponder, DiplomacyProposalType pType, long pWarId)
        {
            return pRequester?.data != null && pResponder?.data != null &&
                   DiplomacyProposalService.TryCreate(pRequester, pResponder,
                       pType, pPlayerInitiated: false, pWarId,
                       out _, out _);
        }

        private static Kingdom FindAllianceSponsor(Kingdom pRequester)
        {
            if (pRequester?.data == null || World.world?.alliances == null)
                return null;
            try
            {
                foreach (Alliance alliance in World.world.alliances)
                {
                    if (alliance?.data == null || alliance.hasWars() ||
                        !alliance.canJoin(pRequester)) continue;
                    foreach (Kingdom member in alliance.kingdoms_hashset)
                        if (member?.data != null) return member;
                }
            }
            catch { }
            return null;
        }

        private static Kingdom FindOtherAllianceMember(Kingdom pRequester)
        {
            try
            {
                Alliance alliance = pRequester?.getAlliance();
                if (alliance?.kingdoms_hashset == null) return null;
                foreach (Kingdom member in alliance.kingdoms_hashset)
                    if (member?.data != null && member != pRequester)
                        return member;
            }
            catch { }
            return null;
        }

        private static War FindPeaceWar(Kingdom pRequester)
        {
            try
            {
                foreach (War war in pRequester.getWars())
                    if (war?.data != null && !war.hasEnded() &&
                        war.getAsset()?.can_end_with_plot == true)
                        return war;
            }
            catch { }
            return null;
        }

        private static Kingdom FindWarOpponent(War pWar,
            Kingdom pRequester)
        {
            if (pWar?.data == null || pRequester?.data == null) return null;
            try
            {
                return pWar.isAttacker(pRequester)
                    ? pWar.getMainDefender()
                    : pWar.isDefender(pRequester)
                        ? pWar.getMainAttacker()
                        : null;
            }
            catch { return null; }
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
