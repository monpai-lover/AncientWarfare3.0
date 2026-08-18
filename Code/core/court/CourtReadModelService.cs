using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
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
            AddCityLeaders(seeds, pKingdom);
            List<CourtPyramidNodeModel> result = CourtPyramidRules.BuildLayout(
                seeds, HorizontalSpacing, VerticalSpacing);
            AddCachedHeirRole(result, pKingdom);
            ApplyCareerStates(result,
                OfficialCareerStateService.LoadKingdomStates(pKingdom.id),
                CourtService.HasNineRankSystem(pKingdom));
            return result;
        }

        private static void AddKing(List<CourtPyramidNodeModel> pSeeds, Kingdom pKingdom)
        {
            Actor king = pKingdom.king;
            if (!IsValid(king, pKingdom)) return;
            string school = ActorSchool(king, "");
            pSeeds.Add(new CourtPyramidNodeModel(king.data.id, CourtPyramidRoleId.King,
                CourtPyramidRoleId.King, CourtPyramidRules.KingRank, 0, false)
            {
                ActorName = SafeActorName(king),
                SchoolId = school,
                SchoolIconPath = RegisteredSchoolIconPath(school),
                Influence = 100f
            });
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
            var expectedOrder = new Dictionary<string, int>();
            for (int i = 0; i < expected.Length; i++) expectedOrder[expected[i]] = i;
            bool customGraph = CustomCourtRuntime.HasInstance(pKingdom);

            var filled = new HashSet<string>();
            foreach (CourtOfficerView officer in pOfficers ?? new List<CourtOfficerView>())
            {
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
                    CityName = city.data.name ?? "",
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
                    continue;
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
                    if (city?.data != null && city.data.id == pCityId) return city.data.name ?? "";
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
