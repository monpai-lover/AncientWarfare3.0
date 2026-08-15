using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehWarArmyReturnTarget : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!WarArmyReturnService.TryGetTarget(pActor,
                    out WorldTile target))
            {
                ArmyRtsMovementDiagnostic.Log("return",
                    "return_target_rejected", pActor,
                    "result=False reason=no_friendly_home_target");
                pActor?.makeWait(0.2f);
                return BehResult.RepeatStep;
            }
            if (WarArmyReturnService.TryHandleTransport(pActor, target))
            {
                ArmyRtsMovementDiagnostic.Log("return",
                    "return_transport_yield", pActor,
                    "target_tile=" + target.data.tile_id);
                pActor.makeWait(0.5f);
                return BehResult.RepeatStep;
            }
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }
}
