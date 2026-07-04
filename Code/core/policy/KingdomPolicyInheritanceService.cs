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

        public static void RememberSplitSource(Actor pFounder, Kingdom pSource)
        {
            if (pFounder?.data == null || pSource?.data == null || pSource.isRekt()) return;
            PendingSourceByActor[pFounder.data.id] = pSource.id;
        }

        public static void InheritForNewKingdom(Kingdom pNewKingdom, Actor pFounder)
        {
            if (pNewKingdom?.data == null || pNewKingdom.isRekt()) return;
            if (InheritedKingdoms.Contains(pNewKingdom.id)) return;
            if (!KingdomPolicyService.CanUsePolicySystem(pNewKingdom)) return;

            KingdomPolicyService.EnsureInitialized(pNewKingdom);

            Kingdom source = ResolveSource(pNewKingdom, pFounder);
            if (source == null || source == pNewKingdom || source.data == null || source.isRekt()) return;
            if (!KingdomPolicyService.CanUsePolicySystem(source)) return;

            KingdomPolicySnapshot src = KingdomPolicyService.ReadSnapshot(source);
            var dst = new KingdomPolicySnapshot
            {
                class_state = src.class_state,
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
                completed_decisions = ""
            };

            CityTechService.AdjustInheritedSnapshotFromCities(pNewKingdom, dst);
            ClampProgressToDefinition(dst, PolicyNodeKind.Social);
            ClampProgressToDefinition(dst, PolicyNodeKind.Tech);
            KingdomPolicyService.ApplySnapshot(pNewKingdom, dst, pIncludeDecision: false);
            SynchronizeInheritedNameIntegration(pNewKingdom, dst);
            InheritedKingdoms.Add(pNewKingdom.id);
            ModClass.LogInfo("[policy inheritance] " + pNewKingdom.name + " inherited policy state from " + source.name);
        }

        private static void SynchronizeInheritedNameIntegration(Kingdom pNewKingdom, KingdomPolicySnapshot pSnapshot)
        {
            if (pNewKingdom?.data == null || pSnapshot == null) return;
            if (!LineageService.IsXiaKingdom(pNewKingdom)) return;
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

        private static Kingdom ResolveSource(Kingdom pNewKingdom, Actor pFounder)
        {
            if (pFounder?.data != null && PendingSourceByActor.TryGetValue(pFounder.data.id, out long sourceId))
            {
                PendingSourceByActor.Remove(pFounder.data.id);
                Kingdom source = World.world?.kingdoms?.get(sourceId);
                if (source?.data != null && !source.isRekt()) return source;
            }

            Kingdom citySource = pFounder?.city?.kingdom;
            if (citySource?.data != null && citySource != pNewKingdom && !citySource.isRekt()) return citySource;
            return FindRegionalSource(pNewKingdom, pFounder);
        }

        private static Kingdom FindRegionalSource(Kingdom pNewKingdom, Actor pFounder)
        {
            string species = SafeSpecies(pNewKingdom);
            Culture culture = pFounder?.culture ?? pNewKingdom.culture;
            Kingdom best = null;
            float bestScore = -1f;

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom == pNewKingdom || kingdom.isRekt()) continue;
                if (!KingdomPolicyService.CanUsePolicySystem(kingdom)) continue;

                float score = 0f;
                if (!string.IsNullOrEmpty(species) && SafeSpecies(kingdom) == species) score += 100f;
                if (culture != null && kingdom.culture == culture) score += 60f;
                score += Mathf.Min(40f, kingdom.countZones() * 0.01f);
                if (score <= bestScore) continue;

                bestScore = score;
                best = kingdom;
            }

            return bestScore >= 60f ? best : null;
        }

        private static string SafeSpecies(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.getActorAsset()?.id ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void ClampProgressToDefinition(KingdomPolicySnapshot pSnapshot, PolicyNodeKind pKind)
        {
            string id = pKind == PolicyNodeKind.Tech ? pSnapshot.current_tech : pSnapshot.current_policy;
            KingdomPolicyDef def = KingdomPolicyDefs.Get(id);
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
