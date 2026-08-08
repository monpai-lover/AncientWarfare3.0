using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameService
    {
        internal const int SchemaVersion = 1;
        private static readonly object WarningGate = new object();
        private static readonly HashSet<string> WarnedGenerators =
            new HashSet<string>(StringComparer.Ordinal);

        internal static string ProjectActor(Actor pActor)
        {
            if (pActor?.data == null) return string.Empty;
            if (TryProjectHistoricalFigure(pActor, out string historicalName))
                return historicalName;
            string generatorId = ResolveActorGenerator(pActor, MetaType.Unit);
            return EnsureIdentity(pActor.data, "Unit", generatorId, pActor.getID(),
                ResolveCultureId(pActor), (pGenerator, pParameters) =>
                {
                    AWNameParameterGetters.GetActorParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pActor, pParameters);
                     string family = ResolveActorFamily(pActor);
                     if (!string.IsNullOrEmpty(family))
                         pParameters[AWNameDataKeys.FamilyNameInTemplate] = family;
                }, (pGenerated, pSelectedName) =>
                    PersistActorGeneratedComponents(pActor, pGenerated,
                        pSelectedName), pGenerated =>
                    AWActorInitialNameRules.ResolveGeneratedName(
                    pGenerated.Name, pGenerated.Components,
                        CivMonkeyNamingRules.IsCivilizedMonkey(
                            pActor.asset?.id)));
        }

        internal static void ResetGeneratedActorIdentity(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.removeString(AWNameDataKeys.ChineseName);
            pActor.data.removeString(AWNameDataKeys.GivenName);
            pActor.data.removeString(AWNameDataKeys.FamilyComponent);
            pActor.data.removeString(LineageKeys.GIVEN_NAME);
        }

        private static bool TryProjectHistoricalFigure(Actor pActor,
            out string pCanonicalName)
        {
            pCanonicalName = string.Empty;
            if (pActor?.data == null) return false;

            bool isHistoricalFigure = false;
            try
            {
                isHistoricalFigure =
                    pActor.hasTrait(HistoricalFigureService.TRAIT_FIGURE) ||
                    pActor.hasTrait(HistoricalFigureService.TRAIT_FIRST);
            }
            catch { }
            if (!isHistoricalFigure) return false;

            int figureIndex = FigureStateStore.IndexOfActor(pActor.data.id);
            HistoricalFigureDef definition = HistoricalFigureDef.Get(figureIndex);
            if (definition != null)
            {
                pActor.data.set(LineageKeys.FAMILY_NAME,
                    definition.FamilyName);
                pActor.data.set(LineageKeys.CLAN_NAME,
                    definition.ClanName);
                pActor.data.set(LineageKeys.CHINESE_FAMILY_NAME,
                    definition.FamilyName);
                pActor.data.set(LineageKeys.GIVEN_NAME,
                    definition.GivenName);
            }

            pActor.data.get(LineageKeys.FAMILY_NAME, out string family,
                string.Empty);
            pActor.data.get(LineageKeys.GIVEN_NAME, out string given,
                string.Empty);
            if (!HistoricalFigureNameRules.ShouldProtect(
                    hasHistoricalFigureTrait: true, familyName: family,
                    givenName: given)) return false;

            pCanonicalName = HistoricalFigureNameRules.ResolveDisplayName(
                family, given, pActor.data.name);
            if (string.IsNullOrWhiteSpace(pCanonicalName)) return false;

            // Historical names are authored identities, not generated locale
            // identities. Mirror the canonical value into both slots so a
            // later language projection cannot replace it with a random name.
            pActor.data.set(AWNameDataKeys.NativeName, pCanonicalName);
            pActor.data.set(AWNameDataKeys.ChineseName, pCanonicalName);
            pActor.data.set(AWNameDataKeys.GivenName, given.Trim());
            pActor.data.set(AWNameDataKeys.FamilyComponent, family.Trim());
            pActor.data.set(AWNameDataKeys.NamingSchemaVersion, SchemaVersion);
            pActor.data.set(AWNameDataKeys.GeneratorId, "historical_figure");
            pActor.data.set(AWNameDataKeys.CultureId, ResolveCultureId(pActor));
            pActor.data.set("display_name", pCanonicalName);
            if (!string.Equals(pActor.data.name, pCanonicalName,
                    StringComparison.Ordinal))
                pActor.setName(pCanonicalName);
            return true;
        }

        internal static void ApplyCity(City pCity, Actor pFounder)
        {
            if (pCity?.data == null) return;
            string generatorId = ResolveActorGenerator(pFounder, MetaType.City);
            EnsureIdentity(pCity.data, "City", generatorId, pCity.getID(),
                ResolveCultureId(pFounder), (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetCityParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pCity, pParameters));
        }

        internal static void ApplyClan(Clan pClan, Actor pFounder)
        {
            if (pClan?.data == null) return;
            string generatorId = ResolveActorGenerator(pFounder, MetaType.Clan);
            EnsureIdentity(pClan.data, "Clan", generatorId, pClan.getID(),
                ResolveCultureId(pFounder), (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetClanParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pClan, pFounder,
                        pParameters));
        }

        internal static void ApplyKingdom(Kingdom pKingdom, Actor pFounder)
        {
            if (pKingdom?.data == null) return;
            string generatorId = ResolveActorGenerator(pFounder,
                MetaType.Kingdom);
            EnsureIdentity(pKingdom.data, "Kingdom", generatorId, pKingdom.getID(),
                ResolveCultureId(pFounder), (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetKingdomParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pKingdom,
                        pParameters));
        }

        internal static void ApplyCulture(Culture pCulture, Actor pFounder)
        {
            if (pCulture?.data == null) return;
            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForCulture(pCulture);
            string explicitGenerator =
                AWNameParameterGetters.ResolveNameTemplate(pFounder,
                    MetaType.Culture);
            string generatorId = ResolveProfileGenerator(naming,
                AWNamingObjectKind.Culture, pFounder?.asset?.id,
                explicitGenerator);
            EnsureIdentity(pCulture.data, "Culture", generatorId, pCulture.getID(),
                ResolveCultureId(pFounder), (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetCultureParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pCulture,
                        pParameters));
        }

        internal static void ApplyLanguage(Language pLanguage, Actor pFounder)
        {
            if (pLanguage?.data == null) return;
            string generatorId = ResolveActorGenerator(pFounder,
                MetaType.Language);
            EnsureIdentity(pLanguage.data, "Language", generatorId, pLanguage.getID(),
                ResolveCultureId(pFounder), (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetLanguageParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pLanguage,
                        pParameters));
        }

        internal static void ApplyReligion(Religion pReligion, Actor pFounder)
        {
            if (pReligion?.data == null) return;
            string generatorId = ResolveActorGenerator(pFounder,
                MetaType.Religion);
            EnsureIdentity(pReligion.data, "Religion", generatorId, pReligion.getID(),
                ResolveCultureId(pFounder), (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetReligionParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pReligion,
                        pParameters));
        }

        internal static void ApplySubspecies(Subspecies pSubspecies,
            ActorAsset pAsset)
        {
            if (pSubspecies?.data == null) return;
            string explicitGenerator = pAsset?.id == XiaRace.ID
                ? XiaNameSets.SubspeciesGenerator
                : "default_species";
            Culture culture = ResolveCulture(
                pSubspecies.data.name_culture_id);
            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForAsset(pAsset,
                    culture, pSubspecies.getID());
            string generatorId = ResolveProfileGenerator(naming,
                AWNamingObjectKind.Subspecies, pAsset?.id,
                explicitGenerator);
            EnsureIdentity(pSubspecies.data, "Subspecies", generatorId, pSubspecies.getID(),
                pSubspecies.data.name_culture_id,
                (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetSubspeciesParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pSubspecies,
                        pParameters));
        }

        internal static void ApplyAlliance(Alliance pAlliance)
        {
            if (pAlliance?.data == null) return;
            Kingdom founder = ResolveAllianceFounder(pAlliance);
            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForCulture(
                    founder?.culture);
            string generatorId = ResolveProfileGenerator(naming,
                AWNamingObjectKind.Alliance,
                founder?.data?.original_actor_asset, "alliance_name");
            EnsureIdentity(pAlliance.data, "Alliance", generatorId, pAlliance.getID(),
                founder?.culture?.getID() ?? -1L,
                (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetAllianceParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pAlliance,
                        pParameters));
        }

        internal static void ApplyWar(War pWar, WarTypeAsset pType)
        {
            if (pWar?.data == null) return;
            Kingdom founder = pWar.main_attacker;
            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForCulture(
                    founder?.culture);
            string generatorId = ResolveProfileGenerator(naming,
                AWNamingObjectKind.War,
                founder?.data?.original_actor_asset, pType?.name_template);
            EnsureIdentity(pWar.data, "War", generatorId, pWar.getID(),
                pWar.main_attacker?.culture?.getID() ?? -1L,
                (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetWarParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pWar, pParameters));
        }

        internal static void ApplyBook(Book pBook, BookTypeAsset pType)
        {
            if (pBook?.data == null) return;
            Culture culture = ResolveCulture(pBook.data.culture_id);
            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForCulture(culture);
            string generatorId = ResolveProfileGenerator(naming,
                AWNamingObjectKind.Book,
                culture?.data?.original_actor_asset, pType?.name_template);
            EnsureIdentity(pBook.data, "Book", generatorId, pBook.getID(),
                pBook.data.culture_id,
                (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetBookParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pBook, pParameters));
        }

        internal static void ApplyItem(Item pItem, EquipmentAsset pAsset,
            Actor pCreator)
        {
            if (pItem?.data == null || pAsset == null) return;
            string explicitGenerator = ResolveItemGenerator(pAsset);
            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForActor(pCreator);
            string generatorId = ResolveProfileGenerator(naming,
                AWNamingObjectKind.Item, pCreator?.asset?.id,
                explicitGenerator);
            EnsureIdentity(pItem.data, "Item", generatorId, pItem.getID(),
                ResolveCultureId(pCreator), (pGenerator, pParameters) =>
                    AWNameParameterGetters.GetItemParameterGetter(
                        pGenerator.ParameterGetter)?.Invoke(pItem.data, pAsset,
                        pCreator, pParameters));
        }

        internal static void CaptureNative(BaseSystemData pData)
        {
            if (pData == null) return;
            pData.get(AWNameDataKeys.NativeName, out string nativeName,
                string.Empty);
            if (!string.IsNullOrWhiteSpace(nativeName)) return;
            nativeName = (pData.name ?? string.Empty).Trim();
            if (nativeName.Length > 0)
                pData.set(AWNameDataKeys.NativeName, nativeName);
        }

        internal static bool CommitChineseName(BaseSystemData pData,
            string pChineseName, string pMetaType, long pObjectId)
        {
            if (pData == null || string.IsNullOrWhiteSpace(pChineseName) ||
                string.IsNullOrWhiteSpace(pMetaType) || pObjectId < 0L)
                return false;
            CaptureNative(pData);
            pData.set(AWNameDataKeys.ChineseName, pChineseName.Trim());
            pData.set(AWNameDataKeys.NamingSchemaVersion, SchemaVersion);
            ProjectStored(pData);
            return AWLocalizedNameMigrationService.Enqueue(pMetaType, pObjectId,
                pData);
        }

        internal static string ProjectStored(BaseSystemData pData)
        {
            if (pData == null) return string.Empty;
            if (pData.custom_name && !string.IsNullOrWhiteSpace(pData.name))
                return pData.name;
            pData.get(AWNameDataKeys.NativeName, out string nativeName,
                string.Empty);
            pData.get(AWNameDataKeys.ChineseName, out string chineseName,
                string.Empty);
            string selected = AWLocalizedNameProjectionRules.Select(
                CurrentLanguage(), nativeName, chineseName);
            if (selected.Length > 0) pData.name = selected;
            return selected;
        }

        internal static string CurrentLanguage()
        {
            return LocalizedTextManager.current_language?.id ?? string.Empty;
        }

        internal static void ClearRuntime()
        {
            lock (WarningGate) WarnedGenerators.Clear();
        }

        private static string EnsureIdentity(BaseSystemData pData,
            string pMetaType, string pGeneratorId, long pObjectId,
            long pCultureId,
            Action<AWNameGeneratorAsset, Dictionary<string, string>> pFill,
            Action<AWGeneratedName, string> pCaptureGenerated = null,
            Func<AWGeneratedName, string> pSelectGeneratedName = null)
        {
            pData.get(AWNameDataKeys.GeneratorId, out string existingGenerator,
                string.Empty);
            pData.get(AWNameDataKeys.CultureId, out long existingCulture,
                -1L);
            pData.get(AWNameDataKeys.NamingSchemaVersion, out int existingSchema,
                0);
            CaptureNative(pData);
            pData.get(AWNameDataKeys.NativeName, out string nativeName,
                string.Empty);
            pData.get(AWNameDataKeys.ChineseName, out string chineseName,
                string.Empty);
            bool generateChinese = AWNamingLanguageRules.
                ShouldGenerateChineseIdentity(CurrentLanguage(),
                    !string.IsNullOrWhiteSpace(chineseName));

            if (pData.custom_name && generateChinese)
            {
                pData.set(AWNameDataKeys.ChineseName, nativeName);
            }
            else if (generateChinese)
            {
                AWGeneratedName generated = GenerateIdentity(pGeneratorId,
                    pObjectId, pCultureId, pFill);
                chineseName = pSelectGeneratedName?.Invoke(generated) ??
                              generated.Name;
                if (!string.IsNullOrWhiteSpace(chineseName))
                {
                    pData.set(AWNameDataKeys.ChineseName,
                        chineseName.Trim());
                    pCaptureGenerated?.Invoke(generated, chineseName.Trim());
                }
            }

            pData.set(AWNameDataKeys.NamingSchemaVersion, SchemaVersion);
            pData.set(AWNameDataKeys.GeneratorId, pGeneratorId ?? string.Empty);
            pData.set(AWNameDataKeys.CultureId, pCultureId);
            CaptureStructuredComponents(pData);
            string projected = ProjectStored(pData);
            if (existingSchema != SchemaVersion || existingCulture != pCultureId ||
                !string.Equals(existingGenerator, pGeneratorId ?? string.Empty,
                    StringComparison.Ordinal))
                AWLocalizedNameMigrationService.Enqueue(pMetaType, pObjectId,
                    pData);
            return projected;
        }

        internal static bool TryGenerateIdentityComponent(BaseSystemData pData,
            string pGeneratorId, long pObjectId, long pCultureId,
            bool pChineseComponent)
        {
            if (pData == null || string.IsNullOrWhiteSpace(pGeneratorId))
                return false;
            string generated = Generate(pGeneratorId, pObjectId, pCultureId,
                null);
            if (string.IsNullOrWhiteSpace(generated)) return false;
            pData.set(pChineseComponent ? AWNameDataKeys.ChineseName :
                AWNameDataKeys.NativeName, generated.Trim());
            pData.set(AWNameDataKeys.GeneratorId, pGeneratorId);
            pData.set(AWNameDataKeys.CultureId, pCultureId);
            pData.set(AWNameDataKeys.NamingSchemaVersion, SchemaVersion);
            CaptureStructuredComponents(pData);
            return true;
        }

        private static void CaptureStructuredComponents(BaseSystemData pData)
        {
            if (pData == null) return;
            pData.get(AWNameDataKeys.GivenName, out string given,
                string.Empty);
            if (string.IsNullOrWhiteSpace(given))
            {
                pData.get(LineageKeys.GIVEN_NAME, out given, string.Empty);
                if (!string.IsNullOrWhiteSpace(given))
                    pData.set(AWNameDataKeys.GivenName, given.Trim());
            }

            pData.get(AWNameDataKeys.FamilyComponent, out string family,
                string.Empty);
            if (string.IsNullOrWhiteSpace(family))
            {
                pData.get(LineageKeys.CHINESE_FAMILY_NAME, out family,
                    string.Empty);
                if (string.IsNullOrWhiteSpace(family))
                    pData.get(LineageKeys.FAMILY_NAME, out family,
                        string.Empty);
                if (!string.IsNullOrWhiteSpace(family))
                    pData.set(AWNameDataKeys.FamilyComponent, family.Trim());
            }
        }

        private static string Generate(string pGeneratorId, long pObjectId,
            long pCultureId,
            Action<AWNameGeneratorAsset, Dictionary<string, string>> pFill)
        {
            return GenerateIdentity(pGeneratorId, pObjectId, pCultureId,
                pFill).Name;
        }

        private static AWGeneratedName GenerateIdentity(string pGeneratorId,
            long pObjectId, long pCultureId,
            Action<AWNameGeneratorAsset, Dictionary<string, string>> pFill)
        {
            if (string.IsNullOrWhiteSpace(pGeneratorId))
                return AWGeneratedName.Empty;
            AWNameGeneratorAsset generator =
                AWNameGeneratorLibrary.Get(pGeneratorId);
            if (generator == null)
            {
                WarnMissingGenerator(pGeneratorId);
                return AWGeneratedName.Empty;
            }

            try
            {
                var parameters = new Dictionary<string, string>(
                    StringComparer.Ordinal);
                pFill?.Invoke(generator, parameters);
                long seed = AWNamingSeedRules.Combine(pObjectId, pCultureId,
                    pGeneratorId, SchemaVersion);
                var context = new AWNameGenerationContext(seed, parameters,
                    AWNameParameterGetters.CreateGlobalSnapshot());
                AWGeneratedName generated = generator.GenerateIdentity(context,
                    AWWordLibraryManager.Instance);
                string name = generated.Name?.Trim();
                return IsUsableGeneratedName(name)
                    ? new AWGeneratedName(name, generated.Components)
                    : AWGeneratedName.Empty;
            }
            catch (Exception error)
            {
                WarnOnce("error:" + pGeneratorId,
                    "AW3 integrated naming failed for '" + pGeneratorId +
                    "': " + error.Message);
                return AWGeneratedName.Empty;
            }
        }

        private static void PersistActorGeneratedComponents(Actor pActor,
            AWGeneratedName pGenerated, string pSelectedName)
        {
            if (pActor?.data == null || pGenerated?.Components == null) return;

            // Actor creation stores only the generated given name. A family
            // or surname becomes durable only after AW3 admits a family
            // branch or records an inherited parent identity.
            string given = pGenerated.Components.TryGetValue("given_name",
                               out string taggedGiven) &&
                           !string.IsNullOrWhiteSpace(taggedGiven)
                ? taggedGiven.Trim()
                : (pSelectedName ?? string.Empty).Trim();
            if (given.Length == 0) return;
            pActor.data.set(AWNameDataKeys.GivenName, given);
            pActor.data.get(LineageKeys.GIVEN_NAME,
                out string lineageGiven, string.Empty);
            if (string.IsNullOrWhiteSpace(lineageGiven))
                pActor.data.set(LineageKeys.GIVEN_NAME, given);
        }

        internal static string GenerateValue(string pGeneratorId,
            long pObjectId, long pCultureId,
            Action<AWNameGeneratorAsset, Dictionary<string, string>> pFill)
        {
            return Generate(pGeneratorId, pObjectId, pCultureId, pFill);
        }

        private static string ResolveActorGenerator(Actor pActor,
            MetaType pType)
        {
            string explicitGenerator =
                AWNameParameterGetters.ResolveNameTemplate(pActor,
                pType);
            AWCultureNamingTradition naming =
                AWCultureNamingTraditionService.ResolveForActor(pActor);
            return ResolveProfileGenerator(naming, ToNamingObjectKind(pType),
                pActor?.asset?.id, explicitGenerator);
        }

        private static string ResolveProfileGenerator(
            AWCultureNamingTradition pNaming, AWNamingObjectKind pKind,
            string pSpeciesId, string pExplicitGeneratorId)
        {
            string selected =
                AWCultureNamingTraditionRules.ResolveGeneratorId(
                    pNaming.Profile, pNaming.WesternTradition, pKind,
                    pSpeciesId, pExplicitGeneratorId);
            string fallback = AWCultureNamingTraditionRules
                .ResolveFallbackGeneratorId(pNaming.Profile, pKind,
                    pSpeciesId, pExplicitGeneratorId);
            return AWCultureNamingTraditionRules.ResolveAvailableGeneratorId(
                selected, !string.IsNullOrWhiteSpace(selected) &&
                          AWNameGeneratorLibrary.Get(selected) != null,
                fallback, !string.IsNullOrWhiteSpace(fallback) &&
                          AWNameGeneratorLibrary.Get(fallback) != null);
        }

        private static AWNamingObjectKind ToNamingObjectKind(MetaType pType)
        {
            return pType == MetaType.City ? AWNamingObjectKind.City :
                pType == MetaType.Clan ? AWNamingObjectKind.Clan :
                pType == MetaType.Kingdom ? AWNamingObjectKind.Kingdom :
                pType == MetaType.Culture ? AWNamingObjectKind.Culture :
                pType == MetaType.Language ? AWNamingObjectKind.Language :
                pType == MetaType.Religion ? AWNamingObjectKind.Religion :
                AWNamingObjectKind.Actor;
        }

        private static string ResolveActorFamily(Actor pActor)
        {
            if (pActor?.data == null) return string.Empty;
            pActor.data.get(LineageKeys.CHINESE_FAMILY_NAME,
                out string chineseFamily, string.Empty);
            pActor.data.get(LineageKeys.FAMILY_NAME, out string lineageFamily,
                string.Empty);
            string family = ActorLocalizedNameBoundaryRules
                .ResolveTemplateFamily(chineseFamily, lineageFamily);
            if (!string.IsNullOrWhiteSpace(family)) return family.Trim();
            foreach (Actor parent in pActor.getParents())
            {
                if (parent?.data == null || parent.data.sex != ActorSex.Male)
                    continue;
                parent.data.get(LineageKeys.CHINESE_FAMILY_NAME,
                    out chineseFamily, string.Empty);
                parent.data.get(LineageKeys.FAMILY_NAME, out lineageFamily,
                    string.Empty);
                family = ActorLocalizedNameBoundaryRules.ResolveTemplateFamily(
                    chineseFamily, lineageFamily);
                if (!string.IsNullOrWhiteSpace(family)) return family.Trim();
            }
            return string.Empty;
        }

        private static long ResolveCultureId(Actor pActor)
        {
            return pActor?.culture?.getID() ?? -1L;
        }

        private static Culture ResolveCulture(long pCultureId)
        {
            if (pCultureId < 0L || World.world?.cultures == null)
                return null;
            try
            {
                return World.world.cultures.get(pCultureId);
            }
            catch
            {
                return null;
            }
        }

        private static Kingdom ResolveAllianceFounder(Alliance pAlliance)
        {
            if (pAlliance == null) return null;
            Kingdom fallback = null;
            foreach (Kingdom kingdom in pAlliance.kingdoms_list)
            {
                if (kingdom?.data == null) continue;
                if (fallback == null) fallback = kingdom;
                if (kingdom.getID() == pAlliance.data.founder_kingdom_id)
                    return kingdom;
            }
            return fallback;
        }

        private static string ResolveItemGenerator(ItemAsset pAsset)
        {
            if (pAsset?.name_templates == null) return string.Empty;
            foreach (string id in pAsset.name_templates)
            {
                if (AWNameGeneratorLibrary.Get(id) != null) return id;
            }
            return string.Empty;
        }

        private static bool IsUsableGeneratedName(string pName)
        {
            return !string.IsNullOrWhiteSpace(pName) &&
                   !string.Equals(pName, "NO_NAME",
                       StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(pName, "name",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void WarnMissingGenerator(string pGeneratorId)
        {
            WarnOnce("missing:" + pGeneratorId,
                "AW3 integrated naming generator is missing: " +
                pGeneratorId);
        }

        private static void WarnOnce(string pKey, string pMessage)
        {
            lock (WarningGate)
            {
                if (!WarnedGenerators.Add(pKey)) return;
            }
            ModClass.LogWarning(pMessage);
        }
    }
}
