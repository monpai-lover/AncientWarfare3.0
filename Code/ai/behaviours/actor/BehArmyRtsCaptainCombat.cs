using AncientWarfare3.core.lineage;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    // Keeps captain and member combat in bounded tactical loops. Strategic
    // movement remains owned by the formation controller; these behaviours
    // only validate a nearby target and issue an attack.
    public sealed class BehArmyRtsCaptainCombat : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyRtsControllerService.HasCaptainMission(pActor))
            {
                ArmyRtsControllerService.ReleaseActor(pActor);
                return BehResult.Stop;
            }

            Actor target = pActor.beh_actor_target?.a;
            if (!ArmyRtsControllerService.IsValidCaptainCombatTarget(
                    pActor, target))
                target = ArmyRtsControllerService.FindCaptainCombatTarget(
                    pActor);

            if (!ArmyRtsControllerService.IsValidCaptainCombatTarget(
                    pActor, target))
            {
                pActor.beh_actor_target = null;
                pActor.makeWait(0.15f);
                return BehResult.RepeatStep;
            }

            pActor.beh_actor_target = target;
            return BehResult.Continue;
        }
    }

    public sealed class BehArmyRtsCaptainAttack : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            Actor target = pActor?.beh_actor_target?.a;
            if (!ArmyRtsControllerService.IsValidAssignedCombatTarget(
                    pActor, target))
            {
                if (pActor != null) pActor.beh_actor_target = null;
                pActor?.makeWait(0.15f);
                return BehResult.Continue;
            }
            try
            {
                if (pActor.isInAttackRange(target))
                    pActor.tryToAttack(target);
            }
            catch { }
            return BehResult.Continue;
        }
    }

    public sealed class BehArmyRtsMemberCombat : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyRtsControllerService.HasMemberCombatMission(pActor))
            {
                ArmyRtsControllerService.ReleaseActor(pActor);
                return BehResult.Stop;
            }

            Actor target = pActor.beh_actor_target?.a;
            if (!ArmyRtsControllerService.IsValidMemberCombatTarget(
                    pActor, target))
                target = pActor.attack_target?.a;
            if (!ArmyRtsControllerService.IsValidMemberCombatTarget(
                    pActor, target))
                target = ArmyRtsControllerService.FindCaptainCombatTarget(
                    pActor);
            if (!ArmyRtsControllerService.IsValidMemberCombatTarget(
                    pActor, target))
            {
                pActor.beh_actor_target = null;
                pActor.makeWait(0.15f);
                return BehResult.RepeatStep;
            }

            pActor.beh_actor_target = target;
            return BehResult.Continue;
        }
    }

    public sealed class BehArmyRtsSiegeCombat : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyRtsControllerService.HasActiveTargetCitySiege(pActor))
            {
                ArmyRtsControllerService.ReleaseActor(pActor);
                return BehResult.Stop;
            }

            Actor target = pActor.beh_actor_target?.a;
            if (!ArmyRtsControllerService.IsValidSiegeCombatTarget(
                    pActor, target))
                target = ArmyRtsControllerService.FindSiegeCombatTarget(
                    pActor);
            if (!ArmyRtsControllerService.IsValidSiegeCombatTarget(
                    pActor, target))
            {
                pActor.beh_actor_target = null;
                pActor.makeWait(0.15f);
                return BehResult.RepeatStep;
            }

            pActor.beh_actor_target = target;
            return BehResult.Continue;
        }
    }

    public sealed class BehArmyRtsSiegeAdvance : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (!ArmyRtsControllerService.TryGetSiegeAdvanceTarget(pActor,
                    out WorldTile target))
            {
                pActor?.makeWait(0.15f);
                return BehResult.RepeatStep;
            }
            if (target?.data == null ||
                pActor?.current_tile?.data == null ||
                target.data.tile_id == pActor.current_tile.data.tile_id)
            {
                pActor.makeWait(0.15f);
                return BehResult.RepeatStep;
            }
            if (ArmyRtsControllerService.ShouldHandleCaptainTransport(
                    pActor) &&
                ArmyRtsTransportService.TryHandleActor(
                    pActor, target, pMayBegin: true,
                    pForceTransport: true))
            {
                pActor.makeWait(0.2f);
                return BehResult.RepeatStep;
            }
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }
}
