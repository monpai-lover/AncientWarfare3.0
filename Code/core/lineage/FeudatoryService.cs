using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class FeudatoryService
    {
        private static readonly object CacheLock = new object();
        private static FeudatoryCache _cache = FeudatoryCache.Empty;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance.InitializeSuccessful;

        public static IReadOnlyList<FeudatorySnapshot> GetByKingdom(long pKingdomId)
        {
            FeudatoryCache cache = _cache;
            return cache.ByKingdom.TryGetValue(pKingdomId, out FeudatorySnapshot[] rows)
                ? rows
                : Array.Empty<FeudatorySnapshot>();
        }

        public static bool TryGet(long pFeudatoryId, out FeudatorySnapshot pSnapshot)
        {
            return _cache.ById.TryGetValue(pFeudatoryId, out pSnapshot);
        }

        public static bool TryGetByCity(long pCityId, out FeudatorySnapshot pSnapshot)
        {
            pSnapshot = null;
            return _cache.FeudatoryByCity.TryGetValue(pCityId, out long feudatoryId) &&
                   _cache.ById.TryGetValue(feudatoryId, out pSnapshot);
        }

        public static bool TryGetByPrince(long pActorId, out FeudatorySnapshot pSnapshot)
        {
            pSnapshot = null;
            return _cache.FeudatoryByPrince.TryGetValue(pActorId, out long feudatoryId) &&
                   _cache.ById.TryGetValue(feudatoryId, out pSnapshot);
        }

        public static bool IsActivePrince(Actor pActor)
        {
            return pActor?.data != null && !pActor.isRekt() &&
                   TryGetByPrince(pActor.data.id, out FeudatorySnapshot snapshot) &&
                   snapshot.PrinceActorId == pActor.data.id;
        }

        public static bool TryGetRoamTile(Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            if (!IsActivePrince(pActor) ||
                !TryGetByPrince(pActor.data.id, out FeudatorySnapshot snapshot))
                return false;
            City seat;
            try { seat = World.world?.cities?.get(snapshot.SeatCityId); }
            catch { seat = null; }
            if (seat?.data == null || seat.isRekt() || seat.zones == null ||
                seat.zones.Count == 0)
                return false;

            TileZone zone = seat.zones[UnityEngine.Random.Range(0, seat.zones.Count)];
            pTarget = zone?.getRandomTile() ?? zone?.centerTile ?? seat.getTile();
            return pTarget != null;
        }

        public static bool TryEstablish(Kingdom pEmpire, Actor pPrince,
            IReadOnlyList<City> pCities, string pReason, out long pFeudatoryId)
        {
            pFeudatoryId = -1;
            if (!Ready || !ValidateEstablishment(pEmpire, pPrince, pCities))
                return false;

            double now = LineageService.CurTime();
            int year = SafeCurrentYear();
            long feudatoryId;
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                feudatoryId = NextId(transaction, FeudatoryTableItem.GetTableName(),
                    "FEUDATORY_ID");
                long cityEntryId = NextId(transaction,
                    FeudatoryCityTableItem.GetTableName(), "ENTRY_ID");
                InsertHeader(transaction, feudatoryId, pEmpire, pPrince,
                    pCities[0], year, now);
                for (int i = 0; i < pCities.Count; i++)
                    InsertCity(transaction, cityEntryId + i, feudatoryId,
                        pCities[i], now);
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory establishment failed: " +
                                    exception.Message);
                return false;
            }

            var cityIds = new long[pCities.Count];
            for (int i = 0; i < pCities.Count; i++) cityIds[i] = pCities[i].id;
            var snapshot = new FeudatorySnapshot(feudatoryId, pEmpire.id,
                pPrince.data.id, pCities[0].id, 40, 60, cityIds);
            ProjectHotIds(snapshot, pPrince, pCities);
            PublishAdded(snapshot);
            ChronicleEvents.OnFeudatoryEstablished(pEmpire, pPrince, pCities[0],
                pCities.Count);
            pFeudatoryId = feudatoryId;
            return true;
        }

        public static void LoadActiveCache()
        {
            if (!Ready) return;
            var cityIds = new Dictionary<long, List<long>>();
            var rows = new List<FeudatoryTableItem>();
            try
            {
                using (var cityCommand = new SQLiteCommand(DB))
                {
                    cityCommand.CommandText = "SELECT FEUDATORY_ID,CITY_ID FROM " +
                        FeudatoryCityTableItem.GetTableName() +
                        " WHERE ACTIVE=1 ORDER BY FEUDATORY_ID,ENTRY_ID";
                    using SQLiteDataReader reader = cityCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        long feudatoryId = reader.GetInt64(0);
                        if (!cityIds.TryGetValue(feudatoryId, out List<long> list))
                        {
                            list = new List<long>(FeudatoryRules.MaximumCities);
                            cityIds[feudatoryId] = list;
                        }
                        if (list.Count < FeudatoryRules.MaximumCities)
                            list.Add(reader.GetInt64(1));
                    }
                }

                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT * FROM " +
                    FeudatoryTableItem.GetTableName() +
                    " WHERE STATUS=0 AND END_TIME<0 ORDER BY FEUDATORY_ID";
                using SQLiteDataReader headerReader = command.ExecuteReader();
                while (headerReader.Read())
                {
                    var row = new FeudatoryTableItem();
                    row.ReadFromReader(headerReader);
                    rows.Add(row);
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory cache load failed: " +
                                    exception.Message);
                return;
            }

            var snapshots = new List<FeudatorySnapshot>(rows.Count);
            foreach (FeudatoryTableItem row in rows)
            {
                cityIds.TryGetValue(row.feudatory_id, out List<long> memberIds);
                snapshots.Add(new FeudatorySnapshot(row.feudatory_id,
                    row.empire_kingdom_id, row.prince_actor_id, row.seat_city_id,
                    row.autonomy, row.loyalty, memberIds));
            }
            Publish(snapshots);
        }

        private static bool ValidateEstablishment(Kingdom pEmpire, Actor pPrince,
            IReadOnlyList<City> pCities)
        {
            if (pEmpire?.data == null || pEmpire.isRekt() ||
                pPrince?.data == null || pPrince.isRekt() ||
                pCities == null || pCities.Count == 0 ||
                pCities.Count > FeudatoryRules.MaximumCities)
                return false;
            if (MandateService.GetCurrentMandateKingdom() != pEmpire ||
                pPrince.kingdom != pEmpire ||
                TryGetByPrince(pPrince.data.id, out _))
                return false;

            var capitalAdjacent = new HashSet<long>();
            City capital = pEmpire.capital;
            if (capital?.neighbours_cities != null)
                foreach (City adjacent in capital.neighbours_cities)
                    if (adjacent?.data != null)
                        capitalAdjacent.Add(adjacent.id);

            var selected = new HashSet<long>();
            for (int i = 0; i < pCities.Count; i++)
            {
                City city = pCities[i];
                bool connected = i == 0 || IsConnected(city, selected);
                bool assigned = city?.data != null &&
                                TryGetByCity(city.id, out _);
                bool allowed = city?.data != null && FeudatoryRules.CanAssignCity(
                    city.kingdom == pEmpire, !city.isRekt() && city.isAlive(),
                    city == capital, capitalAdjacent.Contains(city.id), assigned,
                    connected, selected.Count);
                if (!allowed || !selected.Add(city.id)) return false;
            }
            return true;
        }

        private static bool IsConnected(City pCity, HashSet<long> pSelected)
        {
            if (pCity?.neighbours_cities == null) return false;
            foreach (City adjacent in pCity.neighbours_cities)
                if (adjacent?.data != null && pSelected.Contains(adjacent.id))
                    return true;
            return false;
        }

        private static void InsertHeader(SQLiteTransaction pTransaction,
            long pFeudatoryId, Kingdom pEmpire, Actor pPrince, City pSeat,
            int pYear, double pNow)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + FeudatoryTableItem.GetTableName() +
                " (FEUDATORY_ID,EMPIRE_KINGDOM_ID,PRINCE_ACTOR_ID,PRINCE_NAME," +
                "SHI_BRANCH_ID,SEAT_CITY_ID,AUTONOMY,LOYALTY,GARRISON_ARMY_ID," +
                "GARRISON_CAPTAIN_ACTOR_ID,ESTABLISHED_YEAR,STATUS,START_TIME," +
                "END_TIME,END_REASON) VALUES (@id,@kingdom,@prince,@name,@shi," +
                "@seat,40,60,-1,-1,@year,0,@start,-1,'')";
            command.Parameters.AddWithValue("@id", pFeudatoryId);
            command.Parameters.AddWithValue("@kingdom", pEmpire.id);
            command.Parameters.AddWithValue("@prince", pPrince.data.id);
            command.Parameters.AddWithValue("@name", pPrince.getName() ?? "");
            command.Parameters.AddWithValue("@shi",
                LineageQuery.GetActorShiId(pPrince.data.id));
            command.Parameters.AddWithValue("@seat", pSeat.id);
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@start", pNow);
            command.ExecuteNonQuery();
        }

        private static void InsertCity(SQLiteTransaction pTransaction,
            long pEntryId, long pFeudatoryId, City pCity, double pNow)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " +
                FeudatoryCityTableItem.GetTableName() +
                " (ENTRY_ID,FEUDATORY_ID,CITY_ID,ACTIVE,ASSIGNED_TIME,END_TIME," +
                "END_REASON) VALUES (@entry,@feudatory,@city,1,@time,-1,'')";
            command.Parameters.AddWithValue("@entry", pEntryId);
            command.Parameters.AddWithValue("@feudatory", pFeudatoryId);
            command.Parameters.AddWithValue("@city", pCity.id);
            command.Parameters.AddWithValue("@time", pNow);
            command.ExecuteNonQuery();
        }

        private static long NextId(SQLiteTransaction pTransaction,
            string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(" + pColumn + "),0)+1 FROM " +
                                  pTable;
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? 1L
                : Convert.ToInt64(value);
        }

        private static void ProjectHotIds(FeudatorySnapshot pSnapshot,
            Actor pPrince, IReadOnlyList<City> pCities)
        {
            pPrince.data.set(LineageKeys.FEUDATORY_ID, pSnapshot.FeudatoryId);
            for (int i = 0; i < pCities.Count; i++)
                pCities[i].data.set(LineageKeys.CITY_FEUDATORY_ID,
                    pSnapshot.FeudatoryId);
            AssignPrinceIdentity(pPrince, pCities[0]);
        }

        private static void AssignPrinceIdentity(Actor pPrince, City pSeat)
        {
            if (pPrince?.data == null || pSeat?.data == null) return;
            pPrince.joinCity(pSeat);
            if (!pPrince.hasTrait(FeudatoryContent.TraitId))
                pPrince.addTrait(FeudatoryContent.TraitId);

            pPrince.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            if (!pPrince.isWarrior() &&
                layer != CourtOfficeLayer.Military)
                CourtService.ClearOfficeForReignTransition(pPrince,
                    "became_feudatory_prince");
            if (pPrince.isWarrior()) return;
            try { pPrince.ai?.setJob(FeudatoryContent.ActorJobId); }
            catch { }
        }

        private static void PublishAdded(FeudatorySnapshot pSnapshot)
        {
            lock (CacheLock)
            {
                var all = new List<FeudatorySnapshot>(_cache.ById.Count + 1);
                foreach (FeudatorySnapshot existing in _cache.ById.Values)
                    all.Add(existing);
                all.Add(pSnapshot);
                _cache = FeudatoryCache.Build(all);
            }
        }

        private static void Publish(IReadOnlyList<FeudatorySnapshot> pSnapshots)
        {
            lock (CacheLock) _cache = FeudatoryCache.Build(pSnapshots);
        }

        private static int SafeCurrentYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }

        private sealed class FeudatoryCache
        {
            public static readonly FeudatoryCache Empty = Build(
                Array.Empty<FeudatorySnapshot>());

            public readonly Dictionary<long, FeudatorySnapshot> ById;
            public readonly Dictionary<long, FeudatorySnapshot[]> ByKingdom;
            public readonly Dictionary<long, long> FeudatoryByCity;
            public readonly Dictionary<long, long> FeudatoryByPrince;

            private FeudatoryCache(
                Dictionary<long, FeudatorySnapshot> pById,
                Dictionary<long, FeudatorySnapshot[]> pByKingdom,
                Dictionary<long, long> pFeudatoryByCity,
                Dictionary<long, long> pFeudatoryByPrince)
            {
                ById = pById;
                ByKingdom = pByKingdom;
                FeudatoryByCity = pFeudatoryByCity;
                FeudatoryByPrince = pFeudatoryByPrince;
            }

            public static FeudatoryCache Build(
                IReadOnlyList<FeudatorySnapshot> pSnapshots)
            {
                var byId = new Dictionary<long, FeudatorySnapshot>();
                var kingdomLists = new Dictionary<long, List<FeudatorySnapshot>>();
                var byCity = new Dictionary<long, long>();
                var byPrince = new Dictionary<long, long>();
                int count = pSnapshots?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    FeudatorySnapshot snapshot = pSnapshots[i];
                    if (snapshot == null) continue;
                    byId[snapshot.FeudatoryId] = snapshot;
                    byPrince[snapshot.PrinceActorId] = snapshot.FeudatoryId;
                    for (int cityIndex = 0; cityIndex < snapshot.CityIds.Count;
                         cityIndex++)
                        byCity[snapshot.CityIds[cityIndex]] = snapshot.FeudatoryId;
                    if (!kingdomLists.TryGetValue(snapshot.EmpireKingdomId,
                            out List<FeudatorySnapshot> rows))
                    {
                        rows = new List<FeudatorySnapshot>();
                        kingdomLists[snapshot.EmpireKingdomId] = rows;
                    }
                    rows.Add(snapshot);
                }

                var byKingdom = new Dictionary<long, FeudatorySnapshot[]>();
                foreach (KeyValuePair<long, List<FeudatorySnapshot>> pair in
                         kingdomLists)
                {
                    pair.Value.Sort((left, right) =>
                        left.FeudatoryId.CompareTo(right.FeudatoryId));
                    byKingdom[pair.Key] = pair.Value.ToArray();
                }
                return new FeudatoryCache(byId, byKingdom, byCity, byPrince);
            }
        }
    }
}
