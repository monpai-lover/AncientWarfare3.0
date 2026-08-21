using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    internal enum IslandEscapeStage
    {
        None,
        Evaluating,
        Gathering,
        Boarding,
        Voyaging,
        Landing,
        Founding,
        Completed,
        Failed
    }

    internal sealed class IslandEscapeMemberFact
    {
        internal IslandEscapeMemberFact(long pActorId, bool pAlive,
            bool pBelongsToOrigin, bool pInsideBoat)
        {
            ActorId = pActorId;
            Alive = pAlive;
            BelongsToOrigin = pBelongsToOrigin;
            InsideBoat = pInsideBoat;
        }

        internal long ActorId { get; }
        internal bool Alive { get; }
        internal bool BelongsToOrigin { get; }
        internal bool InsideBoat { get; }
    }

    internal static class IslandEscapeBehaviourRules
    {
        internal static IReadOnlyList<long> BuildManifest(
            IEnumerable<IslandEscapeMemberFact> pFacts)
        {
            return (pFacts ?? Enumerable.Empty<IslandEscapeMemberFact>())
                .Where(pFact => pFact != null && pFact.ActorId > 0 &&
                                pFact.Alive && pFact.BelongsToOrigin &&
                                !pFact.InsideBoat)
                .Select(pFact => pFact.ActorId)
                .Distinct()
                .OrderBy(pActorId => pActorId)
                .ToList();
        }

        internal static bool CanStartBoarding(IslandEscapeStage pStage,
            int pManifestCount)
        {
            return pManifestCount > 0 &&
                (pStage == IslandEscapeStage.Evaluating ||
                 pStage == IslandEscapeStage.Gathering);
        }

        internal static bool AllMembersReadyForVoyage(
            IEnumerable<bool> pMemberRequests)
        {
            List<bool> requests = (pMemberRequests ??
                    Enumerable.Empty<bool>()).ToList();
            return requests.Count > 0 && requests.All(pReady => pReady);
        }

        internal static bool AllMembersLanded(
            IEnumerable<bool> pMemberLanded)
        {
            List<bool> landed = (pMemberLanded ??
                    Enumerable.Empty<bool>()).ToList();
            return landed.Count > 0 && landed.All(pReady => pReady);
        }

        internal static bool CanTransition(IslandEscapeStage pCurrent,
            IslandEscapeStage pNext, int pManifestCount)
        {
            if (pNext == IslandEscapeStage.Failed)
                return pCurrent != IslandEscapeStage.None &&
                       pCurrent != IslandEscapeStage.Completed;
            if (pNext == IslandEscapeStage.None)
                return pCurrent == IslandEscapeStage.Failed ||
                       pCurrent == IslandEscapeStage.Completed;
            if (pNext >= IslandEscapeStage.Boarding &&
                pNext <= IslandEscapeStage.Founding && pManifestCount <= 0)
                return false;

            return pCurrent switch
            {
                IslandEscapeStage.None =>
                    pNext == IslandEscapeStage.Evaluating,
                IslandEscapeStage.Evaluating =>
                    pNext == IslandEscapeStage.Gathering,
                IslandEscapeStage.Gathering =>
                    pNext == IslandEscapeStage.Boarding,
                IslandEscapeStage.Boarding =>
                    pNext == IslandEscapeStage.Voyaging,
                IslandEscapeStage.Voyaging =>
                    pNext == IslandEscapeStage.Landing,
                IslandEscapeStage.Landing =>
                    pNext == IslandEscapeStage.Founding,
                IslandEscapeStage.Founding =>
                    pNext == IslandEscapeStage.Completed,
                _ => false
            };
        }
    }
}
