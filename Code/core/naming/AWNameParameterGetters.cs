using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using NeoModLoader.General;

namespace AncientWarfare3.core.naming
{
    public static class AWNameParameterGetters
    {
        private static readonly Dictionary<string,
            Action<Actor, Dictionary<string, string>>> ActorGetters =
            NewRegistry<Action<Actor, Dictionary<string, string>>>(
                DefaultActor);
        private static readonly Dictionary<string,
            Action<Book, Dictionary<string, string>>> BookGetters =
            NewRegistry<Action<Book, Dictionary<string, string>>>(DefaultBook);
        private static readonly Dictionary<string,
            Action<City, Dictionary<string, string>>> CityGetters =
            NewRegistry<Action<City, Dictionary<string, string>>>(DefaultCity);
        private static readonly Dictionary<string,
            Action<Kingdom, Dictionary<string, string>>> KingdomGetters =
            NewRegistry<Action<Kingdom, Dictionary<string, string>>>(
                DefaultKingdom);
        private static readonly Dictionary<string,
            Action<Culture, Dictionary<string, string>>> CultureGetters =
            NewRegistry<Action<Culture, Dictionary<string, string>>>(
                DefaultCulture);
        private static readonly Dictionary<string,
            Action<Language, Dictionary<string, string>>> LanguageGetters =
            NewRegistry<Action<Language, Dictionary<string, string>>>(
                DefaultLanguage);
        private static readonly Dictionary<string,
            Action<Subspecies, Dictionary<string, string>>> SubspeciesGetters =
            NewRegistry<Action<Subspecies, Dictionary<string, string>>>(
                DefaultSubspecies);
        private static readonly Dictionary<string,
            Action<Religion, Dictionary<string, string>>> ReligionGetters =
            NewRegistry<Action<Religion, Dictionary<string, string>>>(
                DefaultReligion);
        private static readonly Dictionary<string,
            Action<Clan, Actor, Dictionary<string, string>>> ClanGetters =
            NewRegistry<Action<Clan, Actor, Dictionary<string, string>>>(
                DefaultClan);
        private static readonly Dictionary<string,
            Action<Alliance, Dictionary<string, string>>> AllianceGetters =
            NewRegistry<Action<Alliance, Dictionary<string, string>>>(
                DefaultAlliance);
        private static readonly Dictionary<string,
            Action<War, Dictionary<string, string>>> WarGetters =
            NewRegistry<Action<War, Dictionary<string, string>>>(DefaultWar);
        private static readonly Dictionary<string,
            Action<ItemData, ItemAsset, Actor, Dictionary<string, string>>>
            ItemGetters = NewRegistry<Action<ItemData, ItemAsset, Actor,
                Dictionary<string, string>>>(DefaultItem);
        private static readonly List<Action<Dictionary<string, string>>>
            GlobalGetters = new List<Action<Dictionary<string, string>>>
            {
                DefaultGlobal
            };
        private static readonly Dictionary<Type, Dictionary<string, Delegate>>
            CustomGetters = new Dictionary<Type, Dictionary<string, Delegate>>();

        public static Dictionary<string, string> CreateGlobalSnapshot()
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Action<Dictionary<string, string>> getter in
                     GlobalGetters.ToArray())
            {
                try
                {
                    getter(values);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("AW3 naming global parameter getter failed: " +
                                        error.Message);
                }
            }
            return values;
        }

        public static string ResolveNameTemplate(Actor pActor, MetaType pType)
        {
            if (pActor == null) return null;
            string template = null;
            if (pActor.hasCulture())
                template = pActor.culture.getNameTemplate(pType);
            else
            {
                foreach (Actor parent in pActor.getParents())
                {
                    if (parent == null || !parent.hasCulture()) continue;
                    template = parent.culture.getNameTemplate(pType);
                    break;
                }
            }
            return string.IsNullOrEmpty(template)
                ? pActor.asset?.getNameTemplate(pType)
                : template;
        }

        public static Action<Actor, Dictionary<string, string>>
            GetActorParameterGetter(string pName) => Get(ActorGetters, pName);
        public static Action<Book, Dictionary<string, string>>
            GetBookParameterGetter(string pName) => Get(BookGetters, pName);
        public static Action<City, Dictionary<string, string>>
            GetCityParameterGetter(string pName) => Get(CityGetters, pName);
        public static Action<Kingdom, Dictionary<string, string>>
            GetKingdomParameterGetter(string pName) => Get(KingdomGetters, pName);
        public static Action<Culture, Dictionary<string, string>>
            GetCultureParameterGetter(string pName) => Get(CultureGetters, pName);
        public static Action<Language, Dictionary<string, string>>
            GetLanguageParameterGetter(string pName) => Get(LanguageGetters, pName);
        public static Action<Subspecies, Dictionary<string, string>>
            GetSubspeciesParameterGetter(string pName) =>
            Get(SubspeciesGetters, pName);
        public static Action<Religion, Dictionary<string, string>>
            GetReligionParameterGetter(string pName) => Get(ReligionGetters, pName);
        public static Action<Clan, Actor, Dictionary<string, string>>
            GetClanParameterGetter(string pName) => Get(ClanGetters, pName);
        public static Action<Alliance, Dictionary<string, string>>
            GetAllianceParameterGetter(string pName) => Get(AllianceGetters, pName);
        public static Action<War, Dictionary<string, string>>
            GetWarParameterGetter(string pName) => Get(WarGetters, pName);
        public static Action<ItemData, ItemAsset, Actor,
            Dictionary<string, string>> GetItemParameterGetter(string pName) =>
            Get(ItemGetters, pName);

        public static void PutActorParameterGetter(string pName,
            Action<Actor, Dictionary<string, string>> pGetter) =>
            Put(ActorGetters, pName, pGetter);
        public static void PutBookParameterGetter(string pName,
            Action<Book, Dictionary<string, string>> pGetter) =>
            Put(BookGetters, pName, pGetter);
        public static void PutCityParameterGetter(string pName,
            Action<City, Dictionary<string, string>> pGetter) =>
            Put(CityGetters, pName, pGetter);
        public static void PutKingdomParameterGetter(string pName,
            Action<Kingdom, Dictionary<string, string>> pGetter) =>
            Put(KingdomGetters, pName, pGetter);
        public static void PutCultureParameterGetter(string pName,
            Action<Culture, Dictionary<string, string>> pGetter) =>
            Put(CultureGetters, pName, pGetter);
        public static void PutLanguageParameterGetter(string pName,
            Action<Language, Dictionary<string, string>> pGetter) =>
            Put(LanguageGetters, pName, pGetter);
        public static void PutSubspeciesParameterGetter(string pName,
            Action<Subspecies, Dictionary<string, string>> pGetter) =>
            Put(SubspeciesGetters, pName, pGetter);
        public static void PutReligionParameterGetter(string pName,
            Action<Religion, Dictionary<string, string>> pGetter) =>
            Put(ReligionGetters, pName, pGetter);
        public static void PutClanParameterGetter(string pName,
            Action<Clan, Actor, Dictionary<string, string>> pGetter) =>
            Put(ClanGetters, pName, pGetter);
        public static void PutAllianceParameterGetter(string pName,
            Action<Alliance, Dictionary<string, string>> pGetter) =>
            Put(AllianceGetters, pName, pGetter);
        public static void PutWarParameterGetter(string pName,
            Action<War, Dictionary<string, string>> pGetter) =>
            Put(WarGetters, pName, pGetter);
        public static void PutItemParameterGetter(string pName,
            Action<ItemData, ItemAsset, Actor, Dictionary<string, string>>
                pGetter) => Put(ItemGetters, pName, pGetter);

        public static T GetCustomParameterGetter<T>(string pName)
            where T : Delegate
        {
            if (!CustomGetters.TryGetValue(typeof(T), out var registry))
                return null;
            string key = string.IsNullOrEmpty(pName) ? "default" : pName;
            if (registry.TryGetValue(key, out Delegate found)) return (T)found;
            return registry.TryGetValue("default", out found) ? (T)found : null;
        }

        public static void PutCustomParameterGetter<T>(string pName, T pGetter)
            where T : Delegate
        {
            if (string.IsNullOrWhiteSpace(pName) || pGetter == null) return;
            if (!CustomGetters.TryGetValue(typeof(T), out var registry))
            {
                registry = new Dictionary<string, Delegate>(StringComparer.Ordinal);
                CustomGetters[typeof(T)] = registry;
            }
            registry[pName] = pGetter;
        }

        public static void PutGlobalParameterGetter(
            Action<Dictionary<string, string>> pGetter)
        {
            if (pGetter != null && !GlobalGetters.Contains(pGetter))
                GlobalGetters.Add(pGetter);
        }

        private static Dictionary<string, T> NewRegistry<T>(T pDefault)
            where T : Delegate
        {
            return new Dictionary<string, T>(StringComparer.Ordinal)
            {
                ["default"] = pDefault
            };
        }

        private static T Get<T>(Dictionary<string, T> pRegistry, string pName)
            where T : Delegate
        {
            string key = string.IsNullOrEmpty(pName) ? "default" : pName;
            return pRegistry.TryGetValue(key, out T getter)
                ? getter
                : pRegistry["default"];
        }

        private static void Put<T>(Dictionary<string, T> pRegistry,
            string pName, T pGetter) where T : Delegate
        {
            if (!string.IsNullOrWhiteSpace(pName) && pGetter != null)
                pRegistry[pName] = pGetter;
        }

        private static void DefaultActor(Actor pActor,
            Dictionary<string, string> pValues)
        {
            if (pActor?.asset == null) return;
            pValues["id"] = pActor.asset.id ?? string.Empty;
            if (string.IsNullOrEmpty(pActor.asset.name_locale)) return;
            pValues["locale"] = LocalizedTextManager.stringExists(
                pActor.asset.name_locale)
                ? LM.Get(pActor.asset.name_locale)
                : pActor.asset.name_locale;
        }

        private static void DefaultBook(Book pBook,
            Dictionary<string, string> pValues)
        {
        }

        private static void DefaultSubspecies(Subspecies pSubspecies,
            Dictionary<string, string> pValues)
        {
            if (pSubspecies == null) return;
            pValues["id"] = pSubspecies.species_id ?? string.Empty;
            ActorAsset asset = AssetManager.actor_library.get(
                pSubspecies.species_id);
            pValues["locale"] = asset?.getTranslatedName() ?? string.Empty;
        }

        private static void DefaultReligion(Religion pReligion,
            Dictionary<string, string> pValues)
        {
        }

        private static void DefaultCity(City pCity,
            Dictionary<string, string> pValues)
        {
            pValues["race"] = pCity?.data?.original_actor_asset ?? string.Empty;
        }

        private static void DefaultKingdom(Kingdom pKingdom,
            Dictionary<string, string> pValues)
        {
            pValues["race"] = pKingdom?.data?.original_actor_asset ?? string.Empty;
        }

        private static void DefaultCulture(Culture pCulture,
            Dictionary<string, string> pValues)
        {
            pValues["race"] = pCulture?.data?.original_actor_asset ?? string.Empty;
        }

        private static void DefaultLanguage(Language pLanguage,
            Dictionary<string, string> pValues)
        {
        }

        private static void DefaultClan(Clan pClan, Actor pActor,
            Dictionary<string, string> pValues)
        {
            if (pClan?.data == null) return;
            pValues["race"] = pClan.data.original_actor_asset ?? string.Empty;
            pValues["founder_home"] = string.IsNullOrEmpty(
                pClan.data.founder_city_name)
                ? pClan.data.founder_kingdom_name ?? string.Empty
                : pClan.data.founder_city_name;

            if (pActor != null)
            {
                pValues["founder_family_name"] =
                    ResolveChineseNameFamily(pActor);
                return;
            }

            foreach (Actor member in pClan.units)
            {
                if (member?.data == null) continue;
                string familyName = ResolveChineseNameFamily(member);
                if (string.IsNullOrEmpty(familyName)) continue;
                pValues["founder_family_name"] = familyName;
                break;
            }
        }

        private static string ResolveChineseNameFamily(Actor pActor)
        {
            if (pActor?.data == null) return string.Empty;
            pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME,
                out string chineseFamilyName, string.Empty);
            pActor.data.get(LineageKeys.FAMILY_NAME,
                out string lineageFamilyName, string.Empty);
            return ActorLocalizedNameBoundaryRules.ResolveTemplateFamily(
                chineseFamilyName, lineageFamilyName);
        }

        private static void DefaultAlliance(Alliance pAlliance,
            Dictionary<string, string> pValues)
        {
            if (pAlliance == null) return;
            List<Kingdom> kingdoms = pAlliance.kingdoms_hashset
                .Where(pKingdom => pKingdom?.data != null)
                .OrderBy(pKingdom => pKingdom.getID())
                .Take(2)
                .ToList();
            if (kingdoms.Count < 2) return;
            PopulateAllianceKingdom(pValues, kingdoms[0], "k1");
            PopulateAllianceKingdom(pValues, kingdoms[1], "k2");
        }

        private static void PopulateAllianceKingdom(
            Dictionary<string, string> pValues, Kingdom pKingdom,
            string pPrefix)
        {
            pValues[pPrefix + "_short"] = pKingdom.data.name ?? string.Empty;
            string capital = pKingdom.capital?.name;
            if (string.IsNullOrEmpty(capital))
                capital = pKingdom.cities.FirstOrDefault(pCity =>
                    pCity?.data != null && !pCity.isRekt())?.name;
            pValues[pPrefix + "_capital"] = capital ?? string.Empty;
        }

        private static void DefaultWar(War pWar,
            Dictionary<string, string> pValues)
        {
            if (pWar?.main_attacker?.data == null) return;
            string attacker = pWar.main_attacker.data.name ?? string.Empty;
            string defender = pWar.main_defender?.data?.name ?? string.Empty;
            pValues["attacker"] = attacker;
            pValues["defender"] = defender;
            pValues["attacker_leader"] =
                pWar.data?.started_by_actor_name ?? string.Empty;
            pValues["defender_leader"] =
                pWar.main_defender?.capital?.leader?.getName() ?? string.Empty;
            pValues["attacker_short"] = attacker.Length > 0
                ? attacker[0].ToString()
                : string.Empty;
            pValues["defender_short"] = defender.Length > 0
                ? defender[0].ToString()
                : string.Empty;
            pValues["defender_capital"] =
                pWar.main_defender?.capital?.data?.name ?? string.Empty;
        }

        private static void DefaultItem(ItemData pItemData,
            ItemAsset pItemAsset, Actor pActor,
            Dictionary<string, string> pValues)
        {
            if (pItemData == null || pItemAsset == null) return;
            pValues["material"] = pItemData.material ?? string.Empty;
            pValues["type"] = pItemData.asset_id ?? string.Empty;
            string locale = pItemAsset.getLocaleID();
            pValues["locale"] = LocalizedTextManager.stringExists(locale)
                ? LM.Get(locale)
                : locale ?? string.Empty;
            locale = pItemAsset.name_class;
            pValues["class"] = LocalizedTextManager.stringExists(locale)
                ? LM.Get(locale)
                : locale ?? string.Empty;
            if (pActor == null) return;
            pValues["city"] = pActor.city?.name ?? string.Empty;
            pValues["culture"] = pActor.culture?.data?.name ?? string.Empty;
            pValues["kingdom"] = pActor.kingdom?.data?.name ?? string.Empty;
            pValues["king"] = pActor.kingdom?.king?.getName() ?? string.Empty;

            Kingdom enemy = pActor.kingdom?.getEnemiesKingdoms()
                .Where(pKingdom => pKingdom?.king != null)
                .OrderBy(pKingdom => pKingdom.getID())
                .FirstOrDefault();
            pValues["enemy_kingdom"] = enemy?.data?.name ?? string.Empty;
            pValues["enemy_king"] = enemy?.king?.getName() ?? string.Empty;
        }

        private static void DefaultGlobal(Dictionary<string, string> pValues)
        {
            pValues["month"] = AssetManager.months
                .getMonth(Date.getCurrentMonth())?.english_name ?? string.Empty;
            int year = Date.getCurrentYear();
            pValues["year"] = year.ToString();
            pValues["era"] = World.world?.era_manager?.getCurrentAge()?.id ??
                             string.Empty;
            pValues["天干地支纪年"] =
                GanzhiChronologyRules.GetYearName(year);
        }
    }
}
