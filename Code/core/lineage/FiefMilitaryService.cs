namespace AncientWarfare3.core.lineage
{
    internal static class FiefMilitaryService
    {
        public static void EnsureFiefCommand(City pCity, Actor pGeneral = null)
        {
            if (pCity?.data == null) return;

            FiefInfo info = FiefService.GetActiveFief(pCity);
            Actor general = pGeneral ?? FindActor(info?.general_actor_id ?? -1L);
            Kingdom kingdom = pCity.kingdom;
            bool activeFief = info != null;
            bool generalAlive = general?.data != null && !general.isRekt() && general.isAlive();
            bool sameKingdom = generalAlive && kingdom?.data != null && general.kingdom == kingdom;
            bool slave = generalAlive && SlaveService.IsSlave(general);
            bool king = generalAlive && general.isKing();
            if (!FiefMilitaryRules.ShouldEnforceFiefCommand(activeFief, generalAlive, sameKingdom, slave, king))
                return;

            if (general.city != pCity)
                general.joinCity(pCity);

            if (pCity.leader != general)
                pCity.setLeader(general, pNew: true);

            EnsureArmyCaptain(pCity, general);
        }

        public static void RefreshArmyName(Army pArmy)
        {
            if (pArmy?.data == null) return;

            City city = SafeCity(pArmy);
            if (city?.data == null) return;

            bool activeFief = FiefService.IsActiveFief(city);
            bool slaveArmy = SlaveService.IsSlaveArmy(pArmy);
            bool royalGuardArmy = RoyalGuardService.IsRoyalGuard(pArmy.getCaptain());
            if (!FiefMilitaryRules.ShouldRenameFiefArmy(activeFief, slaveArmy, royalGuardArmy)) return;

            string name = FiefMilitaryRules.BuildFiefArmyName(city.data.name, city.id);
            if (pArmy.data.custom_name && pArmy.data.name == name) return;

            pArmy.data.custom_name = true;
            pArmy.setName(name);
        }

        private static City SafeCity(Army pArmy)
        {
            try
            {
                return pArmy.hasCity() ? pArmy.getCity() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureArmyCaptain(City pCity, Actor pGeneral)
        {
            if (pCity?.data == null || pGeneral?.data == null) return;

            try
            {
                if (!pGeneral.isWarrior() && pCity.checkCanMakeWarrior(pGeneral))
                    pCity.makeWarrior(pGeneral);
            }
            catch { }

            try
            {
                if (!pCity.hasArmy()) return;
                Army army = pCity.getArmy();
                if (army?.data == null) return;
                if (army.getCaptain() != pGeneral)
                    army.setCaptain(pGeneral);
                RefreshArmyName(army);
            }
            catch { }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}
