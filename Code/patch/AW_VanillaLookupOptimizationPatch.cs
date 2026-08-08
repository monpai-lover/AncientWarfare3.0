using System;
using System.Collections.Generic;
using ai.behaviours;
using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch;

[HarmonyPatch]
internal static class AW_VanillaLookupOptimizationPatch
{
    [ThreadStatic]
    private static List<Actor> socializeBestTargets;

    [ThreadStatic]
    private static List<Actor> socializeNormalTargets;

    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix, HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    private static void ClearWorld()
    {
        AWFreeTileSearchIndex.Reset();
        AWChunkWindowIndex.Reset();
    }
    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix, HarmonyPatch(typeof(BehFindBuilding), nameof(BehFindBuilding.execute))]
    private static bool FindBuilding(BehFindBuilding __instance, Actor pActor, ref BehResult __result)
    {
        Building target = FindBuildingTarget(pActor, __instance._type, __instance._only_non_targeted, __instance._only_with_resources);
        pActor.beh_building_target = target;
        __result = target == null ? BehResult.Stop : BehResult.Continue;
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix, HarmonyPatch(typeof(Toolbox), "getBuildingsTypeFromChunk")]
    private static bool BuildingsFromChunk(MapChunk pChunk, string pType, bool pOnlyNonTargeted, bool pOnlyWithResources, ref IEnumerable<Building> __result)
    {
        __result = EnumerateBuildings(pChunk, pType, pOnlyNonTargeted, pOnlyWithResources);
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix, HarmonyPatch(typeof(BehFindMeatSource), "getClosestMeatActor")]
    private static bool ClosestMeat(BehFindMeatSource __instance, Actor pActor, ref Actor __result)
    {
        WorldTile origin = pActor?.current_tile;
        if (origin?.chunk == null) return true;
        bool stopEarly = Randy.randomBool();
        float closest = int.MaxValue;
        Actor found = null;
        MeatTargetType type = __instance._meat_target_type;
        bool factions = __instance._check_for_factions;
        MapChunk[] chunks = AWChunkWindowIndex.Get(origin.chunk, Randy.randomInt(1, 3));
        int offset = Randy.randomInt(0, chunks.Length);
        for (int i = 0; i < chunks.Length; i++)
        {
            List<Actor> units = chunks[(i + offset) % chunks.Length].objects.units_all;
            int unitOffset = stopEarly ? Randy.randomInt(0, units.Count) : 0;
            for (int j = 0; j < units.Count; j++)
            {
                Actor target = units[(j + unitOffset) % units.Count];
                if (!target.isAlive() || target == pActor || target.asset.actor_size > pActor.asset.actor_size ||
                    !target.current_tile.isSameIsland(origin) || !pActor.canAttackTarget(target, factions) ||
                    !MatchesMeat(target, pActor, type)) continue;
                float distance = Toolbox.SquaredDistTile(target.current_tile, origin);
                if (distance >= closest) continue;
                closest = distance;
                found = target;
                if (stopEarly && Randy.randomBool())
                {
                    __result = found;
                    return false;
                }
            }
        }
        __result = found;
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix, HarmonyPatch(typeof(BehFindTargetForHunter), nameof(BehFindTargetForHunter.execute))]
    private static bool Hunter(Actor pActor, ref BehResult __result)
    {
        BaseSimObject current = pActor.beh_actor_target;
        if (current != null && pActor.isTargetOkToAttack(current.a)) { __result = BehResult.Continue; return false; }
        Actor target = null;
        int closest = int.MaxValue;
        WorldTile origin = pActor?.current_tile;
        if (origin?.chunk == null) return true;
        MapChunk[] chunks = AWChunkWindowIndex.Get(origin.chunk, 3);
        int chunkOffset = Randy.randomInt(0, chunks.Length);
        for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
        {
            MapChunk chunk = chunks[(chunkIndex + chunkOffset) % chunks.Length];
            foreach (Actor candidate in chunk.objects.units_all)
                if (candidate.isAlive() && !candidate.isSameKingdom(pActor) && candidate.asset.source_meat && candidate.getAge() >= 3 && pActor.isTargetOkToAttack(candidate))
                {
                    int distance = Toolbox.SquaredDistTile(candidate.current_tile, origin);
                    if (distance < closest) { closest = distance; target = candidate; }
            }
        }
        pActor.beh_actor_target = target;
        __result = target == null ? BehResult.Stop : BehResult.Continue;
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix, HarmonyPatch(typeof(Finder), nameof(Finder.findTileInChunk))]
    private static bool FreeTile(WorldTile pTile, TileFinderType pTileType, ref WorldTile __result)
    {
        if (pTileType != TileFinderType.FreeTile || !AWFreeTileSearchIndex.TryFind(pTile, out WorldTile tile)) return true;
        __result = tile;
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix, HarmonyPatch(typeof(BehFindLover), nameof(BehFindLover.execute))]
    private static bool Lover(Actor pActor, ref BehResult __result)
    {
        if (pActor.hasLover()) { __result = BehResult.Stop; return false; }
        Actor lover = null;
        WorldTile origin = pActor?.current_tile;
        if (origin?.chunk != null)
        {
            MapChunk[] chunks = AWChunkWindowIndex.Get(origin.chunk, 1);
            int chunkOffset = Randy.randomInt(0, chunks.Length);
            for (int i = 0; i < chunks.Length && lover == null; i++)
            {
                List<Actor> units = chunks[(i + chunkOffset) % chunks.Length].objects.units_all;
                for (int j = 0; j < units.Count; j++)
                {
                    Actor candidate = units[j];
                    if (PossibleLover(pActor, candidate)) { lover = candidate; break; }
                }
            }
        }
        if (lover == null && pActor.hasCity())
        {
            List<Actor> units = pActor.city.units;
            int offset = Randy.randomInt(0, units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                Actor candidate = units[(i + offset) % units.Count];
                if (PossibleLover(pActor, candidate) && candidate.inOwnCityBorders()) { lover = candidate; break; }
            }
        }
        if (lover != null) pActor.becomeLoversWith(lover);
        __result = BehResult.Continue;
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix, HarmonyPatch(typeof(BehTryToSocialize), nameof(BehTryToSocialize.execute))]
    private static bool Socialize(BehTryToSocialize __instance, Actor pActor, ref BehResult __result)
    {
        pActor.resetSocialize();
        Actor target = FindSocializeTarget(pActor);
        if (target == null) { __result = BehResult.Stop; return false; }
        pActor.beh_actor_target = target;
        if (pActor.canFallInLoveWith(target)) pActor.becomeLoversWith(target);
        pActor.resetSocialize(); target.resetSocialize();
        __result = pActor.hasTelepathicLink() && target.hasTelepathicLink()
            ? __instance.forceTask(pActor, "socialize_do_talk", false)
            : __instance.forceTask(pActor, "socialize_go_to_target", false);
        return false;
    }

    private static bool IsSocializeTarget(Actor actor, Actor target)
    {
        if (target == null || !target.isAlive() || !actor.canTalkWith(target)) return false;
        bool animalWhisperer = actor.hasCulture() && actor.culture.hasTrait("animal_whisperers");
        if (actor.isKingdomCiv())
        {
            if (target.isKingdomMob() && !animalWhisperer) return false;
        }
        else if (!actor.isSameSpecies(target))
        {
            return false;
        }
        return true;
    }

    private static Actor FindSocializeTarget(Actor actor)
    {
        bool needsOppositeSex = actor.subspecies.needOppositeSexTypeForReproduction();
        bool animalWhisperer = actor.hasCulture() && actor.culture.hasTrait("animal_whisperers");
        bool telepathic = actor.hasTelepathicLink();
        List<Actor> best = socializeBestTargets ??= new List<Actor>(4);
        List<Actor> normal = socializeNormalTargets ??= new List<Actor>(8);
        best.Clear();
        normal.Clear();
        if (telepathic && actor.hasFamily())
            foreach (Actor familyActor in actor.family.units)
                if (actor.canTalkWith(familyActor)) normal.Add(familyActor);
        if (telepathic)
        {
            AddTelepathicParent(actor, actor.data.parent_id_1, best);
            AddTelepathicParent(actor, actor.data.parent_id_2, best);
        }
        MapChunk[] chunks = AWChunkWindowIndex.Get(actor.current_tile.chunk, telepathic ? 2 : 1);
        int chunkOffset = Randy.randomInt(0, chunks.Length);
        bool kingdomCiv = actor.isKingdomCiv();
        bool stop = false;
        for (int i = 0; i < chunks.Length && !stop; i++)
        {
            List<Actor> units = chunks[(i + chunkOffset) % chunks.Length].objects.units_all;
            int offset = Randy.randomInt(0, units.Count);
            for (int j = 0; j < units.Count; j++)
            {
                Actor candidate = units[(j + offset) % units.Count];
                if (!candidate.isAlive() || !actor.canTalkWith(candidate)) continue;
                if (kingdomCiv ? (candidate.isKingdomMob() && !animalWhisperer) : !actor.isSameSpecies(candidate)) continue;
                if (needsOppositeSex && actor.canFallInLoveWith(candidate))
                {
                    best.Add(candidate);
                    stop = true;
                    break;
                }
                normal.Add(candidate);
                if (normal.Count > 3) { stop = true; break; }
            }
        }
        Actor result = best.Count > 0
            ? best[Randy.rnd.Next(0, best.Count)]
            : normal.Count > 0
                ? normal[Randy.rnd.Next(0, normal.Count)]
                : null;
        best.Clear();
        normal.Clear();
        return result;
    }

    private static void AddTelepathicParent(Actor actor, long id, List<Actor> best)
    {
        Actor parent = World.world.units.get(id);
        if (parent != null && parent.isAlive() && actor.canTalkWith(parent)) best.Add(parent);
    }

    private static Building FindBuildingTarget(Actor actor, string type, bool nonTargeted, bool resources)
    {
        if (actor?.current_tile?.chunk == null) return null;
        MapChunk origin = actor.current_tile.chunk;
        MapChunk[] neighbours = origin.neighbours_all;
        int count = neighbours.Length + 1;
        int offset = Randy.randomInt(0, count);
        Building fallback = null;
        for (int i = 0; i < count; i++)
        {
            int logicalIndex = (i + offset) % count;
            MapChunk chunk = logicalIndex == 0 ? origin : neighbours[logicalIndex - 1];
            Randy.randomInt(0, 1);
            List<Building> buildings = chunk.objects.buildings_all;
            int buildingOffset = Randy.randomInt(0, buildings.Count);
            for (int j = 0; j < buildings.Count; j++)
            {
                Building building = buildings[(j + buildingOffset) % buildings.Count];
                if (!building.isAlive() || building.asset.type != type || (resources && !building.hasResourcesToCollect()) || !building.isUsable() || (nonTargeted && building.current_tile.isTargeted())) continue;
                if (building.current_tile.isSameIsland(actor.current_tile)) return building;
                fallback = building;
            }
        }
        return fallback;
    }

    private static IEnumerable<Building> EnumerateBuildings(MapChunk chunk, string type, bool nonTargeted, bool resources)
    {
        if (chunk == null) yield break;
        foreach (Building building in Finder.getBuildingsFromChunk(
                     chunk.tiles[0], 0, 0, pRandom: true))
        {
            if (building.asset.type != type ||
                (resources && !building.hasResourcesToCollect()) ||
                !building.isUsable() ||
                (nonTargeted && building.current_tile.isTargeted())) continue;
            yield return building;
        }
    }

    private static bool MatchesMeat(Actor target, Actor hunter, MeatTargetType type) => type switch
    {
        MeatTargetType.Meat => target.asset.source_meat && !target.isSameSpecies(hunter.asset.id),
        MeatTargetType.MeatSameSpecies => target.isSameSpecies(hunter.asset.id),
        MeatTargetType.Insect => target.asset.source_meat_insect && !target.isSameSpecies(hunter.asset.id),
        _ => true
    };

    private static bool PossibleLover(Actor actor, Actor target) => target != actor && target.hasSubspecies() && target.isAlive() && target.canFallInLoveWith(actor);
}
