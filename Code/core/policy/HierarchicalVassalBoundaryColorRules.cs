using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public readonly struct HierarchyColorIdentity : IEquatable<HierarchyColorIdentity>
    {
        public HierarchyColorIdentity(
            BoundaryTier tier,
            long displayedOwnerId,
            long rootId,
            long systemId,
            long realmId,
            long cityId,
            uint rootRgba)
        {
            Tier = tier;
            DisplayedOwnerId = displayedOwnerId;
            RootId = rootId;
            SystemId = systemId;
            RealmId = realmId;
            CityId = cityId;
            RootRgba = rootRgba;
        }

        public BoundaryTier Tier { get; }
        public long DisplayedOwnerId { get; }
        public long RootId { get; }
        public long SystemId { get; }
        public long RealmId { get; }
        public long CityId { get; }
        public uint RootRgba { get; }

        public bool Equals(HierarchyColorIdentity other)
        {
            return Tier == other.Tier &&
                   DisplayedOwnerId == other.DisplayedOwnerId &&
                   RootId == other.RootId &&
                   SystemId == other.SystemId &&
                   RealmId == other.RealmId &&
                   CityId == other.CityId &&
                   RootRgba == other.RootRgba;
        }

        public override bool Equals(object obj)
        {
            return obj is HierarchyColorIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Tier;
                hash = hash * 31 + DisplayedOwnerId.GetHashCode();
                hash = hash * 31 + RootId.GetHashCode();
                hash = hash * 31 + SystemId.GetHashCode();
                hash = hash * 31 + RealmId.GetHashCode();
                hash = hash * 31 + CityId.GetHashCode();
                return hash * 31 + RootRgba.GetHashCode();
            }
        }
    }

    public readonly struct HierarchyColorEdge : IEquatable<HierarchyColorEdge>
    {
        public HierarchyColorEdge(
            BoundaryTier tier, long firstOwnerId, long secondOwnerId)
        {
            Tier = tier;
            if (firstOwnerId <= secondOwnerId)
            {
                FirstOwnerId = firstOwnerId;
                SecondOwnerId = secondOwnerId;
            }
            else
            {
                FirstOwnerId = secondOwnerId;
                SecondOwnerId = firstOwnerId;
            }
        }

        public BoundaryTier Tier { get; }
        public long FirstOwnerId { get; }
        public long SecondOwnerId { get; }
        public bool IsSelfEdge { get { return FirstOwnerId == SecondOwnerId; } }

        public bool Equals(HierarchyColorEdge other)
        {
            return Tier == other.Tier &&
                   FirstOwnerId == other.FirstOwnerId &&
                   SecondOwnerId == other.SecondOwnerId;
        }

        public override bool Equals(object obj)
        {
            return obj is HierarchyColorEdge other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Tier;
                hash = hash * 31 + FirstOwnerId.GetHashCode();
                return hash * 31 + SecondOwnerId.GetHashCode();
            }
        }
    }

    public sealed class HierarchyColorAssignment
    {
        internal HierarchyColorAssignment(
            BoundaryTier[] tiers,
            long[] ownerIds,
            uint[] colors,
            bool isValid,
            string failureReason)
        {
            _tiers = tiers ?? Array.Empty<BoundaryTier>();
            _ownerIds = ownerIds ?? Array.Empty<long>();
            _colors = colors ?? Array.Empty<uint>();
            IsValid = isValid;
            FailureReason = failureReason ?? string.Empty;
        }

        private readonly BoundaryTier[] _tiers;
        private readonly long[] _ownerIds;
        private readonly uint[] _colors;

        public bool IsValid { get; }
        public string FailureReason { get; }
        public int Count { get { return _ownerIds.Length; } }

        public bool TryGetColor(
            BoundaryTier tier, long ownerId, out uint rgba)
        {
            int minimum = 0;
            int maximum = _ownerIds.Length - 1;
            while (minimum <= maximum)
            {
                int middle = minimum + (maximum - minimum) / 2;
                int comparison = CompareKey(
                    _tiers[middle], _ownerIds[middle], tier, ownerId);
                if (comparison == 0)
                {
                    rgba = _colors[middle];
                    return true;
                }
                if (comparison < 0) minimum = middle + 1;
                else maximum = middle - 1;
            }
            rgba = 0;
            return false;
        }

        internal static int CompareKey(
            BoundaryTier firstTier, long firstOwner,
            BoundaryTier secondTier, long secondOwner)
        {
            int tier = firstTier.CompareTo(secondTier);
            return tier != 0 ? tier : firstOwner.CompareTo(secondOwner);
        }
    }

    public static class HierarchicalVassalBoundaryColorRules
    {
        public static HierarchyColorAssignment BuildCanonicalAssignment(
            IReadOnlyList<HierarchyColorIdentity> identities,
            IReadOnlyList<HierarchyColorEdge> edges)
        {
            if (identities == null)
                throw new ArgumentNullException(nameof(identities));
            if (edges == null)
                throw new ArgumentNullException(nameof(edges));

            var copiedIdentities = new HierarchyColorIdentity[identities.Count];
            for (int i = 0; i < identities.Count; i++)
                copiedIdentities[i] = identities[i];
            Array.Sort(copiedIdentities, CompareIdentity);
            var uniqueIdentities = new List<HierarchyColorIdentity>();
            for (int i = 0; i < copiedIdentities.Length; i++)
            {
                HierarchyColorIdentity identity = copiedIdentities[i];
                if (uniqueIdentities.Count == 0 ||
                    CompareIdentityKey(
                        uniqueIdentities[uniqueIdentities.Count - 1],
                        identity) != 0)
                {
                    uniqueIdentities.Add(identity);
                    continue;
                }
                if (!uniqueIdentities[uniqueIdentities.Count - 1].Equals(identity))
                {
                    return Invalid("conflicting_identity:" +
                        identity.Tier + ":" + identity.DisplayedOwnerId);
                }
            }

            var copiedEdges = new HierarchyColorEdge[edges.Count];
            for (int i = 0; i < edges.Count; i++)
                copiedEdges[i] = edges[i];
            Array.Sort(copiedEdges, CompareEdge);
            var identityKeys = new HashSet<ColorKey>();
            for (int i = 0; i < uniqueIdentities.Count; i++)
                identityKeys.Add(Key(uniqueIdentities[i]));
            var adjacency = new Dictionary<ColorKey, HashSet<ColorKey>>();
            HierarchyColorEdge? previousEdge = null;
            for (int i = 0; i < copiedEdges.Length; i++)
            {
                HierarchyColorEdge edge = copiedEdges[i];
                if (edge.IsSelfEdge ||
                    previousEdge.HasValue && previousEdge.Value.Equals(edge))
                    continue;
                previousEdge = edge;
                var first = new ColorKey(edge.Tier, edge.FirstOwnerId);
                var second = new ColorKey(edge.Tier, edge.SecondOwnerId);
                if (!identityKeys.Contains(first) || !identityKeys.Contains(second))
                    continue;
                AddNeighbor(adjacency, first, second);
                AddNeighbor(adjacency, second, first);
            }

            var assigned = new Dictionary<ColorKey, uint>();
            var tiers = new BoundaryTier[uniqueIdentities.Count];
            var owners = new long[uniqueIdentities.Count];
            var colors = new uint[uniqueIdentities.Count];
            for (int i = 0; i < uniqueIdentities.Count; i++)
            {
                HierarchyColorIdentity identity = uniqueIdentities[i];
                ColorKey key = Key(identity);
                bool found = false;
                uint color = 0;
                for (int candidate = 0; candidate < 32; candidate++)
                {
                    color = CandidateColor(identity, candidate);
                    if (!MatchesAssignedNeighbor(key, color, adjacency, assigned))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return Invalid("candidate_exhausted:" +
                        identity.Tier + ":" + identity.DisplayedOwnerId);
                assigned.Add(key, color);
                tiers[i] = identity.Tier;
                owners[i] = identity.DisplayedOwnerId;
                colors[i] = color;
            }
            return new HierarchyColorAssignment(
                tiers, owners, colors, true, string.Empty);
        }

        public static uint CandidateColor(
            HierarchyColorIdentity identity, int candidateIndex)
        {
            if (candidateIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(candidateIndex));
            if (candidateIndex == 0 &&
                identity.Tier == BoundaryTier.SuzerainSystem &&
                identity.DisplayedOwnerId == identity.SystemId)
                return identity.RootRgba;
            uint baseHash = StableHash(
                identity.SystemId, identity.DisplayedOwnerId, identity.Tier);
            uint hash = candidateIndex == 0
                ? baseHash
                : CandidateHash(IdentityHash(identity, baseHash), candidateIndex);
            int red = (int)(identity.RootRgba >> 24) & 255;
            int green = (int)(identity.RootRgba >> 16) & 255;
            int blue = (int)(identity.RootRgba >> 8) & 255;
            int alpha = (int)identity.RootRgba & 255;
            RgbToHsv(red, green, blue,
                out float hue, out float saturation, out float value);
            hue = WrapHue(hue + ((int)(hash % 41u) - 20) * 0.6f);
            saturation = Clamp01(saturation +
                ((int)((hash / 41u) % 41u) - 20) * 0.0075f);
            value = Clamp01(value +
                ((int)((hash / 1681u) % 41u) - 20) * 0.0075f);
            return HsvToRgba(hue, saturation, value, alpha);
        }

        private static HierarchyColorAssignment Invalid(string reason)
        {
            return new HierarchyColorAssignment(
                Array.Empty<BoundaryTier>(), Array.Empty<long>(),
                Array.Empty<uint>(), false, reason);
        }

        private static ColorKey Key(HierarchyColorIdentity identity)
        {
            return new ColorKey(identity.Tier, identity.DisplayedOwnerId);
        }

        private static int CompareIdentity(
            HierarchyColorIdentity first, HierarchyColorIdentity second)
        {
            int key = CompareIdentityKey(first, second);
            if (key != 0) return key;
            int root = first.RootId.CompareTo(second.RootId);
            if (root != 0) return root;
            int system = first.SystemId.CompareTo(second.SystemId);
            if (system != 0) return system;
            int realm = first.RealmId.CompareTo(second.RealmId);
            if (realm != 0) return realm;
            int city = first.CityId.CompareTo(second.CityId);
            if (city != 0) return city;
            return first.RootRgba.CompareTo(second.RootRgba);
        }

        private static int CompareIdentityKey(
            HierarchyColorIdentity first, HierarchyColorIdentity second)
        {
            return HierarchyColorAssignment.CompareKey(
                first.Tier, first.DisplayedOwnerId,
                second.Tier, second.DisplayedOwnerId);
        }

        private static int CompareEdge(
            HierarchyColorEdge first, HierarchyColorEdge second)
        {
            int tier = first.Tier.CompareTo(second.Tier);
            if (tier != 0) return tier;
            int firstOwner = first.FirstOwnerId.CompareTo(second.FirstOwnerId);
            return firstOwner != 0
                ? firstOwner
                : first.SecondOwnerId.CompareTo(second.SecondOwnerId);
        }

        private static void AddNeighbor(
            IDictionary<ColorKey, HashSet<ColorKey>> adjacency,
            ColorKey owner, ColorKey neighbor)
        {
            if (!adjacency.TryGetValue(owner, out HashSet<ColorKey> neighbors))
            {
                neighbors = new HashSet<ColorKey>();
                adjacency.Add(owner, neighbors);
            }
            neighbors.Add(neighbor);
        }

        private static bool MatchesAssignedNeighbor(
            ColorKey owner,
            uint candidate,
            IReadOnlyDictionary<ColorKey, HashSet<ColorKey>> adjacency,
            IReadOnlyDictionary<ColorKey, uint> assigned)
        {
            if (!adjacency.TryGetValue(owner, out HashSet<ColorKey> neighbors))
                return false;
            foreach (ColorKey neighbor in neighbors)
            {
                if (assigned.TryGetValue(neighbor, out uint color) &&
                    color == candidate)
                    return true;
            }
            return false;
        }

        private static uint StableHash(
            long systemId, long ownerId, BoundaryTier tier)
        {
            unchecked
            {
                ulong value = (ulong)systemId * 11400714819323198485UL;
                value ^= (ulong)ownerId + 0x9E3779B97F4A7C15UL;
                value ^= (ulong)tier * 0xBF58476D1CE4E5B9UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                return (uint)(value ^ (value >> 32));
            }
        }

        private static uint IdentityHash(
            HierarchyColorIdentity identity, uint seed)
        {
            unchecked
            {
                ulong value = seed;
                value = Mix(value, (ulong)identity.RootId);
                value = Mix(value, (ulong)identity.SystemId);
                value = Mix(value, (ulong)identity.RealmId);
                value = Mix(value, (ulong)identity.CityId);
                value = Mix(value, identity.RootRgba);
                return (uint)(value ^ (value >> 32));
            }
        }

        private static ulong Mix(ulong value, ulong fact)
        {
            unchecked
            {
                value ^= fact + 0x9E3779B97F4A7C15UL +
                         (value << 6) + (value >> 2);
                value ^= value >> 30;
                return value * 0xBF58476D1CE4E5B9UL;
            }
        }

        private static uint CandidateHash(uint stableHash, int candidateIndex)
        {
            unchecked
            {
                uint value = stableHash +
                    (uint)candidateIndex * 0x9E3779B9u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }

        private static void RgbToHsv(
            int redByte, int greenByte, int blueByte,
            out float hue, out float saturation, out float value)
        {
            float red = redByte / 255f;
            float green = greenByte / 255f;
            float blue = blueByte / 255f;
            float maximum = Math.Max(red, Math.Max(green, blue));
            float minimum = Math.Min(red, Math.Min(green, blue));
            float delta = maximum - minimum;
            value = maximum;
            saturation = maximum <= 0f ? 0f : delta / maximum;
            if (delta <= 0f)
            {
                hue = 0f;
                return;
            }
            if (maximum == red)
                hue = 60f * (((green - blue) / delta) % 6f);
            else if (maximum == green)
                hue = 60f * (((blue - red) / delta) + 2f);
            else
                hue = 60f * (((red - green) / delta) + 4f);
            hue = WrapHue(hue);
        }

        private static uint HsvToRgba(
            float hue, float saturation, float value, int alpha)
        {
            float chroma = value * saturation;
            float section = hue / 60f;
            float secondary = chroma * (1f - Math.Abs(section % 2f - 1f));
            float red = 0f;
            float green = 0f;
            float blue = 0f;
            if (section < 1f) { red = chroma; green = secondary; }
            else if (section < 2f) { red = secondary; green = chroma; }
            else if (section < 3f) { green = chroma; blue = secondary; }
            else if (section < 4f) { green = secondary; blue = chroma; }
            else if (section < 5f) { red = secondary; blue = chroma; }
            else { red = chroma; blue = secondary; }
            float match = value - chroma;
            return Pack(
                ClampColor((int)Math.Round((red + match) * 255f)),
                ClampColor((int)Math.Round((green + match) * 255f)),
                ClampColor((int)Math.Round((blue + match) * 255f)),
                alpha);
        }

        private static float WrapHue(float hue)
        {
            hue %= 360f;
            return hue < 0f ? hue + 360f : hue;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private static int ClampColor(int value)
        {
            return Math.Max(24, Math.Min(231, value));
        }

        private static uint Pack(int red, int green, int blue, int alpha)
        {
            return ((uint)red << 24) | ((uint)green << 16) |
                   ((uint)blue << 8) | (uint)alpha;
        }

        private readonly struct ColorKey : IEquatable<ColorKey>
        {
            public ColorKey(BoundaryTier tier, long ownerId)
            {
                Tier = tier;
                OwnerId = ownerId;
            }

            public BoundaryTier Tier { get; }
            public long OwnerId { get; }

            public bool Equals(ColorKey other)
            {
                return Tier == other.Tier && OwnerId == other.OwnerId;
            }

            public override bool Equals(object obj)
            {
                return obj is ColorKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Tier * 397) ^ OwnerId.GetHashCode();
                }
            }
        }
    }
}
