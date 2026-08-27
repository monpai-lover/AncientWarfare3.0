using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace AncientWarfare3.core.performance;

internal static class AWPresentationVisibility
{
    private static int _zonesFrame = -1;
    private static List<TileZone> _zones;
    private static int _signatureFrame = -1;
    private static bool _signatureRenderGameplay;
    private static ulong _signature;
    private static int _lastZoneCount;
    private static long _zoneReadCalls;
    private static long _zoneReadTicks;
    private static long _signatureBuilds;
    private static long _signatureChanges;
    private static long _zoneReadSuppressed;
    private static long _lastReadTicks;
    private static long _maxReadTicks;
    private static double _nextZoneReadAt;

    internal static List<TileZone> GetVisibleZones()
    {
        int frame = Time.frameCount;
        double now = Time.realtimeSinceStartupAsDouble;
        if (_zonesFrame == frame && _zones != null)
            return _zones;
        if (!PresentationRefreshRules.ShouldReadVisibleZones(
                _zones != null, now, _nextZoneReadAt))
        {
            _zoneReadSuppressed++;
            return _zones;
        }

        long started = Stopwatch.GetTimestamp();
        _zones = World.world?.zone_camera?.getVisibleZones();
        _zonesFrame = frame;
        _nextZoneReadAt =
            PresentationRefreshRules.ScheduleNextCameraRefresh(now);
        _lastZoneCount = _zones?.Count ?? 0;
        long elapsed = Stopwatch.GetTimestamp() - started;
        _lastReadTicks = elapsed;
        _zoneReadTicks += elapsed;
        _zoneReadCalls++;
        if (elapsed > _maxReadTicks) _maxReadTicks = elapsed;
        return _zones;
    }

    internal static ulong GetSignature(bool renderGameplay)
    {
        int frame = Time.frameCount;
        if (_signatureFrame == frame &&
            _signatureRenderGameplay == renderGameplay)
            return _signature;

        ulong previous = _signature;
        _signature = BuildSignature(renderGameplay);
        _signatureFrame = frame;
        _signatureRenderGameplay = renderGameplay;
        _signatureBuilds++;
        if (_signatureBuilds > 1 && previous != _signature)
            _signatureChanges++;
        return _signature;
    }

    private static ulong BuildSignature(bool renderGameplay)
    {
        unchecked
        {
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;
            ulong hash = (offset ^ (renderGameplay ? 1UL : 0UL)) * prime;
            List<TileZone> zones = GetVisibleZones();
            if (zones == null)
            {
                return hash;
            }

            hash = (hash ^ (ulong)zones.Count) * prime;
            if (zones.Count == 0) return hash;

            // ZoneCamera owns this ordered rectangular list. Its count and
            // endpoints change whenever the native visible range changes.
            hash = (hash ^ (uint)(zones[0]?.id ?? -1)) * prime;
            hash = (hash ^ (uint)(zones[zones.Count - 1]?.id ?? -1)) * prime;

            return hash;
        }
    }

    internal static string GetDiagnostics()
    {
        double average = _zoneReadCalls <= 0
            ? 0d
            : _zoneReadTicks * 1000d /
              Stopwatch.Frequency / _zoneReadCalls;
        double last = _lastReadTicks * 1000d / Stopwatch.Frequency;
        double max = _maxReadTicks * 1000d / Stopwatch.Frequency;
        return "visible_zone_reads=" + _zoneReadCalls +
               " visible_zone_count=" + _lastZoneCount +
               " visible_zone_read_ms=" + last.ToString("0.###") +
               "(avg=" + average.ToString("0.###") +
               ",max=" + max.ToString("0.###") + ")" +
               " visible_zone_read_suppressed=" + _zoneReadSuppressed +
               " visibility_signature_builds=" + _signatureBuilds +
               " visibility_signature_changes=" + _signatureChanges;
    }

    internal static void Reset()
    {
        _zonesFrame = -1;
        _zones = null;
        _signatureFrame = -1;
        _signature = 0UL;
        _lastZoneCount = 0;
        _zoneReadCalls = 0L;
        _zoneReadTicks = 0L;
        _signatureBuilds = 0L;
        _signatureChanges = 0L;
        _zoneReadSuppressed = 0L;
        _lastReadTicks = 0L;
        _maxReadTicks = 0L;
        _nextZoneReadAt = 0d;
    }
}
