using System;
using System.Collections.Generic;
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
            out long pFatherId)
        {
            pFatherId = -1L;
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pMother?.data == null || !pMother.isSexFemale())
                return false;

            Actor father = GetMutualLivingPartner(pMother);
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

        public static void OnPregnancyStarted(Actor pMother, long pFatherId)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pMother?.data == null || pFatherId < 0L)
                return;

            pMother.data.set(LineageKeys.DYNASTIC_PREGNANCY_MANAGED, true);
            pMother.data.set(LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID,
                pFatherId);
            ClearPendingOnly(pMother);
        }

        public static void OnPregnancyDeliveryCompleted(Actor pMother)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pMother?.data == null)
                return;

            pMother.data.get(LineageKeys.DYNASTIC_PREGNANCY_MANAGED,
                out bool managed, false);
            if (!managed) return;
            pMother.data.removeBool(LineageKeys.DYNASTIC_PREGNANCY_MANAGED);

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
            Enqueue(pMother.data.id);
        }

        public static void OnActorLoaded(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_PENDING,
                out bool pending, false);
            if (pending) Enqueue(pActor.data.id);
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

        private static void ProcessMother(Actor pMother)
        {
            pMother.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_PENDING,
                out bool pending, false);
            if (!pending) return;

            pMother.data.get(LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID,
                out long storedFatherId, -1L);
            Actor storedFather = ResolveActor(storedFatherId);
            Actor currentPartner = GetMutualLivingPartner(pMother);
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

        private static void ClearAll(Actor pMother)
        {
            if (pMother?.data == null) return;
            ClearPendingOnly(pMother);
            pMother.data.removeBool(LineageKeys.DYNASTIC_PREGNANCY_MANAGED);
            pMother.data.removeLong(LineageKeys.DYNASTIC_HEIR_RETRY_FATHER_ID);
        }
    }
}
