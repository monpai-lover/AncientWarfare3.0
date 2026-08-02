using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.policy
{
    public sealed class KingdomPolicyProfileMigrationState
    {
        public string profileId = string.Empty;
        public int migrationVersion;
        public string currentPolicy = string.Empty;
        public string currentTech = string.Empty;
        public string currentDecision = string.Empty;
        public string completedPolicies = string.Empty;
        public string completedTechs = string.Empty;
        public string completedDecisions = string.Empty;
        public string lockedNodes = string.Empty;
        public string obsoleteNodeIds = string.Empty;
    }

    public static class KingdomPolicyProfileMigrationRules
    {
        public const int CurrentVersion = 2;
        public const string LegacyAppeaseXiaCitiesDecisionId =
            "aw_decision_appease_xia_cities";
        public const string AppeaseForeignCitiesDecisionId =
            "aw_decision_appease_foreign_cities";

        public static string MapLegacyDecisionId(string pDecisionId)
        {
            string id = (pDecisionId ?? string.Empty).Trim();
            return string.Equals(id, LegacyAppeaseXiaCitiesDecisionId,
                    StringComparison.Ordinal)
                ? AppeaseForeignCitiesDecisionId
                : id;
        }

        public static string AppendObsoleteNodeId(string pRaw,
            string pNodeId)
        {
            var obsolete = new OrderedIdSet(pRaw);
            obsolete.Add(pNodeId);
            return obsolete.Serialize();
        }

        public static KingdomPolicyProfileMigrationState Sanitize(
            KingdomPolicyProfileMigrationState pInput,
            Func<string, bool> policyAllowed,
            Func<string, bool> techAllowed,
            Func<string, bool> decisionAllowed)
        {
            pInput ??= new KingdomPolicyProfileMigrationState();
            bool validProfile = KingdomPolicyProfileRules.TryParsePersisted(
                pInput.profileId, out _);
            Func<string, bool> policy = validProfile
                ? policyAllowed ?? (_ => false)
                : _ => false;
            Func<string, bool> tech = validProfile
                ? techAllowed ?? (_ => false)
                : _ => false;
            Func<string, bool> decision = validProfile
                ? decisionAllowed ?? (_ => false)
                : _ => false;

            var obsolete = new OrderedIdSet(pInput.obsoleteNodeIds);
            string currentPolicy = SanitizeCurrent(pInput.currentPolicy,
                policy, obsolete);
            string currentTech = SanitizeCurrent(pInput.currentTech, tech,
                obsolete);
            string currentDecision = SanitizeCurrent(pInput.currentDecision,
                decision, obsolete, MapLegacyDecisionId);
            string completedPolicies = SanitizeSet(pInput.completedPolicies,
                policy, obsolete);
            string completedTechs = SanitizeSet(pInput.completedTechs, tech,
                obsolete);
            string completedDecisions = SanitizeSet(
                pInput.completedDecisions, decision, obsolete,
                MapLegacyDecisionId);
            string lockedNodes = SanitizeSet(pInput.lockedNodes,
                id => policy(id) || tech(id) || decision(id), obsolete,
                MapLegacyDecisionId);

            return new KingdomPolicyProfileMigrationState
            {
                profileId = validProfile ? pInput.profileId : string.Empty,
                migrationVersion = CurrentVersion,
                currentPolicy = currentPolicy,
                currentTech = currentTech,
                currentDecision = currentDecision,
                completedPolicies = completedPolicies,
                completedTechs = completedTechs,
                completedDecisions = completedDecisions,
                lockedNodes = lockedNodes,
                obsoleteNodeIds = obsolete.Serialize()
            };
        }

        private static string SanitizeCurrent(string pId,
            Func<string, bool> pAllowed, OrderedIdSet pObsolete,
            Func<string, string> pMap = null)
        {
            string sourceId = (pId ?? string.Empty).Trim();
            if (sourceId.Length == 0) return string.Empty;
            string id = pMap == null ? sourceId : pMap(sourceId);
            if (!string.Equals(sourceId, id, StringComparison.Ordinal))
                pObsolete.Add(sourceId);
            if (pAllowed(id)) return id;
            pObsolete.Add(sourceId);
            return string.Empty;
        }

        private static string SanitizeSet(string pRaw,
            Func<string, bool> pAllowed, OrderedIdSet pObsolete,
            Func<string, string> pMap = null)
        {
            var active = new OrderedIdSet();
            foreach (string sourceId in Split(pRaw))
            {
                string id = pMap == null ? sourceId : pMap(sourceId);
                if (!string.Equals(sourceId, id, StringComparison.Ordinal))
                    pObsolete.Add(sourceId);
                if (pAllowed(id)) active.Add(id);
                else pObsolete.Add(sourceId);
            }
            return active.Serialize();
        }

        private static IEnumerable<string> Split(string pRaw)
        {
            return (pRaw ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => id.Length > 0);
        }

        private sealed class OrderedIdSet
        {
            private readonly List<string> _ids = new List<string>();
            private readonly HashSet<string> _known =
                new HashSet<string>(StringComparer.Ordinal);

            public OrderedIdSet()
            {
            }

            public OrderedIdSet(string pRaw)
            {
                foreach (string id in Split(pRaw)) Add(id);
            }

            public void Add(string pId)
            {
                string id = (pId ?? string.Empty).Trim();
                if (id.Length == 0 || !_known.Add(id)) return;
                _ids.Add(id);
            }

            public string Serialize()
            {
                return string.Join(";", _ids.ToArray());
            }
        }
    }
}
