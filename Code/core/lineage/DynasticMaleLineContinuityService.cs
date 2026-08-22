using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class DynasticMaleLineContinuityService
    {
        public const int MaxHolderRefreshesPerCycle = 8;

        private static readonly HashSet<long> ActiveMaleTitleHolders =
            new HashSet<long>();
        private static readonly HashSet<long> ExpectedMaleTitleSuccessors =
            new HashSet<long>();
        private static readonly Dictionary<long, long> SuccessorByHolder =
            new Dictionary<long, long>();
        private static readonly Queue<long> DirtyHolders =
            new Queue<long>();
        private static readonly HashSet<long> EnqueuedHolders =
            new HashSet<long>();

        public static bool HasEligibleRole(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isSexMale()) return false;
            bool isKing = SafeIsKing(pActor);
            bool isRegisteredHeir = SafeIsRegisteredHeir(pActor);
            bool isFeudatoryPrince = SafeIsFeudatoryPrince(pActor);
            bool isFeudatorySuccessor = SafeIsFeudatorySuccessor(pActor);
            long actorId = pActor.data.id;
            bool holdsActiveMaleTitle =
                ActiveMaleTitleHolders.Contains(actorId);
            bool isExpectedMaleTitleSuccessor =
                ExpectedMaleTitleSuccessors.Contains(actorId);
            return DynasticMaleLineContinuityRules.IsEligibleRole(isKing,
                isRegisteredHeir, isFeudatoryPrince,
                isFeudatorySuccessor, holdsActiveMaleTitle,
                isExpectedMaleTitleSuccessor);
        }

        public static bool NeedsContinuation(Actor pActor)
        {
            if (pActor?.data == null) return false;
            bool alive = SafeIsAlive(pActor);
            bool adult = alive && SafeIsAdult(pActor);
            bool breedingAge = adult && SafeIsBreedingAge(pActor);
            bool canProduceBabies = breedingAge &&
                                    SafeCanProduceBabies(pActor);
            return DynasticMaleLineContinuityRules
                .ShouldBypassPersonalOffspringLimit(
                    HasEligibleRole(pActor), alive, adult, breedingAge,
                    canProduceBabies,
                    DynasticLivingSonIndexService.HasLivingSon(pActor));
        }

        public static void OnTitleProjectionChanged(Actor pHolder)
        {
            if (pHolder?.data == null) return;
            long holderId = pHolder.data.id;
            if (HasActiveMaleTitle(pHolder) && SafeIsAlive(pHolder))
                ActiveMaleTitleHolders.Add(holderId);
            else
                ActiveMaleTitleHolders.Remove(holderId);
            EnqueueHolder(holderId);
        }

        public static void OnChildBorn(Actor pChild, Actor pParent1,
            Actor pParent2)
        {
            QueueIfMaleTitleHolder(pParent1);
            if (pParent2 != pParent1) QueueIfMaleTitleHolder(pParent2);
        }

        public static void OnActorDying(Actor pActor)
        {
            if (pActor?.data == null) return;
            long actorId = pActor.data.id;
            ActiveMaleTitleHolders.Remove(actorId);
            ExpectedMaleTitleSuccessors.Remove(actorId);
            RemoveHolderSuccessor(actorId);
            QueueParentHolder(pActor.data.parent_id_1);
            if (pActor.data.parent_id_2 != pActor.data.parent_id_1)
                QueueParentHolder(pActor.data.parent_id_2);
        }

        public static void OnActorLoaded(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (!HasActiveMaleTitle(pActor) || !SafeIsAlive(pActor)) return;
            ActiveMaleTitleHolders.Add(pActor.data.id);
            EnqueueHolder(pActor.data.id);
        }

        public static void ProcessAuthorityCycle()
        {
            int count = System.Math.Min(MaxHolderRefreshesPerCycle,
                DirtyHolders.Count);
            for (int i = 0; i < count; i++)
            {
                long holderId = DirtyHolders.Dequeue();
                EnqueuedHolders.Remove(holderId);
                RefreshHolder(holderId);
            }
        }

        public static void Reset()
        {
            ActiveMaleTitleHolders.Clear();
            ExpectedMaleTitleSuccessors.Clear();
            SuccessorByHolder.Clear();
            DirtyHolders.Clear();
            EnqueuedHolders.Clear();
        }

        private static void RefreshHolder(long pHolderId)
        {
            Actor holder = ResolveActor(pHolderId);
            if (holder?.data == null || !SafeIsAlive(holder) ||
                !HasActiveMaleTitle(holder))
            {
                ActiveMaleTitleHolders.Remove(pHolderId);
                RemoveHolderSuccessor(pHolderId);
                return;
            }

            ActiveMaleTitleHolders.Add(pHolderId);
            long successorId = SelectExpectedSuccessorId(holder);
            RemoveHolderSuccessor(pHolderId);
            if (successorId < 0L) return;
            SuccessorByHolder[pHolderId] = successorId;
            ExpectedMaleTitleSuccessors.Add(successorId);
        }

        private static long SelectExpectedSuccessorId(Actor pHolder)
        {
            Actor successor = HereditaryTitleSuccessionService.FindSuccessor(
                pHolder, pHolder?.kingdom);
            return successor?.data?.id ?? -1L;
        }

        private static void QueueIfMaleTitleHolder(Actor pActor)
        {
            if (pActor?.data == null || !HasActiveMaleTitle(pActor)) return;
            ActiveMaleTitleHolders.Add(pActor.data.id);
            EnqueueHolder(pActor.data.id);
        }

        private static void QueueParentHolder(long pParentId)
        {
            Actor parent = ResolveActor(pParentId);
            QueueIfMaleTitleHolder(parent);
        }

        private static void EnqueueHolder(long pHolderId)
        {
            if (pHolderId < 0L || !EnqueuedHolders.Add(pHolderId)) return;
            DirtyHolders.Enqueue(pHolderId);
        }

        private static void RemoveHolderSuccessor(long pHolderId)
        {
            if (!SuccessorByHolder.TryGetValue(pHolderId,
                    out long successorId))
                return;
            SuccessorByHolder.Remove(pHolderId);
            ExpectedMaleTitleSuccessors.Remove(successorId);
        }

        private static bool HasActiveMaleTitle(Actor pActor)
        {
            try
            {
                NobleTitleSnapshot title = NobleRankService.ReadHot(pActor);
                return title.IsActive &&
                       title.Style == NobleTitleStyle.Male;
            }
            catch { return false; }
        }

        private static bool SafeIsKing(Actor pActor)
        {
            try { return pActor.isKing(); }
            catch { return false; }
        }

        private static bool SafeIsRegisteredHeir(Actor pActor)
        {
            try
            {
                return pActor.kingdom?.data != null &&
                       HeirService.IsCurrentHeir(pActor.kingdom, pActor);
            }
            catch { return false; }
        }

        private static bool SafeIsFeudatoryPrince(Actor pActor)
        {
            try { return FeudatoryService.IsActivePrince(pActor); }
            catch { return false; }
        }

        private static bool SafeIsFeudatorySuccessor(Actor pActor)
        {
            try
            {
                return FeudatoryService.TryGetBySuccessor(pActor.data.id,
                           out FeudatorySnapshot snapshot) &&
                       snapshot.SuccessorActorId == pActor.data.id;
            }
            catch { return false; }
        }

        private static bool SafeIsAlive(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool SafeIsAdult(Actor pActor)
        {
            try { return pActor.isAdult(); }
            catch { return false; }
        }

        private static bool SafeIsBreedingAge(Actor pActor)
        {
            try { return pActor.isBreedingAge(); }
            catch { return false; }
        }

        private static bool SafeCanProduceBabies(Actor pActor)
        {
            try { return pActor.canProduceBabies(); }
            catch { return false; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId < 0L) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}
