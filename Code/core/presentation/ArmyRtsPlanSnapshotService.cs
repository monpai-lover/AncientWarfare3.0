using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using UnityEngine;

namespace AncientWarfare3.core.presentation
{
    internal static class ArmyRtsPlanSnapshotService
    {
        private const double InitialPlanningDelaySeconds = 0.35d;
        private const double EmptyPlanWaitSeconds = 3d;
        private const double RetrySeconds = 0.35d;
        private const double RevisionCooldownSeconds =
            ArmyRtsPlanRules.DefaultCaptureCadenceSeconds;
        private const double PeriodicAuditSeconds =
            ArmyRtsPlanRules.DefaultCaptureCadenceSeconds;
        private const int MaximumArmiesPerSnapshot = 2048;

        private sealed class PendingRequest
        {
            internal long WarId;
            internal string Reason;
            internal double FirstRequested;
            internal double NextAttempt;
        }

        private sealed class ParticipantSet
        {
            internal readonly List<Kingdom> Kingdoms = new List<Kingdom>();
            internal readonly HashSet<long> Ids = new HashSet<long>();
            internal readonly Dictionary<long, bool> AttackerById =
                new Dictionary<long, bool>();
        }

        private static readonly Dictionary<long, PendingRequest> Pending =
            new Dictionary<long, PendingRequest>();
        private static readonly ArmyRtsPlanRevisionLedger Revisions =
            new ArmyRtsPlanRevisionLedger(RevisionCooldownSeconds);
        private static readonly List<long> ArmyIds = new List<long>(64);
        private static ArmyRtsPlanArtifactWriter _writer;
        private static bool _reportedFailure;
        private static double _nextPeriodicAudit;
        private static string _pendingLoadDirectory;
        private static string _currentSaveDirectory;

        static ArmyRtsPlanSnapshotService()
        {
            AWPerformanceSettings.ArmyRtsDiagnosticsDisabled +=
                DisableDiagnostics;
        }

        public static void OnWarStarted(War pWar)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
            {
                DisableDiagnostics();
                return;
            }
            if (!IsActiveWar(pWar)) return;
            Request(pWar.data.id, "war_started",
                Realtime() + InitialPlanningDelaySeconds);
        }

        public static void OnMissionChanged(Army pArmy,
            ArmyRtsMission pMission, string pReason)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
            {
                DisableDiagnostics();
                return;
            }
            if (pArmy?.data == null || pMission == null ||
                pMission.WarId < 0L) return;
            Request(pMission.WarId,
                string.IsNullOrWhiteSpace(pReason)
                    ? "mission_changed"
                    : pReason,
                Realtime());
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            Pending.Remove(pWar.data.id);
            Revisions.ClearWar(pWar.data.id);
            _writer?.CloseWar(pWar.data.id);
        }

        public static void ProcessFrame()
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
            {
                DisableDiagnostics();
                return;
            }
            if (!Config.game_loaded || SmoothLoader.isLoading() ||
                ArmyRtsRuntimeMode.Current != ArmyRtsMode.On ||
                AW3MultiplayerReplicaScope.IsReplicaSession) return;
            try
            {
                double realtime = Realtime();
                AuditActiveWars(realtime);
                if (Pending.Count == 0) return;
                EnsureWriter();
                PendingRequest request = FirstReadyRequest(realtime);
                if (request == null) return;
                War war = FindWar(request.WarId);
                if (!IsActiveWar(war))
                {
                    Pending.Remove(request.WarId);
                    Revisions.ClearWar(request.WarId);
                    return;
                }
                if (Revisions.TryGetCaptureDeferral(request.WarId,
                        realtime, out double retryAt))
                {
                    request.NextAttempt = retryAt;
                    return;
                }
                ArmyRtsPlanSnapshot snapshot = Capture(war,
                    request.Reason);
                double now = Realtime();
                if (snapshot.Armies.Count == 0 &&
                    now - request.FirstRequested < EmptyPlanWaitSeconds)
                {
                    request.NextAttempt = now + RetrySeconds;
                    return;
                }
                ArmyRtsPlanTerrain terrain = CaptureTerrain(
                    snapshot.Kingdoms);
                snapshot = WithTerrain(snapshot, terrain);
                ulong fingerprint = ArmyRtsPlanRules.Fingerprint(snapshot);
                if (!Revisions.TryReserve(request.WarId, fingerprint, now,
                        out int revision))
                {
                    if (Revisions.HasPending(request.WarId))
                        request.NextAttempt = now + RetrySeconds;
                    else
                        Pending.Remove(request.WarId);
                    return;
                }
                var artifact = new ArmyRtsPlanArtifact(snapshot, revision,
                    _writer.WorldGeneration, fingerprint);
                if (_writer.TryEnqueue(artifact))
                {
                    Pending.Remove(request.WarId);
                    _reportedFailure = false;
                    ModClass.LogInfo("Army RTS plan PNG queued war=" +
                                     request.WarId + " revision=" + revision +
                                     " reason=" + snapshot.Reason +
                                     " armies=" + snapshot.Armies.Count);
                }
                else
                    request.NextAttempt = now + RetrySeconds;
            }
            catch (Exception error)
            {
                if (_reportedFailure) return;
                _reportedFailure = true;
                ModClass.LogWarning("Army RTS plan snapshot failed: " +
                                    error.Message);
            }
        }

        public static void ObserveLoadDirectory(string pPath)
        {
            string directory = NormalizeDirectory(pPath);
            if (string.IsNullOrWhiteSpace(directory)) return;
            _pendingLoadDirectory = directory;
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
            {
                DisableDiagnostics();
                return;
            }
        }

        public static void PublishToSave(string pFolder)
        {
            string directory = NormalizeDirectory(pFolder);
            if (string.IsNullOrWhiteSpace(directory)) return;
            _currentSaveDirectory = directory;
            _pendingLoadDirectory = directory;
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
            {
                DisableDiagnostics();
                return;
            }
            EnsureWriter();
            _writer.PublishToSave(directory);
        }

        public static void OnNewWorldGenerated()
        {
            _pendingLoadDirectory = null;
            _currentSaveDirectory = null;
            _writer?.ClearSaveDirectory();
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
                DisableDiagnostics();
        }

        public static void ClearRuntime()
        {
            Pending.Clear();
            Revisions.Clear();
            ArmyIds.Clear();
            _reportedFailure = false;
            _nextPeriodicAudit = 0d;
            if (AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
            {
                _writer?.ResetWorld();
                _currentSaveDirectory = _pendingLoadDirectory;
                _pendingLoadDirectory = null;
                if (!string.IsNullOrWhiteSpace(_currentSaveDirectory))
                {
                    EnsureWriter();
                    _writer.ObserveSaveDirectory(_currentSaveDirectory);
                }
            }
            else
            {
                _writer?.DiscardPending();
                _writer?.ResetWorld();
                _currentSaveDirectory = _pendingLoadDirectory;
                _pendingLoadDirectory = null;
            }
        }

        public static void Shutdown()
        {
            Shutdown(pPublish: true);
        }

        public static void DiscardAndShutdown()
        {
            Shutdown(pPublish: false);
        }

        private static void Shutdown(bool pPublish)
        {
            Pending.Clear();
            Revisions.Clear();
            ArmyIds.Clear();
            _nextPeriodicAudit = 0d;
            _pendingLoadDirectory = null;
            _currentSaveDirectory = null;
            ArmyRtsPlanArtifactWriter writer = _writer;
            _writer = null;
            if (!pPublish ||
                !AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
                writer?.DiscardPending();
            writer?.Shutdown(TimeSpan.FromSeconds(5));
        }

        private static void Request(long pWarId, string pReason,
            double pNextAttempt)
        {
            if (pWarId < 0L) return;
            double now = Realtime();
            if (!Pending.TryGetValue(pWarId, out PendingRequest request))
            {
                request = new PendingRequest
                {
                    WarId = pWarId,
                    FirstRequested = now
                };
                Pending[pWarId] = request;
            }
            request.Reason = pReason ?? "plan_changed";
            request.NextAttempt = Math.Min(request.NextAttempt <= 0d
                    ? pNextAttempt
                    : request.NextAttempt,
                pNextAttempt);
        }

        private static void AuditActiveWars(double pNow)
        {
            if (pNow < _nextPeriodicAudit) return;
            _nextPeriodicAudit = pNow + PeriodicAuditSeconds;
            if (World.world?.wars == null) return;
            foreach (War war in World.world.wars)
            {
                if (!IsActiveWar(war) ||
                    Pending.ContainsKey(war.data.id)) continue;
                Request(war.data.id, "periodic_plan_audit", pNow);
            }
        }

        private static PendingRequest FirstReadyRequest(double pNow)
        {
            PendingRequest selected = null;
            foreach (PendingRequest request in Pending.Values)
            {
                if (request.NextAttempt > pNow) continue;
                if (selected == null || request.NextAttempt <
                    selected.NextAttempt ||
                    request.NextAttempt == selected.NextAttempt &&
                    request.WarId < selected.WarId)
                    selected = request;
            }
            return selected;
        }

        private static ArmyRtsPlanSnapshot Capture(War pWar,
            string pReason)
        {
            ParticipantSet participants = CaptureParticipants(pWar,
                out List<ArmyRtsPlanKingdom> kingdoms);
            var zones = new List<ArmyRtsPlanZone>();
            var fronts = new List<ArmyRtsPlanFront>();
            CaptureZonesAndFronts(pWar, participants, zones, fronts);
            List<ArmyRtsPlanCity> cities = CaptureCities(pWar,
                participants);
            List<ArmyRtsPlanArmy> armies = CaptureArmies(pWar,
                participants);
            return new ArmyRtsPlanSnapshot(pWar.data.id, CurrentWorldYear(),
                Math.Max(1, MapBox.width), Math.Max(1, MapBox.height),
                pReason, kingdoms, zones, cities, armies, fronts);
        }

        private static ArmyRtsPlanTerrain CaptureTerrain(
            IReadOnlyList<ArmyRtsPlanKingdom> pParticipants)
        {
            int worldWidth = Math.Max(1, MapBox.width);
            int worldHeight = Math.Max(1, MapBox.height);
            ArmyRtsPlanCanvas canvas = ArmyRtsPlanRules.Project(worldWidth,
                worldHeight);
            int count = checked(canvas.Width * canvas.Height);
            var colors = new ArmyRtsPlanColor[count];
            var owners = new long[count];
            var kingdomColors =
                new Dictionary<long, ArmyRtsPlanColor>();
            var participantIds = new HashSet<long>();
            for (int i = 0; i < pParticipants.Count; i++)
                participantIds.Add(pParticipants[i].KingdomId);
            WorldTile[] tiles = World.world?.tiles_list;
            for (int y = 0; y < canvas.Height; y++)
            {
                int worldY = UnprojectAxis(canvas.Height - 1 - y,
                    canvas.Height, worldHeight);
                for (int x = 0; x < canvas.Width; x++)
                {
                    int index = y * canvas.Width + x;
                    int worldX = UnprojectAxis(x, canvas.Width,
                        worldWidth);
                    int tileIndex = worldX + worldY * worldWidth;
                    WorldTile tile = tiles != null && tileIndex >= 0 &&
                                     tileIndex < tiles.Length
                        ? tiles[tileIndex]
                        : null;
                    colors[index] = TileColor(tile);
                    Kingdom owner = tile?.zone?.city?.kingdom;
                    long ownerId = owner?.data?.id ?? -1L;
                    owners[index] = ownerId;
                    if (ownerId >= 0L &&
                        !kingdomColors.ContainsKey(ownerId))
                        kingdomColors[ownerId] = ColorOf(owner);
                }
            }
            return ArmyRtsPlanTerrainBuilder.Build(canvas.Width,
                canvas.Height, colors, owners, participantIds,
                kingdomColors);
        }

        private static ArmyRtsPlanSnapshot WithTerrain(
            ArmyRtsPlanSnapshot pSnapshot, ArmyRtsPlanTerrain pTerrain)
        {
            return new ArmyRtsPlanSnapshot(pSnapshot.WarId,
                pSnapshot.WorldYear, pSnapshot.WorldWidth,
                pSnapshot.WorldHeight, pSnapshot.Reason,
                pSnapshot.Kingdoms, pSnapshot.Zones, pSnapshot.Cities,
                pSnapshot.Armies, pSnapshot.Fronts, pTerrain);
        }

        private static int UnprojectAxis(int pCanvasValue,
            int pCanvasExtent, int pWorldExtent)
        {
            if (pCanvasExtent <= 1 || pWorldExtent <= 1) return 0;
            return Math.Max(0, Math.Min(pWorldExtent - 1,
                (int)Math.Round(pCanvasValue * (pWorldExtent - 1d) /
                                (pCanvasExtent - 1d))));
        }

        private static ArmyRtsPlanColor TileColor(WorldTile pTile)
        {
            try
            {
                if (pTile == null) return ArmyRtsPlanRasterizer.LandColor;
                Color32 color = pTile.getColor();
                return new ArmyRtsPlanColor(color.r, color.g, color.b,
                    color.a);
            }
            catch { return ArmyRtsPlanRasterizer.LandColor; }
        }

        private static ParticipantSet CaptureParticipants(War pWar,
            out List<ArmyRtsPlanKingdom> pRows)
        {
            var participants = new ParticipantSet();
            pRows = new List<ArmyRtsPlanKingdom>();
            foreach (Kingdom kingdom in pWar.getAttackers())
                AddParticipant(participants, pRows, kingdom, true);
            foreach (Kingdom kingdom in pWar.getDefenders())
                AddParticipant(participants, pRows, kingdom, false);
            return participants;
        }

        private static void AddParticipant(ParticipantSet pParticipants,
            List<ArmyRtsPlanKingdom> pRows, Kingdom pKingdom,
            bool pAttacker)
        {
            if (!IsLiveKingdom(pKingdom) ||
                !pParticipants.Ids.Add(pKingdom.id)) return;
            pParticipants.Kingdoms.Add(pKingdom);
            pParticipants.AttackerById[pKingdom.id] = pAttacker;
            pRows.Add(new ArmyRtsPlanKingdom(pKingdom.id,
                SafeName(pKingdom), ColorOf(pKingdom), pAttacker));
        }

        private static void CaptureZonesAndFronts(War pWar,
            ParticipantSet pParticipants, List<ArmyRtsPlanZone> pZones,
            List<ArmyRtsPlanFront> pFronts)
        {
            List<TileZone> liveZones = World.world?.city_zone_helper?
                .city_place_finder?.zones;
            if (liveZones == null) return;
            var frontPairs = new HashSet<long>();
            for (int i = 0; i < liveZones.Count; i++)
            {
                TileZone zone = liveZones[i];
                if (zone == null) continue;
                City city = zone.city;
                Kingdom kingdom = city?.kingdom;
                long cityId = city?.data?.id ?? -1L;
                long kingdomId = kingdom?.data?.id ?? -1L;
                bool participant = pParticipants.Ids.Contains(kingdomId);
                bool water = zone.tiles_with_ground == 0 ||
                             zone.tiles_with_liquid > zone.tiles_with_ground;
                pZones.Add(new ArmyRtsPlanZone(zone.x * 8, zone.y * 8,
                    8, 8, cityId, kingdomId, ColorOf(kingdom), water,
                    participant));
                if (!participant || zone.neighbours == null) continue;
                for (int n = 0; n < zone.neighbours.Length; n++)
                {
                    TileZone neighbour = zone.neighbours[n];
                    Kingdom other = neighbour?.city?.kingdom;
                    if (!IsLiveKingdom(other) || other == kingdom ||
                        !pParticipants.Ids.Contains(other.id) ||
                        !SafeEnemiesInWar(pWar, kingdom, other)) continue;
                    int first = Math.Min(zone.id, neighbour.id);
                    int second = Math.Max(zone.id, neighbour.id);
                    long key = ((long)first << 32) |
                               unchecked((uint)second);
                    if (!frontPairs.Add(key) || zone.centerTile == null ||
                        neighbour.centerTile == null) continue;
                    pFronts.Add(new ArmyRtsPlanFront(key, kingdomId,
                        Point(zone.centerTile),
                        Point(neighbour.centerTile)));
                }
            }
        }

        private static List<ArmyRtsPlanCity> CaptureCities(War pWar,
            ParticipantSet pParticipants)
        {
            var result = new List<ArmyRtsPlanCity>();
            if (World.world?.cities == null) return result;
            foreach (City city in World.world.cities)
            {
                if (!IsLiveCity(city)) continue;
                Kingdom owner = city.kingdom;
                long ownerId = owner?.data?.id ?? -1L;
                long controllerId = ResolveController(pWar, city, ownerId);
                if (!pParticipants.Ids.Contains(ownerId) &&
                    !pParticipants.Ids.Contains(controllerId)) continue;
                WorldTile tile = SafeCityTile(city);
                if (tile == null) continue;
                result.Add(new ArmyRtsPlanCity(city.id, ownerId,
                    controllerId, Point(tile),
                    controllerId >= 0L && controllerId != ownerId));
            }
            return result;
        }

        private static List<ArmyRtsPlanArmy> CaptureArmies(War pWar,
            ParticipantSet pParticipants)
        {
            var result = new List<ArmyRtsPlanArmy>();
            for (int kingdomIndex = 0;
                 kingdomIndex < pParticipants.Kingdoms.Count &&
                 result.Count < MaximumArmiesPerSnapshot; kingdomIndex++)
            {
                Kingdom kingdom = pParticipants.Kingdoms[kingdomIndex];
                long afterArmyId = -1L;
                bool complete = false;
                while (!complete && result.Count < MaximumArmiesPerSnapshot)
                {
                    ArmyStrategicIndexService.CopyArmyIdsAfter(kingdom,
                        afterArmyId, 64, ArmyIds, out complete);
                    for (int i = 0; i < ArmyIds.Count &&
                                    result.Count < MaximumArmiesPerSnapshot;
                         i++)
                    {
                        afterArmyId = ArmyIds[i];
                        Army army = ArmyStrategicIndexService.
                            ResolveIndexedArmy(afterArmyId, kingdom.id);
                        ArmyRtsPlanArmy row = CaptureArmy(pWar, kingdom,
                            army);
                        if (row != null) result.Add(row);
                    }
                    if (ArmyIds.Count == 0) complete = true;
                }
            }
            return result;
        }

        private static ArmyRtsPlanArmy CaptureArmy(War pWar,
            Kingdom pKingdom, Army pArmy)
        {
            if (pArmy?.data == null ||
                !ArmyRtsControllerService.TryGetMission(pArmy,
                    out ArmyRtsMission mission) ||
                mission.WarId != pWar.data.id ||
                !ArmyRtsControllerService.TryGetProjection(pArmy,
                    out ArmyRtsStrategicProjection projection)) return null;
            City targetCity = FindCity(mission.TargetCityId);
            WorldTile target = SafeCityTile(targetCity);
            if (target == null) return null;
            Actor captain = SafeCaptain(pArmy);
            WorldTile origin = captain?.current_tile ??
                               SafeCityTile(SafeArmyCity(pArmy));
            if (origin == null) origin = target;
            ArmyRtsPlanPoint? routeAnchor = null;
            if (captain != null &&
                ArmyRtsControllerService.TryGetCaptainTarget(captain,
                    out WorldTile anchor) && anchor?.data != null &&
                anchor != target)
                routeAnchor = Point(anchor);
            bool friendlyRecovery = IsFriendlyRecovery(pWar, pKingdom,
                targetCity, mission);
            return new ArmyRtsPlanArmy(pArmy.id, pKingdom.id,
                Point(origin), mission.TargetCityId, Point(target),
                routeAnchor, mission.FrontId, OperationOf(projection.State,
                    mission.Role), friendlyRecovery,
                ArmyRtsTransportService.HasActiveVoyage(pArmy),
                mission.PlayerOrder,
                projection.State == ArmyRtsState.Idle || captain == null,
                mission.ProposalKind, mission.Role, mission.Posture);
        }

        private static bool IsFriendlyRecovery(War pWar,
            Kingdom pKingdom, City pTarget, ArmyRtsMission pMission)
        {
            if (pTarget?.kingdom != pKingdom ||
                pMission.Role != ArmyRtsRole.Defense) return false;
            long controller = ResolveController(pWar, pTarget, pKingdom.id);
            return controller >= 0L && controller != pKingdom.id;
        }

        private static long ResolveController(War pWar, City pCity,
            long pOwnerId)
        {
            if (pWar?.data != null && pCity?.data != null &&
                WarScoreService.TryGetFrozenOccupation(pWar.data.id,
                    pCity.id, out long frozen) && frozen >= 0L)
                return frozen;
            Kingdom capturing = null;
            try { capturing = pCity?.getCapturingKingdom(); }
            catch { }
            return capturing?.data != null ? capturing.id : pOwnerId;
        }

        private static ArmyRtsPlanOperation OperationOf(ArmyRtsState pState,
            ArmyRtsRole pRole)
        {
            switch (pState)
            {
                case ArmyRtsState.Assault:
                case ArmyRtsState.Pursue:
                    return ArmyRtsPlanOperation.Attack;
                case ArmyRtsState.Retreat:
                case ArmyRtsState.Regroup:
                    return ArmyRtsPlanOperation.Retreat;
                case ArmyRtsState.Hold:
                case ArmyRtsState.Deploy:
                    return ArmyRtsPlanOperation.Hold;
                case ArmyRtsState.Replenish:
                    return ArmyRtsPlanOperation.Replenish;
                case ArmyRtsState.Rally:
                    return ArmyRtsPlanOperation.Rally;
                default:
                    return pRole == ArmyRtsRole.Defense
                        ? ArmyRtsPlanOperation.Defense
                        : ArmyRtsPlanOperation.Attack;
            }
        }

        private static void EnsureWriter()
        {
            if (_writer != null)
            {
                if (!string.IsNullOrWhiteSpace(_currentSaveDirectory))
                    _writer.ObserveSaveDirectory(_currentSaveDirectory);
                return;
            }
            string modDirectory = ModClass.Instance.GetDeclaration().FolderPath;
            string staging = ArmyRtsPlanRules.ResolveStagingDirectory(
                modDirectory, Process.GetCurrentProcess().Id);
            _writer = new ArmyRtsPlanArtifactWriter(staging,
                pFault: error => ModClass.LogWarning(
                    "Army RTS plan PNG write failed: " + error.Message));
            if (!string.IsNullOrWhiteSpace(_currentSaveDirectory))
                _writer.ObserveSaveDirectory(_currentSaveDirectory);
        }

        internal static void DisableDiagnostics()
        {
            Pending.Clear();
            Revisions.Clear();
            ArmyIds.Clear();
            _nextPeriodicAudit = 0d;
            _writer?.DiscardPending();
        }

        private static string NormalizeDirectory(string pPath)
        {
            if (string.IsNullOrWhiteSpace(pPath)) return string.Empty;
            string full = Path.GetFullPath(pPath);
            return File.Exists(full) ? Path.GetDirectoryName(full) : full;
        }

        private static string Sanitize(string pValue)
        {
            return (pValue ?? string.Empty).Replace('\r', ' ')
                .Replace('\n', ' ');
        }

        private static ArmyRtsPlanColor ColorOf(Kingdom pKingdom)
        {
            try
            {
                ColorAsset asset = pKingdom?.getColor();
                if (asset == null) return new ArmyRtsPlanColor(96, 96, 96);
                asset.initColor();
                Color32 color = asset.getColorMain32();
                return new ArmyRtsPlanColor(color.r, color.g, color.b,
                    color.a);
            }
            catch { return new ArmyRtsPlanColor(96, 96, 96); }
        }

        private static ArmyRtsPlanPoint Point(WorldTile pTile)
        {
            return new ArmyRtsPlanPoint(pTile?.x ?? 0, pTile?.y ?? 0);
        }

        private static string SafeName(Kingdom pKingdom)
        {
            try { return pKingdom?.name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try
            {
                Actor captain = pArmy?.getCaptain();
                return captain?.data != null && captain.isAlive() &&
                       !captain.isRekt()
                    ? captain
                    : null;
            }
            catch { return null; }
        }

        private static City SafeArmyCity(Army pArmy)
        {
            try { return pArmy?.getCity(); }
            catch { return null; }
        }

        private static WorldTile SafeCityTile(City pCity)
        {
            try { return pCity?.getTile(); }
            catch { return null; }
        }

        private static bool SafeEnemiesInWar(War pWar, Kingdom pFirst,
            Kingdom pSecond)
        {
            try { return pWar.isInWarWith(pFirst, pSecond); }
            catch { return false; }
        }

        private static bool IsActiveWar(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && pKingdom.isAlive() &&
                       !pKingdom.isRekt();
            }
            catch { return false; }
        }

        private static bool IsLiveCity(City pCity)
        {
            try
            {
                return pCity?.data != null && pCity.isAlive() &&
                       !pCity.isRekt();
            }
            catch { return false; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static int CurrentWorldYear()
        {
            try { return Date.getYear(World.world.getCurWorldTime()); }
            catch { return 0; }
        }

        private static double Realtime()
        {
            return Time.realtimeSinceStartup;
        }
    }
}
