using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class FeudatoryService
    {
        private const int MaximumAnnualRepairs = 4;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<long, int> AnnualCursorByKingdom =
            new Dictionary<long, int>();
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
            Actor successor = FindFeudatorySuccessor(pPrince);
            string seatName = pCities[0].data.name ?? "";
            var snapshot = new FeudatorySnapshot(feudatoryId, pEmpire.id,
                pPrince.data.id, pCities[0].id, 40, 60, cityIds,
                pEmpireName: pEmpire.name ?? "",
                pPrinceName: pPrince.getName() ?? "",
                pSeatName: seatName,
                pFeudatoryName: BuildFeudatoryName(seatName),
                pParentColor: HistoryColors.FromKingdom(pEmpire),
                pPrinceShiLabel: BuildPrinceShiLabel(pPrince.data.id),
                pSuccessorActorId: successor?.data?.id ?? -1L,
                pSuccessorName: successor?.getName() ?? "",
                pCityRows: BuildCityRows(cityIds));
            ProjectHotIds(snapshot, pPrince, pCities);
            PublishAdded(snapshot);
            FeudatoryMapModeService.DirtyMapIfActive();
            for (int i = 0; i < pCities.Count; i++)
                MandateService.OnKingdomCoreCreated(pEmpire, pCities[i], "feudatory");
            ChronicleEvents.OnFeudatoryEstablished(pEmpire, pPrince, pCities[0],
                pCities.Count);
            FeudatoryGarrisonService.EnsureFor(snapshot);
            pFeudatoryId = feudatoryId;
            return true;
        }

        public static bool UpdateGarrison(long pFeudatoryId, long pArmyId,
            long pCaptainActorId)
        {
            if (!Ready || pArmyId < 0 || pCaptainActorId < 0 ||
                !TryGet(pFeudatoryId, out FeudatorySnapshot current))
                return false;
            if (current.GarrisonArmyId == pArmyId &&
                current.GarrisonCaptainActorId == pCaptainActorId)
                return true;

            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + FeudatoryTableItem.GetTableName() +
                    " SET GARRISON_ARMY_ID=@army," +
                    "GARRISON_CAPTAIN_ACTOR_ID=@captain WHERE FEUDATORY_ID=@id " +
                    "AND STATUS=0 AND END_TIME<0";
                command.Parameters.AddWithValue("@army", pArmyId);
                command.Parameters.AddWithValue("@captain", pCaptainActorId);
                command.Parameters.AddWithValue("@id", pFeudatoryId);
                if (command.ExecuteNonQuery() != 1) return false;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory garrison update failed: " +
                                    exception.Message);
                return false;
            }

            PublishReplaced(current.WithGarrison(pArmyId, pCaptainActorId,
                GetArmySize(pArmyId)));
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
                Kingdom empire = FindKingdom(row.empire_kingdom_id);
                Actor prince = FindActor(row.prince_actor_id);
                Actor successor = FindFeudatorySuccessor(prince);
                string seatName = FindCity(row.seat_city_id)?.data?.name ?? "";
                snapshots.Add(new FeudatorySnapshot(row.feudatory_id,
                    row.empire_kingdom_id, row.prince_actor_id, row.seat_city_id,
                    row.autonomy, row.loyalty, memberIds,
                    row.garrison_army_id, row.garrison_captain_actor_id,
                    empire?.name ?? "",
                    row.prince_name ?? "",
                    seatName, BuildFeudatoryName(seatName),
                    HistoryColors.FromKingdom(empire),
                    BuildPrinceShiLabel(row.prince_actor_id),
                    GetArmySize(row.garrison_army_id),
                    successor?.data?.id ?? -1L,
                    successor?.getName() ?? "", BuildCityRows(memberIds)));
            }
            Publish(snapshots);
            FeudatoryMapModeService.DirtyMapIfActive();
        }

        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom,
            Kingdom pNewKingdom)
        {
            if (!Ready || pCity?.data == null || pOldKingdom == pNewKingdom ||
                !TryGetByCity(pCity.id, out FeudatorySnapshot snapshot))
                return;
            RemoveCity(snapshot, pCity.id, "owner_changed");
        }

        public static void OnKingdomYear(Kingdom pEmpire)
        {
            if (!Ready || pEmpire?.data == null || pEmpire.isRekt() ||
                !MandateService.IsMandateKingdom(pEmpire))
                return;
            int year = SafeCurrentYear();
            if (!FeudatoryRules.ShouldRunAnnualWork(year, pEmpire.id)) return;
            IReadOnlyList<FeudatorySnapshot> rows = GetByKingdom(pEmpire.id);
            if (rows.Count == 0) return;

            var capitalCoreIds = new List<long>();
            City capital = pEmpire.capital;
            if (capital?.data != null)
            {
                capitalCoreIds.Add(capital.id);
                if (capital.neighbours_cities != null)
                    foreach (City adjacent in capital.neighbours_cities)
                        if (adjacent?.data != null)
                            capitalCoreIds.Add(adjacent.id);
            }

            AnnualCursorByKingdom.TryGetValue(pEmpire.id, out int cursor);
            if (cursor < 0 || cursor >= rows.Count) cursor = 0;
            int count = Math.Min(MaximumAnnualRepairs, rows.Count);
            bool reclaimedCoreCity = false;
            bool repairedGarrison = false;
            for (int offset = 0; offset < count; offset++)
            {
                FeudatorySnapshot snapshot = rows[(cursor + offset) % rows.Count];
                long invalidCityId = FindFirstInvalidCity(snapshot, pEmpire);
                bool removedInvalidCity = invalidCityId >= 0 &&
                    RemoveCity(snapshot, invalidCityId, "annual_invalid_city");
                if (removedInvalidCity &&
                    !TryGet(snapshot.FeudatoryId, out snapshot))
                    continue;

                if (!removedInvalidCity && !reclaimedCoreCity)
                {
                    long cityId = FeudatoryRules.SelectOneCapitalCoreRepair(
                        snapshot.CityIds, capitalCoreIds);
                    if (cityId >= 0)
                    {
                        RemoveCity(snapshot, cityId, "capital_core_repair");
                        reclaimedCoreCity = true;
                        if (!TryGet(snapshot.FeudatoryId, out snapshot)) continue;
                    }
                }

                if (!IsValidPrince(snapshot, pEmpire)) continue;

                if (!repairedGarrison &&
                    FeudatoryGarrisonService.NeedsRepair(snapshot))
                {
                    FeudatoryGarrisonService.EnsureFor(snapshot);
                    repairedGarrison = true;
                }
            }
            AnnualCursorByKingdom[pEmpire.id] = (cursor + count) % rows.Count;
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

        private static bool RemoveCity(FeudatorySnapshot pSnapshot,
            long pCityId, string pReason)
        {
            if (pSnapshot == null) return false;
            var remaining = new List<long>(pSnapshot.CityIds.Count - 1);
            bool member = false;
            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
            {
                long cityId = pSnapshot.CityIds[i];
                if (cityId == pCityId)
                {
                    member = true;
                    continue;
                }
                remaining.Add(cityId);
            }
            FeudatoryRepairDecision decision = FeudatoryRules.ResolveCityTransfer(
                member, pSameOwner: false, pCityId, pSnapshot.SeatCityId,
                remaining);
            if (decision.Action == FeudatoryRepairAction.Ignore) return false;

            double now = LineageService.CurTime();
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                using (var cityCommand = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    cityCommand.CommandText = "UPDATE " +
                        FeudatoryCityTableItem.GetTableName() +
                        " SET ACTIVE=0,END_TIME=@time,END_REASON=@reason " +
                        "WHERE FEUDATORY_ID=@feudatory AND CITY_ID=@city " +
                        "AND ACTIVE=1";
                    cityCommand.Parameters.AddWithValue("@time", now);
                    cityCommand.Parameters.AddWithValue("@reason", pReason ?? "");
                    cityCommand.Parameters.AddWithValue("@feudatory",
                        pSnapshot.FeudatoryId);
                    cityCommand.Parameters.AddWithValue("@city", pCityId);
                    if (cityCommand.ExecuteNonQuery() != 1) return false;
                }

                using (var headerCommand = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    if (decision.Action == FeudatoryRepairAction.Abolish)
                    {
                        headerCommand.CommandText = "UPDATE " +
                            FeudatoryTableItem.GetTableName() +
                            " SET STATUS=1,END_TIME=@time,END_REASON=@reason " +
                            "WHERE FEUDATORY_ID=@id AND STATUS=0 AND END_TIME<0";
                    }
                    else if (decision.Action == FeudatoryRepairAction.MoveSeat)
                    {
                        headerCommand.CommandText = "UPDATE " +
                            FeudatoryTableItem.GetTableName() +
                            " SET SEAT_CITY_ID=@seat WHERE FEUDATORY_ID=@id " +
                            "AND STATUS=0 AND END_TIME<0";
                        headerCommand.Parameters.AddWithValue("@seat",
                            decision.NewSeatCityId);
                    }
                    if (headerCommand.CommandText.Length > 0)
                    {
                        headerCommand.Parameters.AddWithValue("@time", now);
                        headerCommand.Parameters.AddWithValue("@reason",
                            pReason ?? "");
                        headerCommand.Parameters.AddWithValue("@id",
                            pSnapshot.FeudatoryId);
                        if (headerCommand.ExecuteNonQuery() != 1) return false;
                    }
                }
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory city repair failed: " +
                                    exception.Message);
                return false;
            }

            ClearCityProjection(pCityId);
            if (decision.Action == FeudatoryRepairAction.Abolish)
            {
                PublishRemoved(pSnapshot.FeudatoryId);
                FeudatoryMapModeService.DirtyMapIfActive();
                ClearPrinceIdentity(pSnapshot);
                RemoveGarrison(pSnapshot);
                return true;
            }

            long seatId = decision.Action == FeudatoryRepairAction.MoveSeat
                ? decision.NewSeatCityId
                : pSnapshot.SeatCityId;
            string seatName = FindCity(seatId)?.data?.name;
            FeudatorySnapshot updated = pSnapshot.WithCitiesAndSeat(remaining,
                seatId, seatName, BuildCityRows(remaining),
                BuildFeudatoryName(seatName));
            PublishReplaced(updated);
            FeudatoryMapModeService.DirtyMapIfActive();
            if (decision.Action == FeudatoryRepairAction.MoveSeat)
            {
                MovePrinceToSeat(updated);
                FeudatoryGarrisonService.EnsureFor(updated);
            }
            return true;
        }

        private static long FindFirstInvalidCity(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire)
        {
            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
            {
                long cityId = pSnapshot.CityIds[i];
                City city = FindCity(cityId);
                if (city?.data == null || city.isRekt() || !city.isAlive() ||
                    city.kingdom != pEmpire)
                    return cityId;
            }
            return -1L;
        }

        private static bool IsValidPrince(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire)
        {
            Actor prince = FindActor(pSnapshot.PrinceActorId);
            return prince?.data != null && !prince.isRekt() && prince.isAlive() &&
                   prince.kingdom == pEmpire;
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

        private static void PublishReplaced(FeudatorySnapshot pSnapshot)
        {
            lock (CacheLock)
            {
                var all = new List<FeudatorySnapshot>(_cache.ById.Count);
                foreach (FeudatorySnapshot existing in _cache.ById.Values)
                    all.Add(existing.FeudatoryId == pSnapshot.FeudatoryId
                        ? pSnapshot
                        : existing);
                _cache = FeudatoryCache.Build(all);
            }
        }

        private static void PublishRemoved(long pFeudatoryId)
        {
            lock (CacheLock)
            {
                var all = new List<FeudatorySnapshot>(
                    Math.Max(0, _cache.ById.Count - 1));
                foreach (FeudatorySnapshot existing in _cache.ById.Values)
                    if (existing.FeudatoryId != pFeudatoryId)
                        all.Add(existing);
                _cache = FeudatoryCache.Build(all);
            }
        }

        private static void ClearCityProjection(long pCityId)
        {
            City city = FindCity(pCityId);
            city?.data?.set(LineageKeys.CITY_FEUDATORY_ID, -1L);
        }

        private static void ClearPrinceIdentity(FeudatorySnapshot pSnapshot)
        {
            Actor prince = FindActor(pSnapshot.PrinceActorId);
            if (prince?.data == null) return;
            prince.data.set(LineageKeys.FEUDATORY_ID, -1L);
            if (prince.hasTrait(FeudatoryContent.TraitId))
                prince.removeTrait(FeudatoryContent.TraitId);
            if (!prince.isRekt())
            {
                try { prince.ai?.setJob(prince.getNextJob()); }
                catch { }
                prince.clearGraphicsFully();
            }
        }

        private static void MovePrinceToSeat(FeudatorySnapshot pSnapshot)
        {
            Actor prince = FindActor(pSnapshot.PrinceActorId);
            City seat = FindCity(pSnapshot.SeatCityId);
            if (prince?.data == null || prince.isRekt() || seat?.data == null ||
                seat.kingdom != prince.kingdom)
                return;
            prince.joinCity(seat);
        }

        private static void RemoveGarrison(FeudatorySnapshot pSnapshot)
        {
            if (pSnapshot.GarrisonArmyId < 0) return;
            try
            {
                ArmyManager armies = World.world?.armies;
                Army army = armies?.get(pSnapshot.GarrisonArmyId);
                if (AWArmyService.IsRoleArmy(army,
                        AWArmyRole.FeudatoryGarrison))
                    AWArmyService.RemoveSpecialArmy(army);
            }
            catch { }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try
            {
                ActorManager units = World.world?.units;
                return units?.get(pActorId);
            }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try
            {
                CityManager cities = World.world?.cities;
                return cities?.get(pCityId);
            }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try
            {
                KingdomManager kingdoms = World.world?.kingdoms;
                return kingdoms?.get(pKingdomId);
            }
            catch { return null; }
        }

        private static FeudatoryCityDisplayRow[] BuildCityRows(
            IReadOnlyList<long> pCityIds)
        {
            int count = Math.Min(FeudatoryRules.MaximumCities,
                pCityIds?.Count ?? 0);
            var rows = new FeudatoryCityDisplayRow[count];
            for (int i = 0; i < count; i++)
            {
                long cityId = pCityIds[i];
                City city = FindCity(cityId);
                Actor governor = city?.leader;
                rows[i] = new FeudatoryCityDisplayRow(cityId,
                    city?.data?.name ?? "", governor?.data?.id ?? -1L,
                    governor?.getName() ?? "");
            }
            return rows;
        }

        private static string BuildFeudatoryName(string pSeatName)
        {
            return string.IsNullOrEmpty(pSeatName) ? "" : pSeatName + "藩";
        }

        private static string BuildPrinceShiLabel(long pActorId)
        {
            long shiId = LineageQuery.GetActorShiId(pActorId);
            ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(shiId);
            return branch == null
                ? ""
                : ShiBranchRules.BuildDisplayName(branch.origin_city_name,
                    branch.clan_name);
        }

        private static Actor FindFeudatorySuccessor(Actor pPrince)
        {
            if (pPrince?.data == null) return null;
            Actor eldest = null;
            double earliest = double.MaxValue;
            foreach (Actor child in pPrince.getChildren(false))
            {
                if (child?.data == null || child.isRekt() || !child.isAlive() ||
                    !child.isAdult() || !child.isSexMale() ||
                    child.kingdom != pPrince.kingdom ||
                    child.data.created_time >= earliest)
                    continue;
                eldest = child;
                earliest = child.data.created_time;
            }
            return eldest;
        }

        private static int GetArmySize(long pArmyId)
        {
            if (pArmyId < 0) return 0;
            try
            {
                ArmyManager armies = World.world?.armies;
                return Math.Max(0, armies?.get(pArmyId)?.countUnits() ?? 0);
            }
            catch { return 0; }
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
