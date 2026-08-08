using AncientWarfare3.content.schools;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class AccessionIdentityService
    {
        private const int DeferredInstallationBudget = 4;
        private const int DeferredInstallationMaxAttempts = 8;
        private static string LastPrepareFailureReason = "none";
        private static bool _capitalRepairInProgress;
        private sealed class DeferredInstallation
        {
            internal long KingdomId;
            internal long ActorId;
            internal int Attempts;
            internal int NextEligibleFrame;
        }

        private static readonly Dictionary<long, DeferredInstallation>
            DeferredInstallations = new Dictionary<long, DeferredInstallation>();

        internal static void ClearRuntime()
        {
            DeferredInstallations.Clear();
            _capitalRepairInProgress = false;
        }

        internal static void DeferInstalledKing(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pKingdom.isRekt() || pActor.isRekt() ||
                !SuccessionTransitionRules.ShouldUseManagedSuccession(
                    LineageService.IsXiaKingdom(pKingdom),
                    XiaizationService.UsesXiaizedInstitutionSystem(pKingdom)))
                return;
            long kingdomId = pKingdom.data.id;
            long actorId = pActor.data.id;
            if (DeferredInstallations.TryGetValue(kingdomId,
                    out DeferredInstallation existing) &&
                existing.ActorId == actorId)
                return;
            DeferredInstallations[kingdomId] = new DeferredInstallation
            {
                KingdomId = kingdomId,
                ActorId = actorId,
                Attempts = 0,
                NextEligibleFrame = Time.frameCount
            };
        }

        internal static void ClearDeferredInstalledKing(Kingdom pKingdom)
        {
            long kingdomId = pKingdom?.data?.id ?? -1L;
            if (kingdomId >= 0) DeferredInstallations.Remove(kingdomId);
        }

        internal static void ProcessDeferredInstallations()
        {
            if (DeferredInstallations.Count == 0 ||
                World.world?.kingdoms == null || World.world?.units == null)
                return;
            long[] ids = new long[Math.Min(
                DeferredInstallationBudget, DeferredInstallations.Count)];
            int index = 0;
            foreach (long id in DeferredInstallations.Keys)
            {
                ids[index++] = id;
                if (index >= ids.Length) break;
            }

            foreach (long kingdomId in ids)
            {
                if (!DeferredInstallations.TryGetValue(kingdomId,
                        out DeferredInstallation pending))
                    continue;
                if (Time.frameCount < pending.NextEligibleFrame)
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
                    DeferredInstallations.Remove(kingdomId);
                    continue;
                }

                pending.Attempts++;
                LastPrepareFailureReason = "none";
                if (Prepare(kingdom, actor) && Commit(kingdom, actor))
                {
                    CompleteDeferredInstallation(kingdom, actor);
                    DeferredInstallations.Remove(kingdomId);
                    continue;
                }

                pending.NextEligibleFrame = Time.frameCount +
                    AccessionIdentityRules.ResolveDeferredRetryDelay(
                        pending.Attempts);

                if (pending.Attempts >= DeferredInstallationMaxAttempts)
                {
                    ModClass.LogWarning(
                        "Deferred king identity repair exhausted for kingdom " +
                        pending.KingdomId + " actor " + pending.ActorId +
                        " reason=" + LastPrepareFailureReason);
                    DeferredInstallations.Remove(kingdomId);
                }
            }
        }

        private static void CompleteDeferredInstallation(
            Kingdom pKingdom, Actor pActor)
        {
            FormerHeirService.ClearSnapshot(pActor);
            FormerKingService.ClearSnapshot(pActor);
            HeirService.ClearHeir(pKingdom);
            HeirService.RefreshHeir(pKingdom);
            CourtDirectionService.MarkDirty(pKingdom);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
            FamilyTreeProjectionRevision.Advance(
                FamilyTreeProjectionChange.RulerAccession);
            AW3MultiplayerSuccessionFacade.NotifyKingInstalled(
                pKingdom, pActor);
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
            RepublicGovernmentService.MarkMonarchyEstablished(pKingdom);
            FormerHeirService.ClearSnapshot(king);
            FormerKingService.ClearSnapshot(king);
            HeirService.ClearHeir(pKingdom);
            HeirService.RefreshHeir(pKingdom);
            CourtDirectionService.MarkDirty(pKingdom);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
            FamilyTreeProjectionRevision.Advance(
                FamilyTreeProjectionChange.RulerAccession);
            return true;
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
