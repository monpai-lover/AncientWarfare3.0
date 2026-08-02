using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Idempotent main-thread settlement for wars that cannot use ordinary
    /// peace proposals.  Only the defeated side's largest connected territory
    /// is transferred; the war is then closed exactly once.
    /// </summary>
    internal static class WarTotalWarSurrenderService
    {
        private static readonly HashSet<long> Applied =
            new HashSet<long>();

        public static bool Apply(War pWar, Kingdom pDefeated,
            Kingdom pWinner)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pDefeated?.data == null ||
                pWinner?.data == null || pWar.hasEnded() ||
                pDefeated == pWinner) return false;
            long warId = pWar.data.id;
            if (!Applied.Add(warId)) return true;
            try
            {
                WarTerritoryService.TransferLargestConnectedDefeatedTerritory(
                    pWar, pDefeated, pWinner);
                WarWinner result = pWar.isAttacker(pWinner)
                    ? WarWinner.Attackers
                    : WarWinner.Defenders;
                World.world?.wars?.endWar(pWar, result);
                return true;
            }
            catch (Exception exception)
            {
                Applied.Remove(warId);
                ModClass.LogWarning("Total-war no-force settlement failed " +
                                    "war=" + warId + ": " +
                                    exception.Message);
                return false;
            }
        }

        public static void ClearRuntime()
        {
            Applied.Clear();
        }
    }
}
