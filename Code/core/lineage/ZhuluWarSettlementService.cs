using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluWarSettlementService
    {
        private const string QueuePrefix = "zhulu_settlement:";
        private const int MaximumAttempts = 2;
        private const string ForceQueuePrefix =
            "zhulu_force_elimination_settlement:";

        [ThreadStatic] private static int _dedicatedSettlementDepth;
        private static readonly HashSet<long> QueuedWarIds =
            new HashSet<long>();
        private static readonly HashSet<long> ForceQueuedWarIds =
            new HashSet<long>();

        public static bool IsDedicatedSettlementActive =>
            _dedicatedSettlementDepth > 0;

        public static bool Queue(War war)
        {
            long warId = war?.data?.id ?? -1L;
            bool active = false;
            try { active = war?.data != null && !war.hasEnded(); }
            catch { }
            if (!ZhuluWarService.IsZhuluWar(war, requireActive: false) ||
                !ZhuluWarRules.CanQueueSettlement(war?.data != null,
                    active, QueuedWarIds.Contains(warId)))
                return false;
            QueuedWarIds.Add(warId);
            Enqueue(warId, attempt: 0);
            return true;
        }

        public static void ClearRuntime()
        {
            QueuedWarIds.Clear();
            ForceQueuedWarIds.Clear();
            _dedicatedSettlementDepth = 0;
        }

        public static bool QueueForceElimination(War pWar,
            WarForceEliminationDecision pDecision)
        {
            long warId = pWar?.data?.id ?? -1L;
            if (!ZhuluWarService.IsZhuluWar(pWar, requireActive: false) ||
                pDecision.Kind == WarForceEliminationDecisionKind.None)
                return false;
            if (!ForceQueuedWarIds.Add(warId)) return true;
            EnqueueForce(warId, 0);
            return true;
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            try
            {
                if (World.world?.wars == null) return;
                foreach (War war in World.world.wars)
                    if (ZhuluWarService.IsZhuluWar(war)) Queue(war);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Zhulu settlement rebuild failed: " +
                                    exception.Message);
            }
        }

        public static bool AbortFailedDeclaration(War war)
        {
            if (!ZhuluWarService.IsZhuluWar(war)) return false;
            try
            {
                _dedicatedSettlementDepth++;
                try { World.world?.wars?.endWar(war, WarWinner.Nobody); }
                finally
                {
                    _dedicatedSettlementDepth = Math.Max(0,
                        _dedicatedSettlementDepth - 1);
                }
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Zhulu declaration rollback failed for war " +
                    (war?.data?.id ?? -1L) + ": " + exception.Message);
                return false;
            }
        }

        private static void Enqueue(long warId, int attempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + warId, DeferredWorkClass.Runtime,
                () => Process(warId, attempt));
        }

        private static void EnqueueForce(long pWarId, int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                ForceQueuePrefix + pWarId, DeferredWorkClass.Runtime,
                () => ProcessForceElimination(pWarId, pAttempt));
        }

        private static void ProcessForceElimination(long pWarId,
            int pAttempt)
        {
            War war = FindWar(pWarId);
            if (war?.data == null || war.hasEnded() ||
                !ZhuluWarService.IsZhuluWar(war, requireActive: false) ||
                !WarForceEliminationSettlementService.
                    TryGetConfirmedDecision(war,
                        out WarForceEliminationDecision decision))
            {
                ForceQueuedWarIds.Remove(pWarId);
                return;
            }
            Kingdom attacker = war.getMainAttacker();
            Kingdom defender = war.getMainDefender();
            Kingdom winner = decision.Beneficiary == WarScoreSide.Attackers
                ? attacker
                : defender;
            Kingdom loser = winner == attacker ? defender : attacker;
            WarWinner result = decision.Beneficiary == WarScoreSide.Attackers
                ? WarWinner.Attackers
                : decision.Beneficiary == WarScoreSide.Defenders
                    ? WarWinner.Defenders
                    : WarWinner.Peace;
            try
            {
                bool surrender = decision.Kind ==
                                     WarForceEliminationDecisionKind.
                                         AttackersSurrender ||
                                 decision.Kind ==
                                     WarForceEliminationDecisionKind.
                                         DefendersSurrender;
                bool transferred = result == WarWinner.Peace ||
                    (surrender
                        ? WarForceSpecialSettlementService.
                            TransferAllCities(loser, winner)
                        : WarForceSpecialSettlementService.
                            TransferScoreAffordableCities(war, loser,
                                winner, decision.Score));
                if (!transferred)
                    throw new InvalidOperationException(
                        "zhulu force territory transfer failed");
                _dedicatedSettlementDepth++;
                try { World.world.wars.endWar(war, result); }
                finally
                {
                    _dedicatedSettlementDepth = Math.Max(0,
                        _dedicatedSettlementDepth - 1);
                }
                ForceQueuedWarIds.Remove(pWarId);
                QueuedWarIds.Remove(pWarId);
            }
            catch (Exception exception)
            {
                if (pAttempt < MaximumAttempts)
                {
                    EnqueueForce(pWarId, pAttempt + 1);
                    return;
                }
                ForceQueuedWarIds.Remove(pWarId);
                ModClass.LogWarning("Zhulu force elimination failed war=" +
                                    pWarId + ": " + exception.Message);
            }
        }

        private static void Process(long warId, int attempt)
        {
            War war = FindWar(warId);
            if (war?.data == null || !ZhuluWarService.IsZhuluWar(war))
            {
                QueuedWarIds.Remove(warId);
                return;
            }

            Kingdom attacker = war.getMainAttacker();
            Kingdom defender = war.getMainDefender();
            ZhuluWarOutcome outcome = ZhuluWarRules.ResolveOutcome(
                ZhuluWarService.IsValidRealm(attacker),
                CountCities(attacker),
                ZhuluWarService.IsValidRealm(defender),
                CountCities(defender));
            if (outcome == ZhuluWarOutcome.None ||
                outcome == ZhuluWarOutcome.Ambiguous)
            {
                QueuedWarIds.Remove(warId);
                return;
            }

            Kingdom winner = outcome == ZhuluWarOutcome.Attackers
                ? attacker
                : defender;
            Kingdom loser = outcome == ZhuluWarOutcome.Attackers
                ? defender
                : attacker;
            WarWinner result = outcome == ZhuluWarOutcome.Attackers
                ? WarWinner.Attackers
                : WarWinner.Defenders;
            try
            {
                if (!ZhuluWarService.IsValidRealm(winner))
                    throw new InvalidOperationException(
                        "zhulu winner is unavailable");
                foreach (City city in SnapshotCities(loser))
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom == winner) continue;
                    city.joinAnotherKingdom(winner, pCaptured: false,
                        pRebellion: false);
                    if (city.kingdom != winner)
                        throw new InvalidOperationException(
                            "zhulu city transfer was rejected");
                }

                _dedicatedSettlementDepth++;
                try { World.world.wars.endWar(war, result); }
                finally
                {
                    _dedicatedSettlementDepth = Math.Max(0,
                        _dedicatedSettlementDepth - 1);
                }
                QueuedWarIds.Remove(warId);
            }
            catch (Exception exception)
            {
                if (attempt < MaximumAttempts)
                {
                    Enqueue(warId, attempt + 1);
                    return;
                }
                QueuedWarIds.Remove(warId);
                ModClass.LogWarning("Zhulu settlement failed for war " +
                                    warId + ": " + exception.Message);
            }
        }

        private static War FindWar(long warId)
        {
            if (warId < 0L) return null;
            try { return World.world?.wars?.get(warId); }
            catch { return null; }
        }

        private static int CountCities(Kingdom kingdom)
        {
            return SnapshotCities(kingdom).Count;
        }

        private static List<City> SnapshotCities(Kingdom kingdom)
        {
            var result = new List<City>();
            if (kingdom?.data == null) return result;
            try
            {
                foreach (City city in kingdom.getCities())
                    if (city?.data != null && !city.isRekt())
                        result.Add(city);
            }
            catch { }
            return result;
        }
    }
}
