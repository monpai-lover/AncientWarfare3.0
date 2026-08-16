using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.presentation
{
    internal static class PeasantRebelAppearanceService
    {
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            ActorVisualRoleResolver.Register(
                new PeasantRebelVisualRoleProvider());
        }

        public static void OnProjectionChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            InvalidateActor(pKingdom.king?.data?.id ?? -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                out long heirActorId, -1L);
            InvalidateActor(heirActorId);
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
                // Presentation invalidation cannot interrupt government state.
            }
        }
    }
}
