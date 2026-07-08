using System.Collections.Generic;
using ai;

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
            if (pKingdom?.data == null) return;
            EnsureLegitimateLine(pKingdom, pKingdom.king);

            // 鍏堟竻鏃х户鎵夸汉鐨?IS_HEIR 鏍囪(鍗镐换 鈫?鎭㈠鏅€氱毊鑲?鍥炬爣)銆?            ClearOldHeirFlag(pKingdom);

            ClearOldHeirFlag(pKingdom);
            HeirSelection selection = FindHeir(pKingdom, pKingdom.king, pIncludeRegisteredHeir: false);
            StoreHeirSelection(pKingdom, selection);
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
            var old = World.world.units.get(oldId);
            SetHeirFlag(old, false);
        }

        /// <summary>
        ///     鍙栫户鎵夸汉:**鐜颁换浼樺厛绋冲畾**鈥斺€斿凡鐧昏缁ф壙浜轰笖**浠嶅悎鏍?*(娲?闈炵帇/鎴愬勾/闈炵柉)鍒欎繚鎸佷笉鍙?
        ///     鐜颁换澶辨牸(姝?缁т綅/澶辨牸)鎴栦粠鏈櫥璁?鈫?鎵?FindHeir 閲嶉€夊苟鍐欏洖銆?        ///     鍏奸【涓や釜闇€姹?鈶?宸叉湁鍚堟牸缁ф壙浜轰笉琚绻佹敼閫?鐢ㄦ埛鎶?宸插瓨鍦ㄨ繕琚噸閫?);
        ///     鈶?鍗充綅鏃跺効瀛愯繕灏忋€佸悗鏉ユ垚骞?鈫?鐜颁换涓虹┖/澶辨牸鏃堕噸閫変細鎶婃垚骞村効瀛愰€変笂銆?        /// </summary>
        public static Actor GetHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return null;
            Actor king = pKingdom.king;
            EnsureLegitimateLine(pKingdom, king);
            ClearOldHeirFlag(pKingdom);
            HeirSelection selection = FindHeir(pKingdom, king, pIncludeRegisteredHeir: true);
            StoreHeirSelection(pKingdom, selection);
            return selection.Actor;
        }

        public static bool HasHeir(Kingdom pKingdom)
        {
            return GetHeir(pKingdom) != null;
        }

        public static void PrepareSuccessionBeforeKingDeath(Kingdom pKingdom, Actor pDyingKing)
        {
            if (pKingdom?.data == null || pDyingKing?.data == null) return;
            RememberPreSuccessionKing(pKingdom, pDyingKing);
            ClearOldHeirFlag(pKingdom);
            HeirSelection selection = FindHeir(pKingdom, pDyingKing, pIncludeRegisteredHeir: true);
            StoreHeirSelection(pKingdom, selection);
        }

        public static bool HasSuccessionCandidate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long curId, -1L);
            if (curId >= 0)
            {
                Actor current = World.world.units.get(curId);
                if (IsSuitableRegisteredHeir(current, pKingdom, pKingdom.king)) return true;
            }
            if (FindHeir(pKingdom, pKingdom.king, pIncludeRegisteredHeir: true).Actor != null) return true;
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
            Actor heir = GetHeir(pKingdom);
            return heir?.data != null && heir.data.id == pActor.data.id;
        }

        public static void ClearHeir(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            ClearOldHeirFlag(pKingdom);                       // 娓呮棫缁ф壙浜?IS_HEIR 鏍囪
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, -1L);
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

        private static void StoreHeirSelection(Kingdom pKingdom, HeirSelection pSelection)
        {
            if (pKingdom?.data == null) return;
            Actor heir = pSelection.Actor;
            RecallForeignSelectedHeir(pKingdom, heir);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID, heir?.data?.id ?? -1L);
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE,
                heir?.data == null ? SuccessionMode.NONE : pSelection.Mode);
            SetHeirFlag(heir, true);
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

        /// <summary>閫夋渶鍚堥€傜户鎵夸汉:浼樺厛鍥界帇**鐩寸郴鎴愬勾鍎垮瓙(闀垮瓙浼樺厛=created_time鏈€灏?**;
        /// 鏃犲悎鏍煎効瀛愬啀鎵鹃暱濂?涓よ€呴兘娌℃湁鎵?fallback 鍒?royal_clan銆?/summary>
        private static HeirSelection FindHeir(Kingdom pKingdom, Actor pKnownKing,
            bool pIncludeRegisteredHeir)
        {
            Actor king = pKnownKing ?? pKingdom.king;
            if (pKingdom?.data == null) return new HeirSelection(null, SuccessionMode.NONE);
            EnsureLegitimateLine(pKingdom, king);

            if (king != null)
            {
                Actor adultSon = FindAdultDirectSon(king);
                if (adultSon != null) return new HeirSelection(adultSon, SuccessionMode.DIRECT);

                Actor underageSon = PickEldestUnderageDirectSon(king);
                if (underageSon != null) return new HeirSelection(underageSon, SuccessionMode.UNDERAGE_DIRECT);
            }

            if (pIncludeRegisteredHeir)
            {
                Actor registered = GetRegisteredHeirIfSuitable(pKingdom, king);
                if (registered != null) return new HeirSelection(registered, SuccessionMode.REGISTERED);
            }

            Actor collateral = FindCollateralRestorationHeir(pKingdom, king);
            if (collateral != null) return new HeirSelection(collateral, SuccessionMode.COLLATERAL_RESTORE);

            if (!ShouldUseOrdinaryFallbackSuccession(pKingdom))
                return new HeirSelection(null, SuccessionMode.NONE);

            Actor clanFallback = FindRoyalClanFallbackHeir(pKingdom, king);
            return new HeirSelection(clanFallback,
                clanFallback == null ? SuccessionMode.NONE : SuccessionMode.CLAN_FALLBACK);
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
            Actor exactAdult = PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: true, pRequireAdult: true);
            if (exactAdult != null) return exactAdult;

            Actor traceableBranchAdult = PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: true);
            if (traceableBranchAdult != null) return traceableBranchAdult;

            Actor exactUnderage = PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: true, pRequireAdult: false);
            if (exactUnderage != null) return exactUnderage;

            return PickCollateralCandidate(pool, pKingdom, pKing, legitimateLineage, legitimateShi,
                pRequireLegitimateShi: false, pRequireAdult: false);
        }

        private static List<Actor> CollectSuccessionCandidatePool(Kingdom pKingdom)
        {
            var result = new List<Actor>();
            var seen = new HashSet<long>();
            if (pKingdom?.data == null) return result;

            try
            {
                foreach (Actor unit in pKingdom.getUnits())
                    AddCandidate(unit, result, seen);
            }
            catch { }

            try
            {
                long royalClanId = pKingdom.data.royal_clan_id;
                if (royalClanId >= 0)
                {
                    var clan = World.world.clans.get(royalClanId);
                    if (clan?.units != null)
                    {
                        foreach (Actor unit in clan.units)
                            AddCandidate(unit, result, seen);
                    }
                }
            }
            catch { }

            return result;
        }

        private static void AddCandidate(Actor pActor, List<Actor> pResult, HashSet<long> pSeen)
        {
            if (pActor?.data == null || pSeen == null || pResult == null) return;
            if (!pSeen.Add(pActor.data.id)) return;
            pResult.Add(pActor);
        }

        private static Actor PickCollateralCandidate(List<Actor> pCandidates, Kingdom pKingdom, Actor pKing,
            long pLegitimateLineage, long pLegitimateShi, bool pRequireLegitimateShi, bool pRequireAdult)
        {
            Actor best = null;
            int bestScore = int.MaxValue;
            foreach (Actor candidate in pCandidates)
            {
                if (!IsCollateralCandidate(candidate, pKingdom, pKing, pLegitimateLineage, pLegitimateShi,
                        pRequireLegitimateShi, pRequireAdult))
                    continue;

                int score = CollateralCandidateScore(candidate, pKing, pLegitimateShi);
                if (best != null && score >= bestScore) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        private static bool IsCollateralCandidate(Actor pActor, Kingdom pKingdom, Actor pKing,
            long pLegitimateLineage, long pLegitimateShi, bool pRequireLegitimateShi, bool pRequireAdult)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor == pKing) return false;
            if (pActor.kingdom != pKingdom) return false;
            if (!LineageService.IsXia(pActor) && !LineageService.UsesAwLineageSystem(pActor)) return false;
            if (!pActor.isSexMale()) return false;
            if (pActor.isRekt() || !pActor.isAlive()) return false;
            if (pActor.isKing()) return false;
            if (pActor.hasTrait("madness")) return false;
            if (pRequireAdult && !pActor.isAdult()) return false;
            if (!pRequireAdult && pActor.isAdult()) return false;

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineage, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long shi, -1L);
            if (lineage != pLegitimateLineage) return false;
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
            foreach (long parent in LineageQuery.GetParentIds(pActorId))
                if (parent >= 0) yield return parent;
            foreach (long child in LineageQuery.GetChildIds(pActorId))
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
            if (pActor == null || pActor.isRekt()) return false;
            if (pActor == pKing) return false;
            if (!pActor.isSexMale()) return false;
            if (pActor.isKing()) return false;
            if (!pActor.isAdult()) return false;
            if (pActor.hasTrait("madness")) return false;
            return true;
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
            if (pActor.hasTrait("madness")) return false;
            return MandateSuccessionRules.CanUseUnderageDirectSonFallback(
                IsDirectChildOf(pActor, pKing),
                pActor.isSexMale(),
                !pActor.isRekt(),
                pActor.isKing() || pActor == pKing,
                pHasAdultDirectSon);
        }
    }
}
