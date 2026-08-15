namespace AncientWarfare3.core.lineage
{
    internal sealed class PeasantRebelFoundingRoute :
        IPeasantRebelRouteBehavior
    {
        public string Id => PeasantRebelRouteIds.Founding;
        public string RulerTitleKey => "";
        public string HeirTitleKey => "";

        public bool Enter(PeasantRebelRouteEntryContext pContext)
        {
            MandateRebelService.EnterFoundingRoute(pContext.Rebel,
                pContext.Origin, pContext.FoundingCity);
            return true;
        }

        public void OnKingdomYear(Kingdom pKingdom)
        {
            MandateRebelService.RunFoundingRouteYear(pKingdom);
        }

        public bool CanDeclareWar(Kingdom pKingdom)
        {
            return true;
        }

        public bool CanReceiveDirectWar(Kingdom pKingdom, Kingdom pAttacker)
        {
            return true;
        }

        public bool CanAcquireCity(Kingdom pKingdom, City pCity)
        {
            return true;
        }

        public string ComposeStateName(string pRoot)
        {
            return PeasantRebelRouteRules.ComposeName(pRoot, Id);
        }

        public void Exit(Kingdom pKingdom)
        {
        }

        public void OnKingdomDestroying(Kingdom pKingdom)
        {
        }

        internal static void RecordTransition(Kingdom pKingdom,
            Kingdom pOrigin)
        {
            if (pKingdom?.data == null) return;
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_bandit_converted"),
                HistoryTarget.Kingdom(pOrigin?.data != null
                    ? pOrigin
                    : pKingdom));
        }
    }
}
