using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class DynasticMaleLineContinuityService
    {
        public const int MaxHolderRefreshesPerCycle = 8;

        private static readonly HashSet<long> ActiveMaleTitleHolders =
            new HashSet<long>();
        private static readonly HashSet<long> HoldersNeedingSuccessor =
            new HashSet<long>();
        private static readonly HashSet<long> ExpectedMaleTitleSuccessors =
            new HashSet<long>();
        private static readonly Dictionary<long, long> SuccessorByHolder =
            new Dictionary<long, long>();
        // SuccessorByHolder 的反向映射。缺了它,一个「旁系」预定继承人死亡时
        // 持有者永远不会被重算 —— OnActorDying 原本只重新入队死者的父母,而旁系
        // 继承人的父母通常不是持有者,于是预定就此过期。
        private static readonly Dictionary<long, long> HolderBySuccessor =
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
            bool needsSuccessor = HoldersNeedingSuccessor.Contains(
                pActor.data.id);
            return DynasticMaleLineContinuityRules
                .ShouldBypassPersonalOffspringLimit(
                    needsSuccessor, alive, adult, breedingAge,
                    canProduceBabies, hasLivingSon: false);
        }

        public static void RequestContinuation(Actor pHolder)
        {
            if (pHolder?.data == null || !pHolder.isSexMale() ||
                !SafeIsAlive(pHolder)) return;
            EnqueueHolder(pHolder.data.id);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            RequestContinuation(pKingdom.king);
            RequestContinuation(HeirService.GetHeir(pKingdom));
            try
            {
                foreach (FeudatorySnapshot snapshot in
                         FeudatoryService.GetByKingdom(pKingdom.id))
                    RequestContinuation(ResolveActor(
                        snapshot?.PrinceActorId ?? -1L));
            }
            catch { }
        }

        public static void OnTitleProjectionChanged(Actor pHolder)
        {
            if (pHolder?.data == null) return;
            long holderId = pHolder.data.id;
            if (IsHereditaryHolder(pHolder) && SafeIsAlive(pHolder))
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
            // 死者是某个持有者的预定继承人时,必须把那个持有者重新入队重算 ——
            // 原来只入队死者的父母,而旁系继承人的父母通常不是持有者。
            if (HolderBySuccessor.TryGetValue(actorId, out long holderId))
            {
                HolderBySuccessor.Remove(actorId);
                if (SuccessorByHolder.TryGetValue(holderId,
                        out long designatedId) && designatedId == actorId)
                    SuccessorByHolder.Remove(holderId);
                if (holderId != actorId) EnqueueHolder(holderId);
            }

            ActiveMaleTitleHolders.Remove(actorId);
            ExpectedMaleTitleSuccessors.Remove(actorId);
            RemoveHolderSuccessor(actorId);
            HoldersNeedingSuccessor.Remove(actorId);
            QueueParentHolder(pActor.data.parent_id_1);
            if (pActor.data.parent_id_2 != pActor.data.parent_id_1)
                QueueParentHolder(pActor.data.parent_id_2);
        }

        /// <summary>
        /// 成年会新增旁系候选资格(HereditaryTitleSuccessionService
        /// .AddCollateralCandidate 要求 SafeAdult),所以一个新成年人可能正好补上
        /// 某个空缺。原来没有这个钩子。
        ///
        /// 只重新入队「当前缺继承人」的持有者:成年只会增加资格、不会让已有预定
        /// 失效,所以其余持有者无需重算。这个集合本身就很小,而 EnqueueHolder 又
        /// 按 id 去重,所以多个人同时成年也不会放大成本。
        /// </summary>
        public static void OnActorAdult(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isSexMale() ||
                !SafeIsAdult(pActor) || !SafeIsAlive(pActor)) return;
            if (HoldersNeedingSuccessor.Count == 0) return;
            foreach (long holderId in HoldersNeedingSuccessor)
                EnqueueHolder(holderId);
        }

        public static void OnActorLoaded(Actor pActor)
        {
            if (pActor?.data == null) return;
            if (IsHereditaryHolder(pActor) && SafeIsAlive(pActor))
            {
                ActiveMaleTitleHolders.Add(pActor.data.id);
                EnqueueHolder(pActor.data.id);
            }
            if (!pActor.isSexMale()) return;
            QueueParentHolder(pActor.data.parent_id_1);
            if (pActor.data.parent_id_2 != pActor.data.parent_id_1)
                QueueParentHolder(pActor.data.parent_id_2);
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
            HoldersNeedingSuccessor.Clear();
            ExpectedMaleTitleSuccessors.Clear();
            SuccessorByHolder.Clear();
            HolderBySuccessor.Clear();
            DirtyHolders.Clear();
            EnqueuedHolders.Clear();
        }

        private static void RefreshHolder(long pHolderId)
        {
            Actor holder = ResolveActor(pHolderId);
            if (holder?.data == null || !SafeIsAlive(holder) ||
                !IsHereditaryHolder(holder))
            {
                ActiveMaleTitleHolders.Remove(pHolderId);
                RemoveHolderSuccessor(pHolderId);
                HoldersNeedingSuccessor.Remove(pHolderId);
                StoreDesignation(holder, -1L);
                return;
            }

            ActiveMaleTitleHolders.Add(pHolderId);
            long successorId = SelectExpectedSuccessorId(holder);
            RemoveHolderSuccessor(pHolderId);
            if (DynasticMaleLineContinuityRules.ShouldRequestHeir(
                    eligibleHereditaryRole: true,
                    hasLegalSuccessor: successorId >= 0L))
            {
                HoldersNeedingSuccessor.Add(pHolderId);
                NobleRemarriageService.MarkDirty(holder.kingdom);
                NobleHeirPregnancyService.RequestForHolder(holder);
            }
            else
            {
                HoldersNeedingSuccessor.Remove(pHolderId);
                NobleHeirPregnancyService.CancelPendingForHolder(holder);
            }
            // 预定写在持有者身上并随存档持久化,死亡路径直接读它。
            StoreDesignation(holder, successorId);
            if (successorId < 0L) return;
            SuccessorByHolder[pHolderId] = successorId;
            HolderBySuccessor[successorId] = pHolderId;
            ExpectedMaleTitleSuccessors.Add(successorId);
        }

        private static void StoreDesignation(Actor pHolder, long pSuccessorId)
        {
            if (pHolder?.data == null) return;
            pHolder.data.set(LineageKeys.NOBLE_TITLE_SUCCESSOR_ID,
                pSuccessorId);
        }

        /// <summary>
        /// 持有者身上记下的预定继承人。死亡路径用它,失效由调用方退回搜索。
        /// </summary>
        internal static long ReadDesignatedSuccessorId(Actor pHolder)
        {
            if (pHolder?.data == null) return -1L;
            pHolder.data.get(LineageKeys.NOBLE_TITLE_SUCCESSOR_ID,
                out long successorId, -1L);
            return successorId;
        }

        private static long SelectExpectedSuccessorId(Actor pHolder)
        {
            Actor successor = HereditaryTitleSuccessionService.FindSuccessor(
                pHolder, pHolder?.kingdom);
            return successor?.data?.id ?? -1L;
        }

        private static void QueueIfMaleTitleHolder(Actor pActor)
        {
            if (pActor?.data == null || !IsHereditaryHolder(pActor)) return;
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
            if (HolderBySuccessor.TryGetValue(successorId,
                    out long mappedHolderId) &&
                mappedHolderId == pHolderId)
                HolderBySuccessor.Remove(successorId);
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

        private static bool IsHereditaryHolder(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isSexMale()) return false;
            return SafeIsKing(pActor) || SafeIsRegisteredHeir(pActor) ||
                   SafeIsFeudatoryPrince(pActor) ||
                   HasActiveMaleTitle(pActor) ||
                   // 只持有可继承虚衔的人也要预定,否则他们永远走死亡时兜底搜索。
                   // 这一项是纯 actor data 读取,不查库 —— 本方法跑在读档的每个
                   // actor 上。
                   VirtualNobleTitleService.HoldsHereditaryTitleFast(pActor);
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
