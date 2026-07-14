using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    [Flags]
    public enum HistoricalSchoolRevisionMask
    {
        None = 0,
        Residence = 1,
        Presence = 2,
        Service = 4,
        Structure = 8,
        Score = 16,
        Activity = 32
    }

    public static class HistoricalSchoolRevisionRules
    {
        public static HistoricalSchoolRevisionMask ClassifyAffiliation(
            long pOldResidence,
            long pNewResidence,
            bool pOldPresent,
            bool pNewPresent,
            long pOldService,
            long pNewService)
        {
            HistoricalSchoolRevisionMask result = HistoricalSchoolRevisionMask.None;
            if (pOldResidence != pNewResidence)
                result |= HistoricalSchoolRevisionMask.Residence;
            if (pOldPresent != pNewPresent)
                result |= HistoricalSchoolRevisionMask.Presence;
            if (pOldService != pNewService)
                result |= HistoricalSchoolRevisionMask.Service;
            return result;
        }
    }

    public interface IHistoricalSchoolRevisionSource
    {
        long StructureRevision(string pSchoolId);
        long ScoreRevision(string pSchoolId);
        long ActivityRevision(string pSchoolId);
        long ResidenceRevisionForCity(long pCityId);
    }

    public sealed class HistoricalSchoolRosterRevisionStamp
    {
        private readonly string _schoolId;
        private readonly long _structureRevision;
        private readonly long _scoreRevision;
        private readonly long _activityRevision;
        private readonly long[] _cityIds;
        private readonly long[] _cityResidenceRevisions;

        private HistoricalSchoolRosterRevisionStamp(string pSchoolId,
            long pStructureRevision, long pScoreRevision, long pActivityRevision,
            long[] pCityIds, long[] pCityResidenceRevisions)
        {
            _schoolId = pSchoolId ?? "";
            _structureRevision = pStructureRevision;
            _scoreRevision = pScoreRevision;
            _activityRevision = pActivityRevision;
            _cityIds = pCityIds ?? Array.Empty<long>();
            _cityResidenceRevisions = pCityResidenceRevisions ?? Array.Empty<long>();
        }

        public int CityCount => _cityIds.Length;

        public static HistoricalSchoolRosterRevisionStamp Capture(string pSchoolId,
            IEnumerable<long> pCityIds, IHistoricalSchoolRevisionSource pSource)
        {
            if (pSource == null) throw new ArgumentNullException(nameof(pSource));
            string schoolId = pSchoolId ?? "";
            var uniqueCityIds = new HashSet<long>();
            if (pCityIds != null)
                foreach (long cityId in pCityIds)
                    if (cityId >= 0) uniqueCityIds.Add(cityId);
            var cityIds = new long[uniqueCityIds.Count];
            uniqueCityIds.CopyTo(cityIds);
            Array.Sort(cityIds);
            var cityRevisions = new long[cityIds.Length];
            for (int index = 0; index < cityIds.Length; index++)
                cityRevisions[index] = pSource.ResidenceRevisionForCity(cityIds[index]);
            return new HistoricalSchoolRosterRevisionStamp(schoolId,
                pSource.StructureRevision(schoolId), pSource.ScoreRevision(schoolId),
                pSource.ActivityRevision(schoolId), cityIds, cityRevisions);
        }

        public bool IsCurrent(string pSchoolId,
            IHistoricalSchoolRevisionSource pSource)
        {
            if (pSource == null || !string.Equals(_schoolId, pSchoolId ?? "",
                    StringComparison.Ordinal) ||
                _structureRevision != pSource.StructureRevision(_schoolId) ||
                _scoreRevision != pSource.ScoreRevision(_schoolId) ||
                _activityRevision != pSource.ActivityRevision(_schoolId)) return false;
            for (int index = 0; index < _cityIds.Length; index++)
                if (_cityResidenceRevisions[index] !=
                    pSource.ResidenceRevisionForCity(_cityIds[index])) return false;
            return true;
        }
    }
}
