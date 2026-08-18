using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolAcademyRepairService
    {
        private static int _repairCursor;

        internal static void LoadState()
        {
            HistoricalSchoolStore.RestoreMissingAcademyRepairTickets(
                World.world?.getCurWorldTime() ?? 0d);
        }

        internal static void ClearRuntime()
        {
            _repairCursor = 0;
        }

        internal static bool ProcessYearFrame(int pYear)
        {
            List<HistoricalSchoolAcademyRepairTicket> tickets =
                HistoricalSchoolStore.LoadAcademyRepairTickets();
            int budget = HistoricalSchoolAcademyRepairRules.RepairBudget(tickets.Count);
            for (int index = 0; index < budget; index++)
                ProcessTicket(tickets[(_repairCursor + index) % tickets.Count]);
            if (tickets.Count > 0)
                _repairCursor = (_repairCursor + budget) % tickets.Count;
            return true;
        }

        internal static void CancelCity(long pCityId)
        {
            if (pCityId < 0) return;
            List<HistoricalSchoolAcademyRepairTicket> tickets =
                HistoricalSchoolStore.LoadAcademyRepairTickets();
            double worldTime = World.world?.getCurWorldTime() ?? 0d;
            for (int index = 0; index < tickets.Count; index++)
            {
                HistoricalSchoolAcademyRepairTicket ticket = tickets[index];
                if (ticket.CityId != pCityId) continue;
                HistoricalSchoolStore.CancelAcademyRepair(ticket.InstitutionId,
                    worldTime);
            }
            SchoolLandmarkService.MarkDirty(pCityId);
            CitySchoolSnapshotService.MarkDirtyById(pCityId);
        }

        internal static void OnConstructionCompleted(Building pBuilding)
        {
            if (!HistoricalSchoolAcademyService.IsAcademy(pBuilding) ||
                pBuilding?.data == null) return;
            List<HistoricalSchoolAcademyRepairTicket> tickets =
                HistoricalSchoolStore.LoadAcademyRepairTickets();
            for (int index = 0; index < tickets.Count; index++)
            {
                HistoricalSchoolAcademyRepairTicket ticket = tickets[index];
                if (ticket.BuildingId != pBuilding.data.id ||
                    ticket.State != HistoricalSchoolAcademyPhysicalState.Rebuilding)
                    continue;
                CompleteAcademy(ticket, pBuilding);
                return;
            }
        }

        private static void ProcessTicket(
            HistoricalSchoolAcademyRepairTicket pTicket)
        {
            if (pTicket == null || pTicket.InstitutionId < 0 || pTicket.CityId < 0)
                return;
            City city = null;
            try { city = World.world?.cities?.get(pTicket.CityId); }
            catch { }
            Kingdom owner = city?.kingdom;
            bool cityExists = city?.data != null;
            bool cityUsable = cityExists && !city.isRekt() && owner?.data != null &&
                              !owner.isRekt() && !owner.isNeutral();
            long ownerId = owner?.data?.id ?? -1L;
            HistoricalSchoolAcademyRepairDisposition disposition =
                HistoricalSchoolAcademyRepairRules.ResolveDisposition(cityExists,
                    cityUsable, ownerId >= 0 && ownerId != pTicket.OwnerKingdomId);
            double worldTime = World.world?.getCurWorldTime() ?? 0d;
            if (disposition == HistoricalSchoolAcademyRepairDisposition.Cancel)
            {
                HistoricalSchoolStore.CancelAcademyRepair(pTicket.InstitutionId,
                    worldTime);
                SchoolLandmarkService.MarkDirty(pTicket.CityId);
                CitySchoolSnapshotService.MarkDirtyById(pTicket.CityId);
                return;
            }
            if (disposition == HistoricalSchoolAcademyRepairDisposition.RebindOwner &&
                HistoricalSchoolStore.RebindAcademyRepairOwner(
                    pTicket.InstitutionId, ownerId, worldTime))
                pTicket.OwnerKingdomId = ownerId;

            Building existing = FindBuilding(pTicket.BuildingId);
            if (!IsRepairBuilding(existing, city))
            {
                Building inProgress = city.under_construction_building;
                if (IsRepairBuilding(inProgress, city)) existing = inProgress;
                else
                {
                    Building usable = HistoricalSchoolAcademyService.FindUsable(city);
                    if (IsRepairBuilding(usable, city)) existing = usable;
                }
            }
            if (disposition == HistoricalSchoolAcademyRepairDisposition.RebindOwner &&
                existing != null)
                existing.setKingdom(owner);
            if (IsRepairBuilding(existing, city))
            {
                if (pTicket.State != HistoricalSchoolAcademyPhysicalState.Rebuilding)
                {
                    if (!HistoricalSchoolStore.MarkAcademyRebuilding(
                            pTicket.InstitutionId, existing.data.id, ownerId, worldTime))
                        return;
                    pTicket.State = HistoricalSchoolAcademyPhysicalState.Rebuilding;
                    pTicket.BuildingId = existing.data.id;
                }
                if (existing.isUnderConstruction()) return;
                if (HistoricalSchoolAcademyService.IsLiveAcademyForCity(existing, city))
                {
                    CompleteAcademy(pTicket, existing);
                    return;
                }
            }

            WorldTile originalTile = null;
            if (pTicket.TileX >= 0 && pTicket.TileY >= 0)
            {
                try { originalTile = World.world?.GetTile(pTicket.TileX, pTicket.TileY); }
                catch { }
            }
            Building academy;
            if (HistoricalSchoolAcademyConstructionService.CanStartAt(city, originalTile))
                academy = HistoricalSchoolAcademyConstructionService.
                    TryStartAt(city, originalTile);
            else
                academy = HistoricalSchoolAcademyConstructionService.TryStart(city);
            if (academy?.data == null) return;
            if (!HistoricalSchoolStore.MarkAcademyRebuilding(
                    pTicket.InstitutionId, academy.data.id, ownerId, worldTime))
            {
                ModClass.LogWarning("School academy repair binding write failed: " +
                                    "institution=" + pTicket.InstitutionId +
                                    " building=" + academy.data.id);
                return;
            }
            pTicket.State = HistoricalSchoolAcademyPhysicalState.Rebuilding;
            pTicket.BuildingId = academy.data.id;
        }

        private static void CompleteAcademy(
            HistoricalSchoolAcademyRepairTicket pTicket, Building pAcademy)
        {
            WorldTile tile = pAcademy?.current_tile;
            if (pTicket == null || pAcademy?.data == null || tile == null) return;
            if (!HistoricalSchoolStore.CompleteAcademyRepair(
                    pTicket.InstitutionId, pAcademy.data.id, tile.x, tile.y,
                    World.world?.getCurWorldTime() ?? 0d)) return;
            long cityId = pTicket.CityId;
            City city = null;
            try { city = World.world?.cities?.get(cityId); }
            catch { }
            if (city != null) CitySchoolSnapshotService.MarkDirty(city);
            else CitySchoolSnapshotService.MarkDirtyById(cityId);
            HistoricalSchoolVenueService.InvalidateCity(cityId);
            SchoolLandmarkService.MarkDirty(cityId);
        }

        private static Building FindBuilding(long pBuildingId)
        {
            if (pBuildingId < 0) return null;
            try { return World.world?.buildings?.get(pBuildingId); }
            catch { return null; }
        }

        private static bool IsRepairBuilding(Building pBuilding, City pCity)
        {
            if (!HistoricalSchoolAcademyService.IsAcademy(pBuilding) ||
                pBuilding?.data == null || !pBuilding.isAlive() ||
                pBuilding.isOnRemove() || pBuilding.isRemoved() ||
                pBuilding.isRuin()) return false;
            City attached = null;
            try { attached = pBuilding.getCity(); }
            catch { }
            return ReferenceEquals(attached, pCity) || attached == null &&
                   ReferenceEquals(pBuilding.current_tile?.zone?.city, pCity);
        }
    }
}
