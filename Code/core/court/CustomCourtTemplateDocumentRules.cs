using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtTemplateDocumentRules
    {
        public static CustomCourtTemplate CreateCentralDocument(
            CustomCourtTemplate pSource)
        {
            CustomCourtTemplate document =
                CustomCourtTemplateJsonCodec.Normalize(pSource);
            document.Scope = CustomCourtTemplateScope.CentralCourt;
            CustomCourtTemplateJsonCodec.EnsureRegionalLayer(document);
            document.LocalTemplates = new List<CustomLocalCourtTemplate>();
            document.ArchivedCrossLayerEdges = new List<CustomCourtEdge>();
            return document;
        }

        public static CustomCourtTemplate CreateLocalDocument(
            CustomLocalCourtTemplate pSource, int pRevision = 1)
        {
            if (pSource == null)
                throw new ArgumentNullException(nameof(pSource));
            var envelope = new CustomCourtTemplate
            {
                SchemaVersion = CustomCourtTemplateRules.CurrentSchemaVersion,
                Scope = CustomCourtTemplateScope.LocalGovernment,
                Id = pSource.Id,
                Revision = Math.Max(1, pRevision),
                Name = pSource.Name ?? new CustomCourtLocalizedText(),
                RegionalGovernmentLayer = null,
                LocalTemplates = new List<CustomLocalCourtTemplate>
                {
                    pSource
                }
            };
            return CustomCourtTemplateJsonCodec.Normalize(envelope);
        }

        public static bool IsCentralDocument(CustomCourtTemplate pDocument)
        {
            return pDocument != null &&
                   CustomCourtTemplateRules.Validate(pDocument) ==
                   CustomCourtTemplateValidationError.None &&
                   pDocument.Offices != null && pDocument.Offices.Count > 0 &&
                   (pDocument.LocalTemplates == null ||
                    pDocument.LocalTemplates.Count == 0) &&
                   (pDocument.ArchivedCrossLayerEdges == null ||
                    pDocument.ArchivedCrossLayerEdges.Count == 0);
        }

        public static bool TryGetLocalDocument(CustomCourtTemplate pDocument,
            out CustomLocalCourtTemplate pTemplate)
        {
            pTemplate = null;
            if (pDocument == null ||
                CustomCourtTemplateRules.Validate(pDocument) !=
                CustomCourtTemplateValidationError.None ||
                pDocument.Offices == null || pDocument.Offices.Count != 0 ||
                pDocument.Edges == null || pDocument.Edges.Count != 0 ||
                pDocument.LocalTemplates == null ||
                pDocument.LocalTemplates.Count != 1 ||
                pDocument.ArchivedCrossLayerEdges == null ||
                pDocument.ArchivedCrossLayerEdges.Count != 0)
                return false;
            pTemplate = CloneLocal(pDocument.LocalTemplates[0]);
            return pTemplate != null;
        }

        public static bool TryApplyCentralDocument(
            CustomCourtTemplate pCurrent, CustomCourtTemplate pDocument,
            out CustomCourtTemplate pMerged)
        {
            pMerged = null;
            if (pCurrent == null || !IsCentralDocument(pDocument))
                return false;
            CustomCourtTemplate current =
                CustomCourtTemplateJsonCodec.Normalize(pCurrent);
            CustomCourtTemplate central =
                CustomCourtTemplateJsonCodec.Normalize(pDocument);
            current.SchemaVersion = central.SchemaVersion;
            current.Id = central.Id;
            current.Revision = central.Revision;
            current.Name = central.Name;
            current.Offices = central.Offices;
            current.Edges = central.Edges;
            current.RegionalGovernmentLayer = central.RegionalGovernmentLayer;
            CustomCourtTemplateJsonCodec.EnsureRegionalLayer(current);
            if (CustomCourtTemplateRules.Validate(current) !=
                CustomCourtTemplateValidationError.None) return false;
            pMerged = current;
            return true;
        }

        public static bool TryApplyLocalDocument(
            CustomCourtTemplate pCurrent, CustomCourtTemplate pDocument,
            out CustomCourtTemplate pMerged, out string pImportedTemplateId)
        {
            pMerged = null;
            pImportedTemplateId = string.Empty;
            if (pCurrent == null || !TryGetLocalDocument(pDocument,
                    out CustomLocalCourtTemplate imported)) return false;
            CustomCourtTemplate current =
                CustomCourtTemplateJsonCodec.Normalize(pCurrent);
            current.LocalTemplates = current.LocalTemplates ??
                                     new List<CustomLocalCourtTemplate>();
            int index = current.LocalTemplates.FindIndex(template =>
                template != null && string.Equals(template.Id, imported.Id,
                    StringComparison.Ordinal));
            if (index < 0 && current.LocalTemplates.Count >=
                CustomLocalCourtTemplateRules.MaximumTemplates) return false;
            if (imported.DefaultKind != CustomLocalCourtDefaultKind.ManualOnly)
                foreach (CustomLocalCourtTemplate other in
                         current.LocalTemplates.Where(other => other != null &&
                             other.Id != imported.Id &&
                             other.DefaultKind == imported.DefaultKind))
                    other.DefaultKind = CustomLocalCourtDefaultKind.ManualOnly;
            if (index >= 0) current.LocalTemplates[index] = imported;
            else current.LocalTemplates.Add(imported);
            if (CustomCourtTemplateRules.Validate(current) !=
                CustomCourtTemplateValidationError.None) return false;
            pMerged = CustomCourtTemplateJsonCodec.Normalize(current);
            pImportedTemplateId = imported.Id;
            return true;
        }

        private static CustomLocalCourtTemplate CloneLocal(
            CustomLocalCourtTemplate pTemplate)
        {
            if (pTemplate == null) return null;
            CustomCourtTemplate envelope = CreateLocalDocument(pTemplate);
            return envelope.LocalTemplates.Single();
        }
    }
}
