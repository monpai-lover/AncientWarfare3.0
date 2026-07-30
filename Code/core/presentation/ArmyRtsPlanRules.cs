using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AncientWarfare3.core.presentation
{
    public readonly struct ArmyRtsPlanCanvas
    {
        public ArmyRtsPlanCanvas(int pWidth, int pHeight,
            int pWorldWidth, int pWorldHeight)
        {
            Width = Math.Max(1, pWidth);
            Height = Math.Max(1, pHeight);
            WorldWidth = Math.Max(1, pWorldWidth);
            WorldHeight = Math.Max(1, pWorldHeight);
        }

        public int Width { get; }
        public int Height { get; }
        public int WorldWidth { get; }
        public int WorldHeight { get; }

        public ArmyRtsPlanPoint ProjectPoint(ArmyRtsPlanPoint pWorld)
        {
            int x = ProjectAxis(pWorld.X, WorldWidth, Width);
            int y = Height - 1 - ProjectAxis(pWorld.Y, WorldHeight,
                Height);
            return new ArmyRtsPlanPoint(x, y);
        }

        public bool Contains(ArmyRtsPlanPoint pPoint)
        {
            return pPoint.X >= 0 && pPoint.X < Width &&
                   pPoint.Y >= 0 && pPoint.Y < Height;
        }

        private static int ProjectAxis(int pValue, int pWorldExtent,
            int pCanvasExtent)
        {
            if (pWorldExtent <= 1 || pCanvasExtent <= 1) return 0;
            int value = Math.Max(0, Math.Min(pWorldExtent - 1, pValue));
            return (int)Math.Round(value * (pCanvasExtent - 1d) /
                                   (pWorldExtent - 1d));
        }
    }

    public static class ArmyRtsPlanRules
    {
        public const string ArtifactDirectoryName = "aw3_rts_plans";
        public const int DefaultMaximumLongEdge = 768;
        public const int DefaultMaximumFramesPerSequence = 32;
        public const int DefaultMaximumGlobalFrames = 48;
        public const int DefaultMaximumSequences = 8;
        public const int DefaultFrameDelayCentiseconds = 75;
        public const double DefaultCaptureCadenceSeconds = 30d;

        public static ArmyRtsPlanCanvas Project(int pWorldWidth,
            int pWorldHeight,
            int maximumLongEdge = DefaultMaximumLongEdge)
        {
            int worldWidth = Math.Max(1, pWorldWidth);
            int worldHeight = Math.Max(1, pWorldHeight);
            int edge = Math.Max(1, maximumLongEdge);
            int width;
            int height;
            if (worldWidth >= worldHeight)
            {
                width = edge;
                height = Math.Max(1, (int)Math.Round(
                    edge * worldHeight / (double)worldWidth));
            }
            else
            {
                height = edge;
                width = Math.Max(1, (int)Math.Round(
                    edge * worldWidth / (double)worldHeight));
            }
            return new ArmyRtsPlanCanvas(width, height, worldWidth,
                worldHeight);
        }

        public static ArmyRtsPlanArrowStyle ArrowStyle(
            ArmyRtsPlanArmy pArmy)
        {
            if (pArmy == null) return ArmyRtsPlanArrowStyle.March;
            if (pArmy.TransportActive)
                return ArmyRtsPlanArrowStyle.Transport;
            if (pArmy.FriendlyRecovery)
                return ArmyRtsPlanArrowStyle.Recovery;
            if (pArmy.Operation == ArmyRtsPlanOperation.Retreat ||
                pArmy.Operation == ArmyRtsPlanOperation.Defense)
                return ArmyRtsPlanArrowStyle.Redeploy;
            if (pArmy.Operation == ArmyRtsPlanOperation.Attack)
                return ArmyRtsPlanArrowStyle.Attack;
            return ArmyRtsPlanArrowStyle.March;
        }

        public static ArmyRtsPlanFrameSummary Summarize(
            ArmyRtsPlanSnapshot pSnapshot)
        {
            if (pSnapshot == null) return default;
            int proposalKindMask = 0;
            int roleMask = 0;
            int postureMask = 0;
            for (int i = 0; i < pSnapshot.Armies.Count; i++)
            {
                ArmyRtsPlanArmy army = pSnapshot.Armies[i];
                proposalKindMask |= 1 << (int)army.ProposalKind;
                roleMask |= 1 << (int)army.Role;
                postureMask |= 1 << (int)army.Posture;
            }
            return new ArmyRtsPlanFrameSummary(pSnapshot.WorldWidth,
                pSnapshot.WorldHeight, pSnapshot.Kingdoms.Count,
                pSnapshot.Cities.Count, pSnapshot.Armies.Count,
                pSnapshot.Fronts.Count, proposalKindMask, roleMask,
                postureMask);
        }

        public static ulong Fingerprint(ArmyRtsPlanSnapshot pSnapshot)
        {
            if (pSnapshot == null) return 0UL;
            ulong hash = FnvOffset;
            Add(ref hash, pSnapshot.WarId);
            Add(ref hash, pSnapshot.WorldWidth);
            Add(ref hash, pSnapshot.WorldHeight);
            if (pSnapshot.Terrain != null)
            {
                Add(ref hash, pSnapshot.Terrain.Width);
                Add(ref hash, pSnapshot.Terrain.Height);
                byte[] terrain = pSnapshot.Terrain.Pixels;
                for (int i = 0; i < terrain.Length; i++)
                    Add(ref hash, terrain[i]);
            }

            var kingdoms = new List<ArmyRtsPlanKingdom>(
                pSnapshot.Kingdoms);
            kingdoms.Sort((pLeft, pRight) =>
                pLeft.KingdomId.CompareTo(pRight.KingdomId));
            foreach (ArmyRtsPlanKingdom kingdom in kingdoms)
            {
                Add(ref hash, kingdom.KingdomId);
                Add(ref hash, kingdom.Attacker);
            }

            var zones = new List<ArmyRtsPlanZone>(pSnapshot.Zones);
            zones.Sort((pLeft, pRight) =>
            {
                int x = pLeft.X.CompareTo(pRight.X);
                return x != 0 ? x : pLeft.Y.CompareTo(pRight.Y);
            });
            foreach (ArmyRtsPlanZone zone in zones)
            {
                Add(ref hash, zone.X);
                Add(ref hash, zone.Y);
                Add(ref hash, zone.CityId);
                Add(ref hash, zone.KingdomId);
                Add(ref hash, zone.Participant);
                Add(ref hash, zone.Water);
            }

            var cities = new List<ArmyRtsPlanCity>(pSnapshot.Cities);
            cities.Sort((pLeft, pRight) =>
                pLeft.CityId.CompareTo(pRight.CityId));
            foreach (ArmyRtsPlanCity city in cities)
            {
                Add(ref hash, city.CityId);
                Add(ref hash, city.OwnerKingdomId);
                Add(ref hash, city.ControllerKingdomId);
                Add(ref hash, city.FriendlyOccupied);
            }

            var armies = new List<ArmyRtsPlanArmy>(pSnapshot.Armies);
            armies.Sort((pLeft, pRight) =>
                pLeft.ArmyId.CompareTo(pRight.ArmyId));
            foreach (ArmyRtsPlanArmy army in armies)
            {
                Add(ref hash, army.ArmyId);
                Add(ref hash, army.KingdomId);
                Add(ref hash, army.TargetCityId);
                Add(ref hash, army.FrontId);
                Add(ref hash, (int)army.Operation);
                Add(ref hash, (int)army.ProposalKind);
                Add(ref hash, (int)army.Role);
                Add(ref hash, (int)army.Posture);
                Add(ref hash, army.FriendlyRecovery);
                Add(ref hash, army.TransportActive);
                Add(ref hash, army.PlayerOrder);
                Add(ref hash, army.Stalled);
            }

            var fronts = new List<ArmyRtsPlanFront>(pSnapshot.Fronts);
            fronts.Sort((pLeft, pRight) =>
                pLeft.FrontId.CompareTo(pRight.FrontId));
            foreach (ArmyRtsPlanFront front in fronts)
            {
                Add(ref hash, front.FrontId);
                Add(ref hash, front.KingdomId);
                Add(ref hash, front.Start.X);
                Add(ref hash, front.Start.Y);
                Add(ref hash, front.End.X);
                Add(ref hash, front.End.Y);
            }
            return hash;
        }

        public static string FileStem(long pWarId, int pWorldYear,
            int pRevision)
        {
            return "war_" + Math.Max(0L, pWarId).ToString(
                       CultureInfo.InvariantCulture) + "_" +
                   Math.Max(0, pWorldYear).ToString(
                       CultureInfo.InvariantCulture) + "_" +
                   Math.Max(0, pRevision).ToString("000",
                       CultureInfo.InvariantCulture);
        }

        public static string SequenceFileStem(long pWarId, int pWorldYear,
            long pWorldGeneration, string pSessionId)
        {
            string session = string.IsNullOrWhiteSpace(pSessionId)
                ? "session"
                : SanitizeFilePart(pSessionId);
            return "war_" + Math.Max(0L, pWarId).ToString(
                       CultureInfo.InvariantCulture) + "_" +
                   Math.Max(0, pWorldYear).ToString(
                       CultureInfo.InvariantCulture) + "_session_" +
                   session + "_world_" +
                   Math.Max(0L, pWorldGeneration).ToString(
                       CultureInfo.InvariantCulture);
        }

        public static string ResolveOutputDirectory(string pSaveDirectory)
        {
            if (string.IsNullOrWhiteSpace(pSaveDirectory))
                throw new ArgumentException("Save directory is required.",
                    nameof(pSaveDirectory));
            return Path.Combine(Path.GetFullPath(pSaveDirectory),
                ArtifactDirectoryName);
        }

        public static string ResolveStagingDirectory(string pModDirectory,
            int pProcessId)
        {
            if (string.IsNullOrWhiteSpace(pModDirectory))
                throw new ArgumentException("Mod directory is required.",
                    nameof(pModDirectory));
            if (pProcessId <= 0)
                throw new ArgumentOutOfRangeException(nameof(pProcessId));
            return Path.Combine(Path.GetFullPath(pModDirectory), ".runtime",
                "process-" + pProcessId.ToString(
                    CultureInfo.InvariantCulture), ArtifactDirectoryName);
        }

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static void Add(ref ulong pHash, bool pValue)
        {
            Add(ref pHash, pValue ? 1L : 0L);
        }

        private static void Add(ref ulong pHash, byte pValue)
        {
            unchecked
            {
                pHash ^= pValue;
                pHash *= FnvPrime;
            }
        }

        private static void Add(ref ulong pHash, int pValue)
        {
            Add(ref pHash, (long)pValue);
        }

        private static void Add(ref ulong pHash, long pValue)
        {
            unchecked
            {
                ulong value = (ulong)pValue;
                for (int i = 0; i < 8; i++)
                {
                    pHash ^= (byte)value;
                    pHash *= FnvPrime;
                    value >>= 8;
                }
            }
        }

        private static string SanitizeFilePart(string pValue)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] characters = pValue.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
                if (Array.IndexOf(invalid, characters[i]) >= 0 ||
                    char.IsWhiteSpace(characters[i])) characters[i] = '_';
            return new string(characters);
        }
    }

    public sealed class ArmyRtsPlanRevisionLedger
    {
        private sealed class Entry
        {
            public bool HasEmitted;
            public ulong EmittedFingerprint;
            public bool HasPending;
            public ulong PendingFingerprint;
            public int Revision = -1;
            public double NextAllowedTime;
        }

        private readonly double _cooldownSeconds;
        private readonly Dictionary<long, Entry> _entries =
            new Dictionary<long, Entry>();

        public ArmyRtsPlanRevisionLedger(double pCooldownSeconds)
        {
            _cooldownSeconds = Math.Max(0d, pCooldownSeconds);
        }

        public bool TryReserve(long pWarId, ulong pFingerprint,
            double pNow, out int pRevision)
        {
            pRevision = -1;
            if (pWarId < 0L) return false;
            if (!_entries.TryGetValue(pWarId, out Entry entry))
            {
                entry = new Entry();
                _entries[pWarId] = entry;
            }
            if (entry.HasEmitted &&
                entry.EmittedFingerprint == pFingerprint) return false;
            if (entry.HasPending &&
                entry.PendingFingerprint == pFingerprint)
            {
                if (pNow < entry.NextAllowedTime) return false;
                Emit(entry, pFingerprint, pNow, out pRevision);
                return true;
            }
            if (entry.HasEmitted && pNow < entry.NextAllowedTime)
            {
                entry.HasPending = true;
                entry.PendingFingerprint = pFingerprint;
                return false;
            }
            Emit(entry, pFingerprint, pNow, out pRevision);
            return true;
        }

        public bool TryGetCaptureDeferral(long pWarId, double pNow,
            out double pRetryAt)
        {
            pRetryAt = 0d;
            if (pWarId < 0L ||
                !_entries.TryGetValue(pWarId, out Entry entry) ||
                !entry.HasEmitted || pNow >= entry.NextAllowedTime)
                return false;
            pRetryAt = entry.NextAllowedTime;
            return true;
        }

        public bool TryReleasePending(long pWarId, double pNow,
            out ulong pFingerprint, out int pRevision)
        {
            pFingerprint = 0UL;
            pRevision = -1;
            if (!_entries.TryGetValue(pWarId, out Entry entry) ||
                !entry.HasPending || pNow < entry.NextAllowedTime)
                return false;
            pFingerprint = entry.PendingFingerprint;
            Emit(entry, pFingerprint, pNow, out pRevision);
            return true;
        }

        public void ClearWar(long pWarId)
        {
            _entries.Remove(pWarId);
        }

        public bool HasPending(long pWarId)
        {
            return _entries.TryGetValue(pWarId, out Entry entry) &&
                   entry.HasPending;
        }

        public void Clear()
        {
            _entries.Clear();
        }

        private void Emit(Entry pEntry, ulong pFingerprint, double pNow,
            out int pRevision)
        {
            pEntry.HasEmitted = true;
            pEntry.EmittedFingerprint = pFingerprint;
            pEntry.HasPending = false;
            pEntry.PendingFingerprint = 0UL;
            pEntry.Revision = pEntry.Revision < int.MaxValue
                ? pEntry.Revision + 1
                : int.MaxValue;
            pEntry.NextAllowedTime = pNow + _cooldownSeconds;
            pRevision = pEntry.Revision;
        }
    }
}
