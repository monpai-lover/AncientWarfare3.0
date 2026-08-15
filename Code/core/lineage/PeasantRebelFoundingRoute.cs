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
            return MandateRebelService.EnterFoundingRoute(pContext.Rebel,
                pContext.Origin, pContext.FoundingCity);
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
    }
}
