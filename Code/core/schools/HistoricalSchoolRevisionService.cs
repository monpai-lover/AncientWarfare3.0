using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolRevisionService
    {
        private sealed class LiveRevisionSource : IHistoricalSchoolRevisionSource
        {
            public long StructureRevision(string pSchoolId) =>
                HistoricalSchoolRevisionService.StructureRevision(pSchoolId);

            public long ScoreRevision(string pSchoolId) =>
                HistoricalSchoolRevisionService.ScoreRevision(pSchoolId);

            public long ActivityRevision(string pSchoolId) =>
                HistoricalSchoolRevisionService.ActivityRevision(pSchoolId);

            public long ResidenceRevisionForCity(long pCityId) =>
                HistoricalSchoolRevisionService.ResidenceRevisionForCity(pCityId);
        }

        private sealed class CityRevisions
        {
            public long Residence;
            public long Presence;
            public long Service;
        }

        private static readonly Dictionary<string, long> StructureBySchool =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> ScoreBySchool =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> ActivityBySchool =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<long, CityRevisions> ByCity =
            new Dictionary<long, CityRevisions>();
        public static IHistoricalSchoolRevisionSource Source { get; } =
            new LiveRevisionSource();

        public static long StructureRevision(string pSchoolId) =>
            Get(StructureBySchool, pSchoolId);

        public static long ScoreRevision(string pSchoolId) =>
            Get(ScoreBySchool, pSchoolId);

        public static long ActivityRevision(string pSchoolId) =>
            Get(ActivityBySchool, pSchoolId);

        public static long ResidenceRevisionForCity(long pCityId) =>
            GetCity(pCityId)?.Residence ?? 0L;

        public static long PresenceRevisionForCity(long pCityId) =>
            GetCity(pCityId)?.Presence ?? 0L;

        public static long ServiceRevisionForCity(long pCityId) =>
            GetCity(pCityId)?.Service ?? 0L;

        public static void ApplyMembershipChange(
            SchoolMembershipRecord pOld,
            SchoolMembershipRecord pNext)
        {
            if (MembershipStructureExact(pOld, pNext))
            {
                if (pOld != null && pNext != null &&
                    !pOld.Reputation.Equals(pNext.Reputation))
                    Increment(ScoreBySchool, pNext.SchoolId);
                return;
            }

            string oldSchool = pOld?.SchoolId ?? "";
            string nextSchool = pNext?.SchoolId ?? "";
            Increment(StructureBySchool, oldSchool);
            if (!string.Equals(oldSchool, nextSchool, StringComparison.Ordinal))
                Increment(StructureBySchool, nextSchool);
        }

        public static HistoricalSchoolRevisionMask ApplyAffiliationChange(
            HistoricalSchoolAffiliationSnapshot pOld,
            HistoricalSchoolAffiliationSnapshot pNext)
        {
            long oldResidence = pOld?.ResidenceCityId ?? -1L;
            long nextResidence = pNext?.ResidenceCityId ?? -1L;
            long oldService = pOld?.ServiceKingdomId ?? -1L;
            long nextService = pNext?.ServiceKingdomId ?? -1L;
            HistoricalSchoolRevisionMask mask =
                HistoricalSchoolRevisionRules.ClassifyAffiliation(
                    oldResidence,
                    nextResidence,
                    IsPresent(pOld),
                    IsPresent(pNext),
                    oldService,
                    nextService);
            if (mask == HistoricalSchoolRevisionMask.None) return mask;

            MarkCity(oldResidence, mask);
            if (nextResidence != oldResidence) MarkCity(nextResidence, mask);
            if (oldResidence >= 0) CitySchoolSnapshotService.MarkDirtyById(oldResidence);
            if (nextResidence >= 0 && nextResidence != oldResidence)
                CitySchoolSnapshotService.MarkDirtyById(nextResidence);
            return mask;
        }

        public static void MarkActivity(string pSchoolId)
        {
            Increment(ActivityBySchool, pSchoolId);
        }

        public static void Clear()
        {
            StructureBySchool.Clear();
            ScoreBySchool.Clear();
            ActivityBySchool.Clear();
            ByCity.Clear();
        }

        internal static bool IsPresent(HistoricalSchoolAffiliationSnapshot pState)
        {
            if (pState == null) return true;
            return pState.LifecycleState !=
                       HistoricalSchoolLifecycleState.ChoosingDestination &&
                   pState.LifecycleState != HistoricalSchoolLifecycleState.Travelling &&
                   pState.LifecycleState != HistoricalSchoolLifecycleState.Voyage &&
                   pState.LifecycleState != HistoricalSchoolLifecycleState.Dead;
        }

        private static bool MembershipStructureExact(
            SchoolMembershipRecord pLeft,
            SchoolMembershipRecord pRight)
        {
            if (ReferenceEquals(pLeft, pRight)) return true;
            return pLeft != null && pRight != null &&
                   pLeft.MembershipId == pRight.MembershipId &&
                   pLeft.ActorId == pRight.ActorId &&
                   pLeft.SchoolId == pRight.SchoolId &&
                   pLeft.Source == pRight.Source &&
                   pLeft.SourceId == pRight.SourceId &&
                   pLeft.TeacherActorId == pRight.TeacherActorId &&
                   pLeft.CityId == pRight.CityId &&
                   pLeft.Generation == pRight.Generation &&
                   pLeft.StartYear == pRight.StartYear &&
                   pLeft.EndYear == pRight.EndYear &&
                   pLeft.Active == pRight.Active &&
                   pLeft.EndReason == pRight.EndReason &&
                   pLeft.Standing == pRight.Standing &&
                   pLeft.LoyaltyUntilYear == pRight.LoyaltyUntilYear;
        }

        private static CityRevisions GetCity(long pCityId)
        {
            return pCityId >= 0 && ByCity.TryGetValue(pCityId, out CityRevisions value)
                ? value
                : null;
        }

        private static void MarkCity(
            long pCityId,
            HistoricalSchoolRevisionMask pMask)
        {
            if (pCityId < 0) return;
            if (!ByCity.TryGetValue(pCityId, out CityRevisions revisions))
            {
                revisions = new CityRevisions();
                ByCity.Add(pCityId, revisions);
            }
            if ((pMask & HistoricalSchoolRevisionMask.Residence) != 0)
                revisions.Residence = Next(revisions.Residence);
            if ((pMask & HistoricalSchoolRevisionMask.Presence) != 0)
                revisions.Presence = Next(revisions.Presence);
            if ((pMask & HistoricalSchoolRevisionMask.Service) != 0)
                revisions.Service = Next(revisions.Service);
        }

        private static long Get(
            Dictionary<string, long> pValues,
            string pSchoolId)
        {
            return !string.IsNullOrEmpty(pSchoolId) &&
                   pValues.TryGetValue(pSchoolId, out long value)
                ? value
                : 0L;
        }

        private static void Increment(
            Dictionary<string, long> pValues,
            string pSchoolId)
        {
            if (string.IsNullOrEmpty(pSchoolId)) return;
            pValues.TryGetValue(pSchoolId, out long current);
            pValues[pSchoolId] = Next(current);
        }

        private static long Next(long pValue)
        {
            return pValue == long.MaxValue ? 1L : pValue + 1L;
        }
    }
}
