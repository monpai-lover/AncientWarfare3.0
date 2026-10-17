#if !AW3_RULES_TESTS
using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.pathfinding;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyAbstractBattleService
    {
        private const int MaximumBattlesPerFrame = 4;
        private const int MaximumCitiesDiscoveredPerFrame = 32;
        private static readonly HashSet<string> ProcessingKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<long, ArmyAbstractBattleTransactionSnapshot>
            PendingTransactions = new Dictionary<long, ArmyAbstractBattleTransactionSnapshot>();
        private static IEnumerator<City> _cityDiscovery;
        private static bool _discoveryComplete;
        private static bool _preferNewBattle;
        private static int _recoveryCursor;

        public static void ProcessFrame()
        {
            ProcessFrame(MaximumBattlesPerFrame);
        }

        public static int PendingWorkUpperBound
        {
            get
            {
                if (!ArmyRtsWarDoctrine.IsAbstractDecisive ||
                    AW3MultiplayerReplicaScope.IsReplicaSession)
                    return 0;
                int missions = ArmyRtsControllerService.
                    SnapshotMissions().Count;
                long total = (long)Math.Max(0, missions) +
                             PendingTransactions.Count + (_discoveryComplete ? 0 : 1);
                return total >= int.MaxValue ? int.MaxValue : (int)total;
            }
        }

        public static void ProcessFrame(int pMaximumBattles)
        {
            if (!ArmyRtsWarDoctrine.IsAbstractDecisive ||
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                World.world?.armies == null) return;
            int maximumBattles = Math.Max(0, pMaximumBattles);
            if (maximumBattles == 0) return;
            DiscoverPendingTransactions();
            var reservedArmies = new HashSet<long>();
            foreach (ArmyAbstractBattleTransactionSnapshot snapshot in PendingTransactions.Values)
                foreach (long armyId in snapshot.ParticipantArmyIds)
                    reservedArmies.Add(armyId);
            IReadOnlyList<ArmyRtsMission> missions =
                ArmyRtsControllerService.SnapshotMissions();
            bool hasNewBattle = false;
            if (_discoveryComplete)
                for (int i = 0; i < missions.Count; i++)
                    if (missions[i]?.ProposalKind == ArmyRtsProposalKind.Attack &&
                        !reservedArmies.Contains(missions[i].ArmyId))
                    {
                        hasNewBattle = true;
                        break;
                    }
            int recoveryBudget = maximumBattles;
            if (hasNewBattle && PendingTransactions.Count > 0)
            {
                if (maximumBattles > 1) recoveryBudget--;
                else
                {
                    recoveryBudget = _preferNewBattle ? 0 : 1;
                    _preferNewBattle = !_preferNewBattle;
                }
            }
            int processed = ResumePendingTransactions(recoveryBudget);
            if (processed >= maximumBattles || !_discoveryComplete) return;
            var groups = new Dictionary<(long WarId, long CityId),
                List<ArmyRtsMission>>();
            for (int i = 0; i < missions.Count; i++)
            {
                ArmyRtsMission mission = missions[i];
                if (mission?.ProposalKind != ArmyRtsProposalKind.Attack ||
                    mission.WarId < 0L || mission.TargetCityId < 0L ||
                    reservedArmies.Contains(mission.ArmyId))
                    continue;
                var key = (mission.WarId, mission.TargetCityId);
                if (!groups.TryGetValue(key,
                        out List<ArmyRtsMission> group))
                {
                    group = new List<ArmyRtsMission>();
                    groups[key] = group;
                }
                bool duplicate = false;
                for (int j = 0; j < group.Count; j++)
                    if (group[j].ArmyId == mission.ArmyId)
                    {
                        duplicate = true;
                        break;
                    }
                if (!duplicate) group.Add(mission);
            }

            var keys = new List<(long WarId, long CityId)>(groups.Keys);
            keys.Sort((pLeft, pRight) =>
            {
                int war = pLeft.WarId.CompareTo(pRight.WarId);
                return war != 0 ? war : pLeft.CityId.CompareTo(pRight.CityId);
            });
            for (int i = 0; i < keys.Count &&
                         processed < maximumBattles; i++)
            {
                List<ArmyRtsMission> group = groups[keys[i]];
                group.Sort((pLeft, pRight) =>
                    pLeft.ArmyId.CompareTo(pRight.ArmyId));
                if (TryResolve(keys[i].WarId, keys[i].CityId, group,
                        reservedArmies))
                    processed++;
            }
        }

        public static void ClearRuntime()
        {
            ProcessingKeys.Clear();
            PendingTransactions.Clear();
            _cityDiscovery?.Dispose();
            _cityDiscovery = null;
            _discoveryComplete = false;
            _preferNewBattle = false;
            _recoveryCursor = 0;
        }

        private static bool TryResolve(long pWarId, long pTargetCityId,
            IReadOnlyList<ArmyRtsMission> pMissions,
            HashSet<long> pReservedArmies)
        {
            City target = FindCity(pTargetCityId);
            War war = FindWar(pWarId);
            if (target?.data == null || war?.data == null ||
                target.isRekt() || war.hasEnded()) return false;
            ArmyAbstractBattleTransactionSnapshot pending = ReadTransaction(target);
            if (pending != null && pending.Phase != ArmyAbstractBattlePhase.Complete)
                return false;

            var attackers = new List<ArmyAbstractBattleParticipant>();
            var attackerArmies = new Dictionary<long, Army>();
            var moraleByKingdom = new Dictionary<long,
                CustomCourtEffectModifier>();
            Kingdom defenderKingdom = target.kingdom;
            for (int i = 0; i < pMissions.Count; i++)
            {
                ArmyRtsMission mission = pMissions[i];
                Army army = FindArmy(mission.ArmyId);
                Kingdom kingdom = SafeKingdom(army);
                if (army?.data == null || pReservedArmies.Contains(army.id) ||
                    kingdom?.data == null ||
                    defenderKingdom?.data == null || kingdom == defenderKingdom ||
                    !IsWarParticipant(war, kingdom) ||
                    !IsWarParticipant(war, defenderKingdom)) continue;
                ArmyRtsObjectiveState state = ArmyRtsObjectiveService.
                    Classify(war, kingdom, target);
                if (state != ArmyRtsObjectiveState.OpenAttack) continue;
                int count = SafeUnitCount(army);
                if (count <= 0) continue;
                Actor captain = SafeCaptain(army);
                attackers.Add(new ArmyAbstractBattleParticipant
                {
                    ArmyId = army.id,
                    ActorId = captain?.data?.id ?? -1L,
                    UnitCount = count,
                    CommanderStrength = CommanderStrength(captain),
                    MoraleModifier = ResolveMoraleModifier(kingdom, moraleByKingdom),
                    IsAttacker = true,
                    IsSynthetic = false,
                    OwningCityId = AWArmyService.GetAnchorCityId(army)
                });
                attackerArmies[army.id] = army;
            }
            if (attackers.Count == 0) return false;

            var defenders = new List<ArmyAbstractBattleParticipant>();
            List<Army> defenderArmies = CollectDefenderArmies(target);
            for (int i = 0; i < defenderArmies.Count; i++)
            {
                Army defender = defenderArmies[i];
                if (pReservedArmies.Contains(defender.id)) return false;
                int count = SafeUnitCount(defender);
                if (count > 0)
                {
                    Actor captain = SafeCaptain(defender);
                    defenders.Add(new ArmyAbstractBattleParticipant
                    {
                        ArmyId = defender.id,
                        ActorId = captain?.data?.id ?? -1L,
                        UnitCount = count,
                        CommanderStrength = CommanderStrength(captain),
                        MoraleModifier = ResolveMoraleModifier(defenderKingdom, moraleByKingdom),
                        IsAttacker = false,
                        IsSynthetic = false,
                        OwningCityId = target.id
                    });
                }
            }

            var facts = new ArmyAbstractBattleFacts
            {
                WarId = pWarId,
                TargetCityId = pTargetCityId,
                Attackers = attackers,
                Defenders = defenders
            };
            ulong participantHash = ArmyAbstractBattleRules.ParticipantHash(
                facts);
            target.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PARTICIPANT_HASH,
                out long storedHash, 0L);
            target.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_SEQUENCE,
                out long storedSequence, 0L);
            target.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                out int storedPhase, 0);
            if ((ulong)storedHash == participantHash &&
                storedPhase == (int)ArmyAbstractBattlePhase.Complete)
                return false;
            long sequence = (ulong)storedHash == participantHash
                ? Math.Max(0L, storedSequence)
                : Math.Max(0L, storedSequence) + 1L;
            facts.ResolutionSequence = sequence;
            ArmyAbstractBattleResult result =
                ArmyAbstractBattleRules.Resolve(facts);
            if (result.Outcome == ArmyAbstractBattleOutcome.NoBattle)
                return false;
            ArmyAbstractBattleParticipant primary =
                ArmyAbstractBattleRules.SelectPrimaryAttacker(attackers);
            Army primaryArmy = primary == null ? null : FindArmy(primary.ArmyId);
            Kingdom receiver = result.Outcome == ArmyAbstractBattleOutcome.AttackVictory
                ? SafeKingdom(primaryArmy) : defenderKingdom;
            City transferredCity = result.Outcome == ArmyAbstractBattleOutcome.AttackVictory
                ? target : primary == null ? null : FindCity(primary.OwningCityId);
            if (receiver?.data == null || transferredCity?.data == null)
                return false;

            var participants = new List<Army>(attackerArmies.Values);
            participants.AddRange(defenderArmies);
            List<Army> losers = result.Outcome == ArmyAbstractBattleOutcome.AttackVictory
                ? defenderArmies : new List<Army>(attackerArmies.Values);
            ArmyAbstractBattleTransactionSnapshot snapshot =
                ArmyAbstractBattleTransactionRules.Prepare(pWarId, target.id,
                    transferredCity.id, sequence, result.Outcome, receiver.id,
                    primary?.ArmyId ?? -1L, participantHash,
                    CollectArmyIds(participants), CollectActorIds(participants),
                    CollectArmyIds(losers), CollectActorIds(losers));
            SaveTransaction(target, snapshot);
            foreach (long armyId in snapshot.ParticipantArmyIds)
                pReservedArmies.Add(armyId);
            TryResumeTransaction(target, snapshot);
            return true;
        }

        private static void DiscoverPendingTransactions()
        {
            if (_discoveryComplete) return;
            if (World.world?.cities == null)
            {
                _discoveryComplete = true;
                return;
            }
            _cityDiscovery ??= EnumerateCities().GetEnumerator();
            try
            {
                for (int inspected = 0; inspected < MaximumCitiesDiscoveredPerFrame; inspected++)
                {
                    if (!_cityDiscovery.MoveNext())
                    {
                        _cityDiscovery.Dispose();
                        _cityDiscovery = null;
                        _discoveryComplete = true;
                        break;
                    }
                    City city = _cityDiscovery.Current;
                    if (city?.data == null || PendingTransactions.ContainsKey(city.id))
                        continue;
                    ArmyAbstractBattleTransactionSnapshot snapshot = ReadTransaction(city);
                    if (snapshot != null && snapshot.Phase != ArmyAbstractBattlePhase.Complete)
                        PendingTransactions[city.id] = snapshot;
                }
            }
            catch (InvalidOperationException)
            {
                // City creation/removal may invalidate the enumerator between frames.
                // Restart discovery without spending unbounded work in this frame.
                _cityDiscovery.Dispose();
                _cityDiscovery = null;
            }
        }

        private static IEnumerable<City> EnumerateCities()
        {
            foreach (City city in World.world.cities)
                yield return city;
        }

        private static int ResumePendingTransactions(int pMaximumBattles)
        {
            var pending = new List<ArmyAbstractBattleTransactionSnapshot>(
                PendingTransactions.Values);
            if (pending.Count == 0)
            {
                _recoveryCursor = 0;
                return 0;
            }
            pending.Sort((left, right) => left.TargetCityId.CompareTo(right.TargetCityId));
            int count = Math.Min(pMaximumBattles, pending.Count);
            int start = _recoveryCursor % pending.Count;
            for (int i = 0; i < count; i++)
            {
                ArmyAbstractBattleTransactionSnapshot snapshot =
                    pending[(start + i) % pending.Count];
                TryResumeTransaction(FindCity(snapshot.TargetCityId), snapshot);
            }
            _recoveryCursor = (start + count) % pending.Count;
            return count;
        }

        private static ArmyAbstractBattleTransactionSnapshot ReadTransaction(City pCity)
        {
            if (pCity?.data == null) return null;
            pCity.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                out int phase, 0);
            if (phase == (int)ArmyAbstractBattlePhase.Complete) return null;
            pCity.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION,
                out string encoded, string.Empty);
            ArmyAbstractBattleTransactionSnapshot snapshot =
                ArmyAbstractBattleTransactionRules.Decode(encoded);
            if (snapshot == null || snapshot.TargetCityId != pCity.id ||
                snapshot.Phase < ArmyAbstractBattlePhase.Prepared ||
                snapshot.Outcome == ArmyAbstractBattleOutcome.NoBattle)
                return null;
            return snapshot;
        }

        private static void SaveTransaction(City pCity,
            ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            pCity.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION,
                ArmyAbstractBattleTransactionRules.Encode(pSnapshot));
            pCity.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_OPERATION,
                pSnapshot.WarId + ":" + pSnapshot.TargetCityId + ":" + pSnapshot.Sequence);
            pCity.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_SEQUENCE,
                pSnapshot.Sequence);
            pCity.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PARTICIPANT_HASH,
                unchecked((long)pSnapshot.ParticipantHash));
            pCity.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                (int)pSnapshot.Phase);
            if (pSnapshot.Phase == ArmyAbstractBattlePhase.Complete)
                PendingTransactions.Remove(pSnapshot.TargetCityId);
            else
                PendingTransactions[pSnapshot.TargetCityId] = pSnapshot;
        }

        private static bool TryResumeTransaction(City pTarget,
            ArmyAbstractBattleTransactionSnapshot snapshot)
        {
            if (pTarget?.data == null)
            {
                PendingTransactions.Remove(snapshot.TargetCityId);
                return true;
            }
            string operation = snapshot.WarId + ":" + snapshot.TargetCityId + ":" +
                               snapshot.Sequence;
            if (!ProcessingKeys.Add(operation)) return false;
            try
            {
                if (snapshot.Phase == ArmyAbstractBattlePhase.Prepared)
                {
                    City transferredCity = FindCity(snapshot.TransferredCityId);
                    Kingdom receiver = FindKingdom(snapshot.ReceiverKingdomId);
                    War war = FindWar(snapshot.WarId);
                    // A transfer may already have succeeded immediately before a save.
                    // In that case finish the saved loser roster even if the war ended.
                    if (transferredCity?.data == null || receiver?.data == null ||
                        (transferredCity.kingdom != receiver &&
                         (war?.data == null || war.hasEnded() ||
                          ArmyRtsObjectiveService.Classify(war, receiver, transferredCity) !=
                          ArmyRtsObjectiveState.OpenAttack)))
                    {
                        SaveTransaction(pTarget, ArmyAbstractBattleTransactionRules.Advance(
                            snapshot, ArmyAbstractBattlePhase.Complete));
                        return true;
                    }
                    ReleasePersistedParticipants(snapshot);
                    if (!TryTransferCity(transferredCity, receiver)) return false;
                    snapshot = ArmyAbstractBattleTransactionRules.Advance(snapshot,
                        ArmyAbstractBattlePhase.Transferred);
                    SaveTransaction(pTarget, snapshot);
                }
                if (snapshot.Phase == ArmyAbstractBattlePhase.Transferred)
                {
                    if (snapshot.Outcome == ArmyAbstractBattleOutcome.AttackVictory)
                        ArmyRtsControllerService.OnTargetCompleted(pTarget);
                    snapshot = ArmyAbstractBattleTransactionRules.Advance(snapshot,
                        ArmyAbstractBattlePhase.Demobilizing);
                    SaveTransaction(pTarget, snapshot);
                }
                if (snapshot.Phase == ArmyAbstractBattlePhase.Demobilizing)
                {
                    while (!ArmyAbstractBattleTransactionRules.IsDemobilizationComplete(snapshot))
                    {
                        Actor actor = FindActor(snapshot.LoserActorIds[snapshot.DemobilizationCursor]);
                        DemobilizeActor(FindLoserArmy(snapshot, actor), actor);
                        snapshot.DemobilizationCursor++;
                    }
                    foreach (long armyId in snapshot.LoserArmyIds)
                    {
                        ArmyRtsControllerService.Invalidate(armyId);
                        Army army = FindArmy(armyId);
                        if (army?.data != null) ArmyRtsWarLifecycleService.ClearArmy(army);
                    }
                    snapshot = ArmyAbstractBattleTransactionRules.Advance(snapshot,
                        ArmyAbstractBattlePhase.Complete);
                    SaveTransaction(pTarget, snapshot);
                }
                return snapshot.Phase == ArmyAbstractBattlePhase.Complete;
            }
            catch (Exception error)
            {
                // Retirements are idempotent, so a save taken from inside a retirement
                // may safely replay earlier actors. Persist partial progress on failure
                // rather than re-encoding the entire roster for every soldier.
                try { SaveTransaction(pTarget, snapshot); }
                catch (Exception persistenceError)
                {
                    ModClass.LogWarning("AW3 abstract battle progress save failed for " +
                        operation + ": " + persistenceError);
                }
                ModClass.LogWarning("AW3 abstract battle transaction " + operation +
                    " remains pending at " + snapshot.Phase + ": " + error);
                return false;
            }
            finally
            {
                ProcessingKeys.Remove(operation);
            }
        }

        private static List<Army> CollectDefenderArmies(City pCity)
        {
            var result = new List<Army>();
            var seen = new HashSet<long>();
            if (ArmyFieldIndexService.TryGetCityArmy(pCity, out Army ordinary))
                AddDefender(ordinary);
            foreach (string role in new[] { AWArmyRole.RoyalGuard, AWArmyRole.SlaveArmy,
                         AWArmyRole.BorderArmy, AWArmyRole.FeudatoryGarrison })
                foreach (Army army in AWArmyService.GetRoleArmies(pCity.kingdom, role))
                    AddDefender(army);
            result.Sort((left, right) => left.id.CompareTo(right.id));
            return result;

            void AddDefender(Army army)
            {
                if (army?.data != null && SafeKingdom(army) == pCity.kingdom &&
                    AWArmyService.GetAnchorCityId(army) == pCity.id &&
                    SafeUnitCount(army) > 0 && seen.Add(army.id))
                    result.Add(army);
            }
        }

        private static List<long> CollectArmyIds(IEnumerable<Army> pArmies)
        {
            var result = new List<long>();
            foreach (Army army in pArmies)
                if (army?.data != null) result.Add(army.id);
            return result;
        }

        private static HashSet<long> CollectActorIds(IEnumerable<Army> pArmies)
        {
            var result = new HashSet<long>();
            foreach (Army army in pArmies)
            {
                for (int i = 0; i < army.units.Count; i++)
                {
                    Actor actor = army.units[i];
                    if (actor?.data != null) result.Add(actor.data.id);
                }
                Actor captain = SafeCaptain(army);
                if (captain?.data != null) result.Add(captain.data.id);
            }
            return result;
        }

        private static void ReleasePersistedParticipants(
            ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            foreach (long armyId in pSnapshot.ParticipantArmyIds)
                ReleaseStrategicControl(FindArmy(armyId));
        }

        private static Army FindLoserArmy(ArmyAbstractBattleTransactionSnapshot pSnapshot,
            Actor pActor)
        {
            if (pActor?.data == null) return null;
            foreach (long armyId in pSnapshot.LoserArmyIds)
            {
                Army army = FindArmy(armyId);
                if (army == null) continue;
                if (pActor.army == army || SafeCaptain(army) == pActor) return army;
                for (int i = 0; i < army.units.Count; i++)
                    if (army.units[i] == pActor) return army;
            }
            return null;
        }

        private static void ReleaseStrategicControl(Army pArmy)
        {
            if (pArmy?.data == null) return;
            try
            {
                ArmyRouteProviderService.Cancel(pArmy.id,
                    ArmyRouteCancelReason.TargetReplaced);
            }
            catch { }
            try { AWArmyMarchService.ClearArmy(pArmy.id); }
            catch { }
            try { ArmyRtsTransportService.ReleaseArmy(pArmy); }
            catch { }
            try { ArmyFormationService.RemoveArmy(pArmy.id); }
            catch { }
        }

        private static void DemobilizeActor(Army pArmy, Actor pActor)
        {
            if (pActor?.data == null) return;
            if (SyntheticLevyService.IsSynthetic(pActor))
            {
                SyntheticLevyService.RemoveWithoutPersonalHistory(pActor);
                return;
            }
            if (IsProtectedCivilAuthority(pActor))
            {
                using (ArmyCaptainDisposalScope.Open(pArmy))
                {
                    try { pActor.removeFromArmy(); } catch { }
                    try
                    {
                        if (pActor.isWarrior()) pActor.stopBeingWarrior();
                    }
                    catch { }
                }
                return;
            }
            try
            {
                if (pActor.isAlive() && !pActor.isRekt())
                    pActor.die(false, AttackType.Other, false, false);
            }
            catch
            {
                using (ArmyCaptainDisposalScope.Open(pArmy))
                {
                    try { pActor.removeFromArmy(); } catch { }
                }
            }
        }

        private static bool IsProtectedCivilAuthority(Actor pActor)
        {
            try
            {
                if (pActor.isKing() || pActor.isCityLeader() ||
                    GeneralService.IsActiveGeneralFast(pActor)) return true;
                if (HeirService.IsCurrentHeir(pActor.kingdom, pActor))
                    return true;
            }
            catch { }
            try
            {
                pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir,
                    false);
                if (isHeir) return true;
            }
            catch { }
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string officeId, string.Empty);
            return !string.IsNullOrWhiteSpace(officeId);
        }

        private static bool TryTransferCity(City pCity, Kingdom pReceiver)
        {
            if (pCity?.data == null || pReceiver?.data == null ||
                pCity.isRekt()) return false;
            if (pCity.kingdom == pReceiver) return true;
            try
            {
                pCity.joinAnotherKingdom(pReceiver, pCaptured: false,
                    pRebellion: false);
                return pCity.kingdom == pReceiver;
            }
            catch { return false; }
        }

        private static int CommanderStrength(Actor pActor)
        {
            if (pActor?.data == null) return 0;
            try { return Math.Max(0, Math.Min(100, pActor.warfare)); }
            catch { return 0; }
        }

        private static CustomCourtEffectModifier ResolveMoraleModifier(
            Kingdom pKingdom,
            Dictionary<long, CustomCourtEffectModifier> pCache)
        {
            if (pKingdom?.data == null || pCache == null)
                return CustomCourtEffectModifier.Identity;
            if (pCache.TryGetValue(pKingdom.id,
                    out CustomCourtEffectModifier cached)) return cached;
            CustomCourtEffectModifier modifier =
                CustomCourtRuntimeEffectService.GetArmyModifier(pKingdom);
            pCache[pKingdom.id] = modifier;
            return modifier;
        }

        private static bool IsWarParticipant(War pWar, Kingdom pKingdom)
        {
            try { return pKingdom?.data != null && pWar.hasKingdom(pKingdom); }
            catch { return false; }
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return AWArmyService.GetIntendedKingdom(pArmy); }
            catch
            {
                try { return pArmy?.getKingdom(); }
                catch { return null; }
            }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }
    }
}
#endif
