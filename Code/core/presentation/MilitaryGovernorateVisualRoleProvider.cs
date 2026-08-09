using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.presentation
{
    internal sealed class MilitaryGovernorateVisualRoleProvider :
        IActorVisualRoleProvider
    {
        public bool TryResolve(Actor pActor, out ActorVisualRole pRole)
        {
            pRole = ActorVisualRole.Default;
            if (pActor?.data == null) return false;

            Kingdom subject = pActor.kingdom;
            bool active = MilitaryGovernorateStore.TryGetRuntimeProjection(
                subject, out _, out long successorActorId);
            long actorId = pActor.data.id;
            long governorActorId = subject?.king?.data?.id ?? -1L;
            bool alive;
            try
            {
                alive = pActor.isAlive() && !pActor.isRekt();
            }
            catch
            {
                alive = false;
            }

            pRole = ActorVisualRoleRules.ResolveMilitaryGovernorateRole(
                active, alive, subject != null, actorId,
                governorActorId, successorActorId);
            return pRole != ActorVisualRole.Default;
        }
    }
}
