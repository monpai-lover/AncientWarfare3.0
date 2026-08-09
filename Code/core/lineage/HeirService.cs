using System.Collections.Generic;
using ai;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     缁ф壙浜虹郴缁?鍙傝€?AW2 AW_Kingdom.FindHeir/SetHeir,浣嗕笉鐢ㄥ瓙绫诲ず鑸?鈥斺€?鏂扮増涓嶅彲琛?
    ///     鏀圭敤 kingdom.data 鑷畾涔夊瓧娈?aw_heir_id 瀛樼户鎵夸汉 + Harmony patch 鎺ョ缁т綅)銆?    ///
    ///     閫夋嫨瑙勫垯(AW2 鍚?:royal_clan 鎴愬憳涓?娲荤潃鈭ч潪鐜嬧埀鎴愬勾鈭ч潪鐤媯;鎸?|age-18| 鏈€灏忎紭鍏?    ///     (瓒婃帴杩戞垚骞磋秺浼樺厛,閬垮厤閫夎€佷汉鎴栧瀛?銆?    /// </summary>
    internal static class HeirService
    {
        private struct HeirSelection
        {
            public Actor Actor;
            public string Mode;

            public HeirSelection(Actor pActor, string pMode)
            {
                Actor = pActor;
                Mode = string.IsNullOrEmpty(pMode) ? SuccessionMode.NONE : pMode;
            }
        }

        /// <summary>閲嶉€夌户鎵夸汉骞跺啓鍏?kingdom.data銆傛柊鐜嬪嵆浣嶅悗璋冪敤銆傚悓姝ョ淮鎶?actor.data 鐨?IS_HEIR 鏍囪(heir 鐨偆 + minimap 鐢?銆?/summary>
        public static void RefreshHeir(Kingdom pKingdom)
        {
            if (RepublicGovernmentService.IsRepublic(pKingdom))
            {
                RepublicGovernmentService.RefreshRepublicSuccessor(pKingdom, pKingdom?.king);
                return;
            }
            RefreshHeirAndReturn(pKingdom);
        }

        public static void RememberPreSuccessionKing(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            pKingdom.data.set(LineageKeys.KINGDOM_PRE_SUCCESSION_KING_ID, pKing.data.id);
            EnsureLegitimateLine(pKingdom, pKing);
        }

        public static void EnsureLegitimateLine(Kingdom pKingdom, Actor pKing = null)
        {
            if (pKingdom?.data == null) return;
            Actor king = pKing ?? pKingdom.king;
            if (king?.data == null || (!LineageService.IsXia(king) && !LineageService.UsesAwLineageSystem(king)))
                return;

            king.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            king.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (lineageId < 0 && shiId >= 0)
            {
                ShiBranchInfo info = LineageQuery.GetShiBranchInfo(shiId);
                if (info != null) lineageId = info.lineage_id;
            }
            if (lineageId < 0 && shiId < 0) return;

            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, out long oldLineage, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID, out long oldShi, -1L);
            if (oldLineage < 0 && lineageId >= 0)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, lineageId);
            if (oldShi < 0 && shiId >= 0)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID, shiId);
        }

        /// <summary>娓呮帀 kingdom 褰撳墠鐧昏缁ф壙浜?actor 鐨?IS_HEIR 鏍囪(鑻ヨ actor 浠嶅湪)銆?/summary>
        private static void ClearOldHeirFlag(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long oldId, -1L);
            if (oldId < 0) return;
            Actor old = World.world?.units?.get(oldId);
            if (old?.data == null) return;
            int otherRegistrations = CountOtherLiveHeirRegistrations(oldId, pKingdom);
            if (HeirRegistrationRules.ShouldClearGlobalFlag(otherRegistrations))
                SetHeirFlag(old, false);
        }

        private static int CountOtherLiveHeirRegistrations(long pActorId,
            Kingdom pExcludedKingdom)
        {
            KingdomManager kingdoms = World.world?.kingdoms;
            if (pActorId < 0 || kingdoms == null) return 0;

            int count = 0;
            foreach (Kingdom kingdom in kingdoms)
            {
                if (kingdom?.data == null) continue;
                kingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
                bool isRekt = kingdom.isRekt();
                bool isCivilization = !isRekt && kingdom.isCiv();
                bool hasCities = isCivilization && kingdom.hasCities();
                if (HeirRegistrationRules.CountsAsOtherLiveRegistration(
                        object.ReferenceEquals(kingdom, pExcludedKingdom),
                        isCivilization,
                        isRekt,
                        hasCities,
                        heirId,
                        pActorId))
                    count++;
            }
            return count;
        }

        /// <summary>
        ///     鍙栫户鎵夸汉:**鐜颁换浼樺厛绋冲畾**鈥斺€斿凡鐧昏缁ф壙浜轰笖**浠嶅悎鏍?*(娲?闈炵帇/鎴愬勾/闈炵柉)鍒欎繚鎸佷笉鍙?
        ///     鐜颁换澶辨牸(姝?缁т綅/澶辨牸)鎴栦粠鏈櫥璁?鈫?鎵?FindHeir 閲嶉€夊苟鍐欏洖銆?        ///     鍏奸【涓や釜闇€姹?鈶?宸叉湁鍚堟牸缁ф壙浜轰笉琚绻佹敼閫?鐢ㄦ埛鎶?宸插瓨鍦ㄨ繕琚噸閫?);
        ///     鈶?鍗充綅鏃跺効瀛愯繕灏忋€佸悗鏉ユ垚骞?鈫?鐜颁换涓虹┖/澶辨牸鏃堕噸閫変細鎶婃垚骞村効瀛愰€変笂銆?        /// </summary>
        public static Actor GetHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            if (RepublicGovernmentService.IsRepublic(pKingdom))
                return RepublicGovernmentService.GetRegisteredSuccessor(pKingdom);
            return ReconcileHeir(pKingdom, pForce: false);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            bool lawChanged;
            try
            {
                lawChanged = InheritanceLawService.OnKingdomYear(pKingdom);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.KingdomInheritanceLawIndex,
                    benchmark);
            }

            benchmark = RecentFeatureBenchmark.Begin();
            try { ReconcileHeir(pKingdom, pForce: lawChanged); }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.KingdomHeirReconcileIndex,
                    benchmark);
            }

            benchmark = RecentFeatureBenchmark.Begin();
            try { SuccessionDisputeService.OnKingdomYear(pKingdom); }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.KingdomSuccessionDisputeIndex,
                    benchmark);
            }
        }

        public static Actor ReconcileHeir(Kingdom pKingdom, bool pForce)
        {
            if (pKingdom?.data == null) return null;
            if (RepublicGovernmentService.IsRepublic(pKingdom))
                return RepublicGovernmentService.GetRegisteredSuccessor(pKingdom);

            Actor cached = PeekRegisteredHeir(pKingdom);
            bool pending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
            if (pending) return cached;

            long referenceKingId = ResolveReferenceKingId(pKingdom, pKingdom.king);
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_RELATION_ACTOR_ID, out long signedHeirId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_RELATION_KING_ID, out long signedKingId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_SELECTION_DIRTY,
                out bool successionDirty, false);
            bool cachedRelationshipValid = cached?.data != null &&
                HeirDirectSonRules.IsCachedRelationshipSignatureValid(
                    cached.data.id, referenceKingId, signedHeirId, signedKingId);
            InheritanceLaw effectiveLaw =
                InheritanceLawService.GetEffectiveLaw(pKingdom);
            bool cachedEligible = cached?.data != null &&
                                  (effectiveLaw == InheritanceLaw.Primogeniture ||
                                   cached.isAdult());
            if (!HeirDirectSonRules.NeedsEventDrivenRefresh(pForce,
                    cachedEligible, cachedRelationshipValid,
                    successionDirty))
                return cached;
            return RefreshHeirAndReturn(pKingdom);
        }

        public static void MarkSuccessionDirtyForActor(Actor pActor)
        {
            if (pActor?.data == null) return;
            Kingdom kingdom = pActor.kingdom;
            Actor king = kingdom?.king;
            if (kingdom?.data == null || king?.data == null) return;
            kingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                out long heirId, -1L);
            bool registeredHeir = heirId == pActor.data.id;
            bool directRoyalChild = pActor.data.parent_id_1 == king.data.id ||
                                    pActor.data.parent_id_2 == king.data.id;
            if (!registeredHeir && !directRoyalChild) return;
            kingdom.data.set(LineageKeys.KINGDOM_HEIR_SELECTION_DIRTY, true);
            SuccessionPreparationService.MarkDirty(kingdom);
        }

        public static Actor PeekRegisteredHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            if (heirId < 0) return null;
            Actor heir = World.world?.units?.get(heirId);
            return IsRegisteredCandidateEligible(heir, pKingdom) ? heir : null;
        }

        public static Actor PeekStoredHeirForMinimap(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            if (heirId < 0) return null;

            Actor heir = World.world?.units?.get(heirId);
            if (heir?.data == null || !heir.isAlive()) return null;
            return heir;
        }

        public static Actor FindHeirReadOnly(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            if (RepublicGovernmentService.IsRepublic(pKingdom))
                return RepublicGovernmentService.GetRegisteredSuccessor(pKingdom);
            Actor cached = PeekRegisteredHeir(pKingdom);
            if (cached?.data != null) return cached;
            if (SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king)) return null;

            Actor knownKing = pKingdom.king;
            long referenceKingId = ResolveReferenceKingId(pKingdom, knownKing);
            return SelectByEffectiveLaw(pKingdom, knownKing,
                referenceKingId, pIncludeRegisteredHeir: false).Actor;
        }

        internal static Actor PreviewSuccessionCandidate(Kingdom pKingdom,
            Actor pReferenceKing, out string pMode)
        {
            pMode = SuccessionMode.NONE;
            if (pKingdom?.data == null || pReferenceKing?.data == null)
                return null;
            Actor registered = PeekStoredHeirForMinimap(pKingdom);
            if (IsRegisteredCandidateEligible(registered, pKingdom))
            {
                pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE,
                    out pMode, SuccessionMode.NONE);
                if (string.IsNullOrEmpty(pMode) ||
                    pMode == SuccessionMode.NONE)
                    pMode = SuccessionMode.REGISTERED;
                return registered;
            }

            long referenceKingId = pReferenceKing.data.id;
            HeirSelection selection = SelectByEffectiveLaw(pKingdom,
                pReferenceKing, referenceKingId,
                pIncludeRegisteredHeir: false);
            pMode = selection.Mode;
            return selection.Actor;
        }

        internal static Actor PreviewPrimogenitureCandidate(Kingdom pKingdom,
            Actor pReferenceKing = null)
        {
            if (pKingdom?.data == null ||
                RepublicGovernmentService.IsRepublic(pKingdom)) return null;
            Actor knownKing = pReferenceKing ?? pKingdom.king;
            long referenceKingId = ResolveReferenceKingId(pKingdom,
                knownKing);
            return FindHeir(pKingdom, knownKing, referenceKingId,
                pIncludeRegisteredHeir: false).Actor;
        }

        private static Actor RefreshHeirAndReturn(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            Actor knownKing = pKingdom.king;
            Actor storedHeir = PeekStoredHeirForMinimap(pKingdom);
            if (storedHeir?.data != null &&
                !RoyalGuardService.ReleaseForRegisteredHeir(pKingdom,
                    storedHeir, "became_heir"))
            {
                ClearHeir(pKingdom);
                return null;
            }
            EnsureLegitimateLine(pKingdom, knownKing);
            InheritanceLawService.RestorePrimogenitureForDirectSon(pKingdom,
                PickEldestLivingSon(knownKing)?.data != null);
            long referenceKingId = ResolveReferenceKingId(pKingdom, knownKing);
            bool pending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
            if (!SuccessionTransitionRules.ShouldOverwriteCachedHeir(pending, referenceKingId >= 0))
                return PeekRegisteredHeir(pKingdom);

            HeirSelection selection = SelectByEffectiveLaw(pKingdom,
                knownKing, referenceKingId,
                pIncludeRegisteredHeir: false);
            return StoreHeirSelection(pKingdom, selection);
        }

        private static long ResolveReferenceKingId(Kingdom pKingdom, Actor pKnownKing)
        {
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(LineageKeys.KINGDOM_PRE_SUCCESSION_KING_ID, out long previousKingId, -1L);
            return SuccessionTransitionRules.ResolveReferenceKingId(
                pKnownKing?.data?.id ?? -1L,
                pKnownKing?.data != null,
                previousKingId);
        }

        public static bool HasHeir(Kingdom pKingdom)
        {
            return GetHeir(pKingdom) != null;
        }

        public static bool HasSuccessionCandidate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (FindHeirReadOnly(pKingdom)?.data != null) return true;
            return ShouldUseOrdinaryFallbackSuccession(pKingdom) && GetLeaderSuccessionCandidate(pKingdom) != null;
        }

        public static void RefreshForNewRoyalChild(Actor pBaby, Actor pParent1, Actor pParent2)
        {
            if (pBaby?.data == null || !pBaby.isSexMale()) return;
            Actor father = FindFather(pParent1, pParent2);
            if (father?.data == null) return;
            Kingdom kingdom = father.kingdom;
            bool fatherIsCurrentKing = kingdom?.king == father || father.isKing();
            if (!RoyalSuccessionBirthRules.ShouldRefreshHeirForNewChild(pBaby.isSexMale(), fatherIsCurrentKing))
                return;
            RefreshHeir(kingdom);
        }

        public static int GetMandateChildScarcityPenalty(Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            if (king?.data == null)
                return MandateSuccessionRules.ChildScarcityPenalty(0, 0, 0, pHasKing: false,
                    pYearsSinceAccession: 0);

            int adultSons = 0;
            int underageSons = 0;
            int totalChildren = 0;
            foreach (Actor child in king.getChildren(false))
            {
                if (child == null || child.isRekt()) continue;
                totalChildren++;
                if (!child.isSexMale()) continue;
                if (child.isAdult()) adultSons++;
                else underageSons++;
            }
            return MandateSuccessionRules.ChildScarcityPenalty(adultSons, underageSons, totalChildren, true,
                GetYearsSinceAccession(pKingdom, king));
        }

        private static int GetYearsSinceAccession(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null || pKing?.data == null) return 0;
            ReignRecordWriter.ReignInfo reign = ReignRecordWriter.ReadOpenReignInfo(pKingdom.id);
            if (!reign.IsValid || reign.KingActorId != pKing.data.id || reign.StartTime < 0) return 0;
            try
            {
                int currentYear = Date.getYear(World.world.getCurWorldTime());
                int startYear = Date.getYear(reign.StartTime);
                return currentYear > startYear ? currentYear - startYear : 0;
            }
            catch { return 0; }
        }

        public static bool IsCurrentHeir(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return false;
            // 读缓存的继承人 id(由王位交代/王室出生/RefreshHeir 事件维护),O(1)。
            // 不再每次 GetHeir 跑全搜索+副作用——那是奴隶军/禁卫军 per-unit 循环性能崩溃的根因。
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            return heirId >= 0 && heirId == pActor.data.id;
        }

        public static bool IsRecognizedSuccessionCandidate(
            Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return false;
            if (IsCurrentHeir(pKingdom, pActor)) return true;
            pActor.data.get(LineageKeys.IS_HEIR, out bool registered, false);
            if (registered) return true;

            Actor king = pKingdom.king;
            long referenceKingId = ResolveReferenceKingId(pKingdom, king);
            if (referenceKingId < 0L || pActor.kingdom != pKingdom ||
                !IsHeirBaseEligible(pActor, pKingdom, king) ||
                pActor.data.id == referenceKingId) return false;
            long ancestor = SuccessionRelationshipIndex.
                NearestCommonAgnaticAncestor(
                referenceKingId, pActor.data.id, out int kingDepth,
                out int candidateDepth);
            if (ancestor < 0L) return false;
            int tier = HeirGenerationRules.ClassifyTier(
                kingDepth == 0, candidateDepth - kingDepth);
            return HeirGenerationRules.IsEligible(tier);
        }

        public static void ClearHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                out long previousHeirId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE,
                out string previousMode, SuccessionMode.NONE);
            Actor previousHeir = previousHeirId < 0
                ? null
                : World.world?.units?.get(previousHeirId);
            ClearOldHeirFlag(pKingdom);                       // 娓呮棫缁ф壙浜?IS_HEIR 鏍囪
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE, SuccessionMode.NONE);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_RELATION_ACTOR_ID, -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_RELATION_KING_ID, -1L);
            InheritanceLawService.MirrorCandidate(pKingdom, null,
                SuccessionMode.NONE, -1L);
            CitySchoolSnapshotService.MarkKingdomDirty(pKingdom);
            RoyalMedicalCareService.ReconcileTargets(pKingdom);
            if (previousHeir?.data != null)
                LineageService.ArchiveActor(previousHeir,
                    pAlive: previousHeir.isAlive());
            if (previousHeirId >= 0 || previousMode != SuccessionMode.NONE)
                FamilyTreeProjectionRevision.Advance(
                    FamilyTreeProjectionChange.Heir);
            HeirMinimapMarkerIndex.Refresh(pKingdom);
        }

        public static void StoreSelectedHeir(Kingdom pKingdom, Actor pHeir, string pMode)
        {
            if (pKingdom?.data == null) return;
            StoreHeirSelection(pKingdom, new HeirSelection(pHeir, pMode));
        }

        public static string ResolveSuccessionModeForCandidate(
            Kingdom pKingdom, Actor pReferenceKing, Actor pCandidate,
            InheritanceLaw pLaw, Actor pDefaultCandidate = null,
            string pDefaultMode = null)
        {
            if (pKingdom?.data == null || pCandidate?.data == null)
                return SuccessionMode.NONE;
            if (pDefaultCandidate?.data != null &&
                pDefaultCandidate.data.id == pCandidate.data.id &&
                !string.IsNullOrEmpty(pDefaultMode) &&
                pDefaultMode != SuccessionMode.NONE)
                return pDefaultMode;
            if (pLaw == InheritanceLaw.MilitaryAcclaim ||
                pLaw == InheritanceLaw.CivilAcclaim)
                return InheritanceLawService.ModeForLaw(pLaw);

            bool direct = pReferenceKing?.data != null &&
                          (pCandidate.data.parent_id_1 ==
                               pReferenceKing.data.id ||
                           pCandidate.data.parent_id_2 ==
                               pReferenceKing.data.id);
            if (direct)
                return pCandidate.isAdult()
                    ? SuccessionMode.DIRECT
                    : SuccessionMode.UNDERAGE_DIRECT;
            return SuccessionMode.COLLATERAL_RESTORE;
        }

        public static void RecallForSuccession(Kingdom pKingdom, Actor pNewKing, bool pWasRegisteredHeir)
        {
            if (pKingdom?.data == null || pNewKing?.data == null) return;
            bool isCityLeader = IsCityLeaderOfAnyCity(pKingdom, pNewKing);
            bool isArmyCaptain = IsArmyCaptain(pNewKing);
            bool isGeneral = GeneralService.IsGeneral(pNewKing);
            bool hasFief = FiefService.GetFiefCityId(pNewKing) >= 0;
            if (!HeirRecallRules.ShouldRecallForSuccession(pWasRegisteredHeir, pNewKing.isKing(),
                    isCityLeader, isArmyCaptain, isGeneral, hasFief))
                return;

            if (isCityLeader) RemoveCityLeaderOffice(pKingdom, pNewKing);
            if (isGeneral || hasFief) GeneralService.RetireForSuccession(pNewKing);

            try { pNewKing.clearGraphicsFully(); } catch { }
        }

        public static Actor GetLeaderSuccessionCandidate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            Actor best = null;
            int bestScore = int.MinValue;
            foreach (City city in pKingdom.getCities())
            {
                Actor leader = city?.leader;
                if (!IsSuitableHeir(leader, pKingdom.king)) continue;
                if (leader.kingdom != pKingdom) continue;
                int score = ActorTool.attributeDice(leader);
                if (best != null && score <= bestScore) continue;
                best = leader;
                bestScore = score;
            }
            return best;
        }

        public static bool ShouldUseOrdinaryFallbackSuccession(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, out long lineage, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID, out long shi, -1L);
            bool hasLegitimateDynasty = lineage >= 0 || shi >= 0;
            return MandateSuccessionRules.ShouldUseOrdinaryClanFallbackAfterCollateralSearch(
                hasDirectSon: false,
                hasRegisteredHeir: false,
                hasCollateralRestorationCandidate: false,
                isMandateOrLegitimateDynasty: hasLegitimateDynasty);
        }

        public static void MarkLeaderFallbackSuccession(Kingdom pKingdom, Actor pLeader)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE,
                pLeader?.data == null ? SuccessionMode.NONE : SuccessionMode.LEADER_FALLBACK);
        }

        public static void MarkClanFallbackSuccession(Kingdom pKingdom, Actor pRuler)
        {
            if (pKingdom?.data == null) return;
            ClearHeir(pKingdom);
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE,
                pRuler?.data == null ? SuccessionMode.NONE : SuccessionMode.CLAN_FALLBACK);
        }

        private static Actor StoreHeirSelection(Kingdom pKingdom,
            HeirSelection pSelection)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                out long previousHeirId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE,
                out string previousMode, SuccessionMode.NONE);
            Actor previousHeir = previousHeirId < 0
                ? null
                : World.world?.units?.get(previousHeirId);
            Actor heir = pSelection.Actor;
            long heirId = heir?.data?.id ?? -1L;
            string mode = heir?.data == null
                ? SuccessionMode.NONE
                : pSelection.Mode;
            long referenceKingId = ResolveReferenceKingId(pKingdom,
                pKingdom.king);
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_RELATION_ACTOR_ID,
                out long signedHeirId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_RELATION_KING_ID,
                out long signedKingId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_SELECTION_DIRTY,
                out bool successionDirty, false);
            bool currentActorAvailable = previousHeirId < 0L ||
                previousHeir?.data != null && previousHeir.isAlive();
            if (currentActorAvailable && signedHeirId == previousHeirId &&
                HeirSelectionSignatureRules.IsUnchanged(previousHeirId,
                    previousMode, signedKingId, successionDirty, heirId,
                    mode, referenceKingId))
                return previousHeir;

            ClearOldHeirFlag(pKingdom);
            FormerHeirService.ClearSnapshot(heir);
            RoyalAsylumService.RecallForSuccession(heir, pKingdom);
            RecallForeignSelectedHeir(pKingdom, heir);
            if (heir?.data != null &&
                !RoyalGuardService.ReleaseForRegisteredHeir(pKingdom,
                    heir, "became_heir"))
            {
                heir = null;
                pSelection = new HeirSelection(null, SuccessionMode.NONE);
            }
            if (heir?.data != null)
                LineageService.EnsureRoyalHeirLineage(pKingdom, heir);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, heir?.data?.id ?? -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE,
                heir?.data == null ? SuccessionMode.NONE : pSelection.Mode);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_RELATION_ACTOR_ID, heir?.data?.id ?? -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_RELATION_KING_ID,
                heir?.data == null ? -1L : referenceKingId);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_SELECTION_DIRTY, false);
            HeirMinimapMarkerIndex.Refresh(pKingdom);
            InheritanceLawService.MirrorCandidate(pKingdom, heir,
                pSelection.Mode, referenceKingId);
            SetHeirFlag(heir, true);
            if (heir?.data != null) CourtService.EnsurePersonalSchool(heir);
            if (heir?.data != null && heir.data.id != previousHeirId)
                ChronicleEvents.OnHeirDesignated(pKingdom, pKingdom.king,
                    heir, pSelection.Mode);
            CitySchoolSnapshotService.MarkKingdomDirty(pKingdom);
            RoyalMedicalCareService.ReconcileTargets(pKingdom);
            if (previousHeir?.data != null && previousHeir != heir)
                LineageService.ArchiveActor(previousHeir,
                    pAlive: previousHeir.isAlive());
            if (heir?.data != null)
                LineageService.ArchiveActor(heir, pAlive: heir.isAlive());
            heirId = heir?.data?.id ?? -1L;
            mode = heir?.data == null ? SuccessionMode.NONE : pSelection.Mode;
            if (previousHeirId != heirId || previousMode != mode)
                FamilyTreeProjectionRevision.Advance(
                    FamilyTreeProjectionChange.Heir);
            return heir;
        }

        private static HeirSelection SelectByEffectiveLaw(Kingdom pKingdom,
            Actor pKnownKing, long pReferenceKingId,
            bool pIncludeRegisteredHeir)
        {
            InheritanceLaw law = InheritanceLawService.GetEffectiveLaw(
                pKingdom);
            if (law == InheritanceLaw.Primogeniture)
            {
                HeirSelection hereditary = FindHeir(pKingdom, pKnownKing,
                    pReferenceKingId, pIncludeRegisteredHeir);
                if (hereditary.Actor?.data != null) return hereditary;
            }
            else
            {
                InheritanceCandidateSelection selected =
                    InheritanceCandidateService.SelectCandidate(pKingdom,
                        law, pKnownKing);
                if (selected?.Actor?.data != null)
                    return new HeirSelection(selected.Actor,
                        InheritanceLawService.ModeForLaw(law));
            }
            return SelectAlternativeFaction(pKingdom, pKnownKing,
                pReferenceKingId, pIncludeRegisteredHeir, law);
        }

        private static HeirSelection SelectAlternativeFaction(
            Kingdom pKingdom, Actor pKnownKing, long pReferenceKingId,
            bool pIncludeRegisteredHeir, InheritanceLaw pUnavailableLaw)
        {
            HeirSelection hereditary = pUnavailableLaw ==
                                         InheritanceLaw.Primogeniture
                ? new HeirSelection(null, SuccessionMode.NONE)
                : FindHeir(pKingdom, pKnownKing, pReferenceKingId,
                    pIncludeRegisteredHeir);
            pKingdom.data.get(LineageKeys.INHERITANCE_MILITARY_UNLOCKED,
                out bool militaryUnlocked, false);
            pKingdom.data.get(LineageKeys.INHERITANCE_CIVIL_UNLOCKED,
                out bool civilUnlocked, false);
            InheritanceCandidateSelection military = militaryUnlocked &&
                                                       pUnavailableLaw !=
                                                       InheritanceLaw.MilitaryAcclaim
                ? InheritanceCandidateService.SelectCandidate(pKingdom,
                    InheritanceLaw.MilitaryAcclaim, pKnownKing)
                : null;
            InheritanceCandidateSelection civil = civilUnlocked &&
                                                   pUnavailableLaw !=
                                                   InheritanceLaw.CivilAcclaim
                ? InheritanceCandidateService.SelectCandidate(pKingdom,
                    InheritanceLaw.CivilAcclaim, pKnownKing)
                : null;
            pKingdom.data.get(LineageKeys.INHERITANCE_SCORE_PRIMOGENITURE,
                out int hereditaryScore, 0);
            pKingdom.data.get(LineageKeys.INHERITANCE_SCORE_MILITARY,
                out int militaryScore, 0);
            pKingdom.data.get(LineageKeys.INHERITANCE_SCORE_CIVIL,
                out int civilScore, 0);
            InheritanceLaw fallback = InheritanceLawRules.ResolveAvailableLaw(
                pUnavailableLaw, hereditary.Actor?.data != null,
                hereditaryScore, military?.Actor?.data != null,
                militaryScore, civil?.Actor?.data != null, civilScore);
            if (fallback == InheritanceLaw.Primogeniture &&
                hereditary.Actor?.data != null)
            {
                InheritanceLawService.SetTemporaryEffective(pKingdom,
                    fallback);
                return hereditary;
            }
            InheritanceCandidateSelection selected = fallback ==
                                                       InheritanceLaw.MilitaryAcclaim
                ? military
                : fallback == InheritanceLaw.CivilAcclaim ? civil : null;
            if (selected?.Actor?.data == null)
                return new HeirSelection(null, SuccessionMode.NONE);
            InheritanceLawService.SetTemporaryEffective(pKingdom, fallback);
            return new HeirSelection(selected.Actor,
                InheritanceLawService.ModeForLaw(fallback));
        }

        private static void RecallForeignSelectedHeir(Kingdom pKingdom, Actor pHeir)
        {
            if (pKingdom?.data == null || pHeir?.data == null) return;
            City capital = pKingdom.capital;
            if (!HeirRecallRules.ShouldRecallForeignSelectedHeir(
                    pHasHeir: true,
                    pSameKingdom: pHeir.kingdom == pKingdom,
                    pHasCapital: capital?.data != null))
                return;

            try { if (pHeir.hasArmy()) pHeir.removeFromArmy(); } catch { }
            try { pHeir.joinCity(capital); } catch { }
            try { pHeir.clearGraphicsFully(); } catch { }
        }

        private static bool IsCityLeaderOfAnyCity(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return false;
            try { if (pActor.isCityLeader()) return true; } catch { }
            foreach (City city in pKingdom.getCities())
            {
                if (city?.leader == pActor) return true;
            }
            return false;
        }

        private static void RemoveCityLeaderOffice(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            try
            {
                if (pActor.city?.leader == pActor)
                    pActor.city.removeLeader();
            }
            catch { }

            foreach (City city in pKingdom.getCities())
            {
                if (city?.leader != pActor) continue;
                try { city.removeLeader(); }
                catch { }
            }
        }

        private static bool IsArmyCaptain(Actor pActor)
        {
            if (pActor?.data == null) return false;
            try { if (pActor.isArmyGroupLeader()) return true; } catch { }
            try { return pActor.hasArmy() && pActor.army?.getCaptain() == pActor; }
            catch { return false; }
        }

        private static void SetHeirFlag(Actor pActor, bool pValue)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.IS_HEIR, out bool oldValue, false);
            if (oldValue == pValue) return;
            pActor.data.set(LineageKeys.IS_HEIR, pValue);
            pActor.clearGraphicsFully();
        }

        /// <summary>
        ///     按氏族大谱 + 辈分就近选继承人:直系后裔(子→孙→曾孙…由近及远) → 同辈 → 旁系过继;
        ///     严禁辈分高于国王者,严禁非本姓男系(不改氏硬塞);同辈分内嫡长优先、再论成年。
        ///     全无合法男系 → NONE(绝嗣/亡国)。pIncludeRegisteredHeir 已废弃(取消登记继承人机制)。
        /// </summary>
        private static HeirSelection FindHeir(Kingdom pKingdom, Actor pKnownKing, long pReferenceKingId,
            bool pIncludeRegisteredHeir)
        {
            if (pKingdom?.data == null) return new HeirSelection(null, SuccessionMode.NONE);
            Actor king = pKnownKing ?? pKingdom.king;
            EnsureLegitimateLine(pKingdom, king);
            long kingId = pReferenceKingId >= 0 ? pReferenceKingId : ResolveReferenceKingId(pKingdom, king);
            if (kingId < 0) return new HeirSelection(null, SuccessionMode.NONE);

            Actor directSon = PickEldestLivingSon(king, kingId);
            if (directSon?.data != null)
                return new HeirSelection(directSon,
                    directSon.isAdult() ? SuccessionMode.DIRECT : SuccessionMode.UNDERAGE_DIRECT);

            Actor directDescendant = null;
            int directDescendantDelta = 0;
            double directDescendantBirth = 0;
            bool directDescendantAdult = false;
            Actor collateral = null;
            int collateralTier = HeirGenerationRules.TierIneligible;
            int collateralDelta = 0;
            double collateralBirth = 0;
            bool collateralAdult = false;

            foreach (Actor cand in CollectSuccessionCandidatePool(pKingdom,
                         king, kingId))
            {
                if (!IsHeirBaseEligible(cand, pKingdom, king)) continue;
                long candId = cand.data.id;
                if (candId == kingId) continue;

                // 同源判定:与国王的最近共同父系祖先(纯 parent 记录,不看 LINEAGE_ID)。
                // 姓氏合流后姓辨识失效,故按"同源"而非"同姓"判亲缘;无共同父系祖先 = 非同源 → 排除。
                long anc = SuccessionRelationshipIndex.
                    NearestCommonAgnaticAncestor(kingId, candId,
                    out int kingDepth, out int candDepth);
                if (anc < 0) continue;                 // 非同源 → 严禁入选(不会抓不相干的人)

                bool isDesc = kingDepth == 0;          // 共同祖先即国王本人 → 国王男系后裔
                int delta = candDepth - kingDepth;     // >0 晚辈 / 0 同辈 / <0 长辈
                int tier = HeirGenerationRules.ClassifyTier(isDesc, delta);

                double birth = SafeCreatedTime(cand);
                bool adult = SafeIsAdult(cand);
                if (isDesc)
                {
                    if (directDescendant == null ||
                        HeirGenerationRules.Compare(tier, delta, birth, adult,
                            HeirGenerationRules.TierDirectDescendant,
                            directDescendantDelta, directDescendantBirth,
                            directDescendantAdult) < 0)
                    {
                        directDescendant = cand;
                        directDescendantDelta = delta;
                        directDescendantBirth = birth;
                        directDescendantAdult = adult;
                    }
                    continue;
                }

                if (collateral == null || HeirGenerationRules.Compare(
                        tier, delta, birth, adult, collateralTier,
                        collateralDelta, collateralBirth, collateralAdult) < 0)
                {
                    collateral = cand;
                    collateralTier = tier;
                    collateralDelta = delta;
                    collateralBirth = birth;
                    collateralAdult = adult;
                }
            }

            if (directDescendant != null)
                return new HeirSelection(directDescendant,
                    directDescendantAdult ? SuccessionMode.DIRECT :
                    SuccessionMode.UNDERAGE_DIRECT);

            Actor fullBrother = PickEldestEligibleFullBrother(pKingdom, king,
                kingId);
            if (fullBrother != null)
                return new HeirSelection(fullBrother,
                    SuccessionMode.COLLATERAL_RESTORE);

            if (collateral == null)
                return new HeirSelection(null, SuccessionMode.NONE); // 真·绝嗣/亡国

            return new HeirSelection(collateral,
                SuccessionMode.COLLATERAL_RESTORE);
        }

        private static Actor PickEldestEligibleFullBrother(Kingdom pKingdom,
            Actor pKing, long pKingId)
        {
            if (pKingdom?.data == null || pKingId < 0L ||
                !TryGetParentPair(pKingId, out long parentA,
                    out long parentB)) return null;

            var actors = new Dictionary<long, Actor>();
            var candidates = new List<HeirFullBrotherCandidate>();
            foreach (long siblingId in CollectFullSiblingIds(parentA, parentB))
            {
                if (siblingId < 0L || siblingId == pKingId) continue;
                Actor sibling = World.world?.units?.get(siblingId);
                if (sibling?.data == null ||
                    actors.ContainsKey(sibling.data.id)) continue;
                bool sharesBothParents = HasSameParentPair(sibling.data.id,
                    parentA, parentB);
                bool eligible = sharesBothParents &&
                    sibling.kingdom == pKingdom && SafeIsAdult(sibling) &&
                    IsHeirBaseEligible(sibling, pKingdom, pKing);
                actors[sibling.data.id] = sibling;
                candidates.Add(new HeirFullBrotherCandidate(sibling.data.id,
                    eligible, sharesBothParents, SafeCreatedTime(sibling)));
            }

            long selectedId = HeirFullBrotherRules.SelectEldestEligibleId(
                candidates);
            return selectedId >= 0L && actors.TryGetValue(selectedId,
                out Actor selected) ? selected : null;
        }

        private static List<long> CollectFullSiblingIds(long pParentA,
            long pParentB)
        {
            var siblingIds = new HashSet<long>();
            try
            {
                foreach (long childId in SuccessionRelationshipIndex.
                             GetChildIds(pParentA))
                    if (childId >= 0L) siblingIds.Add(childId);
                foreach (long childId in SuccessionRelationshipIndex.
                             GetChildIds(pParentB))
                    if (childId >= 0L) siblingIds.Add(childId);
            }
            catch { }
            return new List<long>(siblingIds);
        }

        private static bool HasSameParentPair(long pActorId, long pParentA,
            long pParentB)
        {
            return TryGetParentPair(pActorId, out long candidateParentA,
                out long candidateParentB) &&
                   ((candidateParentA == pParentA &&
                     candidateParentB == pParentB) ||
                    (candidateParentA == pParentB &&
                     candidateParentB == pParentA));
        }

        private static bool TryGetParentPair(long pActorId, out long pParentA,
            out long pParentB)
        {
            pParentA = -1L;
            pParentB = -1L;
            try
            {
                foreach (long parentId in SuccessionRelationshipIndex.
                             GetParentIds(pActorId))
                {
                    if (parentId < 0L || parentId == pParentA ||
                        parentId == pParentB) continue;
                    if (pParentA < 0L)
                        pParentA = parentId;
                    else if (pParentB < 0L)
                        pParentB = parentId;
                    else
                        return false;
                }
            }
            catch { return false; }
            return pParentA >= 0L && pParentB >= 0L;
        }

        /// <summary>取国王在世男嗣中的嫡长(出生最早),排除疯癫/奴隶/已为国王者。</summary>
        private static Actor PickEldestLivingSon(Actor pKing,
            long pReferenceKingId = -1L)
        {
            long kingId = pKing?.data?.id ?? pReferenceKingId;
            if (kingId < 0) return null;
            var actors = new Dictionary<long, Actor>();
            var candidates = new List<HeirDirectSonCandidate>();
            IReadOnlyList<long> indexedChildren =
                SuccessionRelationshipIndex.GetChildIds(kingId);
            for (int i = 0; i < indexedChildren.Count; i++)
                AddDirectSonCandidate(World.world?.units?.get(indexedChildren[i]),
                    pKing, actors, candidates);

            if (indexedChildren.Count > 0)
            {
                long indexedSelectedId =
                    HeirDirectSonRules.SelectEldestEligibleId(candidates);
                return indexedSelectedId >= 0 && actors.TryGetValue(
                    indexedSelectedId, out Actor indexedSelected)
                    ? indexedSelected
                    : null;
            }

            try
            {
                foreach (Actor child in pKing.getChildren(false))
                    AddDirectSonCandidate(child, pKing, actors, candidates);
            }
            catch { }

            long selectedId = HeirDirectSonRules.SelectEldestEligibleId(candidates);
            return selectedId >= 0 && actors.TryGetValue(selectedId, out Actor selected) ? selected : null;
        }

        private static void AddDirectSonCandidate(Actor pChild, Actor pKing,
            IDictionary<long, Actor> pActors,
            ICollection<HeirDirectSonCandidate> pCandidates)
        {
            if (pChild?.data == null || pChild == pKing ||
                pActors.ContainsKey(pChild.data.id)) return;
            bool eligible = pChild.isSexMale() && !pChild.isRekt() &&
                            pChild.isAlive() && !pChild.isKing() &&
                            !pChild.hasTrait("madness") &&
                            !SlaveService.IsSlave(pChild);
            pActors[pChild.data.id] = pChild;
            pChild.data.get(LineageKeys.BIRTH_LEGITIMACY,
                out bool legitimateBirth, true);
            pCandidates.Add(new HeirDirectSonCandidate(pChild.data.id,
                eligible, SafeCreatedTime(pChild), SafeIsAdult(pChild),
                legitimateBirth));
        }

        private static bool IsHeirBaseEligible(Actor pActor, Kingdom pKingdom, Actor pKing)
        {
            if (pActor?.data == null || pActor == pKing) return false;
            if (!LineageService.IsXia(pActor) && !LineageService.UsesAwLineageSystem(pActor)) return false;
            if (!pActor.isSexMale()) return false;
            if (pActor.isRekt() || !pActor.isAlive()) return false;
            if (!SuccessionTransitionRules.IsOfficialRoleEligible(
                    pActor.isKing(), pIsCityLeader: false, pIsGeneral: false,
                    pIsArmyCaptain: false, pHasFief: false))
                return false;
            if (pActor.hasTrait("madness")) return false;
            if (SlaveService.IsSlave(pActor)) return false;
            // 亲缘不再用 LINEAGE_ID(合流后失效)判定,改由调用方按"最近共同父系祖先(同源)"筛选。
            return true;
        }

        private static bool IsRegisteredCandidateEligible(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor.kingdom != pKingdom) return false;
            return IsHeirBaseEligible(pActor, pKingdom, pKingdom.king);
        }

        private static double SafeCreatedTime(Actor pActor)
        {
            try { return pActor?.data?.created_time ?? double.MaxValue; }
            catch { return double.MaxValue; }
        }

        private static bool SafeIsAdult(Actor pActor)
        {
            try { return pActor?.data != null && pActor.isAdult(); }
            catch { return false; }
        }

        private static Actor FindHeir(Kingdom pKingdom, Actor pKnownKing = null)
        {
            Actor king = pKnownKing ?? pKingdom.king;
            long royalClanId = pKingdom.data.royal_clan_id;

            if (king != null)
            {
                Actor directChildHeir = FindDirectChildHeir(king);
                if (directChildHeir != null) return directChildHeir;
            }

            // fallback:royal_clan 鎴愬憳(鎸墊age-18|閫?銆?            royalClanId = pKingdom.data.royal_clan_id;
            if (royalClanId < 0) return null;
            var clan = World.world.clans.get(royalClanId);
            if (clan == null) return null;

            return PickClosest(new List<Actor>(clan.units), king, pPreferMale: false);
        }

        private static Actor FindAdultDirectSon(Actor pKing)
        {
            return pKing?.data == null ? null : PickEldestStable(pKing.getChildren(false), pKing, pMaleOnly: true);
        }

        private static Actor GetRegisteredHeirIfSuitable(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long curId, -1L);
            if (curId < 0) return null;
            Actor current = World.world.units.get(curId);
            return IsSuitableRegisteredHeir(current, pKingdom, pKing) ? current : null;
        }

        private static Actor FindRoyalClanFallbackHeir(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null) return null;
            long royalClanId = pKingdom.data.royal_clan_id;
            if (royalClanId < 0) return null;
            var clan = World.world.clans.get(royalClanId);
            if (clan == null) return null;

            var candidates = new List<Actor>();
            foreach (Actor member in clan.units)
            {
                if (!HeirCandidateRules.IsFallbackEligibleCore(
                        isSuitable: IsSuitableHeir(member, pKing),
                        sameKingdom: member?.kingdom == pKingdom,
                        hasLineage: HasLineage(member),
                        hasShi: HasShi(member)))
                    continue;
                candidates.Add(member);
            }
            return PickClosest(candidates, pKing, pPreferMale: false);
        }

        private static Actor FindCollateralRestorationHeir(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, out long legitimateLineage, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID, out long legitimateShi, -1L);
            if (legitimateLineage < 0) return null;

            List<Actor> pool = CollectSuccessionCandidatePool(pKingdom);

            // 男系(同姓父系)优先:氏/分支可不同,但父系必须一路同姓。先成年,再未成年。
            Actor agnaticAdult = PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: true, pRequireAgnatic: true);
            if (agnaticAdult != null) return agnaticAdult;

            Actor agnaticUnderage = PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: false, pRequireAgnatic: true);
            if (agnaticUnderage != null) return agnaticUnderage;

            // 无男系同姓后裔 → 回退(非男系,后续 ApplyCollateralRestoration 会标记为异姓入继)。
            Actor exactAdult = PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: true, pRequireAdult: true, pRequireAgnatic: false);
            if (exactAdult != null) return exactAdult;

            Actor traceableBranchAdult = PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: true, pRequireAgnatic: false);
            if (traceableBranchAdult != null) return traceableBranchAdult;

            Actor exactUnderage = PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: true, pRequireAdult: false, pRequireAgnatic: false);
            if (exactUnderage != null) return exactUnderage;

            return PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: false, pRequireAgnatic: false);
        }

        private static List<Actor> CollectSuccessionCandidatePool(Kingdom pKingdom,
            Actor pReferenceKing = null, long pReferenceKingId = -1L)
        {
            return InheritanceCandidateService.CollectRoyalCandidates(
                pKingdom, pReferenceKing ?? pKingdom?.king,
                pReferenceKingId);
        }

        private static Actor PickCollateralCandidate(List<Actor> pCandidates, Kingdom pKingdom, Actor pKing,
            long pLegitimateLineage, long pLegitimateShi, bool pRequireLegitimateShi, bool pRequireAdult,
            bool pRequireAgnatic)
        {
            Actor best = null;
            int bestScore = int.MaxValue;
            foreach (Actor candidate in pCandidates)
            {
                if (!IsCollateralCandidate(candidate, pKingdom, pKing, pLegitimateLineage, pLegitimateShi,
                        pRequireLegitimateShi, pRequireAdult, pRequireAgnatic))
                    continue;

                int score = CollateralCandidateScore(candidate, pKing, pLegitimateShi);
                if (best != null && score >= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        private static bool IsCollateralCandidate(Actor pActor, Kingdom pKingdom, Actor pKing,
            long pLegitimateLineage, long pLegitimateShi, bool pRequireLegitimateShi, bool pRequireAdult,
            bool pRequireAgnatic)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor == pKing) return false;
            if (pActor.kingdom != pKingdom) return false;
            if (!LineageService.IsXia(pActor) && !LineageService.UsesAwLineageSystem(pActor)) return false;
            if (!pActor.isSexMale()) return false;
            if (pActor.isRekt() || !pActor.isAlive()) return false;
            if (pActor.isKing()) return false;
            if (pActor.hasTrait("madness")) return false;
            if (SlaveService.IsSlave(pActor)) return false;
            if (pRequireAdult && !pActor.isAdult()) return false;
            if (!pRequireAdult && pActor.isAdult()) return false;

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineage, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long shi, -1L);
            if (lineage != pLegitimateLineage) return false;

            bool agnatic = SuccessionRelationshipIndex.IsAgnaticDescendant(
                pActor.data.id, pLegitimateLineage);
            if (pRequireAgnatic)
                // 男系同姓即合格,氏(分支)可不同。成年/未成年已由上面的 pRequireAdult 分档,这里传 isAdult:true。
                return MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                    isXia: true, isMale: true, isAlive: true, isAdult: true, isKing: false,
                    hasMadness: false, sameLineage: true, belongsToLegitimateShi: true,
                    canTraceToLegitimateBranch: true, requireAgnatic: true, isAgnaticLineDescendant: agnatic);

            bool canRestore = CollateralRestorationTraceService.CanRestoreToLegitimateShi(
                pActor, pLegitimateLineage, pLegitimateShi);
            if (pRequireLegitimateShi)
            {
                if (shi != pLegitimateShi) return false;
                if (!pRequireAdult) return true;
                return MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                    isXia: true,
                    isMale: true,
                    isAlive: true,
                    isAdult: pActor.isAdult(),
                    isKing: false,
                    hasMadness: false,
                    sameLineage: true,
                    belongsToLegitimateShi: true);
            }
            return MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                isXia: true,
                isMale: true,
                isAlive: true,
                isAdult: pActor.isAdult(),
                isKing: false,
                hasMadness: false,
                sameLineage: true,
                belongsToLegitimateShi: false,
                canTraceToLegitimateBranch: canRestore);
        }

        private static int CollateralCandidateScore(Actor pActor, Actor pKing, long pLegitimateShi)
        {
            int kin = KinDistance(pActor, pKing);
            int ageScore = 500;
            try
            {
                int age = pActor.getAge();
                ageScore = pActor.isAdult() ? System.Math.Abs(age - 30) : 40 - age;
            }
            catch { }

            pActor.data.get(LineageKeys.SHI_ID, out long shi, -1L);
            int shiPenalty = shi == pLegitimateShi ? 0 : 2500;
            int ability = 0;
            try { ability = ActorTool.attributeDice(pActor); } catch { }
            return kin * 100000 + shiPenalty + ageScore * 100 - ability;
        }

        private static int KinDistance(Actor pActor, Actor pKing)
        {
            if (pActor?.data == null || pKing?.data == null) return 99;
            long target = pActor.data.id;
            long start = pKing.data.id;
            if (target == start) return 0;

            var queue = new Queue<long>();
            var distance = new Dictionary<long, int>();
            queue.Enqueue(start);
            distance[start] = 0;

            while (queue.Count > 0)
            {
                long current = queue.Dequeue();
                int d = distance[current];
                if (d >= 6) continue;
                foreach (long next in KinNeighborIds(current))
                {
                    if (next < 0 || distance.ContainsKey(next)) continue;
                    int nd = d + 1;
                    if (next == target) return nd;
                    distance[next] = nd;
                    queue.Enqueue(next);
                }
            }
            return 99;
        }

        private static IEnumerable<long> KinNeighborIds(long pActorId)
        {
            foreach (long parent in SuccessionRelationshipIndex.
                         GetParentIds(pActorId))
                if (parent >= 0) yield return parent;
            foreach (long child in SuccessionRelationshipIndex.
                         GetChildIds(pActorId))
                if (child >= 0) yield return child;
        }

        private static Actor FindDirectChildHeir(Actor pKing)
        {
            if (pKing?.data == null) return null;

            // 鍎垮瓙浼樺厛:闀垮瓙(created_time 鏈€灏忕殑鍚堟牸鎴愬勾鍎垮瓙)
            Actor eldest = PickEldestStable(pKing.getChildren(false), pKing, pMaleOnly: true);
            if (eldest != null) return eldest;

            Actor underageSon = PickEldestUnderageDirectSon(pKing);
            if (underageSon != null) return underageSon;

            // 鏃犲悎鏍煎効瀛?闀垮コ
            return null;
        }

        private static Actor FindFather(Actor pParent1, Actor pParent2)
        {
            if (pParent1?.data != null && pParent1.isSexMale()) return pParent1;
            if (pParent2?.data != null && pParent2.isSexMale()) return pParent2;
            return null;
        }

        private static bool IsDirectChildOf(Actor pActor, Actor pParent)
        {
            if (pActor?.data == null || pParent?.data == null) return false;
            long parentId = pParent.data.id;
            return pActor.data.parent_id_1 == parentId || pActor.data.parent_id_2 == parentId;
        }

        private static Actor PickEldestUnderageDirectSon(Actor pKing)
        {
            if (pKing?.data == null) return null;
            Actor eldest = null;
            double earliestTime = double.MaxValue;
            foreach (Actor child in pKing.getChildren(false))
            {
                if (!IsUnderageDirectSonFallback(child, pKing, pHasAdultDirectSon: false)) continue;
                if (child.data.created_time < earliestTime)
                {
                    earliestTime = child.data.created_time;
                    eldest = child;
                }
            }
            return eldest;
        }

        /// <summary>浠庣洿绯诲瓙濂充腑閫夐暱瀛?闀垮コ(created_time鏈€灏?鏈€鏃╁嚭鐢?銆俻MaleOnly=true 鍙€夊効瀛愩€?/summary>
        private static Actor PickEldest(System.Collections.Generic.IEnumerable<Actor> pCandidates,
            Actor pKing, bool pMaleOnly)
        {
            Actor eldest = null;
            double earliestTime = double.MaxValue;
            foreach (var c in pCandidates)
            {
                if (!IsSuitableHeir(c, pKing)) continue;
                if (pMaleOnly && !c.isSexMale()) continue;
                if (!pMaleOnly && c.isSexMale()) continue; // 濂冲効杞椂璺宠繃鐢?                if (c.data.created_time < earliestTime)
                {
                    earliestTime = c.data.created_time;
                    eldest = c;
                }
            }
            return eldest;
        }

        /// <summary>浠庡€欓€夐噷鎸?鍚堟牸(娲烩埀闈炵帇鈭ф垚骞粹埀闈炵柉)涓?|age-18| 鏈€灏?pPreferMale 鏃剁敺鎬ц幏 -1000 鍋忕疆(蹇呬紭鍏堜簬濂虫€?銆?/summary>
        private static Actor PickEldestStable(System.Collections.Generic.IEnumerable<Actor> pCandidates,
            Actor pKing, bool pMaleOnly)
        {
            Actor eldest = null;
            double earliestTime = double.MaxValue;
            foreach (Actor candidate in pCandidates)
            {
                if (!IsSuitableHeir(candidate, pKing)) continue;
                if (pMaleOnly && !candidate.isSexMale()) continue;
                if (!pMaleOnly && candidate.isSexMale()) continue;
                if (candidate.data.created_time >= earliestTime) continue;
                earliestTime = candidate.data.created_time;
                eldest = candidate;
            }
            return eldest;
        }

        private static Actor PickClosest(System.Collections.Generic.IEnumerable<Actor> pCandidates, Actor pKing, bool pPreferMale)
        {
            Actor best = null;
            int bestScore = int.MaxValue;
            foreach (var member in pCandidates)
            {
                if (!IsSuitableHeir(member, pKing)) continue;
                int score = System.Math.Abs(member.getAge() - 18);
                if (pPreferMale && member.isSexMale()) score -= 1000; // 鐢锋€т紭鍏?鎴愬勾鍎垮瓙浼樺厛浜庡コ鍎?
                if (score < bestScore)
                {
                    bestScore = score;
                    best = member;
                }
            }
            return best;
        }

        /// <summary>缁ф壙浜鸿祫鏍?娲荤潃鈭ч潪鐜颁换鐜嬧埀鎴愬勾鈭ч潪鐤媯銆?/summary>
        private static bool IsSuitableHeir(Actor pActor, Actor pKing)
        {
            if (!RoyalGuardOfficeRules.CanBecomeSuccessionCandidate(
                    RoyalGuardService.IsRoyalGuard(pActor))) return false;
            return HeirCandidateRules.IsBasicMaleSuccessionEligible(
                isAlive: pActor != null && !pActor.isRekt() && pActor.isAlive(),
                sameAsCurrentKing: pActor == pKing,
                isMale: pActor?.data != null && pActor.isSexMale(),
                isCurrentKing: pActor?.data != null && pActor.isKing(),
                isAdult: pActor?.data != null && pActor.isAdult(),
                hasMadness: pActor?.data != null && pActor.hasTrait("madness"),
                isSlave: pActor?.data != null && SlaveService.IsSlave(pActor));
        }

        private static bool HasLineage(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineage, -1L);
            return lineage >= 0;
        }

        private static bool HasShi(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.SHI_ID, out long shi, -1L);
            return shi >= 0;
        }

        private static bool IsSuitableRegisteredHeir(Actor pActor, Kingdom pKingdom, Actor pKing)
        {
            if (pActor == null || pActor.isRekt()) return false;
            if (pKingdom?.data == null || pActor.kingdom != pKingdom) return false;
            if (!pActor.isSexMale()) return false;
            if (pActor.isKing() || pActor == pKing) return false;
            if (pActor.hasTrait("madness")) return false;
            if (SlaveService.IsSlave(pActor)) return false;

            bool direct = pKing != null && IsDirectChildOf(pActor, pKing);
            if (pActor.isAdult())
            {
                if (direct) return true;
                pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID, out long legitimateShi, -1L);
                return CollateralRestorationTraceService.BelongsToLegitimateShi(pActor, legitimateShi);
            }

            bool hasAdultDirectSon = pKing != null &&
                PickEldestStable(pKing.getChildren(false), pKing, pMaleOnly: true) != null;
            return MandateSuccessionRules.CanUseUnderageDirectSonFallback(
                direct,
                pActor.isSexMale(),
                !pActor.isRekt(),
                pActor.isKing() || pActor == pKing,
                hasAdultDirectSon);
        }

        private static bool IsUnderageDirectSonFallback(Actor pActor, Actor pKing, bool pHasAdultDirectSon)
        {
            if (pActor == null || pActor.isRekt() || pActor.isAdult()) return false;
            return HeirCandidateRules.IsUnderageDirectSonEligible(
                isDirectSon: IsDirectChildOf(pActor, pKing),
                isMale: pActor.isSexMale(),
                isAlive: pActor.isAlive(),
                isCurrentKing: pActor.isKing() || pActor == pKing,
                hasAdultDirectSon: pHasAdultDirectSon,
                hasMadness: pActor.hasTrait("madness"),
                isSlave: SlaveService.IsSlave(pActor));
        }
    }
}
