using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.county;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.court
{
    internal static class CourtReadModelService
    {
        private const float HorizontalSpacing = 142f;
        private const float VerticalSpacing = 116f;

        public static List<CourtPyramidNodeModel> Build(Kingdom pKingdom)
        {
            var seeds = new List<CourtPyramidNodeModel>();
            if (pKingdom?.data == null || pKingdom.isRekt()) return seeds;

            string tier = CourtService.ResolveTier(pKingdom);
            CustomCourtTemplate customSnapshot;
            IReadOnlyDictionary<string, int> customRanks =
                CustomCourtRuntime.TryGetSnapshot(pKingdom,
                    out customSnapshot)
                    ? CustomCourtHierarchyLayoutRules.BuildRanks(
                        customSnapshot.Offices, customSnapshot.Edges)
                    : null;
            AddKing(seeds, pKingdom);
            AddPrimitiveHeir(seeds, pKingdom, tier);
            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(pKingdom, 96);
            AddOfficersAndVacancies(seeds, pKingdom, officers, tier,
                customRanks);
            AddGenerals(seeds, pKingdom);
            AddMilitaryGovernorates(seeds, pKingdom);
            AddFeudatoryPrinces(seeds, pKingdom);
            AddRegionalGovernmentNodes(seeds, pKingdom);
            List<CourtPyramidNodeModel> result = CourtPyramidRules.BuildLayout(
                seeds, HorizontalSpacing, VerticalSpacing);
            AddCachedHeirRole(result, pKingdom);
            ApplyCareerStates(result,
                OfficialCareerStateService.LoadKingdomStates(pKingdom.id),
                CourtService.HasNineRankSystem(pKingdom));
            return result;
        }

        public static List<LocalCourtReadModel> BuildLocalGovernments(
            Kingdom pKingdom)
        {
            var result = new List<LocalCourtReadModel>();
            if (pKingdom?.data == null || pKingdom.isRekt()) return result;
            IEnumerable<City> cities;
            try
            {
                cities = pKingdom.getCities().Where(city =>
                    city?.data != null && !city.isRekt() &&
                    city.kingdom == pKingdom).OrderBy(city => city.data.id)
                    .ToList();
            }
            catch
            {
                return result;
            }
            Dictionary<long, CityBureauView> bureaus = CourtService
                .GetCityBureaus(pKingdom, 256).Where(row => row != null &&
                    row.city_id >= 0).GroupBy(row => row.city_id)
                .ToDictionary(group => group.Key, group => group.First());
            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(
                    pKingdom, int.MaxValue).Where(row => row != null &&
                    (row.layer == CourtOfficeLayer.City ||
                     row.layer == CourtOfficeLayer.County)).ToList();
            Dictionary<long, OfficialCareerStateView> careerStates =
                OfficialCareerStateService.LoadKingdomStates(pKingdom.id);
            foreach (City city in cities)
            {
                LocalCourtReadModel local = BuildLocal(pKingdom, city,
                    bureaus, officers, careerStates);
                if (local != null) result.Add(local);
            }
            return result;
        }

        public static LocalCourtReadModel BuildLocal(Kingdom pKingdom,
            City pCity)
        {
            Dictionary<long, CityBureauView> bureaus = CourtService
                .GetCityBureaus(pKingdom, 256).Where(row => row != null &&
                    row.city_id >= 0).GroupBy(row => row.city_id)
                .ToDictionary(group => group.Key, group => group.First());
            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(
                    pKingdom, int.MaxValue).Where(row => row != null &&
                    (row.layer == CourtOfficeLayer.City ||
                     row.layer == CourtOfficeLayer.County)).ToList();
            return BuildLocal(pKingdom, pCity, bureaus, officers,
                OfficialCareerStateService.LoadKingdomStates(
                    pKingdom?.id ?? -1L));
        }

        private static void AddRegionalGovernmentNodes(
            List<CourtPyramidNodeModel> pSeeds, Kingdom pKingdom)
        {
            IReadOnlyList<RegionalGovernmentReadModel> regions =
                RegionalGovernmentAggregationService.Build(pKingdom);
            int order = 0;
            foreach (RegionalGovernmentReadModel region in regions)
            {
                City seatCity = FindCity(pKingdom, region.EffectiveSeatCityId);
                EnsureNativeCityLeaderProjection(pKingdom, seatCity);
                Actor governor = seatCity?.leader;
                if (!IsCurrentRegionalGovernor(governor, pKingdom,
                        seatCity) && region.GovernorActorId >= 0L)
                    governor = World.world?.units?.get(
                        region.GovernorActorId);
                bool liveGovernor = IsCurrentRegionalGovernor(governor,
                    pKingdom, seatCity);
                string nodeId = "regional-folder:" + pKingdom.id + ":" +
                    region.RegionId;
                var node = new CourtPyramidNodeModel(liveGovernor
                        ? governor.data.id : -1L, nodeId,
                    CourtPyramidRoleId.RegionalGovernor,
                    CourtPyramidRules.GovernorRank - 1, order++, !liveGovernor)
                {
                    OfficeLayer = CourtOfficeLayer.Regional,
                    ActorName = liveGovernor ? SafeActorName(governor) : "",
                    SchoolId = liveGovernor ? ActorSchool(governor, "") : "",
                    SchoolIconPath = liveGovernor
                        ? RegisteredSchoolIconPath(ActorSchool(governor, ""))
                        : "",
                    CityId = region.EffectiveSeatCityId,
                    HistoryCityId = region.EffectiveSeatCityId,
                    HistoryOfficeLayer = CourtOfficeLayer.City,
                    HistoryOfficeId = CourtService.ResolveCityOffice(
                        pKingdom, seatCity),
                    CityName = FindCityName(pKingdom, region.EffectiveSeatCityId),
                    CommandName = RegionalGovernmentCommandName(
                        region.RegionName, region.RegionTitle,
                        region.GovernorTitle,
                        FindCityName(pKingdom, region.EffectiveSeatCityId),
                        region.MemberCount),
                    DisplayTitle = region.GovernorTitle,
                    Influence = liveGovernor ? SafeStat(governor, "stewardship") : 0f
                };
                node.IsFixedRole = true;
                pSeeds.Add(node);
            }
        }

        private static LocalCourtReadModel BuildLocal(Kingdom pKingdom,
            City pCity, IReadOnlyDictionary<long, CityBureauView> pBureaus,
            IReadOnlyList<CourtOfficerView> pOfficers,
            Dictionary<long, OfficialCareerStateView> pCareerStates)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pKingdom.isRekt() || pCity.isRekt() ||
                pCity.kingdom != pKingdom) return null;

            CityBureauView bureau = null;
            pBureaus?.TryGetValue(pCity.data.id, out bureau);
            var model = new LocalCourtReadModel
            {
                KingdomId = pKingdom.id,
                CityId = pCity.data.id,
                CityName = DeJureRegionStore.ResolveCountyNameForPresentation(
                    pCity),
                ActiveSeats = 0,
                TotalSeats = Math.Max(1, bureau?.office_slots ?? 1),
                Efficiency = bureau?.efficiency ?? 0f,
                LocalSchoolId = bureau?.local_school ?? string.Empty,
                CountryCorruption = CorruptionService.ReadCountry(pKingdom),
                CityCorruption = CorruptionService.ReadCity(pCity)
            };
            model.Counties = CountyAdministrationService.CountiesForCity(
                pCity.data.id).Where(county => county != null && county.Active)
                .OrderBy(county => county.Ordinal).ToList();
            if (RegionalGovernmentAggregationService.TryFindRegion(pKingdom,
                    pCity.data.id, out RegionalGovernmentReadModel region))
            {
                model.RegionSeatCityId = region.EffectiveSeatCityId;
                model.RegionName = region.RegionName;
                model.RegionTitle = region.RegionTitle;
                model.RegionalGovernorTitle = region.GovernorTitle;
                model.LocalLevelTitle = region.LocalLevelTitle;
                model.RegionMemberCount = region.MemberCount;
                model.RegionalGovernorActorId = region.GovernorActorId;
            }

            CustomLocalCourtTemplate localTemplate;
            if (CustomCourtRuntime.TryGetLocalTemplate(pKingdom, pCity,
                    out localTemplate))
            {
                model.TemplateId = localTemplate.Id ?? string.Empty;
                model.TemplateName = CustomLocalCourtTemplateRules.CityTypeName(
                    localTemplate,
                    HistoryLocalizationRules.CurrentLanguage() == "en");
                model.CityTypeName = model.TemplateName;
                model.Edges = (localTemplate.Edges ??
                    new List<CustomCourtEdge>()).Where(edge => edge != null)
                    .ToList();
            }
            else
            {
                model.TemplateName = AW_L10n.Text("aw_local_court_builtin_type",
                    "Local Government");
                model.CityTypeName = model.TemplateName;
            }

            List<CourtOfficerView> officers = (pOfficers ??
                    Array.Empty<CourtOfficerView>())
                .Where(row => row != null &&
                    row.layer == CourtOfficeLayer.City &&
                    row.city_id == pCity.data.id).ToList();
            List<string> seats = BuildLocalSeats(pKingdom, pCity,
                localTemplate, model.TotalSeats);
            if (seats.Count > model.TotalSeats) model.TotalSeats = seats.Count;
            EnsurePersistedLocalLeader(pKingdom, pCity, seats, officers);
            if (seats.Count > 0 && !officers.Any(row =>
                    row.office_id == seats[0]))
                officers = CourtService.GetActiveOfficers(pKingdom, int.MaxValue)
                    .Where(row => row != null &&
                        row.layer == CourtOfficeLayer.City &&
                        row.city_id == pCity.data.id).ToList();
            AddLocalNodes(model, pKingdom, pCity, seats, officers,
                localTemplate, pCareerStates);
            AddCountyNodes(model, pKingdom, pCity, pOfficers, pCareerStates);
            AddRegionalSuperiorNode(model, pKingdom, pCity);
            // The regional superior is part of the local court graph and must
            // participate in the same layout pass as the city officials.
            // Otherwise it keeps the default (0, 0) position and can be
            // hidden behind another node or rendered on top of it.
            LayoutLocalHierarchy(model.Nodes);
            model.ActiveSeats = model.Nodes.Count(node =>
                node != null && (node.OfficeLayer == CourtOfficeLayer.City ||
                    node.OfficeLayer == CourtOfficeLayer.County) &&
                !node.IsVacancy && node.ActorId >= 0);
            model.LeaderNode = model.Nodes.FirstOrDefault(node =>
                node?.OfficeLayer == CourtOfficeLayer.City &&
                node.ActorId == pCity.leader?.data?.id);
            return model;
        }

        private static void EnsurePersistedLocalLeader(Kingdom pKingdom,
            City pCity, IReadOnlyList<string> pSeats,
            IReadOnlyList<CourtOfficerView> pOfficers)
        {
            if (pSeats == null || pSeats.Count == 0 ||
                pCity?.leader?.data == null || pCity.kingdom != pKingdom ||
                pOfficers?.Any(row => row != null &&
                    row.office_id == pSeats[0]) == true) return;
            EnsureNativeCityLeaderProjection(pKingdom, pCity);
            if (pCity.leader?.data == null) return;
            // Older saves can have a valid city leader projected in the UI
            // without the corresponding career row. Backfill once through
            // the normal appointment path so history is persistent.
            CourtService.TryAssignLocalOfficerRecord(pCity.leader, pKingdom,
                pCity, pSeats[0], pVacancyPromotion: true);
        }

        private static void EnsureNativeCityLeaderProjection(Kingdom pKingdom,
            City pCity)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom ||
                pCity.leader?.data == null) return;
            bool nativeCityLeader;
            try { nativeCityLeader = pCity.leader.isCityLeader(); }
            catch { return; }
            if (nativeCityLeader) return;
            string officeId = CourtService.ResolveCityOffice(pKingdom, pCity);
            if (string.IsNullOrEmpty(officeId)) return;
            CourtService.TryAssignLocalOfficer(pCity.leader, pKingdom,
                pCity, officeId, pVacancyPromotion: true);
        }

        private static void AddRegionalSuperiorNode(LocalCourtReadModel pModel,
            Kingdom pKingdom, City pCity)
        {
            if (pModel == null || pCity?.data == null ||
                pModel.RegionSeatCityId < 0L) return;
            City seatCity = null;
            try
            {
                seatCity = pKingdom?.getCities()?.FirstOrDefault(city =>
                    city?.data?.id == pModel.RegionSeatCityId &&
                    !city.isRekt() && city.kingdom == pKingdom);
            }
            catch { }
            Actor governor = seatCity?.leader;
            EnsureNativeCityLeaderProjection(pKingdom, seatCity);
            governor = seatCity?.leader;
            if (!IsCurrentRegionalGovernor(governor, pKingdom, seatCity) &&
                pModel.RegionalGovernorActorId >= 0L)
                governor = World.world?.units?.get(
                    pModel.RegionalGovernorActorId);
            if (!IsCurrentRegionalGovernor(governor, pKingdom, seatCity))
            {
                pModel.RegionalSuperiorNode = new CourtPyramidNodeModel(-1L,
                        "regional-folder:" + pKingdom.id + ":" +
                        pModel.RegionSeatCityId,
                        CourtPyramidRoleId.RegionalGovernor,
                        CourtPyramidRules.KingRank, -1, true)
                    {
                        OfficeLayer = CourtOfficeLayer.Regional,
                        CityId = pModel.RegionSeatCityId,
                        HistoryCityId = pModel.RegionSeatCityId,
                        HistoryOfficeLayer = CourtOfficeLayer.City,
                        HistoryOfficeId = CourtService.ResolveCityOffice(
                            pKingdom, seatCity),
                        CityName = FindCityName(pKingdom, pModel.RegionSeatCityId),
                        DisplayTitle = pModel.RegionalGovernorTitle
                    };
                pModel.RegionalSuperiorNode.IsFixedRole = true;
                pModel.Nodes.Insert(0, pModel.RegionalSuperiorNode);
                return;
            }
            string school = ActorSchool(governor, "");
            CourtPyramidNodeModel node = new CourtPyramidNodeModel(
                    governor.data.id,
                    "regional-folder:" + pKingdom.id + ":" +
                    pModel.RegionSeatCityId,
                    CourtPyramidRoleId.RegionalGovernor,
                    CourtPyramidRules.KingRank, -1, false)
                {
                    OfficeLayer = CourtOfficeLayer.Regional,
                    ActorName = SafeActorName(governor),
                    SchoolId = school,
                    SchoolIconPath = RegisteredSchoolIconPath(school),
                    CityId = pModel.RegionSeatCityId,
                    HistoryCityId = pModel.RegionSeatCityId,
                    HistoryOfficeLayer = CourtOfficeLayer.City,
                    HistoryOfficeId = CourtService.ResolveCityOffice(
                        pKingdom, seatCity),
                    CityName = FindCityName(pKingdom, pModel.RegionSeatCityId),
                    CommandName = RegionalGovernmentCommandName(
                        pModel.RegionName, pModel.RegionTitle,
                        pModel.RegionalGovernorTitle,
                        FindCityName(pKingdom, pModel.RegionSeatCityId),
                        pModel.RegionMemberCount),
                    DisplayTitle = pModel.RegionalGovernorTitle
                };
            node.IsFixedRole = true;
            pModel.RegionalSuperiorNode = node;
            pModel.Nodes.Insert(0, node);
        }

        private static string RegionalGovernmentCommandName(
            string pRegionName, string pRegionTitle, string pGovernorTitle,
            string pSeatCityName, int pMemberCount)
        {
            return string.Format(AW_L10n.Text(
                    "aw_court_regional_node_summary",
                    "{0} | {1} | Seat {2} | {3} prefectures"),
                RegionalGovernmentRules.AdministrativeLabel(
                    pRegionName, pRegionTitle),
                pGovernorTitle,
                pSeatCityName,
                Math.Max(1, pMemberCount));
        }

        private static List<string> BuildLocalSeats(Kingdom pKingdom,
            City pCity, CustomLocalCourtTemplate pTemplate, int pCapacity)
        {
            int capacity = Math.Max(1, pCapacity);
            var result = new List<string>();
            if (pTemplate != null)
                return LocalChiefOfficeResolver.ResolveOrderedSeats(
                    pKingdom, pCity, capacity).ToList();

            string leaderOffice = CourtService.ResolveCityOffice(pKingdom,
                pCity);
            for (int slot = 0; slot < capacity; slot++)
            {
                string office = LocalCourtOfficeRules.OfficeForSlot(slot,
                    leaderOffice);
                if (!string.IsNullOrEmpty(office)) result.Add(office);
            }
            return result;
        }

        private static void AddLocalNodes(LocalCourtReadModel pModel,
            Kingdom pKingdom, City pCity, IReadOnlyList<string> pSeats,
            IReadOnlyList<CourtOfficerView> pOfficers,
            CustomLocalCourtTemplate pTemplate,
            Dictionary<long, OfficialCareerStateView> pCareerStates)
        {
            var remaining = (pOfficers ?? Array.Empty<CourtOfficerView>())
                .Where(row => row != null).ToList();
            IReadOnlyDictionary<string, int> ranks = pTemplate == null
                ? null
                : CustomCourtHierarchyLayoutRules.BuildRanks(
                    pTemplate.Offices, pTemplate.Edges);
            for (int index = 0; index < pSeats.Count; index++)
            {
                string officeId = pSeats[index];
                CourtOfficerView officer = remaining.FirstOrDefault(row =>
                    row.office_id == officeId);
                if (officer != null) remaining.Remove(officer);
                Actor actor = officer == null ? null :
                    World.world?.units?.get(officer.actor_id);
                bool rootLeader = index == 0 && IsCurrentCityLeader(pCity,
                    pKingdom);
                if (rootLeader)
                {
                    officer = null;
                    actor = pCity.leader;
                }
                bool valid = rootLeader || IsValid(actor, pKingdom);
                int graphRank = ranks != null &&
                    ranks.TryGetValue(officeId, out int resolvedRank)
                    ? resolvedRank : index;
                var node = new CourtPyramidNodeModel(valid
                        ? actor.data.id : -1L, officeId, officeId,
                    CourtPyramidRules.GovernorRank + graphRank * 10,
                    index, !valid)
                {
                    OfficeLayer = CourtOfficeLayer.City,
                    CityId = pCity.data.id,
                    CityName = DeJureRegionStore.ResolveCountyNameForPresentation(
                        pCity),
                    DisplayTitle = pModel.LocalLevelTitle ?? string.Empty,
                    ActorName = valid ? SafeActorName(actor) : string.Empty,
                    SchoolId = valid ? ActorSchool(actor, "") :
                        CourtSchoolId.None,
                    SchoolIconPath = valid
                        ? RegisteredSchoolIconPath(ActorSchool(actor, ""))
                        : string.Empty,
                    AppointmentYear = valid
                        ? officer?.appointed_year ?? -1 : -1,
                    Influence = valid
                        ? officer?.influence ?? SafeStat(actor, "stewardship")
                        : 0f
                };
                pModel.Nodes.Add(node);
            }

            if (pSeats.Count == 0 && IsCurrentCityLeader(pCity, pKingdom))
            {
                string officeId = CourtService.ResolveCityOffice(pKingdom,
                    pCity);
                pModel.Nodes.Add(new CourtPyramidNodeModel(
                    pCity.leader.data.id, officeId, officeId,
                    CourtPyramidRules.GovernorRank, 0, false)
                {
                    OfficeLayer = CourtOfficeLayer.City,
                    CityId = pCity.data.id,
                    CityName = DeJureRegionStore.ResolveCountyNameForPresentation(
                        pCity),
                    DisplayTitle = pModel.LocalLevelTitle ?? string.Empty,
                    ActorName = SafeActorName(pCity.leader),
                    SchoolId = ActorSchool(pCity.leader, ""),
                    SchoolIconPath = RegisteredSchoolIconPath(
                        ActorSchool(pCity.leader, "")),
                    Influence = SafeStat(pCity.leader, "stewardship")
                });
            }

            pModel.Nodes = pModel.Nodes.OrderBy(node => node.Rank)
                .ThenBy(node => node.StableOrder)
                .ThenBy(node => node.IsVacancy)
                .ThenBy(node => node.ActorId).ToList();
            LayoutLocalHierarchy(pModel.Nodes);
            ApplyCareerStates(pModel.Nodes, pCareerStates,
                CourtService.HasNineRankSystem(pKingdom));
        }

        private static void AddCountyNodes(LocalCourtReadModel pModel,
            Kingdom pKingdom, City pCity, IReadOnlyList<CourtOfficerView> pOfficers,
            Dictionary<long, OfficialCareerStateView> pCareerStates)
        {
            if (pModel == null || pCity?.data == null ||
                pModel.Counties == null || pModel.Counties.Count == 0) return;
            int order = pModel.Nodes.Count + 1;
            foreach (CountyRecord county in pModel.Counties)
            {
                CourtOfficerView officer = (pOfficers ??
                        Array.Empty<CourtOfficerView>()).FirstOrDefault(row =>
                    row != null && row.layer == CourtOfficeLayer.County &&
                    row.city_id == pCity.data.id &&
                    row.county_id == county.CountyId &&
                    row.office_id == CourtOfficeId.CountyMagistrate);
                Actor actor = officer == null ? null :
                    World.world?.units?.get(officer.actor_id);
                bool valid = IsValid(actor, pKingdom);
                int chiefRank = pModel.Nodes.Where(item => item != null &&
                        item.OfficeLayer == CourtOfficeLayer.City)
                    .Select(item => item.Rank).DefaultIfEmpty(
                        CourtPyramidRules.GovernorRank).Min();
                var node = new CourtPyramidNodeModel(valid ? actor.data.id : -1L,
                    CourtOfficeId.CountyMagistrate,
                    CourtOfficeId.CountyMagistrate, chiefRank + 10,
                    order++, !valid)
                {
                    OfficeLayer = CourtOfficeLayer.County,
                    CityId = pCity.data.id,
                    CountyId = county.CountyId,
                    CityName = county.Name ?? string.Empty,
                    DisplayTitle = AW_L10n.Text("aw_county_label", "County"),
                    ActorName = valid ? SafeActorName(actor) : string.Empty,
                    SchoolId = valid ? ActorSchool(actor, "") : CourtSchoolId.None,
                    SchoolIconPath = valid ? RegisteredSchoolIconPath(
                        ActorSchool(actor, "")) : string.Empty,
                    AppointmentYear = valid ? officer?.appointed_year ?? -1 : -1,
                    Influence = valid ? officer?.influence ?? SafeStat(actor,
                        "stewardship") : 0f
                };
                pModel.Nodes.Add(node);
            }
            pModel.Nodes = pModel.Nodes.OrderBy(node => node.Rank)
                .ThenBy(node => node.StableOrder)
                .ThenBy(node => node.IsVacancy)
                .ThenBy(node => node.ActorId).ToList();
            LayoutLocalHierarchy(pModel.Nodes);
            ApplyCareerStates(pModel.Nodes, pCareerStates,
                CourtService.HasNineRankSystem(pKingdom));
        }

        private static void LayoutLocalHierarchy(
            IReadOnlyList<CourtPyramidNodeModel> pNodes)
        {
            int rowIndex = 0;
            foreach (IGrouping<int, CourtPyramidNodeModel> row in
                     (pNodes ?? Array.Empty<CourtPyramidNodeModel>())
                     .Where(node => node != null).GroupBy(node => node.Rank)
                     .OrderBy(group => group.Key))
            {
                CourtPyramidNodeModel[] nodes = row.OrderBy(node =>
                        node.StableOrder).ThenBy(node => node.ActorId)
                    .ToArray();
                float startX = -(nodes.Length - 1) * HorizontalSpacing * .5f;
                for (int index = 0; index < nodes.Length; index++)
                {
                    nodes[index].X = startX + index * HorizontalSpacing;
                    nodes[index].Y = -rowIndex * VerticalSpacing;
                }
                rowIndex++;
            }
        }

        private static void AddKing(List<CourtPyramidNodeModel> pSeeds, Kingdom pKingdom)
        {
            Actor king = pKingdom.king;
            if (!IsValidKing(king, pKingdom)) return;
            string school = ActorSchool(king, "");
            pSeeds.Add(new CourtPyramidNodeModel(king.data.id, CourtPyramidRoleId.King,
                CourtPyramidRoleId.King, CourtPyramidRules.KingRank, 0, false)
            {
                ActorName = SafeActorName(king),
                SchoolId = school,
                SchoolIconPath = RegisteredSchoolIconPath(school),
                Influence = 100f,
                IsFixedRole = true
            });
        }

        private static bool IsValidKing(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pKingdom.isRekt()) return false;
            try
            {
                return pActor.isAlive() && !pActor.isRekt() &&
                       (pActor.kingdom == pKingdom || pActor.isKing());
            }
            catch { return false; }
        }

        private static void AddPrimitiveHeir(List<CourtPyramidNodeModel> pSeeds,
            Kingdom pKingdom, string pTier)
        {
            Actor heir = HeirService.PeekRegisteredHeir(pKingdom);
            if (!CourtPyramidRules.ShouldAddStandaloneHeir(pTier, heir?.data != null)) return;
            string school = ActorSchool(heir, "");
            pSeeds.Add(new CourtPyramidNodeModel(heir.data.id, CourtPyramidRoleId.Heir,
                CourtPyramidRoleId.Heir, CourtPyramidRules.HeirRank, 0, false)
            {
                ActorName = SafeActorName(heir),
                SchoolId = school,
                SchoolIconPath = RegisteredSchoolIconPath(school),
                Influence = SafeStat(heir, "stewardship")
            });
        }

        private static void AddOfficersAndVacancies(List<CourtPyramidNodeModel> pSeeds,
            Kingdom pKingdom, List<CourtOfficerView> pOfficers, string pTier,
            IReadOnlyDictionary<string, int> pCustomRanks)
        {
            string[] expected = CourtService.CentralOfficeIdsForCurrentProfile(
                    pKingdom)
                .Concat(CourtProfileRegistry.OfficeIdsForLayer(pKingdom,
                    CourtOfficeLayer.Military))
                .ToArray();
            // 自定义官场里既有 central 也有 censor / feudatory 层 —— 但
            // CentralOfficeIdsForCurrentProfile 只精确匹配 "central",于是
            // 用户自定义的「监察」层官职(设置图里「官场层级=监察」)从 expected
            // 里消失:在职的(604)被 continue,空缺的(648)不生成,界面上就是
            // 缺一格。customGraph 下应把自定义模板里全部非 city/county 层的
            // 权威官职都纳入 expected。
            if (CustomCourtRuntime.HasInstance(pKingdom))
                expected = CustomCourtRuntime.Resolver.ResolveGraph(
                        CustomCourtRuntime.KingdomKey(pKingdom),
                        CourtProfileRegistry.For(pKingdom),
                        CourtInstitutionService.GetInstitution(pKingdom))
                    .Where(p => p != null &&
                        !string.Equals(p.Layer, CourtOfficeLayer.City,
                            StringComparison.Ordinal) &&
                        !string.Equals(p.Layer, CourtOfficeLayer.County,
                            StringComparison.Ordinal))
                    .Select(p => p.Id).ToArray();
            var expectedOrder = new Dictionary<string, int>();
            for (int i = 0; i < expected.Length; i++) expectedOrder[expected[i]] = i;
            bool customGraph = CustomCourtRuntime.HasInstance(pKingdom);

            var filled = new HashSet<string>();
            foreach (CourtOfficerView officer in pOfficers ?? new List<CourtOfficerView>())
            {
                // Local officials are rendered in their city government card.
                // Keeping either layer here duplicates them in the central tree.
                if (string.Equals(officer.layer, CourtOfficeLayer.City,
                        System.StringComparison.Ordinal) ||
                    string.Equals(officer.layer, CourtOfficeLayer.County,
                        System.StringComparison.Ordinal)) continue;
                if (customGraph && !expectedOrder.ContainsKey(
                        officer.office_id)) continue;
                Actor actor = World.world?.units?.get(officer.actor_id);
                if (!IsValid(actor, pKingdom)) continue;
                int order = expectedOrder.TryGetValue(officer.office_id, out int officeOrder)
                    ? officeOrder
                    : expected.Length + StableStringOrder(officer.office_id);
                int rank = pCustomRanks != null &&
                    pCustomRanks.TryGetValue(officer.office_id,
                        out int customRank)
                    ? customRank
                    : officer.layer == CourtOfficeLayer.Feudatory
                    ? FeudatoryOfficeRules.InspectorRank
                    : officer.layer == CourtOfficeLayer.City
                    ? CourtPyramidRules.GovernorRank
                    : officer.layer == CourtOfficeLayer.Military
                        ? CourtPyramidRules.GeneralRank
                        : CourtPyramidRules.RankForOffice(officer.office_id);
                string school = string.IsNullOrEmpty(officer.school_id)
                    ? ActorSchool(actor, "")
                    : officer.school_id;
                HistoricalSchoolAffiliationSnapshot affiliation =
                    HistoricalAffiliationService.Get(actor.data.id);
                bool guest = affiliation?.LifecycleState ==
                             HistoricalSchoolLifecycleState.Serving &&
                             affiliation.ServiceKingdomId == pKingdom.id &&
                             affiliation.HomeKingdomId != pKingdom.id;
                pSeeds.Add(new CourtPyramidNodeModel(actor.data.id, officer.office_id,
                    officer.office_id, rank, order, false)
                {
                    ActorName = SafeActorName(actor) + (guest
                        ? " (" + AW_L10n.Text("aw_school_guest_service", "Guest") + ")"
                        : ""),
                    OfficeLayer = officer.layer ?? "",
                    SchoolId = school,
                    SchoolIconPath = RegisteredSchoolIconPath(school),
                    CityId = officer.city_id,
                    CityName = FindCityName(pKingdom, officer.city_id),
                    AppointmentYear = officer.appointed_year,
                    Influence = officer.influence
                });
                filled.Add(officer.office_id);
            }

            for (int i = 0; i < expected.Length; i++)
            {
                string office = expected[i];
                if (filled.Contains(office)) continue;
                CourtOfficeDefinition definition =
                    CourtProfileRegistry.FindOffice(pKingdom, office);
                int rank = pCustomRanks != null &&
                    pCustomRanks.TryGetValue(office, out int customRank)
                    ? customRank
                    : definition?.Layer == CourtOfficeLayer.Military
                    ? CourtPyramidRules.GeneralRank
                    : CourtPyramidRules.RankForOffice(office);
                pSeeds.Add(new CourtPyramidNodeModel(-1L, office, office,
                    rank, i, true)
                {
                    OfficeLayer = definition?.Layer ?? "",
                    SchoolId = CourtSchoolId.None,
                    SchoolIconPath = ""
                });
            }
        }

        private static void AddGenerals(List<CourtPyramidNodeModel> pSeeds, Kingdom pKingdom)
        {
            int order = 0;
            foreach (GeneralReadModelEntry entry in GeneralService.GetActiveGeneralsForReadModel(
                         pKingdom, pAllowUnitFallback: false)
                         .OrderByDescending(p => p.Merit)
                         .ThenBy(p => p.Actor.data.id))
            {
                Actor general = entry.Actor;
                int merit = entry.Merit;
                pSeeds.Add(new CourtPyramidNodeModel(general.data.id, CourtPyramidRoleId.General,
                    CourtPyramidRoleId.General, CourtPyramidRules.GeneralRank, order++, false)
                {
                    OfficeLayer = CourtOfficeLayer.Military,
                    ActorName = SafeActorName(general),
                    SchoolId = ActorSchool(general, ""),
                    SchoolIconPath = RegisteredSchoolIconPath(ActorSchool(general, "")),
                    CityId = general.city?.data?.id ?? -1L,
                    CityName = general.city?.data?.name ?? "",
                    AppointmentYear = entry.AppointmentYear,
                    Influence = merit,
                    Merit = merit
                });
            }
        }

        private static void AddMilitaryGovernorates(
            List<CourtPyramidNodeModel> pSeeds, Kingdom pKingdom)
        {
            List<Kingdom> directSubjects = VassalService.GetVassals(pKingdom)
                .Where(p => p?.data != null && !p.isRekt())
                .OrderBy(p => p.id)
                .ToList();
            if (directSubjects.Count == 0) return;

            var snapshots = new Dictionary<long,
                MilitaryGovernorateSnapshot>();
            foreach (MilitaryGovernorateSnapshot snapshot in
                     MilitaryGovernorateStore.GetDirectActive(pKingdom, 256))
            {
                if (snapshot == null || snapshot.SubjectKingdomId < 0)
                    continue;
                if (!snapshots.TryGetValue(snapshot.SubjectKingdomId,
                        out MilitaryGovernorateSnapshot current) ||
                    current.StateId < snapshot.StateId)
                    snapshots[snapshot.SubjectKingdomId] = snapshot;
            }

            int order = 0;
            foreach (Kingdom subject in directSubjects)
            {
                bool military = VassalService.GetSubjectKind(subject) ==
                                VassalSubjectKind.MilitaryGovernorate;
                bool projected =
                    MilitaryGovernorateStore.TryGetRuntimeProjection(
                        subject, out long stateId,
                        out long projectedSuccessorId);
                bool hasSnapshot = snapshots.TryGetValue(subject.id,
                    out MilitaryGovernorateSnapshot snapshot) &&
                    snapshot.SuzerainKingdomId == pKingdom.id &&
                    snapshot.SubjectKingdomId == subject.id &&
                    snapshot.StateId == stateId;
                if (!MilitaryGovernorateCourtRules.ShouldInclude(
                        pIsDirectVassal: true,
                        pIsMilitaryGovernorate: military,
                        pProjectionActive: projected && hasSnapshot))
                    continue;

                int stableOrder =
                    MilitaryGovernorateCourtRules.StableOrderBase + order++;
                Actor governor = subject.king;
                if (!IsValidSubjectActor(governor, subject) ||
                    governor.data.id != snapshot.GovernorActorId)
                    continue;
                string seatName = FindCityName(subject,
                    snapshot.SeatCityId);
                string governorSchool = ActorSchool(governor, "");
                pSeeds.Add(new CourtPyramidNodeModel(governor.data.id,
                    CourtPyramidRoleId.MilitaryGovernorateGovernor,
                    CourtPyramidRoleId.MilitaryGovernorateGovernor,
                    CourtPyramidRules.MilitaryGovernorateGovernorRank,
                    stableOrder, false)
                {
                    OfficeLayer = CourtOfficeLayer.Military,
                    ActorName = SafeActorName(governor),
                    SchoolId = governorSchool,
                    SchoolIconPath = RegisteredSchoolIconPath(
                        governorSchool),
                    CityId = snapshot.SeatCityId,
                    CityName = seatName,
                    CommandName = snapshot.CommandName ?? "",
                    Influence = SafeStat(governor, "warfare")
                });

                if (projectedSuccessorId < 0 ||
                    projectedSuccessorId != snapshot.SuccessorActorId)
                    continue;
                Actor successor = World.world?.units?.get(
                    projectedSuccessorId);
                if (!IsValidSubjectActor(successor, subject)) continue;
                string successorSchool = ActorSchool(successor, "");
                pSeeds.Add(new CourtPyramidNodeModel(successor.data.id,
                    CourtPyramidRoleId.MilitaryGovernorateSuccessor,
                    CourtPyramidRoleId.MilitaryGovernorateSuccessor,
                    CourtPyramidRules.MilitaryGovernorateSuccessorRank,
                    stableOrder, false)
                {
                    OfficeLayer = CourtOfficeLayer.Military,
                    ActorName = SafeActorName(successor),
                    SchoolId = successorSchool,
                    SchoolIconPath = RegisteredSchoolIconPath(
                        successorSchool),
                    CityId = snapshot.SeatCityId,
                    CityName = seatName,
                    CommandName = snapshot.CommandName ?? "",
                    Influence = SafeStat(successor, "warfare")
                });
            }
        }

        private static void AddFeudatoryPrinces(
            List<CourtPyramidNodeModel> pSeeds, Kingdom pKingdom)
        {
            IReadOnlyList<FeudatorySnapshot> rows =
                FeudatoryService.GetByKingdom(pKingdom.id);
            for (int i = 0; i < rows.Count; i++)
            {
                FeudatorySnapshot snapshot = rows[i];
                Actor prince = World.world?.units?.get(snapshot.PrinceActorId);
                if (!IsValid(prince, pKingdom)) continue;
                string school = ActorSchool(prince, "");
                pSeeds.Add(new CourtPyramidNodeModel(prince.data.id,
                    CourtPyramidRoleId.FeudatoryPrince,
                    CourtPyramidRoleId.FeudatoryPrince,
                    FeudatoryOfficeRules.PrinceRank, i, false)
                {
                    OfficeLayer = CourtOfficeLayer.Feudatory,
                    ActorName = SafeActorName(prince),
                    SchoolId = school,
                    SchoolIconPath = RegisteredSchoolIconPath(school),
                    CityId = snapshot.SeatCityId,
                    CityName = snapshot.SeatName,
                    Influence = 30f + snapshot.Autonomy * 0.3f
                });
            }
        }

        private static void AddCityLeaders(List<CourtPyramidNodeModel> pSeeds, Kingdom pKingdom)
        {
            int order = 0;
            IEnumerable<City> cities;
            try { cities = pKingdom.getCities().Where(p => p?.data != null && !p.isRekt()).ToList(); }
            catch { return; }
            foreach (City city in cities.OrderBy(p => p.data.id))
            {
                Actor leader = city.leader;
                if (!IsValid(leader, pKingdom)) continue;
                string office = CourtService.ResolveCityOffice(pKingdom,
                    city);
                if (string.IsNullOrEmpty(office))
                    office = CourtOfficeId.Governor;
                string school = ActorSchool(leader, "");
                pSeeds.Add(new CourtPyramidNodeModel(leader.data.id, office,
                    office, CourtPyramidRules.GovernorRank, order++, false)
                {
                    OfficeLayer = CourtOfficeLayer.City,
                    ActorName = SafeActorName(leader),
                    SchoolId = school,
                    SchoolIconPath = RegisteredSchoolIconPath(school),
                    CityId = city.data.id,
                    CityName = DeJureRegionStore.ResolveCountyNameForPresentation(
                        city),
                    Influence = SafeStat(leader, "stewardship")
                });
            }
        }

        private static bool IsValid(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return false;
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            return CourtAffiliationResolver.CanServe(pActor, pKingdom, layer);
        }

        private static bool IsCurrentRegionalGovernor(Actor pActor,
            Kingdom pKingdom, City pSeatCity)
        {
            if (pActor?.data == null || pSeatCity?.data == null ||
                pKingdom?.data == null || pSeatCity.kingdom != pKingdom ||
                pSeatCity.leader != pActor)
                return false;
            bool live;
            try { live = pActor.isAlive() && !pActor.isRekt() &&
                pActor.isCityLeader(); }
            catch { live = false; }
            return LocalGovernorIdentityRules.IsCurrentSeatLeader(
                seatControlled: true, actorIsLeader: pSeatCity.leader == pActor,
                actorLive: live);
        }

        private static bool IsCurrentCityLeader(City pCity,
            Kingdom pKingdom)
        {
            if (pCity?.data == null || pKingdom?.data == null ||
                pCity.kingdom != pKingdom || pCity.leader?.data == null)
                return false;
            bool live;
            try { live = pCity.leader.isAlive() &&
                !pCity.leader.isRekt() && pCity.leader.isCityLeader(); }
            catch { live = false; }
            return LocalGovernorIdentityRules.IsCurrentCityLeader(
                cityControlled: true, actorLive: live);
        }

        private static City FindCity(Kingdom pKingdom, long pCityId)
        {
            if (pKingdom?.data == null || pCityId < 0L) return null;
            try
            {
                return pKingdom.getCities()?.FirstOrDefault(city =>
                    city?.data?.id == pCityId && !city.isRekt() &&
                    city.kingdom == pKingdom);
            }
            catch { return null; }
        }

        private static bool IsValidSubjectActor(Actor pActor,
            Kingdom pSubject)
        {
            bool valid;
            try
            {
                valid = pActor?.data != null && pActor.isAlive() &&
                        !pActor.isRekt();
            }
            catch
            {
                valid = false;
            }
            return MilitaryGovernorateCourtRules.IsSubjectActor(valid,
                pActor?.kingdom?.id ?? -1L, pSubject?.id ?? -1L);
        }

        private static void AddCachedHeirRole(List<CourtPyramidNodeModel> pNodes, Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            if (heirId < 0) return;
            CourtPyramidNodeModel node = pNodes.FirstOrDefault(p => p.ActorId == heirId);
            if (node != null && !node.Roles.Contains(CourtPyramidRoleId.Heir))
                node.Roles.Insert(0, CourtPyramidRoleId.Heir);
        }

        private static void ApplyCareerStates(List<CourtPyramidNodeModel> pNodes,
            Dictionary<long, OfficialCareerStateView> pStates,
            bool pHasNineRankSystem)
        {
            if (pNodes == null || pStates == null || pStates.Count == 0) return;
            foreach (CourtPyramidNodeModel node in pNodes)
            {
                if (node == null || node.ActorId < 0 ||
                    !pStates.TryGetValue(node.ActorId, out OfficialCareerStateView state))
                {
                    if (node?.ActorId < 0 || !pHasNineRankSystem) continue;
                    Actor actor = World.world?.units?.get(node.ActorId);
                    if (actor?.data == null) continue;
                    int hotRank = OfficialCareerStateService.ReadRankFast(actor);
                    if (!OfficialCareerRankRules.CanDisplayRankedCareer(
                            pHasNineRankSystem, hotRank)) continue;
                    node.OfficialRank = hotRank;
                    actor.data.get(LineageKeys.OFFICER_TRACK,
                        out node.OfficialTrack,
                        OfficialCareerRankRules.CivilTrack);
                    actor.data.get(LineageKeys.OFFICER_MERIT,
                        out node.OfficialMerit, 0f);
                    actor.data.get(LineageKeys.OFFICER_MERIT_CAP,
                        out node.OfficialMeritCap, 1);
                    actor.data.get(LineageKeys.OFFICER_TERM_END_YEAR,
                        out node.OfficialTermEndYear, -1);
                    actor.data.get(LineageKeys.OFFICER_LAST_KAOKE,
                        out node.OfficialLastEvaluation, -1);
                    actor.data.get(LineageKeys.OFFICER_LOCAL_GRADE,
                        out node.OfficialLocalGrade, NineRankRules.Unranked);
                    continue;
                }
                bool rankedCareer = OfficialCareerRankRules.CanDisplayRankedCareer(
                    pHasNineRankSystem, state.Rank);
                node.OfficialRank = rankedCareer
                    ? state.Rank
                    : OfficialCareerRankRules.Unranked;
                node.OfficialTrack = state.Track;
                node.OfficialMerit = state.Merit;
                node.OfficialMeritCap = state.MeritCap;
                node.OfficialLastEvaluation = state.LastEvaluation;
                node.OfficialTermEndYear = state.TermEndYear;
                node.OfficialLocalGrade = rankedCareer
                    ? state.LocalGrade
                    : NineRankRules.Unranked;
            }
        }

        private static string ActorSchool(Actor pActor, string pFallback)
        {
            return pActor?.data == null
                ? CourtSchoolId.None
                : SchoolMembershipService.GetSchool(pActor.data.id);
        }

        private static string RegisteredSchoolIconPath(string pSchool)
        {
            try
            {
                string traitId = CourtTraitRules.TraitForSchool(pSchool);
                string path = string.IsNullOrEmpty(traitId) ? "" : AssetManager.traits.get(traitId)?.path_icon;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch { }
            return CourtPyramidRules.SchoolIconPath(pSchool);
        }

        private static string FindCityName(Kingdom pKingdom, long pCityId)
        {
            if (pCityId < 0) return "";
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && city.data.id == pCityId)
                        return DeJureRegionStore.ResolveCountyNameForPresentation(
                            city);
            }
            catch { }
            return "";
        }

        private static int StableStringOrder(string pValue)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in pValue ?? "") hash = hash * 31 + c;
                return hash & 0x7fffffff;
            }
        }

        private static string SafeActorName(Actor pActor)
        {
            try { return pActor?.getName() ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}
