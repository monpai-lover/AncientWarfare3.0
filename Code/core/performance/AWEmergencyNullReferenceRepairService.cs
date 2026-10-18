using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.performance
{
    // Called only by manual fault recovery, while paused and after all workers join.
    // Do not use lifecycle APIs here: they can invoke the behaviour which just failed.
    internal static class AWEmergencyNullReferenceRepairService
    {
        public static string Repair(MapBox pWorld)
        {
            var result = new RepairResult();
            try
            {
                if (pWorld == null || !ReferenceEquals(pWorld, World.world) ||
                    pWorld.units == null)
                    throw new InvalidOperationException("Current world/actor manager is unavailable.");

                var actors = new List<KeyValuePair<long, Actor>>(pWorld.units.dict);
                foreach (KeyValuePair<long, Actor> entry in actors)
                {
                    Actor actor = entry.Value;
                    if (actor?.data == null)
                    {
                        result.Unresolved("actor " + entry.Key + " has no live data");
                        continue;
                    }
                    if (actor.data.id != entry.Key)
                    {
                        result.Unresolved("actor " + entry.Key + " has mismatched identity");
                        continue;
                    }
                    // Timer/visibility jobs dereference these before the dead/boat
                    // guards. A dirty spawn is recalculated by native findCurrentTile
                    // at the start of its timer job; do not invent its location here.
                    if (actor.asset == null)
                        result.Unresolved("actor " + entry.Key + " has no asset");
                    if (actor.current_tile != null && !IsCurrentTile(pWorld, actor.current_tile) ||
                        actor.current_tile == null && !actor.dirty_current_tile)
                        result.Unresolved("actor " + entry.Key + " has no valid current tile");

                    // A corpse with data still participates in vanilla death jobs.
                    if (!actor.isAlive()) continue;
                    if (actor.ai == null)
                        result.Unresolved("actor " + entry.Key + " has no AI");

                    RepairActor(pWorld, actor, result);
                    if (actor.batch == null && (actor._is_moving || actor.has_attack_target))
                        result.Unresolved("actor " + entry.Key + " has no movement/combat batch");
                }
                RepairBatches(pWorld, result);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(result.Summary() +
                    "; repair interrupted: " + error.Message, error);
            }

            if (result.UnresolvedCount > 0)
                throw new InvalidOperationException(result.Summary() + "; " +
                    string.Join("; ", result.Details));
            return result.Summary();
        }

        private static void RepairActor(MapBox pWorld, Actor pActor, RepairResult pResult)
        {
            bool invalidBehaviour = false;
            if (pActor.attack_target != null && IsInvalidTarget(pWorld, pActor.attack_target) ||
                pActor.has_attack_target && pActor.attack_target == null)
            {
                // clearAttackTarget assumes batch != null, and ignores a stale pointer
                // when has_attack_target is already false.
                pActor.attack_target = null;
                pActor.has_attack_target = false;
                pActor.batch?.c_check_attack_target?.Remove(pActor);
                pResult.References++;
                invalidBehaviour = pActor.ai?.task?.in_combat == true &&
                    pActor.beh_actor_target == null;
            }
            if (pActor.attackedBy != null && IsInvalidTarget(pWorld, pActor.attackedBy))
            {
                pActor.attackedBy = null;
                pResult.References++;
            }

            if (pActor.beh_actor_target != null && IsInvalidTarget(pWorld, pActor.beh_actor_target))
            {
                pActor.beh_actor_target = null;
                invalidBehaviour = true;
                pResult.References++;
            }
            if (pActor.beh_building_target != null && IsInvalidTarget(pWorld, pActor.beh_building_target))
            {
                pActor.beh_building_target = null;
                invalidBehaviour = true;
                pResult.References++;
            }
            if (pActor.beh_book_target != null && pActor.beh_book_target.data == null)
            {
                pActor.beh_book_target = null;
                invalidBehaviour = true;
                pResult.References++;
            }
            if (pActor.beh_tile_target != null && !IsCurrentTile(pWorld, pActor.beh_tile_target))
            {
                pActor.beh_tile_target = null;
                invalidBehaviour = true;
                pResult.References++;
            }

            if (HasInvalidMovement(pWorld, pActor))
            {
                // These native operations only clear fields/lists. stopMovement and
                // cancelAllBeh also exit buildings, finish statuses, and run callbacks.
                pActor.clearOldPath();
                if (pActor.tile_target != null &&
                    ReferenceEquals(pActor.tile_target._targeted_by, pActor))
                    pActor.tile_target.cleanTargetedBy();
                pActor.tile_target = null;
                pActor.scheduled_tile_target = null;
                pActor._next_step_tile = null;
                pActor.split_path = SplitPathStatus.Normal;
                pActor._is_moving = false;
                pActor.batch?.c_update_movement?.Remove(pActor);
                pResult.Paths++;
                invalidBehaviour = true;
            }

            if (invalidBehaviour && pActor.ai != null)
            {
                // Native setTaskBehFinished clears only task/action/index; it retains
                // the job and invokes neither clearBeh nor the task-switch callback.
                pActor.ai.setTaskBehFinished();
                pActor.timer_action = 0f;
                pResult.Tasks++;
            }
        }

        private static bool IsInvalidTarget(MapBox pWorld, BaseSimObject pTarget)
        {
            if (!pTarget.isAlive()) return true;
            if (pTarget is Actor actor)
                return actor.data == null || actor.asset == null ||
                    !ReferenceEquals(pTarget.a, actor) ||
                    !IsCurrentTile(pWorld, actor.current_tile);
            if (pTarget is Building building)
                return building.data == null || building.asset == null ||
                    !ReferenceEquals(pTarget.b, building) ||
                    !IsCurrentTile(pWorld, building.current_tile);
            // Unknown third-party subclasses have no proven invalid data contract.
            return false;
        }

        private static bool IsCurrentTile(MapBox pWorld, WorldTile pTile)
        {
            if (pTile?.data == null || pTile.Type == null) return false;
            WorldTile[] tiles = pWorld.tiles_list;
            int id = pTile.data.tile_id;
            return tiles != null && id >= 0 && id < tiles.Length &&
                ReferenceEquals(tiles[id], pTile);
        }

        private static bool HasInvalidMovement(MapBox pWorld, Actor pActor)
        {
            if (pActor.tile_target != null && !IsCurrentTile(pWorld, pActor.tile_target) ||
                pActor.scheduled_tile_target != null && !IsCurrentTile(pWorld, pActor.scheduled_tile_target) ||
                pActor._next_step_tile != null && !IsCurrentTile(pWorld, pActor._next_step_tile))
                return true;
            if (pActor.current_path.Count > 0 && pActor.current_path_index < 0)
                return true;
            // Consumed path entries are not dereferenced again by native movement.
            for (int i = Math.Max(0, pActor.current_path_index); i < pActor.current_path.Count; i++)
                if (!IsCurrentTile(pWorld, pActor.current_path[i])) return true;
            if (pActor.current_path_global != null)
                foreach (MapRegion region in pActor.current_path_global)
                    if (region == null) return true;
            return false;
        }

        private static void RepairBatches(MapBox pWorld, RepairResult pResult)
        {
            if (pWorld.units._job_manager == null) return;
            var batches = new HashSet<BatchActors>(pWorld.units._job_manager._batches_active);
            batches.UnionWith(pWorld.units._job_manager._batches_free);
            foreach (BatchActors batch in batches)
            {
                if (batch == null)
                {
                    pResult.Unresolved("actor job manager contains a null batch");
                    continue;
                }
                foreach (ObjectContainer<Actor> container in batch.containers)
                {
                    if (container == null)
                    {
                        pResult.Unresolved("actor batch contains a null container");
                        continue;
                    }
                    var invalid = new HashSet<Actor>();
                    foreach (Actor actor in container._hashSet)
                        if (actor?.data == null) invalid.Add(actor);
                    foreach (Actor actor in container._to_add)
                        if (actor?.data == null) invalid.Add(actor);
                    foreach (Actor actor in invalid) container.Remove(actor);
                    // Preserve pending valid additions/removals and invalidate native
                    // cached arrays/lists through the same operation as normal jobs.
                    container.checkAddRemove();
                    pResult.BatchEntries += invalid.Count;
                    RepairCachedViews(container, pResult);
                }
                if (batch.l_parallel_update_sprites != null)
                    pResult.BatchEntries += batch.l_parallel_update_sprites.RemoveAll(
                        actor => actor?.data == null);
            }
        }

        private static void RepairCachedViews(ObjectContainer<Actor> pContainer, RepairResult pResult)
        {
            bool invalid = false;
            foreach (Actor actor in pContainer._simple_list)
                if (actor?.data == null) { invalid = true; break; }
            int count = pContainer._array_count;
            if (count < 0 || count > (pContainer._array?.Length ?? 0)) invalid = true;
            else
                for (int i = 0; i < count; i++)
                    if (pContainer._array[i]?.data == null) { invalid = true; break; }
            if (!invalid) return;
            // An interrupted job may leave only a cached view stale. Rebuild from
            // canonical membership without invoking actor removal/disposal hooks.
            pContainer._dirty_list = true;
            pContainer._dirty_array = true;
            pContainer.getSimpleList();
            pContainer.getSimpleArray();
            pResult.CacheViews++;
        }

        private sealed class RepairResult
        {
            public int References;
            public int Paths;
            public int Tasks;
            public int BatchEntries;
            public int CacheViews;
            public int UnresolvedCount;
            public readonly List<string> Details = new List<string>();

            public void Unresolved(string pDetail)
            {
                UnresolvedCount++;
                if (Details.Count < 8) Details.Add(pDetail);
            }

            public string Summary() => "Emergency reference repair: references=" + References +
                ", paths=" + Paths + ", tasks=" + Tasks + ", batch_entries=" + BatchEntries +
                ", unresolved=" + UnresolvedCount + ", cache_views=" + CacheViews;
        }
    }
}
