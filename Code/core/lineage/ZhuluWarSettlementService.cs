using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluWarSettlementService
    {
        private const string QueuePrefix = "zhulu_settlement:";
        private const int MaximumAttempts = 2;

        [ThreadStatic] private static int _dedicatedSettlementDepth;
        private static readonly HashSet<long> QueuedWarIds =
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
            _dedicatedSettlementDepth = 0;
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

        internal static bool ApplyNoForce(War pWar, Kingdom pDefeated,
            Kingdom pWinner)
        {
            if (!ZhuluWarService.IsZhuluWar(pWar) ||
                pDefeated?.data == null || pWinner?.data == null ||
                pWar.hasEnded()) return false;
            try
            {
                WarTerritoryService.TransferLargestConnectedDefeatedTerritory(
                    pWar, pDefeated, pWinner);
                WarWinner result = pWar.isAttacker(pWinner)
                    ? WarWinner.Attackers
                    : WarWinner.Defenders;
                _dedicatedSettlementDepth++;
                try { World.world?.wars?.endWar(pWar, result); }
                finally
                {
                    _dedicatedSettlementDepth = Math.Max(0,
                        _dedicatedSettlementDepth - 1);
                }
                return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Zhulu no-force settlement failed: " +
                                    exception.Message);
                return false;
            }
        }

        private static void Enqueue(long warId, int attempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + warId, DeferredWorkClass.Runtime,
                () => Process(warId, attempt));
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
                // Zhulu is a no-negotiation war, but its surrender result is
                // still bounded to the defeated side's largest connected
                // mainland.  Disconnected islands/enclaves remain available
                // for a later war instead of being silently erased.
                WarTerritoryService.TransferLargestConnectedDefeatedTerritory(
                    war, loser, winner);

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
