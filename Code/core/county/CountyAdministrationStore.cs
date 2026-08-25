using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AncientWarfare3.core.county
{
    internal static class CountyAdministrationStore
    {
        private const string FileName = "aw3_counties.json";
        private static readonly object Gate = new object();
        private static CountyAdministrationSnapshot _snapshot;
        private static string _directory;
        private static bool _readAttempted;

        internal static long Revision
        {
            get { EnsureInitialized(); return _snapshot?.Revision ?? 0L; }
        }

        internal static void ObserveLoadDirectory(string pDirectory)
        {
            lock (Gate)
            {
                _directory = Normalize(pDirectory);
                _snapshot = ReadFile(_directory) ?? new CountyAdministrationSnapshot();
                Normalize(_snapshot);
                _readAttempted = true;
            }
        }

        internal static void PublishToSave(string pDirectory)
        {
            string directory = Normalize(pDirectory);
            if (string.IsNullOrEmpty(directory)) return;
            EnsureInitialized();
            lock (Gate) _directory = directory;
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, FileName);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_snapshot,
                    Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception error)
            {
                ModClass.LogError("County sidecar save failed: " + error.Message);
            }
        }

        internal static void ClearForNewWorld()
        {
            lock (Gate)
            {
                _directory = null;
                _snapshot = new CountyAdministrationSnapshot();
                _readAttempted = true;
            }
        }

        internal static void ClearRuntime()
        {
            lock (Gate)
            {
                _directory = null;
                _snapshot = null;
                _readAttempted = false;
            }
        }

        internal static void RepairAfterWorldLoaded()
        {
            EnsureInitialized();
            if (World.world?.cities == null) return;
            foreach (City city in World.world.cities)
                CountyAdministrationService.ReconcileCity(city);
        }

        internal static IReadOnlyList<CountyRecord> ForCity(long pCityId)
        {
            EnsureInitialized();
            lock (Gate)
                return _snapshot.Counties.Where(p => p != null && p.Active &&
                    p.CityId == pCityId).Select(Clone).ToArray();
        }

        internal static CountyRecord FindByZone(long pZoneId)
        {
            EnsureInitialized();
            lock (Gate)
                return _snapshot.Counties.FirstOrDefault(p => p != null && p.Active &&
                    p.ZoneIds != null && p.ZoneIds.Contains(pZoneId));
        }

        internal static CountyRecord Upsert(CountyRecord pRecord)
        {
            if (pRecord == null) return null;
            EnsureInitialized();
            lock (Gate)
            {
                if (pRecord.CountyId < 0) pRecord.CountyId = _snapshot.NextCountyId++;
                CountyRecord existing = _snapshot.Counties.FirstOrDefault(
                    p => p?.CountyId == pRecord.CountyId);
                if (existing == null) _snapshot.Counties.Add(pRecord);
                else
                {
                    int index = _snapshot.Counties.IndexOf(existing);
                    _snapshot.Counties[index] = pRecord;
                }
                _snapshot.Revision++;
                pRecord.Revision = _snapshot.Revision;
                return Clone(pRecord);
            }
        }

        internal static void MarkCityDirty(long pCityId) { }

        private static void EnsureInitialized()
        {
            lock (Gate)
            {
                if (_readAttempted) return;
                _snapshot = new CountyAdministrationSnapshot();
                _readAttempted = true;
            }
        }

        private static CountyAdministrationSnapshot ReadFile(string pDirectory)
        {
            if (string.IsNullOrEmpty(pDirectory)) return null;
            try
            {
                string path = Path.Combine(pDirectory, FileName);
                return File.Exists(path)
                    ? JsonConvert.DeserializeObject<CountyAdministrationSnapshot>(
                        File.ReadAllText(path)) : null;
            }
            catch { return null; }
        }

        private static string Normalize(string pDirectory)
        {
            return string.IsNullOrWhiteSpace(pDirectory) ? null :
                Path.GetFullPath(pDirectory.Trim());
        }

        private static void Normalize(CountyAdministrationSnapshot pSnapshot)
        {
            if (pSnapshot == null) return;
            if (pSnapshot.Counties == null) pSnapshot.Counties = new List<CountyRecord>();
            pSnapshot.NextCountyId = Math.Max(1L, pSnapshot.NextCountyId);
            foreach (CountyRecord county in pSnapshot.Counties)
            {
                if (county == null) continue;
                if (county.ZoneIds == null) county.ZoneIds = new List<long>();
                county.ZoneIds = county.ZoneIds.Distinct().OrderBy(p => p).ToList();
            }
        }

        private static CountyRecord Clone(CountyRecord pRecord)
        {
            if (pRecord == null) return null;
            return new CountyRecord
            {
                CountyId = pRecord.CountyId, CityId = pRecord.CityId,
                RegionId = pRecord.RegionId, Ordinal = pRecord.Ordinal,
                Name = pRecord.Name, ManualName = pRecord.ManualName,
                ZoneIds = pRecord.ZoneIds == null ? new List<long>() :
                    new List<long>(pRecord.ZoneIds),
                LeaderActorId = pRecord.LeaderActorId, Active = pRecord.Active,
                CreatedYear = pRecord.CreatedYear,
                LastRepairedYear = pRecord.LastRepairedYear,
                Revision = pRecord.Revision
            };
        }
    }
}
