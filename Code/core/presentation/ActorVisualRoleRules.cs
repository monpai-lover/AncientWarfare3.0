namespace AncientWarfare3.core.presentation
{
    public enum ActorVisualRole
    {
        Default = 0,
        Civilian = 1,
        Warrior = 2,
        Leader = 3,
        King = 4
    }

    public static class ActorVisualRoleRules
    {
        public static bool ShouldRenderCustomHead(bool pIsBaby)
        {
            return !pIsBaby;
        }

        public static bool IsKing(ActorVisualRole pRole,
            bool pVanillaKing)
        {
            if (pRole == ActorVisualRole.Default) return pVanillaKing;
            return pRole == ActorVisualRole.King;
        }

        public static bool IsLeader(ActorVisualRole pRole,
            bool pVanillaLeader)
        {
            if (pRole == ActorVisualRole.Default) return pVanillaLeader;
            return pRole == ActorVisualRole.Leader;
        }

        public static bool IsWarrior(ActorVisualRole pRole,
            bool pVanillaWarrior)
        {
            if (pRole == ActorVisualRole.Default) return pVanillaWarrior;
            return pRole == ActorVisualRole.Warrior;
        }

        public static ActorVisualRole ResolveMilitaryGovernorateRole(
            bool pProjectionActive, bool pActorAlive,
            bool pActorKingdomMatches, long pActorId,
            long pGovernorActorId, long pSuccessorActorId)
        {
            if (!pProjectionActive || !pActorAlive ||
                !pActorKingdomMatches || pActorId < 0)
                return ActorVisualRole.Default;
            return pActorId == pGovernorActorId ||
                   pActorId == pSuccessorActorId
                ? ActorVisualRole.Warrior
                : ActorVisualRole.Default;
        }

        public static ActorVisualRole ResolvePeasantRebelRole(
            bool pRebelActive, bool pActorAlive,
            bool pActorKingdomMatches, long pActorId,
            long pKingActorId, long pHeirActorId)
        {
            if (!pRebelActive || !pActorAlive ||
                !pActorKingdomMatches || pActorId < 0)
                return ActorVisualRole.Default;
            return pActorId == pKingActorId || pActorId == pHeirActorId
                ? ActorVisualRole.Warrior
                : ActorVisualRole.Default;
        }
    }
}
