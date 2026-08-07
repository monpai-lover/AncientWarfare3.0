#if !AW3_RULES_TESTS
using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.pathfinding;

namespace AncientWarfare3.core.lineage
{
    internal enum ArmyAbstractBattlePhase
    {
        None = 0,
        Prepared = 1,
        Transferred = 2,
        Demobilizing = 3,
        Complete = 4
    }

    internal static class ArmyAbstractBattleService
    {
        private const int MaximumBattlesPerFrame = 4;
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
        }

        public static void ClearRuntime()
        {
            ProcessingKeys.Clear();
        }

        private static bool TryResolve(long pWarId, long pTargetCityId,
            IReadOnlyList<ArmyRtsMission> pMissions)
        {
            City target = FindCity(pTargetCityId);
            War war = FindWar(pWarId);
            if (target?.data == null || war?.data == null ||
                target.isRekt() || war.hasEnded()) return false;
            if (!ArmyFieldIndexService.TryGetCityArmy(target,
                    out Army defender)) defender = null;

            var attackers = new List<ArmyAbstractBattleParticipant>();
            var attackerArmies = new Dictionary<long, Army>();
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
                attackerArmies[army.id] = army;
            }
            if (attackers.Count == 0) return false;

            var defenders = new List<ArmyAbstractBattleParticipant>();
            if (defender?.data != null)
            {
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
            string operation = pWarId + ":" + pTargetCityId + ":" +
                               sequence;
            if (!ProcessingKeys.Add(operation)) return false;
            try
            {
                target.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_OPERATION,
                    operation);
                target.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                    (int)ArmyAbstractBattlePhase.Prepared);
                target.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_SEQUENCE,
                    sequence);
                target.data.set(
                    LineageKeys.AW_RTS_ABSTRACT_BATTLE_PARTICIPANT_HASH,
                    unchecked((long)participantHash));

                for (int i = 0; i < attackers.Count; i++)
                {
                    Army army = FindArmy(attackers[i].ArmyId);
                    ReleaseStrategicControl(army);
                }

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
                    !TryTransferCity(transferredCity, receiver)) return false;
                target.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                    (int)ArmyAbstractBattlePhase.Transferred);
                target.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                    (int)ArmyAbstractBattlePhase.Demobilizing);

                if (result.Outcome == ArmyAbstractBattleOutcome.AttackVictory)
                {
                    ArmyRtsControllerService.OnTargetCompleted(target);
                    if (defender?.data != null)
                        DemobilizeArmy(defender);
                }
                else
                {
                    var loserIds = new HashSet<long>();
                    for (int i = 0; i < attackers.Count; i++)
                        if (loserIds.Add(attackers[i].ArmyId) &&
                            attackerArmies.TryGetValue(attackers[i].ArmyId,
                                out Army loser)) DemobilizeArmy(loser);
                }
                target.data.set(LineageKeys.AW_RTS_ABSTRACT_BATTLE_PHASE,
                    (int)ArmyAbstractBattlePhase.Complete);
                return true;
            }
            finally
            {
                ProcessingKeys.Remove(operation);
            }
        }

        private static void DemobilizeArmy(Army pArmy)
        {
            if (pArmy?.data == null) return;
            var actors = new List<Actor>();
            var seen = new HashSet<long>();
            try
            {
                for (int i = 0; i < pArmy.units.Count; i++)
                {
                    Actor actor = pArmy.units[i];
                    if (actor?.data != null && seen.Add(actor.data.id))
                        actors.Add(actor);
                }
            }
            catch { }
            Actor captain = SafeCaptain(pArmy);
            if (captain?.data != null && seen.Add(captain.data.id))
                actors.Add(captain);
            for (int i = 0; i < actors.Count; i++)
                DemobilizeActor(pArmy, actors[i]);
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

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }
    }
}
#endif
