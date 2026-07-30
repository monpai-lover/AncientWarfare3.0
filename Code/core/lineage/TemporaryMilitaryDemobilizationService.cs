namespace AncientWarfare3.core.lineage
{
    internal static class TemporaryMilitaryDemobilizationService
    {
        public static void RestoreCivilian(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                !pActor.isAlive()) return;
            if (!TemporaryMilitaryReturnService.
                    TryBeginOrComplete(pActor)) return;

            Army army = pActor.army;
            bool currentCaptain = false;
            try
            {
                currentCaptain = army?.data != null &&
                                 ReferenceEquals(army.getCaptain(), pActor);
            }
            catch { }
            if (ArmyCaptainContinuityRules.ShouldRetainCareerCaptain(
                    actorAlive: true, currentCaptain,
                    ArmyCaptainDisposalScope.IsActive(army)))
            {
                StandingArmyPeacetimeService.RefreshJob(pActor);
                return;
            }

            if (pActor.isWarrior()) pActor.stopBeingWarrior();
            if (!pActor.isProfession(UnitProfession.Unit))
                pActor.setProfession(UnitProfession.Unit);
            if (pActor.army != null)
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
            }

            if (pActor.ai == null) return;
            pActor.ai.clearJob();
            try
            {
                string job = pActor.getNextJob();
                if (!string.IsNullOrEmpty(job)) pActor.ai.setJob(job);
            }
            catch { }
        }
    }
}
