using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluWarMigrationService
    {
        public static void RebuildRuntime()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                World.world?.wars == null) return;

            var legacyWars = new List<War>();
            foreach (War war in World.world.wars)
            {
                bool active;
                try { active = war?.data != null && !war.hasEnded(); }
                catch { active = false; }
                bool hasDeclaredDefender =
                    ZhuluWarService.TryGetDeclaredDefenderId(war, out _);
                if (ZhuluWarRules.RequiresLegacyRosterMigration(
                        war?.getAsset()?.id, active,
                        hasDeclaredDefender))
                    legacyWars.Add(war);
            }

            for (int index = 0; index < legacyWars.Count; index++)
            {
                War war = legacyWars[index];
                if (!ZhuluWarService.IsZhuluWar(war)) continue;
                WarTerritoryService.ResolveLegacyZhuluGoals(war.data.id,
                    "legacy_zhulu_roster_migrated");
                try
                {
                    World.world.wars.endWar(war, WarWinner.Peace);
                }
                catch (Exception exception)
                {
                    ModClass.LogWarning(
                        "Legacy Zhulu war migration failed war=" +
                        war.data.id + ": " + exception.Message);
                }
            }
        }
    }
}
