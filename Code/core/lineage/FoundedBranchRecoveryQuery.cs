using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct FoundedBranchOwnership
    {
        public FoundedBranchOwnership(long founderActorId, long shiId)
        {
            FounderActorId = founderActorId;
            ShiId = shiId;
        }

        public long FounderActorId { get; }
        public long ShiId { get; }

        public override bool Equals(object obj)
        {
            return obj is FoundedBranchOwnership other &&
                   other.FounderActorId == FounderActorId &&
                   other.ShiId == ShiId;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (FounderActorId.GetHashCode() * 397) ^
                       ShiId.GetHashCode();
            }
        }
    }

    internal sealed class FoundedBranchRecoverySnapshot
    {
        private readonly Dictionary<long, long> _newestByFounder;
        private readonly HashSet<FoundedBranchOwnership> _ownedBranches;

        internal FoundedBranchRecoverySnapshot(
            Dictionary<long, long> newestByFounder,
            HashSet<FoundedBranchOwnership> ownedBranches)
        {
            _newestByFounder = newestByFounder ?? new Dictionary<long, long>();
            _ownedBranches = ownedBranches ??
                             new HashSet<FoundedBranchOwnership>();
        }

        public long Resolve(long actorId, long storedShiId)
        {
            if (actorId < 0L) return -1L;
            if (storedShiId >= 0L && _ownedBranches.Contains(
                    new FoundedBranchOwnership(actorId, storedShiId)))
                return storedShiId;
            return _newestByFounder.TryGetValue(actorId, out long newestShiId)
                ? newestShiId
                : -1L;
        }
    }

    internal static class FoundedBranchRecoveryQuery
    {
        public static bool TryResolve(SQLiteConnection connection,
            SQLiteTransaction transaction, long actorId, long storedShiId,
            out long resolvedShiId)
        {
            resolvedShiId = storedShiId;
            if (!TryRead(connection, transaction, new[] { actorId },
                    out FoundedBranchRecoverySnapshot snapshot))
                return false;
            resolvedShiId = snapshot.Resolve(actorId, storedShiId);
            return true;
        }

        public static bool TryRead(SQLiteConnection connection,
            SQLiteTransaction transaction, IEnumerable<long> actorIds,
            out FoundedBranchRecoverySnapshot snapshot)
        {
            snapshot = Empty();
            if (connection == null || actorIds == null) return false;

            var founders = new HashSet<long>();
            foreach (long actorId in actorIds)
                if (actorId >= 0L) founders.Add(actorId);
            if (founders.Count == 0) return true;

            try
            {
                using var command = new SQLiteCommand(connection);
                command.Transaction = transaction;
                command.CommandText =
                    "SELECT FOUNDER_ACTOR_ID,SHI_ID FROM ShiBranch " +
                    "WHERE SOURCE_TYPE IN (@kingSource,@feudatorySource) " +
                    "AND FOUNDER_ACTOR_ID IN (" +
                    string.Join(",", founders) + ") " +
                    "ORDER BY FOUNDER_ACTOR_ID,CREATED_TIME DESC," +
                    "SHI_ID DESC";
                command.Parameters.AddWithValue("@kingSource",
                    ShiSourceType.KING_FOUNDED);
                command.Parameters.AddWithValue("@feudatorySource",
                    ShiSourceType.FEUDATORY);

                var newestByFounder = new Dictionary<long, long>();
                var ownedBranches = new HashSet<FoundedBranchOwnership>();
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long founderActorId = reader.GetInt64(0);
                    long shiId = reader.GetInt64(1);
                    if (!newestByFounder.ContainsKey(founderActorId))
                        newestByFounder.Add(founderActorId, shiId);
                    ownedBranches.Add(new FoundedBranchOwnership(
                        founderActorId, shiId));
                }

                snapshot = new FoundedBranchRecoverySnapshot(newestByFounder,
                    ownedBranches);
                return true;
            }
            catch (SQLiteException)
            {
                snapshot = Empty();
                return false;
            }
        }

        private static FoundedBranchRecoverySnapshot Empty()
        {
            return new FoundedBranchRecoverySnapshot(
                new Dictionary<long, long>(),
                new HashSet<FoundedBranchOwnership>());
        }
    }
}
