using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace AncientWarfare3.core.performance;

/// <summary>
/// ???? ChunkObjectContainer ??????
/// ??????? World.units rank ???????????????
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
    private static readonly HashSet<Actor>
        RepairSeenMembers = new();
    private static readonly ConditionalWeakTable<
            ChunkObjectContainer,
            ValidationState>
        ValidationStates = new();

    private sealed class ValidationState
    {
        internal int UnitCursor;
        internal int KingdomCursor;
        internal int KingdomMemberCursor;
    }

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
        long kingdomId,
        int actorRank,
        Dictionary<Actor, int> actorRanks)
    {
        int removedFromAll =
            RemoveActorReferences(
                container.units_all,
                actor);

        Dictionary<long, List<Actor>>
            unitsByKingdom =
                UnitsByKingdomField(container);
        int removedFromExpected = 0;
        if (unitsByKingdom.TryGetValue(
                kingdomId,
                out List<Actor> expectedUnits))
        {
            removedFromExpected =
                RemoveActorAtRank(
                    expectedUnits,
                    actor,
                    actorRank,
                    actorRanks);
        }

        foreach (KeyValuePair<long, List<Actor>> pair in
                 unitsByKingdom)
        {
            if (pair.Key == kingdomId)
            {
                continue;
            }

            RemoveActorAtRank(
                pair.Value,
                actor,
                actorRank,
                actorRanks);
        }

        ref int totalUnits =
            ref TotalUnitsField(container);
        totalUnits = container.units_all.Count;
        if (removedFromAll != removedFromExpected)
        {
            ValidateNextMember(container, actorRanks);
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
                "chunk units_all ???????");
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
                "chunk kingdom ??????????");
        }

        ref int totalUnits =
            ref TotalUnitsField(container);
        totalUnits--;
        if (totalUnits < 0)
        {
            throw new InvalidOperationException(
                "chunk ????????");
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
            container,
            container.units_all,
            actor,
            actorRank,
            actorRanks,
            null);
        InsertActorAtRank(
            container,
            kingdomUnits,
            actor,
            actorRank,
            actorRanks,
            kingdomId);
        ref int totalUnits =
            ref TotalUnitsField(container);
        totalUnits = container.units_all.Count;
        ValidateNextMember(
            container,
            actorRanks);
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
        List<Actor> newUnits =
            EnsureKingdom(
                container,
                newKingdomId);
        int removed = 0;
        if (unitsByKingdom.TryGetValue(
                oldKingdomId,
                out List<Actor> oldUnits))
        {
            removed = RemoveActorReferences(
                oldUnits,
                actor);
        }

        if (removed != 1)
        {
            bool vanillaAlreadyCommitted =
                removed == 0 &&
                ContainsActorAtRank(
                    container.units_all,
                    actor,
                    actorRank,
                    actorRanks) &&
                ContainsActorAtRank(
                    newUnits,
                    actor,
                    actorRank,
                    actorRanks);
            if (!vanillaAlreadyCommitted)
            {
                RepairUnexpectedKingdomProjection(
                    container,
                    actorRanks);
                return;
            }
        }

        InsertActorAtRank(
            container,
            newUnits,
            actor,
            actorRank,
            actorRanks,
            newKingdomId);
        ValidateNextMember(
            container,
            actorRanks);
    }

    private static void RepairUnexpectedKingdomProjection(
        ChunkObjectContainer container,
        Dictionary<Actor, int> actorRanks)
    {
        RepairActorMembers(container, actorRanks);
        ValidateNextMember(container, actorRanks);
    }

    private static void RepairActorMembers(
        ChunkObjectContainer container,
        Dictionary<Actor, int> actorRanks)
    {
        List<Actor> unitsAll = container.units_all;
        RepairSeenMembers.Clear();
        int writeIndex = 0;
        for (int i = 0; i < unitsAll.Count; i++)
        {
            Actor member = unitsAll[i];
            if (!TryGetOwnedActorRank(
                    container,
                    member,
                    actorRanks,
                    out _) ||
                !RepairSeenMembers.Add(member))
            {
                continue;
            }

            unitsAll[writeIndex++] = member;
        }

        if (writeIndex < unitsAll.Count)
        {
            unitsAll.RemoveRange(
                writeIndex,
                unitsAll.Count - writeIndex);
        }

        unitsAll.Sort(
            (left, right) =>
                actorRanks[left].CompareTo(
                    actorRanks[right]));
        RepairSeenMembers.Clear();
        RebuildKingdomActorMembers(container, unitsAll);

        ref int totalUnits =
            ref TotalUnitsField(container);
        totalUnits = unitsAll.Count;

        ValidationState state =
            ValidationStates.GetOrCreateValue(
                container);
        state.UnitCursor = 0;
        state.KingdomCursor = 0;
        state.KingdomMemberCursor = 0;
    }

    private static void ValidateNextMember(
        ChunkObjectContainer container,
        Dictionary<Actor, int> actorRanks)
    {
        List<Actor> unitsAll = container.units_all;
        if (TotalUnitsField(container) != unitsAll.Count)
        {
            RepairActorMembers(container, actorRanks);
            return;
        }

        ValidationState state =
            ValidationStates.GetOrCreateValue(
                container);
        if (unitsAll.Count > 0)
        {
            int unitIndex =
                NormalizeCursor(
                    state.UnitCursor++,
                    unitsAll.Count);
            Actor member = unitsAll[unitIndex];
            if (!IsValidOrderedMember(
                    container,
                    unitsAll,
                    unitIndex,
                    member,
                    actorRanks) ||
                !UnitsByKingdomField(container)
                    .TryGetValue(
                        member.kingdom.id,
                        out List<Actor> kingdomUnits) ||
                !ContainsActorAtRank(
                    kingdomUnits,
                    member,
                    actorRanks[member],
                    actorRanks))
            {
                RepairActorMembers(container, actorRanks);
                return;
            }
        }

        var kingdoms = container.kingdoms;
        if (kingdoms.Count == 0)
        {
            state.KingdomCursor = 0;
            state.KingdomMemberCursor = 0;
            return;
        }

        int kingdomIndex = NormalizeCursor(
            state.KingdomCursor,
            kingdoms.Count);
        long kingdomId = kingdoms[kingdomIndex];
        Dictionary<long, List<Actor>> unitsByKingdom =
            UnitsByKingdomField(container);
        if (!unitsByKingdom.TryGetValue(
                kingdomId,
                out List<Actor> members) ||
            members == null)
        {
            RepairActorMembers(container, actorRanks);
            return;
        }

        if (members.Count == 0)
        {
            state.KingdomCursor = kingdomIndex + 1;
            state.KingdomMemberCursor = 0;
            return;
        }

        int memberIndex = NormalizeCursor(
            state.KingdomMemberCursor++,
            members.Count);
        Actor kingdomMember = members[memberIndex];
        if (!IsValidOrderedMember(
                container,
                members,
                memberIndex,
                kingdomMember,
                actorRanks) ||
            kingdomMember.kingdom.id != kingdomId ||
            !ContainsActorAtRank(
                unitsAll,
                kingdomMember,
                actorRanks[kingdomMember],
                actorRanks))
        {
            RepairActorMembers(container, actorRanks);
            return;
        }

        if (memberIndex + 1 >= members.Count)
        {
            state.KingdomCursor = kingdomIndex + 1;
            state.KingdomMemberCursor = 0;
        }
    }

    private static bool IsValidOrderedMember(
        ChunkObjectContainer container,
        List<Actor> members,
        int index,
        Actor member,
        Dictionary<Actor, int> actorRanks)
    {
        if (!TryGetOwnedActorRank(
                container,
                member,
                actorRanks,
                out int rank))
        {
            return false;
        }

        if (index > 0 &&
            (!TryGetMemberRank(
                 members[index - 1],
                 actorRanks,
                 out int previousRank) ||
             previousRank >= rank))
        {
            return false;
        }

        return index + 1 >= members.Count ||
               TryGetMemberRank(
                   members[index + 1],
                   actorRanks,
                   out int nextRank) &&
               nextRank > rank;
    }

    private static bool TryGetMemberRank(
        Actor member,
        Dictionary<Actor, int> actorRanks,
        out int rank)
    {
        rank = -1;
        return !ReferenceEquals(member, null) &&
               actorRanks.TryGetValue(member, out rank);
    }

    private static int NormalizeCursor(
        int cursor,
        int count)
    {
        int normalized = cursor % count;
        return normalized >= 0
            ? normalized
            : 0;
    }

    private static bool TryGetOwnedActorRank(
        ChunkObjectContainer container,
        Actor member,
        Dictionary<Actor, int> actorRanks,
        out int rank)
    {
        rank = -1;
        if (ReferenceEquals(member, null) ||
            member.data == null ||
            member.isRekt() ||
            !member.isAlive() ||
            !actorRanks.TryGetValue(member, out rank))
        {
            return false;
        }

        WorldTile tile = member.current_tile;
        if (tile == null ||
            !ReferenceEquals(tile.chunk?.objects, container) ||
            tile.region?.island == null)
        {
            return false;
        }

        Kingdom kingdom = member.kingdom;
        return kingdom?.data != null;
    }

    private static void RebuildKingdomActorMembers(
        ChunkObjectContainer container,
        List<Actor> unitsAll)
    {
        Dictionary<long, List<Actor>> unitsByKingdom =
            UnitsByKingdomField(container);
        foreach (List<Actor> units in unitsByKingdom.Values)
        {
            units?.Clear();
        }

        for (int i = 0; i < unitsAll.Count; i++)
        {
            Actor member = unitsAll[i];
            EnsureKingdom(
                    container,
                    member.kingdom.id)
                .Add(member);
        }
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

    private static int RemoveActorAtRank(
        List<Actor> units,
        Actor actor,
        int actorRank,
        Dictionary<Actor, int> actorRanks)
    {
        if (units == null || units.Count == 0)
        {
            return 0;
        }

        int low = 0;
        int high = units.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (!TryGetMemberRank(
                    units[middle],
                    actorRanks,
                    out int middleRank))
            {
                return RemoveActorReferences(units, actor);
            }

            if (middleRank < actorRank)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        int removed = 0;
        while (low < units.Count &&
               TryGetMemberRank(
                   units[low],
                   actorRanks,
                   out int rank) &&
               rank == actorRank)
        {
            if (ReferenceEquals(units[low], actor))
            {
                units.RemoveAt(low);
                removed++;
                continue;
            }

            low++;
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
                "chunk ????????????");
        }

        List<Actor> unitsAll =
            container.units_all;
        if (unitsAll.Count != expected.Count)
        {
            throw new InvalidOperationException(
                "chunk units_all ??????????");
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
                    "chunk units_all ??? World.units ???");
            }

            long kingdomId = actor.kingdom.id;
            if (!unitsByKingdom.TryGetValue(
                    kingdomId,
                    out List<Actor> kingdomUnits))
            {
                throw new InvalidOperationException(
                    "chunk ?????? kingdom ???");
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
                    "chunk kingdom ????? World.units ???");
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
                    "chunk kingdom ????????????");
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
        ChunkObjectContainer container,
        List<Actor> target,
        Actor actor,
        int actorRank,
        Dictionary<Actor, int> actorRanks,
        long? targetKingdomId)
    {
        int low = 0;
        int high = target.Count;
        while (low < high)
        {
            int middle =
                low + (high - low) / 2;
            Actor member = target[middle];
            if (!TryGetOwnedActorRank(
                    container,
                    member,
                    actorRanks,
                    out int middleRank) ||
                targetKingdomId.HasValue &&
                member.kingdom.id != targetKingdomId.Value ||
                middle > 0 &&
                ReferenceEquals(target[middle - 1], member) ||
                middle + 1 < target.Count &&
                ReferenceEquals(target[middle + 1], member))
            {
                RepairActorMembers(container, actorRanks);
                if (ContainsActorReference(target, actor))
                {
                    return;
                }

                low = 0;
                high = target.Count;
                continue;
            }

            if (middleRank < actorRank)
            {
                low = middle + 1;
            }
            else if (middleRank == actorRank)
            {
                if (ReferenceEquals(member, actor))
                {
                    return;
                }

                RepairActorMembers(container, actorRanks);
                if (ContainsActorReference(target, actor))
                {
                    return;
                }

                low = 0;
                high = target.Count;
            }
            else
            {
                high = middle;
            }
        }

        target.Insert(low, actor);
    }

    private static bool ContainsActorAtRank(
        List<Actor> target,
        Actor actor,
        int actorRank,
        Dictionary<Actor, int> actorRanks)
    {
        int low = 0;
        int high = target.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            Actor member = target[middle];
            if (!TryGetMemberRank(
                    member,
                    actorRanks,
                    out int middleRank))
            {
                return false;
            }

            if (middleRank < actorRank)
            {
                low = middle + 1;
            }
            else if (middleRank > actorRank)
            {
                high = middle - 1;
            }
            else
            {
                return ReferenceEquals(member, actor);
            }
        }

        return false;
    }

    private static bool ContainsActorReference(
        List<Actor> target,
        Actor actor)
    {
        for (int i = 0; i < target.Count; i++)
        {
            if (ReferenceEquals(target[i], actor))
            {
                return true;
            }
        }

        return false;
    }
}
