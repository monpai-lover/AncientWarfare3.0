using System;
using System.Collections.Generic;
using HarmonyLib;

namespace AncientWarfare3.core.performance;

/// <summary>
/// 增量维护 ChunkObjectContainer 的角色成员。
/// 所有列表始终按 World.units rank 排列，与原版完整重建顺序一致。
/// </summary>
internal static class AWIncrementalChunkActorMembership
{
    private static readonly AccessTools.FieldRef<
            ChunkObjectContainer,
            HashSet<long>>
        KingdomSetField =
            AccessTools.FieldRefAccess<
                ChunkObjectContainer,
                HashSet<long>>(
                "_hash_kingdoms");

    private static readonly AccessTools.FieldRef<
            ChunkObjectContainer,
            Dictionary<long, List<Actor>>>
        UnitsByKingdomField =
            AccessTools.FieldRefAccess<
                ChunkObjectContainer,
                Dictionary<long, List<Actor>>>(
                "_dict_units");

    private static readonly AccessTools.FieldRef<
            ChunkObjectContainer,
            Dictionary<long, List<Building>>>
        BuildingsByKingdomField =
            AccessTools.FieldRefAccess<
                ChunkObjectContainer,
                Dictionary<long, List<Building>>>(
                "_dict_buildings");

    private static readonly AccessTools.FieldRef<
            ChunkObjectContainer,
            int>
        TotalUnitsField =
            AccessTools.FieldRefAccess<
                ChunkObjectContainer,
                int>("_total_units");

    private static readonly Dictionary<long, int>
        ValidationCursors = new();

    // Full SimObjectsZones rebuilds are performed by vanilla's live container
    // mutation. This entry point validates that the resulting container still
    // owns the deterministic World.units order before the incremental path is
    // admitted for the next pass.
    internal static void Rebuild(
        ChunkObjectContainer container,
        List<Actor> expected)
    {
        Validate(container, expected);
    }

    internal static void Remove(
        ChunkObjectContainer container,
        Actor actor,
        long kingdomId)
    {
        bool removedFromAll =
            RemoveActorReferences(container.units_all, actor) > 0;

        Dictionary<long, List<Actor>>
            unitsByKingdom =
                UnitsByKingdomField(container);
        int removedFromKingdom = 0;
        List<Actor> kingdomUnits = null;
        if (unitsByKingdom.TryGetValue(
                kingdomId,
                out kingdomUnits))
        {
            removedFromKingdom =
                RemoveActorReferences(kingdomUnits, actor);
        }

        // Vanilla may have already moved the actor before this dirty record
        // is committed. Repair a stale projection instead of aborting the
        // simulation pass on an ordering mismatch.
        if (removedFromKingdom == 0)
        {
            foreach (List<Actor> units in
                     unitsByKingdom.Values)
            {
                if (ReferenceEquals(units, kingdomUnits))
                {
                    continue;
                }

                RemoveActorReferences(units, actor);
            }
        }

        if (removedFromAll)
        {
            ref int totalUnits =
                ref TotalUnitsField(container);
            if (totalUnits > 0)
            {
                totalUnits--;
            }
        }
    }

    internal static void Add(
        ChunkObjectContainer container,
        Actor actor,
        long kingdomId,
        int actorRank,
        Dictionary<Actor, int> actorRanks)
    {
        AddLegacy(
            container,
            actor,
            kingdomId,
            actorRank,
            actorRanks);
    }

    internal static void RemoveLegacy(
        ChunkObjectContainer container,
        Actor actor,
        long kingdomId)
    {
        if (!container.units_all.Remove(actor))
        {
            throw new InvalidOperationException(
                "chunk units_all 缺少待移除角色");
        }

        Dictionary<long, List<Actor>>
            unitsByKingdom =
                UnitsByKingdomField(container);
        if (!unitsByKingdom.TryGetValue(
                kingdomId,
                out List<Actor> kingdomUnits) ||
            !kingdomUnits.Remove(actor))
        {
            throw new InvalidOperationException(
                "chunk kingdom 角色表缺少待移除角色");
        }

        ref int totalUnits =
            ref TotalUnitsField(container);
        totalUnits--;
        if (totalUnits < 0)
        {
            throw new InvalidOperationException(
                "chunk 角色总数出现负值");
        }
    }

    internal static void AddLegacy(
        ChunkObjectContainer container,
        Actor actor,
        long kingdomId,
        int actorRank,
        Dictionary<Actor, int> actorRanks)
    {
        List<Actor> kingdomUnits =
            EnsureKingdom(
                container,
                kingdomId);
        InsertActorAtRank(
            container.units_all,
            actor,
            actorRank,
            actorRanks);
        InsertActorAtRank(
            kingdomUnits,
            actor,
            actorRank,
            actorRanks);
        ref int totalUnits =
            ref TotalUnitsField(container);
        totalUnits++;
    }

    internal static void ChangeKingdom(
        ChunkObjectContainer container,
        Actor actor,
        long oldKingdomId,
        long newKingdomId,
        int actorRank,
        Dictionary<Actor, int> actorRanks)
    {
        Dictionary<long, List<Actor>>
            unitsByKingdom =
                UnitsByKingdomField(container);
        // Vanilla can update the kingdom projection before an incremental
        // dirty record is committed. Treat migration as an idempotent repair:
        // remove stale/duplicate references, then insert the actor once.
        RemoveActorFromKingdomLists(
            unitsByKingdom,
            oldKingdomId,
            actor);

        List<Actor> newUnits =
            EnsureKingdom(
                container,
                newKingdomId);
        InsertActorAtRank(
            newUnits,
            actor,
            actorRank,
            actorRanks);
    }

    private static int RemoveActorFromKingdomLists(
        Dictionary<long, List<Actor>> unitsByKingdom,
        long oldKingdomId,
        Actor actor)
    {
        if (unitsByKingdom.TryGetValue(
                oldKingdomId,
                out List<Actor> oldUnits))
        {
            int removedFromOld =
                RemoveActorReferences(
                    oldUnits,
                    actor);
            if (removedFromOld > 0)
            {
                return removedFromOld;
            }
        }

        // The old projection can already be gone after a native mutation;
        // only that recovery path needs the more expensive global scan.
        int removed = 0;
        foreach (List<Actor> units in unitsByKingdom.Values)
        {
            removed += RemoveActorReferences(
                units,
                actor);
        }

        return removed;
    }

    private static int RemoveActorReferences(
        List<Actor> units,
        Actor actor)
    {
        if (units == null)
        {
            return 0;
        }

        int removed = 0;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(units[i], actor))
            {
                continue;
            }

            units.RemoveAt(i);
            removed++;
        }

        return removed;
    }

    internal static void Validate(
        ChunkObjectContainer container,
        List<Actor> expected)
    {
        if (TotalUnitsField(container) !=
            expected.Count)
        {
            throw new InvalidOperationException(
                "chunk 角色总数与增量基线不一致");
        }

        List<Actor> unitsAll =
            container.units_all;
        if (unitsAll.Count != expected.Count)
        {
            throw new InvalidOperationException(
                "chunk units_all 数量与增量基线不一致");
        }

        Dictionary<long, List<Actor>>
            unitsByKingdom =
                UnitsByKingdomField(container);
        ValidationCursors.Clear();
        for (int i = 0; i < expected.Count; i++)
        {
            Actor actor = expected[i];
            if (!ReferenceEquals(
                    unitsAll[i],
                    actor))
            {
                throw new InvalidOperationException(
                    "chunk units_all 顺序与 World.units 不一致");
            }

            long kingdomId = actor.kingdom.id;
            if (!unitsByKingdom.TryGetValue(
                    kingdomId,
                    out List<Actor> kingdomUnits))
            {
                throw new InvalidOperationException(
                    "chunk 缺少角色所属 kingdom 成员表");
            }

            ValidationCursors.TryGetValue(
                kingdomId,
                out int cursor);
            if (cursor >= kingdomUnits.Count ||
                !ReferenceEquals(
                    kingdomUnits[cursor],
                    actor))
            {
                throw new InvalidOperationException(
                    "chunk kingdom 角色顺序与 World.units 不一致");
            }

            ValidationCursors[kingdomId] =
                cursor + 1;
        }

        foreach (KeyValuePair<
                     long,
                     List<Actor>> pair in
                 unitsByKingdom)
        {
            ValidationCursors.TryGetValue(
                pair.Key,
                out int expectedCount);
            if (pair.Value.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    "chunk kingdom 角色数量与增量基线不一致");
            }
        }
    }

    private static List<Actor> EnsureKingdom(
        ChunkObjectContainer container,
        long kingdomId)
    {
        HashSet<long> kingdomSet =
            KingdomSetField(container);
        Dictionary<long, List<Actor>>
            unitsByKingdom =
                UnitsByKingdomField(container);
        if (!unitsByKingdom.TryGetValue(
                kingdomId,
                out List<Actor> units))
        {
            units = new List<Actor>();
            unitsByKingdom.Add(
                kingdomId,
                units);

            Dictionary<long, List<Building>> buildingsByKingdom =
                BuildingsByKingdomField(container);
            if (!buildingsByKingdom.ContainsKey(kingdomId))
            {
                buildingsByKingdom.Add(
                    kingdomId,
                    new List<Building>());
            }
        }

        if (kingdomSet.Add(kingdomId))
        {
            container.kingdoms.Add(kingdomId);
        }

        return units;
    }

    private static void InsertActorAtRank(
        List<Actor> target,
        Actor actor,
        int actorRank,
        Dictionary<Actor, int> actorRanks)
    {
        int low = 0;
        int high = target.Count;
        while (low < high)
        {
            int middle =
                low + (high - low) / 2;
            if (actorRanks[target[middle]] <
                actorRank)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        target.Insert(low, actor);
    }
}
