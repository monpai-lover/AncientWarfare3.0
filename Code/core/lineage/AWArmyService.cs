using System;
using System.Reflection;

namespace AncientWarfare3.core.lineage
{
    internal static class AWArmyService
    {
        private static readonly MethodInfo NewArmyObjectMethod = ResolveNewArmyObjectMethod();
        private static bool _creatingSpecialArmy;

        public static bool IsCreatingSpecialArmy => _creatingSpecialArmy;

        public static string GetRole(Army pArmy)
        {
            if (pArmy?.data == null) return "";
            pArmy.data.get(LineageKeys.AW_ARMY_ROLE, out string role, "");
            return role ?? "";
        }

        public static bool IsRoleArmy(Army pArmy, string pRole)
        {
            return pArmy?.data != null && GetRole(pArmy) == pRole;
        }

        public static bool IsSpecialArmy(Army pArmy)
        {
            return AWArmyRoleRules.IsSpecialRole(GetRole(pArmy));
        }

        public static long GetAnchorCityId(Army pArmy)
        {
            if (pArmy?.data == null) return -1L;
            pArmy.data.get(LineageKeys.AW_ARMY_CITY_ID, out long cityId, -1L);
            return cityId;
        }

        public static Army EnsureArmy(Kingdom pKingdom, City pAnchorCity, Actor pCaptain, string pRole,
            string pName, bool pDetached)
        {
            if (pKingdom?.data == null || pCaptain?.data == null || !AWArmyRoleRules.IsSpecialRole(pRole))
                return null;

            Army army = FindArmy(pKingdom, pAnchorCity, pRole);
            if (army == null)
                army = CreateArmy(pKingdom, pAnchorCity ?? pCaptain.city ?? pKingdom.capital, pCaptain, pDetached);
            if (army == null) return null;

            MarkArmy(army, pKingdom, pAnchorCity ?? pCaptain.city ?? pKingdom.capital, pRole, pName);
            if (!pCaptain.isRekt())
            {
                AddToArmy(pCaptain, army);
                army.setCaptain(pCaptain);
            }
            return army;
        }

        public static Army FindArmy(Kingdom pKingdom, City pAnchorCity, string pRole)
        {
            if (pKingdom?.data == null || World.world?.armies == null) return null;
            long cityId = pAnchorCity?.id ?? -1L;
            foreach (Army army in World.world.armies)
            {
                if (army?.data == null || !army.isAlive()) continue;
                if (!IsRoleArmy(army, pRole)) continue;
                try
                {
                    if (army.getKingdom() != pKingdom) continue;
                }
                catch { continue; }

                if (cityId >= 0 && GetAnchorCityId(army) != cityId) continue;
                return army;
            }
            return null;
        }

        public static void MarkArmy(Army pArmy, Kingdom pKingdom, City pAnchorCity, string pRole, string pName)
        {
            if (pArmy?.data == null || pKingdom?.data == null || !AWArmyRoleRules.IsSpecialRole(pRole)) return;
            pArmy.data.set(LineageKeys.AW_ARMY_ROLE, pRole);
            pArmy.data.set(LineageKeys.AW_ARMY_CITY_ID, pAnchorCity?.id ?? -1L);
            WorldTile tile = pAnchorCity?.getTile();
            pArmy.data.set(LineageKeys.AW_ARMY_ANCHOR_X, tile?.x ?? -1);
            pArmy.data.set(LineageKeys.AW_ARMY_ANCHOR_Y, tile?.y ?? -1);
            pArmy.data.custom_name = true;
            if (!string.IsNullOrEmpty(pName) && pArmy.data.name != pName)
                pArmy.setName(pName);
            if (AWArmyRoleRules.ShouldUseDetachedArmy(pRole) && pArmy.hasCity())
                pArmy.clearCity();
        }

        public static void AddToArmy(Actor pActor, Army pArmy)
        {
            if (pActor?.data == null || pArmy?.data == null) return;
            if (pActor.army == pArmy)
            {
                try
                {
                    if (!pArmy.units.Contains(pActor))
                        pArmy.listUnit(pActor);
                }
                catch { }
                return;
            }
            Army oldArmy = pActor.army;
            if (pActor.hasArmy())
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
                try { oldArmy?.units?.Remove(pActor); }
                catch { }
            }
            pActor.setArmy(pArmy);
            try
            {
                if (!pArmy.units.Contains(pActor))
                    pArmy.listUnit(pActor);
            }
            catch { }
        }

        public static void TryRemoveEmptyArmy(Army pArmy)
        {
            if (!IsSpecialArmy(pArmy)) return;
            if (pArmy.countUnits() > 0 || pArmy.hasCaptain()) return;
            try { World.world?.armies?.removeObject(pArmy); }
            catch { }
        }

        private static Army CreateArmy(Kingdom pKingdom, City pCity, Actor pCaptain, bool pDetached)
        {
            if (NewArmyObjectMethod == null || World.world?.armies == null) return null;
            if (pCity?.data == null || pCaptain?.data == null) return null;

            try
            {
                var army = NewArmyObjectMethod.Invoke(World.world.armies, null) as Army;
                if (army == null) return null;

                _creatingSpecialArmy = true;
                try { army.createArmy(pCaptain, pCity); }
                finally { _creatingSpecialArmy = false; }

                if (pDetached)
                    army.clearCity();
                return army;
            }
            catch (Exception e)
            {
                _creatingSpecialArmy = false;
                ModClass.LogWarning("Create AW3 special army failed: " + e.Message);
                return null;
            }
        }

        private static MethodInfo ResolveNewArmyObjectMethod()
        {
            Type type = typeof(ArmyManager);
            while (type != null)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    if (method.Name != "newObject") continue;
                    if (method.GetParameters().Length == 0) return method;
                }
                type = type.BaseType;
            }
            return null;
        }
    }
}
