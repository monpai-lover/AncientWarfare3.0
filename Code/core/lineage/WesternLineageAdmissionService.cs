using System;
using System.Collections.Generic;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class WesternLineageAdmissionService
    {
        private readonly struct RelativeIdentity
        {
            internal RelativeIdentity(long pLineageId, long pShiId,
                ShiBranchInfo pBranch)
            {
                LineageId = pLineageId;
                ShiId = pShiId;
                Branch = pBranch;
            }

            internal long LineageId { get; }
            internal long ShiId { get; }
            internal ShiBranchInfo Branch { get; }
            internal bool Valid => LineageId >= 0L && ShiId >= 0L &&
                                   Branch != null;
        }

        internal static bool TryEnsure(Actor pActor, bool pRuler,
            bool pHeir, bool pNoble, bool pOfficial,
            string pSourceType = "western_admission")
        {
            if (pActor?.data == null || pActor.asset == null)
                return false;

            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForActor(pActor);
            NamingProfileId profile = naming.Profile;
            bool supported = profile == NamingProfileId.Western ||
                             profile == NamingProfileId.OrcNomadic;
            if (!supported) return false;

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            ShiBranchInfo existingBranch = shiId >= 0L
                ? LineageQuery.GetShiBranchInfo(shiId)
                : null;
            bool complete = IsCompatibleComplete(existingBranch, profile) &&
                            lineageId >= 0L;
            RelativeIdentity relative = complete
                ? default
                : FindRelative(pActor, profile);
            bool roleAdmission = WesternLineageAdmissionRules.IsRoleAdmission(
                pRuler, pHeir, pNoble, pOfficial);
            WesternLineageAdmissionAction action =
                WesternLineageAdmissionRules.Resolve(profile,
                    valid: pActor.data.id >= 0L,
                    civilized: pActor.asset.civ,
                    rekt: pActor.isRekt(),
                    canonicalMaster:
                    HistoricalSchoolDescentService.IsCanonicalMaster(pActor),
                    requiresCompleteFamily: roleAdmission,
                    hasStableLineage: lineageId >= 0L,
                    hasCompleteLineageAndShi: complete,
                    hasSameProfileRelativeCompleteSource: relative.Valid);
            if (action == WesternLineageAdmissionAction.Reject)
                return false;

            FamilyBranchIdentityProjection identity;
            if (action == WesternLineageAdmissionAction.ReuseComplete)
            {
                identity = Project(existingBranch, profile);
            }
            else if (action == WesternLineageAdmissionAction.InheritRelative)
            {
                lineageId = relative.LineageId;
                shiId = relative.ShiId;
                identity = Project(relative.Branch, profile);
            }
            else
            {
                identity = CreateIdentity(pActor, naming, shiId);
            }
            if (string.IsNullOrWhiteSpace(identity.DisplayStem))
                return false;

            string givenName = ResolveGivenName(pActor);
            string displayName = WesternFamilyIdentityRules.BuildActor(
                identity, givenName, noble: true);
            if (string.IsNullOrWhiteSpace(displayName)) return false;

            var request = new WesternLineageAdmissionCommitRequest
            {
                Action = action,
                ActorId = pActor.data.id,
                ExistingLineageId = lineageId,
                ExistingShiId = shiId,
                ParentShiId = action ==
                              WesternLineageAdmissionAction
                                  .CompletePartialBranch
                    ? shiId
                    : identity.ParentShiId,
                GivenName = givenName,
                DisplayName = displayName,
                FamilyName = identity.DisplayStem,
                ClanName = identity.DisplayStem,
                AssetId = pActor.asset.id,
                Sex = pActor.isSexMale() ? 0 : 1,
                NamingProfile = identity.PersistedNamingProfile,
                WesternNamingTradition =
                    identity.PersistedWesternNamingTradition,
                OriginCityChineseName = identity.OriginCityChineseName,
                DisplayStem = identity.DisplayStem,
                SourceType = pSourceType ?? "western_admission",
                OriginKingdomId = pActor.kingdom?.id ?? -1L,
                OriginCityId = pActor.city?.data?.id ?? -1L,
                OriginOriginalClanId = pActor.clan?.data?.id ?? -1L,
                CreatedTime = World.world?.getCurWorldTime() ?? 0d
            };
            WesternLineageAdmissionCommitResult result =
                WesternLineageAdmissionPersistence.TryCommit(
                    LineageArchiveManager.Instance?.OperatingDB, request);
            if (!result.Success) return false;

            pActor.data.set(LineageKeys.LINEAGE_ID, result.LineageId);
            pActor.data.set(LineageKeys.SHI_ID, result.ShiId);
            pActor.data.set(LineageKeys.GIVEN_NAME, givenName);
            pActor.data.set(LineageKeys.FAMILY_NAME, identity.DisplayStem);
            pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME,
                identity.DisplayStem);
            pActor.data.set(LineageKeys.CLAN_NAME, identity.DisplayStem);
            pActor.data.set(AWNameDataKeys.FamilyComponent,
                identity.DisplayStem);
            pActor.data.set(LineageKeys.NAMING_PROFILE,
                identity.PersistedNamingProfile);
            pActor.data.set(LineageKeys.WESTERN_NAMING_TRADITION,
                identity.PersistedWesternNamingTradition);
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            pActor.data.set(LineageKeys.LINEAGE_STATUS,
                LineageStatus.NOBLE);
            if (!pActor.hasTrait(LineageKeys.TRAIT_GUIZU))
                pActor.addTrait(LineageKeys.TRAIT_GUIZU);
            bool clanSynchronized = SynchronizeOriginalClan(pActor,
                identity, result.LineageId, result.ShiId, pRuler);
            if (!clanSynchronized)
                ModClass.LogWarning(
                    "Western vanilla clan synchronization failed for actor=" +
                    pActor.data.id + ", profile=" +
                    identity.PersistedNamingProfile + ".");
            AWLocalizedNameService.CommitChineseName(pActor.data,
                displayName, "Unit", pActor.data.id);
            FamilyTreeProjectionPendingStore.IncludePrerequisite(
                pActor.data.id,
                FamilyTreeProjectionChange.FamilyStructure);
            LineageService.ArchiveActor(pActor, pAlive: true);
            try { pActor.clearGraphicsFully(); }
            catch { }
            return true;
        }

        private static bool SynchronizeOriginalClan(Actor pActor,
            FamilyBranchIdentityProjection pIdentity, long pLineageId,
            long pShiId, bool pRuler)
        {
            Clan clan = pActor.clan;
            Clan familyClan = clan?.data == null
                ? FindParentClan(pActor, pLineageId, pShiId)
                : null;
            WesternOriginalClanSyncAction action =
                WesternLineageAdmissionRules.ResolveOriginalClanSync(
                    pIdentity.Profile, pRuler, clan?.data != null,
                    familyClan?.data != null);
            if (action == WesternOriginalClanSyncAction.None) return true;

            string heading = WesternFamilyIdentityRules.BuildHeading(
                pIdentity);
            if (string.IsNullOrWhiteSpace(heading)) return false;

            if (action == WesternOriginalClanSyncAction.BindFamilyClan)
            {
                clan = familyClan;
                try { pActor.setClan(clan); }
                catch { return false; }
            }
            else if (action == WesternOriginalClanSyncAction.CreateClan)
            {
                HashSet<long> clanIdsBefore = CaptureValidClanIds();
                try
                {
                    clan = World.world?.clans?.newClan(pActor,
                        pAddDefaultTraits: true);
                }
                catch (Exception exception)
                {
                    ModClass.LogWarning(
                        "Western vanilla clan creation failed for actor=" +
                        pActor.data.id + ": " + exception.Message);
                    clan = FindUniqueNewFounderClan(pActor, clanIdsBefore,
                        out int candidateCount);
                    if (clan?.data == null)
                        ModClass.LogWarning(
                            "Western vanilla clan recovery rejected for actor=" +
                            pActor.data.id + ", new founder candidates=" +
                            candidateCount + ".");
                }
                bool createdForActor = clan?.data != null &&
                    clan.data.id >= 0L &&
                    !clanIdsBefore.Contains(clan.data.id) &&
                    clan.data.founder_actor_id == pActor.data.id;
                if (!createdForActor) return false;
                if (clan?.data != null && pActor.clan != clan)
                    try { pActor.setClan(clan); }
                    catch { return false; }
            }

            if (clan?.data == null ||
                !ReferenceEquals(pActor.clan, clan)) return false;
            try
            {
                if (!string.Equals(clan.data.name, heading,
                        StringComparison.Ordinal))
                    clan.setName(heading);
            }
            catch { return false; }
            if (!string.Equals(clan.data.name, heading,
                        StringComparison.Ordinal)) return false;

            if (!pRuler) return true;

            Kingdom kingdom = pActor.kingdom;
            if (kingdom?.data == null ||
                !ReferenceEquals(pActor.kingdom?.king, pActor)) return false;
            try { pActor.kingdom?.trySetRoyalClan(); }
            catch { return false; }
            return kingdom.data.royal_clan_id == clan.data.id;
        }

        private static HashSet<long> CaptureValidClanIds()
        {
            var result = new HashSet<long>();
            ClanManager manager = World.world?.clans;
            if (manager?.list == null) return result;
            for (int i = 0; i < manager.list.Count; i++)
            {
                Clan candidate = manager.list[i];
                if (candidate?.data != null && candidate.data.id >= 0L)
                    result.Add(candidate.data.id);
            }
            return result;
        }

        private static Clan FindUniqueNewFounderClan(Actor pActor,
            HashSet<long> pClanIdsBefore, out int candidateCount)
        {
            candidateCount = 0;
            if (pActor?.data == null || pClanIdsBefore == null) return null;
            ClanManager manager = World.world?.clans;
            if (manager?.list == null) return null;

            Clan match = null;
            var matchedIds = new HashSet<long>();
            for (int i = 0; i < manager.list.Count; i++)
            {
                Clan candidate = manager.list[i];
                if (candidate?.data == null || candidate.data.id < 0L)
                    continue;
                bool sameFounder = candidate.data.founder_actor_id ==
                                   pActor.data.id;
                bool newId = !pClanIdsBefore.Contains(candidate.data.id);
                if (!newId || !sameFounder ||
                    !matchedIds.Add(candidate.data.id))
                    continue;
                candidateCount++;
                match = candidate;
            }
            return candidateCount == 1 ? match : null;
        }

        private static Clan FindParentClan(Actor pActor, long pLineageId,
            long pShiId)
        {
            long[] parentIds =
            {
                pActor.data.parent_id_1,
                pActor.data.parent_id_2
            };
            for (int i = 0; i < parentIds.Length; i++)
            {
                Actor parent = World.world?.units?.get(parentIds[i]);
                if (parent?.data == null || parent.clan?.data == null)
                    continue;
                parent.data.get(LineageKeys.LINEAGE_ID,
                    out long parentLineageId, -1L);
                parent.data.get(LineageKeys.SHI_ID,
                    out long parentShiId, -1L);
                if (parentLineageId == pLineageId &&
                    parentShiId == pShiId) return parent.clan;
            }
            // Archive-only parent ids cannot recover a runtime Clan object.
            return null;
        }

        private static FamilyBranchIdentityProjection CreateIdentity(
            Actor pActor, AWCultureNamingTradition pNaming,
            long pExistingShiId)
        {
            string origin = ResolveOriginCityChineseName(pActor);
            string rawStem = pNaming.Profile == NamingProfileId.OrcNomadic
                ? AWLocalizedNameService.GenerateValue(
                    AWOrcNomadicNamingRules.FamilyStemGeneratorId,
                    pActor.data.id, pActor.culture?.getID() ?? -1L, null)
                : AWWesternFamilyNameRules.ResolveFamilyStem(
                    pActor.data.id, pNaming.WesternTradition, origin,
                    AWWordLibraryManager.Instance.GetWords(
                        "\u4e2d\u6b27\u59d3\u6c0f"));
            if (pNaming.Profile == NamingProfileId.OrcNomadic &&
                string.IsNullOrWhiteSpace(rawStem))
                rawStem = pActor.clan?.data?.name ?? string.Empty;
            string tradition = pNaming.Profile == NamingProfileId.Western
                ? AWCultureNamingTraditionRules.SerializeTradition(
                    pNaming.WesternTradition)
                : string.Empty;
            return WesternFamilyIdentityRules.ProjectBranch(pNaming.Profile,
                tradition,
                pExistingShiId >= 0L ? pExistingShiId : -1L,
                origin, rawStem);
        }

        private static FamilyBranchIdentityProjection Project(
            ShiBranchInfo pBranch, NamingProfileId pProfile)
        {
            return WesternFamilyIdentityRules.ProjectBranch(pProfile,
                pBranch?.western_naming_tradition,
                pBranch?.parent_shi_id ?? -1L,
                pBranch?.origin_city_chinese_name,
                pBranch?.display_stem);
        }

        private static RelativeIdentity FindRelative(Actor pActor,
            NamingProfileId pProfile)
        {
            long[] parentIds =
            {
                pActor.data.parent_id_1,
                pActor.data.parent_id_2
            };
            for (int i = 0; i < parentIds.Length; i++)
            {
                long parentId = parentIds[i];
                if (parentId < 0L) continue;
                long lineageId = -1L;
                long shiId = -1L;
                Actor parent = World.world?.units?.get(parentId);
                if (parent?.data != null && !parent.isRekt())
                {
                    parent.data.get(LineageKeys.LINEAGE_ID, out lineageId,
                        -1L);
                    parent.data.get(LineageKeys.SHI_ID, out shiId, -1L);
                }
                else
                {
                    LineageArchiveReader.TryGetLineage(parentId,
                        out lineageId, out shiId, out _, out _, out _);
                }
                ShiBranchInfo branch = shiId >= 0L
                    ? LineageQuery.GetShiBranchInfo(shiId)
                    : null;
                if (lineageId >= 0L &&
                    IsCompatibleComplete(branch, pProfile))
                    return new RelativeIdentity(lineageId, shiId, branch);
            }
            return default;
        }

        private static bool IsCompatibleComplete(ShiBranchInfo pBranch,
            NamingProfileId pProfile)
        {
            return pBranch != null &&
                   AWCultureNamingTraditionRules.ParseProfile(
                       pBranch.naming_profile) == pProfile &&
                   !string.IsNullOrWhiteSpace(pBranch.display_stem);
        }

        private static string ResolveGivenName(Actor pActor)
        {
            pActor.data.get(AWNameDataKeys.GivenName, out string givenName,
                string.Empty);
            if (string.IsNullOrWhiteSpace(givenName))
                pActor.data.get(LineageKeys.GIVEN_NAME, out givenName,
                    string.Empty);
            if (string.IsNullOrWhiteSpace(givenName))
                pActor.data.get(AWNameDataKeys.ChineseName, out givenName,
                    string.Empty);
            if (string.IsNullOrWhiteSpace(givenName))
                givenName = pActor.data.name ?? string.Empty;
            return givenName.Trim();
        }

        private static string ResolveOriginCityChineseName(Actor pActor)
        {
            City city = pActor?.city;
            if (city?.data == null) return string.Empty;
            return (city.name ?? string.Empty).Trim();
        }
    }
}
