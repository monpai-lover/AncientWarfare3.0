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

        internal static bool RefreshInstalledKingHome(Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            if (pKingdom?.data == null || king?.data == null ||
                king.isRekt() || king.kingdom != pKingdom ||
                !TryGetValidCapital(pKingdom, out City capital))
                return false;
            if (king.city == capital) return true;
            try
            {
                using (FormalAffiliationTransferScope.Open(
                           king.data.id, pKingdom.id, capital.data.id))
                    king.joinCity(capital);
            }
            catch { return false; }
            if (king.city != capital) return false;
            LineageService.ArchiveActor(king, pAlive: true);
            CitySchoolSnapshotService.MarkActorDirty(king);
            try { king.clearGraphicsFully(); } catch { }
            return true;
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
            EnsureRoyalClanAfterNativeAccession(pKingdom, pActor);
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
            EnsureRoyalClanAfterNativeAccession(pKingdom, king);
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
            City home = ResolveAccessionHome(pKingdom, pActor);
            Kingdom previousKingdom = pActor.kingdom;
            City previousCity = pActor.city;

            try
            {
                pActor.cancelAllBeh();
                if (pActor.kingdom != pKingdom &&
                    pActor.kingdom?.asset == null)
                    pActor.kingdom = null;
                using (FormalAffiliationTransferScope.Open(
                           pActor.data.id, pKingdom.id,
                           home?.data?.id ?? -1L))
                {
                    if (home == null && pActor.city?.kingdom != pKingdom)
                        pActor.setCity(null);
                    if (pActor.kingdom != pKingdom)
                        pActor.joinKingdom(pKingdom);
                    if (home != null && pActor.city != home)
                        pActor.joinCity(home);
                }
            }
            catch
            {
                if (!IsAffiliationCommitted(pActor, pKingdom, home))
                {
                    RestoreAffiliation(
                        pActor,
                        previousKingdom,
                        previousCity);
                    LastPrepareFailureReason =
                        "native_affiliation_transfer";
                    return false;
                }
            }

            if (!IsAffiliationCommitted(pActor, pKingdom, home))
            {
                try
                {
                    using (FormalAffiliationTransferScope.Open(
                               pActor.data.id, pKingdom.id,
                               home?.data?.id ?? -1L))
                    {
                        if (pActor.kingdom != pKingdom)
                        {
                            if (pActor.kingdom?.asset == null)
                                pActor.kingdom = null;
                            pActor.joinKingdom(pKingdom);
                        }
                        if (home != null && pActor.city != home)
                            pActor.joinCity(home);
                    }
                }
                catch { }
            }
            if (!IsAffiliationCommitted(pActor, pKingdom, home))
            {
                RestoreAffiliation(pActor, previousKingdom, previousCity);
                LastPrepareFailureReason = "affiliation_not_committed";
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
            if (!CloseGuestOffice(pActor, "became_king"))
            {
                LastPrepareFailureReason = "guest_office_close";
                return false;
            }

            CourtService.ClearOfficeForReignTransition(pActor, "became_king",
                pPersistCareer: false);
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
            if (previousCity?.leader == pActor)
            {
                try { previousCity.removeLeader(); }
                catch { }
            }
            return true;
        }

        internal static void RestoreAffiliation(Actor pActor,
            Kingdom pPreviousKingdom, City pPreviousCity)
        {
            if (pActor?.data == null) return;
            try
            {
                using (FormalAffiliationTransferScope.Open(
                           pActor.data.id,
                           pPreviousKingdom?.data?.id ?? -1L,
                           pPreviousCity?.data?.id ?? -1L))
                {
                    if (pPreviousCity?.data != null &&
                        !pPreviousCity.isRekt())
                    {
                        if (pActor.city != pPreviousCity)
                            pActor.joinCity(pPreviousCity);
                    }
                    else if (pActor.city != null)
                    {
                        pActor.setCity(null);
                    }

                    if (pPreviousKingdom?.data != null)
                    {
                        if (pActor.kingdom != pPreviousKingdom)
                            pActor.joinKingdom(pPreviousKingdom);
                    }
                    else
                    {
                        pActor.kingdom = pPreviousKingdom;
                    }
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Succession affiliation rollback failed for " +
                                    pActor.data.id + ": " + exception.Message);
            }
        }

        internal static bool IsAffiliationCommitted(
            Actor pActor,
            Kingdom pKingdom,
            City pHome)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pActor.kingdom != pKingdom)
            {
                return false;
            }

            return pHome != null
                ? pActor.city == pHome
                : pActor.city == null ||
                  pActor.city.kingdom == pKingdom;
        }

        public static bool Commit(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pKingdom.king != pActor || pActor.kingdom != pKingdom ||
                pActor.isRekt())
            {
                LastPrepareFailureReason = "commit_state_mismatch";
                return false;
            }
            City home = ResolveAccessionHome(pKingdom, pActor);
            if (home != null && pActor.city != home)
            {
                LastPrepareFailureReason = "commit_home_mismatch";
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

        private static City ResolveAccessionHome(Kingdom pKingdom,
            Actor pActor)
        {
            if (TryGetValidCapital(pKingdom, out City capital)) return capital;
            if (IsValidRealmCity(pActor?.city, pKingdom)) return pActor.city;
            if (pKingdom?.data == null || pKingdom.isRekt()) return null;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (IsValidRealmCity(city, pKingdom)) return city;
                }
            }
            catch { }
            return null;
        }

        private static bool IsValidRealmCity(City pCity, Kingdom pKingdom)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                   pCity.isAlive() && pCity.kingdom == pKingdom;
        }

        internal static void SanitizeRoyalClanBeforeNativeAccession(
            Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return;
            Clan clan = pActor.clan;
            bool invalidClan = clan != null && clan.data == null;
            if (!invalidClan && clan != null)
            {
                try { invalidClan = clan.isRekt(); }
                catch { invalidClan = true; }
            }
            if (invalidClan) pActor.clan = null;

            if (!pKingdom.data.royal_clan_id.hasValue()) return;
            Clan royalClan = null;
            try
            {
                royalClan = World.world?.clans?.get(
                    pKingdom.data.royal_clan_id);
            }
            catch { }
            bool invalidRoyalClan = royalClan?.data == null;
            if (!invalidRoyalClan)
            {
                try { invalidRoyalClan = royalClan.isRekt(); }
                catch { invalidRoyalClan = true; }
            }
            if (invalidRoyalClan) pKingdom.data.royal_clan_id = -1L;
        }

        internal static void EnsureRoyalClanAfterNativeAccession(
            Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null ||
                pKingdom.king != pActor) return;
            try
            {
                if (pActor.clan?.data == null)
                    World.world?.clans?.newClan(pActor,
                        pAddDefaultTraits: true);
                pKingdom.trySetRoyalClan();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Native royal house repair failed for actor " +
                                    pActor.data.id + " in kingdom " +
                                    pKingdom.id + ": " + exception.Message);
            }
        }

        internal static bool CloseGuestOfficeForDesignation(Actor pActor)
        {
            return CloseGuestOffice(pActor, "became_heir");
        }

        private static bool CloseGuestOffice(Actor pActor, string pReason)
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
            if (!SchoolGuestOfficeService.QueueGuestOfficerEnd(pActor, host,
                    pReason, year)) return false;
            ClearGuestStatus(pActor);
            return true;
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
