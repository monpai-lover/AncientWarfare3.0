using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Migrates social identity for active formal officers using the career
    /// index instead of scanning every actor in the world.
    /// </summary>
    internal static class SocialIdentityMigrationService
    {
        private static readonly object Gate = new object();
        private static int _lastWorldIdentity;
        private static bool _completed;

        internal static int RepairAfterWorldLoaded()
        {
            if (World.world?.units == null) return 0;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return 0;
            int worldIdentity = World.world.GetHashCode();
            lock (Gate)
            {
                if (_completed && _lastWorldIdentity == worldIdentity)
                    return 0;
            }

            int changed = 0;
            try
            {
                var repairedActorIds = new HashSet<long>();
                foreach (OfficialCareerRecord record in
                         OfficialCareerPersistence.ReadAuthoritativeActiveAppointments(db))
                {
                    if (record == null || record.IsActing || record.ActorId < 0)
                        continue;
                    Actor actor = World.world.units.get(record.ActorId);
                    RepairActor(actor, repairedActorIds, ref changed);
                }
                RepairCurrentRealmLeaders(repairedActorIds, ref changed);
                lock (Gate)
                {
                    _completed = true;
                    _lastWorldIdentity = worldIdentity;
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Social identity migration failed: " +
                    error.Message);
            }
            return changed;
        }

        private static void RepairCurrentRealmLeaders(
            HashSet<long> pRepairedActorIds, ref int pChanged)
        {
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() ||
                    kingdom.isNeutral() || !kingdom.isCiv()) continue;
                RepairActor(kingdom.king, pRepairedActorIds, ref pChanged);
                RepairActor(HeirService.PeekRegisteredHeir(kingdom),
                    pRepairedActorIds, ref pChanged);
                try
                {
                    foreach (City city in kingdom.getCities())
                        RepairActor(city?.leader, pRepairedActorIds,
                            ref pChanged);
                }
                catch { }
            }
        }

        private static void RepairActor(Actor pActor,
            HashSet<long> pRepairedActorIds, ref int pChanged)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                !pActor.isAlive() ||
                !pRepairedActorIds.Add(pActor.data.id)) return;
            bool scholarBefore = pActor.hasTrait(LineageKeys.TRAIT_SHIDAFU);
            bool nobleBefore = pActor.hasTrait(LineageKeys.TRAIT_GUIZU);
            SocialIdentityService.ApplyOfficial(pActor);
            if (scholarBefore != pActor.hasTrait(LineageKeys.TRAIT_SHIDAFU) ||
                nobleBefore != pActor.hasTrait(LineageKeys.TRAIT_GUIZU))
                pChanged++;
        }

        internal static void ResetForNewWorld()
        {
            lock (Gate)
            {
                _completed = false;
                _lastWorldIdentity = 0;
            }
        }
    }
}
