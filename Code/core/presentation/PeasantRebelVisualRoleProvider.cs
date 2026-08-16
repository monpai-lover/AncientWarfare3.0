using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.presentation
{
    internal sealed class PeasantRebelVisualRoleProvider :
        IActorVisualRoleProvider
    {
        public bool TryResolve(Actor pActor, out ActorVisualRole pRole)
        {
            pRole = ActorVisualRole.Default;
            if (pActor?.data == null) return false;

            Kingdom kingdom = pActor.kingdom;
            bool rebelActive = MandateRebelService.IsRebelKingdom(kingdom);
            long kingActorId = kingdom?.king?.data?.id ?? -1L;
            long heirActorId = -1L;
            kingdom?.data?.get(LineageKeys.KINGDOM_HEIR_ID,
                out heirActorId, -1L);
            bool alive;
            try { alive = pActor.isAlive() && !pActor.isRekt(); }
            catch { alive = false; }

            pRole = ActorVisualRoleRules.ResolvePeasantRebelRole(
                rebelActive, alive, kingdom != null &&
                                    pActor.kingdom == kingdom,
                pActor.data.id, kingActorId, heirActorId);
            return pRole != ActorVisualRole.Default;
        }
    }
}
