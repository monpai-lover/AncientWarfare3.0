using System;
using System.Collections.Generic;
using AncientWarfare3.core.schools;
using AncientWarfare3.core.presentation;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class MilitaryGovernorateSuccessionService
    {
        private const string DeathQueuePrefix =
            "military_governorate_succession:death:";
        private const string RecoveryQueuePrefix =
            "military_governorate_succession:recovery:";

        public static void OnRulerDied(Kingdom pSubject, long pRulerActorId)
        {
            if (pSubject?.data == null || pSubject.id < 0 ||
                pRulerActorId < 0) return;
            long subjectId = pSubject.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeathQueuePrefix + subjectId + ":" + pRulerActorId,
                DeferredWorkClass.CriticalRuntime,
                () => Process(subjectId, pRulerActorId));
        }

        public static void OnKingdomYear(Kingdom pSubject)
        {
            if (!IsActiveSubject(pSubject)) return;
            EnqueueRecovery(pSubject);
        }

        private static void EnqueueRecovery(Kingdom pSubject)
        {
            if (pSubject?.data == null || pSubject.id < 0) return;
            long subjectId = pSubject.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                RecoveryQueuePrefix + subjectId,
                DeferredWorkClass.CriticalRuntime,
                () => Process(subjectId, -1L));
        }

        public static bool TryDesignate(Kingdom pSuzerain,
            Kingdom pSubject, Actor pCandidate, out string pReason)
        {
            pReason = "invalid_governorate";
            if (!TryReadManagedState(pSuzerain, pSubject,
                    out MilitaryGovernorateSnapshot state)) return false;
            if (!IsEligibleDesignated(pCandidate, pSubject, pSuzerain))
            {
                pReason = "invalid_general";
                return false;
            }
            if (!MilitaryGovernorateStore.SetSuccessor(state.StateId,
                    pCandidate.data.id))
            {
                pReason = "persistence_failed";
                return false;
            }
            ProjectSuccessor(pSubject, pCandidate.data.id);
            try
            {
                ChronicleEvents.OnHeirDesignated(pSubject, pSubject.king,
                    pCandidate, HeirTitleSelectionRules.MilitaryAcclaimMode);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Military governorate designation chronicle failed: " +
                    error.Message);
            }
            pReason = "ok";
            return true;
        }

        public static Actor GetDesignatedSuccessorForReadModel(
            Kingdom pSubject)
        {
            if (!IsActiveSubject(pSubject)) return null;
            pSubject.data.get(
                LineageKeys.MILITARY_GOVERNORATE_SUCCESSOR_ACTOR_ID,
                out long actorId, -1L);
            Actor actor = FindActor(actorId);
            return IsLiving(actor) ? actor : null;
        }

        public static bool CanReplaceGovernorForReadModel(Kingdom pSubject)
        {
            if (!IsActiveSubject(pSubject)) return false;
            pSubject.data.get(
                LineageKeys.MILITARY_GOVERNORATE_REPLACEMENT_ALLOWED,
                out bool allowed, false);
            return allowed;
        }

        public static bool TryReplaceGovernor(Kingdom pSuzerain,
            Kingdom pSubject, Actor pGovernor, out string pReason)
        {
            pReason = "invalid_governorate";
            if (!TryReadManagedState(pSuzerain, pSubject,
                    out MilitaryGovernorateSnapshot state)) return false;
            if (!CanReplaceGovernorForReadModel(pSubject))
            {
                pReason = "replacement_not_allowed";
                return false;
            }
            if (!IsEligibleDesignated(pGovernor, pSubject, pSuzerain))
            {
                pReason = "invalid_general";
                return false;
            }

            if (!MilitaryGovernorateStore.SetSuccessor(state.StateId,
                    pGovernor.data.id))
            {
                pReason = "persistence_failed";
                return false;
            }
            ProjectSuccessor(pSubject, pGovernor.data.id);
            if (!Commit(pSubject, pSuzerain, state, pGovernor))
            {
                EnqueueRecovery(pSubject);
                pReason = "replacement_pending";
                return false;
            }
            pReason = "ok";
            return true;
        }

        private static void Process(long pKingdomId, long pRulerActorId)
        {
            Kingdom subject = FindKingdom(pKingdomId);
            if (!IsActiveSubject(subject) ||
                !MilitaryGovernorateStore.TryGetActive(subject,
                    out MilitaryGovernorateSnapshot state)) return;
            if (pRulerActorId >= 0 &&
                state.GovernorActorId != pRulerActorId) return;

            Actor recordedGovernor = FindActor(state.GovernorActorId);
            if (IsLivingRuler(recordedGovernor, subject) &&
                subject.king == recordedGovernor)
            {
                if (state.SuccessionState != 0)
                    MilitaryGovernorateStore.SetSuccessionState(
                        state.StateId, 0);
                return;
            }

            Kingdom suzerain = FindKingdom(state.SuzerainKingdomId);
            Actor designated = FindActor(state.SuccessorActorId);
            bool eligibleDesignated = IsEligibleDesignated(
                designated, subject, suzerain);
            bool currentSubjectKing = IsLivingRuler(designated, subject) &&
                                      subject.king == designated;
            if (MilitaryGovernorateSuccessionRules.CanCommitDesignated(
                    IsLiving(designated), eligibleDesignated,
                    currentSubjectKing))
            {
                Commit(subject, suzerain, state, designated);
                return;
            }
            if (state.SuccessorActorId >= 0)
            {
                MilitaryGovernorateStore.SetSuccessor(state.StateId, -1L);
                ProjectSuccessor(subject, -1L);
            }

            int year = SafeYear();
            int pendingSinceYear = DecodePendingYear(state.SuccessionState);
            if (pendingSinceYear < 0)
            {
                pendingSinceYear = year;
                MilitaryGovernorateStore.SetSuccessionState(state.StateId,
                    EncodePendingYear(year));
            }
            if (MilitaryGovernorateSuccessionRules.ShouldWaitForSuzerain(
                    year, pendingSinceYear,
                    IsSuzerainStable(suzerain, subject))) return;

            Actor elected = SelectLocalGeneral(subject, year);
            if (elected != null && MilitaryGovernorateStore.SetSuccessor(
                    state.StateId, elected.data.id))
            {
                ProjectSuccessor(subject, elected.data.id);
                Commit(subject, suzerain, state, elected);
            }
        }

        private static Actor SelectLocalGeneral(Kingdom pSubject, int pYear)
        {
            List<GeneralReadModelEntry> entries =
                GeneralService.GetActiveGeneralsForReadModel(pSubject,
                    pAllowUnitFallback: false,
                    pLimit: MilitaryGovernorateSuccessionRules.CandidateLimit);
            Actor best = null;
            int bestScore = int.MinValue;
            long bestId = long.MaxValue;
            int limit = Math.Min(entries.Count,
                MilitaryGovernorateSuccessionRules.CandidateLimit);
            for (int i = 0; i < limit; i++)
            {
                GeneralReadModelEntry entry = entries[i];
                Actor actor = entry?.Actor;
                if (!IsEligibleLocalElectionCandidate(actor, pSubject))
                    continue;
                int serviceYears = entry.AppointmentYear < 0 || pYear < 0
                    ? 0
                    : Math.Max(0, pYear - entry.AppointmentYear);
                int score = MilitaryGovernorateSuccessionRules.ElectionScore(
                    entry.Merit, SafeProwess(actor),
                    GeneralService.CountPersonalPower(actor), serviceYears);
                long actorId = actor.data.id;
                if (best == null ||
                    MilitaryGovernorateSuccessionRules.CompareCandidate(
                        score, actorId, bestScore, bestId) < 0)
                {
                    best = actor;
                    bestScore = score;
                    bestId = actorId;
                }
            }
            return best;
        }

        private static bool Commit(Kingdom pSubject, Kingdom pSuzerain,
            MilitaryGovernorateSnapshot pState, Actor pSuccessor)
        {
            if (pSubject?.data == null || pState == null ||
                pSubject.capital?.data == null) return false;

            bool alreadyKing = IsLivingRuler(pSuccessor, pSubject) &&
                               pSubject.king == pSuccessor;
            if (!alreadyKing && !IsEligibleDesignated(
                    pSuccessor, pSubject, pSuzerain)) return false;

            long sourceKingdomId = pSuccessor.kingdom?.id ?? -1L;
            long oldGovernorActorId = pSubject.king?.data?.id ?? -1L;
            try
            {
                if (!alreadyKing)
                {
                    if (pSuccessor.hasArmy()) pSuccessor.removeFromArmy();
                    if (pSuccessor.isCityLeader() &&
                        pSuccessor.city?.leader == pSuccessor)
                        pSuccessor.city.removeLeader();
                    pSuccessor.stopBeingWarrior();

                    if (MilitaryGovernorateSuccessionRules.ShouldMoveAtCommit(
                            sourceKingdomId, pSubject.id))
                    {
                        using (FormalAffiliationTransferScope.Open(
                                   pSuccessor.data.id, pSubject.id,
                                   pSubject.capital.id))
                        {
                            pSuccessor.joinKingdom(pSubject);
                            pSuccessor.joinCity(pSubject.capital);
                        }
                    }
                    else if (pSuccessor.city != pSubject.capital)
                    {
                        pSuccessor.joinCity(pSubject.capital);
                    }

                    if (pSubject.king?.data != null &&
                        pSubject.king != pSuccessor)
                        pSubject.kingLeftEvent();
                    pSubject.setKing(pSuccessor);
                    MilitaryGovernorateAppearanceService.OnGovernorChanged(
                        oldGovernorActorId, pSuccessor.data.id);
                }
                pSuccessor.setProfession(UnitProfession.King);
                if (pSubject.king != pSuccessor) return false;

                GeneralService.RetireForSuccession(pSuccessor);
                HeirService.ClearHeir(pSubject);
                if (!ChronicleEvents.TryRecordMilitaryGovernorateSucceeded(
                        pState.StateId, pSuzerain, pSubject, pSuccessor))
                    return false;
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Military governorate succession commit failed: " +
                    error.Message);
                return false;
            }
            bool stateSaved = MilitaryGovernorateStore.CommitSuccession(
                pState.StateId, pSuccessor.data.id);
            if (!stateSaved) return false;
            ProjectSuccessor(pSubject, -1L);
            pSubject.data.set(
                LineageKeys.MILITARY_GOVERNORATE_REPLACEMENT_ALLOWED,
                false);
            try { WorldLog.logNewKing(pSubject); }
            catch { }
            return true;
        }

        private static bool TryReadManagedState(Kingdom pSuzerain,
            Kingdom pSubject, out MilitaryGovernorateSnapshot pState)
        {
            pState = null;
            if (pSuzerain?.data == null || pSubject?.data == null ||
                pSuzerain.isRekt() || pSubject.isRekt() ||
                VassalService.GetSuzerain(pSubject) != pSuzerain ||
                VassalService.GetSubjectKind(pSubject) !=
                VassalSubjectKind.MilitaryGovernorate ||
                !MilitaryGovernorateStore.TryGetActive(pSubject,
                    out pState)) return false;
            return pState.SuzerainKingdomId == pSuzerain.id &&
                   pState.SubjectKingdomId == pSubject.id;
        }

        private static bool IsEligibleDesignated(Actor pActor,
            Kingdom pSubject, Kingdom pSuzerain)
        {
            if (pActor?.data == null || pSubject?.data == null) return false;
            Kingdom realm = pActor.kingdom;
            return MilitaryGovernorateSuccessionRules.CanDesignate(
                GeneralService.IsGeneral(pActor), IsLiving(pActor),
                SafeAdult(pActor), pActor.isKing(),
                realm == pSubject, realm == pSuzerain &&
                                   VassalService.GetSuzerain(pSubject) ==
                                   pSuzerain);
        }

        private static bool IsEligibleLocalElectionCandidate(Actor pActor,
            Kingdom pSubject)
        {
            return pActor?.kingdom == pSubject &&
                   IsEligibleDesignated(pActor, pSubject,
                       VassalService.GetSuzerain(pSubject));
        }

        private static bool IsActiveSubject(Kingdom pSubject)
        {
            return pSubject?.data != null && !pSubject.isRekt() &&
                   pSubject.isCiv() &&
                   VassalService.GetSubjectKind(pSubject) ==
                   VassalSubjectKind.MilitaryGovernorate;
        }

        private static bool IsSuzerainStable(Kingdom pSuzerain,
            Kingdom pSubject)
        {
            if (pSuzerain?.data == null || pSubject?.data == null ||
                pSuzerain.isRekt() || !pSuzerain.isCiv() ||
                pSuzerain.capital?.data == null ||
                VassalService.GetSuzerain(pSubject) != pSuzerain ||
                !IsLiving(pSuzerain.king)) return false;
            float centerPower = VassalService.GetPowerScore(pSuzerain,
                pIncludeVassals: false);
            float localPower = VassalService.GetPowerScore(pSubject,
                pIncludeVassals: false);
            return centerPower >= Math.Max(1f, localPower);
        }

        private static bool IsLivingRuler(Actor pActor, Kingdom pSubject)
        {
            return IsLiving(pActor) && pActor.kingdom == pSubject &&
                   pActor.isKing();
        }

        private static bool IsLiving(Actor pActor)
        {
            try
            {
                return pActor?.data != null && !pActor.isRekt() &&
                       pActor.isAlive();
            }
            catch { return false; }
        }

        private static bool SafeAdult(Actor pActor)
        {
            try { return pActor?.isAdult() == true; }
            catch { return false; }
        }

        private static int SafeProwess(Actor pActor)
        {
            float damage = SafeStat(pActor, "damage");
            float warfare = SafeStat(pActor, "warfare");
            if (warfare <= 0f)
            {
                try { warfare = Math.Max(0, pActor?.warfare ?? 0); }
                catch { warfare = 0f; }
            }
            double value = Math.Max(0d, damage) +
                           Math.Max(0d, warfare) * 2d;
            return value >= int.MaxValue ? int.MaxValue :
                Mathf.RoundToInt((float)value);
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }

        private static int EncodePendingYear(int pYear)
        {
            return pYear < 0 ? 1 : pYear + 1;
        }

        private static int DecodePendingYear(int pState)
        {
            return pState <= 0 ? -1 : pState - 1;
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return -1; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static void ProjectSuccessor(Kingdom pSubject, long pActorId)
        {
            bool active = MilitaryGovernorateStore.TryGetRuntimeProjection(
                pSubject, out _, out long oldActorId);
            pSubject?.data?.set(
                LineageKeys.MILITARY_GOVERNORATE_SUCCESSOR_ACTOR_ID,
                pActorId);
            if (active && oldActorId != pActorId)
                MilitaryGovernorateAppearanceService.OnProjectionChanged(
                    pSubject, true, oldActorId, true, pActorId);
        }
    }
}
