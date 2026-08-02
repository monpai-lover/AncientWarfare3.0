using System.Collections.Generic;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomPolicyInheritanceService
    {
        private const float TECH_PROGRESS_FACTOR = 0.75f;
        private const float POLICY_PROGRESS_FACTOR = 0.60f;
        private const float POINT_FACTOR = 0.35f;
        private const float POINT_CAP = 160f;

        private static readonly Dictionary<long, long> PendingSourceByActor = new Dictionary<long, long>();
        private static readonly HashSet<long> InheritedKingdoms = new HashSet<long>();

        public static void ClearRuntime()
        {
            PendingSourceByActor.Clear();
            InheritedKingdoms.Clear();
        }

        public static void RememberSplitSource(Actor pFounder,
            Kingdom pSource, bool pRebellion, bool pFellApart)
        {
            if (pFounder?.data == null) return;
            PendingSourceByActor.Remove(pFounder.data.id);
            if (!KingdomPolicySplitInheritanceRules.ShouldCaptureSplitSource(
                    pRebellion, pFellApart,
                    KingdomIdentityContinuityService.IsCreatingRestoration,
                    pFounderValid: true,
                    pSourceValid: pSource?.data != null,
                    pSourceAlive: IsLivingSource(pSource))) return;
            PendingSourceByActor[pFounder.data.id] = pSource.id;
        }

        public static void InheritForNewKingdom(Kingdom pNewKingdom, Actor pFounder)
        {
            if (KingdomIdentityContinuityService.IsCreatingRestoration)
            {
                if (pFounder?.data != null) PendingSourceByActor.Remove(pFounder.data.id);
                return;
            }
            if (pNewKingdom?.data == null || pNewKingdom.isRekt()) return;
            if (InheritedKingdoms.Contains(pNewKingdom.id)) return;

            Kingdom source = ResolveSource(pNewKingdom, pFounder);
            KingdomPolicyProfileId childProfile =
                KingdomPolicyProfileService.Resolve(pNewKingdom);
            bool childHasPolicyProfile = KingdomPolicyProfileRules.
                IsResolvableKingdomProfile(childProfile);
            if (!KingdomPolicySplitInheritanceRules.ShouldInheritFromSplit(
                    pHasCapturedSource: source != null,
                    pNewKingdomValid: pNewKingdom != source,
                    pSourceValid: source?.data != null,
                    pSourceAlive: IsLivingSource(source),
                    pChildHasPolicyProfile: childHasPolicyProfile)) return;

            if (!XiaizationService.InheritForSplit(pNewKingdom, source))
                return;
            InheritedKingdoms.Add(pNewKingdom.id);

            if (!KingdomPolicyService.CanUsePolicySystem(pNewKingdom)) return;
            if (!KingdomPolicyService.CanUsePolicySystem(source)) return;

            KingdomPolicyService.EnsureInitialized(pNewKingdom);

            KingdomPolicySnapshot src = KingdomPolicyService.ReadSnapshot(source);
            string childProfileId = KingdomPolicyProfileRules.ToPersistedId(
                KingdomPolicyService.GetPolicyProfile(pNewKingdom));
            string sourceProfileId = KingdomPolicyProfileRules.ToPersistedId(
                KingdomPolicyService.GetPolicyProfile(source));
            var dst = new KingdomPolicySnapshot
            {
                profile_id = childProfileId,
                government_state = KingdomPolicySplitInheritanceRules.
                    ResolveInheritedGovernmentState(childProfileId,
                        src.government_state),
                royal_authority = KingdomPolicySplitInheritanceRules.
                    ResolveInheritedRoyalAuthority(childProfileId,
                        sourceProfileId, src.royal_authority,
                        WesternRoyalAuthorityRules.
                            MaximumConsolidatedAuthority),
                migration_version =
                    KingdomPolicyProfileMigrationRules.CurrentVersion,
                obsolete_node_ids = src.obsolete_node_ids,
                class_state = KingdomPolicyInheritanceRules.SanitizeClassStateForNewKingdom(
                    src.class_state, KingdomPolicyDefs.ClassDefault),
                army_state = src.army_state,
                name_state = src.name_state,
                enfeoffment_state = src.enfeoffment_state,
                policy_points = Mathf.Min(POINT_CAP, src.policy_points * POINT_FACTOR),
                tech_points = Mathf.Min(POINT_CAP, src.tech_points * POINT_FACTOR),
                current_policy = src.current_policy,
                policy_progress = src.policy_progress * POLICY_PROGRESS_FACTOR,
                current_tech = src.current_tech,
                tech_progress = src.tech_progress * TECH_PROGRESS_FACTOR,
                completed_policies = src.completed_policies,
                completed_techs = src.completed_techs,
                current_decision = "",
                decision_progress = 0f,
                completed_decisions = "",
                locked_nodes = ""
            };

            CityTechService.AdjustInheritedSnapshotFromCities(pNewKingdom, dst);
            ClampProgressToDefinition(pNewKingdom, dst,
                PolicyNodeKind.Social);
            ClampProgressToDefinition(pNewKingdom, dst,
                PolicyNodeKind.Tech);
            KingdomPolicyService.ApplySnapshot(pNewKingdom, dst, pIncludeDecision: false);
            SynchronizeInheritedNameIntegration(pNewKingdom, dst);
            ModClass.LogInfo("[policy inheritance] " + pNewKingdom.name + " inherited policy state from " + source.name);
        }

        public static void PrepareForIdentityRestoration(long pKingdomId, long pFounderActorId)
        {
            if (pKingdomId >= 0) InheritedKingdoms.Remove(pKingdomId);
            if (pFounderActorId >= 0) PendingSourceByActor.Remove(pFounderActorId);
        }

        private static void SynchronizeInheritedNameIntegration(Kingdom pNewKingdom, KingdomPolicySnapshot pSnapshot)
        {
            if (pNewKingdom?.data == null || pSnapshot == null) return;
            if (!XiaizationService.UsesXiaizedInstitutionSystem(
                    pNewKingdom)) return;
            if (LineageService.IsKingdomIntegrated(pNewKingdom)) return;

            bool inheritedCompletedIntegration =
                ContainsCompleted(pSnapshot.completed_policies, "aw_policy_name_integration") ||
                pSnapshot.name_state == KingdomPolicyDefs.NameIntegration;
            if (!inheritedCompletedIntegration) return;

            LineageService.ApplyNameIntegration(pNewKingdom);
        }

        private static bool ContainsCompleted(string pRaw, string pId)
        {
            if (string.IsNullOrEmpty(pRaw) || string.IsNullOrEmpty(pId)) return false;
            string[] parts = pRaw.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
                if (part == pId) return true;
            return false;
        }

        private static Kingdom ResolveSource(Kingdom pNewKingdom,
            Actor pFounder)
        {
            if (pFounder?.data != null && PendingSourceByActor.TryGetValue(pFounder.data.id, out long sourceId))
            {
                PendingSourceByActor.Remove(pFounder.data.id);
                Kingdom source = World.world?.kingdoms?.get(sourceId);
                if (source?.data != null && source != pNewKingdom)
                    return source;
            }
            return null;
        }

        private static bool IsLivingSource(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.countCities() > 0;
        }

        private static void ClampProgressToDefinition(Kingdom pKingdom,
            KingdomPolicySnapshot pSnapshot, PolicyNodeKind pKind)
        {
            string id = pKind == PolicyNodeKind.Tech ? pSnapshot.current_tech : pSnapshot.current_policy;
            KingdomPolicyDef def = KingdomPolicyService.GetDefinition(
                pKingdom, id);
            if (def == null)
            {
                if (pKind == PolicyNodeKind.Tech)
                {
                    pSnapshot.current_tech = "";
                    pSnapshot.tech_progress = 0f;
                }
                else
                {
                    pSnapshot.current_policy = "";
                    pSnapshot.policy_progress = 0f;
                }

                return;
            }

            float max = Mathf.Max(0f, def.Cost - 0.01f);
            if (pKind == PolicyNodeKind.Tech)
                pSnapshot.tech_progress = Mathf.Clamp(pSnapshot.tech_progress, 0f, max);
            else
                pSnapshot.policy_progress = Mathf.Clamp(pSnapshot.policy_progress, 0f, max);
        }
    }
}
