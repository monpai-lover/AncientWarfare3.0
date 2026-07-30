using System;

namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolEducationRules
    {
        public const int MaxLoadRecoveryActorsPerFrame = 16;

        public static bool RequiresEducation(string layer, string officeId)
        {
            if (!string.Equals(layer, "central",
                    StringComparison.Ordinal)) return false;
            return !IsMilitaryCentralOffice(officeId);
        }

        public static bool IsEducated(bool canonicalMaster,
            bool activeMembership, bool registeredSchool,
            int membershipStartYear, int currentYear, bool pendingFailure)
        {
            if (canonicalMaster) return true;
            return activeMembership && registeredSchool && !pendingFailure &&
                   membershipStartYear >= 0 &&
                   currentYear > membershipStartYear;
        }

        public static bool CanAppoint(string layer, string officeId,
            bool educated)
        {
            return !RequiresEducation(layer, officeId) || educated;
        }

        public static bool CanSelectTeacher(bool sameCity,
            bool sameRealm, bool academyCommoner)
        {
            return sameCity || !academyCommoner && sameRealm;
        }

        public static bool RequiresJourney(bool sameCity,
            bool sameRealm, bool academyCommoner)
        {
            return !sameCity && CanSelectTeacher(sameCity, sameRealm,
                academyCommoner);
        }

        public static bool CanCommitAdmission(bool sameCity,
            bool arrivedAtDestination, bool teacherValid)
        {
            return teacherValid && (sameCity || arrivedAtDestination);
        }

        public static int LoadRecoveryBatchCount(int remainingActors)
        {
            return Math.Min(MaxLoadRecoveryActorsPerFrame,
                Math.Max(0, remainingActors));
        }

        public static bool ShouldRenewVoyageLease(bool actorInsideBoat,
            bool ownsTaxiJourney, bool journeyValid)
        {
            return actorInsideBoat && ownsTaxiJourney && journeyValid;
        }

        private static bool IsMilitaryCentralOffice(string officeId)
        {
            return string.Equals(officeId, "sima",
                       StringComparison.Ordinal) ||
                   string.Equals(officeId, "marshal",
                       StringComparison.Ordinal) ||
                   string.Equals(officeId, "bingbu",
                       StringComparison.Ordinal);
        }
    }
}
