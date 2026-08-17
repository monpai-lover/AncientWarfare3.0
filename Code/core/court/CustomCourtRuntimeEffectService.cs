using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
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
            return GetAggregateModifier(kingdom,
                CustomCourtEffectId.ArmyMorale,
                CustomCourtEffectScope.Kingdom, CustomCourtEffectScope.Army);
        }

        public static CustomCourtEffectModifier GetOfficeInfluenceModifier(
            Kingdom kingdom, string officeId, long actorId)
        {
            if (!TryGetRuntime(kingdom, out CustomCourtTemplate snapshot,
                    out List<CourtOfficerView> officers))
                return CustomCourtEffectModifier.Identity;
            CustomCourtOffice office = FindOffice(snapshot, officeId);
            if (office == null || !HasActiveIncumbent(kingdom, office,
                    officers, actorId))
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

        private static bool HasActiveIncumbent(Kingdom kingdom,
            CustomCourtOffice office, List<CourtOfficerView> officers,
            long requiredActorId)
        {
            for (int i = 0; i < officers.Count; i++)
            {
                CourtOfficerView row = officers[i];
                if (row == null || (requiredActorId >= 0L &&
                    row.actor_id != requiredActorId)) continue;
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
