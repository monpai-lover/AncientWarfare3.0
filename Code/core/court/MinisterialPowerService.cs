using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class MinisterialPowerService
    {
        private sealed class PremierCandidate
        {
            public Actor Actor;
            public string OfficeId = "";
            public int Priority;
            public int Rank;
            public float Merit;
            public int AppointmentYear;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral()) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.MINISTERIAL_POWER_LAST_YEAR,
                out int lastYear, -1);
            if (lastYear == year) return;

            pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_ID,
                out long previousPremierId, -1L);
            pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_POWER,
                out int previousRealmPower, 0);
            if (RepublicGovernmentService.IsRepublic(pKingdom) ||
                !HasLivingKing(pKingdom))
            {
                DecayPreviousPremier(pKingdom, previousPremierId, -1L, year);
                ClearRealmProjection(pKingdom, year);
                if (previousPremierId >= 0 || previousRealmPower != 0)
                    CourtDirectionService.MarkDirty(pKingdom);
                return;
            }

            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(pKingdom, 96);
            PremierCandidate premier = SelectPremier(pKingdom, officers);
            long premierId = premier?.Actor?.data?.id ?? -1L;
            DecayPreviousPremier(pKingdom, previousPremierId, premierId, year);
            if (premier == null)
            {
                ClearRealmProjection(pKingdom, year);
                if (previousPremierId >= 0 || previousRealmPower != 0)
                    CourtDirectionService.MarkDirty(pKingdom);
                return;
            }

            Actor actor = premier.Actor;
            actor.data.get(LineageKeys.MINISTERIAL_POWER_KINGDOM_ID,
                out long powerKingdomId, -1L);
            actor.data.get(LineageKeys.MINISTERIAL_POWER_LAST_KINGDOM_ID,
                out long lastPowerKingdomId, -1L);
            actor.data.get(LineageKeys.MINISTERIAL_POWER_ACTOR_LAST_YEAR,
                out int actorPowerLastYear, year);
            actor.data.get(LineageKeys.MINISTERIAL_POWER, out int oldPower, 0);
            if (powerKingdomId >= 0 && powerKingdomId != pKingdom.id)
                oldPower = 0;
            else if (powerKingdomId < 0)
                oldPower = lastPowerKingdomId == pKingdom.id
                    ? MinisterialPowerRules.DecayFormerPremier(oldPower,
                        Math.Max(0, year - actorPowerLastYear))
                    : 0;
            int tenure = premier.AppointmentYear < 0
                ? 0
                : Math.Max(0, year - premier.AppointmentYear);
            bool childOrOldRuler = IsChildOrOldRuler(pKingdom.king);
            bool weakRuler = childOrOldRuler || IsWeakRuler(pKingdom.king);
            int delta = MinisterialPowerRules.AnnualDelta(premier.Rank,
                premier.Merit, tenure, weakRuler, childOrOldRuler,
                HasLowMandate(pKingdom), HasRoyalGuard(pKingdom));
            int nextPower = MinisterialPowerRules.NextPower(oldPower, delta);

            RecordCrossedThresholds(pKingdom, actor, oldPower, nextPower);

            actor.data.set(LineageKeys.MINISTERIAL_POWER, nextPower);
            actor.data.set(LineageKeys.MINISTERIAL_POWER_KINGDOM_ID, pKingdom.id);
            actor.data.set(LineageKeys.MINISTERIAL_POWER_LAST_KINGDOM_ID, pKingdom.id);
            actor.data.set(LineageKeys.MINISTERIAL_POWER_ACTOR_LAST_YEAR, year);
            pKingdom.data.set(LineageKeys.MINISTERIAL_PREMIER_ID, actor.data.id);
            pKingdom.data.set(LineageKeys.MINISTERIAL_PREMIER_POWER, nextPower);
            pKingdom.data.set(LineageKeys.MINISTERIAL_POWER_LAST_YEAR, year);
            if (previousPremierId != actor.data.id || previousRealmPower != nextPower)
                CourtDirectionService.MarkDirty(pKingdom);
            TryHandleCoupAttempt(pKingdom, actor, nextPower, weakRuler,
                HasLowMandate(pKingdom), year);
        }

        private static bool TryHandleCoupAttempt(Kingdom pKingdom, Actor pPremier,
            int pPower, bool pWeakRuler, bool pMandateCrisis, int pYear)
        {
            bool monarchy = !RepublicGovernmentService.IsRepublic(pKingdom);
            if (!MinisterialPowerRules.CanAttemptCoup(monarchy, IsAtWar(pKingdom),
                    pPower, pWeakRuler, pMandateCrisis)) return false;

            bool success = GeneralRebellionService.TryResolvePalaceCoup(
                pPremier, pKingdom, pPower);
            if (!success)
                CourtService.ClearOfficeForReignTransition(pPremier,
                    "failed_palace_coup");
            string eventId = success
                ? "ministerial_palace_coup_success"
                : "ministerial_palace_coup_failed";
            HistoryText text = HistoryText.Actor(pPremier) +
                               HistoryLocalizationRules.H(success
                                   ? "aw_hist_ministerial_coup_success"
                                   : "aw_hist_ministerial_coup_failed") +
                               HistoryLocalizationRules.H(
                                   "aw_hist_ministerial_power_label") +
                               HistoryText.PlainText(pPower.ToString());
            HistoryWriter.RecordPerson(pPremier.data.id, pKingdom,
                pPremier.getName(), eventId, text, ChronicleCategory.WAR,
                HistoryTarget.Kingdom(pKingdom));
            HistoryWriter.RecordKingdom(pKingdom, eventId, text,
                HistoryTarget.Actor(pPremier));

            pPremier.data.set(LineageKeys.MINISTERIAL_POWER, 0);
            pPremier.data.set(LineageKeys.MINISTERIAL_POWER_KINGDOM_ID, -1L);
            pPremier.data.set(LineageKeys.MINISTERIAL_POWER_LAST_KINGDOM_ID,
                pKingdom.id);
            pPremier.data.set(LineageKeys.MINISTERIAL_POWER_ACTOR_LAST_YEAR, pYear);
            ClearRealmProjection(pKingdom, pYear);
            CourtDirectionService.MarkDirty(pKingdom);
            return true;
        }

        private static void RecordCrossedThresholds(Kingdom pKingdom, Actor pPremier,
            int pPreviousPower, int pNextPower)
        {
            foreach (int threshold in MinisterialPowerRules.Thresholds)
            {
                if (!MinisterialPowerRules.CrossedThreshold(
                        pPreviousPower, pNextPower, threshold)) continue;
                string eventId = PersonEvent.MINISTERIAL_POWER + "_" + threshold;
                HistoryText text = HistoryText.Actor(pPremier) +
                                   HistoryLocalizationRules.H(
                                       "aw_hist_ministerial_power_reached") +
                                   HistoryText.PlainText(threshold.ToString()) +
                                   HistoryLocalizationRules.H(
                                       "aw_hist_ministerial_power_suffix");
                HistoryWriter.RecordPerson(pPremier.data.id, pKingdom,
                    pPremier.getName(), eventId, text, ChronicleCategory.HONOR,
                    HistoryTarget.Kingdom(pKingdom));
                HistoryWriter.RecordKingdom(pKingdom, eventId, text,
                    HistoryTarget.Actor(pPremier));
            }
        }

        private static PremierCandidate SelectPremier(Kingdom pKingdom,
            List<CourtOfficerView> pOfficers)
        {
            PremierCandidate best = null;
            foreach (CourtOfficerView officer in pOfficers ?? new List<CourtOfficerView>())
            {
                int priority = MinisterialPowerRules.OfficePriority(officer.office_id);
                if (priority == int.MaxValue) continue;
                Actor actor = World.world?.units?.get(officer.actor_id);
                if (!IsCurrentOfficer(actor, pKingdom, officer.office_id)) continue;
                var candidate = new PremierCandidate
                {
                    Actor = actor,
                    OfficeId = officer.office_id,
                    Priority = priority,
                    Rank = OfficialCareerStateService.ReadRankFast(actor),
                    Merit = OfficialCareerStateService.ReadMeritFast(actor),
                    AppointmentYear = officer.appointed_year
                };
                if (best == null || MinisterialPowerRules.CompareCandidates(
                        candidate.Priority, candidate.Rank, candidate.Merit,
                        NormalizeAppointmentYear(candidate.AppointmentYear), actor.data.id,
                        best.Priority, best.Rank, best.Merit,
                        NormalizeAppointmentYear(best.AppointmentYear), best.Actor.data.id) < 0)
                    best = candidate;
            }
            return best;
        }

        private static bool IsCurrentOfficer(Actor pActor, Kingdom pKingdom,
            string pOfficeId)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return false;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string officeId, "");
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            return courtKingdomId == pKingdom.id && officeId == pOfficeId &&
                   CourtAffiliationResolver.CanServe(pActor, pKingdom, layer);
        }

        private static void DecayPreviousPremier(Kingdom pKingdom,
            long pPreviousPremierId, long pCurrentPremierId, int pYear)
        {
            if (pPreviousPremierId < 0 || pPreviousPremierId == pCurrentPremierId) return;
            Actor previous = World.world?.units?.get(pPreviousPremierId);
            if (previous?.data == null) return;
            previous.data.get(LineageKeys.MINISTERIAL_POWER_KINGDOM_ID,
                out long powerKingdomId, -1L);
            if (powerKingdomId != pKingdom.id) return;
            previous.data.get(LineageKeys.MINISTERIAL_POWER, out int power, 0);
            previous.data.set(LineageKeys.MINISTERIAL_POWER,
                MinisterialPowerRules.DecayFormerPremier(power));
            previous.data.set(LineageKeys.MINISTERIAL_POWER_KINGDOM_ID, -1L);
            previous.data.set(LineageKeys.MINISTERIAL_POWER_LAST_KINGDOM_ID,
                pKingdom.id);
            previous.data.set(LineageKeys.MINISTERIAL_POWER_ACTOR_LAST_YEAR, pYear);
        }

        private static void ClearRealmProjection(Kingdom pKingdom, int pYear)
        {
            pKingdom.data.set(LineageKeys.MINISTERIAL_PREMIER_ID, -1L);
            pKingdom.data.set(LineageKeys.MINISTERIAL_PREMIER_POWER, 0);
            pKingdom.data.set(LineageKeys.MINISTERIAL_POWER_LAST_YEAR, pYear);
        }

        private static bool HasLivingKing(Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            return king?.data != null && king.isAlive() && !king.isRekt();
        }

        private static bool IsChildOrOldRuler(Actor pKing)
        {
            if (pKing?.data == null) return true;
            try { return !pKing.isAdult() || pKing.getAge() >= 75; }
            catch { return true; }
        }

        private static bool IsWeakRuler(Actor pKing)
        {
            return SafeStat(pKing, "stewardship") + SafeStat(pKing, "diplomacy") +
                   SafeStat(pKing, "warfare") < 30f;
        }

        private static bool HasLowMandate(Kingdom pKingdom)
        {
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null || mandate.id != pKingdom.id) return false;
            pKingdom.data.get(LineageKeys.MANDATE_VALUE, out int value, 80);
            pKingdom.data.get(LineageKeys.MANDATE_AUTHORITY, out int authority, 60);
            return value <= 40 || authority <= 40;
        }

        private static bool HasRoyalGuard(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.ROYAL_GUARD_ARMY_ID, out long armyId, -1L);
            return armyId >= 0;
        }

        private static bool IsAtWar(Kingdom pKingdom)
        {
            try
            {
                foreach (War _ in pKingdom.getWars()) return true;
            }
            catch { }
            return false;
        }

        private static int NormalizeAppointmentYear(int pYear)
        {
            return pYear < 0 ? int.MaxValue : pYear;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }
    }
}
