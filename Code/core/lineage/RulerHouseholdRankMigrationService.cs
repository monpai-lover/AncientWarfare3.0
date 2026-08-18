using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public sealed class RulerHouseholdRankMigrationEntry
    {
        public RulerHouseholdRankMigrationEntry(long relationshipId,
            RulerHouseholdKind kind, string rankCode, int startYear,
            double startTime, bool active, bool closed = false,
            string endReason = "", bool needsWrite = false)
        {
            RelationshipId = relationshipId;
            Kind = kind;
            RankCode = rankCode ?? "";
            StartYear = startYear;
            StartTime = startTime;
            Active = active;
            Closed = closed;
            EndReason = endReason ?? "";
            NeedsWrite = needsWrite;
        }

        public long RelationshipId { get; }
        public RulerHouseholdKind Kind { get; }
        public string RankCode { get; }
        public int StartYear { get; }
        public double StartTime { get; }
        public bool Active { get; }
        public bool Closed { get; }
        public string EndReason { get; }
        public bool NeedsWrite { get; }
    }

    public static class RulerHouseholdRankMigrationService
    {
        public const string OverCapacityReason =
            "legacy_harem_over_capacity";

        public static IReadOnlyList<RulerHouseholdRankMigrationEntry>
            AssignLegacy(IEnumerable<RulerHouseholdRankMigrationEntry> pRows)
        {
            if (pRows == null)
                return Array.Empty<RulerHouseholdRankMigrationEntry>();

            List<RulerHouseholdRankMigrationEntry> ordered = pRows
                .Where(pRow => pRow != null)
                .OrderBy(pRow => pRow.Kind ==
                    RulerHouseholdKind.PrincipalWife ? 0 : 1)
                .ThenBy(pRow => pRow.StartYear < 0
                    ? int.MaxValue
                    : pRow.StartYear)
                .ThenBy(pRow => pRow.StartTime < 0d
                    ? double.MaxValue
                    : pRow.StartTime)
                .ThenBy(pRow => pRow.RelationshipId)
                .ToList();

            var result = new List<RulerHouseholdRankMigrationEntry>(
                ordered.Count);
            bool principalAssigned = false;
            int consortSeat = 1;
            for (int i = 0; i < ordered.Count; i++)
            {
                RulerHouseholdRankMigrationEntry row = ordered[i];
                if (!row.Active || row.Closed)
                {
                    result.Add(Copy(row, row.RankCode, row.Closed,
                        row.EndReason, needsWrite: false));
                    continue;
                }

                string expected = "";
                if (row.Kind == RulerHouseholdKind.PrincipalWife &&
                    !principalAssigned)
                {
                    expected = RulerHouseholdRankRules.SeatCode(0);
                    principalAssigned = true;
                }
                else if (row.Kind == RulerHouseholdKind.Consort &&
                         consortSeat <
                         RulerHouseholdRankRules.ImperialSeatCodes.Length)
                {
                    expected = RulerHouseholdRankRules.SeatCode(consortSeat);
                    consortSeat++;
                }

                if (string.IsNullOrEmpty(expected))
                {
                    result.Add(Copy(row, row.RankCode, closed: true,
                        OverCapacityReason, needsWrite: true));
                    continue;
                }

                bool changed = !string.Equals(row.RankCode, expected,
                    StringComparison.Ordinal);
                result.Add(Copy(row, expected, closed: false, "",
                    changed));
            }
            return result;
        }

        private static RulerHouseholdRankMigrationEntry Copy(
            RulerHouseholdRankMigrationEntry pRow, string pRankCode,
            bool closed, string pEndReason, bool needsWrite)
        {
            return new RulerHouseholdRankMigrationEntry(
                pRow.RelationshipId, pRow.Kind, pRankCode,
                pRow.StartYear, pRow.StartTime,
                active: pRow.Active && !closed, closed, pEndReason,
                needsWrite);
        }
    }
}
