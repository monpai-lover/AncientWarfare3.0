using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class RebellionCollapseSettlementService
    {
        private const string QueuePrefix =
            "rebellion_force_collapse_settlement:";

        public static bool QueueIfCollapsed(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(pWar)) return false;
            long warId = pWar.data.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + warId, DeferredWorkClass.Runtime,
                () => Process(warId));
            return true;
        }

        private static void Process(long pWarId)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            bool valid = war?.data != null;
            bool active = false;
            bool rebellion = false;
            bool rebelParticipantValid = false;
            bool warriorFactsReadable = false;
            int rebelWarriors = -1;
            int rebelReserves = -1;
            Kingdom rebel = null;
            try
            {
                active = valid && !war.hasEnded();
                rebellion = active && war.getAsset()?.rebellion == true;
                rebel = active ? war.getMainAttacker() : null;
                rebelParticipantValid = rebel?.data != null &&
                                        war.isAttacker(rebel);
                if (rebelParticipantValid)
                {
                    rebelWarriors = war.countAttackersWarriors();
                    warriorFactsReadable = rebelWarriors >= 0;
                    rebelReserves = CityReservePoolService.
                        CountAvailable(rebel);
                }
            }
            catch
            {
                warriorFactsReadable = false;
            }

            if (!RebellionForceCollapseRules.ShouldCollapse(
                    valid, active, rebellion, rebelParticipantValid,
                    warriorFactsReadable, rebelWarriors, rebelReserves))
                return;
            World.world?.wars?.endWar(war, WarWinner.Defenders);
        }
    }
}
