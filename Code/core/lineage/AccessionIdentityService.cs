using AncientWarfare3.content.schools;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    public struct AccessionCompletionContext
    {
        internal Actor PreviousKing;
        internal bool WasRegisteredHeir;
        internal int PreNobleDistance;
        internal string SuccessionSourceMode;
        internal InheritanceLaw AccessionLaw;
        internal bool Captured;
    }

    internal static class AccessionIdentityService
    {
        private const int DeferredInstallationBudget = 4;
        private const int DeferredInstallationMaxAttempts = 8;
        private const int DeferredInstallationExhaustedRetryDelay = 1024;
        private static string LastPrepareFailureReason = "none";
        private static bool _capitalRepairInProgress;
        private enum AccessionCompletionStage
        {
            Snapshots,
            Monarchy,
            LineageBranch,
            Recall,
            DisputePersistence,
            InheritanceBranch,
            ReigningIndex,
            DisputeRuntime,
            ClearHeir,
            RefreshHeir,
            Feudatory,
            Remarriage,
            Court,
            Appellation,
            FamilyProjection,
            Multiplayer,
            Complete
        }

        private sealed class AccessionCompletionProgress
        {
            internal long ActorId;
            internal AccessionCompletionContext Context;
            internal AccessionCompletionStage Stage;
        }

        private sealed class DeferredInstallation
        {
            internal long KingdomId;
            internal long ActorId;
            internal int Attempts;
            internal int NextEligibleFrame;
            internal AccessionCompletionContext CompletionContext;
            internal bool IdentityCommitted;
            internal bool ExhaustionLogged;
        }

        private static readonly Dictionary<long, DeferredInstallation>
            DeferredInstallations = new Dictionary<long, DeferredInstallation>();
        private static readonly List<long> DeferredInstallationOrder =
            new List<long>();
        private static int _deferredInstallationCursor;
        private static readonly Dictionary<long, AccessionCompletionProgress>
            CompletionProgressByKingdom =
                new Dictionary<long, AccessionCompletionProgress>();

        internal static void ClearRuntime()
        {
            DeferredInstallations.Clear();
            DeferredInstallationOrder.Clear();
            _deferredInstallationCursor = 0;
            CompletionProgressByKingdom.Clear();
            _capitalRepairInProgress = false;
        }

        internal static void DeferInstalledKing(Kingdom pKingdom, Actor pActor)
        {
            DeferInstalledKing(pKingdom, pActor, default,
                pIdentityCommitted: false);
        }

        internal static void DeferInstalledKing(Kingdom pKingdom, Actor pActor,
            AccessionCompletionContext pCompletionContext)
        {
            DeferInstalledKing(pKingdom, pActor, pCompletionContext,
                pIdentityCommitted: false);
        }

        internal static void DeferInstalledKing(Kingdom pKingdom, Actor pActor,
            AccessionCompletionContext pCompletionContext,
            bool pIdentityCommitted)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pKingdom.isRekt() || pActor.isRekt() ||
                !SuccessionTransitionRules.ShouldUseManagedSuccession(
                    LineageService.IsXiaKingdom(pKingdom),
                    XiaizationService.UsesXiaizedInstitutionSystem(pKingdom)))
                return;
            long kingdomId = pKingdom.data.id;
            long actorId = pActor.data.id;
            if (CompletionProgressByKingdom.TryGetValue(kingdomId,
                    out AccessionCompletionProgress progress) &&
                progress.ActorId != actorId)
                CompletionProgressByKingdom.Remove(kingdomId);
            bool alreadyTracked = DeferredInstallations.TryGetValue(kingdomId,
                out DeferredInstallation existing);
            if (alreadyTracked && existing.ActorId == actorId)
            {
                if (pCompletionContext.Captured)
                    existing.CompletionContext = pCompletionContext;
                if (pIdentityCommitted) existing.IdentityCommitted = true;
                return;
            }
            DeferredInstallations[kingdomId] = new DeferredInstallation
            {
                KingdomId = kingdomId,
                ActorId = actorId,
                Attempts = 0,
                NextEligibleFrame = Time.frameCount,
                CompletionContext = pCompletionContext,
                IdentityCommitted = pIdentityCommitted,
                ExhaustionLogged = false
            };
            if (!alreadyTracked) DeferredInstallationOrder.Add(kingdomId);
        }

        internal static void ClearDeferredInstalledKing(Kingdom pKingdom)
        {
            long kingdomId = pKingdom?.data?.id ?? -1L;
            if (kingdomId >= 0) RemoveDeferredInstallation(kingdomId);
        }

        internal static void ProcessDeferredInstallations()
        {
            if (DeferredInstallations.Count == 0 ||
                World.world?.kingdoms == null || World.world?.units == null)
                return;
            int selectionBudget = Math.Min(DeferredInstallationBudget,
                DeferredInstallations.Count);
            long[] ids = new long[selectionBudget];
            int selected = 0;
            int inspected = 0;
            int inspectionBudget = Math.Min(DeferredInstallationOrder.Count,
                DeferredInstallationBudget * 4);
            while (selected < selectionBudget &&
                   inspected < inspectionBudget &&
                   DeferredInstallationOrder.Count > 0)
            {
                if (_deferredInstallationCursor >=
                    DeferredInstallationOrder.Count)
                    _deferredInstallationCursor = 0;
                long id = DeferredInstallationOrder[
                    _deferredInstallationCursor++];
                inspected++;
                if (!DeferredInstallations.TryGetValue(id,
                        out DeferredInstallation candidate) ||
                    Time.frameCount < candidate.NextEligibleFrame)
                    continue;
                ids[selected++] = id;
            }

            for (int selectedIndex = 0;
                 selectedIndex < selected;
                 selectedIndex++)
            {
                long kingdomId = ids[selectedIndex];
                if (!DeferredInstallations.TryGetValue(kingdomId,
                        out DeferredInstallation pending))
                    continue;
                Kingdom kingdom = null;
                Actor actor = null;
                try
                {
                    kingdom = World.world.kingdoms.get(pending.KingdomId);
                    actor = World.world.units.get(pending.ActorId);
                }
                catch { }

                if (kingdom?.data == null || actor?.data == null ||
                    kingdom.isRekt() || actor.isRekt() ||
                    kingdom.king != actor)
                {
                    RemoveDeferredInstallation(kingdomId);
                    continue;
                }

                pending.Attempts++;
                LastPrepareFailureReason = "none";
                if (!pending.IdentityCommitted && Prepare(kingdom, actor) &&
                    Commit(kingdom, actor))
                    pending.IdentityCommitted = true;
                if (pending.IdentityCommitted &&
                    CompleteDeferredInstallation(kingdom, actor,
                        pending.CompletionContext))
                {
                    RemoveDeferredInstallation(kingdomId);
                    continue;
                }

                pending.NextEligibleFrame = Time.frameCount +
                    AccessionIdentityRules.ResolveDeferredRetryDelay(
                        pending.Attempts);

                if (pending.Attempts >= DeferredInstallationMaxAttempts)
                {
                    pending.NextEligibleFrame = Time.frameCount +
                        DeferredInstallationExhaustedRetryDelay;
                    if (!pending.ExhaustionLogged)
                    {
                        pending.ExhaustionLogged = true;
                        ModClass.LogWarning(
                            "Deferred king identity repair remains pending for " +
                            "kingdom " + pending.KingdomId + " actor " +
                            pending.ActorId + " reason=" +
                            LastPrepareFailureReason);
                    }
                }
            }
        }

        private static bool CompleteDeferredInstallation(
            Kingdom pKingdom, Actor pActor,
            AccessionCompletionContext pCompletionContext)
        {
            return CompleteInstalledKing(pKingdom, pActor,
                pCompletionContext);
        }

        internal static bool CompleteInstalledKing(Kingdom pKingdom,
            Actor pActor, AccessionCompletionContext pCompletionContext)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pKingdom.king != pActor || pKingdom.isRekt() ||
                pActor.isRekt()) return false;
            long kingdomId = pKingdom.id;
            long actorId = pActor.data.id;
            if (!CompletionProgressByKingdom.TryGetValue(kingdomId,
                    out AccessionCompletionProgress progress) ||
                progress.ActorId != actorId)
            {
                progress = new AccessionCompletionProgress
                {
                    ActorId = actorId,
                    Context = pCompletionContext,
                    Stage = AccessionCompletionStage.Snapshots
                };
                CompletionProgressByKingdom[kingdomId] = progress;
            }
            else if (pCompletionContext.Captured &&
                     !progress.Context.Captured)
                progress.Context = pCompletionContext;

            try
            {
                while (progress.Stage != AccessionCompletionStage.Complete)
                {
                    switch (progress.Stage)
                    {
                        case AccessionCompletionStage.Snapshots:
                            FormerHeirService.ClearSnapshot(pActor);
                            FormerKingService.ClearSnapshot(pActor);
                            progress.Stage = AccessionCompletionStage.Monarchy;
                            break;
                        case AccessionCompletionStage.Monarchy:
                            if (SuccessionTransitionRules
                                .ShouldMarkMonarchyEstablished(true,
                                    RepublicGovernmentService.IsRepublic(
                                        pKingdom),
                                    RepublicGovernmentService
                                        .IsRepublicLeader(pActor)))
                                RepublicGovernmentService
                                    .MarkMonarchyEstablished(pKingdom);
                            progress.Stage =
                                AccessionCompletionStage.LineageBranch;
                            break;
                        case AccessionCompletionStage.LineageBranch:
                            if (progress.Context.Captured)
                                LineageService.OnKingFoundBranch(pKingdom,
                                    pActor, progress.Context.PreviousKing,
                                    progress.Context.WasRegisteredHeir,
                                    progress.Context.PreNobleDistance,
                                    progress.Context.SuccessionSourceMode);
                            progress.Stage = AccessionCompletionStage.Recall;
                            break;
                        case AccessionCompletionStage.Recall:
                            if (progress.Context.Captured)
                                HeirService.RecallForSuccession(pKingdom,
                                    pActor,
                                    progress.Context.WasRegisteredHeir);
                            progress.Stage =
                                AccessionCompletionStage.DisputePersistence;
                            break;
                        case AccessionCompletionStage.DisputePersistence:
                            if (progress.Context.Captured)
                                SuccessionDisputePersistenceService.EnqueueInstalledSuccession(
                                    pKingdom, progress.Context.PreviousKing,
                                    pActor,
                                    progress.Context.SuccessionSourceMode,
                                    progress.Context.AccessionLaw);
                            progress.Stage =
                                AccessionCompletionStage.InheritanceBranch;
                            break;
                        case AccessionCompletionStage.InheritanceBranch:
                            if (progress.Context.Captured)
                                InheritanceLawService.EstablishHereditaryBranchAfterAccession(
                                    pKingdom, pActor,
                                    progress.Context.SuccessionSourceMode);
                            progress.Stage =
                                AccessionCompletionStage.ReigningIndex;
                            break;
                        case AccessionCompletionStage.ReigningIndex:
                            ReigningRoyalLineageIndex.OnKingInstalled(
                                pKingdom, pActor);
                            progress.Stage =
                                AccessionCompletionStage.DisputeRuntime;
                            break;
                        case AccessionCompletionStage.DisputeRuntime:
                            SuccessionDisputeService.OnSuccessorInstalled(
                                pKingdom, pActor);
                            progress.Stage = AccessionCompletionStage.ClearHeir;
                            break;
                        case AccessionCompletionStage.ClearHeir:
                            HeirService.ClearHeir(pKingdom);
                            progress.Stage =
                                AccessionCompletionStage.RefreshHeir;
                            break;
                        case AccessionCompletionStage.RefreshHeir:
                            HeirService.RefreshHeir(pKingdom);
                            progress.Stage = AccessionCompletionStage.Feudatory;
                            break;
                        case AccessionCompletionStage.Feudatory:
                            FeudatoryService.OnPrinceAccededToEmpire(
                                pKingdom, pActor);
                            progress.Stage =
                                AccessionCompletionStage.Remarriage;
                            break;
                        case AccessionCompletionStage.Remarriage:
                            NobleRemarriageService.MarkDirty(pKingdom);
                            progress.Stage = AccessionCompletionStage.Court;
                            break;
                        case AccessionCompletionStage.Court:
                            CourtDirectionService.MarkDirty(pKingdom);
                            progress.Stage =
                                AccessionCompletionStage.Appellation;
                            break;
                        case AccessionCompletionStage.Appellation:
                            RulerAppellationService.RefreshLivingProjection(
                                pKingdom);
                            progress.Stage =
                                AccessionCompletionStage.FamilyProjection;
                            break;
                        case AccessionCompletionStage.FamilyProjection:
                            FamilyTreeProjectionRevision.Advance(
                                FamilyTreeProjectionChange.RulerAccession);
                            progress.Stage =
                                AccessionCompletionStage.Multiplayer;
                            break;
                        case AccessionCompletionStage.Multiplayer:
                            AW3MultiplayerSuccessionFacade.NotifyKingInstalled(
                                pKingdom, pActor);
                            progress.Stage = AccessionCompletionStage.Complete;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                ModClass.LogWarning("Accession completion deferred for kingdom " +
                                    kingdomId + " actor " + actorId +
                                    " stage=" + progress.Stage + " error=" +
                                    ex.GetType().Name);
                return false;
            }
            return true;
        }

        public static bool FinalizeDeferredFounding(Kingdom pKingdom)
        {
            if (_capitalRepairInProgress) return false;
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            Actor king = pKingdom?.king;
            City capital = pKingdom?.capital;
            bool livingKing = false;
            try
            {
                livingKing = king?.data != null && !king.isRekt() &&
                             king.isAlive();
            }
            catch { }
            bool validCapital = capital?.data != null && !capital.isRekt() &&
                                capital.kingdom == pKingdom;
            if (!AccessionIdentityRules.ShouldFinalizeDeferredFounding(
                    pUsesManagedSuccession:
                    SuccessionTransitionRules.ShouldUseManagedSuccession(
                        LineageService.IsXiaKingdom(pKingdom),
                        XiaizationService.UsesXiaizedInstitutionSystem(
                            pKingdom)),
                    pHasLivingKing: livingKing,
                    pHasValidCapital: validCapital,
                    pKingJoinedKingdom: king?.kingdom == pKingdom,
                    pKingLivesInCapital: king?.city == capital,
                    pMonarchyEstablished:
                    RepublicGovernmentService.HasEstablishedMonarchy(
                        pKingdom),
                    pIsRepublic: RepublicGovernmentService.IsRepublic(
                        pKingdom),
                    pIsRepublicLeader:
                    RepublicGovernmentService.IsRepublicLeader(king)))
                return false;

            if (!Prepare(pKingdom, king) || !Commit(pKingdom, king))
                return false;
            AccessionCompletionContext context = default;
            if (DeferredInstallations.TryGetValue(pKingdom.id,
                    out DeferredInstallation pending) &&
                pending.ActorId == king.data.id)
            {
                context = pending.CompletionContext;
                pending.IdentityCommitted = true;
            }
            bool completed = CompleteInstalledKing(pKingdom, king, context);
            if (completed) RemoveDeferredInstallation(pKingdom.id);
            return completed;
        }

        private static void RemoveDeferredInstallation(long pKingdomId)
        {
            if (!DeferredInstallations.Remove(pKingdomId)) return;
            int orderIndex = DeferredInstallationOrder.IndexOf(pKingdomId);
            if (orderIndex < 0) return;
            DeferredInstallationOrder.RemoveAt(orderIndex);
            if (orderIndex < _deferredInstallationCursor)
                _deferredInstallationCursor--;
            if (_deferredInstallationCursor < 0 ||
                _deferredInstallationCursor >=
                    DeferredInstallationOrder.Count)
                _deferredInstallationCursor = 0;
        }

        public static bool Prepare(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pActor.isRekt())
            {
                LastPrepareFailureReason = "invalid_actor_or_kingdom";
                return false;
            }
            bool registeredHeir = HeirService.IsCurrentHeir(pKingdom,
                pActor);
            bool guardIdentity = RoyalGuardService.IsRoyalGuard(pActor);
            if ((registeredHeir || guardIdentity) &&
                !RoyalGuardService.ReleaseForAccession(pKingdom, pActor))
            {
                LastPrepareFailureReason = "royal_guard_release";
                return false;
            }
            if (!RoyalGuardOfficeRules.CanReplaceLifetimeGuardIdentity(
                RoyalGuardService.IsRoyalGuard(pActor)))
            {
                LastPrepareFailureReason = "royal_guard_identity";
                return false;
            }
            if (!TryRepairCapital(pKingdom, out City capital))
            {
                LastPrepareFailureReason = "invalid_capital";
                return false;
            }
            if (!CloseGuestOffice(pActor))
            {
                LastPrepareFailureReason = "guest_office_close";
                return false;
            }

            CourtService.ClearOfficeForReignTransition(pActor, "became_king");
            GeneralService.RetireForSuccession(pActor);
            RoyalGuardService.DismissGuard(pActor, "became_king");
            TemporaryLevyService.OnActorInvalidated(pActor);
            WartimeGarrisonService.OnActorInvalidated(pActor);

            Army previousArmy = pActor.army;
            if (previousArmy != null)
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
                AWArmyService.TryRemoveEmptyArmy(previousArmy);
            }

            if (SlaveService.IsSlave(pActor))
            {
                SlaveService.FreeSlave(pActor, "became_king");
                LineageService.OnActorPromoted(pActor, NobleTrigger.King);
            }

            pActor.data.set(LineageKeys.CAPTIVE_NOBLE_TITLE, "");
            pActor.data.set(LineageKeys.CAPTIVE_NOBLE_COLOR, "");

            City previousCity = pActor.city;
            if (previousCity?.leader == pActor)
            {
                try { previousCity.removeLeader(); }
                catch { return false; }
            }

            try
            {
                pActor.cancelAllBeh();
                if (pActor.kingdom != pKingdom)
                    pActor.kingdom = null;
                using (FormalAffiliationTransferScope.Open(
                           pActor.data.id, pKingdom.id, capital.data.id))
                {
                    if (pActor.kingdom != pKingdom)
                        pActor.joinKingdom(pKingdom);
                    if (pActor.city != capital)
                        pActor.joinCity(capital);
                }
            }
            catch
            {
                LastPrepareFailureReason = "native_affiliation_transfer";
                return false;
            }

            if (pActor.kingdom != pKingdom || pActor.city != capital)
            {
                try
                {
                    if (pActor.kingdom != pKingdom)
                    {
                        pActor.kingdom = null;
                        pActor.joinKingdom(pKingdom);
                    }
                    if (pActor.city != capital)
                        pActor.joinCity(capital);
                }
                catch { }
            }
            if (pActor.kingdom != pKingdom || pActor.city != capital)
            {
                LastPrepareFailureReason = "affiliation_not_committed";
                return false;
            }
            return true;
        }

        public static bool Commit(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pKingdom.king != pActor || pActor.kingdom != pKingdom ||
                pActor.isRekt() || !TryGetValidCapital(pKingdom,
                    out City capital) || pActor.city != capital)
            {
                LastPrepareFailureReason = "commit_state_mismatch";
                return false;
            }
            LineageService.ArchiveActor(pActor, pAlive: true);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            pActor.clearGraphicsFully();
            return true;
        }

        private static bool TryGetValidCapital(Kingdom pKingdom,
            out City pCapital)
        {
            pCapital = pKingdom?.capital;
            return pCapital?.data != null && !pCapital.isRekt() &&
                   pCapital.kingdom == pKingdom;
        }

        private static bool TryRepairCapital(Kingdom pKingdom,
            out City pCapital)
        {
            if (TryGetValidCapital(pKingdom, out pCapital)) return true;
            pCapital = null;
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            try
            {
                City best = null;
                int bestScore = int.MinValue;
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom || !city.isAlive()) continue;
                    int score = 0;
                    try { score += city.getPopulationPeople() * 4; } catch { }
                    try { score += city.zones.Count; } catch { }
                    if (best == null || score > bestScore)
                    {
                        best = city;
                        bestScore = score;
                    }
                }
                if (best == null) return false;
                _capitalRepairInProgress = true;
                try { pKingdom.setCapital(best); }
                finally { _capitalRepairInProgress = false; }
                return TryGetValidCapital(pKingdom, out pCapital);
            }
            catch { return false; }
        }

        private static bool CloseGuestOffice(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            if (affiliation?.LifecycleState !=
                    HistoricalSchoolLifecycleState.Serving ||
                affiliation.ServiceKingdomId < 0)
            {
                ClearGuestStatus(pActor);
                return true;
            }

            Kingdom host = HistoricalAffiliationService.ServiceKingdom(pActor);
            int year = Date.getCurrentYear();
            if (host?.data == null || host.isRekt())
            {
                if (HistoricalAffiliationService.EndService(pActor, year))
                {
                    ClearGuestStatus(pActor);
                    return true;
                }
            }
            GuestOfficeEndRequest request = GuestOfficeEndPersistence.PrepareEnd(
                affiliation, "became_king", year, LineageService.CurTime());
            if (request == null)
                return HistoricalAffiliationService.EndService(pActor, year);
            GuestOfficeEndResult result = GuestOfficeEndPersistence.End(request);
            if (result == null || !result.Persistence.IsCommitted ||
                result.Affiliation == null)
                return HistoricalAffiliationService.EndService(pActor, year);
            if (!HistoricalAffiliationService.AdoptCommittedServiceEnd(
                result.Affiliation))
                return HistoricalAffiliationService.EndService(pActor, year);
            bool applied = CourtService.ApplyCommittedGuestOfficerEnd(pActor,
                host, request.HostKingdomId, request.OfficeId, "became_king");
            if (applied) ClearGuestStatus(pActor);
            return applied;
        }

        private static void ClearGuestStatus(Actor pActor)
        {
            try
            {
                pActor?.finishStatusEffect(
                    HistoricalSchoolContent.GuestStatusId);
            }
            catch { }
        }
    }
}
