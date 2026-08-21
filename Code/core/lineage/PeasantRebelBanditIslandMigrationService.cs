using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditIslandMigrationService
    {
        internal static void OnKingdomYear(Kingdom pBandit)
        {
            if (!CanMutate() || pBandit?.data == null || pBandit.isRekt() ||
                !PeasantRebelBanditStateStore.TryResolveOperational(pBandit,
                    out PeasantRebelBanditStrongholdState state)) return;
            if (state.StrongholdKind == BanditStrongholdKind.Island ||
                IsMigrationActive(state.Migration)) return;

            City oldStronghold = ResolveCity(state.StrongholdCityId);
            if (oldStronghold?.data == null || oldStronghold.isRekt()) return;
            Kingdom origin = ResolveKingdom(state.OriginKingdomId);
            bool suppressionWar = HasWarAgainst(pBandit, origin);
            int banditStrength = PeasantRebelRouteService.RealmStrength(pBandit);
            int hostileStrength = ResolveHostileStrength(pBandit, origin);
            int population = SafePopulation(oldStronghold);
            int threatCycles = PeasantRebelBanditIslandRules.NextThreatCycles(
                suppressionWar, state.Migration.ThreatCycles);
            state.Migration.ThreatCycles = threatCycles;
            PeasantRebelBanditStateStore.Write(pBandit, state);
            if (!PeasantRebelBanditIslandRules.ShouldStartEvacuation(
                    suppressionWar, suppressionWar, banditStrength,
                    hostileStrength, population, threatCycles)) return;
            if (!PeasantRebelBanditIslandCandidateService.TrySelect(
                    oldStronghold, ResolveSuppressor(pBandit, origin),
                    out PeasantRebelBanditIslandCandidate candidate)) return;
            TryStart(pBandit, state, oldStronghold, candidate);
        }

        internal static bool IsActive(Kingdom pBandit)
        {
            return pBandit?.data != null &&
                PeasantRebelBanditStateStore.TryRead(pBandit,
                    out PeasantRebelBanditStrongholdState state) &&
                IsMigrationActive(state.Migration);
        }

        internal static void ClearRuntime()
        {
            IslandEscapeService.Clear();
        }

        internal static void RestoreRuntime()
        {
            if (!CanMutate() || World.world?.kingdoms == null) return;
            foreach (Kingdom bandit in World.world.kingdoms.ToList())
            {
                if (bandit?.data == null || bandit.isRekt() ||
                    !PeasantRebelBanditStateStore.TryResolveOperational(bandit,
                        out PeasantRebelBanditStrongholdState state) ||
                    !IsMigrationActive(state.Migration)) continue;
                City oldCity = ResolveCity(state.Migration.OldStrongholdCityId);
                WorldTile landing = ResolveTile(
                    state.Migration.TargetLandingTileId);
                List<Actor> members = ResolveMembers(state.Migration.MemberActorIds);
                Actor leader = ResolveActor(state.Migration.LeaderActorId) ??
                    members.FirstOrDefault();
                if (oldCity?.data == null || landing?.data == null ||
                    leader?.data == null || members.Count == 0) continue;
                IslandEscapeService.TryBegin(new IslandEscapeGroupSpec
                {
                    GroupKey = "bandit:island:" + bandit.getID(),
                    OriginCity = oldCity,
                    EntryTile = oldCity.getTile(),
                    LandingTile = landing,
                    Members = members,
                    Leader = leader,
                    OnStageChanged = next => PersistStage(bandit, state,
                        next.Stage),
                    OnFounded = (next, survivors) => FinishMigration(bandit,
                        state, oldCity,
                        new PeasantRebelBanditIslandCandidate
                        {
                            LandingTile = landing,
                            FoundingTile = landing,
                            Island = landing.region?.island
                        }, survivors),
                    OnFailed = (next, reason) => FailMigration(bandit, state,
                        reason)
                }, out _);
            }
        }

        private static bool TryStart(Kingdom pBandit,
            PeasantRebelBanditStrongholdState pState, City pOldStronghold,
            PeasantRebelBanditIslandCandidate pCandidate)
        {
            if (pCandidate?.LandingTile?.data == null ||
                pOldStronghold?.data == null) return false;
            List<Actor> members = CollectResidents(pOldStronghold);
            Actor leader = ResolveLeader(pBandit, members);
            if (leader?.data == null) return false;
            if (!IslandEscapeService.TryBegin(new IslandEscapeGroupSpec
            {
                GroupKey = "bandit:island:" + pBandit.getID(),
                OriginCity = pOldStronghold,
                EntryTile = pOldStronghold.getTile(),
                LandingTile = pCandidate.LandingTile,
                Members = members,
                Leader = leader,
                OnStageChanged = next => PersistStage(pBandit, pState,
                    next.Stage),
                OnFounded = (next, survivors) => FinishMigration(pBandit,
                    pState, pOldStronghold, pCandidate, survivors),
                OnFailed = (next, reason) => FailMigration(pBandit, pState,
                    reason)
            }, out IslandEscapeGroupState group)) return false;

            pState.Migration.Stage = BanditMigrationStage.Evaluating;
            pState.Migration.OldStrongholdCityId = pOldStronghold.getID();
            pState.Migration.TargetIslandId = pCandidate.Island?.id ?? -1L;
            pState.Migration.TargetLandingTileId =
                pCandidate.LandingTile.data.tile_id;
            pState.Migration.StartedYear = Date.getCurrentYear();
            pState.Migration.MemberActorIds = group.MemberActorIds.ToList();
            pState.Migration.LeaderActorId = group.LeaderActorId;
            return PeasantRebelBanditStateStore.Write(pBandit, pState);
        }

        private static void PersistStage(Kingdom pBandit,
            PeasantRebelBanditStrongholdState pState,
            IslandEscapeStage pStage)
        {
            if (pState?.Migration == null) return;
            pState.Migration.Stage = ToBanditStage(pStage);
            PeasantRebelBanditStateStore.Write(pBandit, pState);
        }

        private static void FinishMigration(Kingdom pBandit,
            PeasantRebelBanditStrongholdState pState, City pOldStronghold,
            PeasantRebelBanditIslandCandidate pCandidate,
            IReadOnlyList<Actor> pSurvivors)
        {
            try
            {
                TileZone zone = pCandidate?.FoundingTile?.zone;
                Actor leader = ResolveActor(pState.Migration.LeaderActorId);
                if (leader == null)
                    leader = pSurvivors?.FirstOrDefault();
                if (zone == null || leader?.data == null)
                {
                    FailMigration(pBandit, pState, "founding_tile_invalid");
                    return;
                }
                City islandCity = World.world.cities.newCity(pBandit, zone,
                    leader);
                if (islandCity?.data == null)
                {
                    FailMigration(pBandit, pState, "city_creation_failed");
                    return;
                }
                foreach (Actor actor in pSurvivors ??
                         Array.Empty<Actor>())
                {
                    if (actor?.data == null || actor.isRekt() ||
                        !actor.isAlive()) continue;
                    actor.joinCity(islandCity);
                    actor.spawnOn(islandCity.getTile());
                }
                pBandit.setCityMetas(islandCity);
                if (pOldStronghold?.data != null &&
                    pOldStronghold.kingdom == pBandit)
                {
                    Kingdom origin = ResolveKingdom(pState.OriginKingdomId);
                    if (origin?.data != null && !origin.isRekt())
                        pOldStronghold.joinAnotherKingdom(origin,
                            pCaptured: false);
                }
                pState.StrongholdKind = BanditStrongholdKind.Island;
                pState.StrongholdCityId = islandCity.getID();
                pState.Migration.Stage = BanditMigrationStage.Completed;
                PeasantRebelBanditStateStore.Write(pBandit, pState);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Bandit island founding failed: " +
                                    error.Message);
                FailMigration(pBandit, pState, "founding_exception");
            }
        }

        private static void FailMigration(Kingdom pBandit,
            PeasantRebelBanditStrongholdState pState, string pReason)
        {
            if (pState?.Migration == null) return;
            pState.Migration.Stage = BanditMigrationStage.Failed;
            pState.Migration.FailureCount++;
            PeasantRebelBanditStateStore.Write(pBandit, pState);
            ModClass.LogWarning("Bandit island migration failed: " +
                                pReason);
        }

        private static List<Actor> CollectResidents(City pCity)
        {
            var result = new Dictionary<long, Actor>();
            try
            {
                foreach (Actor actor in pCity.units.ToList())
                    if (actor?.data != null && actor.isAlive() &&
                        !actor.isRekt() && !actor.is_inside_boat)
                        result[actor.getID()] = actor;
            }
            catch { }
            return result.Values.OrderBy(actor => actor.getID()).ToList();
        }

        private static Actor ResolveLeader(Kingdom pBandit,
            List<Actor> pMembers)
        {
            Actor king = pBandit?.king;
            if (king?.data != null && pMembers.Any(actor => actor == king))
                return king;
            return pMembers?.OrderByDescending(actor => actor.isWarrior())
                .ThenBy(actor => actor.getID()).FirstOrDefault();
        }

        private static bool IsMigrationActive(BanditIslandMigrationState pState)
        {
            return pState != null && pState.Stage != BanditMigrationStage.None &&
                pState.Stage != BanditMigrationStage.Completed &&
                pState.Stage != BanditMigrationStage.Failed;
        }

        private static BanditMigrationStage ToBanditStage(
            IslandEscapeStage pStage)
        {
            switch (pStage)
            {
                case IslandEscapeStage.Evaluating:
                    return BanditMigrationStage.Evaluating;
                case IslandEscapeStage.Gathering:
                case IslandEscapeStage.Boarding:
                    return BanditMigrationStage.Boarding;
                case IslandEscapeStage.Voyaging:
                case IslandEscapeStage.Landing:
                    return BanditMigrationStage.Voyaging;
                case IslandEscapeStage.Founding:
                    return BanditMigrationStage.Founding;
                case IslandEscapeStage.Completed:
                    return BanditMigrationStage.Completed;
                case IslandEscapeStage.Failed:
                    return BanditMigrationStage.Failed;
                default:
                    return BanditMigrationStage.None;
            }
        }

        private static bool HasWarAgainst(Kingdom pBandit, Kingdom pOrigin)
        {
            if (pBandit?.data == null || pOrigin?.data == null) return false;
            try
            {
                foreach (War war in pBandit.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    if (war.getMainAttacker() == pOrigin ||
                        war.getMainDefender() == pOrigin) return true;
                }
            }
            catch { }
            return false;
        }

        private static Kingdom ResolveSuppressor(Kingdom pBandit,
            Kingdom pOrigin)
        {
            try
            {
                foreach (War war in pBandit.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    Kingdom attacker = war.getMainAttacker();
                    Kingdom defender = war.getMainDefender();
                    if (attacker != pOrigin && attacker != pBandit)
                        return attacker;
                    if (defender != pOrigin && defender != pBandit)
                        return defender;
                }
            }
            catch { }
            return pOrigin;
        }

        private static int ResolveHostileStrength(Kingdom pBandit,
            Kingdom pOrigin)
        {
            Kingdom hostile = ResolveSuppressor(pBandit, pOrigin);
            return hostile?.data == null ? 0 :
                PeasantRebelRouteService.RealmStrength(hostile);
        }

        private static City ResolveCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static List<Actor> ResolveMembers(IEnumerable<long> pIds)
        {
            var result = new List<Actor>();
            foreach (long actorId in pIds ?? Enumerable.Empty<long>())
            {
                Actor actor = ResolveActor(actorId);
                if (actor?.data != null && actor.isAlive() &&
                    !actor.isRekt()) result.Add(actor);
            }
            return result;
        }

        private static WorldTile ResolveTile(int pTileId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                ? tiles[pTileId]
                : null;
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
