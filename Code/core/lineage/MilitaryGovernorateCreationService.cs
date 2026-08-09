using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class MilitaryGovernorateSeatCandidate
    {
        public City City;
        public int Score;
    }

    internal sealed class MilitaryGovernorateGeneralCandidate
    {
        public Actor Actor;
        public int Score;
    }

    internal static class MilitaryGovernorateCreationService
    {
        public const string Reason = "military_governorate";

        public static List<MilitaryGovernorateSeatCandidate>
            GetEligibleSeats(Kingdom pSuzerain, int pLimit = 0)
        {
            var result = new List<MilitaryGovernorateSeatCandidate>();
            if (pSuzerain?.data == null || pSuzerain.isRekt()) return result;
            int limit = BoundedLimit(pLimit,
                MilitaryGovernorateRules.CityScanBudget);
            int scanned = 0;
            foreach (City city in pSuzerain.getCities())
            {
                if (scanned++ >= limit) break;
                if (!IsEligibleSeat(city, pSuzerain)) continue;
                result.Add(new MilitaryGovernorateSeatCandidate
                {
                    City = city,
                    Score = MilitaryGovernorateCreationRules.SeatScore(
                        true, Population(city), Zones(city))
                });
            }
            result.Sort((left, right) =>
                MilitaryGovernorateCreationRules.CompareCandidate(
                    left.Score, left.City?.id ?? long.MaxValue,
                    right.Score, right.City?.id ?? long.MaxValue));
            return result;
        }

        public static List<MilitaryGovernorateGeneralCandidate>
            GetGeneralCandidates(Kingdom pSuzerain, int pLimit = 0)
        {
            var result = new List<MilitaryGovernorateGeneralCandidate>();
            if (pSuzerain?.data == null || pSuzerain.isRekt()) return result;
            int limit = BoundedLimit(pLimit,
                MilitaryGovernorateRules.GeneralScanBudget);
            List<GeneralReadModelEntry> generals =
                GeneralService.GetActiveGeneralsForReadModel(pSuzerain,
                    pAllowUnitFallback: false, pLimit: limit);
            int year = SafeYear();
            for (int i = 0; i < generals.Count && i < limit; i++)
            {
                GeneralReadModelEntry entry = generals[i];
                Actor actor = entry?.Actor;
                if (!IsEligibleGeneral(actor, pSuzerain)) continue;
                int serviceYears = entry.AppointmentYear < 0 || year < 0
                    ? 0
                    : Math.Max(0, year - entry.AppointmentYear);
                result.Add(new MilitaryGovernorateGeneralCandidate
                {
                    Actor = actor,
                    Score = MilitaryGovernorateCreationRules.GeneralScore(
                        entry.Merit, entry.Loyalty, entry.Ambition,
                        serviceYears)
                });
            }
            result.Sort((left, right) =>
                MilitaryGovernorateCreationRules.CompareCandidate(
                    left.Score, left.Actor?.getID() ?? long.MaxValue,
                    right.Score, right.Actor?.getID() ?? long.MaxValue));
            return result;
        }

        public static bool TryCreate(City pSeat, Actor pGeneral,
            out Kingdom pSubject, out string pReason)
        {
            pSubject = null;
            pReason = "invalid_city";
            Kingdom suzerain = pSeat?.kingdom;
            if (!CanCreateFor(suzerain))
            {
                pReason = "realm_not_eligible";
                return false;
            }
            if (!IsEligibleSeat(pSeat, suzerain)) return false;
            if (!ContainsGeneralCandidate(suzerain, pGeneral))
            {
                pReason = "invalid_general";
                return false;
            }

            MilitaryGovernorateCreationStage stage =
                MilitaryGovernorateCreationStage.None;
            long stateId = -1;
            City originalCity = pGeneral.city;
            try
            {
                Kingdom subject = World.world.kingdoms.makeNewCivKingdom(
                    pGeneral, pID: null, pLog: true);
                if (subject?.data == null)
                {
                    pReason = "kingdom_creation_failed";
                    return false;
                }
                pSubject = subject;
                stage = MilitaryGovernorateCreationStage.KingdomCreated;

                pSeat.setKingdom(subject);
                if (pSeat.kingdom != subject)
                    return Fail("city_transfer_failed", stage, stateId,
                        subject, suzerain, pSeat, pGeneral, originalCity,
                        out pSubject, out pReason);
                stage = MilitaryGovernorateCreationStage.CityTransferred;

                pGeneral.joinCity(pSeat);
                subject.setCapital(pSeat);
                if (subject.capital != pSeat || pGeneral.city != pSeat)
                    return Fail("capital_assignment_failed", stage, stateId,
                        subject, suzerain, pSeat, pGeneral, originalCity,
                        out pSubject, out pReason);
                stage = MilitaryGovernorateCreationStage.CapitalAssigned;

                string commandName = MilitaryGovernorateRules.CommandName(
                    pSeat.data.name, "\u519b");
                subject.setName(commandName);
                if (!VassalService.SetMilitaryGovernorate(subject,
                        suzerain, Reason))
                    return Fail("relation_creation_failed", stage, stateId,
                        subject, suzerain, pSeat, pGeneral, originalCity,
                        out pSubject, out pReason);
                stage = MilitaryGovernorateCreationStage.RelationCreated;

                subject.data.get(LineageKeys.VASSAL_RELATION_ID,
                    out long relationId, -1L);
                if (!MilitaryGovernorateStore.TryCreate(relationId,
                        subject, suzerain, pSeat, pGeneral, commandName,
                        SafeYear(), out stateId))
                    return Fail("state_creation_failed", stage, stateId,
                        subject, suzerain, pSeat, pGeneral, originalCity,
                        out pSubject, out pReason);
                stage = MilitaryGovernorateCreationStage.StateCreated;

                stage = MilitaryGovernorateCreationStage.Completed;
                try
                {
                    GeneralService.RetireForMilitaryGovernorate(pGeneral);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "Military governorate general retirement failed: " +
                        error.Message);
                }
                try
                {
                    ChronicleEvents.OnMilitaryGovernorateCreated(suzerain,
                        subject, pSeat, pGeneral);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "Military governorate chronicle failed: " +
                        error.Message);
                }
                pReason = "";
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Military governorate creation failed: " +
                                    error.GetType().Name + ": " +
                                    error.Message);
                return Fail("creation_failed", stage, stateId, pSubject,
                    suzerain, pSeat, pGeneral, originalCity,
                    out pSubject, out pReason);
            }
        }

        private static bool CanCreateFor(Kingdom pSuzerain)
        {
            if (pSuzerain?.data == null || pSuzerain.isRekt() ||
                !pSuzerain.isCiv() || pSuzerain.isNeutral()) return false;
            bool xiaSystem = XiaizationService.GetLevel(pSuzerain) >=
                             XiaizationService.LevelXiaizedDynasty;
            return MilitaryGovernorateRules.CanCreate(xiaSystem,
                pSuzerain.countCities(), pSuzerain.getMaxCities());
        }

        private static bool IsEligibleSeat(City pSeat, Kingdom pSuzerain)
        {
            if (pSeat?.data == null || pSuzerain?.data == null) return false;
            pSeat.data.get(LineageKeys.CITY_FEUDATORY_ID,
                out long feudatoryId, -1L);
            bool external = CentralizationBorderDeploymentService.
                HasExternalLandBorderForRoot(pSeat, pSuzerain);
            return !pSeat.isRekt() && pSeat.isAlive() &&
                   MilitaryGovernorateRules.IsEligibleSeat(
                       pSeat.kingdom == pSuzerain,
                       pSuzerain.capital == pSeat,
                       feudatoryId >= 0, external);
        }

        private static bool ContainsGeneralCandidate(Kingdom pSuzerain,
            Actor pGeneral)
        {
            if (pGeneral?.data == null) return false;
            List<MilitaryGovernorateGeneralCandidate> candidates =
                GetGeneralCandidates(pSuzerain,
                    MilitaryGovernorateRules.GeneralScanBudget);
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].Actor == pGeneral) return true;
            return false;
        }

        private static bool IsEligibleGeneral(Actor pActor,
            Kingdom pSuzerain)
        {
            try
            {
                return pActor?.data != null && pActor.kingdom == pSuzerain &&
                       pActor.isAlive() && !pActor.isRekt() &&
                       pActor.isAdult() && !pActor.isKing() &&
                       GeneralService.IsGeneral(pActor);
            }
            catch { return false; }
        }

        private static bool Fail(string pFailureReason,
            MilitaryGovernorateCreationStage pStage, long pStateId,
            Kingdom pSubject, Kingdom pSuzerain, City pSeat,
            Actor pGeneral, City pOriginalCity, out Kingdom pResult,
            out string pReason)
        {
            Rollback(pStage, pStateId, pSubject, pSuzerain, pSeat,
                pGeneral, pOriginalCity);
            pResult = null;
            pReason = pFailureReason;
            return false;
        }

        private static void Rollback(MilitaryGovernorateCreationStage pStage,
            long pStateId, Kingdom pSubject, Kingdom pSuzerain, City pSeat,
            Actor pGeneral, City pOriginalCity)
        {
            MilitaryGovernorateRollbackAction actions =
                MilitaryGovernorateCreationRules.RollbackFor(pStage);
            if (actions.HasFlag(MilitaryGovernorateRollbackAction.EndState))
                try { MilitaryGovernorateStore.End(pStateId,
                    "creation_rollback"); } catch { }
            if (actions.HasFlag(MilitaryGovernorateRollbackAction.EndRelation))
                try { VassalService.RollbackCreatedRelation(pSubject); }
                catch { }
            if (actions.HasFlag(MilitaryGovernorateRollbackAction.RestoreCity))
            {
                try
                {
                    if (pSeat?.data != null && pSuzerain?.data != null)
                        pSeat.setKingdom(pSuzerain);
                }
                catch { }
                try
                {
                    if (pGeneral?.data != null &&
                        pOriginalCity?.data != null &&
                        !pOriginalCity.isRekt())
                        pGeneral.joinCity(pOriginalCity);
                    else if (pGeneral?.data != null && pSeat?.data != null)
                        pGeneral.joinCity(pSeat);
                }
                catch { }
            }
            if (actions.HasFlag(MilitaryGovernorateRollbackAction.RemoveKingdom))
                try
                {
                    if (pSubject?.data != null &&
                        World.world?.kingdoms?.get(pSubject.id) != null)
                        World.world.kingdoms.removeObject(pSubject);
                }
                catch { }
        }

        private static int BoundedLimit(int pRequested, int pMaximum)
        {
            return pRequested <= 0 ? pMaximum : Math.Min(pMaximum,
                pRequested);
        }

        private static int Population(City pCity)
        {
            try { return Math.Max(0, pCity.getPopulationPeople()); }
            catch { return 0; }
        }

        private static int Zones(City pCity)
        {
            try { return Math.Max(0, pCity.countZones()); }
            catch { return 0; }
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return -1; }
        }
    }
}
