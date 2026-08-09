namespace AncientWarfare3.core.presentation
{
    internal static class MilitaryGovernorateAppearanceService
    {
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            ActorVisualRoleResolver.Register(
                new MilitaryGovernorateVisualRoleProvider());
        }

        public static void OnProjectionChanged(Kingdom pSubject,
            bool pWasActive, long pOldSuccessorActorId,
            bool pIsActive, long pNewSuccessorActorId)
        {
            long currentGovernorId = pSubject?.king?.data?.id ?? -1L;
            if (pWasActive != pIsActive)
                InvalidateActor(currentGovernorId);
            if (pOldSuccessorActorId != pNewSuccessorActorId)
            {
                if (!pIsActive || pOldSuccessorActorId != currentGovernorId)
                    InvalidateActor(pOldSuccessorActorId);
                if (!pIsActive || pNewSuccessorActorId != currentGovernorId)
                    InvalidateActor(pNewSuccessorActorId);
            }
        }

        public static void OnGovernorChanged(long pOldGovernorActorId,
            long pNewGovernorActorId)
        {
            InvalidateActor(pOldGovernorActorId);
            if (pNewGovernorActorId != pOldGovernorActorId)
                InvalidateActor(pNewGovernorActorId);
        }

        private static void InvalidateActor(long pActorId)
        {
            if (pActorId < 0) return;
            Actor actor;
            try { actor = World.world?.units?.get(pActorId); }
            catch { return; }
            try
            {
                if (actor?.data == null || actor.isRekt() ||
                    !actor.isAlive()) return;
                actor.clearGraphicsFully();
            }
            catch
            {
                // Role invalidation must remain safe during actor disposal.
            }
        }
    }
}
