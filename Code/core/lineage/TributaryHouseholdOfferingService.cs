using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class TributaryHouseholdOfferingService
    {
        private const int MaximumRecipientCandidates = 12;

        internal static string TryOffer(Kingdom tributary, Kingdom suzerain,
            long relationId, int tributeYear)
        {
            try
            {
                SQLiteConnection db =
                    LineageArchiveManager.Instance?.OperatingDB;
                if (db == null || tributary?.data == null ||
                    suzerain?.data == null || tributary.isRekt() ||
                    suzerain.isRekt() || relationId < 0L)
                    return "error";
                var query = new RulerHouseholdQuery(db);
                if (query.HasTributaryOffering(relationId, tributeYear))
                    return "duplicate";

                RoyalHouseholdRecipientCandidate recipient =
                    SelectRecipient(suzerain, query);
                if (recipient.ActorId < 0L) return "no_recipient";
                Actor owner = FindActor(recipient.ActorId);
                IReadOnlyList<Actor> candidates =
                    RulerHouseholdService.BuildEligibleConsortCandidates(
                        tributary);
                Actor candidate = null;
                for (int index = 0; index < candidates.Count; index++)
                    if (!RulerHouseholdService.AreRelated(owner,
                            candidates[index]))
                    {
                        candidate = candidates[index];
                        break;
                    }
                if (candidate?.data == null) return "no_candidate";

                string role = RoleCode(recipient.Role);
                bool committed = RulerHouseholdService
                    .TryCommitTributaryConsort(tributary, suzerain, owner,
                        candidate, role, relationId, tributeYear,
                        recipient.Capacity, out string reason);
                if (committed) return "offered";
                return string.Equals(reason, "duplicate",
                    StringComparison.Ordinal) ? "duplicate" :
                    string.Equals(reason, "migration_failed",
                        StringComparison.Ordinal) ? "migration_failed" :
                    "error";
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Tributary household offering failed: " +
                                    error.Message);
                return "error";
            }
        }

        private static RoyalHouseholdRecipientCandidate SelectRecipient(
            Kingdom kingdom, RulerHouseholdQuery query)
        {
            var candidates = new List<RoyalHouseholdRecipientCandidate>();
            AddCandidate(candidates, kingdom, kingdom?.king,
                RoyalHouseholdOwnerRole.King, query);
            Actor heir = HeirService.GetHeir(kingdom);
            AddCandidate(candidates, kingdom, heir,
                RoyalHouseholdOwnerRole.Heir, query);
            Actor king = kingdom?.king;
            if (king?.data != null)
            {
                IReadOnlyList<long> childIds =
                    SuccessionRelationshipIndex.GetChildIds(king.data.id);
                int acceptedPrinceCandidates = 0;
                for (int index = 0; index < childIds.Count &&
                     acceptedPrinceCandidates < MaximumRecipientCandidates;
                     index++)
                {
                    Actor prince = FindActor(childIds[index]);
                    if (prince == heir) continue;
                    if (AddCandidate(candidates, kingdom, prince,
                            RoyalHouseholdOwnerRole.Prince, query))
                        acceptedPrinceCandidates++;
                }
            }
            candidates.Sort(RoyalHouseholdRecipientRules.Compare);
            for (int index = 0; index < candidates.Count; index++)
                if (candidates[index].HasVacancy) return candidates[index];
            return default;
        }

        private static bool AddCandidate(
            List<RoyalHouseholdRecipientCandidate> candidates,
            Kingdom kingdom, Actor actor, RoyalHouseholdOwnerRole role,
            RulerHouseholdQuery query)
        {
            if (actor?.data == null || candidates.Any(candidate =>
                    candidate.ActorId == actor.data.id)) return false;
            int capacity = RoyalHouseholdRecipientRules.Capacity(role,
                RulerHouseholdService.ResolveRealmTier(kingdom));
            bool eligible = RulerHouseholdService.IsEligibleTributaryOwner(
                actor, kingdom);
            if (!eligible) return false;
            int activeConsorts = query.CountActiveConsorts(actor.data.id);
            if (activeConsorts >= capacity) return false;
            bool legitimate = true;
            actor.data.get(LineageKeys.BIRTH_LEGITIMACY, out legitimate,
                true);
            candidates.Add(new RoyalHouseholdRecipientCandidate(
                actor.data.id, role, true, activeConsorts, capacity,
                legitimate, actor.data.created_time));
            return true;
        }

        private static Actor FindActor(long actorId)
        {
            if (actorId < 0L) return null;
            try { return World.world?.units?.get(actorId); }
            catch { return null; }
        }

        private static string RoleCode(RoyalHouseholdOwnerRole role)
        {
            return role switch
            {
                RoyalHouseholdOwnerRole.King => "king",
                RoyalHouseholdOwnerRole.Heir => "heir",
                RoyalHouseholdOwnerRole.Prince => "prince",
                _ => ""
            };
        }
    }
}
