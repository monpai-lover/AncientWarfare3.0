using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.content;
using life.taxi;

namespace AncientWarfare3.core.lineage
{
    internal sealed class IslandEscapeGroupSpec
    {
        internal string GroupKey;
        internal City OriginCity;
        internal WorldTile EntryTile;
        internal WorldTile LandingTile;
        internal IEnumerable<Actor> Members;
        internal Actor Leader;
        internal Action<IslandEscapeGroupState, IReadOnlyList<Actor>>
            OnFounded;
        internal Action<IslandEscapeGroupState, string> OnFailed;
        internal Action<IslandEscapeGroupState> OnStageChanged;
    }

    internal sealed class IslandEscapeGroupState
    {
        internal string GroupKey;
        internal long OriginCityId = -1L;
        internal int EntryTileId = -1;
        internal int LandingTileId = -1;
        internal long LeaderActorId = -1L;
        internal List<long> MemberActorIds = new List<long>();
        internal IslandEscapeStage Stage = IslandEscapeStage.None;
        internal long TransportRequestId = -1L;
        internal long TransportBoatId = -1L;
        internal int FailureCount;
    }

    internal static class IslandEscapeService
    {
        private sealed class Registration
        {
            internal IslandEscapeGroupState State;
            internal WorldTile EntryTile;
            internal WorldTile LandingTile;
            internal Action<IslandEscapeGroupState, IReadOnlyList<Actor>>
                OnFounded;
            internal Action<IslandEscapeGroupState, string> OnFailed;
            internal Action<IslandEscapeGroupState> OnStageChanged;
        }

        private static readonly Dictionary<string, Registration> Groups =
            new Dictionary<string, Registration>(StringComparer.Ordinal);
        private static readonly Dictionary<long, string> ActorGroups =
            new Dictionary<long, string>();

        internal static bool TryBegin(IslandEscapeGroupSpec pSpec,
            out IslandEscapeGroupState pState)
        {
            pState = null;
            if (pSpec == null || string.IsNullOrWhiteSpace(pSpec.GroupKey) ||
                pSpec.EntryTile?.data == null ||
                pSpec.LandingTile?.data == null) return false;

            List<Actor> members = (pSpec.Members ??
                    Enumerable.Empty<Actor>())
                .Where(IsInitialMember)
                .GroupBy(pActor => pActor.getID())
                .Select(pGroup => pGroup.First())
                .OrderBy(pActor => pActor.getID())
                .ToList();
            List<IslandEscapeMemberFact> facts = members.Select(pActor =>
                new IslandEscapeMemberFact(pActor.getID(),
                    pActor.isAlive(),
                    pSpec.OriginCity == null || pActor.city == pSpec.OriginCity ||
                    pActor.current_tile?.zone?.city == pSpec.OriginCity,
                    pActor.is_inside_boat)).ToList();
            IReadOnlyList<long> manifest =
                IslandEscapeBehaviourRules.BuildManifest(facts);
            if (manifest.Count == 0) return false;

            if (Groups.TryGetValue(pSpec.GroupKey, out Registration existing) &&
                existing.State.Stage != IslandEscapeStage.Completed &&
                existing.State.Stage != IslandEscapeStage.Failed)
            {
                pState = existing.State;
                return true;
            }

            var state = new IslandEscapeGroupState
            {
                GroupKey = pSpec.GroupKey,
                OriginCityId = pSpec.OriginCity?.getID() ?? -1L,
                EntryTileId = pSpec.EntryTile.data.tile_id,
                LandingTileId = pSpec.LandingTile.data.tile_id,
                LeaderActorId = pSpec.Leader?.getID() ?? manifest[0],
                MemberActorIds = manifest.ToList(),
                Stage = IslandEscapeStage.Evaluating
            };
            var registration = new Registration
            {
                State = state,
                EntryTile = pSpec.EntryTile,
                LandingTile = pSpec.LandingTile,
                OnFounded = pSpec.OnFounded,
                OnFailed = pSpec.OnFailed,
                OnStageChanged = pSpec.OnStageChanged
            };
            Groups[pSpec.GroupKey] = registration;
            foreach (long actorId in state.MemberActorIds)
            {
                ActorGroups[actorId] = state.GroupKey;
                Actor actor = ResolveActor(actorId);
                try { actor?.ai?.setJob(IslandEscapeContent.JobId); }
                catch (Exception e) { ModClass.LogWarning(
                    "Island escape task assignment failed: " + e.Message); }
            }
            pState = state;
            return true;
        }

        internal static bool TryGetForActor(Actor pActor,
            out IslandEscapeGroupState pState)
        {
            pState = null;
            if (pActor?.data == null ||
                !ActorGroups.TryGetValue(pActor.getID(), out string key) ||
                !Groups.TryGetValue(key, out Registration registration))
                return false;
            pState = registration.State;
            return pState != null && pState.Stage != IslandEscapeStage.Completed &&
                   pState.Stage != IslandEscapeStage.Failed;
        }

        internal static bool TryExecute(Actor pActor, out WorldTile pMoveTarget)
        {
            pMoveTarget = null;
            if (!TryGetRegistration(pActor, out Registration registration))
                return false;
            IslandEscapeGroupState state = registration.State;
            PruneMembers(state);
            if (state.MemberActorIds.Count == 0)
            {
                Fail(registration, "manifest_empty");
                return false;
            }

            if (state.Stage == IslandEscapeStage.Evaluating)
                SetStage(registration, IslandEscapeStage.Gathering);

            if (state.Stage == IslandEscapeStage.Gathering)
            {
                SetStage(registration, IslandEscapeStage.Boarding);
                return ExecuteBoarding(pActor, registration, out pMoveTarget);
            }
            if (state.Stage == IslandEscapeStage.Boarding)
                return ExecuteBoarding(pActor, registration, out pMoveTarget);
            if (state.Stage == IslandEscapeStage.Voyaging ||
                state.Stage == IslandEscapeStage.Landing)
                return ExecuteVoyage(pActor, registration);
            if (state.Stage == IslandEscapeStage.Founding)
            {
                Complete(registration);
                return false;
            }
            return false;
        }

        internal static void HandleArrival(Actor pActor)
        {
            if (!TryGetRegistration(pActor, out Registration registration))
                return;
            IslandEscapeGroupState state = registration.State;
            if (state.Stage == IslandEscapeStage.Boarding &&
                AllMembersHaveRequests(state))
            {
                SetStage(registration, IslandEscapeStage.Voyaging);
                state.TransportRequestId = ResolveCommonRequestId(state);
            }
            else if (state.Stage == IslandEscapeStage.Voyaging &&
                     AllMembersLanded(state, registration.LandingTile))
            {
                SetStage(registration, IslandEscapeStage.Landing);
            }
            else if (state.Stage == IslandEscapeStage.Landing)
            {
                SetStage(registration, IslandEscapeStage.Founding);
            }
        }

        internal static void Clear()
        {
            Groups.Clear();
            ActorGroups.Clear();
        }

        private static bool ExecuteBoarding(Actor pActor,
            Registration pRegistration, out WorldTile pMoveTarget)
        {
            pMoveTarget = null;
            IslandEscapeGroupState state = pRegistration.State;
            if (pActor == null || pActor.is_inside_boat) return true;
            if (pActor.current_tile?.data?.tile_id !=
                pRegistration.EntryTile.data.tile_id)
            {
                pMoveTarget = pRegistration.EntryTile;
                return true;
            }
            if (!AWDockTaxiRouteService.TryCreateOrJoinRequest(pActor,
                    pRegistration.EntryTile, pRegistration.LandingTile,
                    out TaxiRequest request))
            {
                Fail(pRegistration, "transport_request_failed");
                return false;
            }
            state.TransportRequestId = request?.GetHashCode() ?? -1L;
            if (AllMembersHaveRequests(state))
            {
                SetStage(pRegistration, IslandEscapeStage.Voyaging);
                state.TransportRequestId = ResolveCommonRequestId(state);
            }
            return true;
        }

        private static bool ExecuteVoyage(Actor pActor,
            Registration pRegistration)
        {
            if (AllMembersLanded(pRegistration.State, pRegistration.LandingTile))
            {
                SetStage(pRegistration, IslandEscapeStage.Landing);
                SetStage(pRegistration, IslandEscapeStage.Founding);
                Complete(pRegistration);
            }
            return true;
        }

        private static bool AllMembersHaveRequests(IslandEscapeGroupState pState)
        {
            return pState.MemberActorIds.All(pActorId =>
            {
                Actor actor = ResolveActor(pActorId);
                return actor?.data != null && actor.is_inside_boat ||
                    actor != null && TaxiManager.getRequestForActor(actor) != null;
            });
        }

        private static bool AllMembersLanded(IslandEscapeGroupState pState,
            WorldTile pLandingTile)
        {
            return pLandingTile?.data != null && pState.MemberActorIds.All(
                pActorId =>
                {
                    Actor actor = ResolveActor(pActorId);
                    return actor?.data != null && actor.isAlive() &&
                        !actor.is_inside_boat &&
                        actor.current_tile?.isSameIsland(pLandingTile) == true;
                });
        }

        private static long ResolveCommonRequestId(IslandEscapeGroupState pState)
        {
            Actor actor = ResolveActor(pState.MemberActorIds.FirstOrDefault());
            TaxiRequest request = actor == null ? null :
                TaxiManager.getRequestForActor(actor);
            return request?.GetHashCode() ?? -1L;
        }

        private static void Complete(Registration pRegistration)
        {
            if (pRegistration.State.Stage == IslandEscapeStage.Completed)
                return;
            SetStage(pRegistration, IslandEscapeStage.Completed);
            IReadOnlyList<Actor> members = pRegistration.State.MemberActorIds
                .Select(ResolveActor)
                .Where(IsLiveMember)
                .ToList();
            try { pRegistration.OnFounded?.Invoke(pRegistration.State, members); }
            catch (Exception e) { ModClass.LogWarning(
                "Island escape founding callback failed: " + e.Message); }
            ReleaseActors(pRegistration.State);
        }

        private static void Fail(Registration pRegistration, string pReason)
        {
            if (pRegistration.State.Stage == IslandEscapeStage.Failed ||
                pRegistration.State.Stage == IslandEscapeStage.Completed)
                return;
            pRegistration.State.FailureCount++;
            SetStage(pRegistration, IslandEscapeStage.Failed);
            try { pRegistration.OnFailed?.Invoke(pRegistration.State, pReason); }
            catch (Exception e) { ModClass.LogWarning(
                "Island escape failure callback failed: " + e.Message); }
            ReleaseActors(pRegistration.State);
        }

        private static void ReleaseActors(IslandEscapeGroupState pState)
        {
            foreach (long actorId in pState.MemberActorIds)
            {
                ActorGroups.Remove(actorId);
                Actor actor = ResolveActor(actorId);
                try { actor?.ai?.setJob(actor.getNextJob()); }
                catch { }
            }
        }

        private static void SetStage(Registration pRegistration,
            IslandEscapeStage pStage)
        {
            if (pRegistration?.State == null ||
                pRegistration.State.Stage == pStage) return;
            pRegistration.State.Stage = pStage;
            try { pRegistration.OnStageChanged?.Invoke(pRegistration.State); }
            catch (Exception e) { ModClass.LogWarning(
                "Island escape stage callback failed: " + e.Message); }
        }

        private static void PruneMembers(IslandEscapeGroupState pState)
        {
            pState.MemberActorIds = pState.MemberActorIds.Where(pActorId =>
            {
                Actor actor = ResolveActor(pActorId);
                return IsLiveMember(actor);
            }).ToList();
        }

        private static bool TryGetRegistration(Actor pActor,
            out Registration pRegistration)
        {
            pRegistration = null;
            if (pActor?.data == null ||
                !ActorGroups.TryGetValue(pActor.getID(), out string key))
                return false;
            return Groups.TryGetValue(key, out pRegistration) &&
                   pRegistration?.State != null;
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId <= 0 || World.world?.units == null) return null;
            try { return World.world.units.get(pActorId); }
            catch { return null; }
        }

        private static bool IsInitialMember(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() &&
                !pActor.isRekt() && !pActor.is_inside_boat;
        }

        private static bool IsLiveMember(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() &&
                !pActor.isRekt();
        }
    }
}
