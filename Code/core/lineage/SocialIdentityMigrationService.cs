using System;
using AncientWarfare3.core.court;

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
            int worldIdentity = World.world.GetHashCode();
            lock (Gate)
            {
                if (_completed && _lastWorldIdentity == worldIdentity)
                    return 0;
                _completed = true;
                _lastWorldIdentity = worldIdentity;
            }

            int changed = 0;
            try
            {
                var db = LineageArchiveManager.Instance?.OperatingDB;
                foreach (OfficialCareerRecord record in
                         OfficialCareerPersistence.ReadAuthoritativeActiveAppointments(db))
                {
                    if (record == null || record.IsActing || record.ActorId < 0)
                        continue;
                    Actor actor = World.world.units.get(record.ActorId);
                    if (actor?.data == null || actor.isRekt() || !actor.isAlive())
                        continue;
                    bool scholarBefore = actor.hasTrait(LineageKeys.TRAIT_SHIDAFU);
                    bool nobleBefore = actor.hasTrait(LineageKeys.TRAIT_GUIZU);
                    SocialIdentityService.ApplyOfficial(actor);
                    if (scholarBefore != actor.hasTrait(LineageKeys.TRAIT_SHIDAFU) ||
                        nobleBefore != actor.hasTrait(LineageKeys.TRAIT_GUIZU))
                        changed++;
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Social identity migration failed: " +
                    error.Message);
            }
            return changed;
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
