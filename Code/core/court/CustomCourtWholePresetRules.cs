using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public readonly struct CustomCourtWorkflowLayout
    {
        public CustomCourtWorkflowLayout(float canvasWidth,
            float canvasHeight, float toolbarViewportWidth,
            float toolbarViewportHeight)
        {
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            ToolbarViewportWidth = toolbarViewportWidth;
            ToolbarViewportHeight = toolbarViewportHeight;
        }

        public float CanvasWidth { get; }
        public float CanvasHeight { get; }
        public float ToolbarLeft => 0f;
        public float ToolbarViewportWidth { get; }
        public float ToolbarViewportHeight { get; }
        public float VisibleCanvasCenterOffsetX =>
            ToolbarViewportWidth * 0.5f;
    }

    public static class CustomCourtWorkflowLayoutRules
    {
        public static CustomCourtWorkflowLayout Resolve(float contentWidth,
            float viewportHeight, float toolbarWidth, float toolbarScale,
            float scrollbarWidth)
        {
            float canvasWidth = Math.Max(1f, contentWidth);
            float canvasHeight = Math.Max(1f, viewportHeight);
            float toolbarViewportWidth = Math.Max(1f,
                toolbarWidth * toolbarScale + scrollbarWidth);
            return new CustomCourtWorkflowLayout(canvasWidth, canvasHeight,
                toolbarViewportWidth, canvasHeight);
        }
    }

    public readonly struct CustomCourtWholePresetOption
    {
        public CustomCourtWholePresetOption(string institutionId,
            bool unlocked)
        {
            InstitutionId = institutionId ?? string.Empty;
            Unlocked = unlocked;
        }

        public string InstitutionId { get; }
        public bool Unlocked { get; }
    }

    public static class CustomCourtWholePresetRules
    {
        private const float HorizontalSpacing = 150f;
        private const float VerticalSpacing = 96f;

        public static IReadOnlyList<CustomCourtWholePresetOption> Options(
            CourtProfileId profileId, string currentInstitutionId)
        {
            string[] institutions;
            switch (profileId)
            {
                case CourtProfileId.Xia:
                    institutions = new[]
                    {
                        CourtInstitutionId.Zhou,
                        CourtInstitutionId.Han,
                        CourtInstitutionId.Tang,
                        CourtInstitutionId.Song
                    };
                    break;
                case CourtProfileId.Western:
                    institutions = new[]
                    {
                        CourtInstitutionId.WesternBureaucratic,
                        CourtInstitutionId.WesternFeudalBureaucratic
                    };
                    break;
                default:
                    return Array.Empty<CustomCourtWholePresetOption>();
            }

            int currentRank = CourtInstitutionRules.Rank(
                currentInstitutionId);
            return institutions.Select(institutionId =>
                new CustomCourtWholePresetOption(institutionId,
                    CourtInstitutionRules.Rank(institutionId) <= currentRank))
                .ToArray();
        }

        public static CustomCourtWholePresetOption NextUnlockedPreset(
            IReadOnlyList<CustomCourtWholePresetOption> options,
            string currentInstitutionId)
        {
            if (options == null || options.Count == 0)
                return default;

            int start = -1;
            for (int index = 0; index < options.Count; index++)
            {
                if (string.Equals(options[index].InstitutionId,
                        currentInstitutionId, StringComparison.Ordinal))
                {
                    start = index;
                    break;
                }
            }

            for (int offset = 1; offset <= options.Count; offset++)
            {
                int index = (start + offset) % options.Count;
                if (options[index].Unlocked)
                    return options[index];
            }
            return default;
        }

        public static bool TryReplace(CustomCourtTemplate source,
            ICourtProfile profile, string institutionId,
            Func<CourtOfficeDefinition, CustomCourtLocalizedText> nameResolver,
            float centerX, float centerY, out CustomCourtTemplate replacement)
        {
            replacement = source;
            if (source == null || profile == null ||
                string.IsNullOrEmpty(institutionId)) return false;

            IReadOnlyList<string> sourceIds =
                profile.OfficeIdsForInstitution(institutionId);
            var definitions = new List<CourtOfficeDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string officeId in sourceIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(officeId) || !seen.Add(officeId))
                    continue;
                CourtOfficeDefinition definition = profile.FindOffice(officeId);
                if (definition != null) definitions.Add(definition);
            }
            if (definitions.Count == 0) return false;

            List<CustomCourtOffice> offices = BuildOffices(definitions,
                nameResolver, centerX, centerY);
            replacement = new CustomCourtTemplate
            {
                SchemaVersion = source.SchemaVersion,
                Id = source.Id,
                Revision = source.Revision,
                Name = CloneName(source.Name),
                Offices = offices,
                Edges = new List<CustomCourtEdge>(),
                LocalTemplates = source.LocalTemplates ??
                    new List<CustomLocalCourtTemplate>(),
                ArchivedCrossLayerEdges = source.ArchivedCrossLayerEdges ??
                    new List<CustomCourtEdge>()
            };
            return true;
        }

        private static List<CustomCourtOffice> BuildOffices(
            IReadOnlyList<CourtOfficeDefinition> definitions,
            Func<CourtOfficeDefinition, CustomCourtLocalizedText> nameResolver,
            float centerX, float centerY)
        {
            var groups = definitions.GroupBy(definition => new
                {
                    LayerOrder = LayerOrder(definition.Layer),
                    definition.Layer,
                    definition.Grade
                })
                .OrderBy(group => group.Key.LayerOrder)
                .ThenBy(group => group.Key.Grade)
                .ThenBy(group => group.Key.Layer, StringComparer.Ordinal)
                .ToArray();
            float firstRowY = centerY - (groups.Length - 1) *
                VerticalSpacing * 0.5f;
            var layoutById = new Dictionary<string, CustomCourtOfficeLayout>(
                StringComparer.Ordinal);
            for (int row = 0; row < groups.Length; row++)
            {
                CourtOfficeDefinition[] items = groups[row].ToArray();
                float firstX = centerX - (items.Length - 1) *
                    HorizontalSpacing * 0.5f;
                for (int column = 0; column < items.Length; column++)
                    layoutById[items[column].Id] = new CustomCourtOfficeLayout
                    {
                        X = firstX + column * HorizontalSpacing,
                        Y = firstRowY + row * VerticalSpacing,
                        Lane = row
                    };
            }

            return definitions.Select(definition => new CustomCourtOffice
            {
                Id = definition.Id,
                Name = CloneName(nameResolver?.Invoke(definition)),
                Layer = definition.Layer,
                Grade = definition.Grade,
                Slots = 1,
                MilitaryCapable = definition.MilitaryCapable,
                PreferredSchoolId = definition.PreferredSchoolId,
                Layout = layoutById[definition.Id],
                Requirements = new CustomCourtOfficeRequirement(),
                Effects = new List<CustomCourtOfficeEffect>()
            }).ToList();
        }

        private static int LayerOrder(string layer)
        {
            switch (layer ?? string.Empty)
            {
                case CourtOfficeLayer.Central: return 0;
                case CourtOfficeLayer.Censor: return 1;
                case CourtOfficeLayer.Military: return 2;
                case CourtOfficeLayer.City: return 3;
                case CourtOfficeLayer.Feudatory: return 4;
                case CourtOfficeLayer.Primitive: return 5;
                default: return 6;
            }
        }

        private static CustomCourtLocalizedText CloneName(
            CustomCourtLocalizedText source)
        {
            return new CustomCourtLocalizedText
            {
                Chinese = source?.Chinese ?? string.Empty,
                English = source?.English ?? string.Empty
            };
        }
    }
}
