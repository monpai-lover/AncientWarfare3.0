using System;
using AncientWarfare3.content;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.naming
{
    internal readonly struct AWCultureNamingTradition
    {
        internal AWCultureNamingTradition(NamingProfileId pProfile,
            WesternNamingTradition pWesternTradition)
        {
            Profile = pProfile;
            WesternTradition = pWesternTradition;
        }

        internal NamingProfileId Profile { get; }
        internal WesternNamingTradition WesternTradition { get; }
    }

    internal static class AWCultureNamingTraditionService
    {
        internal static AWCultureNamingTradition Ensure(Culture pCulture)
        {
            if (pCulture?.data == null)
                return Empty;

            NamingProfileId naturalProfile = ResolveNaturalProfile(
                pCulture.data.creator_species_id,
                pCulture.data.original_actor_asset, valid: true);
            pCulture.data.get(LineageKeys.NAMING_PROFILE,
                out string persistedProfileId, string.Empty);
            NamingProfileId profile =
                AWCultureNamingTraditionRules.ResolveEffectiveProfile(
                    naturalProfile,
                    AWCultureNamingTraditionRules.ParseProfile(
                        persistedProfileId),
                    XiaCultureIntegrationService.IsIntegrated(pCulture),
                    XiaCultureIntegrationService.IsFullyIntegrated(pCulture));
            return Persist(pCulture, profile, null);
        }

        internal static AWCultureNamingTradition Inherit(Culture pChild,
            Culture pParent)
        {
            if (pChild?.data == null)
                return Empty;
            if (pParent?.data == null || ReferenceEquals(pChild, pParent))
                return Ensure(pChild);

            AWCultureNamingTradition parent = Ensure(pParent);
            NamingProfileId childNaturalProfile = ResolveNaturalProfile(
                pChild.data.creator_species_id,
                pChild.data.original_actor_asset, valid: true);
            NamingProfileId profile =
                AWCultureNamingTraditionRules.ResolveInheritedProfile(
                    childNaturalProfile, parent.Profile,
                    XiaCultureIntegrationService.IsIntegrated(pChild),
                    XiaCultureIntegrationService.IsFullyIntegrated(pChild));
            pChild.data.set(LineageKeys.CULTURE_PARENT_ID, pParent.getID());
            WesternNamingTradition? inherited = profile ==
                                                    NamingProfileId.Western
                ? AWCultureNamingTraditionRules.ResolveInheritedTradition(
                    parent.Profile, parent.WesternTradition, pChild.getID())
                : (WesternNamingTradition?)null;
            return Persist(pChild, profile, inherited);
        }

        internal static AWCultureNamingTradition ResolveForActor(Actor pActor)
        {
            if (pActor?.data == null || pActor.asset == null)
                return Empty;
            return ResolveForAsset(pActor.asset, ResolveActorCulture(pActor),
                pActor.getID());
        }

        internal static AWCultureNamingTradition ResolveForCulture(
            Culture pCulture)
        {
            return Ensure(pCulture);
        }

        internal static AWCultureNamingTradition ResolveForAsset(
            ActorAsset pAsset, Culture pCulture, long pStableId)
        {
            if (pAsset == null)
                return Empty;
            NamingProfileId naturalProfile = AWNamingProfileRules.Resolve(
                biologicalXia: string.Equals(pAsset.id,
                    LineageService.XIA_ASSET_ID, StringComparison.Ordinal),
                civilizedMonkey: CivMonkeyNamingRules.IsCivilizedMonkey(
                    pAsset.id),
                nativeSinitic: AWNativeSiniticSpeciesRules
                    .IsNativeSiniticSpecies(pAsset.id),
                orc: string.Equals(pAsset.id, "orc",
                    StringComparison.Ordinal),
                civilized: pAsset.civ,
                valid: true);
            AWCultureNamingTradition cultureNaming = pCulture?.data != null
                ? Ensure(pCulture)
                : Empty;
            NamingProfileId profile =
                AWCultureNamingTraditionRules.ResolveActorProfile(
                    naturalProfile, cultureNaming.Profile);
            if (profile == cultureNaming.Profile)
                return cultureNaming;
            return profile == NamingProfileId.Western
                ? new AWCultureNamingTradition(profile,
                    AWCultureNamingTraditionRules.SelectWesternTradition(
                        pStableId))
                : new AWCultureNamingTradition(profile,
                    WesternNamingTradition.Von);
        }

        internal static AWCultureNamingTradition ResolveForActorReadOnly(
            Actor pActor)
        {
            if (pActor?.data == null || pActor.asset == null)
                return Empty;
            pActor.data.get(LineageKeys.NAMING_PROFILE,
                out string actorProfileId, string.Empty);
            NamingProfileId persistedActorProfile =
                AWCultureNamingTraditionRules.ParseProfile(actorProfileId);
            NamingProfileId naturalProfile = ResolveAssetNaturalProfile(
                pActor.asset);
            NamingProfileId profile = AWCultureNamingTraditionRules
                .ResolveActorSnapshotProfile(naturalProfile,
                    persistedActorProfile, NamingProfileId.None,
                    pCreationBoundary: false);
            return new AWCultureNamingTradition(profile,
                ResolveActorTraditionReadOnly(pActor, profile));
        }

        internal static void InitializeActorProfile(Actor pActor)
        {
            if (pActor?.data == null || pActor.asset == null) return;
            pActor.data.get(LineageKeys.NAMING_PROFILE,
                out string existing, string.Empty);
            if (AWCultureNamingTraditionRules.ParseProfile(existing) !=
                NamingProfileId.None) return;
            NamingProfileId naturalProfile = ResolveAssetNaturalProfile(
                pActor.asset);
            if (AWCultureNamingTraditionRules
                    .ShouldDeferActorProfileInitialization(
                        naturalProfile, ResolveActorCulture(pActor) != null))
                return;
            AWCultureNamingTradition culture = ResolveForCultureReadOnly(
                ResolveActorCulture(pActor));
            NamingProfileId profile = AWCultureNamingTraditionRules
                .ResolveActorSnapshotProfile(naturalProfile,
                    NamingProfileId.None, culture.Profile,
                    pCreationBoundary: true);
            if (profile == NamingProfileId.None) return;
            pActor.data.set(LineageKeys.NAMING_PROFILE,
                AWCultureNamingTraditionRules.SerializeProfile(
                    profile));
        }

        private static WesternNamingTradition ResolveActorTraditionReadOnly(
            Actor pActor, NamingProfileId pProfile)
        {
            if (pProfile != NamingProfileId.Western) return WesternNamingTradition.Von;
            pActor.data.get(LineageKeys.WESTERN_NAMING_TRADITION,
                out string persisted, string.Empty);
            return AWCultureNamingTraditionRules.ResolvePersistedTradition(
                persisted, null, pActor.getID());
        }

        internal static AWCultureNamingTradition ResolveForAssetReadOnly(
            ActorAsset pAsset, Culture pCulture, long pStableId)
        {
            if (pAsset == null)
                return Empty;
            NamingProfileId naturalProfile = AWNamingProfileRules.Resolve(
                biologicalXia: string.Equals(pAsset.id,
                    LineageService.XIA_ASSET_ID, StringComparison.Ordinal),
                civilizedMonkey: CivMonkeyNamingRules.IsCivilizedMonkey(
                    pAsset.id),
                nativeSinitic: AWNativeSiniticSpeciesRules
                    .IsNativeSiniticSpecies(pAsset.id),
                orc: string.Equals(pAsset.id, "orc",
                    StringComparison.Ordinal),
                civilized: pAsset.civ,
                valid: true);
            AWCultureNamingTradition cultureNaming =
                ResolveForCultureReadOnly(pCulture);
            NamingProfileId profile =
                AWCultureNamingTraditionRules.ResolveActorProfile(
                    naturalProfile, cultureNaming.Profile);
            if (profile == cultureNaming.Profile)
                return cultureNaming;
            return profile == NamingProfileId.Western
                ? new AWCultureNamingTradition(profile,
                    AWCultureNamingTraditionRules.SelectWesternTradition(
                        pStableId))
                : new AWCultureNamingTradition(profile,
                    WesternNamingTradition.Von);
        }

        private static AWCultureNamingTradition ResolveForCultureReadOnly(
            Culture pCulture)
        {
            if (pCulture?.data == null)
                return Empty;

            NamingProfileId naturalProfile = ResolveNaturalProfile(
                pCulture.data.creator_species_id,
                pCulture.data.original_actor_asset, valid: true);
            pCulture.data.get(LineageKeys.NAMING_PROFILE,
                out string persistedProfileId, string.Empty);
            NamingProfileId profile =
                AWCultureNamingTraditionRules.ResolveEffectiveProfile(
                    naturalProfile,
                    AWCultureNamingTraditionRules.ParseProfile(
                        persistedProfileId),
                    XiaCultureIntegrationService.IsIntegrated(pCulture),
                    XiaCultureIntegrationService.IsFullyIntegrated(pCulture));
            if (profile == NamingProfileId.None)
                return Empty;

            pCulture.data.get(LineageKeys.WESTERN_NAMING_TRADITION,
                out string persistedTradition, string.Empty);
            WesternNamingTradition tradition = profile ==
                                                  NamingProfileId.Western
                ? AWCultureNamingTraditionRules.ResolvePersistedTradition(
                    persistedTradition, null, pCulture.getID())
                : WesternNamingTradition.Von;
            return new AWCultureNamingTradition(profile, tradition);
        }

        private static AWCultureNamingTradition Persist(Culture pCulture,
            NamingProfileId pProfile,
            WesternNamingTradition? pInheritedTradition)
        {
            if (pCulture?.data == null || pProfile == NamingProfileId.None)
            {
                if (pCulture?.data != null)
                {
                    pCulture.data.removeString(LineageKeys.NAMING_PROFILE);
                    pCulture.data.removeString(
                        LineageKeys.WESTERN_NAMING_TRADITION);
                }
                return Empty;
            }

            pCulture.data.set(LineageKeys.NAMING_PROFILE,
                AWCultureNamingTraditionRules.SerializeProfile(pProfile));
            if (!AWCultureNamingTraditionRules
                    .ShouldPersistWesternTradition(pProfile))
            {
                pCulture.data.removeString(
                    LineageKeys.WESTERN_NAMING_TRADITION);
                return new AWCultureNamingTradition(pProfile,
                    WesternNamingTradition.Von);
            }

            pCulture.data.get(LineageKeys.WESTERN_NAMING_TRADITION,
                out string persistedTradition, string.Empty);
            WesternNamingTradition selected = AWCultureNamingTraditionRules
                .ResolvePersistedTradition(persistedTradition,
                    pInheritedTradition, pCulture.getID());
            pCulture.data.set(LineageKeys.WESTERN_NAMING_TRADITION,
                AWCultureNamingTraditionRules.SerializeTradition(selected));
            return new AWCultureNamingTradition(pProfile, selected);
        }

        private static NamingProfileId ResolveNaturalProfile(
            string pCreatorSpeciesId, string pOriginalActorAssetId,
            bool valid)
        {
            string speciesId = !string.IsNullOrWhiteSpace(pCreatorSpeciesId)
                ? pCreatorSpeciesId
                : pOriginalActorAssetId;
            ActorAsset asset = string.IsNullOrWhiteSpace(speciesId)
                ? null
                : AssetManager.actor_library.get(speciesId);
            return AWNamingProfileRules.Resolve(
                biologicalXia: string.Equals(speciesId,
                    LineageService.XIA_ASSET_ID, StringComparison.Ordinal),
                civilizedMonkey: CivMonkeyNamingRules.IsCivilizedMonkey(
                    speciesId),
                nativeSinitic: AWNativeSiniticSpeciesRules
                    .IsNativeSiniticSpecies(speciesId),
                orc: string.Equals(speciesId, "orc",
                    StringComparison.Ordinal),
                civilized: asset != null && asset.civ,
                valid: valid);
        }

        private static NamingProfileId ResolveAssetNaturalProfile(
            ActorAsset pAsset)
        {
            if (pAsset == null) return NamingProfileId.None;
            return AWNamingProfileRules.Resolve(
                biologicalXia: string.Equals(pAsset.id,
                    LineageService.XIA_ASSET_ID, StringComparison.Ordinal),
                civilizedMonkey: CivMonkeyNamingRules.IsCivilizedMonkey(
                    pAsset.id),
                nativeSinitic: AWNativeSiniticSpeciesRules
                    .IsNativeSiniticSpecies(pAsset.id),
                orc: string.Equals(pAsset.id, "orc",
                    StringComparison.Ordinal),
                civilized: pAsset.civ,
                valid: true);
        }

        private static Culture ResolveActorCulture(Actor pActor)
        {
            if (pActor == null) return null;
            if (pActor.hasCulture()) return pActor.culture;
            foreach (Actor parent in pActor.getParents())
                if (parent != null && parent.hasCulture())
                    return parent.culture;
            return null;
        }

        private static AWCultureNamingTradition Empty =>
            new AWCultureNamingTradition(NamingProfileId.None,
                WesternNamingTradition.Von);
    }
}
