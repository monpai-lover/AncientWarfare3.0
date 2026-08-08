#if !AW3_RULES_TESTS
using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.pathfinding;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyAbstractBattleService
    {
        private const int MaximumBattlesPerFrame = 4;
        private const int MaximumDemobilizationsPerFrame = 64;
        private static readonly HashSet<string> ProcessingKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public static void ProcessFrame()
        {
            if (!ArmyRtsWarDoctrine.IsAbstractDecisive ||
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                World.world?.armies == null) return;
            IReadOnlyList<ArmyRtsMission> missions =
                ArmyRtsControllerService.SnapshotMissions();
            var groups = new Dictionary<(long WarId, long CityId),
                List<ArmyRtsMission>>();
            for (int i = 0; i < missions.Count; i++)
            {
                ArmyRtsMission mission = missions[i];
                if (mission?.ProposalKind != ArmyRtsProposalKind.Attack ||
                    mission.WarId < 0L || mission.TargetCityId < 0L)
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
            int processed = 0;
            for (int i = 0; i < keys.Count &&
                         processed < MaximumBattlesPerFrame; i++)
            {
                List<ArmyRtsMission> group = groups[keys[i]];
                group.Sort((pLeft, pRight) =>
                    pLeft.ArmyId.CompareTo(pRight.ArmyId));
                if (TryResolve(keys[i].WarId, keys[i].CityId, group))
                    processed++;
            }

            if (processed >= MaximumBattlesPerFrame) return;
            List<(City City, ArmyAbstractBattleTransactionSnapshot Snapshot)>
                pending = SnapshotPendingTransactions();
            for (int i = 0; i < pending.Count &&
                         processed < MaximumBattlesPerFrame; i++)
            {
                var entry = pending[i];
                if (groups.ContainsKey((entry.Snapshot.WarId,
                        entry.Snapshot.TargetCityId))) continue;
                string operation = BuildOperation(entry.Snapshot);
                if (!ProcessingKeys.Add(operation)) continue;
                try
                {
                    if (ResumeTransaction(entry.City, entry.Snapshot))
                        processed++;
                }
                finally
                {
                    ProcessingKeys.Remove(operation);
                }
            }
        }

        public static void ClearRuntime()
        {
            ProcessingKeys.Clear();
        }

        private static bool TryResolve(long pWarId, long pTargetCityId,
            IReadOnlyList<ArmyRtsMission> pMissions)
        {
            City target = FindCity(pTargetCityId);
            if (target?.data == null || target.isRekt()) return false;

            ArmyAbstractBattleTransactionSnapshot stored =
                ReadTransaction(target);
            if (stored != null)
            {
                if (stored.WarId != pWarId ||
                    stored.TargetCityId != pTargetCityId)
                    return false;
                if (stored.Phase == ArmyAbstractBattlePhase.Complete)
                    stored = null;
                else
                {
                    string operation = BuildOperation(stored);
                    if (!ProcessingKeys.Add(operation)) return false;
                    try { return ResumeTransaction(target, stored); }
                    finally { ProcessingKeys.Remove(operation); }
                }
            }

            target.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                out int legacyPhase, 0);
            if (legacyPhase >= (int)ArmyAbstractBattlePhase.Prepared &&
                legacyPhase < (int)ArmyAbstractBattlePhase.Complete)
                return false;

            War war = FindWar(pWarId);
            if (war?.data == null || war.hasEnded()) return false;
            Army canonicalDefender = null;
            if (!ArmyFieldIndexService.TryGetCityArmy(target,
                    out canonicalDefender)) canonicalDefender = null;

            var attackers = new List<ArmyAbstractBattleParticipant>();
            Kingdom defenderKingdom = target.kingdom;
            for (int i = 0; i < pMissions.Count; i++)
            {
                ArmyRtsMission mission = pMissions[i];
                Army army = FindArmy(mission.ArmyId);
                Kingdom kingdom = SafeKingdom(army);
                if (army?.data == null || kingdom?.data == null ||
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
                    IsAttacker = true,
                    IsSynthetic = false,
                    OwningCityId = AWArmyService.GetAnchorCityId(army)
                });
            }
            if (attackers.Count == 0) return false;

            List<Army> defenderArmies = CollectDefenderArmies(target,
                defenderKingdom, canonicalDefender);
            var defenders = new List<ArmyAbstractBattleParticipant>();
            for (int i = 0; i < defenderArmies.Count; i++)
            {
                Army defender = defenderArmies[i];
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
            target.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_SEQUENCE,
                out long storedSequence, 0L);
            long sequence = Math.Max(0L, storedSequence) + 1L;
            facts.ResolutionSequence = sequence;
            ArmyAbstractBattleResult result =
                ArmyAbstractBattleRules.Resolve(facts);
            if (result.Outcome == ArmyAbstractBattleOutcome.NoBattle)
                return false;
            string operation = pWarId + ":" + pTargetCityId + ":" +
                               sequence;
            if (!ProcessingKeys.Add(operation)) return false;
            try
            {
                ArmyAbstractBattleParticipant primary =
                    ArmyAbstractBattleRules.SelectPrimaryAttacker(attackers);
                Kingdom receiver;
                City transferredCity;
                if (result.Outcome == ArmyAbstractBattleOutcome.AttackVictory)
                {
                    Army primaryArmy = primary == null ? null :
                        FindArmy(primary.ArmyId);
                    receiver = SafeKingdom(primaryArmy);
                    transferredCity = target;
                }
                else
                {
                    receiver = defenderKingdom;
                    transferredCity = primary == null
                        ? null
                        : FindCity(primary.OwningCityId);
                }
                if (receiver?.data == null || transferredCity?.data == null ||
                    transferredCity.isRekt())
                    return false;

                var participantArmyIds = new List<long>();
                var participantActorIds = new List<long>();
                for (int i = 0; i < attackers.Count; i++)
                    AddArmyRoster(FindArmy(attackers[i].ArmyId),
                        participantArmyIds, participantActorIds);
                for (int i = 0; i < defenders.Count; i++)
                    AddArmyRoster(FindArmy(defenders[i].ArmyId),
                        participantArmyIds, participantActorIds);

                var loserArmyIds = new List<long>();
                if (result.Outcome == ArmyAbstractBattleOutcome.AttackVictory)
                {
                    for (int i = 0; i < defenders.Count; i++)
                        loserArmyIds.Add(defenders[i].ArmyId);
                }
                else
                {
                    for (int i = 0; i < attackers.Count; i++)
                        loserArmyIds.Add(attackers[i].ArmyId);
                }
                var loserActorIds = new List<long>();
                for (int i = 0; i < loserArmyIds.Count; i++)
                    AddArmyActorIds(FindArmy(loserArmyIds[i]), loserActorIds);

                ArmyAbstractBattleTransactionSnapshot transaction =
                    ArmyAbstractBattleTransactionRules.Prepare(
                        pWarId, pTargetCityId, transferredCity.id, sequence,
                        result.Outcome, receiver.id,
                        primary?.ArmyId ?? -1L, participantHash,
                        participantArmyIds, participantActorIds, loserArmyIds,
                        loserActorIds);
                PersistTransaction(target, transaction, operation);
                return ResumeTransaction(target, transaction);
            }
            finally
            {
                ProcessingKeys.Remove(operation);
            }
        }

        private static List<(City City,
            ArmyAbstractBattleTransactionSnapshot Snapshot)>
            SnapshotPendingTransactions()
        {
            var result = new List<(City City,
                ArmyAbstractBattleTransactionSnapshot Snapshot)>();
            if (World.world?.cities == null) return result;
            try
            {
                foreach (City city in World.world.cities)
                {
                    ArmyAbstractBattleTransactionSnapshot snapshot =
                        ReadTransaction(city);
                    if (city?.data == null || snapshot == null ||
                        snapshot.Phase == ArmyAbstractBattlePhase.Complete)
                        continue;
                    result.Add((city, snapshot));
                }
            }
            catch { }
            result.Sort((pLeft, pRight) =>
            {
                int war = pLeft.Snapshot.WarId.CompareTo(
                    pRight.Snapshot.WarId);
                return war != 0 ? war : pLeft.Snapshot.TargetCityId.
                    CompareTo(pRight.Snapshot.TargetCityId);
            });
            return result;
        }

        private static ArmyAbstractBattleTransactionSnapshot ReadTransaction(
            City pCity)
        {
            if (pCity?.data == null) return null;
            pCity.data.get(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION,
                out string encoded, string.Empty);
            return ArmyAbstractBattleTransactionRules.Decode(encoded);
        }

        private static void PersistTransaction(City pTarget,
            ArmyAbstractBattleTransactionSnapshot pSnapshot,
            string pOperation = null)
        {
            if (pTarget?.data == null || pSnapshot == null) return;
            string operation = string.IsNullOrWhiteSpace(pOperation)
                ? BuildOperation(pSnapshot) : pOperation;
            pTarget.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_OPERATION,
                operation);
            pTarget.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_TRANSACTION,
                ArmyAbstractBattleTransactionRules.Encode(pSnapshot));
            pTarget.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                (int)pSnapshot.Phase);
            pTarget.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_SEQUENCE,
                pSnapshot.Sequence);
            pTarget.data.set(
                LineageKeys.AW_RTS_ABSTRACT_BATTLE_PARTICIPANT_HASH,
                unchecked((long)pSnapshot.ParticipantHash));
        }

        private static string BuildOperation(
            ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            if (pSnapshot == null) return string.Empty;
            return pSnapshot.WarId + ":" + pSnapshot.TargetCityId + ":" +
                pSnapshot.Sequence;
        }

        private static bool ResumeTransaction(City pTarget,
            ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            if (pTarget?.data == null || pSnapshot == null ||
                pSnapshot.Phase == ArmyAbstractBattlePhase.Complete)
                return false;

            ArmyAbstractBattleTransactionSnapshot snapshot = pSnapshot.Clone();
            if (snapshot.Phase == ArmyAbstractBattlePhase.Prepared)
            {
                // The roster is frozen in Prepared. Release all strategic
                // control before any transfer so a retry cannot move these
                // armies while the city ownership change is in flight.
                ReleasePersistedParticipants(snapshot);
                City transferredCity = FindCity(snapshot.TransferredCityId);
                Kingdom receiver = FindKingdom(snapshot.ReceiverKingdomId);
                if (transferredCity?.data == null || receiver?.data == null ||
                    !TryTransferCity(transferredCity, receiver)) return false;
                snapshot = ArmyAbstractBattleTransactionRules.Advance(snapshot,
                    ArmyAbstractBattlePhase.Transferred);
                PersistTransaction(pTarget, snapshot);
            }

            if (snapshot.Phase == ArmyAbstractBattlePhase.Transferred)
            {
                if (snapshot.Outcome == ArmyAbstractBattleOutcome.AttackVictory)
                    ArmyRtsControllerService.OnTargetCompleted(pTarget);
                snapshot = ArmyAbstractBattleTransactionRules.Advance(snapshot,
                    ArmyAbstractBattlePhase.Demobilizing);
                PersistTransaction(pTarget, snapshot);
            }

            if (snapshot.Phase != ArmyAbstractBattlePhase.Demobilizing)
                return false;

            IReadOnlyList<long> loserActorIds = snapshot.LoserActorIds ??
                Array.Empty<long>();
            int cursor = Math.Max(0, Math.Min(snapshot.DemobilizationCursor,
                loserActorIds.Count));
            int end = Math.Min(loserActorIds.Count,
                cursor + MaximumDemobilizationsPerFrame);
            for (int i = cursor; i < end; i++)
            {
                Actor actor = FindActor(loserActorIds[i]);
                if (actor?.data == null) continue;
                DemobilizeActor(FindLoserArmy(snapshot, actor), actor);
            }
            snapshot.DemobilizationCursor = end;
            if (end < loserActorIds.Count)
            {
                PersistTransaction(pTarget, snapshot);
                return true;
            }

            IReadOnlyList<long> loserArmyIds = snapshot.LoserArmyIds ??
                Array.Empty<long>();
            for (int i = 0; i < loserArmyIds.Count; i++)
                CleanupArmy(FindArmy(loserArmyIds[i]));
            snapshot = ArmyAbstractBattleTransactionRules.Advance(snapshot,
                ArmyAbstractBattlePhase.Complete);
            PersistTransaction(pTarget, snapshot);
            return true;
        }

        private static void ReleasePersistedParticipants(
            ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            IReadOnlyList<long> armyIds = pSnapshot?.ParticipantArmyIds ??
                Array.Empty<long>();
            for (int i = 0; i < armyIds.Count; i++)
                ReleaseStrategicControl(FindArmy(armyIds[i]));
        }

        private static void AddArmyRoster(Army pArmy, ICollection<long> pArmyIds,
            ICollection<long> pActorIds)
        {
            if (pArmy?.data == null) return;
            pArmyIds.Add(pArmy.id);
            AddArmyActorIds(pArmy, pActorIds);
        }

        private static void AddArmyActorIds(Army pArmy,
            ICollection<long> pActorIds)
        {
            if (pArmy?.data == null) return;
            try
            {
                for (int i = 0; i < pArmy.units.Count; i++)
                {
                    Actor actor = pArmy.units[i];
                    if (actor?.data != null) pActorIds.Add(actor.data.id);
                }
            }
            catch { }
            Actor captain = SafeCaptain(pArmy);
            if (captain?.data != null) pActorIds.Add(captain.data.id);
        }

        private static List<Army> CollectDefenderArmies(City pTarget,
            Kingdom pKingdom, Army pCanonical)
        {
            var result = new List<Army>();
            var seen = new HashSet<long>();
            AddDefenderArmy(result, seen, pTarget, pKingdom, pCanonical);
            string[] roles =
            {
                AWArmyRole.RoyalGuard,
                AWArmyRole.SlaveArmy,
                AWArmyRole.BorderArmy,
                AWArmyRole.FeudatoryGarrison
            };
            for (int i = 0; i < roles.Length; i++)
            {
                List<Army> roleArmies;
                try { roleArmies = AWArmyService.GetRoleArmies(pKingdom,
                    roles[i]); }
                catch { roleArmies = null; }
                if (roleArmies == null) continue;
                for (int j = 0; j < roleArmies.Count; j++)
                    AddDefenderArmy(result, seen, pTarget, pKingdom,
                        roleArmies[j]);
            }
            result.Sort((pLeft, pRight) => pLeft.id.CompareTo(pRight.id));
            return result;
        }

        private static void AddDefenderArmy(ICollection<Army> pResult,
            ISet<long> pSeen, City pTarget, Kingdom pKingdom, Army pArmy)
        {
            if (pArmy?.data == null || pTarget?.data == null ||
                pKingdom?.data == null || !pArmy.isAlive() ||
                SafeKingdom(pArmy) != pKingdom ||
                AWArmyService.GetAnchorCityId(pArmy) != pTarget.id ||
                !pSeen.Add(pArmy.id)) return;
            pResult.Add(pArmy);
        }

        private static Army FindLoserArmy(
            ArmyAbstractBattleTransactionSnapshot pSnapshot, Actor pActor)
        {
            Army current = SafeArmy(pActor);
            if (current?.data != null && IsPersistedLoserArmy(pSnapshot,
                    current.id)) return current;
            IReadOnlyList<long> loserArmyIds = pSnapshot?.LoserArmyIds ??
                Array.Empty<long>();
            for (int i = 0; i < loserArmyIds.Count; i++)
            {
                Army candidate = FindArmy(loserArmyIds[i]);
                if (candidate?.data == null) continue;
                if (ContainsActor(candidate, pActor)) return candidate;
            }
            return null;
        }

        private static bool IsPersistedLoserArmy(
            ArmyAbstractBattleTransactionSnapshot pSnapshot, long pArmyId)
        {
            IReadOnlyList<long> loserArmyIds = pSnapshot?.LoserArmyIds ??
                Array.Empty<long>();
            for (int i = 0; i < loserArmyIds.Count; i++)
                if (loserArmyIds[i] == pArmyId) return true;
            return false;
        }

        private static bool ContainsActor(Army pArmy, Actor pActor)
        {
            if (pArmy?.data == null || pActor?.data == null) return false;
            if (SafeCaptain(pArmy) == pActor) return true;
            try
            {
                for (int i = 0; i < pArmy.units.Count; i++)
                    if (pArmy.units[i] == pActor) return true;
            }
            catch { }
            return false;
        }

        private static void CleanupArmy(Army pArmy)
        {
            if (pArmy?.data == null) return;
            ArmyRtsControllerService.Invalidate(pArmy.id);
            ArmyRtsWarLifecycleService.ClearArmy(pArmy);
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
                if (!pActor.isAlive() || pActor.isRekt())
                {
                    using (ArmyCaptainDisposalScope.Open(pArmy))
                    {
                        try { pActor.removeFromArmy(); } catch { }
                    }
                    return;
                }
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

        private static Army SafeArmy(Actor pActor)
        {
            try { return pActor?.army; }
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

        private static Actor FindActor(long pActorId)
        {
            try
            {
                return pActorId >= 0L ? World.world?.units?.get(pActorId) :
                    null;
            }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try
            {
                return pKingdomId >= 0L ? World.world?.kingdoms?.get(
                    pKingdomId) : null;
            }
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
