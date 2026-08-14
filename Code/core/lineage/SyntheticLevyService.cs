using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class SyntheticLevyService
    {
        internal static bool IsSynthetic(Actor actor)
        {
            return TryReadFlags(actor, out bool synthetic,
                out bool promoted) && synthetic && !promoted;
        }

        internal static bool SuppressPersonalHistory(Actor actor)
        {
            if (SyntheticLevySpawnScope.IsActive) return true;
            if (!TryReadFlags(actor, out bool synthetic,
                    out bool promoted)) return false;
            return SyntheticLevyRules.SuppressPersonalHistory(
                synthetic, promoted);
        }

        private static bool TryReadFlags(Actor actor,
            out bool synthetic, out bool promoted)
        {
            synthetic = false;
            promoted = false;
            try
            {
                var data = actor?.data;
                if (data == null) return false;
                data.get(LineageKeys.SYNTHETIC_LEVY,
                    out synthetic, false);
                data.get(LineageKeys.SYNTHETIC_LEVY_PROMOTED,
                    out promoted, false);
                return true;
            }
            catch (NullReferenceException)
            {
                synthetic = false;
                promoted = false;
                return false;
            }
        }

        internal static Actor TryCreate(City city, Kingdom kingdom,
            Army army, Actor template, long emergencyId,
            WorldTile pSpawnTile = null)
        {
            if (city?.data == null || kingdom?.data == null ||
                army?.data == null || template?.asset == null ||
                World.world?.units == null || city.kingdom != kingdom ||
                AWArmyService.GetIntendedKingdom(army) != kingdom)
                return null;

            Actor actor = null;
            try
            {
                using (SyntheticLevySpawnScope.Open())
                {
                    WorldTile spawnTile = pSpawnTile ?? city.getTile();
                    actor = World.world.units.createNewUnit(
                        template.asset.id, spawnTile, false, 0f,
                        template.subspecies, null, true, true);
                    if (actor?.data == null) return null;
                    Mark(actor, city, kingdom, emergencyId);
                    actor.joinCity(city);
                    using (MilitaryRecruitmentScope.Open(
                               MilitaryRecruitmentKind.TemporaryLevy))
                        city.makeWarrior(actor);
                    if (!actor.isWarrior())
                        throw new InvalidOperationException(
                            "synthetic levy did not become warrior");
                    AWArmyService.AddToArmy(actor, army);
                    if (actor.army != army)
                        throw new InvalidOperationException(
                            "synthetic levy army assignment failed");
                }
                SyntheticMobilizationLedgerService.
                    OnSyntheticMaterialized(actor);
                return actor;
            }
            catch
            {
                RemoveWithoutPersonalHistory(actor,
                    updateManpowerLedger: false);
                return null;
            }
        }

        internal static int CreateBatch(City city, Kingdom kingdom,
            Army army, int requested, long emergencyId)
        {
            return CreateBatch(city, kingdom, army, requested,
                emergencyId, null);
        }

        internal static int CreateBatchAtTile(City city, Kingdom kingdom,
            Army army, WorldTile pSpawnTile, int requested,
            long emergencyId, List<Actor> createdActors)
        {
            int limit = Math.Min(Math.Max(0, requested),
                TemporaryLevyRules.MaxRecruitsPerWorkItem);
            Actor template = ResolveTemplate(city, army);
            if (pSpawnTile?.data == null || template?.asset == null)
                return 0;
            int created = 0;
            while (created < limit)
            {
                Actor actor = TryCreate(city, kingdom, army, template,
                    emergencyId, pSpawnTile);
                if (actor == null) break;
                createdActors?.Add(actor);
                created++;
            }
            return created;
        }

        internal static int CreateBatch(City city, Kingdom kingdom,
            Army army, int requested, long emergencyId,
            List<Actor> createdActors)
        {
            int limit = Math.Min(Math.Max(0, requested),
                TemporaryLevyRules.MaxRecruitsPerWorkItem);
            Actor template = ResolveTemplate(city, army);
            if (template?.asset == null) return 0;

            int created = 0;
            while (created < limit)
            {
                Actor actor = TryCreate(city, kingdom, army, template,
                    emergencyId);
                if (actor == null)
                    break;
                createdActors?.Add(actor);
                created++;
            }
            return created;
        }

        internal static void OnActorDied(Actor actor)
        {
            bool dead;
            try
            {
                dead = actor?.data != null && IsSynthetic(actor) &&
                       (!actor.isAlive() || actor.isRekt());
            }
            catch { dead = false; }
            if (dead) ReleaseLiveLedgerOnce(actor);
        }

        internal static void ReconcileLoadedActor(Actor actor)
        {
            if (!IsSynthetic(actor)) return;
            try
            {
                Actor lover = actor.lover;
                if (lover?.lover == actor) lover.lover = null;
                actor.lover = null;
            }
            catch { }
            try
            {
                if (actor.hasStatus("pregnant"))
                    actor.finishStatusEffect("pregnant");
            }
            catch { }
            SyntheticMobilizationLedgerService.OnSyntheticMaterialized(
                actor);
        }

        // A synthetic soldier selected for command becomes a permanent
        // military actor before captain appointment and levy cleanup.
        internal static void PromoteToPermanentCommand(Actor actor)
        {
            if (actor?.data == null || !SyntheticLevyRules.
                    ShouldPromoteForCaptain(IsSynthetic(actor),
                        selectedAsCaptain: true)) return;
            ReleaseLiveLedgerOnce(actor);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY, false);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_PROMOTED, false);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_SOURCE_CITY_ID, -1L);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_SOURCE_KINGDOM_ID,
                -1L);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_EMERGENCY_ID, -1L);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_LEDGER_RELEASED,
                false);
        }

        internal static void RemoveWithoutPersonalHistory(Actor actor)
        {
            RemoveWithoutPersonalHistory(actor,
                updateManpowerLedger: true);
        }

        private static void RemoveWithoutPersonalHistory(Actor actor,
            bool updateManpowerLedger)
        {
            if (actor?.data == null) return;
            City sourceCity = FindSourceCity(actor);
            try
            {
                using (SyntheticLevySpawnScope.Open())
                {
                    if (actor.army != null)
                    {
                        try { actor.removeFromArmy(); }
                        catch { actor.setArmy(null); }
                    }
                    ActionLibrary.removeUnit(actor);
                }
            }
            finally
            {
                if (updateManpowerLedger)
                    ReleaseLiveLedgerOnce(actor, sourceCity);
            }
        }

        private static void Mark(Actor actor, City city, Kingdom kingdom,
            long emergencyId)
        {
            actor.data.set(LineageKeys.SYNTHETIC_LEVY, true);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_PROMOTED, false);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_SOURCE_CITY_ID,
                city.id);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_SOURCE_KINGDOM_ID,
                kingdom.id);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_EMERGENCY_ID,
                emergencyId);
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_LEDGER_RELEASED,
                false);
        }

        private static void ReleaseLiveLedgerOnce(Actor actor,
            City sourceCity = null)
        {
            if (actor?.data == null) return;
            actor.data.get(LineageKeys.SYNTHETIC_LEVY_LEDGER_RELEASED,
                out bool released, false);
            if (released) return;
            actor.data.set(LineageKeys.SYNTHETIC_LEVY_LEDGER_RELEASED,
                true);
            sourceCity ??= FindSourceCity(actor);
            actor.data.get(LineageKeys.SYNTHETIC_LEVY_EMERGENCY_ID,
                out long warId, -1L);
            if (sourceCity?.data != null && warId >= 0L)
                SyntheticMobilizationLedgerService.OnSyntheticRemoved(
                    warId, sourceCity.id, actor.data.id, 1);
        }

        private static Actor ResolveTemplate(City city, Army army)
        {
            Actor captain = null;
            try { captain = army?.getCaptain(); }
            catch { }
            if (captain?.asset != null && captain.kingdom == city?.kingdom &&
                !IsSynthetic(captain))
                return captain;
            if (city?.units == null) return null;
            for (int i = 0; i < city.units.Count; i++)
            {
                Actor actor = city.units[i];
                bool live;
                try
                {
                    live = actor?.asset != null && actor.isAlive() &&
                           !actor.isRekt();
                }
                catch { live = false; }
                if (live && !IsSynthetic(actor)) return actor;
            }
            return null;
        }

        private static City FindSourceCity(Actor actor)
        {
            if (actor?.data == null) return null;
            actor.data.get(LineageKeys.SYNTHETIC_LEVY_SOURCE_CITY_ID,
                out long cityId, -1L);
            if (cityId < 0L) return actor.city;
            try { return World.world?.cities?.get(cityId); }
            catch { return actor.city; }
        }
    }
}
