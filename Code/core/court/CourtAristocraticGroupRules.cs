using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AncientWarfare3.core.court
{
    public enum CourtAristocraticRole
    {
        Governor = 0,
        CentralSpecialist = 1,
        General = 2,
        CentralMiddle = 3,
        CentralHigh = 4
    }

    public readonly struct CourtAristocraticMemberFact
    {
        public CourtAristocraticMemberFact(long actorId, long shiId,
            string shiName, CourtAristocraticRole role, int influence,
            int merit)
        {
            ActorId = actorId;
            ShiId = shiId;
            ShiName = shiName ?? "";
            Role = role;
            Influence = influence;
            Merit = merit;
        }

        public long ActorId { get; }
        public long ShiId { get; }
        public string ShiName { get; }
        public CourtAristocraticRole Role { get; }
        public int Influence { get; }
        public int Merit { get; }
    }

    public sealed class CourtAristocraticGroup
    {
        public CourtAristocraticGroup(long shiId, string shiName, int power,
            int memberCount, long leaderActorId)
        {
            ShiId = shiId;
            ShiName = shiName ?? "";
            Power = Math.Max(0, power);
            MemberCount = Math.Max(0, memberCount);
            LeaderActorId = leaderActorId;
        }

        public long ShiId { get; }
        public string ShiName { get; }
        public int Power { get; }
        public int MemberCount { get; }
        public long LeaderActorId { get; }
    }

    public static class CourtAristocraticGroupRules
    {
        public const int MaximumGroups = 7;
        public const int MaximumPatronageBonus = 8;

        public static IReadOnlyList<CourtAristocraticGroup> Aggregate(
            IEnumerable<CourtAristocraticMemberFact> pFacts,
            long rulingShiId)
        {
            var strongestByActor = new Dictionary<long,
                CourtAristocraticMemberFact>();
            foreach (CourtAristocraticMemberFact fact in
                     pFacts ?? Array.Empty<CourtAristocraticMemberFact>())
            {
                if (fact.ActorId < 0 || fact.ShiId < 0 ||
                    fact.ShiId == rulingShiId ||
                    string.IsNullOrWhiteSpace(fact.ShiName)) continue;
                if (!strongestByActor.TryGetValue(fact.ActorId,
                        out CourtAristocraticMemberFact current) ||
                    CompareMemberFacts(fact, current) < 0)
                    strongestByActor[fact.ActorId] = fact;
            }

            var builders = new Dictionary<long, GroupBuilder>();
            foreach (CourtAristocraticMemberFact fact in
                     strongestByActor.Values)
            {
                if (!builders.TryGetValue(fact.ShiId,
                        out GroupBuilder builder))
                {
                    builder = new GroupBuilder(fact.ShiId, fact.ShiName);
                    builders.Add(fact.ShiId, builder);
                }
                builder.Add(fact);
            }

            return builders.Values
                .Select(p => p.Build())
                .OrderByDescending(p => p.Power)
                .ThenByDescending(p => p.MemberCount)
                .ThenBy(p => p.ShiId)
                .Take(MaximumGroups)
                .ToArray();
        }

        public static int RolePower(CourtAristocraticRole pRole)
        {
            switch (pRole)
            {
                case CourtAristocraticRole.CentralHigh: return 50;
                case CourtAristocraticRole.CentralMiddle: return 40;
                case CourtAristocraticRole.General: return 35;
                case CourtAristocraticRole.CentralSpecialist: return 30;
                default: return 25;
            }
        }

        public static int MemberPower(CourtAristocraticMemberFact pFact)
        {
            int influence = Math.Min(12,
                Math.Max(0, pFact.Influence) / 10);
            int merit = Math.Min(8, Math.Max(0, pFact.Merit) / 10);
            return RolePower(pFact.Role) + influence + merit;
        }

        public static int PatronageBonus(long pShiId,
            IReadOnlyList<CourtAristocraticGroup> pGroups)
        {
            if (pShiId < 0 || pGroups == null) return 0;
            int count = Math.Min(MaximumGroups, pGroups.Count);
            for (int i = 0; i < count; i++)
            {
                if (pGroups[i]?.ShiId != pShiId) continue;
                if (i == 0) return MaximumPatronageBonus;
                if (i <= 2) return 6;
                if (i <= 4) return 4;
                return 2;
            }
            return 0;
        }

        public static string Encode(
            IEnumerable<CourtAristocraticGroup> pGroups)
        {
            var parts = new List<string>(MaximumGroups);
            foreach (CourtAristocraticGroup group in
                     (pGroups ?? Array.Empty<CourtAristocraticGroup>())
                     .Where(p => p != null && p.ShiId >= 0 &&
                                 !string.IsNullOrWhiteSpace(p.ShiName))
                     .Take(MaximumGroups))
            {
                string name = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(group.ShiName));
                parts.Add(string.Join("~", new[]
                {
                    group.ShiId.ToString(CultureInfo.InvariantCulture),
                    name,
                    group.Power.ToString(CultureInfo.InvariantCulture),
                    group.MemberCount.ToString(CultureInfo.InvariantCulture),
                    group.LeaderActorId.ToString(CultureInfo.InvariantCulture)
                }));
            }
            return string.Join(";", parts);
        }

        public static IReadOnlyList<CourtAristocraticGroup> Decode(string pRaw)
        {
            if (string.IsNullOrWhiteSpace(pRaw))
                return Array.Empty<CourtAristocraticGroup>();
            var result = new List<CourtAristocraticGroup>(MaximumGroups);
            foreach (string row in pRaw.Split(';'))
            {
                if (result.Count >= MaximumGroups) break;
                string[] fields = row.Split('~');
                if (fields.Length != 5 ||
                    !long.TryParse(fields[0], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long shiId) ||
                    !int.TryParse(fields[2], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int power) ||
                    !int.TryParse(fields[3], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int memberCount) ||
                    !long.TryParse(fields[4], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long leaderActorId))
                    continue;
                string name;
                try
                {
                    name = Encoding.UTF8.GetString(
                        Convert.FromBase64String(fields[1]));
                }
                catch
                {
                    continue;
                }
                if (shiId < 0 || string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new CourtAristocraticGroup(shiId, name, power,
                    memberCount, leaderActorId));
            }
            return result;
        }

        private static int CompareMemberFacts(
            CourtAristocraticMemberFact pLeft,
            CourtAristocraticMemberFact pRight)
        {
            int order = RolePower(pRight.Role).CompareTo(
                RolePower(pLeft.Role));
            if (order != 0) return order;
            order = MemberPower(pRight).CompareTo(MemberPower(pLeft));
            if (order != 0) return order;
            order = pLeft.ShiId.CompareTo(pRight.ShiId);
            return order != 0 ? order : string.CompareOrdinal(
                pLeft.ShiName, pRight.ShiName);
        }

        private sealed class GroupBuilder
        {
            private int _power;
            private int _members;
            private int _leaderPower = -1;
            private long _leaderActorId = -1;

            public GroupBuilder(long pShiId, string pShiName)
            {
                ShiId = pShiId;
                ShiName = pShiName ?? "";
            }

            public long ShiId { get; }
            public string ShiName { get; }

            public void Add(CourtAristocraticMemberFact pFact)
            {
                int power = MemberPower(pFact);
                _power += power;
                _members++;
                if (power > _leaderPower ||
                    power == _leaderPower &&
                    (_leaderActorId < 0 || pFact.ActorId < _leaderActorId))
                {
                    _leaderPower = power;
                    _leaderActorId = pFact.ActorId;
                }
            }

            public CourtAristocraticGroup Build()
            {
                return new CourtAristocraticGroup(ShiId, ShiName, _power,
                    _members, _leaderActorId);
            }
        }
    }
}
