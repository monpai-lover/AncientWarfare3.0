using AncientWarfare3.content.schools;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class AccessionIdentityService
    {
        private const int DeferredInstallationBudget = 4;
        private const int DeferredInstallationMaxAttempts = 8;
        private sealed class DeferredInstallation
        {
            internal long KingdomId;
            internal long ActorId;
            internal int Attempts;
        }

        private static readonly Dictionary<long, DeferredInstallation>
            DeferredInstallations = new Dictionary<long, DeferredInstallation>();

        internal static void ClearRuntime()
        {
            DeferredInstallations.Clear();
        }

        internal static void DeferInstalledKing(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pKingdom.isRekt() || pActor.isRekt() ||
                !SuccessionTransitionRules.ShouldUseManagedSuccession(
                    LineageService.IsXiaKingdom(pKingdom),
                    XiaizationService.UsesXiaizedInstitutionSystem(pKingdom)))
                return;
            DeferredInstallations[pKingdom.data.id] = new DeferredInstallation
            {
                KingdomId = pKingdom.data.id,
                ActorId = pActor.data.id,
                Attempts = 0
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
                if (Prepare(kingdom, actor) && Commit(kingdom, actor))
                {
                    CompleteDeferredInstallation(kingdom, actor);
                    DeferredInstallations.Remove(kingdomId);
                    continue;
                }

                if (pending.Attempts >= DeferredInstallationMaxAttempts)
                {
                    ModClass.LogWarning(
                        "Deferred king identity repair exhausted for kingdom " +
                        pending.KingdomId + " actor " + pending.ActorId);
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
                pActor.isRekt()) return false;
            bool registeredHeir = HeirService.IsCurrentHeir(pKingdom,
                pActor);
            if (registeredHeir &&
                !RoyalGuardService.ReleaseForRegisteredHeir(pKingdom,
                    pActor, "became_king")) return false;
            if (!RoyalGuardOfficeRules.CanReplaceLifetimeGuardIdentity(
                    RoyalGuardService.IsRoyalGuard(pActor))) return false;
            if (!TryGetValidCapital(pKingdom, out City capital)) return false;
            if (pActor.isKing() && pActor.kingdom != pKingdom) return false;

            if (!CloseGuestOffice(pActor)) return false;

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
                return false;
            }

            if (pActor.kingdom != pKingdom || pActor.city != capital)
                return false;
            return true;
        }

        public static bool Commit(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pKingdom.king != pActor || pActor.kingdom != pKingdom ||
                pActor.isRekt() || !TryGetValidCapital(pKingdom,
                    out City capital) || pActor.city != capital) return false;
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
            GuestOfficeEndRequest request = GuestOfficeEndPersistence.PrepareEnd(
                affiliation, "became_king", year, LineageService.CurTime());
            if (request == null) return false;
            GuestOfficeEndResult result = GuestOfficeEndPersistence.End(request);
            if (result == null || !result.Persistence.IsCommitted ||
                result.Affiliation == null) return false;
            if (!HistoricalAffiliationService.AdoptCommittedServiceEnd(
                    result.Affiliation)) return false;
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
