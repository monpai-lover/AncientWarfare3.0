using System;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalMasterIdentityProjection
    {
        internal static bool TryApply(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            if (!MatchesRequest(pActor, pMaster, pIdentity)) return false;

            ApplyCanonicalActorFields(pActor, pMaster, pIdentity);
            Clan clan = EnsurePersonalClan(pActor);
            if (clan?.data == null || clan.data.founder_actor_id != pActor.data.id)
                return false;

            if (clan.data.chief_id != pActor.data.id) clan.setChief(pActor);
            LineageService.RenameClanByLeader(clan, pActor);
            string expectedClanName =
                HistoricalMasterIdentityRules.EnsureSingleShiSuffix(pMaster.CanonicalShiName);
            if (clan.data.name != expectedClanName)
            {
                try { clan.setName(expectedClanName); }
                catch { return false; }
            }

            bool archiveProjected =
                LineageArchiveWriter.ReplaceHistoricalMasterIdentity(pActor, pIdentity);
            if (!archiveProjected)
                ModClass.LogWarning("Historical school master archive mirror pending: " +
                                    pMaster.Id + " actor=" + pActor.data.id);

            LineageService.SyncExistingChildrenAfterLineageChange(pActor);
            return MatchesProjectedIdentity(pActor, pMaster, pIdentity, clan);
        }

        private static bool MatchesRequest(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            return pActor?.data != null && pMaster != null && pIdentity != null &&
                   pIdentity.IsValid && pIdentity.IdsFrozen &&
                   pActor.data.id == pIdentity.ActorId &&
                   pMaster.CanonicalName == pIdentity.CanonicalName &&
                   pMaster.CanonicalShiName == pIdentity.ShiName &&
                   pMaster.CanonicalGivenName == pIdentity.GivenName &&
                   pMaster.CanonicalFamilyName == pIdentity.FamilyName &&
                   pMaster.FamilyEvidence == pIdentity.FamilyEvidence;
        }

        private static void ApplyCanonicalActorFields(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            pActor.data.sex = pMaster.IsMale ? ActorSex.Male : ActorSex.Female;
            pActor.data.age_overgrowth = pMaster.SpawnAge;
            pActor.data.favorite = true;
            pActor.data.set(LineageKeys.SCHOOL_MASTER_ID, pMaster.Id);
            pActor.data.set(LineageKeys.GIVEN_NAME, pMaster.CanonicalGivenName);
            pActor.data.set("display_name", pMaster.CanonicalName);
            pActor.data.set(LineageKeys.FAMILY_NAME, pMaster.CanonicalFamilyName);
            pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME, pMaster.CanonicalFamilyName);
            pActor.data.set(LineageKeys.CLAN_NAME, pMaster.CanonicalShiName);
            pActor.data.set(LineageKeys.LINEAGE_ID, pIdentity.LineageId);
            pActor.data.set(LineageKeys.SHI_ID, pIdentity.ShiId);
            pActor.data.set(LineageKeys.FOUNDED_BRANCH_SHI_ID, -1L);
            pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.COMMON);
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, 99);
            pActor.data.set(LineageKeys.NAME_INTEGRATED, true);
            pActor.data.set("aw_school_master_stewardship", pMaster.Abilities.Stewardship);
            pActor.data.set("aw_school_master_diplomacy", pMaster.Abilities.Diplomacy);
            pActor.data.set("aw_school_master_warfare", pMaster.Abilities.Warfare);
            pActor.data.set("aw_school_master_intelligence", pMaster.Abilities.Intelligence);
            pActor.setName(pMaster.CanonicalName);
            if (!pActor.hasTrait(HistoricalSchoolContent.MasterTraitId))
                pActor.addTrait(HistoricalSchoolContent.MasterTraitId);
            pActor.setStatsDirty();
            pActor.updateStats();
            pActor.setHealth(pActor.getMaxHealth());
            try { pActor.clearGraphicsFully(); } catch { }
        }

        private static Clan EnsurePersonalClan(Actor pActor)
        {
            Clan current = pActor.clan;
            if (current?.data != null && current.data.founder_actor_id == pActor.data.id)
                return current;

            Clan created = World.world?.clans?.newClan(pActor, pAddDefaultTraits: true);
            return created?.data != null && ReferenceEquals(pActor.clan, created)
                ? created
                : null;
        }

        private static bool MatchesProjectedIdentity(Actor pActor,
            HistoricalSchoolMasterDefinition pMaster,
            HistoricalMasterLineageCommitIdentity pIdentity, Clan pClan)
        {
            pActor.data.get(LineageKeys.SCHOOL_MASTER_ID, out string masterId, "");
            pActor.data.get(LineageKeys.GIVEN_NAME, out string givenName, "");
            pActor.data.get("display_name", out string displayName, "");
            pActor.data.get(LineageKeys.FAMILY_NAME, out string familyName, "");
            pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME, out string chineseFamilyName, "");
            pActor.data.get(LineageKeys.CLAN_NAME, out string shiName, "");
            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            string expectedClanName =
                HistoricalMasterIdentityRules.EnsureSingleShiSuffix(pMaster.CanonicalShiName);
            return masterId == pMaster.Id && givenName == pMaster.CanonicalGivenName &&
                   displayName == pMaster.CanonicalName &&
                   familyName == pMaster.CanonicalFamilyName &&
                   chineseFamilyName == pMaster.CanonicalFamilyName &&
                   pMaster.FamilyEvidence == pIdentity.FamilyEvidence &&
                   shiName == pMaster.CanonicalShiName &&
                   lineageId == pIdentity.LineageId && shiId == pIdentity.ShiId &&
                   pActor.data.name == pMaster.CanonicalName &&
                   pClan?.data?.founder_actor_id == pActor.data.id &&
                   pClan.data.chief_id == pActor.data.id &&
                   pClan.data.name == expectedClanName &&
                   pActor.hasTrait(HistoricalSchoolContent.MasterTraitId);
        }
    }
}
