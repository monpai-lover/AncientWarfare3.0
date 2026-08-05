using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal enum VirtualNobleTitleGrantResult
    {
        Success = 0,
        NotReady = 1,
        InvalidTarget = 2,
        InvalidText = 3,
        Duplicate = 4,
        PersistenceFailed = 5
    }

    internal readonly struct VirtualNobleTitleSnapshot
    {
        public VirtualNobleTitleSnapshot(long pTitleId, long pKingdomId,
            long pActorId, string pText, long pPredecessorId,
            string pState, int pGrantedYear)
        {
            TitleId = pTitleId;
            KingdomId = pKingdomId;
            ActorId = pActorId;
            Text = pText ?? "";
            PredecessorId = pPredecessorId;
            State = pState ?? "";
            GrantedYear = pGrantedYear;
        }

        public long TitleId { get; }
        public long KingdomId { get; }
        public long ActorId { get; }
        public string Text { get; }
        public long PredecessorId { get; }
        public string State { get; }
        public int GrantedYear { get; }
        public bool IsActive => string.Equals(State, "active",
            StringComparison.OrdinalIgnoreCase) && ActorId >= 0;
    }

    internal static class VirtualNobleTitleService
    {
        private const string Table = "VirtualNobleTitle";
        private static readonly Dictionary<long,
            List<VirtualNobleTitleSnapshot>> KingdomCache =
            new Dictionary<long, List<VirtualNobleTitleSnapshot>>();
        private static readonly Dictionary<long,
            List<VirtualNobleTitleSnapshot>> ActorCache =
            new Dictionary<long, List<VirtualNobleTitleSnapshot>>();

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
            LineageArchiveManager.Instance.InitializeSuccessful;

        internal static VirtualNobleTitleGrantResult TryGrant(
            Kingdom pKingdom, Actor pGrantor, Actor pTarget, string pText,
            out VirtualNobleTitleSnapshot pSnapshot)
        {
            pSnapshot = default;
            if (!Ready) return VirtualNobleTitleGrantResult.NotReady;
            if (pKingdom?.data == null || pTarget?.data == null ||
                pGrantor?.data == null || pKingdom.isRekt() ||
                pTarget.isRekt() || !pTarget.isAlive() ||
                pTarget.kingdom != pKingdom)
                return VirtualNobleTitleGrantResult.InvalidTarget;
            if (!VirtualNobleTitleRules.IsValidTitle(pText))
                return VirtualNobleTitleGrantResult.InvalidText;

            string title = VirtualNobleTitleRules.NormalizeTitle(pText);
            string key = VirtualNobleTitleRules.NormalizeTitleKey(title);
            try
            {
                using SQLiteCommand duplicate = new SQLiteCommand(DB);
                duplicate.CommandText = "SELECT TITLE_ID FROM " + Table +
                    " WHERE KINGDOM_ID=@k AND NORMALIZED_KEY=@n AND ACTIVE=1 LIMIT 1";
                duplicate.Parameters.AddWithValue("@k", pKingdom.id);
                duplicate.Parameters.AddWithValue("@n", key);
                if (duplicate.ExecuteScalar() != null)
                    return VirtualNobleTitleGrantResult.Duplicate;

                long titleId = TableIdAllocator.Next(DB, Table, "TITLE_ID");
                long grantorId = pGrantor.data.id;
                int year = Date.getCurrentYear();
                double now = LineageService.CurTime();
                using SQLiteTransaction transaction = DB.BeginTransaction();
                using SQLiteCommand insert = new SQLiteCommand(DB);
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO " + Table +
                    " (TITLE_ID,KINGDOM_ID,KINGDOM_NAME,CURRENT_ACTOR_ID," +
                    "TITLE_TEXT,NORMALIZED_KEY,GRANTOR_ACTOR_ID,GRANTOR_NAME," +
                    "PREDECESSOR_TITLE_ID,INHERITED_FROM_ACTOR_ID,SUCCESSION_STATE," +
                    "GRANTED_YEAR,GRANTED_TIME,END_YEAR,END_TIME,ACTIVE,END_REASON," +
                    "PRIMARY_TITLE_SNAPSHOT) VALUES (@id,@k,@kn,@a,@t,@n,@g,@gn," +
                    "-1,-1,'active',@y,@time,-1,-1,1,'',@t)";
                Add(insert, "@id", titleId);
                Add(insert, "@k", pKingdom.id);
                Add(insert, "@kn", pKingdom.name ?? "");
                Add(insert, "@a", pTarget.data.id);
                Add(insert, "@t", title);
                Add(insert, "@n", key);
                Add(insert, "@g", grantorId);
                Add(insert, "@gn", pGrantor.getName() ?? "");
                Add(insert, "@y", year);
                Add(insert, "@time", now);
                insert.ExecuteNonQuery();
                transaction.Commit();

                // A virtual title is sufficient to establish noble identity even
                // when the actor has no formal rank yet.
                if (!NobleIdentityService.IsNobleActor(pTarget))
                {
                    try { LineageService.OnActorPromoted(pTarget,
                        NobleTrigger.Figure); }
                    catch { }
                    pTarget.data.set(LineageKeys.NOBLE_DISTANCE, 0);
                    pTarget.data.set(LineageKeys.LINEAGE_STATUS,
                        LineageStatus.NOBLE);
                    if (!pTarget.hasTrait(LineageKeys.TRAIT_GUIZU))
                        pTarget.addTrait(LineageKeys.TRAIT_GUIZU);
                }
                Invalidate(pKingdom.id, pTarget.data.id);
                pSnapshot = new VirtualNobleTitleSnapshot(titleId,
                    pKingdom.id, pTarget.data.id, title, -1L, "active", year);
                ChronicleEvents.OnVirtualNobleTitleGranted(pKingdom, pGrantor,
                    pTarget, title);
                try { LineageService.ArchiveActor(pTarget, pAlive: true); }
                catch { }
                return VirtualNobleTitleGrantResult.Success;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Virtual noble title grant failed: " +
                                    error.Message);
                return VirtualNobleTitleGrantResult.PersistenceFailed;
            }
        }

        internal static IReadOnlyList<VirtualNobleTitleSnapshot>
            GetActiveForKingdom(long pKingdomId)
        {
            if (pKingdomId < 0 || !Ready)
                return Array.Empty<VirtualNobleTitleSnapshot>();
            if (KingdomCache.TryGetValue(pKingdomId, out
                List<VirtualNobleTitleSnapshot> cached)) return cached;
            var result = new List<VirtualNobleTitleSnapshot>();
            try
            {
                using SQLiteCommand command = new SQLiteCommand(DB);
                command.CommandText = "SELECT TITLE_ID,KINGDOM_ID,CURRENT_ACTOR_ID," +
                    "TITLE_TEXT,PREDECESSOR_TITLE_ID,SUCCESSION_STATE,GRANTED_YEAR " +
                    "FROM " + Table + " WHERE KINGDOM_ID=@k AND ACTIVE=1 " +
                    "ORDER BY GRANTED_TIME,TITLE_ID";
                command.Parameters.AddWithValue("@k", pKingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadSnapshot(reader));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Virtual noble title read failed: " +
                                    error.Message);
            }
            KingdomCache[pKingdomId] = result;
            return result;
        }

        internal static IReadOnlyList<VirtualNobleTitleSnapshot>
            GetActiveForActor(long pActorId)
        {
            if (pActorId < 0 || !Ready)
                return Array.Empty<VirtualNobleTitleSnapshot>();
            if (ActorCache.TryGetValue(pActorId, out
                List<VirtualNobleTitleSnapshot> cached)) return cached;
            var result = new List<VirtualNobleTitleSnapshot>();
            try
            {
                using SQLiteCommand command = new SQLiteCommand(DB);
                command.CommandText = "SELECT TITLE_ID,KINGDOM_ID,CURRENT_ACTOR_ID," +
                    "TITLE_TEXT,PREDECESSOR_TITLE_ID,SUCCESSION_STATE,GRANTED_YEAR " +
                    "FROM " + Table + " WHERE CURRENT_ACTOR_ID=@a AND ACTIVE=1 " +
                    "ORDER BY GRANTED_TIME,TITLE_ID";
                command.Parameters.AddWithValue("@a", pActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(ReadSnapshot(reader));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Virtual actor title read failed: " +
                                    error.Message);
            }
            ActorCache[pActorId] = result;
            return result;
        }

        internal static string GetPrimaryTitle(Actor pActor)
        {
            if (pActor?.data == null) return "";
            if (pActor.kingdom?.king == pActor) return "";
            NobleTitleSnapshot formal = NobleRankService.ReadHot(pActor);
            if (formal.IsActive) return "";
            IReadOnlyList<VirtualNobleTitleSnapshot> titles =
                GetActiveForActor(pActor.data.id);
            return titles.Count == 0 ? "" : titles[0].Text;
        }

        internal static void OnActorDying(Actor pHolder)
        {
            if (!Ready || pHolder?.data == null) return;
            List<VirtualNobleTitleSnapshot> titles =
                new List<VirtualNobleTitleSnapshot>(GetActiveForActor(
                    pHolder.data.id));
            for (int i = 0; i < titles.Count; i++)
            {
                VirtualNobleTitleSnapshot title = titles[i];
                Actor successor = FindSuccessor(pHolder, title.KingdomId);
                if (successor == null)
                {
                    Close(title.TitleId, "extinct");
                    ChronicleEvents.OnVirtualNobleTitleExtinct(
                        ResolveKingdom(title.KingdomId), pHolder, title.Text);
                    continue;
                }
                long successorId = CreateSuccessor(title, successor,
                    pHolder.data.id);
                if (successorId < 0) continue;
                Invalidate(title.KingdomId, pHolder.data.id);
                Invalidate(title.KingdomId, successor.data.id);
                ChronicleEvents.OnVirtualNobleTitleInherited(
                    ResolveKingdom(title.KingdomId), pHolder, successor,
                    title.Text);
            }
        }

        internal static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null) return;
            try
            {
                using SQLiteCommand command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + Table +
                    " SET ACTIVE=0,SUCCESSION_STATE='extinct',END_REASON='kingdom_destroyed'," +
                    "END_YEAR=@y,END_TIME=@t WHERE KINGDOM_ID=@k AND ACTIVE=1";
                Add(command, "@y", Date.getCurrentYear());
                Add(command, "@t", LineageService.CurTime());
                Add(command, "@k", pKingdom.id);
                command.ExecuteNonQuery();
            }
            catch { }
            Invalidate(pKingdom.id, -1L);
        }

        internal static void ClearRuntime()
        {
            KingdomCache.Clear();
            ActorCache.Clear();
        }

        private static long CreateSuccessor(VirtualNobleTitleSnapshot pTitle,
            Actor pSuccessor, long pPreviousActorId)
        {
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                Close(pTitle.TitleId, "inherited", transaction);
                long nextId = TableIdAllocator.Next(DB, Table, "TITLE_ID");
                using SQLiteCommand command = new SQLiteCommand(DB);
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO " + Table +
                    " (TITLE_ID,KINGDOM_ID,CURRENT_ACTOR_ID,TITLE_TEXT,NORMALIZED_KEY," +
                    "GRANTOR_ACTOR_ID,PREDECESSOR_TITLE_ID,INHERITED_FROM_ACTOR_ID," +
                    "SUCCESSION_STATE,GRANTED_YEAR,GRANTED_TIME,ACTIVE,PRIMARY_TITLE_SNAPSHOT) " +
                    "SELECT @id,KINGDOM_ID,@actor,TITLE_TEXT,NORMALIZED_KEY,GRANTOR_ACTOR_ID," +
                    "TITLE_ID,@prev,'active',@year,@time,1,TITLE_TEXT FROM " + Table +
                    " WHERE TITLE_ID=@title";
                Add(command, "@id", nextId);
                Add(command, "@actor", pSuccessor.data.id);
                Add(command, "@prev", pPreviousActorId);
                Add(command, "@year", Date.getCurrentYear());
                Add(command, "@time", LineageService.CurTime());
                Add(command, "@title", pTitle.TitleId);
                command.ExecuteNonQuery();
                transaction.Commit();
                return nextId;
            }
            catch { return -1L; }
        }

        private static void Close(long pTitleId, string pReason,
            SQLiteTransaction pTransaction = null)
        {
            using SQLiteCommand command = new SQLiteCommand(DB);
            command.Transaction = pTransaction;
            command.CommandText = "UPDATE " + Table +
                " SET ACTIVE=0,SUCCESSION_STATE=@state,END_REASON=@reason," +
                "END_YEAR=@year,END_TIME=@time WHERE TITLE_ID=@id";
            Add(command, "@state", pReason == "inherited" ? "inherited" : "extinct");
            Add(command, "@reason", pReason ?? "");
            Add(command, "@year", Date.getCurrentYear());
            Add(command, "@time", LineageService.CurTime());
            Add(command, "@id", pTitleId);
            command.ExecuteNonQuery();
        }

        private static Actor FindSuccessor(Actor pHolder, long pKingdomId)
        {
            Actor best = null;
            foreach (long childId in LineageQuery.GetChildIds(pHolder.data.id))
            {
                Actor child = World.world?.units?.get(childId);
                if (child?.data == null || child.isRekt() || !child.isAlive() ||
                    child.kingdom?.id != pKingdomId) continue;
                if (best == null || child.data.id < best.data.id) best = child;
            }
            return best;
        }

        private static VirtualNobleTitleSnapshot ReadSnapshot(
            SQLiteDataReader pReader)
        {
            return new VirtualNobleTitleSnapshot(pReader.GetInt64(0),
                pReader.GetInt64(1), pReader.GetInt64(2),
                pReader.IsDBNull(3) ? "" : pReader.GetString(3),
                pReader.GetInt64(4), pReader.IsDBNull(5) ? "" :
                    pReader.GetString(5), pReader.GetInt32(6));
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static void Invalidate(long pKingdomId, long pActorId)
        {
            if (pKingdomId >= 0) KingdomCache.Remove(pKingdomId);
            if (pActorId >= 0) ActorCache.Remove(pActorId);
        }

        private static void Add(SQLiteCommand pCommand, string pName,
            object pValue) => pCommand.Parameters.AddWithValue(pName, pValue ?? "");
    }
}
