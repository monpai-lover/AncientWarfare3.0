using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolAcademyLifecycleService
    {
        private sealed class CaptureState
        {
            public Building Building;
            public City City;
            public long InstitutionId;
            public long CityId;
            public long BuildingId;
            public int TileX;
            public int TileY;
            public long OwnerKingdomId;
        }

        private static readonly Dictionary<long, CaptureState> Captures =
            new Dictionary<long, CaptureState>();

        internal static void Capture(Building pBuilding)
        {
            if (!HistoricalSchoolAcademyService.IsAcademy(pBuilding) ||
                pBuilding?.data == null || pBuilding.current_tile == null) return;
            City city = null;
            try { city = pBuilding.getCity(); }
            catch { }
            city ??= pBuilding.current_tile.zone?.city;
            long cityId = city?.data?.id ?? pBuilding.data.cityID;
            long buildingId = pBuilding.data.id;
            int tileX = pBuilding.current_tile.x;
            int tileY = pBuilding.current_tile.y;
            if (!HistoricalSchoolAcademyRepairRules.ShouldCaptureDestruction(
                    pBuilding.asset.id == SchoolAcademyBuildingContent.BuildingId ||
                    pBuilding.asset.type ==
                    SchoolAcademyBuildingContent.BuildingTypeId,
                    cityId, buildingId, tileX, tileY)) return;
            if (Captures.ContainsKey(buildingId)) return;
            HistoricalSchoolStore.TryResolveAcademyInstitution(cityId, buildingId,
                out long institutionId);
            Captures[buildingId] = new CaptureState
            {
                Building = pBuilding,
                City = city,
                InstitutionId = institutionId,
                CityId = cityId,
                BuildingId = buildingId,
                TileX = tileX,
                TileY = tileY,
                OwnerKingdomId = city?.kingdom?.data?.id ?? -1L
            };
        }

        internal static void ConfirmRemoval(Building pBuilding)
        {
            if (!HistoricalSchoolAcademyService.IsAcademy(pBuilding) ||
                pBuilding?.data == null) return;
            long buildingId = pBuilding.data.id;
            if (!Captures.TryGetValue(buildingId, out CaptureState state))
            {
                Capture(pBuilding);
                if (!Captures.TryGetValue(buildingId, out state)) return;
            }
            Captures.Remove(buildingId);
            HistoricalSchoolAcademyConstructionService.OnAcademyRemoved(
                state.City, state.Building);
            HistoricalSchoolVenueService.ReleaseCityClaims(state.CityId);
            HistoricalSchoolVenueService.InvalidateCity(state.CityId);
            SchoolLandmarkService.MarkDirty(state.CityId);
            CitySchoolSnapshotService.MarkDirtyById(state.CityId);
            if (state.InstitutionId < 0) return;
            bool queued = HistoricalSchoolStore.QueueAcademyRepair(state.InstitutionId,
                state.CityId, state.BuildingId, state.TileX, state.TileY,
                state.OwnerKingdomId, World.world?.getCurWorldTime() ?? 0d);
            if (queued && (state.City?.data == null || state.City.isRekt()))
                HistoricalSchoolStore.CancelAcademyRepair(state.InstitutionId,
                    World.world?.getCurWorldTime() ?? 0d);
        }

        internal static void ClearRuntime()
        {
            Captures.Clear();
        }
    }
}
