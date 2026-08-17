using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtPrerequisiteRules
    {
        public static bool CanAppoint(bool vacancyOpen,
            bool institutionAllows, bool prerequisitesSatisfied,
            bool candidateEligible)
        {
            return vacancyOpen && institutionAllows &&
                prerequisitesSatisfied && candidateEligible;
        }

        public static int ResolveHierarchyRank(int parentRank,
            int edgeDistance)
        {
            if (parentRank < 0)
                parentRank = 0;
            if (edgeDistance < 0)
                edgeDistance = 0;
            return Math.Min(100, parentRank + edgeDistance);
        }

        public static bool HasPrerequisiteOffice(
            IEnumerable<CustomCourtEdge> edges, string officeId,
            ISet<string> filledOfficeIds)
        {
            if (string.IsNullOrEmpty(officeId) || filledOfficeIds == null)
                return false;
            if (edges == null)
                return true;
            foreach (CustomCourtEdge edge in edges)
            {
                if (edge == null || edge.Kind !=
                    CustomCourtEdgeKind.AppointmentPrerequisite ||
                    !string.Equals(edge.ToOfficeId, officeId,
                        StringComparison.Ordinal))
                    continue;
                if (!filledOfficeIds.Contains(edge.FromOfficeId))
                    return false;
            }
            return true;
        }
    }
}
