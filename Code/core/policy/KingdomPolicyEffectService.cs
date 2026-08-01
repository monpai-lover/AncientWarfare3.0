using System;
using System.Collections.Generic;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomPolicyEffectService
    {
        private sealed class CacheEntry
        {
            public long Revision;
            public KingdomPolicyProfileId Profile;
            public string GovernmentState;
            public string CompletedPolicies;
            public string CompletedTechs;
            public KingdomPolicyEffects Effects;
        }

        private static readonly Dictionary<long, CacheEntry> Cache =
            new Dictionary<long, CacheEntry>();
        private static readonly Dictionary<long, long> Revisions =
            new Dictionary<long, long>();

        private const string RoyalDirectGovernment =
            "western_royal_direct";
        private const string RitualOrderTechnology =
            "aw_west_tech_ritual_order";

        public static KingdomPolicyEffects Read(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
                return KingdomPolicyEffects.Neutral;
            long kingdomId = pKingdom.id;
            long revision = Revisions.TryGetValue(kingdomId,
                out long currentRevision)
                ? currentRevision
                : 0L;
            KingdomPolicyProfileId profile =
                KingdomPolicyService.GetPolicyProfile(pKingdom);
            pKingdom.data.get(LineageKeys.POLICY_GOVERNMENT_STATE,
                out string governmentState, "default");
            pKingdom.data.get(LineageKeys.POLICY_COMPLETED,
                out string completedPolicies, "");
            pKingdom.data.get(LineageKeys.TECH_COMPLETED,
                out string completedTechs, "");
            governmentState ??= "default";
            completedPolicies ??= "";
            completedTechs ??= "";

            if (Cache.TryGetValue(kingdomId, out CacheEntry cached) &&
                cached.Revision == revision && cached.Profile == profile &&
                string.Equals(cached.GovernmentState, governmentState,
                    StringComparison.Ordinal) &&
                string.Equals(cached.CompletedPolicies, completedPolicies,
                    StringComparison.Ordinal) &&
                string.Equals(cached.CompletedTechs, completedTechs,
                    StringComparison.Ordinal))
                return cached.Effects;

            var completed = new List<string>();
            AddCompleted(completed, completedPolicies);
            AddCompleted(completed, completedTechs);
            KingdomPolicyEffects effects = KingdomPolicyEffectRules.Resolve(
                profile, completed, governmentState);
            Cache[kingdomId] = new CacheEntry
            {
                Revision = revision,
                Profile = profile,
                GovernmentState = governmentState,
                CompletedPolicies = completedPolicies,
                CompletedTechs = completedTechs,
                Effects = effects
            };
            return effects;
        }

        public static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            long kingdomId = pKingdom.id;
            Revisions.TryGetValue(kingdomId, out long revision);
            Revisions[kingdomId] = revision == long.MaxValue
                ? 1L
                : revision + 1L;
            Cache.Remove(kingdomId);
        }

        public static void ClearRuntime()
        {
            Cache.Clear();
            Revisions.Clear();
        }

        private static void AddCompleted(ICollection<string> pTarget,
            string pRaw)
        {
            if (pTarget == null || string.IsNullOrEmpty(pRaw)) return;
            string[] ids = pRaw.Split(new[] { ';' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i]?.Trim();
                if (!string.IsNullOrEmpty(id)) pTarget.Add(id);
            }
        }

        public static bool CanConsolidateRoyalAuthority(Kingdom pKingdom)
        {
            if (!IsRoyalDirectWesternKingdom(pKingdom)) return false;
            pKingdom.data.get(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                out int authority, 0);
            return authority <
                   WesternRoyalAuthorityRules.MaximumConsolidatedAuthority;
        }

        public static bool ApplyRoyalAuthorityDecision(Kingdom pKingdom)
        {
            if (!CanConsolidateRoyalAuthority(pKingdom)) return false;
            pKingdom.data.get(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                out int current, 0);
            int next = WesternRoyalAuthorityRules.ApplyConsolidation(current);
            if (next <= current) return false;
            pKingdom.data.set(LineageKeys.WESTERN_ROYAL_AUTHORITY, next);
            return true;
        }

        public static int ReadSuccessionAuthorityBonus(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            KingdomPolicyProfileId profile =
                KingdomPolicyService.GetPolicyProfile(pKingdom);
            pKingdom.data.get(LineageKeys.POLICY_GOVERNMENT_STATE,
                out string governmentState, "default");
            bool royalDirectRuleActive = string.Equals(governmentState,
                RoyalDirectGovernment,
                System.StringComparison.Ordinal);
            pKingdom.data.get(LineageKeys.WESTERN_ROYAL_AUTHORITY,
                out int authority, 0);
            bool ritualOrderCompleted = KingdomPolicyService.IsCompleted(
                pKingdom, PolicyNodeKind.Tech, RitualOrderTechnology);
            return WesternRoyalAuthorityRules.ResolveSuccessionBonus(
                profile, ritualOrderCompleted, royalDirectRuleActive,
                authority);
        }

        private static bool IsRoyalDirectWesternKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            if (KingdomPolicyService.GetPolicyProfile(pKingdom) !=
                KingdomPolicyProfileId.WesternGeneral)
                return false;
            pKingdom.data.get(LineageKeys.POLICY_GOVERNMENT_STATE,
                out string governmentState, "default");
            return string.Equals(governmentState,
                RoyalDirectGovernment,
                System.StringComparison.Ordinal);
        }
    }
}
