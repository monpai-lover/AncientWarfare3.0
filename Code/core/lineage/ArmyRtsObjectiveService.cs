using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsObjectiveService
    {
        internal static ArmyRtsObjectiveState Classify(War pWar,
            Kingdom pKingdom, City pCity)
        {
            bool cityLive = IsLiveCity(pCity);
            bool warActive = IsActiveWar(pWar);
            bool ownerAtWar = cityLive && warActive &&
                              IsWarParticipant(pWar, pCity.kingdom);
            bool controlledByParticipantSide = ownerAtWar &&
                IsControlledByParticipantSide(pWar, pKingdom, pCity);
            bool hostileMilitaryInside = controlledByParticipantSide &&
                CityAttackZoneService.HasHostileMilitaryInside(pWar,
                    pCity, pKingdom);
            bool hostileCaptureProgress = controlledByParticipantSide &&
                HasHostileCaptureProgress(pWar, pKingdom, pCity);
            bool externallyControlled = cityLive && warActive &&
                IsExternallyControlled(pWar, pCity);
            bool occupationLockedAgainstKingdom = cityLive && warActive &&
                WarScoreService.IsCityFrozenOccupationLockedAgainst(
                    pCity, pKingdom);

            return ArmyRtsObjectiveRules.Classify(
                new ArmyRtsObjectiveFacts(cityLive, warActive,
                    ownerAtWar, controlledByParticipantSide,
                    hostileMilitaryInside, hostileCaptureProgress,
                    externallyControlled: externallyControlled,
                    occupationLockedAgainstKingdom:
                        occupationLockedAgainstKingdom));
        }

        internal static bool HasOpenObjective(War pWar,
            Kingdom pKingdom, IReadOnlyList<City> pIndexedCandidates)
        {
            return CountOpenObjectives(pWar, pKingdom,
                pIndexedCandidates) > 0;
        }

        internal static int CountOpenObjectives(War pWar,
            Kingdom pKingdom, IReadOnlyList<City> pIndexedCandidates)
        {
            if (!IsActiveWar(pWar) || pKingdom?.data == null ||
                pIndexedCandidates == null) return 0;
            int count = 0;
            for (int i = 0; i < pIndexedCandidates.Count; i++)
                if (IsOpen(Classify(pWar, pKingdom,
                        pIndexedCandidates[i])))
                    count++;
            return count;
        }

        internal static bool IsOpen(ArmyRtsObjectiveState pState)
        {
            return pState == ArmyRtsObjectiveState.OpenAttack ||
                   pState == ArmyRtsObjectiveState.OpenDefense;
        }

        internal static bool HasHostileCaptureProgress(War pWar,
            Kingdom pKingdom, City pCity)
        {
            if (!IsActiveWar(pWar) || pKingdom?.data == null ||
                pCity?.data == null) return false;
            Kingdom capturer;
            bool progressing;
            try
            {
                capturer = pCity.being_captured_by;
                progressing = pCity.isGettingCaptured();
            }
            catch { return false; }
            if (!progressing || capturer?.data == null) return false;
            try
            {
                return !pWar.onTheSameSide(pKingdom, capturer) &&
                       pWar.isInWarWith(pKingdom, capturer);
            }
            catch
            {
                try { return pKingdom.isInWarWith(capturer); }
                catch { return false; }
            }
        }

        private static bool IsControlledByParticipantSide(War pWar,
            Kingdom pKingdom, City pCity)
        {
            if (pWar?.data == null || pKingdom?.data == null ||
                pCity?.data == null) return false;
            if (CityAttackZoneService.IsControlledByEnemySide(pWar,
                    pCity, pKingdom)) return false;
            if (CityAttackZoneService.IsControlledBySide(pWar, pCity,
                    pKingdom)) return true;
            try
            {
                return pCity.kingdom == pKingdom ||
                       pWar.onTheSameSide(pKingdom, pCity.kingdom);
            }
            catch { return pCity.kingdom == pKingdom; }
        }

        internal static bool IsExternallyControlled(War pWar, City pCity)
        {
            if (pWar?.data == null || pCity?.data == null) return false;
            Kingdom controller;
            try { controller = pCity.being_captured_by; }
            catch { return false; }
            if (controller?.data == null || controller == pCity.kingdom)
                return false;
            return !IsWarParticipant(pWar, controller);
        }

        private static bool IsWarParticipant(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null) return false;
            try { return pWar.isAttacker(pKingdom) || pWar.isDefender(pKingdom); }
            catch { return false; }
        }

        private static bool IsActiveWar(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
        }

        private static bool IsLiveCity(City pCity)
        {
            try
            {
                return pCity?.data != null && !pCity.isRekt() &&
                       pCity.isAlive() && pCity.kingdom?.data != null;
            }
            catch { return false; }
        }
    }
}
