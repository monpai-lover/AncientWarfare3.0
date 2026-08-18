using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class WarRosterIntegrityService
    {
        internal static bool PrepareForUpdate(War pWar)
        {
            return RepairActiveWarRoster(pWar) &&
                   pWar?.data != null &&
                   !pWar.hasEnded();
        }

        internal static bool RepairActiveWarRoster(War pWar)
        {
            if (pWar?.data == null || pWar.hasEnded()) return false;
            EnsureLists(pWar.data);
            bool changed = PruneSide(
                pWar.data.list_attackers,
                pWar.data.died_attackers);
            bool totalWar = pWar.isTotalWar();
            if (!totalWar)
            {
                changed |= PruneSide(
                    pWar.data.list_defenders,
                    pWar.data.died_defenders);
            }

            if (!TryResolveLiveKingdom(
                    pWar.data.main_attacker,
                    out _) ||
                !pWar.data.list_attackers.Contains(
                    pWar.data.main_attacker))
            {
                pWar.data.main_attacker =
                    pWar.data.list_attackers.Count > 0
                        ? pWar.data.list_attackers[0]
                        : -1L;
                changed = true;
            }

            if (!totalWar &&
                (!TryResolveLiveKingdom(
                     pWar.data.main_defender,
                     out _) ||
                 !pWar.data.list_defenders.Contains(
                     pWar.data.main_defender)))
            {
                pWar.data.main_defender =
                    pWar.data.list_defenders.Count > 0
                        ? pWar.data.list_defenders[0]
                        : -1L;
                changed = true;
            }

            if (changed)
            {
                try { pWar.prepare(); }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "War roster rebuild failed: " + error.Message);
                    return false;
                }
            }

            bool missingSide = pWar.data.list_attackers.Count == 0 ||
                               !totalWar &&
                               pWar.data.list_defenders.Count == 0;
            if (!missingSide) return true;
            try
            {
                World.world?.wars?.endWar(
                    pWar,
                    WarWinner.Nobody);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Damaged war could not be ended after roster repair: " +
                    error.Message);
            }
            return false;
        }

        internal static bool TryDetachFromActiveWars(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            var active = new List<War>();
            try
            {
                foreach (War war in World.world.wars)
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    bool contains;
                    try { contains = war.hasKingdom(pKingdom); }
                    catch { contains = false; }
                    if (contains) active.Add(war);
                }
            }
            catch
            {
                return false;
            }

            for (int i = 0; i < active.Count; i++)
            {
                War war = active[i];
                if (!RepairActiveWarRoster(war) || war.hasEnded())
                    continue;
                try
                {
                    if (war.hasKingdom(pKingdom))
                        war.lostWar(pKingdom);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "Kingdom extinction war detach failed: " +
                        error.Message);
                    return false;
                }
            }

            for (int i = 0; i < active.Count; i++)
            {
                War war = active[i];
                try
                {
                    if (war?.data != null && !war.hasEnded() &&
                        war.hasKingdom(pKingdom))
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        internal static bool TryResolveLiveKingdom(
            long pKingdomId,
            out Kingdom pKingdom)
        {
            pKingdom = null;
            if (pKingdomId < 0L) return false;
            try
            {
                pKingdom = World.world?.kingdoms?.get(pKingdomId);
                return pKingdom?.data != null &&
                       !pKingdom.isRekt() &&
                       pKingdom.isAlive();
            }
            catch
            {
                pKingdom = null;
                return false;
            }
        }

        private static bool PruneSide(
            List<long> pActiveIds,
            List<long> pDiedIds)
        {
            bool changed = false;
            for (int i = pActiveIds.Count - 1; i >= 0; i--)
            {
                long kingdomId = pActiveIds[i];
                if (TryResolveLiveKingdom(kingdomId, out _)) continue;
                pActiveIds.RemoveAt(i);
                if (!pDiedIds.Contains(kingdomId))
                    pDiedIds.Add(kingdomId);
                changed = true;
            }
            return changed;
        }

        private static void EnsureLists(WarData pData)
        {
            pData.list_attackers ??= new List<long>();
            pData.list_defenders ??= new List<long>();
            pData.died_attackers ??= new List<long>();
            pData.died_defenders ??= new List<long>();
            pData.past_attackers ??= new List<long>();
            pData.past_defenders ??= new List<long>();
        }
    }
}
