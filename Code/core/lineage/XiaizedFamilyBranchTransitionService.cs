using System;
using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal sealed class XiaizedFamilyBranchTransitionPrepared
    {
        internal long NewShiId = -1L;
        internal string FamilyName = string.Empty;
        internal string ClanName = string.Empty;
        internal IReadOnlyList<long> ActorIds = Array.Empty<long>();
        internal bool HasTransition => NewShiId >= 0L;
    }

    internal static class XiaizedFamilyBranchTransitionService
    {
        internal static bool TryPrepare(Kingdom pKingdom,
            out XiaizedFamilyBranchTransitionPrepared pPrepared)
        {
            pPrepared = new XiaizedFamilyBranchTransitionPrepared();
            Actor king = pKingdom?.king;
            if (pKingdom?.data == null || pKingdom.culture == null ||
                king?.data == null || king.asset == null)
                return false;

            bool monkey = CivMonkeyPolicyRules.IsNativePolicySpecies(
                pKingdom.data.original_actor_asset, pKingdom.asset?.id,
                king.asset.id);
            bool biologicalXia = LineageService.IsXia(king);

            king.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            king.data.get(LineageKeys.SHI_ID, out long oldShiId, -1L);
            ShiBranchInfo oldBranch = oldShiId >= 0L
                ? LineageQuery.GetShiBranchInfo(oldShiId)
                : null;
            NamingProfileId oldProfile =
                AWCultureNamingTraditionRules.ParseProfile(
                    oldBranch?.naming_profile);
            if (!XiaizedFamilyBranchTransitionRules.CanTransition(oldProfile,
                    monkey, biologicalXia, valid: oldBranch != null))
                return true;

            if (lineageId < 0L || oldShiId < 0L)
            {
                if (!WesternLineageAdmissionService.TryEnsure(king,
                        pRuler: true, pHeir: false, pNoble: true,
                        pOfficial: false,
                        pSourceType: "xiaization_preparation"))
                    return false;
                king.data.get(LineageKeys.LINEAGE_ID, out lineageId, -1L);
                king.data.get(LineageKeys.SHI_ID, out oldShiId, -1L);
                oldBranch = oldShiId >= 0L
                    ? LineageQuery.GetShiBranchInfo(oldShiId)
                    : null;
                oldProfile = AWCultureNamingTraditionRules.ParseProfile(
                    oldBranch?.naming_profile);
            }
            if (lineageId < 0L || oldShiId < 0L || oldBranch == null)
                return false;

            king.data.get(LineageKeys.CHINESE_FAMILY_NAME,
                out string chineseFamily, string.Empty);
            king.data.get(AWNameDataKeys.FamilyComponent,
                out string localizedFamily, string.Empty);
            string family = XiaizedFamilyBranchTransitionRules.ResolveFamily(
                chineseFamily, localizedFamily,
                LineageNamePool.RandomSurname());
            string cityName = ResolveCityChineseName(pKingdom, king);
            string clan = XiaizedFamilyBranchTransitionRules.ResolveClan(
                cityName, family, RandomShiDifferentFrom(family));
            if (family.Length == 0 || clan.Length == 0) return false;

            var request = new XiaizedFamilyBranchTransitionRequest
            {
                FounderActorId = king.data.id,
                LineageId = lineageId,
                OldShiId = oldShiId,
                OldNamingProfile =
                    AWCultureNamingTraditionRules.SerializeProfile(oldProfile),
                FamilyName = family,
                ClanName = clan,
                OriginKingdomId = pKingdom.id,
                OriginCityId = pKingdom.capital?.data?.id ??
                               king.city?.data?.id ?? -1L,
                OriginCityChineseName = cityName,
                CreatedTime = World.world?.getCurWorldTime() ?? 0d
            };
            XiaizedFamilyBranchTransitionResult result =
                XiaizedFamilyBranchTransitionPersistence.TryCommit(
                    LineageArchiveManager.Instance?.OperatingDB, request);
            if (!result.Success)
            {
                ModClass.LogWarning("Xiaized family branch transition failed: " +
                                    result.Failure);
                return false;
            }

            pPrepared = new XiaizedFamilyBranchTransitionPrepared
            {
                NewShiId = result.NewShiId,
                FamilyName = family,
                ClanName = clan,
                ActorIds = result.MovedActorIds
            };
            return true;
        }

        internal static void Publish(
            XiaizedFamilyBranchTransitionPrepared pPrepared)
        {
            if (pPrepared == null || !pPrepared.HasTransition) return;
            foreach (long actorId in pPrepared.ActorIds)
            {
                Actor actor = World.world?.units?.get(actorId);
                if (actor?.data == null || actor.isRekt()) continue;
                actor.data.set(LineageKeys.SHI_ID, pPrepared.NewShiId);
                actor.data.set(LineageKeys.FAMILY_NAME,
                    pPrepared.FamilyName);
                actor.data.set(LineageKeys.CHINESE_FAMILY_NAME,
                    pPrepared.FamilyName);
                actor.data.set(LineageKeys.CLAN_NAME, pPrepared.ClanName);
                actor.data.set(LineageKeys.NAMING_PROFILE, "xia");
                actor.data.set(LineageKeys.WESTERN_NAMING_TRADITION,
                    string.Empty);
                actor.data.set(LineageKeys.NAME_INTEGRATED, true);
                LineageService.ApplyDisplayName(actor);
                AWLocalizedNameService.CommitChineseName(actor.data,
                    actor.data.name, "Unit", actor.data.id);
                LineageService.ArchiveActor(actor, pAlive: true);
                FamilyTreeProjectionPendingStore.IncludePrerequisite(
                    actor.data.id,
                    FamilyTreeProjectionChange.FamilyStructure);
                try { actor.clearGraphicsFully(); }
                catch { }
            }
        }

        private static string ResolveCityChineseName(Kingdom pKingdom,
            Actor pKing)
        {
            City city = pKingdom?.capital ?? pKing?.city;
            if (city?.data == null) return string.Empty;
            city.data.get(AWNameDataKeys.ChineseName, out string chinese,
                string.Empty);
            return !string.IsNullOrWhiteSpace(chinese)
                ? chinese.Trim()
                : (city.data.name ?? string.Empty).Trim();
        }

        private static string RandomShiDifferentFrom(string pFamily)
        {
            string clan = LineageNamePool.RandomShi();
            for (int i = 0; i < 8 && clan == pFamily; i++)
                clan = LineageNamePool.RandomShi();
            return clan;
        }
    }
}
