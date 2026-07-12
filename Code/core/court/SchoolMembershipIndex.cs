using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public sealed class SchoolMembershipIndex
    {
        private readonly Dictionary<long, string> _schoolByActor = new Dictionary<long, string>();
        private readonly Dictionary<string, HashSet<long>> _actorsBySchool =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);

        public void Update(long pActorId, string pSchoolId, bool alive)
        {
            Remove(pActorId);
            if (!alive || pActorId < 0 || CourtSchoolRegistry.Find(pSchoolId) == null) return;
            _schoolByActor[pActorId] = pSchoolId;
            if (!_actorsBySchool.TryGetValue(pSchoolId, out HashSet<long> actors))
            {
                actors = new HashSet<long>();
                _actorsBySchool[pSchoolId] = actors;
            }
            actors.Add(pActorId);
        }

        public void Remove(long pActorId)
        {
            if (!_schoolByActor.TryGetValue(pActorId, out string previous)) return;
            _schoolByActor.Remove(pActorId);
            if (!_actorsBySchool.TryGetValue(previous, out HashSet<long> actors)) return;
            actors.Remove(pActorId);
            if (actors.Count == 0) _actorsBySchool.Remove(previous);
        }

        public int Count(string pSchoolId)
        {
            return _actorsBySchool.TryGetValue(pSchoolId ?? "", out HashSet<long> actors)
                ? actors.Count
                : 0;
        }

        public long[] Members(string pSchoolId)
        {
            if (!_actorsBySchool.TryGetValue(pSchoolId ?? "", out HashSet<long> actors))
                return Array.Empty<long>();
            var result = new long[actors.Count];
            actors.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        public void Clear()
        {
            _schoolByActor.Clear();
            _actorsBySchool.Clear();
        }
    }
}
