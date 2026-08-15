namespace AncientWarfare3.core.lineage
{
    internal readonly struct PeasantRebelRouteEntryContext
    {
        internal PeasantRebelRouteEntryContext(Kingdom pRebel,
            Kingdom pOrigin, City pFoundingCity, Actor pFounder)
        {
            Rebel = pRebel;
            Origin = pOrigin;
            FoundingCity = pFoundingCity;
            Founder = pFounder;
        }

        public Kingdom Rebel { get; }
        public Kingdom Origin { get; }
        public City FoundingCity { get; }
        public Actor Founder { get; }
    }

    internal interface IPeasantRebelRouteBehavior
    {
        string Id { get; }
        bool Enter(PeasantRebelRouteEntryContext pContext);
        void OnKingdomYear(Kingdom pKingdom);
        bool CanDeclareWar(Kingdom pKingdom);
        bool CanReceiveDirectWar(Kingdom pKingdom, Kingdom pAttacker);
        bool CanAcquireCity(Kingdom pKingdom, City pCity);
        string ComposeStateName(string pRoot);
        string RulerTitleKey { get; }
        string HeirTitleKey { get; }
        void Exit(Kingdom pKingdom);
        void OnKingdomDestroying(Kingdom pKingdom);
    }
}
