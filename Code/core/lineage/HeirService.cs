using System.Collections.Generic;
using ai;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;

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
            pKing.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pKing.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_PRE_SUCCESSION_GENERATION,
                pKing.data.generation);
            pKingdom.data.set(LineageKeys.KINGDOM_PRE_SUCCESSION_LINEAGE_ID,
                lineageId);
            pKingdom.data.set(LineageKeys.KINGDOM_PRE_SUCCESSION_SHI_ID,
                shiId);
            if (lineageId >= 0L)
                pKingdom.data.set(
                    LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, lineageId);
            if (shiId >= 0L)
                pKingdom.data.set(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                    shiId);
        }

        public static void RememberAccessionModeSnapshot(Kingdom pKingdom,
            Actor pCandidate, string pMode)
        {
            if (pKingdom?.data == null || pCandidate?.data == null) return;
            pKingdom.data.set(LineageKeys.KINGDOM_PENDING_ACCESSION_ACTOR_ID,
                pCandidate.data.id);
            pKingdom.data.set(LineageKeys.KINGDOM_PENDING_ACCESSION_MODE,
                string.IsNullOrEmpty(pMode) ? SuccessionMode.NONE : pMode);
            AccessionChronicleRetryService.Track(pKingdom, pCandidate);
        }

        public static void RestoreAccessionModeSnapshotRetry(
            Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            if (TryGetAccessionModeSnapshot(pKingdom, king, out _))
                AccessionChronicleRetryService.Track(pKingdom, king);
        }

        public static bool TryGetAccessionModeSnapshot(Kingdom pKingdom,
            Actor pCandidate, out string pMode)
        {
            pMode = SuccessionMode.NONE;
            if (pKingdom?.data == null || pCandidate?.data == null)
                return false;
            pKingdom.data.get(LineageKeys.KINGDOM_PENDING_ACCESSION_ACTOR_ID,
                out long actorId, -1L);
            if (actorId != pCandidate.data.id) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_PENDING_ACCESSION_MODE,
                out pMode, SuccessionMode.NONE);
            return true;
        }

        public static void CompleteAccessionModeSnapshot(Kingdom pKingdom,
            Actor pCandidate)
        {
            if (pKingdom?.data == null || pCandidate?.data == null) return;
            ClearAccessionModeSnapshot(pKingdom, pCandidate.data.id);
        }

        internal static void ClearAccessionModeSnapshot(Kingdom pKingdom,
            long pActorId)
        {
            if (pKingdom?.data == null || pActorId < 0L) return;
            pKingdom.data.get(LineageKeys.KINGDOM_PENDING_ACCESSION_ACTOR_ID,
                out long actorId, -1L);
            if (actorId != pActorId) return;
            pKingdom.data.set(LineageKeys.KINGDOM_PENDING_ACCESSION_ACTOR_ID,
                -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_PENDING_ACCESSION_MODE,
                SuccessionMode.NONE);
            AccessionChronicleRetryService.Complete(pKingdom, pActorId);
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
        private static void ClearOldHeirFlag(Kingdom pKingdom,
            long pKeepActorId = -1L)
        {
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long oldId, -1L);
            if (oldId < 0) return;
            // 旧继承人就是新继承人(改的只是模式/参照君主):清了马上又要设回去,
            // 白跑一趟全王国扫描,还多两次 clearGraphicsFully。
            if (oldId == pKeepActorId) return;
            Actor old = World.world?.units?.get(oldId);
            if (old?.data == null) return;

            // 他登记的国就是本国 → 不可能有第二处登记,跳过全王国扫描。
            // (HEIR_KINGDOM_ID 是登记时随 IS_HEIR 一起写的;老存档里可能是 -1,
            //  那就还是走扫描。)
            old.data.get(LineageKeys.HEIR_KINGDOM_ID, out long heirKingdomId,
                -1L);
            if (heirKingdomId == pKingdom.id)
            {
                SetHeirFlag(old, false);
                return;
            }

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
            // 两个空位兜底检查：只读 kingdom 字段，不扫描 actor。
            // ① 继承人空缺 → 立刻从顺位池补第一顺位。
            if (PeekRegisteredHeir(pKingdom) == null &&
                !RepublicGovernmentService.IsRepublic(pKingdom) &&
                pKingdom?.data != null &&
                !SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king))
                RefreshHeir(pKingdom);
            // ①b 继承人漂到了别国 → 每年重新归化一次。
            //
            // 册立那一刻归化是成功的(不成会走 register_failed),但**登记之后
            // 再没有任何东西校验过国籍**:ReconcileHeir 只看"在位/成年/签名/脏标",
            // 四条都满足就提前返回,连刷新都不进。于是继承人被联姻、难民潮、
            // 城池易主、游学迁走之后,就一直挂在外国当太子 —— 而太子的名号又是
            // 按所在国算的,顺带连身份也显示不出来。
            //
            // 一年一次,只在真漂了的时候做;归化不成就标脏,让下一次刷新换人
            // (那个人也已经被 LogRegistrationFailure 挡进本年度黑名单)。
            ReconcileHeirNationality(pKingdom);
            // ② 无国王 + 有继承人 → 直接让继承人即位（KingdomBehCheckKing 的补充驱动）。
            if (pKingdom?.king == null || !pKingdom.king.isAlive())
            {
                Actor heir = PeekRegisteredHeir(pKingdom);
                // 走完整的原版即位流程。此前是裸 setKing:继承人不迁都城、
                // 不卸武职,世界日志里也不出现新君即位 —— 王位换了人而玩家
                // 收不到任何提示。册立那一侧的 Prepare 只管归化,不含礼仪。
                if (heir?.data != null && PrepareRegisteredHeirForAccession(pKingdom, heir))
                    KingAccessionCeremonyService.Install(pKingdom, heir,
                        "annual_heir_fallback");
            }
            // 这三段本来只有 RecentFeatureBenchmark,而它受 _sampling 门控 ——
            // 实测 annual_succession 单次 88.22ms 的那一帧不是采样帧,同区间
            // aw3_total_ms 只有 2.484,等于完全漏掉。这里补一层跨帧累计。
            long stamp = KingdomAnnualStepDiagnostics.Mark();
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
                stamp = KingdomAnnualStepDiagnostics.Account(
                    "succession:inheritance_law", stamp);
            }

            benchmark = RecentFeatureBenchmark.Begin();
            try { ReconcileHeir(pKingdom, pForce: lawChanged); }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.KingdomHeirReconcileIndex,
                    benchmark);
                stamp = KingdomAnnualStepDiagnostics.Account(
                    "succession:reconcile_heir", stamp);
            }

            benchmark = RecentFeatureBenchmark.Begin();
            try { SuccessionDisputeService.OnKingdomYear(pKingdom); }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.KingdomSuccessionDisputeIndex,
                    benchmark);
                KingdomAnnualStepDiagnostics.Account("succession:dispute",
                    stamp);
            }
        }

        /// <summary>
        ///     已登记的继承人若不在本国,重新归化他。见 <see cref="OnKingdomYear"/>
        ///     里的调用点说明:这是唯一一处在册立**之后**校验国籍的地方。
        /// </summary>
        private static void ReconcileHeirNationality(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                RepublicGovernmentService.IsRepublic(pKingdom)) return;
            if (SuccessionTransitionRules.IsPending(
                    pKingdom.data.timer_new_king)) return;
            Actor registered = PeekRegisteredHeir(pKingdom);
            if (registered?.data == null ||
                registered.kingdom == pKingdom) return;

            LogHeirDivergence(pKingdom, registered);
            if (NormalizeHeirForRegistration(pKingdom, registered))
            {
                // 归化回来了:名号跟着所继承的国走,顺手把标记补齐。
                SetHeirFlag(registered, true, pKingdom);
                return;
            }
            MarkSelectionDirty(pKingdom);
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
            bool cachedSexEligible = cached?.data != null &&
                                     (RepublicGovernmentService.IsRepublic(
                                          pKingdom) ||
                                      cached.isSexMale() ||
                                      IsSuccessionSexEligible(cached,
                                          pKingdom));
            bool cachedEligible = cachedSexEligible &&
                                  (effectiveLaw == InheritanceLaw.Primogeniture ||
                                   cached.isAdult());
            // 继承人位子空着时立刻从顺位池补上，不等下一个事件触发。
            bool heirVacant = cached?.data == null;
            if (!heirVacant && !HeirDirectSonRules.NeedsEventDrivenRefresh(pForce,
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
            pActor.data.get(LineageKeys.LINEAGE_ID,
                out long actorLineageId, -1L);
            kingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long royalLineageId, -1L);
            if (royalLineageId < 0L)
                king.data.get(LineageKeys.LINEAGE_ID,
                    out royalLineageId, -1L);
            kingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                out long heirId, -1L);
            bool registeredHeir = heirId == pActor.data.id;
            bool directRoyalChild = pActor.data.parent_id_1 == king.data.id ||
                                    pActor.data.parent_id_2 == king.data.id;
            bool lineageReignsInThisKingdom =
                ReigningRoyalLineageIndex.IsRoyalLineageOf(kingdom,
                    actorLineageId);
            if (!RoyalSuccessionEventRules.ShouldMarkSelectionDirty(
                    actorLineageId, royalLineageId,
                    lineageReignsInThisKingdom, registeredHeir,
                    directRoyalChild)) return;
            MarkSelectionDirty(kingdom);
        }

        public static void MarkSelectionDirty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_SELECTION_DIRTY, true);
        }

        public static Actor PeekRegisteredHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            if (heirId < 0) return null;
            Actor heir = World.world?.units?.get(heirId);
            bool isPresent = heir?.data != null;
            bool isAlive = isPresent && heir.isAlive();
            bool sexEligible = RepublicGovernmentService.IsRepublic(
                                   pKingdom) ||
                               heir?.isSexMale() == true ||
                               IsSuccessionSexEligible(heir, pKingdom);
            return AuthoritativeSuccessionRules.IsRegisteredHeirAvailable(
                       isAlive, isPresent) && sexEligible ? heir : null;
        }

        public static Actor PeekStoredHeirForMinimap(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            if (heirId < 0) return null;

            Actor heir = World.world?.units?.get(heirId);
            if (heir?.data == null || !heir.isAlive()) return null;
            if (!RepublicGovernmentService.IsRepublic(pKingdom) &&
                !heir.isSexMale() &&
                !IsSuccessionSexEligible(heir, pKingdom)) return null;
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
            HeirSelection selection = SelectByEffectiveLaw(pKingdom, knownKing,
                referenceKingId, pIncludeRegisteredHeir: false);

            // 这里原来到此为止:算一个候选人交给界面画成"太子",而这个人
            // **从未被登记**。于是 StoreHeirSelection 那一整套都没跑过 ——
            // 不归化(国籍还留在原来的国)、不写 IS_HEIR(身份栏空白)、
            // 连册立失败的探针都不会响。王国窗口的继承人头像走的正是这条
            // (KingdomWindowAddition.cs:1028),所以"看得见太子、其余全没有"。
            //
            // 算出来的人本身是对的,缺的只是登记。所以把**这一次**的选择直接
            // 送进册立,而不是回头再调 RefreshHeir 重算一遍 —— 重算既要重走
            // 一整趟顺位遍历,又可能在 ShouldOverwriteCachedHeir 那里无声折返,
            // 结果和界面上画着的人对不上。
            EnsureRegisteredForReadModel(pKingdom, selection);
            return PeekRegisteredHeir(pKingdom) ?? selection.Actor;
        }

        /// <summary>
        ///     只读路径算出了继承人却没人登记他时,就地补一次正式册立 ——
        ///     归化、IS_HEIR、HEIR_KINGDOM_ID 全在 <see cref="StoreHeirSelection"/> 那条路上。
        ///
        ///     每个王国每年最多一次:这条路径由界面重绘驱动,不能每帧都写。
        ///     先记年份再动手,顺带把重入挡掉(册立过程里若有人又问
        ///     HasSuccessionCandidate,第二次直接返回)。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<long, int>
            ReadModelRegistrations =
                new System.Collections.Generic.Dictionary<long, int>();

        private static void EnsureRegisteredForReadModel(Kingdom pKingdom,
            HeirSelection pSelection)
        {
            if (pKingdom?.data == null || pSelection.Actor?.data == null)
                return;
            int year = SafeCurrentYear();
            if (ReadModelRegistrations.TryGetValue(pKingdom.id,
                    out int lastYear) && lastYear == year) return;
            ReadModelRegistrations[pKingdom.id] = year;
            StoreHeirSelection(pKingdom, pSelection);
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
            if (storedHeir?.data != null)
            {
                NormalizeHeirForRegistration(pKingdom, storedHeir);
                // 老存档里已登记的继承人没有 HEIR_KINGDOM_ID(那时还没这个键),
                // 而 StoreHeirSelection 在「选择没变」时会提前返回、不再走 SetHeirFlag,
                // 光靠它补不上。这里顺手补一次,幂等。
                SetHeirFlag(storedHeir, true, pKingdom);
            }
            EnsureLegitimateLine(pKingdom, knownKing);
            InheritanceLawService.RestorePrimogenitureForDirectSon(pKingdom,
                PickEldestLivingSon(knownKing)?.data != null);
            long referenceKingId = ResolveReferenceKingId(pKingdom, knownKing);
            bool pending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
            if (!SuccessionTransitionRules.ShouldOverwriteCachedHeir(pending, referenceKingId >= 0))
            {
                // 最后一个没有痕迹的决策点:交接中(timer_new_king>0)或者
                // 连参照君主都取不到时,整趟册立会在这里无声折返。
                if (AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                    ModClass.LogInfo("[AW3 HEIR] refresh_skipped kingdom=" +
                        pKingdom.id + " pending=" + pending +
                        " reference_king=" + referenceKingId);
                return PeekRegisteredHeir(pKingdom);
            }

            HeirSelection selection = SelectByEffectiveLaw(pKingdom,
                knownKing, referenceKingId,
                pIncludeRegisteredHeir: false);
            // 「一个都挑不出来」是继承池漏人的唯一可观测症状 —— 池子靠事件维护、
            // 不再定期重建,所以漏接了某个入池事件就长这样。这时才重建一次重试。
            // 同一位参照君主只自愈一次,否则「重建→还是没有→再重建」会变成每次
            // 刷新都重走亲缘遍历,比不缓存还糟。
            if (selection.Actor?.data == null &&
                TryRepairSuccessionPool(pKingdom, referenceKingId))
                selection = SelectByEffectiveLaw(pKingdom, knownKing,
                    referenceKingId, pIncludeRegisteredHeir: false);
            return StoreHeirSelection(pKingdom, selection);
        }

        /// <summary>
        ///     继承池的出错兜底:重建一次并允许重试。
        ///
        ///     节流到「每个王国、每位参照君主、每年一次」。原来是**整朝只做一次**,
        ///     而池子是持续化的:重建之后如果还是选不出人,那次机会就用光了,
        ///     此后即便有宗亲迁回本国、或有成员从别处补进族谱,也没有任何东西会
        ///     再把他放进池子(Insert 只在出生时触发),这个王国就在本朝内永久无嗣。
        ///     一年一次的上限已经足够便宜 —— 它只在「一个都挑不出来」时才触发,
        ///     而那本来就是要重算的情形。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<long,
            (long ReferenceKingId, int Year)> SuccessionPoolRepairs =
            new System.Collections.Generic.Dictionary<long, (long, int)>();

        /// <summary>
        ///     国王父系祖先表,按「王国 + 参照君主」记一份。
        ///
        ///     这张表的内容对全国所有候选人都是同一份,而建一次要沿父系链每代
        ///     GetParentIds(两条 SQL)+ GetActorSex(祖先皆已故,再一条档案读)。
        ///     原来 FindHeir 每次刷新新建一份、IsRecognizedSuccessionCandidate
        ///     每个单位新建一份。
        ///
        ///     参照君主一变(改朝换代)键就不匹配,自动重建;链上都是已故的人,
        ///     亲子边不会再变。串行使用(NearestCommon 内部有复用的 scratch 集合),
        ///     权威周期都在主线程,没有并发问题。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<long,
                (long ReferenceKingId, LineageQuery.AgnaticAncestorDepths
                    Depths)>
            KingAncestries =
                new System.Collections.Generic.Dictionary<long,
                    (long, LineageQuery.AgnaticAncestorDepths)>();

        private static LineageQuery.AgnaticAncestorDepths GetKingAncestry(
            Kingdom pKingdom, long pReferenceKingId)
        {
            if (pKingdom?.data == null || pReferenceKingId < 0L) return null;
            if (KingAncestries.TryGetValue(pKingdom.id,
                    out (long ReferenceKingId,
                        LineageQuery.AgnaticAncestorDepths Depths) entry) &&
                entry.ReferenceKingId == pReferenceKingId &&
                entry.Depths != null) return entry.Depths;

            var depths = new LineageQuery.AgnaticAncestorDepths();
            depths.Reset(pReferenceKingId);
            KingAncestries[pKingdom.id] = (pReferenceKingId, depths);
            return depths;
        }

        internal static void ClearSuccessionPoolRepairs()
        {
            SuccessionPoolRepairs.Clear();
            RegistrationBlocked.Clear();
            ReadModelRegistrations.Clear();
            KingAncestries.Clear();
        }

        private static bool TryRepairSuccessionPool(Kingdom pKingdom,
            long pReferenceKingId)
        {
            if (pKingdom?.data == null) return false;
            int year = SafeCurrentYear();
            if (SuccessionPoolRepairs.TryGetValue(pKingdom.id,
                    out (long ReferenceKingId, int Year) repaired) &&
                repaired.ReferenceKingId == pReferenceKingId &&
                repaired.Year == year) return false;
            SuccessionPoolRepairs[pKingdom.id] = (pReferenceKingId, year);
            SuccessionPoolService.Invalidate(pKingdom);
            return true;
        }

        private static int SafeCurrentYear()
        {
            try { return Date.getYear(World.world.getCurWorldTime()); }
            catch { return 0; }
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
            if (pBaby?.data == null) return;
            Actor father = FindFather(pParent1, pParent2);
            Actor royalParent = pParent1?.isKing() == true ? pParent1 :
                pParent2?.isKing() == true ? pParent2 : father;
            Kingdom kingdom = royalParent?.kingdom ?? father?.kingdom ??
                pParent1?.kingdom ?? pParent2?.kingdom;
            if (kingdom?.data == null) return;
            bool successionSexEligible = IsSuccessionSexEligible(pBaby,
                kingdom);
            if (!successionSexEligible) return;
            bool fatherIsCurrentKing = royalParent?.kingdom == kingdom &&
                (kingdom.king == royalParent || royalParent.isKing());
            // 新子嗣直接插进继承池,而不是让下一次刷新重走一趟亲缘遍历。
            // 顺位由取人时按有效继承法排定,嫡子会落在诸庶兄之前。
            // 带上双亲 id:池子只收池中人(或参照君主)的子女,王孙、王曾孙照样
            // 进得来,与王室无关的新生儿不会一路堆进池子。
            SuccessionPoolService.Insert(kingdom, pBaby,
                pParent1?.data?.id ?? -1L, pParent2?.data?.id ?? -1L);
            if (!RoyalSuccessionBirthRules.ShouldRefreshHeirForNewChild(
                    successionSexEligible, fatherIsCurrentKing))
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
                if (!IsSuccessionSexEligible(child, pKingdom)) continue;
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
            // 走共享的祖先表:静态版每调一次都要新建一个 Dictionary + HashSet,
            // 并把**国王整条父系链**从头走一遍(每代 GetParentIds 两条 SQL +
            // GetActorSex 一条档案读)。而这个方法是逐单位调的 —— 征兵时的免征
            // 判定(TemporaryLevyService:2515)对每个够格的男丁都要问一次。
            // 国王那半边对全国所有人都是同一份,建一次就够。
            LineageQuery.AgnaticAncestorDepths ancestry =
                GetKingAncestry(pKingdom, referenceKingId);
            if (ancestry == null) return false;
            long ancestor = ancestry.NearestCommon(pActor.data.id,
                out int kingDepth, out int candidateDepth);
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
            if (pHeir?.data != null &&
                !RepublicGovernmentService.IsRepublic(pKingdom) &&
                !pHeir.isSexMale() &&
                !IsSuccessionSexEligible(pHeir, pKingdom))
            {
                // An explicit registration must not reintroduce a female heir
                // after a male-only succession law has taken effect.
                MarkSelectionDirty(pKingdom);
                pHeir = null;
                pMode = SuccessionMode.NONE;
            }
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
            bool previousHeirSexEligible = previousHeir?.data != null &&
                (RepublicGovernmentService.IsRepublic(pKingdom) ||
                 previousHeir.isSexMale() ||
                 IsSuccessionSexEligible(previousHeir, pKingdom));
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

            if (heir?.data == null && previousHeirSexEligible &&
                previousHeir.isAlive())
                return previousHeir;

            if (heir?.data != null &&
                !NormalizeHeirForRegistration(pKingdom, heir))
                return previousHeir;

            ClearOldHeirFlag(pKingdom, heir?.data?.id ?? -1L);
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
            SetHeirFlag(heir, true, pKingdom);
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

        internal static bool PrepareRegisteredHeirForAccession(
            Kingdom pKingdom, Actor pHeir)
        {
            return NormalizeHeirForRegistration(pKingdom, pHeir);
        }

        private static bool NormalizeHeirForRegistration(Kingdom pKingdom,
            Actor pHeir)
        {
            if (pKingdom?.data == null || pHeir?.data == null ||
                !pHeir.isAlive())
            {
                LogRegistrationFailure(pKingdom, pHeir, "not_alive");
                return false;
            }
            City home = ResolveRegistrationHome(pKingdom, pHeir);
            if (!ReleaseForeignKingshipForSuccession(pKingdom, pHeir))
            {
                LogRegistrationFailure(pKingdom, pHeir, "foreign_kingship");
                return false;
            }
            if (!RoyalGuardService.ReleaseForRegisteredHeir(pKingdom,
                    pHeir, "became_heir"))
            {
                LogRegistrationFailure(pKingdom, pHeir, "royal_guard");
                return false;
            }
            if (SlaveService.IsSlave(pHeir) &&
                (!SlaveService.FreeSlave(pHeir, "became_heir") ||
                 SlaveService.IsSlave(pHeir)))
            {
                LogRegistrationFailure(pKingdom, pHeir, "slave");
                return false;
            }

            FormerHeirService.ClearSnapshot(pHeir);
            RoyalAsylumService.RecallForSuccession(pHeir, pKingdom);
            try
            {
                // 太子不领郡县:册立时解去地方官职。
                //
                // 原来只解军职,县令的位子留着 —— 于是出现一个"住在京城的县令":
                // 下面几行会把他从本城迁到都城,而官职还挂在原来那座城上。更要紧
                // 的是他会继续按地方官被派去边郡,死得比宗室里其他人快得多(实测
                // 载入存档后胞弟刚册为太子就没了)。入仕那一侧本来就禁着
                // (LocalOfficialCandidateRules.CanEnter 把登记继承人排除在候选之外),
                // 只有"先为官、后册立"这条路漏了,这里补上,与新君即位时的
                // RecallForSuccession 同一口径。
                if (IsCityLeaderOfAnyCity(pKingdom, pHeir))
                    RemoveCityLeaderOffice(pKingdom, pHeir);
                if (pHeir.hasArmy()) pHeir.removeFromArmy();
                if (home?.data == null && pHeir.city?.kingdom != pKingdom)
                    pHeir.setCity(null);
                if (pHeir.kingdom != pKingdom)
                    ActorKingdomSafetyService.DetachForTransfer(pHeir);

                if (!NaturalizeForRegistration(pKingdom, pHeir, home))
                {
                    LogRegistrationFailure(pKingdom, pHeir, "naturalize");
                    return false;
                }

                // 学派籍贯只是从属记录,同步不上不该否掉册立 —— 否掉的代价是
                // 国籍也不改、继承人身份也不写,王国就此无嗣。改成尽力而为,
                // 按他实际落到的城同步(原来传的是**预期**居所,安置换了城就对不上)。
                if (!HistoricalAffiliationService.SynchronizeHomeForSuccession(
                        pHeir, pKingdom, pHeir.city ?? home))
                    LogRegistrationFailure(pKingdom, pHeir, "school_home");
                pHeir.clearGraphicsFully();
            }
            catch { LogRegistrationFailure(pKingdom, pHeir, "exception");
                    return false; }
            // 归化的硬指标只有**国籍**。原来还要求 pHeir.city == home,
            // 也就是必须迁进都城才算登记成功;都城安置不下(容量、住房、
            // joinCity 被别的规则挡住)整个册立就失败,而失败在
            // StoreHeirSelection 里是 return previousHeir —— 于是既没统一国籍,
            // 也没写继承人身份,外部还照旧从只读预览里显示他是太子。
            return pHeir.kingdom == pKingdom;
        }

        /// <summary>
        ///     把继承人归化进本国:先试指定居所,不成再顺着本国其它城试,
        ///     一座都安置不下就至少把国籍改过来。
        ///
        ///     居所只是就近安置,国籍才是继承的前提。两者原来绑在一处,
        ///     所以"住不进都城"会一路升级成"这个王国没有继承人"。
        ///
        ///     每座城要单独开一次 FormalAffiliationTransferScope —— 那道许可是
        ///     按 (actor, kingdom, city) 三元组匹配的(FormalAffiliationTransferRules.Allows),
        ///     拿着都城的许可去 joinCity 别的城会被学派籍贯规则挡掉。
        /// </summary>
        private static bool NaturalizeForRegistration(Kingdom pKingdom,
            Actor pHeir, City pHome)
        {
            if (TryJoinForRegistration(pKingdom, pHeir, pHome)) return true;
            foreach (City city in pKingdom.getCities())
            {
                if (city == pHome || city?.data == null || city.isRekt() ||
                    city.kingdom != pKingdom) continue;
                if (TryJoinForRegistration(pKingdom, pHeir, city)) return true;
            }

            // 一座城都安置不下:先脱离外国的城,否则国籍会跟着城被带回去
            // (原版 actor 的归属很大程度上由 city 决定),再单改国籍。
            try
            {
                if (pHeir.city?.kingdom != pKingdom) pHeir.setCity(null);
            }
            catch { }
            return TryJoinForRegistration(pKingdom, pHeir, null);
        }

        /// <summary>
        ///     一次归化尝试。成功的判据是**国籍归本国、且没有残留的外国城**。
        ///
        ///     光看 `pHeir.kingdom == pKingdom` 不够:原版 setKingdom 只是给字段
        ///     赋值(Actor.cs:7810),而 joinCity 可能被别的规则挡下来。那样就得到
        ///     一个"国籍在本国、城还在外国"的半成品,任何一处从 city 反推归属的
        ///     地方都会把国籍带回去 —— 玩家看到的就是"归化没效果"。
        /// </summary>
        private static bool TryJoinForRegistration(Kingdom pKingdom,
            Actor pHeir, City pHome)
        {
            try
            {
                using (FormalAffiliationTransferScope.Open(pHeir.data.id,
                           pKingdom.id, pHome?.data?.id ?? -1L))
                {
                    if (pHeir.kingdom != pKingdom)
                        pHeir.joinKingdom(pKingdom);
                    if (pHome?.data != null && pHeir.city != pHome)
                        pHeir.joinCity(pHome);
                }
            }
            catch { return false; }
            if (pHeir.kingdom != pKingdom) return false;
            City current = pHeir.city;
            return current?.data == null || current.kingdom == pKingdom;
        }

        /// <summary>
        ///     册立失败的现场,受性能诊断总开关门控。这条路径原来是
        ///     `catch { return false; }` —— 失败没有任何痕迹,而后果
        ///     (继承人身份不写入)要过一年才在别处显形。
        /// </summary>
        /// <summary>
        ///     已登记的继承人却不在本国 —— 归化在册立那一刻是成功的(否则会走
        ///     register_failed),之后被别处扳了回去。这条现场把两类原因分开:
        ///     城还在外国(是城在拖着国籍走),还是城已就位而国籍单独被改。
        ///     每年最多一行,受性能诊断总开关门控。
        /// </summary>
        private static void LogHeirDivergence(Kingdom pKingdom, Actor pHeir)
        {
            if (!AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                return;
            if (pKingdom?.data == null || pHeir?.data == null) return;
            if (pHeir.kingdom == pKingdom) return;
            City city = pHeir.city;
            ModClass.LogInfo("[AW3 HEIR] diverged kingdom=" + pKingdom.id +
                " heir=" + pHeir.data.id +
                " heir_kingdom=" + (pHeir.kingdom?.id ?? -1L) +
                " heir_city=" + (city?.data?.id ?? -1L) +
                " city_kingdom=" + (city?.kingdom?.id ?? -1L) +
                " school_home=" + (AncientWarfare3.core.schools
                    .HistoricalAffiliationService.HomeKingdom(pHeir)?.id ??
                    -1L));
        }

        private static void LogRegistrationFailure(Kingdom pKingdom,
            Actor pHeir, string pStage)
        {
            BlockRegistrationForThisYear(pKingdom, pHeir);
            if (!AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                return;
            ModClass.LogInfo("[AW3 HEIR] register_failed stage=" + pStage +
                " kingdom=" + (pKingdom?.id ?? -1L) +
                " heir=" + (pHeir?.data?.id ?? -1L) +
                " heir_kingdom=" + (pHeir?.kingdom?.id ?? -1L) +
                " heir_city=" + (pHeir?.city?.data?.id ?? -1L) +
                " capital=" + (pKingdom?.capital?.data?.id ?? -1L));
        }

        /// <summary>
        ///     本年内册立失败过的人。册立失败原来是终局:StoreHeirSelection 对
        ///     false 的反应是"什么都不写",而下一次刷新又会挑中同一个人、再失败一次
        ///     —— 顺位第二席永远没有机会,王国就这么一直空着。
        ///
        ///     挡一年:这一年里换下一顺位去立,明年清空重来(卡住的原因多半是
        ///     暂时的 —— 都城刚陷落、人在外国当着王、还挂着禁卫军身份)。
        ///     这不是遮盖问题,失败照样打日志;只是不让一个人堵死整条顺位。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<long,
                (int Year, System.Collections.Generic.HashSet<long> Ids)>
            RegistrationBlocked =
                new System.Collections.Generic.Dictionary<long,
                    (int, System.Collections.Generic.HashSet<long>)>();

        private static void BlockRegistrationForThisYear(Kingdom pKingdom,
            Actor pHeir)
        {
            long heirId = pHeir?.data?.id ?? -1L;
            if (pKingdom?.data == null || heirId < 0L) return;
            int year = SafeCurrentYear();
            if (!RegistrationBlocked.TryGetValue(pKingdom.id,
                    out (int Year, System.Collections.Generic.HashSet<long> Ids)
                        entry) || entry.Year != year)
            {
                entry = (year, new System.Collections.Generic.HashSet<long>());
                RegistrationBlocked[pKingdom.id] = entry;
            }
            entry.Ids.Add(heirId);
        }

        private static bool IsRegistrationBlocked(Kingdom pKingdom,
            Actor pActor)
        {
            long actorId = pActor?.data?.id ?? -1L;
            if (pKingdom?.data == null || actorId < 0L) return false;
            return RegistrationBlocked.TryGetValue(pKingdom.id,
                       out (int Year,
                           System.Collections.Generic.HashSet<long> Ids)
                           entry) &&
                   entry.Year == SafeCurrentYear() &&
                   entry.Ids.Contains(actorId);
        }

        internal static bool ReleaseForeignKingshipForSuccession(
            Kingdom pKingdom, Actor pHeir)
        {
            if (pKingdom?.data == null || pHeir?.data == null) return false;
            Kingdom previousKingdom = pHeir.kingdom;
            if (previousKingdom?.data == null ||
                previousKingdom == pKingdom ||
                previousKingdom.king != pHeir)
                return true;
            try
            {
                ReigningRoyalLineageIndex.OnKingDying(previousKingdom,
                    pHeir);
                previousKingdom.kingLeftEvent();
            }
            catch { return false; }
            return previousKingdom.king != pHeir;
        }

        private static City ResolveRegistrationHome(Kingdom pKingdom,
            Actor pHeir)
        {
            if (pKingdom?.data == null) return null;
            City capital = pKingdom.capital;
            if (capital?.data != null && !capital.isRekt() &&
                capital.kingdom == pKingdom)
                return capital;
            City current = pHeir?.city;
            if (current?.data != null && !current.isRekt() &&
                current.kingdom == pKingdom)
                return current;
            foreach (City city in pKingdom.getCities())
                if (city?.data != null && !city.isRekt() &&
                    city.kingdom == pKingdom)
                    return city;
            return null;
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

        private static void SetHeirFlag(Actor pActor, bool pValue,
            Kingdom pKingdom = null)
        {
            if (pActor?.data == null) return;
            // 名号要认「他是哪个国的继承人」,不能认他此刻站在哪个国 ——
            // 归化可能还没落定。这个 id 和 IS_HEIR 一起写、一起清。
            long kingdomId = pValue ? pKingdom?.id ?? -1L : -1L;
            pActor.data.get(LineageKeys.HEIR_KINGDOM_ID,
                out long oldKingdomId, -1L);
            if (oldKingdomId != kingdomId)
                pActor.data.set(LineageKeys.HEIR_KINGDOM_ID, kingdomId);

            pActor.data.get(LineageKeys.IS_HEIR, out bool oldValue, false);
            if (oldValue == pValue) return;
            pActor.data.set(LineageKeys.IS_HEIR, pValue);
            pActor.clearGraphicsFully();
        }

        /// <summary>
        ///     他是哪个国的继承人。取登记时落下的 <see cref="LineageKeys.HEIR_KINGDOM_ID"/>,
        ///     并用那个国的 KINGDOM_HEIR_ID 反证一次;对不上再退回 actor 当前的国。
        ///
        ///     名号(太子/储君/世子……)必须由**这个**国来定:
        ///     HeirTitleRules.BuildSocialTitle 要读所属国的帝制/天命/藩镇/共和状态,
        ///     拿一个还没归化过来的外国去读,得到的是另一套称谓,甚至什么都没有 ——
        ///     用户报的"族谱 tooltip 和 actor 身上都看不到太子身份"就是这么来的。
        /// </summary>
        internal static Kingdom ResolveHeirKingdom(Actor pActor)
        {
            if (pActor?.data == null) return null;
            pActor.data.get(LineageKeys.HEIR_KINGDOM_ID, out long kingdomId,
                -1L);
            if (kingdomId >= 0L)
            {
                Kingdom registered = FindKingdomById(kingdomId);
                if (registered?.data != null &&
                    IsCurrentHeir(registered, pActor)) return registered;
            }
            Kingdom current = pActor.kingdom;
            return current?.data != null && IsCurrentHeir(current, pActor)
                ? current
                : null;
        }

        /// <summary>
        ///     按 id 取王国。走管理器的索引查询,**不要**遍历 kingdoms ——
        ///     ResolveHeirKingdom 挂在 LineageArchiveWriter 的每次归档和族谱
        ///     tooltip 的每个节点上,线性扫描会随王国数量放大。
        /// </summary>
        private static Kingdom FindKingdomById(long pKingdomId)
        {
            if (pKingdomId < 0L) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
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
            bool directDescendantLegitimate = false;
            double directDescendantBirth = 0;
            bool directDescendantAdult = false;
            Actor collateral = null;
            int collateralTier = HeirGenerationRules.TierIneligible;
            int collateralDelta = 0;
            bool collateralLegitimate = false;
            double collateralBirth = 0;
            bool collateralAdult = false;

            // 国王的父系祖先表在整个循环里是不变的,建一次重复用。原来是对每个
            // 候选人都调一次 NearestCommonAgnaticAncestor,而那个方法内部会新建
            // 一个 Dictionary 并把国王整条父系链重走一遍 —— 同一张表被重建了
            // N 遍。实测 succession:reconcile_heir 单次 60.9ms,占
            // annual_succession 的 99.8%,而该阶段又是权威周期里最大的单项。
            // 按「王国 + 参照君主」共享(见 GetKingAncestry):原来每次刷新新建
            // 一份,而一次核对最多要跑三遍(正统/军功/文治)。
            LineageQuery.AgnaticAncestorDepths kingAncestry =
                GetKingAncestry(pKingdom, kingId);
            if (kingAncestry == null)
                return new HeirSelection(null, SuccessionMode.NONE);
            var pool = CollectSuccessionCandidatePool(pKingdom, king, kingId);
            foreach (Actor cand in pool)
            {
                if (!IsHeirBaseEligible(cand, pKingdom, king)) continue;
                long candId = cand.data.id;
                if (candId == kingId) continue;

                // 同源判定:与国王的最近共同父系祖先(走 AW3 SQLite 族谱,覆盖无原版 parent_id 的宗族成员)。
                long anc = kingAncestry.NearestCommon(candId,
                    out int kingDepth, out int candDepth);
                if (anc < 0) continue;

                bool isDesc = kingDepth == 0;          // 共同祖先即国王本人 → 国王男系后裔
                int delta = candDepth - kingDepth;     // >0 晚辈 / 0 同辈 / <0 长辈
                int tier = HeirGenerationRules.ClassifyTier(isDesc, delta);

                double birth = SafeCreatedTime(cand);
                bool adult = SafeIsAdult(cand);
                bool legitimate = IsLegitimateBirth(cand);
                if (isDesc)
                {
                    if (directDescendant == null ||
                        HeirGenerationRules.Compare(tier, delta, legitimate,
                            birth, adult,
                            HeirGenerationRules.TierDirectDescendant,
                            directDescendantDelta, directDescendantLegitimate,
                            directDescendantBirth,
                            directDescendantAdult) < 0)
                    {
                        directDescendant = cand;
                        directDescendantDelta = delta;
                        directDescendantLegitimate = legitimate;
                        directDescendantBirth = birth;
                        directDescendantAdult = adult;
                    }
                    continue;
                }

                if (collateral == null || HeirGenerationRules.Compare(
                        tier, delta, legitimate, birth, adult, collateralTier,
                        collateralDelta, collateralLegitimate, collateralBirth,
                        collateralAdult) < 0)
                {
                    collateral = cand;
                    collateralTier = tier;
                    collateralDelta = delta;
                    collateralLegitimate = legitimate;
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
            {
                LogVacancy(pKingdom, kingId, pool.Count);
                return new HeirSelection(null, SuccessionMode.NONE); // 真·绝嗣/亡国
            }

            return new HeirSelection(collateral,
                SuccessionMode.COLLATERAL_RESTORE);
        }

        /// <summary>
        ///     一个人都挑不出来时的一行现场记录,受性能诊断总开关门控
        ///     (关掉时零成本)。之前那批 [AW3 HEIR] 探针是按候选人逐条打印的,
        ///     每个空位王国每年刷屏,所以撤了;这里只在**确实选不出人**时打一行,
        ///     且把三个关键前提一并带上 —— 池子大小、父系链有没有接上、
        ///     同胞兄弟认没认出来。绝大多数"明明有胞弟却空着"都倒在这三项里。
        /// </summary>
        private static void LogVacancy(Kingdom pKingdom, long pKingId,
            int pPoolSize)
        {
            if (!AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                return;
            long fatherId = -1L;
            bool parentPair = false;
            try
            {
                fatherId = LineageQuery.GetFatherId(pKingId);
                parentPair = TryGetParentPair(pKingId, out long _, out long _);
            }
            catch { }
            ModClass.LogInfo("[AW3 HEIR] vacancy kingdom=" + pKingdom.id +
                " king=" + pKingId + " pool=" + pPoolSize +
                " father=" + fatherId + " parent_pair=" + parentPair);
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
                // 不在本国不再是硬条件 —— 与 IsHeirBaseEligible 一致,归化在
                // NormalizeHeirForRegistration 里做;这里只作为排序偏好。
                bool eligible = sharesBothParents && SafeIsAdult(sibling) &&
                    IsHeirBaseEligible(sibling, pKingdom, pKing);
                actors[sibling.data.id] = sibling;
                candidates.Add(new HeirFullBrotherCandidate(sibling.data.id,
                    eligible, sharesBothParents, SafeCreatedTime(sibling),
                    sibling.kingdom == pKingdom));
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
                // 走 AW3 族谱查询，覆盖没有原版 parent_id 数据的宗族成员。
                foreach (long childId in LineageQuery.GetChildIds(pParentA))
                    if (childId >= 0L) siblingIds.Add(childId);
                foreach (long childId in LineageQuery.GetChildIds(pParentB))
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
                // 走 AW3 族谱查询，覆盖没有原版 parent_id 数据的宗族成员。
                foreach (long parentId in LineageQuery.GetParentIds(pActorId))
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
                LineageQuery.GetChildIds(kingId);
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
            bool eligible = IsSuccessionSexEligible(pChild, pKing?.kingdom) &&
                            !pChild.isRekt() &&
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
            // 本年内册立失败过的人让位给下一顺位,免得一个人堵死整条顺位。
            if (IsRegistrationBlocked(pKingdom, pActor)) return false;
            // 夏朝/Xia化王国放宽种族/谱系限制：不强求 LINEAGE_ID 或 IsXia，
            // 候选人不需要在本国，归化在 NormalizeHeirForRegistration 里进行。
            bool usesManaged = UsesManagedLineageForKingdom(pKingdom);
            if (!usesManaged)
            {
                if (!LineageService.IsXia(pActor) &&
                    !LineageService.UsesAwLineageSystem(pActor)) return false;
            }
            if (!IsSuccessionSexEligible(pActor, pKingdom)) return false;
            if (pActor.isRekt() || !pActor.isAlive()) return false;
            if (!SuccessionTransitionRules.IsOfficialRoleEligible(
                    pActor.isKing(), pIsCityLeader: false, pIsGeneral: false,
                    pIsArmyCaptain: false, pHasFief: false))
                return false;
            if (pActor.hasTrait("madness")) return false;
            if (SlaveService.IsSlave(pActor)) return false;
            return true;
        }

        private static bool UsesManagedLineageForKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   SuccessionTransitionRules.ShouldUseManagedSuccession(
                       LineageService.IsXiaKingdom(pKingdom),
                       XiaizationService.UsesXiaizedInstitutionSystem(pKingdom));
        }

        private static bool IsSuccessionSexEligible(Actor pActor,
            Kingdom pKingdom)
        {
            return pActor?.data != null &&
                   XiaAuthorityGenderRules.IsSuccessionCandidateSexEligible(
                       pActor.isSexMale(),
                       CourtAuxiliaryLawService.AllowsFemaleSuccession(
                           pKingdom));
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

        private static Actor FindAdultDirectSon(Actor pKing)
        {
            return pKing?.data == null ? null : PickEldestStable(
                pKing.getChildren(false), pKing,
                pMaleOnly: !CourtAuxiliaryLawService.AllowsFemaleSuccession(
                    pKing.kingdom));
        }

        private static Actor GetRegisteredHeirIfSuitable(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long curId, -1L);
            if (curId < 0) return null;
            Actor current = World.world.units.get(curId);
            return IsSuitableRegisteredHeir(current, pKingdom, pKing) ? current : null;
        }

        /// <summary>
        /// 旁系入继筛选的一次性备忘。同一个候选池要按不同条件跑最多 6 遍,而两笔
        /// 重活只取决于候选 id(宗系/氏在一次筛选内是常量),所以算一次存下来。
        /// 由 FindCollateralRestorationHeir 持有,随方法返回即弃。
        /// </summary>
        private sealed class CollateralScanMemo
        {
            private readonly Dictionary<long, bool> _agnatic =
                new Dictionary<long, bool>();
            private readonly Dictionary<long, bool> _restorable =
                new Dictionary<long, bool>();

            internal bool IsAgnaticDescendant(long pActorId, long pLineageId)
            {
                if (_agnatic.TryGetValue(pActorId, out bool cached))
                    return cached;
                bool value = LineageQuery.IsAgnaticDescendant(
                    pActorId, pLineageId);
                _agnatic[pActorId] = value;
                return value;
            }

            internal bool CanRestoreToLegitimateShi(Actor pActor,
                long pLineageId, long pShiId)
            {
                long actorId = pActor?.data?.id ?? -1L;
                if (actorId < 0L) return false;
                if (_restorable.TryGetValue(actorId, out bool cached))
                    return cached;
                bool value = CollateralRestorationTraceService
                    .CanRestoreToLegitimateShi(pActor, pLineageId, pShiId);
                _restorable[actorId] = value;
                return value;
            }
        }

        private static Actor FindCollateralRestorationHeir(Kingdom pKingdom, Actor pKing)
        {
            if (pKingdom?.data == null) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID, out long legitimateLineage, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID, out long legitimateShi, -1L);
            if (legitimateLineage < 0) return null;

            List<Actor> pool = CollectSuccessionCandidatePool(pKingdom);
            // 下面对同一个池最多跑 6 遍,而 legitimateLineage / legitimateShi 在
            // 整个方法里是常量 —— 于是每个候选人身上两笔纯函数式的重活被重复算
            // 了好几遍:
            //   IsAgnaticDescendant  每次新建集合并重走候选那条父系链(6 遍)
            //   CanRestoreToLegitimateShi  含 DB 读、链walk,还有一个对整个父母
            //     DAG 的 BFS(双亲、深度上限 96、每次新建 Queue + HashSet)。只在
            //     非男系的 4 遍里跑。
            // 按候选 id 备忘一次即可。
            var memo = new CollateralScanMemo();

            // 男系(同姓父系)优先:氏/分支可不同,但父系必须一路同姓。先成年,再未成年。
            Actor agnaticAdult = PickCollateralCandidate(pool, memo, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: true, pRequireAgnatic: true);
            if (agnaticAdult != null) return agnaticAdult;

            Actor agnaticUnderage = PickCollateralCandidate(pool, memo, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: false, pRequireAgnatic: true);
            if (agnaticUnderage != null) return agnaticUnderage;

            // 无男系同姓后裔 → 回退(非男系,后续 ApplyCollateralRestoration 会标记为异姓入继)。
            Actor exactAdult = PickCollateralCandidate(pool, memo, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: true, pRequireAdult: true, pRequireAgnatic: false);
            if (exactAdult != null) return exactAdult;

            Actor traceableBranchAdult = PickCollateralCandidate(pool, memo, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: true, pRequireAgnatic: false);
            if (traceableBranchAdult != null) return traceableBranchAdult;

            Actor exactUnderage = PickCollateralCandidate(pool, memo, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: true, pRequireAdult: false, pRequireAgnatic: false);
            if (exactUnderage != null) return exactUnderage;

            return PickCollateralCandidate(pool, memo, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: false, pRequireAgnatic: false);
        }

        private static List<Actor> CollectSuccessionCandidatePool(Kingdom pKingdom,
            Actor pReferenceKing = null, long pReferenceKingId = -1L)
        {
            return InheritanceCandidateService.CollectRoyalCandidates(
                pKingdom, pReferenceKing ?? pKingdom?.king,
                pReferenceKingId);
        }

        private static Actor PickCollateralCandidate(List<Actor> pCandidates,
            CollateralScanMemo pMemo, Kingdom pKingdom, Actor pKing,
            long pLegitimateLineage, long pLegitimateShi, bool pRequireLegitimateShi, bool pRequireAdult,
            bool pRequireAgnatic)
        {
            Actor best = null;
            int bestScore = int.MaxValue;
            foreach (Actor candidate in pCandidates)
            {
                if (!IsCollateralCandidate(candidate, pMemo, pKingdom, pKing, pLegitimateLineage, pLegitimateShi,
                        pRequireLegitimateShi, pRequireAdult, pRequireAgnatic))
                    continue;

                int score = CollateralCandidateScore(candidate, pKing, pLegitimateShi);
                if (best != null && score >= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        private static bool IsCollateralCandidate(Actor pActor,
            CollateralScanMemo pMemo, Kingdom pKingdom, Actor pKing,
            long pLegitimateLineage, long pLegitimateShi, bool pRequireLegitimateShi, bool pRequireAdult,
            bool pRequireAgnatic)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor == pKing) return false;
            if (pActor.kingdom != pKingdom) return false;
            if (!LineageService.IsXia(pActor) && !LineageService.UsesAwLineageSystem(pActor)) return false;
            if (!IsSuccessionSexEligible(pActor, pKingdom)) return false;
            if (pActor.isRekt() || !pActor.isAlive()) return false;
            if (pActor.isKing()) return false;
            if (pActor.hasTrait("madness")) return false;
            if (SlaveService.IsSlave(pActor)) return false;
            if (pRequireAdult && !pActor.isAdult()) return false;
            if (!pRequireAdult && pActor.isAdult()) return false;

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineage, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long shi, -1L);
            if (lineage != pLegitimateLineage) return false;

            // 多遍筛选共用一份答案,见 CollateralScanMemo。
            bool agnatic = pMemo.IsAgnaticDescendant(pActor.data.id,
                pLegitimateLineage);
            if (pRequireAgnatic)
                // 男系同姓即合格,氏(分支)可不同。成年/未成年已由上面的 pRequireAdult 分档,这里传 isAdult:true。
                return MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                    isXia: true, isMale: IsSuccessionSexEligible(pActor, pKingdom), isAlive: true, isAdult: true, isKing: false,
                    hasMadness: false, sameLineage: true, belongsToLegitimateShi: true,
                    canTraceToLegitimateBranch: true, requireAgnatic: true, isAgnaticLineDescendant: agnatic);

            bool canRestore = pMemo.CanRestoreToLegitimateShi(
                pActor, pLegitimateLineage, pLegitimateShi);
            if (pRequireLegitimateShi)
            {
                if (shi != pLegitimateShi) return false;
                if (!pRequireAdult) return true;
                return MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                    isXia: true,
                    isMale: IsSuccessionSexEligible(pActor, pKingdom),
                    isAlive: true,
                    isAdult: pActor.isAdult(),
                    isKing: false,
                    hasMadness: false,
                    sameLineage: true,
                    belongsToLegitimateShi: true);
            }
            return MandateSuccessionRules.IsValidCollateralRestorationCandidate(
                isXia: true,
                isMale: IsSuccessionSexEligible(pActor, pKingdom),
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
            foreach (long parent in LineageQuery.GetParentIds(pActorId))
                if (parent >= 0) yield return parent;
            foreach (long child in LineageQuery.GetChildIds(pActorId))
                if (child >= 0) yield return child;
        }

        private static Actor FindDirectChildHeir(Actor pKing)
        {
            if (pKing?.data == null) return null;

            // 鍎垮瓙浼樺厛:闀垮瓙(created_time 鏈€灏忕殑鍚堟牸鎴愬勾鍎垮瓙)
            bool maleOnly = !CourtAuxiliaryLawService.AllowsFemaleSuccession(
                pKing.kingdom);
            Actor eldest = PickEldestStable(pKing.getChildren(false), pKing,
                pMaleOnly: maleOnly);
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
            // 幼主兜底同样走嫡长,不是单纯比出生早晚。
            Actor best = null;
            bool bestLegitimate = false;
            double bestBirth = double.MaxValue;
            foreach (Actor child in pKing.getChildren(false))
            {
                if (!IsUnderageDirectSonFallback(child, pKing, pHasAdultDirectSon: false)) continue;
                bool legitimate = IsLegitimateBirth(child);
                double birth = SafeCreatedTime(child);
                if (best != null && !SuccessionOrderRules.SortsBefore(
                        SuccessionOrderBasis.Bloodline,
                        SuccessionOrderRules.DirectLine, legitimate, birth, 0,
                        child.data.id,
                        SuccessionOrderRules.DirectLine, bestLegitimate,
                        bestBirth, 0, best.data.id)) continue;
                best = child;
                bestLegitimate = legitimate;
                bestBirth = birth;
            }
            return best;
        }

        /// <summary>浠庣洿绯诲瓙濂充腑閫夐暱瀛?闀垮コ(created_time鏈€灏?鏈€鏃╁嚭鐢?銆俻MaleOnly=true 鍙€夊効瀛愩€?/summary>
        private static Actor PickEldest(System.Collections.Generic.IEnumerable<Actor> pCandidates,
            Actor pKing, bool pMaleOnly)
        {
            // 同上:嫡压长,和 PickEldestStable 共用一条规则。
            return PickEldestStable(pCandidates, pKing, pMaleOnly);
        }

        /// <summary>浠庡€欓€夐噷鎸?鍚堟牸(娲烩埀闈炵帇鈭ф垚骞粹埀闈炵柉)涓?|age-18| 鏈€灏?pPreferMale 鏃剁敺鎬ц幏 -1000 鍋忕疆(蹇呬紭鍏堜簬濂虫€?銆?/summary>
        private static Actor PickEldestStable(System.Collections.Generic.IEnumerable<Actor> pCandidates,
            Actor pKing, bool pMaleOnly)
        {
            // 统一顺位:嫡压长。原来这里只比 created_time,于是庶长会压过嫡幼 ——
            // 和 HeirDirectSonRules 那条路径(一直是嫡优先)自相矛盾,同一个王国
            // 走不同入口能选出不同的继承人。
            Actor best = null;
            bool bestLegitimate = false;
            double bestBirth = double.MaxValue;
            foreach (Actor candidate in pCandidates)
            {
                if (!IsSuitableHeir(candidate, pKing)) continue;
                if (pMaleOnly && !candidate.isSexMale()) continue;
                bool legitimate = IsLegitimateBirth(candidate);
                double birth = SafeCreatedTime(candidate);
                if (best != null && !SuccessionOrderRules.SortsBefore(
                        SuccessionOrderBasis.Bloodline,
                        SuccessionOrderRules.DirectLine, legitimate, birth, 0,
                        candidate.data.id,
                        SuccessionOrderRules.DirectLine, bestLegitimate,
                        bestBirth, 0, best.data.id)) continue;
                best = candidate;
                bestLegitimate = legitimate;
                bestBirth = birth;
            }
            return best;
        }

        /// <summary>嫡出与否。缺省视为嫡出,和继承池其余各处口径一致。</summary>
        private static bool IsLegitimateBirth(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.BIRTH_LEGITIMACY,
                out bool legitimate, true);
            return legitimate;
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
                isMale: pActor?.data != null &&
                    IsSuccessionSexEligible(pActor, pKing?.kingdom),
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
            if (!IsSuccessionSexEligible(pActor, pKingdom)) return false;
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

            bool hasAdultDirectSon = pKing != null && FindAdultDirectSon(
                pKing) != null;
            return MandateSuccessionRules.CanUseUnderageDirectSonFallback(
                direct,
                IsSuccessionSexEligible(pActor, pKingdom),
                !pActor.isRekt(),
                pActor.isKing() || pActor == pKing,
                hasAdultDirectSon);
        }

        private static bool IsUnderageDirectSonFallback(Actor pActor, Actor pKing, bool pHasAdultDirectSon)
        {
            if (pActor == null || pActor.isRekt() || pActor.isAdult()) return false;
            return HeirCandidateRules.IsUnderageDirectSonEligible(
                isDirectSon: IsDirectChildOf(pActor, pKing),
                isMale: IsSuccessionSexEligible(pActor, pKing?.kingdom),
                isAlive: pActor.isAlive(),
                isCurrentKing: pActor.isKing() || pActor == pKing,
                hasAdultDirectSon: pHasAdultDirectSon,
                hasMadness: pActor.hasTrait("madness"),
                isSlave: SlaveService.IsSlave(pActor));
        }
    }
}
