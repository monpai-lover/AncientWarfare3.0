using System;

namespace AncientWarfare3.core.lineage
{
    public static class EmptyCitySurvivalService
    {
        private const string RazeIntentKey = "aw_xenophobic_raze_pending";

        public static bool ShouldSuppressNaturalBorderShrink(City pCity)
        {
            try
            {
                Kingdom owner = pCity?.kingdom;
                return EmptyCitySurvivalRules.ShouldSuppressNaturalBorderShrink(
                    pCity?.data != null,
                    pCity?.isRekt() == true,
                    owner?.data != null && !owner.isRekt(),
                    pCity?.zones?.Count ?? 0,
                    HasLivingResidents(pCity),
                    HasRazeIntent(pCity));
            }
            catch
            {
                return false;
            }
        }

        public static bool ShouldSuppressAutomaticAbandonedZoneCleanup(
            City pCity)
        {
            try
            {
                return EmptyCitySurvivalRules.
                    ShouldSuppressAutomaticAbandonedZoneCleanup(
                        pCity?.data != null,
                        pCity?.isRekt() == true,
                        pCity?.zones?.Count ?? 0);
            }
            catch
            {
                return false;
            }
        }

        public static bool ShouldRecordXenophobicRazeIntent(City pCity)
        {
            try
            {
                bool gettingCaptured = pCity?.data != null &&
                                       pCity.isGettingCaptured();
                Kingdom capturer = gettingCaptured
                    ? pCity.getCapturingKingdom()
                    : null;
                Kingdom defender = pCity?.kingdom;
                bool capturerValid = capturer?.data != null &&
                                     !capturer.isRekt();
                Actor king = capturerValid ? capturer.king : null;
                bool xenophobic = king != null && king.hasXenophobic();
                bool differentSpecies = capturerValid &&
                                        defender?.data != null &&
                                        capturer.getSpecies() !=
                                        defender.getSpecies();
                bool defenderStillInside = HasResidentPhysicallyInside(pCity);
                return EmptyCitySurvivalRules.
                    ShouldRecordXenophobicRazeIntent(
                        gettingCaptured, capturerValid, xenophobic,
                        differentSpecies, defenderStillInside);
            }
            catch
            {
                return false;
            }
        }

        public static void RecordXenophobicRazeIntent(City pCity)
        {
            if (pCity?.data == null || HasLivingResidents(pCity)) return;
            pCity.data.set(RazeIntentKey, true);
        }

        public static void ClearRazeIntentForResident(City pCity,
            Actor pActor)
        {
            bool residentJoined = false;
            try
            {
                residentJoined = pCity?.data != null &&
                                 pActor?.data != null &&
                                 pActor.asset?.is_boat != true &&
                                 pActor.city == pCity &&
                                 pActor.isAlive() && !pActor.isRekt();
            }
            catch { }

            if (EmptyCitySurvivalRules.ShouldClearRazeIntent(
                    residentJoined, ownerChanged: false,
                    newOwnerNeutral: false, fromLoad: false))
                ClearRazeIntent(pCity);
        }

        public static void ClearRazeIntentForTakeover(City pCity,
            Kingdom pOldOwner, bool pFromLoad)
        {
            Kingdom newOwner = pCity?.kingdom;
            bool ownerChanged = pOldOwner != newOwner;
            bool newOwnerNeutral = true;
            try
            {
                newOwnerNeutral = newOwner?.data == null ||
                                  newOwner.isNeutral();
            }
            catch { }

            if (EmptyCitySurvivalRules.ShouldClearRazeIntent(
                    residentJoined: false, ownerChanged,
                    newOwnerNeutral, pFromLoad))
                ClearRazeIntent(pCity);
        }

        public static bool ShouldKeepFormalOwner(City pCity)
        {
            return EmptyCitySurvivalRules.ShouldKeepFormalOwner(
                WarScoreService.ShouldHoldFrozenOccupation(pCity));
        }

        internal static bool HasRazeIntent(City pCity)
        {
            if (pCity?.data == null) return false;
            pCity.data.get(RazeIntentKey, out bool result, false);
            return result;
        }

        private static void ClearRazeIntent(City pCity)
        {
            if (pCity?.data == null) return;
            pCity.data.removeBool(RazeIntentKey);
        }

        private static bool HasLivingResidents(City pCity)
        {
            if (pCity == null) return false;
            foreach (Actor actor in pCity.getUnits())
            {
                if (actor?.data == null || actor.asset?.is_boat == true ||
                    actor.city != pCity) continue;
                if (actor.isAlive() && !actor.isRekt()) return true;
            }
            return false;
        }

        private static bool HasResidentPhysicallyInside(City pCity)
        {
            if (pCity == null) return false;
            foreach (Actor actor in pCity.getUnits())
            {
                if (actor?.current_zone?.city == pCity) return true;
            }
            return false;
        }
    }
}
