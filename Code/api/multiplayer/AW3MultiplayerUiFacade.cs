using System;
using AncientWarfare3.ui.windows;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerUiFacade
    {
        public static AW3WindowOpenResult TryOpen(
            AW3WindowOpenRequest request)
        {
            if (request == null)
                return new AW3WindowOpenResult(
                    AW3WindowOpenStatus.InvalidContext,
                    AW3WindowKind.LineageOverview);

            AW3WindowDescriptor descriptor;
            try
            {
                descriptor = AW3MultiplayerCatalog.GetWindow(request.Kind);
            }
            catch
            {
                return new AW3WindowOpenResult(
                    AW3WindowOpenStatus.InvalidContext, request.Kind);
            }

            if (!request.IsValidFor(descriptor))
                return Result(AW3WindowOpenStatus.InvalidContext,
                    request.Kind);
            if (!ContextExists(request, descriptor.Requirements))
                return Result(AW3WindowOpenStatus.NotFound, request.Kind);

            try
            {
                switch (request.Kind)
                {
                    case AW3WindowKind.LineageOverview:
                        LineageOverviewWindow.Open();
                        break;
                    case AW3WindowKind.ShiBranchList:
                        if (request.CityId > 0)
                            ShiBranchListWindow.OpenForCity(request.CityId);
                        else
                            ShiBranchListWindow.OpenFor(request.Key);
                        break;
                    case AW3WindowKind.FamilyTree:
                        if (request.ActorId > 0)
                            FamilyTreeWindow.OpenFamilyTree(request.ActorId,
                                request.ShiId);
                        else
                            FamilyTreeWindow.OpenBigTree(request.ShiId);
                        break;
                    case AW3WindowKind.History:
                        OpenHistory(request);
                        break;
                    case AW3WindowKind.KingdomRoster:
                        KingdomRosterWindow.Open();
                        break;
                    case AW3WindowKind.PolicyTree:
                        KingdomPolicyWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.AncestryAnalysis:
                        AncestryAnalysisWindow.Open(request.ActorId);
                        break;
                    case AW3WindowKind.MandateDynasty:
                        MandateDynastyWindow.Open();
                        break;
                    case AW3WindowKind.MandateCycle:
                        MandateCycleWindow.Open();
                        break;
                    case AW3WindowKind.MandateDecisions:
                        MandateDecisionWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.VassalRelations:
                        VassalRelationWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.WarTargets:
                        WarDecisionTargetWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.Court:
                        CourtWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.CourtAppointment:
                        if (request.ActorId > 0)
                            CourtAppointmentWindow.Open(request.CountryId,
                                request.OfficeId, request.ActorId);
                        else
                            CourtAppointmentWindow.Open(request.CountryId,
                                request.OfficeId);
                        break;
                    case AW3WindowKind.CourtDisposition:
                        CourtDispositionWindow.Open(request.CountryId,
                            request.TargetActorId);
                        break;
                    case AW3WindowKind.CourtAuxiliaryLaws:
                        CourtAuxiliaryLawWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.InheritanceLaws:
                        InheritanceLawWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.School:
                        SchoolWindow.OpenSchool(string.IsNullOrEmpty(
                            request.SchoolId) ? null : request.SchoolId);
                        break;
                    case AW3WindowKind.SchoolRoster:
                        SchoolRosterWindow.Open(string.IsNullOrEmpty(
                            request.SchoolId) ? null : request.SchoolId);
                        break;
                    case AW3WindowKind.NameDecision:
                        NameDecisionWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.ConferredPosthumous:
                        ConferredPosthumousTitleWindow.Open(
                            request.CountryId, request.ActorId);
                        break;
                    case AW3WindowKind.CentralPower:
                        CentralPowerWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.Feudatories:
                        FeudatoryWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.DiplomacyConversations:
                        DiplomacyConversationWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.DiplomaticWarDeclaration:
                        DiplomaticWarDeclarationWindow.Open(
                            request.CountryId, request.TargetCountryId);
                        break;
                    case AW3WindowKind.DiplomaticMarriage:
                        DiplomaticMarriageWindow.Open(request.CountryId,
                            request.TargetCountryId);
                        break;
                    case AW3WindowKind.CivilServiceExam:
                        CivilServiceExamWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.RulerHousehold:
                        RulerHouseholdWindow.Open(request.CountryId);
                        break;
                    case AW3WindowKind.HouseholdOffer:
                        RulerHouseholdOfferWindow.Open(request.CountryId,
                            request.TargetCountryId);
                        break;
                    default:
                        return Result(AW3WindowOpenStatus.Unavailable,
                            request.Kind);
                }
                return Result(AW3WindowOpenStatus.Opened, request.Kind);
            }
            catch
            {
                return Result(AW3WindowOpenStatus.Unavailable,
                    request.Kind);
            }
        }

        private static void OpenHistory(AW3WindowOpenRequest request)
        {
            if (request.ActorId > 0)
                HistoryListWindow.OpenPerson(request.ActorId,
                    request.CountryId);
            else if (request.CountryId > 0)
                HistoryListWindow.OpenKingdom(request.CountryId);
            else
                HistoryListWindow.OpenCity(request.CityId);
        }

        private static bool ContextExists(AW3WindowOpenRequest request,
            AW3WindowContextRequirement requirements)
        {
            if (Has(requirements, AW3WindowContextRequirement.Country) &&
                FindKingdom(request.CountryId) == null) return false;
            if (Has(requirements,
                    AW3WindowContextRequirement.TargetCountry) &&
                FindKingdom(request.TargetCountryId) == null) return false;
            if (Has(requirements, AW3WindowContextRequirement.Actor) &&
                FindActor(request.ActorId) == null) return false;
            if (Has(requirements,
                    AW3WindowContextRequirement.TargetActor) &&
                FindActor(request.TargetActorId) == null) return false;
            if (Has(requirements, AW3WindowContextRequirement.City) &&
                FindCity(request.CityId) == null) return false;
            if (Has(requirements, AW3WindowContextRequirement.AnySubject))
            {
                if (request.CountryId > 0 &&
                    FindKingdom(request.CountryId) == null) return false;
                if (request.ActorId > 0 &&
                    FindActor(request.ActorId) == null) return false;
                if (request.CityId > 0 &&
                    FindCity(request.CityId) == null) return false;
            }
            return true;
        }

        private static Kingdom FindKingdom(long id)
        {
            if (id <= 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom kingdom = World.world.kingdoms.get(id);
                return kingdom?.data != null && !kingdom.isRekt()
                    ? kingdom
                    : null;
            }
            catch { return null; }
        }

        private static Actor FindActor(long id)
        {
            if (id <= 0 || World.world?.units == null) return null;
            try
            {
                Actor actor = World.world.units.get(id);
                return actor?.data != null && !actor.isRekt()
                    ? actor
                    : null;
            }
            catch { return null; }
        }

        private static City FindCity(long id)
        {
            if (id <= 0 || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(id);
                return city?.data != null && !city.isRekt()
                    ? city
                    : null;
            }
            catch { return null; }
        }

        private static bool Has(AW3WindowContextRequirement value,
            AW3WindowContextRequirement flag) => (value & flag) == flag;

        private static AW3WindowOpenResult Result(
            AW3WindowOpenStatus status, AW3WindowKind kind) =>
            new AW3WindowOpenResult(status, kind);
    }
}
