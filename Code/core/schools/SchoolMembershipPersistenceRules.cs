using System;

namespace AncientWarfare3.core.schools
{
    public readonly struct SchoolMembershipStableIdentity :
        IEquatable<SchoolMembershipStableIdentity>
    {
        public SchoolMembershipStableIdentity(long membershipId, long actorId,
            string schoolId, string sourceType, string sourceId,
            long teacherActorId, long cityId, int generation, int startYear)
        {
            MembershipId = membershipId;
            ActorId = actorId;
            SchoolId = schoolId ?? "";
            SourceType = sourceType ?? "";
            SourceId = sourceId ?? "";
            TeacherActorId = teacherActorId;
            CityId = cityId;
            Generation = generation;
            StartYear = startYear;
        }

        public long MembershipId { get; }
        public long ActorId { get; }
        public string SchoolId { get; }
        public string SourceType { get; }
        public string SourceId { get; }
        public long TeacherActorId { get; }
        public long CityId { get; }
        public int Generation { get; }
        public int StartYear { get; }

        public bool Equals(SchoolMembershipStableIdentity pOther)
        {
            return MembershipId == pOther.MembershipId &&
                   ActorId == pOther.ActorId &&
                   string.Equals(SchoolId, pOther.SchoolId,
                       StringComparison.Ordinal) &&
                   string.Equals(SourceType, pOther.SourceType,
                       StringComparison.Ordinal) &&
                   string.Equals(SourceId, pOther.SourceId,
                       StringComparison.Ordinal) &&
                   TeacherActorId == pOther.TeacherActorId &&
                   CityId == pOther.CityId && Generation == pOther.Generation &&
                   StartYear == pOther.StartYear;
        }

        public override bool Equals(object pObject)
        {
            return pObject is SchoolMembershipStableIdentity other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MembershipId.GetHashCode();
                hash = hash * 397 ^ ActorId.GetHashCode();
                hash = hash * 397 ^ SchoolId.GetHashCode();
                hash = hash * 397 ^ SourceType.GetHashCode();
                hash = hash * 397 ^ SourceId.GetHashCode();
                hash = hash * 397 ^ TeacherActorId.GetHashCode();
                hash = hash * 397 ^ CityId.GetHashCode();
                hash = hash * 397 ^ Generation;
                hash = hash * 397 ^ StartYear;
                return hash;
            }
        }
    }

    public static class SchoolMembershipPersistenceRules
    {
        public static bool ReputationMatches(double pPersisted, float pRuntime)
        {
            if (double.IsNaN(pPersisted) || double.IsInfinity(pPersisted) ||
                float.IsNaN(pRuntime) || float.IsInfinity(pRuntime)) return false;
            return (float)pPersisted == pRuntime;
        }

        public static bool CanPersistPendingActor(bool pHasData, bool pAlive,
            bool pRekt, long pExpectedActorId, long pRuntimeActorId)
        {
            return pHasData && pAlive && !pRekt && pExpectedActorId >= 0 &&
                   pRuntimeActorId == pExpectedActorId;
        }
    }
}
