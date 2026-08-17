using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtTemplateRules
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaximumOffices = 128;
        public const int MaximumEdges = 512;

        private static readonly Regex StableId = new Regex(
            "^[a-z][a-z0-9_]{0,63}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsValidTemplateId(string id)
        {
            return !string.IsNullOrEmpty(id) && StableId.IsMatch(id);
        }

        public static bool IsValidOfficeId(string id)
        {
            return IsValidTemplateId(id);
        }

        public static CustomCourtTemplateValidationError Validate(
            CustomCourtTemplate template)
        {
            if (template == null)
                return CustomCourtTemplateValidationError.MissingOffice;
            if (template.SchemaVersion != CurrentSchemaVersion)
                return CustomCourtTemplateValidationError.UnsupportedSchemaVersion;
            if (!IsValidTemplateId(template.Id))
                return CustomCourtTemplateValidationError.InvalidTemplateId;
            if (template.Revision < 1 || template.Offices == null ||
                template.Offices.Count == 0 || template.Offices.Count > MaximumOffices)
                return CustomCourtTemplateValidationError.MissingOffice;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CustomCourtOffice office in template.Offices)
            {
                CustomCourtTemplateValidationError error =
                    ValidateOffice(office, ids);
                if (error != CustomCourtTemplateValidationError.None)
                    return error;
            }

            return ValidateGraph(template.Offices.Select(item => item.Id),
                template.Edges);
        }

        public static CustomCourtTemplateValidationError ValidateOffice(
            CustomCourtOffice office, ISet<string> existingIds)
        {
            if (office == null)
                return CustomCourtTemplateValidationError.MissingOffice;
            if (!IsValidOfficeId(office.Id))
                return CustomCourtTemplateValidationError.InvalidOfficeId;
            if (existingIds != null && !existingIds.Add(office.Id))
                return CustomCourtTemplateValidationError.DuplicateOffice;
            if (office.Grade < 1 || office.Grade > 100)
                return CustomCourtTemplateValidationError.InvalidOfficeGrade;
            if (office.Slots < 1 || office.Slots > 32)
                return CustomCourtTemplateValidationError.InvalidOfficeSlots;
            if (!IsValidLayer(office.Layer))
                return CustomCourtTemplateValidationError.InvalidOfficeLayer;
            if (!IsValidLayout(office.Layout))
                return CustomCourtTemplateValidationError.InvalidLayout;
            if (!IsValidRequirement(office.Requirements))
                return CustomCourtTemplateValidationError.InvalidRequirement;
            if (office.Effects == null || office.Effects.Count > 16)
                return CustomCourtTemplateValidationError.InvalidEffect;
            foreach (CustomCourtOfficeEffect effect in office.Effects)
            {
                if (!IsEffectValueValid(effect.Id, effect.Mode, effect.Value) ||
                    !IsValidScope(effect.Scope))
                    return CustomCourtTemplateValidationError.InvalidEffectValue;
            }
            return CustomCourtTemplateValidationError.None;
        }

        public static CustomCourtTemplateValidationError ValidateGraph(
            IEnumerable<string> officeIds, IEnumerable<CustomCourtEdge> edges)
        {
            if (officeIds == null)
                return CustomCourtTemplateValidationError.MissingOffice;

            var nodes = new HashSet<string>(officeIds.Where(
                item => item != null), StringComparer.Ordinal);
            if (nodes.Count == 0)
                return CustomCourtTemplateValidationError.MissingOffice;
            if (edges == null)
                return CustomCourtTemplateValidationError.None;

            var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
            var adjacency = new Dictionary<string, List<string>>(
                StringComparer.Ordinal);
            int edgeCount = 0;
            foreach (CustomCourtEdge edge in edges)
            {
                if (edge == null || !IsValidOfficeId(edge.FromOfficeId) ||
                    !IsValidOfficeId(edge.ToOfficeId))
                    return CustomCourtTemplateValidationError.InvalidEdge;
                if (!nodes.Contains(edge.FromOfficeId) ||
                    !nodes.Contains(edge.ToOfficeId))
                    return CustomCourtTemplateValidationError.DanglingOffice;
                if (!Enum.IsDefined(typeof(CustomCourtEdgeKind), edge.Kind))
                    return CustomCourtTemplateValidationError.InvalidEdge;

                string key = string.Concat(edge.Kind.ToString(), ":",
                    edge.FromOfficeId, ":", edge.ToOfficeId);
                if (!edgeKeys.Add(key))
                    return CustomCourtTemplateValidationError.DuplicateEdge;
                if (++edgeCount > MaximumEdges)
                    return CustomCourtTemplateValidationError.InvalidEdge;

                List<string> targets;
                if (!adjacency.TryGetValue(edge.FromOfficeId, out targets))
                {
                    targets = new List<string>();
                    adjacency.Add(edge.FromOfficeId, targets);
                }
                targets.Add(edge.ToOfficeId);
            }

            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string node in nodes)
            {
                if (HasCycle(node, adjacency, visiting, visited))
                    return CustomCourtTemplateValidationError.Cycle;
            }
            return CustomCourtTemplateValidationError.None;
        }

        public static CustomCourtTemplateValidationError ValidateGraph(
            IEnumerable<CustomCourtEdge> edges)
        {
            if (edges == null)
                return CustomCourtTemplateValidationError.None;
            return ValidateGraph(edges.SelectMany(edge => edge == null
                    ? Array.Empty<string>()
                    : new[] { edge.FromOfficeId, edge.ToOfficeId }), edges);
        }

        public static bool IsEffectValueValid(CustomCourtEffectId id,
            CustomCourtEffectMode mode, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) ||
                !Enum.IsDefined(typeof(CustomCourtEffectId), id) ||
                !Enum.IsDefined(typeof(CustomCourtEffectMode), mode))
                return false;
            float absolute = Math.Abs(value);
            switch (mode)
            {
                case CustomCourtEffectMode.AddPercent:
                    return absolute <= 50f;
                case CustomCourtEffectMode.AddFlat:
                    return absolute <= 1000f;
                case CustomCourtEffectMode.Multiply:
                    return value >= 0f && value <= 3f;
                default:
                    return false;
            }
        }

        public static float ClampEffectValue(CustomCourtEffectMode mode,
            float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            switch (mode)
            {
                case CustomCourtEffectMode.AddPercent:
                    return Math.Max(-50f, Math.Min(50f, value));
                case CustomCourtEffectMode.AddFlat:
                    return Math.Max(-1000f, Math.Min(1000f, value));
                case CustomCourtEffectMode.Multiply:
                    return Math.Max(0f, Math.Min(3f, value));
                default:
                    return 0f;
            }
        }

        private static bool HasCycle(string node,
            IDictionary<string, List<string>> adjacency,
            ISet<string> visiting, ISet<string> visited)
        {
            if (visiting.Contains(node))
                return true;
            if (visited.Contains(node))
                return false;
            visiting.Add(node);
            List<string> targets;
            if (adjacency.TryGetValue(node, out targets))
            {
                foreach (string target in targets)
                {
                    if (HasCycle(target, adjacency, visiting, visited))
                        return true;
                }
            }
            visiting.Remove(node);
            visited.Add(node);
            return false;
        }

        private static bool IsValidLayer(string layer)
        {
            return layer == CourtOfficeLayer.Primitive ||
                layer == CourtOfficeLayer.Central ||
                layer == CourtOfficeLayer.City ||
                layer == CourtOfficeLayer.Military ||
                layer == CourtOfficeLayer.Censor ||
                layer == CourtOfficeLayer.Feudatory;
        }

        private static bool IsValidLayout(CustomCourtOfficeLayout layout)
        {
            return layout != null && !float.IsNaN(layout.X) &&
                !float.IsInfinity(layout.X) && !float.IsNaN(layout.Y) &&
                !float.IsInfinity(layout.Y) && layout.X >= -10000f &&
                layout.X <= 10000f && layout.Y >= -10000f &&
                layout.Y <= 10000f && layout.Lane >= 0 && layout.Lane <= 32;
        }

        private static bool IsValidRequirement(
            CustomCourtOfficeRequirement requirement)
        {
            if (requirement == null || requirement.MinimumRank < 0 ||
                requirement.MinimumRank > 100)
                return false;
            return IsOptionalStableId(requirement.RequiredSchoolId) &&
                IsOptionalStableId(requirement.RequiredTraitId) &&
                IsOptionalStableId(requirement.RequiredOfficeId);
        }

        private static bool IsOptionalStableId(string value)
        {
            return string.IsNullOrEmpty(value) || IsValidTemplateId(value);
        }

        private static bool IsValidScope(CustomCourtEffectScope scope)
        {
            return Enum.IsDefined(typeof(CustomCourtEffectScope), scope);
        }
    }
}
