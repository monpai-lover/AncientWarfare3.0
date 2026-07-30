using System;
using System.Collections.Generic;
using System.Globalization;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class NobleHeirPregnancyService
    {
        internal const int MaxRetriesPerCycle = 8;

        private static readonly Queue<long> PendingMothers =
            new Queue<long>();
        private static readonly HashSet<long> EnqueuedMothers =
            new HashSet<long>();

        public static bool TryPreparePregnancy(Actor pMother,
            out long pFatherId,
            out RulerHouseholdConceptionKind pConceptionKind)
        {
            pFatherId = -1L;
            pConceptionKind = RulerHouseholdConceptionKind.None;
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pMother?.data == null || !pMother.isSexFemale())
                return false;

            Actor father = RulerHouseholdPregnancyService
                .ResolveManagedFather(pMother, out pConceptionKind);
            if (father?.data == null) return false;
            bool motherEligible = IsEligibleNoble(pMother);
            bool fatherEligible = IsEligibleNoble(father);
            float duration = NobleHeirPregnancyRules
                .ResolvePregnancyDuration(0f, pPregnancyStatus: true,
                    pHasLivingPartner: true, motherEligible, fatherEligible);
            if (duration != NobleHeirPregnancyRules.TenMonthPregnancySeconds)
                return false;

            pFatherId = father.data.id;
            return true;
        }

        public static void OnBecameLovers(Actor pFirst, Actor pSecond)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pFirst?.data == null || pSecond?.data == null)
                return;
            Actor mother = pFirst.isSexFemale()
                ? pFirst
                : pSecond.isSexFemale()
                    ? pSecond
                    : null;
            Actor father = mother == pFirst ? pSecond : pFirst;
            if (mother?.data == null || father?.data == null ||
                !father.isSexMale() || mother.lover != father ||
                father.lover != mother || !mother.isAlive() ||
                mother.isRekt() || !father.isAlive() || father.isRekt() ||
                !mother.isAdult() || !father.isAdult() ||
                !mother.isBreedingAge() || !father.isBreedingAge() ||
                !IsPotentialTitleLineMember(mother) &&
                !IsPotentialTitleLineMember(father))
                return;

            mother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_PENDING,
                out bool pending, false);
            mother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE,
                out bool active, false);
            mother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_FATHER_ID,
                out long storedFatherId, -1L);
            if ((pending || active) && storedFatherId == father.data.id)
                return;

            if (pending || active)
                ClearLoverRequest(mother, pKeepCompletedToken: false);
            mother.data.get(
                LineageKeys.DYNASTIC_LOVER_HEIR_LAST_RELATION_TOKEN,
                out string completedToken, "");
            if (RelationTokenMatchesPair(completedToken, mother, father))
                return;

            string token = BuildRelationToken(mother, father);
            mother.data.set(LineageKeys.DYNASTIC_LOVER_HEIR_PENDING, true);
            mother.data.removeBool(LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE);
            mother.data.set(LineageKeys.DYNASTIC_LOVER_HEIR_FATHER_ID,
                father.data.id);
            mother.data.set(LineageKeys.DYNASTIC_LOVER_HEIR_RELATION_TOKEN,
                token);
            mother.data.set(LineageKeys.DYNASTIC_LOVER_HEIR_ATTEMPTS, 0);
            mother.data.removeBool(LineageKeys.DYNASTIC_LOVER_HEIR_SON_BORN);
            Enqueue(mother.data.id);
        }

        public static void OnLoverChanging(Actor pActor, Actor pNextLover)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pActor?.data == null)
                return;
            Actor previousLover = pActor.lover;
            if (previousLover?.data == null ||
                previousLover == pNextLover)
                return;

            Actor mother = pActor.isSexFemale()
                ? pActor
                : previousLover.isSexFemale()
                    ? previousLover
                    : null;
            Actor father = mother == pActor ? previousLover : pActor;
            if (mother?.data == null || father?.data == null ||
                !father.isSexMale())
                return;

            mother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_FATHER_ID,
                out long storedFatherId, -1L);
            if (storedFatherId == father.data.id)
            {
                ClearLoverRequest(mother, pKeepCompletedToken: false);
                return;
            }

            mother.data.get(
                LineageKeys.DYNASTIC_LOVER_HEIR_LAST_RELATION_TOKEN,
                out string completedToken, "");
            if (RelationTokenMatchesPair(completedToken, mother, father))
                mother.data.removeString(
                    LineageKeys.DYNASTIC_LOVER_HEIR_LAST_RELATION_TOKEN);
        }

        public static bool IsActiveLoverHeirBirth(Actor pFirst,
            Actor pSecond)
        {
            Actor mother = pFirst?.isSexFemale() == true
                ? pFirst
                : pSecond?.isSexFemale() == true
                    ? pSecond
                    : null;
            Actor father = mother == pFirst ? pSecond : pFirst;
            if (mother?.data == null || father?.data == null) return false;
            mother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE,
                out bool active, false);
            mother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_FATHER_ID,
                out long fatherId, -1L);
            return active && fatherId == father.data.id &&
                   mother.isAlive() && !mother.isRekt() &&
                   father.isAlive() && !father.isRekt() &&
                   mother.lover == father && father.lover == mother;
        }

        public static void OnLoverHeirChildBorn(Actor pChild,
            Actor pFirst, Actor pSecond)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pChild?.data == null ||
                !IsActiveLoverHeirBirth(pFirst, pSecond) ||
                !pChild.isSexMale()) return;
            Actor mother = pFirst?.isSexFemale() == true ? pFirst : pSecond;
            mother?.data?.set(LineageKeys.DYNASTIC_LOVER_HEIR_SON_BORN,
                true);
        }

        public static void OnPregnancyStarted(Actor pMother, long pFatherId,
            RulerHouseholdConceptionKind pConceptionKind)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pMother?.data == null || pFatherId < 0L)
                return;

            pMother.data.set(LineageKeys.DYNASTIC_PREGNANCY_MANAGED, true);
            pMother.data.set(LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID,
                pFatherId);
            RulerHouseholdPregnancyService.RecordConception(pMother,
                ResolveActor(pFatherId), pConceptionKind);
            ClearPendingOnly(pMother);
        }

        public static void OnPregnancyDeliveryCompleted(Actor pMother)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pMother?.data == null)
                return;

            bool loverDelivery = CompleteLoverHeirDelivery(pMother);
            pMother.data.get(LineageKeys.DYNASTIC_PREGNANCY_MANAGED,
                out bool managed, false);
            if (!managed)
            {
                if (loverDelivery)
                    RulerHouseholdPregnancyService.ClearConception(pMother);
                return;
            }
            pMother.data.removeBool(LineageKeys.DYNASTIC_PREGNANCY_MANAGED);

            if (loverDelivery)
            {
                pMother.data.removeBool(
                    LineageKeys.DYNASTIC_HEIR_RETRY_PENDING);
                pMother.data.removeLong(
                    LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID);
                pMother.data.removeFloat(
                    LineageKeys.DYNASTIC_HEIR_RETRY_REQUEST_TIME);
                RulerHouseholdPregnancyService.ClearConception(pMother);
                return;
            }

            pMother.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID,
                out long fatherId, -1L);
            Actor father = ResolveActor(fatherId);
            bool eligible = IsEligibleNoble(pMother) ||
                            IsEligibleNoble(father);
            bool hasLivingSon = EligibleParentHasLivingSon(pMother, father);
            pMother.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_PENDING,
                out bool alreadyPending, false);

            if (hasLivingSon || !eligible)
            {
                ClearAll(pMother);
                return;
            }

            if (NobleHeirPregnancyRules.ShouldCreateRetryRequest(
                    managed, eligible, hasLivingSon, alreadyPending))
            {
                pMother.data.set(LineageKeys.DYNASTIC_HEIR_RETRY_PENDING,
                    true);
                pMother.data.set(
                    LineageKeys.DYNASTIC_HEIR_RETRY_REQUEST_TIME,
                    CurrentWorldTime());
            }
            RulerHouseholdPregnancyService.ClearConception(pMother);
            Enqueue(pMother.data.id);
        }

        public static void OnActorLoaded(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_PENDING,
                out bool pending, false);
            pActor.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_PENDING,
                out bool loverPending, false);
            pActor.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE,
                out bool loverActive, false);
            if (loverActive && !pActor.hasStatus("pregnant"))
            {
                pActor.data.removeBool(
                    LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE);
                pActor.data.set(LineageKeys.DYNASTIC_LOVER_HEIR_PENDING,
                    true);
                pActor.data.removeBool(
                    LineageKeys.DYNASTIC_PREGNANCY_MANAGED);
                pActor.data.removeBool(
                    LineageKeys.DYNASTIC_HEIR_RETRY_PENDING);
                pActor.data.removeLong(
                    LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID);
                pActor.data.removeFloat(
                    LineageKeys.DYNASTIC_HEIR_RETRY_REQUEST_TIME);
                RulerHouseholdPregnancyService.ClearConception(pActor);
                loverPending = true;
            }
            if (pending || loverPending) Enqueue(pActor.data.id);
        }

        public static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                PendingMothers.Count == 0)
                return;

            int count = Math.Min(MaxRetriesPerCycle, PendingMothers.Count);
            for (int i = 0; i < count; i++)
            {
                long motherId = PendingMothers.Dequeue();
                EnqueuedMothers.Remove(motherId);
                try
                {
                    Actor mother = ResolveActor(motherId);
                    if (mother?.data == null) continue;
                    ProcessMother(mother);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Noble pregnancy retry failed for " +
                                        motherId + ": " + error.Message);
                    Enqueue(motherId);
                }
            }
        }

        public static void Reset()
        {
            PendingMothers.Clear();
            EnqueuedMothers.Clear();
        }

        public static bool IsEligibleNoble(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt())
                return false;

            if (DynasticMaleLineContinuityService.HasEligibleRole(pActor))
                return true;

            pActor.data.get(LineageKeys.LINEAGE_STATUS,
                out string lineageStatus, LineageStatus.NONE);
            if (lineageStatus == LineageStatus.NOBLE) return true;

            bool isKing;
            try { isKing = pActor.isKing(); }
            catch { isKing = false; }
            if (isKing && UsesDynasticSystem(pActor)) return true;
            try
            {
                if (HeirService.IsCurrentHeir(pActor.kingdom, pActor) &&
                    UsesDynasticSystem(pActor))
                    return true;
            }
            catch { }
            try
            {
                if (FeudatoryService.IsActivePrince(pActor)) return true;
            }
            catch { }
            if (IsActiveFeudatorySuccessor(pActor)) return true;

            try
            {
                NobleTitleSnapshot title = NobleRankService.ReadHot(pActor);
                return title.IsActive &&
                       title.Style == NobleTitleStyle.Male;
            }
            catch { return false; }
        }

        public static bool IsActiveFeudatorySuccessor(Actor pActor)
        {
            try
            {
                return pActor?.data != null && !pActor.isRekt() &&
                       FeudatoryService.TryGetBySuccessor(pActor.data.id,
                           out FeudatorySnapshot snapshot) &&
                       snapshot.SuccessorActorId == pActor.data.id;
            }
            catch { return false; }
        }

        private static void ProcessLoverMother(Actor pMother)
        {
            pMother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_FATHER_ID,
                out long fatherId, -1L);
            Actor father = ResolveActor(fatherId);
            bool motherAlive = pMother.isAlive() && !pMother.isRekt();
            bool fatherAlive = father?.data != null && father.isAlive() &&
                               !father.isRekt();
            bool mutual = fatherAlive && pMother.lover == father &&
                          father.lover == pMother;
            bool motherAdult = motherAlive && pMother.isAdult();
            bool fatherAdult = fatherAlive && father.isAdult();
            bool motherBreedingAge = motherAdult &&
                                     pMother.isBreedingAge();
            bool fatherBreedingAge = fatherAdult &&
                                     father.isBreedingAge();
            bool motherPregnant = pMother.hasStatus("pregnant");
            bool motherFertile = motherBreedingAge &&
                                  pMother.canProduceBabies();
            bool fatherFertile = fatherBreedingAge && father.canBreed() &&
                                  father.canProduceBabies();
            bool nutrition = motherFertile &&
                             pMother.haveNutritionForNewBaby() &&
                             !pMother.isHungry();
            bool citySafe = IsCitySafe(pMother);
            bool metaRoom = motherAlive &&
                            !BabyHelper.isMetaLimitsReached(pMother);
            bool worldLaw = WorldLawLibrary.world_law_civ_babies
                .isEnabled();
            LoverHeirConceptionDisposition disposition =
                DynasticLoverConceptionRules.Evaluate(
                    !AW3MultiplayerReplicaScope.IsReplicaSession, mutual,
                    motherAlive, fatherAlive, motherAdult, fatherAdult,
                    motherBreedingAge, fatherBreedingAge, motherPregnant,
                    motherFertile, fatherFertile, nutrition, citySafe,
                    metaRoom, worldLaw);
            if (disposition == LoverHeirConceptionDisposition.Cancel)
            {
                ClearLoverRequest(pMother, pKeepCompletedToken: false);
                return;
            }
            if (disposition == LoverHeirConceptionDisposition.Wait)
            {
                Enqueue(pMother.data.id);
                return;
            }

            BabyHelper.babyMakingStart(pMother);
            bool started = pMother.addStatusEffect("pregnant",
                NobleHeirPregnancyRules.TenMonthPregnancySeconds);
            if (!started)
            {
                Enqueue(pMother.data.id);
                return;
            }
            pMother.subspecies.counterReproduction();
            pMother.data.removeBool(
                LineageKeys.DYNASTIC_LOVER_HEIR_PENDING);
            pMother.data.set(LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE, true);
            pMother.data.removeBool(
                LineageKeys.DYNASTIC_LOVER_HEIR_SON_BORN);
            pMother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_ATTEMPTS,
                out int attempts, 0);
            pMother.data.set(LineageKeys.DYNASTIC_LOVER_HEIR_ATTEMPTS,
                attempts == int.MaxValue ? int.MaxValue : attempts + 1);
        }

        private static bool CompleteLoverHeirDelivery(Actor pMother)
        {
            pMother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE,
                out bool active, false);
            if (!active) return false;
            pMother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_SON_BORN,
                out bool sonBorn, false);
            pMother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_FATHER_ID,
                out long fatherId, -1L);
            Actor father = ResolveActor(fatherId);
            bool relationshipValid = father?.data != null &&
                                     father.isAlive() &&
                                     !father.isRekt() &&
                                     pMother.isAlive() &&
                                     !pMother.isRekt() &&
                                     pMother.lover == father &&
                                     father.lover == pMother &&
                                     pMother.isAdult() && father.isAdult() &&
                                     pMother.isBreedingAge() &&
                                     father.isBreedingAge();
            bool retry = DynasticLoverConceptionRules
                .ShouldContinueAfterBirth(active, sonBorn) &&
                         relationshipValid;
            if (retry)
            {
                pMother.data.removeBool(
                    LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE);
                pMother.data.removeBool(
                    LineageKeys.DYNASTIC_LOVER_HEIR_SON_BORN);
                pMother.data.set(LineageKeys.DYNASTIC_LOVER_HEIR_PENDING,
                    true);
                Enqueue(pMother.data.id);
                return true;
            }

            pMother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_RELATION_TOKEN,
                out string token, "");
            bool completed = sonBorn && relationshipValid;
            if (completed && !string.IsNullOrEmpty(token))
                pMother.data.set(
                    LineageKeys.DYNASTIC_LOVER_HEIR_LAST_RELATION_TOKEN,
                    token);
            ClearLoverRequest(pMother, pKeepCompletedToken: completed);
            return true;
        }

        private static void ProcessMother(Actor pMother)
        {
            pMother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_PENDING,
                out bool loverPending, false);
            if (loverPending)
            {
                ProcessLoverMother(pMother);
                return;
            }
            pMother.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_PENDING,
                out bool pending, false);
            if (!pending) return;

            pMother.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID,
                out long storedFatherId, -1L);
            Actor storedFather = ResolveActor(storedFatherId);
            Actor currentPartner = RulerHouseholdPregnancyService
                .ResolveManagedFather(pMother, out _);
            Actor father = currentPartner ?? storedFather;
            bool partnerReady = currentPartner?.data != null &&
                                currentPartner.canBreed() &&
                                currentPartner.canProduceBabies() &&
                                !currentPartner.isFighting();
            bool nobleCouple = IsEligibleNoble(pMother) ||
                               IsEligibleNoble(father);
            bool hasLivingSon = EligibleParentHasLivingSon(pMother, father);
            pMother.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_REQUEST_TIME,
                out float requestTime, -1f);
            float now = CurrentWorldTime();

            bool motherAlive = pMother.isAlive() && !pMother.isRekt();
            bool pregnancyRemoved = !pMother.hasStatus("pregnant");
            bool adult = motherAlive && pMother.isAdult();
            bool breedingAge = adult && pMother.isBreedingAge();
            bool fertile = breedingAge && pMother.canProduceBabies();
            bool nutrition = fertile && pMother.haveNutritionForNewBaby() &&
                              !pMother.isHungry();
            bool citySafe = IsCitySafe(pMother);
            bool personalRoom = motherAlive &&
                                !pMother.hasReachedOffspringLimit();
            bool continuationBypass = motherAlive &&
                (DynasticMaleLineContinuityService.NeedsContinuation(
                     pMother) ||
                 DynasticMaleLineContinuityService.NeedsContinuation(
                     father));
            bool metaRoom = motherAlive &&
                            !BabyHelper.isMetaLimitsReached(pMother);
            bool worldLaw = WorldLawLibrary.world_law_civ_babies.isEnabled();

            NobleHeirRetryDisposition disposition = NobleHeirPregnancyRules
                .EvaluateRetry(
                    pAuthoritative:
                    !AW3MultiplayerReplicaScope.IsReplicaSession,
                    pNextCycleReached: now > requestTime,
                    pMotherAlive: motherAlive,
                    pNobleCoupleEligible: nobleCouple,
                    pEitherEligibleParentHasLivingSon: hasLivingSon,
                    pPartnerReady: partnerReady,
                    pPregnancyRemoved: pregnancyRemoved,
                    pMotherAdult: adult,
                    pMotherBreedingAge: breedingAge,
                    pFertile: fertile,
                    pHasNutrition: nutrition,
                    pCitySafe: citySafe,
                    pPersonalOffspringRoom: personalRoom,
                    pPersonalOffspringLimitBypass: continuationBypass,
                    pMetaLimitRoom: metaRoom,
                    pWorldLawAllows: worldLaw);

            if (disposition == NobleHeirRetryDisposition.Clear)
            {
                ClearAll(pMother);
                return;
            }
            if (disposition == NobleHeirRetryDisposition.Wait)
            {
                Enqueue(pMother.data.id);
                return;
            }

            if (currentPartner.data.id != storedFatherId)
                pMother.data.set(LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID,
                    currentPartner.data.id);

            BabyHelper.babyMakingStart(pMother);
            bool started = pMother.addStatusEffect("pregnant",
                NobleHeirPregnancyRules.TenMonthPregnancySeconds);
            if (started)
            {
                pMother.subspecies.counterReproduction();
                return;
            }
            Enqueue(pMother.data.id);
        }

        private static bool EligibleParentHasLivingSon(Actor pMother,
            Actor pFather)
        {
            return IsEligibleNoble(pMother) &&
                   DynasticLivingSonIndexService.HasLivingSon(pMother) ||
                   IsEligibleNoble(pFather) &&
                   DynasticLivingSonIndexService.HasLivingSon(pFather);
        }

        private static Actor GetMutualLivingPartner(Actor pMother)
        {
            Actor partner = pMother?.lover;
            if (partner?.data == null || !partner.isAlive() ||
                partner.isRekt() || partner.lover != pMother)
                return null;
            return partner;
        }

        private static bool IsPotentialTitleLineMember(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() ||
                pActor.isRekt())
                return false;

            bool actorIsMale = pActor.isSexMale();
            if (HoldsActiveTitle(pActor))
                return DynasticLoverConceptionRules.IsInScope(
                    holdsTitle: true, paternalDistance: 0, actorIsMale);
            if (!actorIsMale) return false;

            Actor ancestor = pActor;
            for (int distance = 1; distance <= 3; distance++)
            {
                ancestor = ResolveLiveFather(ancestor);
                if (ancestor?.data == null) return false;
                if (HoldsActiveTitle(ancestor) &&
                    DynasticLoverConceptionRules.IsInScope(
                        holdsTitle: false, paternalDistance: distance,
                        actorIsMale: true))
                    return true;
            }
            return false;
        }

        private static bool HoldsActiveTitle(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() ||
                pActor.isRekt())
                return false;
            try
            {
                if (pActor.isKing()) return true;
            }
            catch { }
            try
            {
                if (FeudatoryService.IsActivePrince(pActor)) return true;
            }
            catch { }
            try
            {
                return NobleRankService.ReadHot(pActor).IsActive;
            }
            catch
            {
                return false;
            }
        }

        private static Actor ResolveLiveFather(Actor pChild)
        {
            if (pChild?.data == null) return null;
            long first = pChild.data.parent_id_1;
            long second = pChild.data.parent_id_2;
            Actor parent = ResolveActor(first);
            if (IsLiveMale(parent)) return parent;
            if (second == first) return null;
            parent = ResolveActor(second);
            return IsLiveMale(parent) ? parent : null;
        }

        private static bool IsLiveMale(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() &&
                   !pActor.isRekt() && pActor.isSexMale();
        }

        private static string BuildRelationToken(Actor pMother,
            Actor pFather)
        {
            long motherId = pMother?.data?.id ?? -1L;
            long fatherId = pFather?.data?.id ?? -1L;
            long first = Math.Min(motherId, fatherId);
            long second = Math.Max(motherId, fatherId);
            return first.ToString(CultureInfo.InvariantCulture) + ":" +
                   second.ToString(CultureInfo.InvariantCulture) + ":" +
                   CurrentWorldTime().ToString("R",
                       CultureInfo.InvariantCulture);
        }

        private static bool RelationTokenMatchesPair(string pToken,
            Actor pFirst, Actor pSecond)
        {
            if (string.IsNullOrEmpty(pToken) || pFirst?.data == null ||
                pSecond?.data == null)
                return false;
            long first = Math.Min(pFirst.data.id, pSecond.data.id);
            long second = Math.Max(pFirst.data.id, pSecond.data.id);
            string prefix = first.ToString(CultureInfo.InvariantCulture) +
                            ":" +
                            second.ToString(CultureInfo.InvariantCulture) +
                            ":";
            return pToken.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static bool IsCitySafe(Actor pMother)
        {
            if (pMother?.data == null || !pMother.isAlive() ||
                pMother.isRekt() || pMother.isFighting())
                return false;
            City city = pMother?.city;
            if (city?.data == null || city.isRekt() ||
                city.kingdom?.data == null)
                return false;
            if (pMother.kingdom?.data != null &&
                city.kingdom != pMother.kingdom)
                return false;
            return !city.isInDanger();
        }

        private static bool UsesDynasticSystem(Actor pActor)
        {
            return LineageService.IsNativeXiaCultureActor(pActor) ||
                   LineageService.UsesAwLineageSystem(pActor);
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId < 0L) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static float CurrentWorldTime()
        {
            try { return (float)(World.world?.getCurWorldTime() ?? 0d); }
            catch { return 0f; }
        }

        private static void Enqueue(long pMotherId)
        {
            if (pMotherId < 0L || !EnqueuedMothers.Add(pMotherId)) return;
            PendingMothers.Enqueue(pMotherId);
        }

        private static void ClearPendingOnly(Actor pMother)
        {
            pMother.data.removeBool(LineageKeys.DYNASTIC_HEIR_RETRY_PENDING);
            pMother.data.removeFloat(
                LineageKeys.DYNASTIC_HEIR_RETRY_REQUEST_TIME);
            EnqueuedMothers.Remove(pMother.data.id);
        }

        private static void ClearLoverRequest(Actor pMother,
            bool pKeepCompletedToken)
        {
            if (pMother?.data == null) return;
            pMother.data.get(LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE,
                out bool activePregnancy, false);
            pMother.data.removeBool(
                LineageKeys.DYNASTIC_LOVER_HEIR_PENDING);
            pMother.data.removeBool(
                LineageKeys.DYNASTIC_LOVER_HEIR_ACTIVE);
            pMother.data.removeLong(
                LineageKeys.DYNASTIC_LOVER_HEIR_FATHER_ID);
            pMother.data.removeString(
                LineageKeys.DYNASTIC_LOVER_HEIR_RELATION_TOKEN);
            pMother.data.removeInt(
                LineageKeys.DYNASTIC_LOVER_HEIR_ATTEMPTS);
            pMother.data.removeBool(
                LineageKeys.DYNASTIC_LOVER_HEIR_SON_BORN);
            if (!pKeepCompletedToken)
                pMother.data.removeString(
                    LineageKeys.DYNASTIC_LOVER_HEIR_LAST_RELATION_TOKEN);
            if (activePregnancy)
            {
                pMother.data.removeBool(
                    LineageKeys.DYNASTIC_PREGNANCY_MANAGED);
                pMother.data.removeBool(
                    LineageKeys.DYNASTIC_HEIR_RETRY_PENDING);
                pMother.data.removeLong(
                    LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID);
                pMother.data.removeFloat(
                    LineageKeys.DYNASTIC_HEIR_RETRY_REQUEST_TIME);
                RulerHouseholdPregnancyService.ClearConception(pMother);
            }
            EnqueuedMothers.Remove(pMother.data.id);
        }

        private static void ClearAll(Actor pMother)
        {
            if (pMother?.data == null) return;
            ClearPendingOnly(pMother);
            pMother.data.removeBool(LineageKeys.DYNASTIC_PREGNANCY_MANAGED);
            pMother.data.removeLong(LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID);
            RulerHouseholdPregnancyService.ClearConception(pMother);
        }
    }
}
