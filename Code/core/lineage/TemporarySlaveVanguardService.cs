using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class TemporarySlaveVanguardService
    {
        private const int MaxCitiesPerWorkItem = 1;

        private sealed class VanguardState
        {
            public long KingdomId;
            public long ArmyId = -1L;
            public long AnchorCityId = -1L;
            public int CitySlotCursor;
            public int CitySlotsRemaining;
            public long ScanCityId = -1L;
            public int ActorCursor;
            public long CaptainCandidateId = -1L;
            public readonly List<long> SlaveCandidateIds = new List<long>(
                SlaveArmyFormationRules.MaxActorsChangedPerWorkItem);
            public readonly HashSet<long> CandidateIds = new HashSet<long>();
            public readonly HashSet<long> MemberIds = new HashSet<long>();
            public readonly long[] MutationBuffer = new long[
                SlaveArmyFormationRules.MaxActorsChangedPerWorkItem];
            public int SlaveCount;
            public int NonSlaveCount;
            public long AssaultTargetCityId = -1L;
            public double AssaultHeadStartTime = -1d;
            public bool AssaultReleased;
            public bool Cleaning;
            public bool ForceCleanup;
        }

        private static readonly Dictionary<long, VanguardState> States =
            new Dictionary<long, VanguardState>();
        private static readonly Dictionary<long, long> KingdomByMember =
            new Dictionary<long, long>();

        public static bool IsMember(Actor pActor)
        {
            return pActor?.data != null && KingdomByMember.ContainsKey(pActor.data.id);
        }

        public static bool IsDeploymentReady(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            Kingdom kingdom;
            try { kingdom = pArmy.getKingdom(); }
            catch { return false; }
            return kingdom?.data != null &&
                   States.TryGetValue(kingdom.id, out VanguardState state) &&
                   state.ArmyId == pArmy.id && HasValidComposition(state);
        }

        public static bool IsOperationalCaptain(Army pArmy,
            Actor pCaptain)
        {
            try
            {
                return pArmy?.data != null && pCaptain?.data != null &&
                       AWArmyService.IsRoleArmy(pArmy,
                           AWArmyRole.SlaveArmy) &&
                       pCaptain.army == pArmy && pCaptain.isAlive() &&
                       !pCaptain.isRekt() &&
                       pCaptain.is_profession_warrior &&
                       !SlaveService.IsSlave(pCaptain);
            }
            catch { return false; }
        }

        public static void RequestCaptainRecovery(Kingdom pKingdom,
            Army pArmy)
        {
            if (pKingdom?.data == null || pArmy?.data == null ||
                !AWArmyService.IsRoleArmy(pArmy, AWArmyRole.SlaveArmy) ||
                !CanOperate(pKingdom)) return;
            Kingdom armyKingdom;
            try { armyKingdom = pArmy.getKingdom(); }
            catch { return; }
            if (armyKingdom != pKingdom) return;

            Actor captain = SafeCaptain(pArmy);
            if (IsOperationalCaptain(pArmy, captain)) return;
            bool captainAlive = false;
            bool captainIsMember = false;
            try
            {
                captainAlive = captain?.data != null && captain.isAlive() &&
                               !captain.isRekt();
                captainIsMember = captain?.data != null &&
                                  captain.army == pArmy &&
                                  pArmy.units.Contains(captain);
            }
            catch { }
            if (ArmyCaptainContinuityRules.ShouldPreserveAssignedCaptain(
                    captainExists: captain?.data != null,
                    captainAlive, captainIsMember)) return;
            if (captain?.data != null)
            {
                using (ArmyCaptainDisposalScope.Open(pArmy))
                {
                    try { pArmy.setCaptain(null); }
                    catch { }
                }
            }

            VanguardState state = States.TryGetValue(pKingdom.id,
                out VanguardState existing)
                ? existing
                : CreateState(pKingdom);
            Army tracked = ResolveArmy(state);
            if (tracked?.data != null && tracked != pArmy) return;
            AdoptArmy(state, pKingdom, pArmy);
            EnsureScanPassAvailable(state, pKingdom);
            Schedule(pKingdom.id);
        }

        public static bool ShouldDelayBehindVanguard(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() || pActor.army?.data == null ||
                pActor.kingdom?.data == null || pActor.city?.data == null) return false;
            if (!MilitaryEmergencyService.TryGetActiveWarId(pActor.kingdom, out _)) return false;
            if (!States.TryGetValue(pActor.kingdom.id, out VanguardState state)) return false;

            Army actorArmy = pActor.army;
            Army vanguard = ResolveArmy(state);
            if (vanguard?.data == null || vanguard == actorArmy) return false;
            Actor actorCaptain = SafeCaptain(actorArmy);
            Actor vanguardCaptain = SafeCaptain(vanguard);
            City actorTarget = ResolveAttackTarget(pActor.city);
            City vanguardSource = ResolveCity(state.AnchorCityId) ?? vanguardCaptain?.city;
            City vanguardTarget = ResolveAttackTarget(vanguardSource);
            bool sameTarget = actorTarget?.data != null && vanguardTarget?.data != null &&
                              actorTarget.id == vanguardTarget.id;

            if (vanguardTarget?.data == null)
            {
                state.AssaultTargetCityId = -1L;
                state.AssaultHeadStartTime = -1d;
                state.AssaultReleased = false;
            }
            else if (state.AssaultTargetCityId != vanguardTarget.id)
            {
                state.AssaultTargetCityId = vanguardTarget.id;
                state.AssaultHeadStartTime = LineageService.CurTime();
                state.AssaultReleased = false;
            }
            else if (state.AssaultHeadStartTime < 0d)
            {
                state.AssaultHeadStartTime = LineageService.CurTime();
            }

            bool reached = state.AssaultReleased || HasReachedTarget(vanguardCaptain, vanguardTarget);
            bool retreating = IsRetreating(vanguard);
            bool headStartExpired = SlaveVanguardAssaultRules
                .IsHeadStartExpired(state.AssaultHeadStartTime,
                    LineageService.CurTime());
            if (reached || retreating || headStartExpired)
                state.AssaultReleased = true;
            return SlaveVanguardAssaultRules.ShouldHoldOrdinaryArmy(
                pActorIsCaptain: actorCaptain == pActor,
                pActorArmyIsVanguard: false,
                pVanguardReady: HasValidComposition(state) && vanguardCaptain?.current_tile != null,
                pSameAttackTarget: sameTarget,
                pVanguardReachedTarget: state.AssaultReleased,
                pVanguardRetreating: retreating,
                pHeadStartExpired: headStartExpired);
        }

        public static void OnEmergencyChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            bool active = CanOperate(pKingdom);
            if (!States.TryGetValue(pKingdom.id, out VanguardState state))
            {
                if (!active) return;
                state = CreateState(pKingdom);
            }
            else if (!active)
            {
                Schedule(pKingdom.id);
                return;
            }
            else if (state.ForceCleanup)
            {
                state.Cleaning = true;
                Schedule(pKingdom.id);
                return;
            }
            else if (state.Cleaning)
            {
                state.Cleaning = false;
                ResetScanPass(state, pKingdom, pClearCandidates: false);
            }
            if (state.CitySlotsRemaining > 0) Schedule(pKingdom.id);
        }

        public static void OnCandidateAvailable(Kingdom pKingdom, City pCity, Actor pActor)
        {
            if (pKingdom?.data == null || !CanOperate(pKingdom)) return;
            VanguardState state = States.TryGetValue(pKingdom.id, out VanguardState existing)
                ? existing
                : CreateState(pKingdom);
            if (state.ForceCleanup)
            {
                state.Cleaning = true;
                Schedule(pKingdom.id);
                return;
            }
            state.Cleaning = false;
            if (pCity?.data != null && pCity.kingdom == pKingdom)
            {
                ConsiderCandidate(state, pKingdom, pCity, pActor, ResolveArmy(state));
            }
            Schedule(pKingdom.id);
        }

        public static void OnMemberInvalidated(Actor pActor)
        {
            if (pActor?.data == null ||
                !KingdomByMember.TryGetValue(pActor.data.id, out long kingdomId)) return;
            if (!States.TryGetValue(kingdomId, out VanguardState state))
            {
                KingdomByMember.Remove(pActor.data.id);
                ClearMemberFields(pActor);
                return;
            }

            RemoveMemberIndex(state, pActor);
            Army army = ResolveArmy(state);
            ArmyDeploymentService.ReleaseActor(pActor, restoreJob: false);
            if (army?.data != null && pActor.army == army)
                DetachFromArmy(pActor, army);
            if (!pActor.isRekt() && pActor.isAlive() && pActor.isWarrior())
                pActor.stopBeingWarrior();
            if (SlaveService.IsSlave(pActor))
            {
                pActor.data.set(LineageKeys.SLAVE_SOLDIER, false);
                pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            }
            ClearMemberFields(pActor);

            Kingdom kingdom = ResolveKingdom(kingdomId);
            if (kingdom?.data != null)
            {
                WriteRoster(state, kingdom);
                EnsureScanPassAvailable(state, kingdom);
                if (army?.data != null)
                    WarNoticeService.OnArmyChanged(kingdom, army, pRosterExpanded: false);
                else
                    WarNoticeService.OnArmyInvalidated(kingdom, state.ArmyId);
            }
            Schedule(kingdomId);
        }

        public static void OnActorKingdomChanged(Actor pActor, Kingdom pOldKingdom)
        {
            if (pActor?.data == null || pActor.kingdom == pOldKingdom) return;
            long actorId = pActor.data.id;
            OnMemberInvalidated(pActor);
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("slave_vanguard_transfer", actorId),
                DeferredWorkClass.Runtime, () => PersistTransferredSlave(actorId));
        }

        public static void OnWarriorStatusLost(Actor pActor)
        {
            if (!IsMember(pActor)) return;
            OnMemberInvalidated(pActor);
            if (SlaveService.IsSlave(pActor))
                SlaveService.QueueSlaveStatePersistence(pActor, pActive: true,
                    pActor.city, pActor.kingdom);
        }

        private static void PersistTransferredSlave(long pActorId)
        {
            Actor actor = ResolveActor(pActorId);
            if (!SlaveService.IsSlave(actor)) return;
            SlaveService.QueueSlaveStatePersistence(actor, pActive: true,
                actor.city, actor.kingdom);
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            foreach (Kingdom kingdom in pWar.getAttackers()) OnEmergencyChanged(kingdom);
            foreach (Kingdom kingdom in pWar.getDefenders()) OnEmergencyChanged(kingdom);
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            foreach (Kingdom kingdom in pWar.getAttackers()) OnEmergencyChanged(kingdom);
            foreach (Kingdom kingdom in pWar.getDefenders()) OnEmergencyChanged(kingdom);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !States.TryGetValue(pKingdom.id, out VanguardState state))
                return;
            state.Cleaning = true;
            state.ForceCleanup = true;
            Schedule(pKingdom.id);
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) continue;
                if (AWArmyService.TryGetRoleArmy(kingdom, AWArmyRole.SlaveArmy, out Army army))
                {
                    VanguardState state = CreateState(kingdom);
                    AdoptArmy(state, kingdom, army);
                    Schedule(kingdom.id);
                    continue;
                }
                if (CanOperate(kingdom)) OnEmergencyChanged(kingdom);
            }
        }

        public static void ClearRuntime()
        {
            States.Clear();
            KingdomByMember.Clear();
        }

        private static VanguardState CreateState(Kingdom pKingdom)
        {
            var state = new VanguardState { KingdomId = pKingdom.id };
            States[pKingdom.id] = state;
            ResetScanPass(state, pKingdom);
            return state;
        }

        private static void Schedule(long pKingdomId)
        {
            if (pKingdomId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("slave_vanguard", pKingdomId),
                DeferredWorkClass.Runtime, () => Process(pKingdomId));
        }

        private static void Process(long pKingdomId)
        {
            if (!States.TryGetValue(pKingdomId, out VanguardState state)) return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            bool active = kingdom?.data != null && CanOperate(kingdom);
            if (!active)
            {
                if (!TemporaryMilitaryServiceRules.ShouldDemobilize(
                        temporaryRoleActive: true,
                        kingdom?.data != null &&
                        MilitaryEmergencyService.HasAny(kingdom))) return;
                state.Cleaning = true;
                state.ForceCleanup = false;
                CleanupBatch(state, kingdom);
                return;
            }

            if (state.ForceCleanup)
            {
                state.Cleaning = true;
                CleanupBatch(state, kingdom);
                return;
            }

            state.Cleaning = false;
            Army army = ResolveArmy(state);
            if (state.ArmyId >= 0 && army == null)
            {
                state.Cleaning = true;
                state.ForceCleanup = true;
                CleanupBatch(state, kingdom);
                return;
            }
            if (army == null && AWArmyService.TryGetRoleArmy(
                    kingdom, AWArmyRole.SlaveArmy, out Army indexedArmy))
            {
                AdoptArmy(state, kingdom, indexedArmy);
                army = indexedArmy;
            }

            if (army == null && InitialCandidatesReady(state))
            {
                if (FormInitialRosterAtomically(state, kingdom)) return;
            }

            if (army != null && !NeedsRepairOrFill(state)) return;
            if (state.CitySlotsRemaining <= 0)
            {
                if (army != null && !HasValidComposition(state))
                {
                    state.Cleaning = true;
                    state.ForceCleanup = true;
                    Schedule(state.KingdomId);
                }
                return;
            }

            ScanOneCity(state, kingdom, army);
        }

        private static void ScanOneCity(VanguardState pState, Kingdom pKingdom, Army pArmy)
        {
            int visited = 0;
            while (visited < MaxCitiesPerWorkItem)
            {
                City city = ResolveCurrentScanCity(pState, pKingdom);
                if (city?.data == null || city.isRekt() || city.kingdom != pKingdom)
                {
                    CompleteCurrentCity(pState);
                    visited++;
                    break;
                }
                if (!OccupiedCitySupplyService.CanProvideToRealm(
                        city, pKingdom))
                {
                    CompleteCurrentCity(pState);
                    visited++;
                    continue;
                }

                ScanResidents(pState, pKingdom, city, pArmy);
                visited++;
            }

            if (pArmy == null && InitialCandidatesReady(pState))
            {
                if (FormInitialRosterAtomically(pState, pKingdom)) return;
            }
            else if (pArmy != null)
            {
                AttachRepairBatch(pState, pKingdom, pArmy);
                if (!NeedsRepairOrFill(pState)) return;
            }

            if (pState.CitySlotsRemaining > 0)
            {
                Schedule(pState.KingdomId);
                return;
            }
            if (pArmy != null && !HasValidComposition(pState))
            {
                pState.Cleaning = true;
                pState.ForceCleanup = true;
                Schedule(pState.KingdomId);
            }
        }

        private static void ScanResidents(VanguardState pState, Kingdom pKingdom, City pCity, Army pArmy)
        {
            if (pState.ScanCityId != pCity.id)
            {
                pState.ScanCityId = pCity.id;
                pState.ActorCursor = 0;
            }

            int unitCount = pCity.units.Count;
            if (pState.ActorCursor >= unitCount)
            {
                CompleteCurrentCity(pState);
                return;
            }

            int remaining = unitCount - pState.ActorCursor;
            int scanCount = Math.Min(SlaveArmyFormationRules.MaxResidentsScannedPerWorkItem, remaining);
            int scanned = 0;
            for (int i = 0; i < scanCount; i++)
            {
                Actor actor = pCity.units[pState.ActorCursor + i];
                scanned++;
                ConsiderCandidate(pState, pKingdom, pCity, actor, pArmy);
                if (CandidateBatchReady(pState, pArmy)) break;
            }
            pState.ActorCursor += scanned;
            if (pState.ActorCursor >= unitCount) CompleteCurrentCity(pState);
        }

        private static void ConsiderCandidate(VanguardState pState, Kingdom pKingdom, City pCity,
            Actor pActor, Army pArmy)
        {
            if (pActor?.data == null || pState.CandidateIds.Contains(pActor.data.id)) return;
            if (pArmy == null)
            {
                if (pState.CaptainCandidateId < 0 && CanBeCaptain(pKingdom, pCity, pActor))
                {
                    pState.CaptainCandidateId = pActor.data.id;
                    pState.CandidateIds.Add(pActor.data.id);
                    return;
                }
                AddSlaveCandidate(pState, pKingdom, pCity, pActor);
                return;
            }

            if (pState.NonSlaveCount <= 0 && pState.SlaveCount >= SlaveArmyFormationRules.MinimumInitialSlaves)
            {
                if (pState.CaptainCandidateId < 0 && CanBeCaptain(pKingdom, pCity, pActor))
                {
                    pState.CaptainCandidateId = pActor.data.id;
                    pState.CandidateIds.Add(pActor.data.id);
                }
                return;
            }
            AddSlaveCandidate(pState, pKingdom, pCity, pActor);
        }

        private static void AddSlaveCandidate(VanguardState pState, Kingdom pKingdom, City pCity, Actor pActor)
        {
            if (pState.SlaveCandidateIds.Count >= SlaveArmyFormationRules.MaxActorsChangedPerWorkItem ||
                !CanBeSlaveSoldier(pKingdom, pCity, pActor)) return;
            pState.SlaveCandidateIds.Add(pActor.data.id);
            pState.CandidateIds.Add(pActor.data.id);
        }

        private static bool CandidateBatchReady(VanguardState pState, Army pArmy)
        {
            if (pArmy == null) return InitialCandidatesReady(pState);
            if (pState.NonSlaveCount <= 0 && pState.SlaveCount >= SlaveArmyFormationRules.MinimumInitialSlaves)
                return pState.CaptainCandidateId >= 0;
            return pState.SlaveCandidateIds.Count >= SlaveArmyFormationRules.MaxActorsChangedPerWorkItem;
        }

        private static bool InitialCandidatesReady(VanguardState pState)
        {
            return pState.CaptainCandidateId >= 0 &&
                   pState.SlaveCandidateIds.Count >= SlaveArmyFormationRules.MinimumInitialSlaves;
        }

        private static bool FormInitialRosterAtomically(VanguardState pState, Kingdom pKingdom)
        {
            Actor captain = ResolveActor(pState.CaptainCandidateId);
            if (!CanBeCaptain(pKingdom, captain?.city, captain))
            {
                RemoveCaptainCandidate(pState);
                ContinueIfScanRemaining(pState);
                return false;
            }

            var slaves = new List<Actor>(SlaveArmyFormationRules.MinimumInitialSlaves);
            for (int i = 0; i < pState.SlaveCandidateIds.Count &&
                            slaves.Count < SlaveArmyFormationRules.MinimumInitialSlaves; i++)
            {
                Actor slave = ResolveActor(pState.SlaveCandidateIds[i]);
                if (CanBeSlaveSoldier(pKingdom, slave?.city, slave)) slaves.Add(slave);
            }
            if (slaves.Count < SlaveArmyFormationRules.MinimumInitialSlaves)
            {
                PurgeInvalidCandidates(pState, pKingdom);
                ContinueIfScanRemaining(pState);
                return false;
            }

            var roster = new List<Actor>(SlaveArmyFormationRules.InitialRosterSize) { captain };
            roster.AddRange(slaves);
            var promoted = new List<Actor>(roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                Actor actor = roster[i];
                if (!PromoteForVanguard(actor))
                {
                    RollbackPromotions(promoted, null);
                    RemoveCandidate(pState, actor.data.id);
                    ContinueIfScanRemaining(pState);
                    return false;
                }
                promoted.Add(actor);
            }

            City anchor = captain.city ?? pKingdom.capital;
            Army army = AWArmyService.EnsureArmy(pKingdom, anchor, captain, AWArmyRole.SlaveArmy,
                SlaveService.BuildSlaveArmyName(pKingdom, anchor, 1), pDetached: true);
            if (army == null)
            {
                RollbackPromotions(promoted, null);
                ContinueIfScanRemaining(pState);
                return false;
            }

            for (int i = 0; i < roster.Count; i++)
            {
                AWArmyService.AddToArmy(roster[i], army);
                if (roster[i].army == army) continue;
                RollbackPromotions(promoted, army);
                AWArmyService.RemoveSpecialArmy(army);
                ContinueIfScanRemaining(pState);
                return false;
            }
            AWArmyService.SetCaptainIfChanged(army, captain);

            pState.ArmyId = army.id;
            pState.AnchorCityId = anchor?.id ?? -1L;
            pState.SlaveCount = 0;
            pState.NonSlaveCount = 0;
            for (int i = 0; i < roster.Count; i++)
                PublishMember(pState, pKingdom, army, roster[i], pCaptain: i == 0);
            WriteRoster(pState, pKingdom);
            ClearCandidates(pState);
            QueueFormationRecord(pKingdom.id, pState.AnchorCityId);
            WarNoticeService.OnArmyChanged(pKingdom, army);

            if (pState.MemberIds.Count < SlaveArmyFormationRules.MaximumRoster &&
                pState.CitySlotsRemaining > 0)
                Schedule(pState.KingdomId);
            return true;
        }

        private static void AttachRepairBatch(VanguardState pState, Kingdom pKingdom, Army pArmy)
        {
            int memberCountBefore = pState.MemberIds.Count;
            int changed = 0;
            bool captainRepairNeededMoreSlaves = pState.NonSlaveCount <= 0 &&
                                                  pState.SlaveCount <
                                                  SlaveArmyFormationRules.MinimumInitialSlaves;
            if (pState.NonSlaveCount <= 0 &&
                pState.SlaveCount >= SlaveArmyFormationRules.MinimumInitialSlaves &&
                pState.CaptainCandidateId >= 0)
            {
                Actor captain = ResolveActor(pState.CaptainCandidateId);
                if (CanBeCaptain(pKingdom, captain?.city, captain) &&
                    SlaveArmyFormationRules.CanAddNonSlaveCadre(
                        pState.MemberIds.Count, pState.SlaveCount, pState.NonSlaveCount,
                        hasNonSlaveCaptain: false) &&
                    PromoteForVanguard(captain))
                {
                    AWArmyService.AddToArmy(captain, pArmy);
                    if (captain.army == pArmy)
                    {
                        AWArmyService.SetCaptainIfChanged(pArmy, captain);
                        PublishMember(pState, pKingdom, pArmy, captain, pCaptain: true);
                        changed++;
                    }
                    else
                    {
                        DemoteUnpublished(captain, null);
                    }
                }
            }

            int allowedSlaves = SlaveArmyFormationRules.MaxActorsChangedPerWorkItem - changed;
            for (int i = 0; i < pState.SlaveCandidateIds.Count && allowedSlaves > 0; i++)
            {
                if (!SlaveArmyFormationRules.CanAddSlaveToArmy(
                        pState.MemberIds.Count, pState.SlaveCount, pState.NonSlaveCount)) break;
                Actor slave = ResolveActor(pState.SlaveCandidateIds[i]);
                if (!CanBeSlaveSoldier(pKingdom, slave?.city, slave) || !PromoteForVanguard(slave)) continue;
                AWArmyService.AddToArmy(slave, pArmy);
                if (slave.army != pArmy)
                {
                    DemoteUnpublished(slave, null);
                    continue;
                }
                PublishMember(pState, pKingdom, pArmy, slave, pCaptain: false);
                allowedSlaves--;
            }
            ClearCandidates(pState);
            if (pState.MemberIds.Count != memberCountBefore) WriteRoster(pState, pKingdom);
            if (captainRepairNeededMoreSlaves && pState.NonSlaveCount <= 0 &&
                pState.SlaveCount >= SlaveArmyFormationRules.MinimumInitialSlaves)
                ResetScanPass(pState, pKingdom, pClearCandidates: false);
            if (changed > 0 || allowedSlaves < SlaveArmyFormationRules.MaxActorsChangedPerWorkItem - changed)
                WarNoticeService.OnArmyChanged(pKingdom, pArmy);
        }

        private static bool PromoteForVanguard(Actor pActor)
        {
            City city = pActor?.city;
            if (pActor?.data == null || city?.data == null || pActor.isWarrior()) return false;
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.SlaveVanguard))
            {
                if (!city.checkCanMakeWarrior(pActor)) return false;
                city.makeWarrior(pActor);
            }
            return pActor.isWarrior();
        }

        private static void PublishMember(VanguardState pState, Kingdom pKingdom, Army pArmy,
            Actor pActor, bool pCaptain, bool pPersist = true)
        {
            if (pActor?.data == null || !pState.MemberIds.Add(pActor.data.id)) return;
            KingdomByMember[pActor.data.id] = pKingdom.id;
            bool slave = SlaveService.IsSlave(pActor);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_MEMBER, true);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_CAPTAIN, pCaptain);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_KINGDOM_ID, pKingdom.id);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_ARMY_ID, pArmy.id);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_ORIGINAL_CITY_ID,
                pActor.city?.id ?? -1L);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            if (slave)
            {
                pState.SlaveCount++;
                pActor.data.set(LineageKeys.SLAVE_SOLDIER, true);
                if (pPersist)
                    SlaveService.QueueSlaveStatePersistence(pActor, pActive: true, pActor.city, pKingdom);
            }
            else
            {
                pState.NonSlaveCount++;
            }
        }

        private static void CleanupBatch(VanguardState pState, Kingdom pKingdom)
        {
            if (pKingdom?.data != null && CanOperate(pKingdom) && !pState.ForceCleanup)
            {
                pState.Cleaning = false;
                pState.ForceCleanup = false;
                ResetScanPass(pState, pKingdom);
                Schedule(pState.KingdomId);
                return;
            }
            if (!pState.ForceCleanup &&
                !TemporaryMilitaryServiceRules.ShouldDemobilize(
                    temporaryRoleActive: true,
                    pKingdom?.data != null &&
                    MilitaryEmergencyService.HasAny(pKingdom))) return;

            long[] batch = pState.MutationBuffer;
            int count = 0;
            foreach (long actorId in pState.MemberIds)
            {
                batch[count++] = actorId;
                if (count >= batch.Length) break;
            }

            Army army = ResolveArmy(pState);
            for (int i = 0; i < count; i++)
            {
                Actor actor = ResolveActor(batch[i]);
                if (actor?.data != null)
                {
                    RemoveMemberIndex(pState, actor);
                    ArmyDeploymentService.ReleaseActor(actor, restoreJob: false);
                    if (actor.army == army) DetachFromArmy(actor, army);
                    bool living = !actor.isRekt() && actor.isAlive();
                    if (SlaveService.IsSlave(actor))
                    {
                        actor.data.set(LineageKeys.SLAVE_SOLDIER, false);
                        actor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
                        SlaveService.QueueSlaveStatePersistence(actor, pActive: true,
                            actor.city, actor.kingdom);
                    }
                    ClearMemberFields(actor);
                    if (living)
                        TemporaryMilitaryDemobilizationService.RestoreCivilian(
                            actor);
                }
                else
                {
                    pState.MemberIds.Remove(batch[i]);
                    KingdomByMember.Remove(batch[i]);
                }
            }

            if (pState.MemberIds.Count > 0)
            {
                if (pKingdom?.data != null) WriteRoster(pState, pKingdom);
                Schedule(pState.KingdomId);
                return;
            }

            long anchorCityId = pState.AnchorCityId;
            long removedArmyId = pState.ArmyId;
            bool hadFormation = pState.ArmyId >= 0;
            if (army?.data != null) AWArmyService.RemoveSpecialArmy(army);
            WarNoticeService.OnArmyInvalidated(pKingdom, removedArmyId);
            if (pKingdom?.data != null)
                pKingdom.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_ROSTER_IDS, "");
            States.Remove(pState.KingdomId);
            if (hadFormation) QueueDemobilizationRecord(pState.KingdomId, anchorCityId);
        }

        private static void AdoptArmy(VanguardState pState, Kingdom pKingdom, Army pArmy)
        {
            pState.ArmyId = pArmy.id;
            pState.AnchorCityId = AWArmyService.GetAnchorCityId(pArmy);
            foreach (long actorId in pState.MemberIds) KingdomByMember.Remove(actorId);
            pState.MemberIds.Clear();
            pState.SlaveCount = 0;
            pState.NonSlaveCount = 0;
            Actor captain = null;
            try { captain = pArmy.getCaptain(); } catch { }
            List<long> rosterIds = ReadRosterIds(pKingdom);
            if (rosterIds.Count > 0)
            {
                for (int i = 0; i < rosterIds.Count; i++)
                    TryAdoptMember(pState, pKingdom, pArmy, captain, ResolveActor(rosterIds[i]));
            }
            else
            {
                int count = Math.Min(pArmy.units.Count, SlaveArmyFormationRules.MaximumRoster + 1);
                for (int i = 0; i < count; i++)
                    TryAdoptMember(pState, pKingdom, pArmy, captain, pArmy.units[i]);
            }
            WriteRoster(pState, pKingdom);
            ResetScanPass(pState, pKingdom);
        }

        private static void TryAdoptMember(VanguardState pState, Kingdom pKingdom, Army pArmy,
            Actor pCaptain, Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() || !pActor.isAlive() ||
                !pActor.isWarrior() || pActor.kingdom != pKingdom) return;
            if (pActor.army != pArmy) AWArmyService.AddToArmy(pActor, pArmy);
            if (pActor.army != pArmy) return;
            pActor.data.get(LineageKeys.TEMPORARY_SLAVE_VANGUARD_CAPTAIN,
                out bool recordedCaptain, false);
            bool isCaptain = (pActor == pCaptain || recordedCaptain) && !SlaveService.IsSlave(pActor);
            PublishMember(pState, pKingdom, pArmy, pActor, isCaptain, pPersist: false);
            if (isCaptain && pArmy.getCaptain() != pActor)
                AWArmyService.SetCaptainIfChanged(pArmy, pActor);
        }

        private static bool CanOperate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return false;
            pKingdom.data.get(LineageKeys.SLAVERY_ENABLED, out bool slavery, false);
            pKingdom.data.get(LineageKeys.SLAVE_ARMY_ENABLED, out bool capability, false);
            return SlaveArmyFormationRules.CanForm(slavery, capability,
                MilitaryEmergencyService.HasAny(pKingdom), existingKingdomVanguards: 0);
        }

        private static bool CanBeCaptain(Kingdom pKingdom, City pCity, Actor pActor)
        {
            if (!CanBeBaseCandidate(pKingdom, pCity, pActor) || SlaveService.IsSlave(pActor) ||
                SlaveService.IsRetiredSoldier(pActor)) return false;
            if (!RoyalAsylumRules.CanPerformProtectedRole(RoyalAsylumService.IsActive(pActor))) return false;
            if (pActor.isKing() || pActor.isCityLeader() || GeneralService.IsActiveGeneralFast(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor) || HeirService.IsCurrentHeir(pKingdom, pActor) ||
                TemporaryLevyService.IsTemporaryLevy(pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            if (!string.IsNullOrEmpty(office) && layer != CourtOfficeLayer.Military) return false;
            return HistoricalMasterVocationService.CanEnter(pActor,
                HistoricalMasterMilitaryContext.SlaveArmyCadre);
        }

        private static bool CanBeSlaveSoldier(Kingdom pKingdom, City pCity, Actor pActor)
        {
            if (!CanBeBaseCandidate(pKingdom, pCity, pActor) || !SlaveService.IsSlave(pActor) ||
                SlaveService.IsRetiredSoldier(pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor) || RoyalAsylumService.IsActive(pActor) ||
                TemporaryLevyService.IsTemporaryLevy(pActor)) return false;
            return HistoricalMasterVocationService.CanEnter(pActor,
                HistoricalMasterMilitaryContext.SlaveArmyCadre);
        }

        private static bool CanBeBaseCandidate(Kingdom pKingdom, City pCity, Actor pActor)
        {
            return pKingdom?.data != null && pCity?.data != null && pActor?.data != null &&
                   pActor.kingdom == pKingdom && pActor.city == pCity && !pActor.isRekt() &&
                   pActor.isAlive() && pActor.isAdult() && pActor.asset?.is_boat != true &&
                   !pActor.isWarrior() && !pActor.hasArmy() &&
                   pActor.isProfession(UnitProfession.Unit);
        }

        private static bool NeedsRepairOrFill(VanguardState pState)
        {
            if (pState.MemberIds.Count >= SlaveArmyFormationRules.MaximumRoster)
                return !HasValidComposition(pState);
            return true;
        }

        private static bool HasValidComposition(VanguardState pState)
        {
            return SlaveArmyFormationRules.IsSlaveArmyComposition(
                pState.MemberIds.Count, pState.SlaveCount, pState.NonSlaveCount,
                captainNonSlave: pState.NonSlaveCount == 1);
        }

        private static void ResetScanPass(VanguardState pState, Kingdom pKingdom,
            bool pClearCandidates = true)
        {
            pState.CitySlotCursor = 0;
            pState.ActorCursor = 0;
            pState.ScanCityId = -1L;
            pState.CitySlotsRemaining = pKingdom.cities.Count;
            if (pClearCandidates) ClearCandidates(pState);
        }

        private static void EnsureScanPassAvailable(VanguardState pState, Kingdom pKingdom)
        {
            if (pState == null || pKingdom?.data == null ||
                !SlaveArmyFormationRules.ShouldRestartCandidateScan(pState.CitySlotsRemaining)) return;
            ResetScanPass(pState, pKingdom, pClearCandidates: false);
        }

        private static City ResolveCurrentScanCity(VanguardState pState, Kingdom pKingdom)
        {
            if (pState.ScanCityId >= 0)
            {
                City pinned = ResolveCity(pState.ScanCityId);
                if (pinned?.data != null && pinned.kingdom == pKingdom) return pinned;
                pState.ScanCityId = -1L;
                pState.ActorCursor = 0;
            }

            int cityIndex = pState.CitySlotCursor;
            if (cityIndex < 0 || cityIndex >= pKingdom.cities.Count) return null;
            City city = pKingdom.cities[cityIndex];
            pState.ScanCityId = city?.id ?? -1L;
            return city;
        }

        private static void CompleteCurrentCity(VanguardState pState)
        {
            pState.CitySlotCursor++;
            pState.CitySlotsRemaining = Math.Max(0, pState.CitySlotsRemaining - 1);
            pState.ScanCityId = -1L;
            pState.ActorCursor = 0;
        }

        private static void ContinueIfScanRemaining(VanguardState pState)
        {
            if (pState.CitySlotsRemaining > 0) Schedule(pState.KingdomId);
        }

        private static void PurgeInvalidCandidates(VanguardState pState, Kingdom pKingdom)
        {
            if (pState.CaptainCandidateId >= 0)
            {
                Actor captain = ResolveActor(pState.CaptainCandidateId);
                if (!CanBeCaptain(pKingdom, captain?.city, captain)) RemoveCaptainCandidate(pState);
            }
            for (int i = pState.SlaveCandidateIds.Count - 1; i >= 0; i--)
            {
                long actorId = pState.SlaveCandidateIds[i];
                Actor slave = ResolveActor(actorId);
                if (CanBeSlaveSoldier(pKingdom, slave?.city, slave)) continue;
                pState.SlaveCandidateIds.RemoveAt(i);
                pState.CandidateIds.Remove(actorId);
            }
        }

        private static void RemoveCaptainCandidate(VanguardState pState)
        {
            if (pState.CaptainCandidateId >= 0) pState.CandidateIds.Remove(pState.CaptainCandidateId);
            pState.CaptainCandidateId = -1L;
        }

        private static void RemoveCandidate(VanguardState pState, long pActorId)
        {
            if (pState.CaptainCandidateId == pActorId) pState.CaptainCandidateId = -1L;
            pState.SlaveCandidateIds.Remove(pActorId);
            pState.CandidateIds.Remove(pActorId);
        }

        private static void ClearCandidates(VanguardState pState)
        {
            pState.CaptainCandidateId = -1L;
            pState.SlaveCandidateIds.Clear();
            pState.CandidateIds.Clear();
        }

        private static void RemoveMemberIndex(VanguardState pState, Actor pActor)
        {
            if (pActor?.data == null || !pState.MemberIds.Remove(pActor.data.id)) return;
            KingdomByMember.Remove(pActor.data.id);
            if (SlaveService.IsSlave(pActor))
                pState.SlaveCount = Math.Max(0, pState.SlaveCount - 1);
            else
                pState.NonSlaveCount = Math.Max(0, pState.NonSlaveCount - 1);
        }

        private static void ClearMemberFields(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_MEMBER, false);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_CAPTAIN, false);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_ARMY_ID, -1L);
            pActor.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_ORIGINAL_CITY_ID, -1L);
        }

        private static void DetachFromArmy(Actor pActor, Army pArmy)
        {
            try { pActor.removeFromArmy(); }
            catch { pActor.setArmy(null); }
            try { pArmy?.units?.Remove(pActor); } catch { }
        }

        private static void RollbackPromotions(List<Actor> pActors, Army pArmy)
        {
            for (int i = 0; i < pActors.Count; i++) DemoteUnpublished(pActors[i], pArmy);
        }

        private static void DemoteUnpublished(Actor pActor, Army pArmy)
        {
            if (pActor?.data == null) return;
            if (pActor.army == pArmy || (pArmy == null && pActor.hasArmy()))
                DetachFromArmy(pActor, pActor.army);
            if (pActor.isWarrior()) pActor.stopBeingWarrior();
            if (SlaveService.IsSlave(pActor))
            {
                pActor.data.set(LineageKeys.SLAVE_SOLDIER, false);
                SlaveService.QueueSlaveStatePersistence(pActor, pActive: true,
                    pActor.city, pActor.kingdom);
            }
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            ClearMemberFields(pActor);
        }

        private static Army ResolveArmy(VanguardState pState)
        {
            if (pState == null || pState.ArmyId < 0) return null;
            Kingdom kingdom = ResolveKingdom(pState.KingdomId);
            if (kingdom?.data == null ||
                !AWArmyService.TryGetRoleArmy(kingdom, AWArmyRole.SlaveArmy, out Army army)) return null;
            if (army.id != pState.ArmyId) return null;
            return army;
        }

        private static List<long> ReadRosterIds(Kingdom pKingdom)
        {
            var result = new List<long>(SlaveArmyFormationRules.MaximumRoster + 1);
            if (pKingdom?.data == null) return result;
            pKingdom.data.get(LineageKeys.TEMPORARY_SLAVE_VANGUARD_ROSTER_IDS,
                out string raw, "");
            if (string.IsNullOrEmpty(raw)) return result;
            string[] parts = raw.Split(',');
            for (int i = 0; i < parts.Length &&
                            result.Count < SlaveArmyFormationRules.MaximumRoster + 1; i++)
            {
                if (!long.TryParse(parts[i], out long actorId) || actorId < 0 ||
                    result.Contains(actorId)) continue;
                result.Add(actorId);
            }
            return result;
        }

        private static void WriteRoster(VanguardState pState, Kingdom pKingdom)
        {
            if (pState == null || pKingdom?.data == null) return;
            var ids = new List<string>(Math.Min(
                pState.MemberIds.Count, SlaveArmyFormationRules.MaximumRoster + 1));
            foreach (long actorId in pState.MemberIds)
            {
                if (actorId < 0 || ids.Count >= SlaveArmyFormationRules.MaximumRoster + 1) break;
                ids.Add(actorId.ToString());
            }
            string next = string.Join(",", ids.ToArray());
            pKingdom.data.get(LineageKeys.TEMPORARY_SLAVE_VANGUARD_ROSTER_IDS,
                out string current, "");
            if (current != next)
                pKingdom.data.set(LineageKeys.TEMPORARY_SLAVE_VANGUARD_ROSTER_IDS, next);
        }

        private static void QueueFormationRecord(long pKingdomId, long pCityId)
        {
            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent, () =>
            {
                Kingdom kingdom = ResolveKingdom(pKingdomId);
                if (kingdom?.data == null) return;
                SlaveService.RecordSlaveArmyFormation(kingdom, ResolveCity(pCityId));
            });
        }

        private static void QueueDemobilizationRecord(long pKingdomId, long pCityId)
        {
            DeferredRuntimeWorkService.EnqueueOrdered(DeferredWorkClass.Persistent, () =>
            {
                Kingdom kingdom = ResolveKingdom(pKingdomId);
                if (kingdom?.data == null) return;
                HistoryWriter.RecordKingdom(kingdom, "temporary_slave_vanguard_demobilized",
                    HistoryText.Kingdom(kingdom) +
                    HistoryLocalizationRules.H("aw_hist_temporary_slave_vanguard_demobilized"),
                    HistoryTarget.City(ResolveCity(pCityId)));
            });
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try
            {
                Actor captain = pArmy?.getCaptain();
                return captain?.data != null && captain.isAlive() && !captain.isRekt()
                    ? captain
                    : null;
            }
            catch { return null; }
        }

        private static City ResolveAttackTarget(City pSourceCity)
        {
            if (pSourceCity?.data == null) return null;
            try { return pSourceCity.target_attack_city ?? pSourceCity.target_attack_zone?.city; }
            catch { return null; }
        }

        private static bool HasReachedTarget(Actor pCaptain, City pTargetCity)
        {
            if (pCaptain?.current_tile == null || pTargetCity?.data == null) return false;
            try { return pCaptain.current_tile.zone?.city == pTargetCity; }
            catch { return false; }
        }

        private static bool IsRetreating(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            pArmy.data.get(LineageKeys.AW_ARMY_RETREAT_UNTIL_YEAR,
                out int retreatUntilYear, -1);
            return ArmyRetreatRules.ShouldSkipAttackWhileRetreating(
                retreatUntilYear, Date.getCurrentYear());
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static Actor ResolveActor(long pId)
        {
            try { return pId >= 0 ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }
    }
}
