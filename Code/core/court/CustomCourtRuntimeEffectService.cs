using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal sealed class CustomCourtCityEffectModifiers
    {
        internal CustomCourtEffectModifier Tax =
            CustomCourtEffectModifier.Identity;
        internal CustomCourtEffectModifier Food =
            CustomCourtEffectModifier.Identity;
        internal CustomCourtEffectModifier Order =
            CustomCourtEffectModifier.Identity;
    }

    internal static class CustomCourtRuntimeEffectService
    {
        private static readonly CustomCourtEffectService EffectService =
            new CustomCourtEffectService();

        public static CustomCourtEffectModifier GetCityModifier(
            Kingdom kingdom, CustomCourtEffectId effectId)
        {
            return GetAggregateModifier(kingdom, effectId,
                CustomCourtEffectScope.Kingdom, CustomCourtEffectScope.City);
        }

        public static CustomCourtEffectModifier GetArmyModifier(
            Kingdom kingdom)
        {
            return BuildBoundedArmyModifier(kingdom);
        }

        private static CustomCourtEffectModifier BuildBoundedArmyModifier(
            Kingdom kingdom)
        {
            if (!TryGetRuntime(kingdom, out CustomCourtTemplate snapshot,
                    out List<CourtOfficerView> officers))
                return CustomCourtEffectModifier.Identity;

            var active = new List<CustomCourtOffice>();
            AddActiveFilteredOffices(active, kingdom, snapshot.Offices,
                officers, null, CustomCourtEffectId.ArmyMorale,
                CustomCourtEffectScope.Kingdom, CustomCourtEffectScope.Army);
            var activeLocalTemplateIds = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (CustomLocalCourtTemplate local in snapshot.LocalTemplates ??
                     new List<CustomLocalCourtTemplate>())
            {
                if (local == null || string.IsNullOrWhiteSpace(local.Id) ||
                    !activeLocalTemplateIds.Add(local.Id)) continue;
                AddActiveFilteredOffices(active, kingdom, local.Offices,
                    officers, row => IsActiveLocalTemplateRow(kingdom, row,
                        local.Id),
                    CustomCourtEffectId.ArmyMorale,
                    CustomCourtEffectScope.Kingdom,
                    CustomCourtEffectScope.Army);
            }
            IDictionary<CustomCourtEffectId, CustomCourtEffectModifier>
                modifiers = EffectService.AggregateModifiers(active,
                    _ => true);
            return modifiers.TryGetValue(CustomCourtEffectId.ArmyMorale,
                out CustomCourtEffectModifier modifier)
                ? modifier
                : CustomCourtEffectModifier.Identity;
        }

        private static bool IsActiveLocalTemplateRow(Kingdom kingdom,
            CourtOfficerView row, string templateId)
        {
            if (row == null || row.layer != CourtOfficeLayer.City ||
                row.city_id < 0L || string.IsNullOrWhiteSpace(templateId))
                return false;
            City city;
            try { city = World.world?.cities?.get(row.city_id); }
            catch { return false; }
            return city?.data != null && city.kingdom == kingdom &&
                   CustomCourtRuntime.TryGetLocalTemplate(kingdom, city,
                       out CustomLocalCourtTemplate resolved) &&
                   string.Equals(resolved?.Id, templateId,
                       StringComparison.Ordinal);
        }

        public static Dictionary<long, CustomCourtCityEffectModifiers>
            BuildCityModifiers(Kingdom kingdom, IEnumerable<City> cities)
        {
            var result = new Dictionary<long,
                CustomCourtCityEffectModifiers>();
            if (cities == null || !TryGetRuntime(kingdom,
                    out CustomCourtTemplate snapshot,
                    out List<CourtOfficerView> officers)) return result;

            List<CustomCourtOffice> central = CollectActiveOffices(kingdom,
                snapshot.Offices, officers, null);
            foreach (City pCity in cities)
            {
                if (pCity?.data == null || pCity.kingdom != kingdom) continue;
                var active = new List<CustomCourtOffice>(central);
                CustomLocalCourtTemplate localTemplate = null;
                if (snapshot.LocalTemplates != null &&
                    CustomCourtRuntime.TryGetLocalTemplate(kingdom, pCity,
                        out localTemplate) && localTemplate?.Offices != null)
                {
                    List<CustomCourtOffice> local = CollectActiveOffices(
                        kingdom, localTemplate.Offices, officers, row =>
                        {
                            if (row.layer != CourtOfficeLayer.City ||
                                row.city_id != pCity.id) return false;
                            return true;
                        });
                    active.AddRange(local);
                }

                IDictionary<CustomCourtEffectId, CustomCourtEffectModifier>
                    modifiers = EffectService.AggregateModifiers(active,
                        _ => true);
                var cityModifiers = new CustomCourtCityEffectModifiers();
                if (modifiers.TryGetValue(CustomCourtEffectId.TaxIncome,
                        out CustomCourtEffectModifier tax))
                    cityModifiers.Tax = tax;
                if (modifiers.TryGetValue(CustomCourtEffectId.FoodProduction,
                        out CustomCourtEffectModifier food))
                    cityModifiers.Food = food;
                if (modifiers.TryGetValue(CustomCourtEffectId.CivilOrder,
                        out CustomCourtEffectModifier order))
                    cityModifiers.Order = order;
                result[pCity.id] = cityModifiers;
            }
            return result;
        }

        public static CustomCourtEffectModifier GetOfficeInfluenceModifier(
            Kingdom kingdom, string officeId, long actorId)
        {
            if (!TryGetRuntime(kingdom, out CustomCourtTemplate snapshot,
                    out List<CourtOfficerView> officers))
                return CustomCourtEffectModifier.Identity;
            CustomCourtOffice office = FindOfficeForIncumbent(snapshot,
                kingdom, officeId, actorId, officers,
                out CourtOfficerView incumbent);
            if (office == null || !HasActiveIncumbent(kingdom, office,
                    officers, actorId, row => row.layer == incumbent.layer &&
                    row.city_id == incumbent.city_id))
                return CustomCourtEffectModifier.Identity;
            return ComposeOfficeEffects(office,
                CustomCourtEffectId.CourtInfluence,
                CustomCourtEffectScope.Kingdom, CustomCourtEffectScope.Court);
        }

        private static CustomCourtEffectModifier GetAggregateModifier(
            Kingdom kingdom, CustomCourtEffectId effectId,
            params CustomCourtEffectScope[] scopes)
        {
            if (!TryGetRuntime(kingdom, out CustomCourtTemplate snapshot,
                    out List<CourtOfficerView> officers))
                return CustomCourtEffectModifier.Identity;

            var activeOffices = new List<CustomCourtOffice>();
            foreach (CustomCourtOffice office in snapshot.Offices ??
                     new List<CustomCourtOffice>())
            {
                if (office == null || !HasActiveIncumbent(kingdom, office,
                        officers, -1L)) continue;
                var filtered = new List<CustomCourtOfficeEffect>();
                foreach (CustomCourtOfficeEffect effect in office.Effects ??
                         new List<CustomCourtOfficeEffect>())
                    if (effect != null && effect.Id == effectId &&
                        Contains(scopes, effect.Scope)) filtered.Add(effect);
                if (filtered.Count == 0) continue;
                activeOffices.Add(new CustomCourtOffice
                {
                    Effects = filtered
                });
            }

            IDictionary<CustomCourtEffectId, CustomCourtEffectModifier>
                modifiers = EffectService.AggregateModifiers(activeOffices,
                    _ => true);
            return modifiers.TryGetValue(effectId,
                out CustomCourtEffectModifier modifier)
                ? modifier
                : CustomCourtEffectModifier.Identity;
        }

        private static List<CustomCourtOffice> CollectActiveOffices(
            Kingdom kingdom, IEnumerable<CustomCourtOffice> offices,
            List<CourtOfficerView> officers,
            Func<CourtOfficerView, bool> rowFilter)
        {
            var result = new List<CustomCourtOffice>();
            foreach (CustomCourtOffice office in offices ??
                     Array.Empty<CustomCourtOffice>())
            {
                if (office == null || !HasActiveIncumbent(kingdom, office,
                        officers, -1L, rowFilter)) continue;
                result.Add(office);
            }
            return result;
        }

        private static void AddActiveFilteredOffices(
            List<CustomCourtOffice> target, Kingdom kingdom,
            IEnumerable<CustomCourtOffice> offices,
            List<CourtOfficerView> officers,
            Func<CourtOfficerView, bool> rowFilter,
            CustomCourtEffectId effectId,
            params CustomCourtEffectScope[] scopes)
        {
            foreach (CustomCourtOffice office in offices ??
                     Array.Empty<CustomCourtOffice>())
            {
                if (office == null || !HasActiveIncumbent(kingdom, office,
                        officers, -1L, rowFilter)) continue;
                var filtered = new List<CustomCourtOfficeEffect>();
                foreach (CustomCourtOfficeEffect effect in office.Effects ??
                         new List<CustomCourtOfficeEffect>())
                    if (effect != null && effect.Id == effectId &&
                        Contains(scopes, effect.Scope)) filtered.Add(effect);
                if (filtered.Count > 0)
                    target.Add(new CustomCourtOffice { Effects = filtered });
            }
        }

        private static CustomCourtEffectModifier ComposeOfficeEffects(
            CustomCourtOffice office, CustomCourtEffectId effectId,
            params CustomCourtEffectScope[] scopes)
        {
            var effects = new List<CustomCourtOfficeEffect>();
            foreach (CustomCourtOfficeEffect effect in office.Effects ??
                     new List<CustomCourtOfficeEffect>())
                if (effect != null && effect.Id == effectId &&
                    Contains(scopes, effect.Scope)) effects.Add(effect);
            return CustomCourtEffectRules.Compose(effects);
        }

        private static bool TryGetRuntime(Kingdom kingdom,
            out CustomCourtTemplate snapshot,
            out List<CourtOfficerView> officers)
        {
            snapshot = null;
            officers = new List<CourtOfficerView>();
            if (kingdom?.data == null ||
                !CustomCourtRuntime.TryGetSnapshot(kingdom, out snapshot) ||
                snapshot?.Offices == null) return false;
            officers = CourtService.GetActiveOfficers(kingdom, int.MaxValue);
            return officers.Count > 0;
        }

        private static CustomCourtOffice FindOffice(
            CustomCourtTemplate snapshot, string officeId)
        {
            if (snapshot?.Offices == null || string.IsNullOrWhiteSpace(officeId))
                return null;
            foreach (CustomCourtOffice office in snapshot.Offices)
                if (office != null && string.Equals(office.Id, officeId,
                        StringComparison.Ordinal)) return office;
            return null;
        }

        private static CustomCourtOffice FindOfficeForIncumbent(
            CustomCourtTemplate snapshot, Kingdom kingdom, string officeId,
            long actorId, List<CourtOfficerView> officers,
            out CourtOfficerView incumbent)
        {
            incumbent = null;
            foreach (CourtOfficerView row in officers ??
                     new List<CourtOfficerView>())
            {
                if (row == null || row.actor_id != actorId ||
                    !string.Equals(row.office_id, officeId,
                        StringComparison.Ordinal)) continue;
                incumbent = row;
                if (row.layer != CourtOfficeLayer.City)
                    return FindOffice(snapshot, officeId);
                City city;
                try { city = World.world?.cities?.get(row.city_id); }
                catch { return null; }
                if (city?.data == null || city.kingdom != kingdom ||
                    !CustomCourtRuntime.TryGetLocalTemplate(kingdom, city,
                        out CustomLocalCourtTemplate local)) return null;
                return local?.Offices?.FirstOrDefault(office =>
                    office != null && string.Equals(office.Id, officeId,
                        StringComparison.Ordinal));
            }
            return null;
        }

        private static bool HasActiveIncumbent(Kingdom kingdom,
            CustomCourtOffice office, List<CourtOfficerView> officers,
            long requiredActorId,
            Func<CourtOfficerView, bool> rowFilter = null)
        {
            for (int i = 0; i < officers.Count; i++)
            {
                CourtOfficerView row = officers[i];
                if (row == null || (requiredActorId >= 0L &&
                    row.actor_id != requiredActorId) || rowFilter != null &&
                    !rowFilter(row) || row.layer != office.Layer) continue;
                Actor actor = null;
                try { actor = World.world?.units?.get(row.actor_id); }
                catch { }
                bool alive = false;
                long actorKingdomId = -1L;
                long runtimeKingdomId = -1L;
                string runtimeOfficeId = string.Empty;
                if (actor?.data != null)
                {
                    try { alive = actor.isAlive() && !actor.isRekt(); }
                    catch { }
                    try { actorKingdomId = actor.kingdom?.id ?? -1L; }
                    catch { }
                    actor.data.get(LineageKeys.COURT_KINGDOM_ID,
                        out runtimeKingdomId, -1L);
                    actor.data.get(LineageKeys.COURT_OFFICE_ID,
                        out runtimeOfficeId, string.Empty);
                }
                if (CustomCourtRuntimeEffectRules.IsActiveIncumbent(
                        kingdom.id, office.Id, row.actor_id, row.office_id,
                        actor?.data != null, alive, actorKingdomId,
                        runtimeKingdomId, runtimeOfficeId)) return true;
            }
            return false;
        }

        private static bool Contains(CustomCourtEffectScope[] scopes,
            CustomCourtEffectScope scope)
        {
            if (scopes == null) return false;
            for (int i = 0; i < scopes.Length; i++)
                if (scopes[i] == scope) return true;
            return false;
        }
    }
}
