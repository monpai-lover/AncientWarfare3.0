using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public enum CustomCourtEdgeKind
    {
        Management = 0,
        AppointmentPrerequisite = 1
    }

    public enum CustomCourtEffectId
    {
        TaxIncome = 0,
        FoodProduction = 1,
        ArmyMorale = 2,
        CivilOrder = 3,
        CourtInfluence = 4
    }

    public enum CustomCourtEffectMode
    {
        AddPercent = 0,
        AddFlat = 1,
        Multiply = 2
    }

    public enum CustomCourtEffectScope
    {
        Kingdom = 0,
        City = 1,
        Army = 2,
        Court = 3
    }

    public enum CustomCourtValidationSeverity
    {
        Warning = 0,
        Error = 1
    }

    public enum CustomLocalCourtDefaultKind
    {
        ManualOnly = 0,
        CivilDefault = 1,
        MilitaryDefault = 2
    }

    public enum CustomCourtTemplateValidationError
    {
        None = 0,
        UnsupportedSchemaVersion,
        InvalidTemplateId,
        MissingOffice,
        InvalidOfficeId,
        DuplicateOffice,
        InvalidOfficeGrade,
        InvalidOfficeSlots,
        InvalidOfficeLayer,
        InvalidEdge,
        DuplicateEdge,
        DanglingOffice,
        Cycle,
        InvalidLayout,
        InvalidRequirement,
        InvalidEffect,
        InvalidEffectValue
    }

    public sealed class CustomCourtLocalizedText
    {
        public string Chinese { get; set; } = string.Empty;
        public string English { get; set; } = string.Empty;

        public bool IsEmpty => string.IsNullOrWhiteSpace(Chinese) &&
            string.IsNullOrWhiteSpace(English);
    }

    public sealed class CustomCourtOfficeLayout
    {
        public float X { get; set; }
        public float Y { get; set; }
        public int Lane { get; set; }
    }

    public sealed class CustomCourtOfficeRequirement
    {
        public int MinimumRank { get; set; }
        public string RequiredSchoolId { get; set; } = string.Empty;
        public string RequiredTraitId { get; set; } = string.Empty;
        public string RequiredOfficeId { get; set; } = string.Empty;
    }

    public sealed class CustomCourtOfficeEffect
    {
        public CustomCourtEffectId Id { get; set; }
        public CustomCourtEffectMode Mode { get; set; }
        public CustomCourtEffectScope Scope { get; set; }
        public float Value { get; set; }
    }

    public sealed class CustomCourtOffice
    {
        public string Id { get; set; } = string.Empty;
        public CustomCourtLocalizedText Name { get; set; } =
            new CustomCourtLocalizedText();
        public string Layer { get; set; } = string.Empty;
        public int Grade { get; set; }
        public int Slots { get; set; } = 1;
        public bool MilitaryCapable { get; set; }
        public string PreferredSchoolId { get; set; } = string.Empty;
        public CustomCourtOfficeLayout Layout { get; set; } =
            new CustomCourtOfficeLayout();
        public CustomCourtOfficeRequirement Requirements { get; set; } =
            new CustomCourtOfficeRequirement();
        public List<CustomCourtOfficeEffect> Effects { get; set; } =
            new List<CustomCourtOfficeEffect>();
    }

    public sealed class CustomCourtEdge
    {
        public string FromOfficeId { get; set; } = string.Empty;
        public string ToOfficeId { get; set; } = string.Empty;
        public CustomCourtEdgeKind Kind { get; set; }
    }

    public sealed class CustomLocalCourtTemplate
    {
        public string Id { get; set; } = string.Empty;
        public CustomCourtLocalizedText Name { get; set; } =
            new CustomCourtLocalizedText();
        public CustomLocalCourtDefaultKind DefaultKind { get; set; } =
            CustomLocalCourtDefaultKind.ManualOnly;
        public List<CustomCourtOffice> Offices { get; set; } =
            new List<CustomCourtOffice>();
        public List<CustomCourtEdge> Edges { get; set; } =
            new List<CustomCourtEdge>();
    }

    public sealed class CustomCourtTemplate
    {
        public int SchemaVersion { get; set; } = 2;
        public string Id { get; set; } = string.Empty;
        public int Revision { get; set; } = 1;
        public CustomCourtLocalizedText Name { get; set; } =
            new CustomCourtLocalizedText();
        public List<CustomCourtOffice> Offices { get; set; } =
            new List<CustomCourtOffice>();
        public List<CustomCourtEdge> Edges { get; set; } =
            new List<CustomCourtEdge>();
        public List<CustomLocalCourtTemplate> LocalTemplates { get; set; } =
            new List<CustomLocalCourtTemplate>();
        public List<CustomCourtEdge> ArchivedCrossLayerEdges { get; set; } =
            new List<CustomCourtEdge>();
    }

    public readonly struct CustomCourtValidationIssue
    {
        public CustomCourtValidationIssue(
            CustomCourtTemplateValidationError code,
            CustomCourtValidationSeverity severity, string message)
        {
            Code = code;
            Severity = severity;
            Message = message ?? string.Empty;
        }

        public CustomCourtTemplateValidationError Code { get; }
        public CustomCourtValidationSeverity Severity { get; }
        public string Message { get; }
    }
}
