using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.content;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class RoyalGuardService
    {
        private const int MAX_GUARDS_PER_KINGDOM = 20;
        private const float MIN_NOBLE_RATIO = 0.2f;
        private const int CHECK_INTERVAL = 20;
        private const int PROTECT_RADIUS = 10;
        private const int FOLLOW_RADIUS = 4;
        private const int PATROL_MIN_RADIUS = 2;
        private const int PATROL_MAX_RADIUS = 4;
        private const int PATROL_PHASE_INTERVAL = 8;
        private static readonly MethodInfo NewArmyObjectMethod = ResolveNewArmyObjectMethod();
        private static bool _creatingGuardArmy;

        private sealed class GuardCandidate
        {
            public Actor actor;
            public float score;
            public bool noble;
        }

        public static bool IsRoyalGuard(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.ROYAL_GUARD, out bool flag, false);
            pActor.data.get(LineageKeys.ROYAL_GUARD_KINGDOM_ID, out long guardKingdomId, -1L);
            bool hasTrait = pActor.hasTrait(LineageKeys.TRAIT_GUARD);

            if (!flag && guardKingdomId < 0)
            {
                if (hasTrait || IsKingGuardJob(pActor))
                    ClearStaleGuardIdentity(pActor);
                return false;
            }

            if (flag && !hasTrait)
                pActor.addTrait(LineageKeys.TRAIT_GUARD);

            return flag || hasTrait;
        }

        public static bool ShouldBlockNormalArmy(Actor pActor)
        {
            return IsRoyalGuard(pActor);
        }

        public static void EnsureKingdomGuard(Kingdom pKingdom, bool pForce = false)
        {
            if (pKingdom?.data == null) return;

            int now = (int)LineageService.CurTime();
            if (!pForce)
            {
                pKingdom.data.get(LineageKeys.ROYAL_GUARD_LAST_CHECK, out int lastCheck, -1);
                if (lastCheck >= 0 && now - lastCheck < CHECK_INTERVAL) return;
            }
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_LAST_CHECK, now);

            Actor king = pKingdom.king;
            if (king?.data == null || king.isRekt())
            {
                DismissKingdomGuards(pKingdom, "no_king");
                return;
            }

            List<Actor> active = CollectActiveGuards(pKingdom);
            string guardName = BuildGuardName(pKingdom);
            float averageWarriorScore = AverageWarriorScore(pKingdom);
            List<GuardCandidate> candidates = CollectCandidates(pKingdom, active, averageWarriorScore);

            int availableNobles = CountNobles(active) + CountNobleCandidates(candidates);
            if (availableNobles <= 0)
            {
                DismissKingdomGuards(pKingdom, "no_noble_captain");
                return;
            }

            int desired = Math.Min(MAX_GUARDS_PER_KINGDOM, active.Count + candidates.Count);
            desired = Math.Min(desired, availableNobles * 5);
            desired = Math.Max(1, desired);
            TrimExcessGuards(active, desired);

            int targetNobles = Math.Max(1, (int)Math.Ceiling(desired * MIN_NOBLE_RATIO));
            FillNobleQuota(pKingdom, active, candidates, targetNobles, guardName);
            FillGuardSlots(pKingdom, active, candidates, desired, guardName);

            Actor captain = PickCaptain(active);
            if (captain == null)
            {
                DismissKingdomGuards(pKingdom, "no_noble_captain");
                return;
            }

            EnsureFormationRecorded(pKingdom, guardName);
            Army guardArmy = EnsureGuardArmy(pKingdom, captain, guardName);
            foreach (Actor guard in new List<Actor>(active))
                RefreshGuardIdentity(guard, pKingdom, guardName, guard == captain, guardArmy);
        }

        public static void OnKingChanged(Kingdom pKingdom, Actor pNewKing)
        {
            if (pKingdom?.data == null) return;
            if (pNewKing == null)
            {
                DismissKingdomGuards(pKingdom, "no_king");
                return;
            }
            EnsureKingdomGuard(pKingdom, pForce: true);
        }

        public static void OnGuardDeath(Actor pActor)
        {
            if (!IsRoyalGuard(pActor)) return;
            DismissGuard(pActor, "died", pRecord: true, pKeepTrait: false);
        }

        public static void DismissGuard(Actor pActor, string pReason)
        {
            DismissGuard(pActor, pReason, pRecord: true, pKeepTrait: false);
        }

        public static void StripGuardsFromNormalArmy(Army pArmy)
        {
            if (pArmy == null) return;
            if (IsRoyalGuardArmy(pArmy)) return;

            foreach (Actor unit in new List<Actor>(pArmy.getUnits()))
            {
                if (!IsRoyalGuard(unit)) continue;
                RemoveFromNormalArmy(unit);
            }

            Actor captain = pArmy.getCaptain();
            if (IsRoyalGuard(captain))
            {
                Actor replacement = PickArmyCaptainReplacement(pArmy);
                if (replacement != null) pArmy.setCaptain(replacement);
            }
        }

        public static void StripActorFromNormalArmy(Actor pActor)
        {
            if (!IsRoyalGuard(pActor)) return;
            RemoveFromNormalArmy(pActor);
        }

        public static void PrepareArmyCaptain(ref Actor pActor, City pCity)
        {
            if (!IsRoyalGuard(pActor)) return;
            Actor replacement = PickArmyCaptainReplacement(pCity);
            if (replacement != null) pActor = replacement;
        }

        public static bool TryReplaceGuardCaptain(Army pArmy, ref Actor pActor)
        {
            if (_creatingGuardArmy || IsRoyalGuardArmy(pArmy)) return true;
            if (!IsRoyalGuard(pActor)) return true;
            Actor replacement = PickArmyCaptainReplacement(pArmy);
            if (replacement == null) replacement = PickArmyCaptainReplacement(pArmy?.getCity());
            if (replacement == null) return false;
            pActor = replacement;
            return true;
        }

        public static WorldTile GetFollowTile(Actor pGuard)
        {
            if (!IsRoyalGuard(pGuard)) return null;
            Actor king = pGuard.kingdom?.king;
            if (king?.current_tile == null || king.isRekt()) return null;

            RemoveFromNormalArmy(pGuard);
            WorldTile patrolTile = PickPatrolTileAroundKing(pGuard, king.current_tile);
            if (patrolTile == null) return king.current_tile;

            if (pGuard.current_tile != null &&
                Toolbox.SquaredDistTile(pGuard.current_tile, king.current_tile) <= PROTECT_RADIUS * PROTECT_RADIUS &&
                Toolbox.SquaredDistTile(pGuard.current_tile, patrolTile) <= 1)
                return pGuard.current_tile;

            return patrolTile;
        }

        public static Actor FindThreatNearKing(Actor pGuard)
        {
            if (!IsRoyalGuard(pGuard)) return null;
            Kingdom kingdom = pGuard.kingdom;
            Actor king = kingdom?.king;
            if (king?.current_tile == null || king.isRekt()) return null;

            Actor best = null;
            int bestDist = int.MaxValue;
            using ListPool<Kingdom> enemies = kingdom.getEnemiesKingdoms();
            foreach (Kingdom enemy in enemies)
            {
                if (enemy?.data == null) continue;
                foreach (Actor unit in enemy.getUnits())
                {
                    if (!IsValidThreatForGuard(pGuard, unit)) continue;
                    int dist = Toolbox.SquaredDistTile(king.current_tile, unit.current_tile);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = unit;
                    }
                }
            }
            return best;
        }

        public static bool IsValidThreatForGuard(Actor pGuard, Actor pTarget)
        {
            if (!IsRoyalGuard(pGuard)) return false;
            if (pGuard?.kingdom == null || pGuard.current_tile == null) return false;
            if (pTarget?.data == null || pTarget.current_tile == null) return false;
            if (pTarget.isRekt() || pTarget.kingdom == null) return false;
            if (!pGuard.kingdom.isEnemy(pTarget.kingdom)) return false;

            Actor king = pGuard.kingdom.king;
            if (king?.current_tile == null || king.isRekt()) return false;
            if (!king.current_tile.isSameIsland(pTarget.current_tile)) return false;

            int distToKing = Toolbox.SquaredDistTile(king.current_tile, pTarget.current_tile);
            if (distToKing <= PROTECT_RADIUS * PROTECT_RADIUS) return true;

            int distToGuard = Toolbox.SquaredDistTile(pGuard.current_tile, pTarget.current_tile);
            if (distToGuard <= FOLLOW_RADIUS * FOLLOW_RADIUS) return true;

            return IsAttackerOf(pTarget, king) || IsAttackerOf(pTarget, pGuard);
        }

        private static List<Actor> CollectActiveGuards(Kingdom pKingdom)
        {
            var active = new List<Actor>();
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (!IsRoyalGuard(unit)) continue;
                if (!IsStillValidGuard(unit, pKingdom))
                {
                    DismissGuard(unit, "invalid");
                    continue;
                }
                active.Add(unit);
                RemoveFromNormalArmy(unit);
            }
            return active;
        }

        private static bool IsStillValidGuard(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor.kingdom != pKingdom) return false;
            pActor.data.get(LineageKeys.ROYAL_GUARD_KINGDOM_ID, out long guardKingdomId, -1L);
            if (guardKingdomId >= 0 && guardKingdomId != pKingdom.id) return false;
            if (pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            return true;
        }

        private static WorldTile PickPatrolTileAroundKing(Actor pGuard, WorldTile pKingTile)
        {
            if (pGuard?.data == null || pKingTile == null) return null;

            int width = PATROL_MAX_RADIUS * 2 + 1;
            int total = width * width;
            int phase = (int)(LineageService.CurTime() / PATROL_PHASE_INTERVAL);
            int seed = unchecked((int)(pGuard.data.id * 31L + phase * 17L)) & int.MaxValue;

            for (int i = 0; i < total; i++)
            {
                int idx = (seed + i) % total;
                int dx = idx % width - PATROL_MAX_RADIUS;
                int dy = idx / width - PATROL_MAX_RADIUS;
                int distSq = dx * dx + dy * dy;
                if (distSq < PATROL_MIN_RADIUS * PATROL_MIN_RADIUS ||
                    distSq > PATROL_MAX_RADIUS * PATROL_MAX_RADIUS)
                    continue;

                WorldTile tile = World.world.GetTile(pKingTile.x + dx, pKingTile.y + dy);
                if (IsGoodGuardPatrolTile(tile, pKingTile)) return tile;
            }

            return pKingTile.getNeighbourTileSameIsland();
        }

        private static bool IsGoodGuardPatrolTile(WorldTile pTile, WorldTile pKingTile)
        {
            if (pTile == null || pKingTile == null) return false;
            if (!pTile.isSameIsland(pKingTile)) return false;
            if (pTile.Type == null) return false;
            if (!pTile.Type.ground || pTile.Type.liquid || pTile.Type.lava || pTile.Type.block) return false;
            return true;
        }

        private static List<GuardCandidate> CollectCandidates(Kingdom pKingdom, List<Actor> pActive, float pAverageWarriorScore)
        {
            var result = new List<GuardCandidate>();
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (pActive.Contains(unit)) continue;
                if (!IsGuardCandidate(unit, pKingdom)) continue;
                float score = CombatScore(unit);
                if (pAverageWarriorScore > 0f && score <= pAverageWarriorScore) continue;
                result.Add(new GuardCandidate
                {
                    actor = unit,
                    score = score,
                    noble = ChronicleGate.IsNobleActor(unit)
                });
            }
            result.Sort((a, b) => b.score.CompareTo(a.score));
            return result;
        }

        private static bool IsGuardCandidate(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor.kingdom != pKingdom) return false;
            if (!LineageService.IsXia(pActor)) return false;
            if (pActor.asset.is_boat) return false;
            if (pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (pActor.hasTrait("madness")) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            return true;
        }

        private static void FillNobleQuota(Kingdom pKingdom, List<Actor> pActive,
            List<GuardCandidate> pCandidates, int pTargetNobles, string pGuardName)
        {
            while (CountNobles(pActive) < pTargetNobles)
            {
                GuardCandidate next = TakeBestCandidate(pCandidates, pNobleOnly: true);
                if (next == null) return;
                AppointGuard(next.actor, pKingdom, pGuardName, pCaptain: false);
                pActive.Add(next.actor);
            }
        }

        private static void FillGuardSlots(Kingdom pKingdom, List<Actor> pActive,
            List<GuardCandidate> pCandidates, int pDesired, string pGuardName)
        {
            while (pActive.Count < pDesired)
            {
                GuardCandidate next = TakeBestCandidate(pCandidates, pNobleOnly: false);
                if (next == null) return;
                AppointGuard(next.actor, pKingdom, pGuardName, pCaptain: false);
                pActive.Add(next.actor);
            }
        }

        private static GuardCandidate TakeBestCandidate(List<GuardCandidate> pCandidates, bool pNobleOnly)
        {
            for (int i = 0; i < pCandidates.Count; i++)
            {
                GuardCandidate candidate = pCandidates[i];
                if (pNobleOnly && !candidate.noble) continue;
                pCandidates.RemoveAt(i);
                return candidate;
            }
            return null;
        }

        private static void TrimExcessGuards(List<Actor> pActive, int pDesired)
        {
            if (pActive.Count <= pDesired && pActive.Count <= MAX_GUARDS_PER_KINGDOM) return;
            pActive.Sort((a, b) => CombatScore(b).CompareTo(CombatScore(a)));
            int limit = Math.Min(pDesired, MAX_GUARDS_PER_KINGDOM);
            for (int i = pActive.Count - 1; i >= limit; i--)
            {
                Actor guard = pActive[i];
                pActive.RemoveAt(i);
                DismissGuard(guard, "over_limit");
            }
        }

        private static Actor PickCaptain(List<Actor> pActive)
        {
            Actor best = null;
            float bestScore = float.MinValue;
            foreach (Actor guard in pActive)
            {
                if (!ChronicleGate.IsNobleActor(guard)) continue;
                float score = CombatScore(guard);
                if (score <= bestScore) continue;
                bestScore = score;
                best = guard;
            }
            return best;
        }

        private static void AppointGuard(Actor pActor, Kingdom pKingdom, string pGuardName, bool pCaptain)
        {
            RefreshGuardIdentity(pActor, pKingdom, pGuardName, pCaptain, null);
        }

        private static void RefreshGuardIdentity(Actor pActor, Kingdom pKingdom, string pGuardName, bool pCaptain, Army pGuardArmy)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;

            bool wasGuard = IsRoyalGuard(pActor);
            pActor.data.get(LineageKeys.ROYAL_GUARD_CAPTAIN, out bool wasCaptain, false);

            pActor.data.set(LineageKeys.ROYAL_GUARD, true);
            pActor.data.set(LineageKeys.ROYAL_GUARD_CAPTAIN, pCaptain);
            pActor.data.set(LineageKeys.ROYAL_GUARD_KINGDOM_ID, pKingdom.id);
            pActor.data.set(LineageKeys.ROYAL_GUARD_NAME, pGuardName ?? "");

            if (!pActor.hasTrait(LineageKeys.TRAIT_GUARD))
                pActor.addTrait(LineageKeys.TRAIT_GUARD);

            AssignToGuardArmy(pActor, pGuardArmy);
            if (!pActor.isWarrior())
            {
                try { pActor.setProfession(UnitProfession.Warrior); } catch { }
            }
            if (GuardContent.KingGuardJob != null)
                pActor.setCitizenJob(GuardContent.KingGuardJob);

            UpsertGuardState(pActor, pActive: true, pCaptain, ChronicleGate.IsNobleActor(pActor), pGuardName, "");
            LineageService.ArchiveActor(pActor, pAlive: true);
            pActor.clearGraphicsFully();

            if (!wasGuard || wasCaptain != pCaptain)
                ChronicleEvents.OnRoyalGuardAppointed(pActor, pKingdom, pActor.city, pGuardName, pCaptain);
        }

        private static void DismissKingdomGuards(Kingdom pKingdom, string pReason)
        {
            if (pKingdom?.data == null) return;
            foreach (Actor unit in new List<Actor>(pKingdom.getUnits()))
            {
                if (IsRoyalGuard(unit))
                    DismissGuard(unit, pReason);
            }
        }

        private static void DismissGuard(Actor pActor, string pReason, bool pRecord, bool pKeepTrait)
        {
            if (pActor?.data == null || !IsRoyalGuard(pActor)) return;

            Kingdom kingdom = pActor.kingdom;
            City city = pActor.city;
            pActor.data.get(LineageKeys.ROYAL_GUARD_NAME, out string guardName, BuildGuardName(kingdom));
            pActor.data.get(LineageKeys.ROYAL_GUARD_CAPTAIN, out bool wasCaptain, false);

            pActor.data.set(LineageKeys.ROYAL_GUARD, false);
            pActor.data.set(LineageKeys.ROYAL_GUARD_CAPTAIN, false);
            pActor.data.set(LineageKeys.ROYAL_GUARD_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.ROYAL_GUARD_NAME, "");
            if (!pKeepTrait && pActor.hasTrait(LineageKeys.TRAIT_GUARD))
                pActor.removeTrait(LineageKeys.TRAIT_GUARD);
            ClearGuardCitizenJob(pActor);
            RemoveFromGuardArmy(pActor);

            UpsertGuardState(pActor, pActive: false, wasCaptain, ChronicleGate.IsNobleActor(pActor), guardName, pReason);
            if (pRecord)
                ChronicleEvents.OnRoyalGuardDismissed(pActor, kingdom, city, pReason);

            if (!pActor.isRekt())
            {
                pActor.clearGraphicsFully();
                LineageService.ArchiveActor(pActor, pAlive: true);
            }
        }

        private static void ClearStaleGuardIdentity(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.ROYAL_GUARD, false);
            pActor.data.set(LineageKeys.ROYAL_GUARD_CAPTAIN, false);
            pActor.data.set(LineageKeys.ROYAL_GUARD_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.ROYAL_GUARD_NAME, "");
            if (pActor.hasTrait(LineageKeys.TRAIT_GUARD))
                pActor.removeTrait(LineageKeys.TRAIT_GUARD);
            ClearGuardCitizenJob(pActor);
            if (!pActor.isRekt())
                pActor.clearGraphicsFully();
        }

        private static void ClearGuardCitizenJob(Actor pActor)
        {
            if (!IsKingGuardJob(pActor)) return;
            try { pActor.endJob(); }
            catch { pActor.citizen_job = null; }
        }

        private static bool IsKingGuardJob(Actor pActor)
        {
            return pActor?.citizen_job != null &&
                   pActor.citizen_job.id == GuardContent.CITIZEN_JOB_KING_GUARD;
        }

        private static void RemoveFromNormalArmy(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (!pActor.hasArmy()) return;
            if (IsRoyalGuardArmy(pActor.army)) return;
            try { pActor.removeFromArmy(); }
            catch { pActor.setArmy(null); }
        }

        private static void AssignToGuardArmy(Actor pActor, Army pGuardArmy)
        {
            if (pActor?.data == null) return;
            if (pGuardArmy == null)
            {
                RemoveFromNormalArmy(pActor);
                return;
            }

            if (pActor.army == pGuardArmy) return;
            if (pActor.hasArmy())
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
            }
            pActor.setArmy(pGuardArmy);
        }

        private static void RemoveFromGuardArmy(Actor pActor)
        {
            if (pActor?.data == null || !pActor.hasArmy()) return;
            Army army = pActor.army;
            if (!IsRoyalGuardArmy(army)) return;
            try { pActor.removeFromArmy(); }
            catch { pActor.setArmy(null); }
            TryRemoveEmptyGuardArmy(army);
        }

        private static Army EnsureGuardArmy(Kingdom pKingdom, Actor pCaptain, string pGuardName)
        {
            if (pKingdom?.data == null || pCaptain?.data == null) return null;

            Army army = FindGuardArmy(pKingdom);
            if (army == null)
                army = CreateDetachedGuardArmy(pKingdom, pCaptain, pGuardName);
            if (army == null) return null;

            if (army.hasCity())
                army.clearCity();
            if (army.data != null)
            {
                army.data.custom_name = true;
                if (army.data.name != pGuardName)
                    army.setName(pGuardName);
            }
            if (!pCaptain.isRekt())
                army.setCaptain(pCaptain);

            pKingdom.data.set(LineageKeys.ROYAL_GUARD_ARMY_ID, army.id);
            return army;
        }

        private static Army FindGuardArmy(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || World.world?.armies == null) return null;

            pKingdom.data.get(LineageKeys.ROYAL_GUARD_ARMY_ID, out long armyId, -1L);
            Army stored = World.world.armies.get(armyId);
            if (IsRoyalGuardArmyForKingdom(stored, pKingdom)) return stored;

            foreach (Army army in World.world.armies)
                if (IsRoyalGuardArmyForKingdom(army, pKingdom))
                    return army;

            return null;
        }

        private static bool IsRoyalGuardArmyForKingdom(Army pArmy, Kingdom pKingdom)
        {
            if (!IsRoyalGuardArmy(pArmy)) return false;
            try { return pArmy.getKingdom() == pKingdom; }
            catch { return false; }
        }

        private static bool IsRoyalGuardArmy(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            if (pArmy.hasCity()) return false;
            string name = pArmy.data.name ?? "";
            if (name.Contains("\u7981\u536B\u519B")) return true;
            Actor captain = pArmy.getCaptain();
            return IsRoyalGuard(captain);
        }

        private static Army CreateDetachedGuardArmy(Kingdom pKingdom, Actor pCaptain, string pGuardName)
        {
            if (NewArmyObjectMethod == null || World.world?.armies == null) return null;
            City city = pCaptain?.city ?? pKingdom?.capital;
            if (city?.data == null) return null;

            try
            {
                var army = NewArmyObjectMethod.Invoke(World.world.armies, null) as Army;
                if (army == null) return null;

                _creatingGuardArmy = true;
                try { army.createArmy(pCaptain, city); }
                finally { _creatingGuardArmy = false; }

                army.clearCity();
                army.data.custom_name = true;
                army.setName(pGuardName);
                return army;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Create royal guard army failed: " + e.Message);
                _creatingGuardArmy = false;
                return null;
            }
        }

        private static void TryRemoveEmptyGuardArmy(Army pArmy)
        {
            if (!IsRoyalGuardArmy(pArmy)) return;
            if (pArmy.countUnits() > 0 || pArmy.hasCaptain()) return;
            try { World.world?.armies?.removeObject(pArmy); }
            catch { }
        }

        private static MethodInfo ResolveNewArmyObjectMethod()
        {
            Type type = typeof(ArmyManager);
            while (type != null)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (method.Name != "newObject") continue;
                    if (method.GetParameters().Length == 0) return method;
                }
                type = type.BaseType;
            }
            return null;
        }

        private static Actor PickArmyCaptainReplacement(Army pArmy)
        {
            if (pArmy == null) return null;
            foreach (Actor unit in pArmy.getUnits())
            {
                if (CanBeNormalArmyCaptain(unit)) return unit;
            }
            return null;
        }

        private static Actor PickArmyCaptainReplacement(City pCity)
        {
            if (pCity?.data == null) return null;
            foreach (Actor unit in pCity.getUnits())
            {
                if (CanBeNormalArmyCaptain(unit)) return unit;
            }
            return null;
        }

        private static bool CanBeNormalArmyCaptain(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            if (!pActor.isWarrior()) return false;
            if (IsRoyalGuard(pActor)) return false;
            if (SlaveService.IsSlave(pActor)) return false;
            return true;
        }

        private static void EnsureFormationRecorded(Kingdom pKingdom, string pGuardName)
        {
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_RECORDED, out bool recorded, false);
            if (recorded) return;
            pKingdom.data.set(LineageKeys.ROYAL_GUARD_RECORDED, true);
            ChronicleEvents.OnRoyalGuardFormed(pKingdom, pGuardName);
        }

        private static string BuildGuardName(Kingdom pKingdom)
        {
            string kingdomName = pKingdom?.name ?? "";
            return kingdomName + " " + "\u7981\u536B\u519B";
        }

        private static int CountNobles(List<Actor> pActors)
        {
            int count = 0;
            foreach (Actor actor in pActors)
                if (ChronicleGate.IsNobleActor(actor)) count++;
            return count;
        }

        private static int CountNobleCandidates(List<GuardCandidate> pCandidates)
        {
            int count = 0;
            foreach (GuardCandidate candidate in pCandidates)
                if (candidate.noble) count++;
            return count;
        }

        private static float AverageWarriorScore(Kingdom pKingdom)
        {
            float total = 0f;
            int count = 0;
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (unit?.data == null || unit.isRekt()) continue;
                if (!unit.isWarrior()) continue;
                if (IsRoyalGuard(unit) || SlaveService.IsSlave(unit)) continue;
                total += CombatScore(unit);
                count++;
            }
            return count == 0 ? 0f : total / count;
        }

        private static float CombatScore(Actor pActor)
        {
            if (pActor?.stats == null) return 0f;
            return SafeStat(pActor, "damage")
                   + SafeStat(pActor, "warfare") * 2f
                   + SafeStat(pActor, "health") * 0.1f
                   + SafeStat(pActor, "armor") * 2f
                   + SafeStat(pActor, "speed") * 0.25f;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor.stats[pKey]; }
            catch { return 0f; }
        }

        private static bool IsAttackerOf(Actor pAttacker, Actor pVictim)
        {
            try
            {
                BaseSimObject source = pVictim?.attackedBy;
                return source != null && source.isActor() && source.a == pAttacker;
            }
            catch
            {
                return false;
            }
        }

        private static void UpsertGuardState(Actor pActor, bool pActive, bool pCaptain,
            bool pNoble, string pGuardName, string pDismissReason)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || pActor?.data == null) return;

            string table = RoyalGuardStateTableItem.GetTableName();
            Kingdom kingdom = pActor.kingdom;
            City city = pActor.city;
            var values = new[]
            {
                ColumnVal.Create("ACTOR_NAME", pActor.getName() ?? ""),
                ColumnVal.Create("KINGDOM_ID", kingdom?.id ?? -1L),
                ColumnVal.Create("KINGDOM_NAME", kingdom?.name ?? ""),
                ColumnVal.Create("CITY_ID", city?.id ?? -1L),
                ColumnVal.Create("CITY_NAME", city?.data?.name ?? ""),
                ColumnVal.Create("GUARD_NAME", pGuardName ?? ""),
                ColumnVal.Create("ACTIVE", pActive ? 1 : 0),
                ColumnVal.Create("CAPTAIN", pCaptain ? 1 : 0),
                ColumnVal.Create("NOBLE", pNoble ? 1 : 0),
                ColumnVal.Create("APPOINTED_TIME", LineageService.CurTime()),
                ColumnVal.Create("DISMISSED_TIME", pActive ? -1.0 : LineageService.CurTime()),
                ColumnVal.Create("DISMISS_REASON", pDismissReason ?? "")
            };

            try
            {
                if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("ACTOR_ID", pActor.data.id)))
                {
                    db.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pActor.data.id) },
                        values);
                    return;
                }

                var insertValues = new List<ColumnVal> { ColumnVal.Create("ACTOR_ID", pActor.data.id) };
                insertValues.AddRange(values);
                db.Insert(table, insertValues.ToArray());
            }
            catch (Exception e)
            {
                ModClass.LogWarning("RoyalGuardState upsert failed: " + e.Message);
            }
        }
    }
}
