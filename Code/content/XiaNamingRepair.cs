using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.content
{
    internal static class XiaNamingRepair
    {
        private static readonly string[] LanguageMarkers =
        {
            "夏", "华", "雅言", "中原", "河洛", "九州", "王畿", "礼乐", "邦国"
        };

        private static readonly string[] CultureMarkers =
        {
            "诸夏", "华夏", "中原", "河洛", "九州", "礼乐", "青铜", "文化", "雅风", "礼制"
        };

        private static readonly string[] ReligionMarkers =
        {
            "社稷", "宗庙", "礼乐", "华夏", "诸夏", "天命", "王畿", "河洛", "九州", "先王",
            "祖祀", "祀典", "王礼"
        };

        internal static bool TryRenameKingdom(Kingdom pKingdom, Actor pActor, bool pForce)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            if (IsCivilizedMonkeyKingdom(pKingdom, pActor)) return false;
            if (!IsXiaKingdom(pKingdom, pActor)) return false;
            if (!pForce && XiaPreQinKingdomNameRules.IsKnown(pKingdom.data.name)) return false;

            string name = GenerateKingdomName(pKingdom);
            if (XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return false;

            return AWLocalizedNameService.CommitChineseName(
                pKingdom.data, name,
                "Kingdom", pKingdom.getID());
        }

        internal static bool TryApplyFullyXiaizedKingdomName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            if (IsCivilizedMonkeyKingdom(pKingdom)) return false;
            pKingdom.data.get(LineageKeys.XIA_FULL_NAME_APPLIED, out bool applied, false);
            bool originalXia = pKingdom.data.original_actor_asset == XiaRace.ID ||
                               pKingdom.asset?.id == XiaRace.ID;
            if (!XiaizedKingdomNamingRules.ShouldApply(originalXia,
                    XiaizationService.GetLevel(pKingdom), XiaizationService.LevelXiaizedDynasty, applied))
                return false;
            string oldName = pKingdom.name ?? pKingdom.data.name ?? "";
            string preferredName = GenerateKingdomName(pKingdom);
            if (XiaNameRepairRules.IsInvalidGeneratedMetaName(preferredName))
                return false;
            AWLocalizedNameService.CaptureNative(pKingdom.data);
            if (!TryApplyFullyXiaizedStateName(pKingdom, preferredName))
                return false;
            string committedName = pKingdom.name ?? pKingdom.data.name ?? "";
            if (!AWLocalizedNameService.CommitChineseName(pKingdom.data,
                    committedName, "Kingdom", pKingdom.getID()))
                return false;
            pKingdom.data.set(LineageKeys.XIA_FULL_NAME_APPLIED, true);
            string newName = pKingdom.name ?? pKingdom.data.name ?? "";
            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
                KingdomRenameSyncService.OnKingdomNameChanged(pKingdom,
                    oldName, newName, pTrack: true);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
            return true;
        }

        private static bool TryApplyFullyXiaizedStateName(Kingdom pKingdom,
            string pPreferredName)
        {
            Actor ruler = pKingdom.king;
            if (ruler?.data != null)
            {
                ruler.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
                if (shiId >= 0)
                {
                    ShiBranchInfo branch = LineageQuery.GetShiBranchInfo(shiId);
                    StateNameCommitResult committed =
                        StateNameService.EnsureBoundStateName(
                            pKingdom, ruler, shiId,
                            DynastyRecordWriter.GetCurrentDynastyId(pKingdom.id),
                            branch?.origin_kingdom_id ?? pKingdom.id,
                            pPreferredName);
                    return committed.Success &&
                           StateNameService.ProjectCommittedStateName(
                               pKingdom, committed);
                }
            }

            pKingdom.setName(pPreferredName, pTrack: false);
            return string.Equals(pKingdom.name, pPreferredName,
                StringComparison.Ordinal);
        }

        internal static bool TryRenameReligion(Religion pReligion, Actor pActor, bool pForce)
        {
            if (!IsXiaReligion(pReligion, pActor)) return false;
            if (!pForce && IsXiaReligionName(pReligion?.data?.name)) return false;

            string name = GenerateReligionName(pReligion);
            if (XiaNameRepairRules.IsInvalidXiaReligionName(name)) return false;

            pReligion.setName(name, pTrack: false);
            return true;
        }

        internal static bool TryRenameLanguage(Language pLanguage, Actor pActor, bool pForce)
        {
            if (!IsXiaLanguage(pLanguage, pActor)) return false;
            if (!pForce && IsXiaLanguageName(pLanguage?.data?.name)) return false;

            string name = GenerateLanguageName(pLanguage);
            if (XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return false;

            pLanguage.setName(name, pTrack: false);
            return true;
        }

        internal static bool TryRenameSubspecies(Subspecies pSubspecies, ActorAsset pAsset, bool pForce)
        {
            if (!IsXiaSubspecies(pSubspecies, pAsset)) return false;
            if (!pForce && IsXiaSubspeciesName(pSubspecies?.data?.name)) return false;

            string name = GenerateSubspeciesName(pSubspecies);
            if (XiaNameRepairRules.IsInvalidXiaSubspeciesName(name)) return false;

            pSubspecies.setName(name, pTrack: false);
            return true;
        }

        internal static bool TryRenameCulture(Culture pCulture, Actor pActor, bool pForce)
        {
            if (!IsXiaCulture(pCulture, pActor)) return false;
            string originName = GenerateOriginCultureName(pCulture);
            if (!pForce && !string.IsNullOrEmpty(originName) && pCulture?.data?.name == originName) return false;
            if (!pForce && string.IsNullOrEmpty(originName) && IsXiaCultureName(pCulture?.data?.name)) return false;

            string name = GenerateCultureName(pCulture);
            if (XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return false;

            pCulture.setName(name, pTrack: false);
            return true;
        }

        private static bool IsXiaKingdom(Kingdom pKingdom, Actor pActor)
        {
            if (pActor?.asset?.id == XiaRace.ID) return true;
            if (pKingdom?.data == null) return false;
            return pKingdom.data.original_actor_asset == XiaRace.ID ||
                   LineageService.IsXiaKingdom(pKingdom) ||
                   XiaizationService.GetLevel(pKingdom) >= XiaizationService.LevelXiaizedDynasty;
        }

        private static bool IsCivilizedMonkeyKingdom(Kingdom pKingdom,
            Actor pActor = null)
        {
            if (CivMonkeyNamingRules.IsCivilizedMonkey(pActor?.asset?.id))
                return true;
            if (pKingdom?.data == null) return false;
            if (CivMonkeyNamingRules.IsCivilizedMonkey(
                    pKingdom.data.original_actor_asset) ||
                CivMonkeyNamingRules.IsCivilizedMonkey(pKingdom.asset?.id))
                return true;
            try
            {
                return CivMonkeyNamingRules.IsCivilizedMonkey(
                    pKingdom.getActorAsset()?.id);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsXiaReligion(Religion pReligion, Actor pActor)
        {
            if (pActor?.asset?.id == XiaRace.ID) return true;
            return pReligion?.data?.creator_species_id == XiaRace.ID;
        }

        private static bool IsXiaLanguage(Language pLanguage, Actor pActor)
        {
            if (pActor?.asset?.id == XiaRace.ID) return true;
            return pLanguage?.data?.creator_species_id == XiaRace.ID;
        }

        private static bool IsXiaSubspecies(Subspecies pSubspecies, ActorAsset pAsset)
        {
            if (pAsset?.id == XiaRace.ID) return true;
            return pSubspecies != null && pSubspecies.isSpecies(XiaRace.ID);
        }

        private static bool IsXiaCulture(Culture pCulture, Actor pActor)
        {
            if (pActor?.asset?.id == XiaRace.ID) return true;
            return pCulture?.data?.creator_species_id == XiaRace.ID ||
                   pCulture?.data?.original_actor_asset == XiaRace.ID;
        }

        private static bool IsXiaLanguageName(string pName)
        {
            if (string.IsNullOrEmpty(pName)) return false;

            foreach (string marker in LanguageMarkers)
            {
                if (pName.IndexOf(marker, StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        private static bool IsXiaReligionName(string pName)
        {
            if (XiaNameRepairRules.IsInvalidXiaReligionName(pName)) return false;

            foreach (string marker in ReligionMarkers)
            {
                if (pName.IndexOf(marker, StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        private static bool IsXiaSubspeciesName(string pName)
        {
            if (string.IsNullOrEmpty(pName)) return false;
            if (IsBareXiaSubspeciesName(pName)) return false;
            return pName.IndexOf("夏人", StringComparison.Ordinal) >= 0 ||
                   pName.IndexOf("华夏", StringComparison.Ordinal) >= 0 ||
                   pName == "夏";
        }

        private static bool IsXiaCultureName(string pName)
        {
            if (IsInvalidGeneratedName(pName)) return false;

            foreach (string marker in CultureMarkers)
            {
                if (pName.IndexOf(marker, StringComparison.Ordinal) >= 0) return true;
            }

            return false;
        }

        private static bool IsInvalidGeneratedName(string pName)
        {
            return XiaNameRepairRules.IsInvalidGeneratedMetaName(pName);
        }

        private static string GenerateKingdomName(Kingdom pKingdom)
        {
            string chineseName = GenerateIntegratedName(
                XiaNameSets.KingdomGenerator, pKingdom?.getID() ?? 0L,
                pKingdom?.data?.name_culture_id ?? -1L, p =>
            {
                AWNameGeneratorAsset generator = AWNameGeneratorLibrary.Get(
                    XiaNameSets.KingdomGenerator);
                AWNameParameterGetters.GetKingdomParameterGetter(
                    generator.ParameterGetter)(pKingdom, p);
            });
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(chineseName)) return chineseName;

            string name = GenerateVanillaName(XiaNameSets.KingdomGenerator, pKingdom?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return name;

            return XiaFallbackNameRules.LocalKingdomName(pKingdom?.getID() ?? 0L);
        }

        private static string GenerateLanguageName(Language pLanguage)
        {
            string chineseName = GenerateIntegratedName(
                XiaNameSets.LanguageGenerator, pLanguage?.getID() ?? 0L,
                pLanguage?.data?.name_culture_id ?? -1L, p =>
            {
                AWNameGeneratorAsset generator = AWNameGeneratorLibrary.Get(
                    XiaNameSets.LanguageGenerator);
                AWNameParameterGetters.GetLanguageParameterGetter(
                    generator.ParameterGetter)(pLanguage, p);
            });
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(chineseName)) return chineseName;

            string name = GenerateVanillaName(XiaNameSets.LanguageGenerator, pLanguage?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return name;

            return XiaFallbackNameRules.LocalLanguageName(pLanguage?.getID() ?? 0L);
        }

        private static string GenerateReligionName(Religion pReligion)
        {
            string chineseName = GenerateIntegratedName(
                XiaNameSets.ReligionGenerator, pReligion?.getID() ?? 0L,
                pReligion?.data?.name_culture_id ?? -1L, p =>
            {
                AWNameGeneratorAsset generator = AWNameGeneratorLibrary.Get(
                    XiaNameSets.ReligionGenerator);
                AWNameParameterGetters.GetReligionParameterGetter(
                    generator.ParameterGetter)(pReligion, p);
            });
            if (!XiaNameRepairRules.IsInvalidXiaReligionName(chineseName)) return chineseName;

            string name = GenerateVanillaName(XiaNameSets.ReligionGenerator, pReligion?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidXiaReligionName(name)) return name;

            return XiaFallbackNameRules.LocalReligionName(pReligion?.getID() ?? 0L);
        }

        private static string GenerateCultureName(Culture pCulture)
        {
            string originName = GenerateOriginCultureName(pCulture);
            if (!string.IsNullOrEmpty(originName)) return originName;

            string chineseName = GenerateIntegratedName(
                XiaNameSets.CultureGenerator, pCulture?.getID() ?? 0L,
                pCulture?.data?.name_culture_id ?? -1L, p =>
            {
                AWNameGeneratorAsset generator = AWNameGeneratorLibrary.Get(
                    XiaNameSets.CultureGenerator);
                AWNameParameterGetters.GetCultureParameterGetter(
                    generator.ParameterGetter)(pCulture, p);
            });
            if (!IsInvalidGeneratedName(chineseName)) return chineseName;

            string name = GenerateVanillaName(XiaNameSets.CultureGenerator, pCulture?.getID() ?? 0L);
            if (!IsInvalidGeneratedName(name)) return name;

            return XiaFallbackNameRules.LocalCultureName(pCulture?.getID() ?? 0L);
        }

        private static string GenerateOriginCultureName(Culture pCulture)
        {
            string origin = CleanOriginName(pCulture?.data?.creator_city_name);
            if (string.IsNullOrEmpty(origin) && pCulture?.data != null && pCulture.data.creator_city_id >= 0)
            {
                try
                {
                    City city = World.world?.cities?.get(pCulture.data.creator_city_id);
                    origin = CleanOriginName(city?.data?.name);
                }
                catch { origin = ""; }
            }

            if (string.IsNullOrEmpty(origin))
                origin = CleanOriginName(pCulture?.data?.creator_kingdom_name);
            if (string.IsNullOrEmpty(origin))
                origin = CleanOriginName(pCulture?.data?.creator_clan_name);

            if (string.IsNullOrEmpty(origin)) return "";
            return origin.EndsWith("文化", StringComparison.Ordinal) ? origin : origin + "文化";
        }

        private static string CleanOriginName(string pName)
        {
            if (IsInvalidGeneratedName(pName)) return "";
            string trimmed = pName.Trim();
            return trimmed.Replace("#", "").Replace("$", "");
        }

        private static string GenerateSubspeciesName(Subspecies pSubspecies)
        {
            string chineseName = GenerateIntegratedName(
                XiaNameSets.SubspeciesGenerator, pSubspecies?.getID() ?? 0L,
                pSubspecies?.data?.name_culture_id ?? -1L, p =>
            {
                AWNameGeneratorAsset generator = AWNameGeneratorLibrary.Get(
                    XiaNameSets.SubspeciesGenerator);
                AWNameParameterGetters.GetSubspeciesParameterGetter(
                    generator.ParameterGetter)(pSubspecies, p);
            });
            if (IsUsefulSubspeciesName(chineseName)) return chineseName;

            string name = GenerateVanillaName(XiaNameSets.SubspeciesGenerator, pSubspecies?.getID() ?? 0L);
            if (IsUsefulSubspeciesName(name)) return name;

            return XiaFallbackNameRules.LocalSubspeciesName(pSubspecies?.getID() ?? 0L);
        }

        private static bool IsUsefulSubspeciesName(string pName)
        {
            return !XiaNameRepairRules.IsInvalidXiaSubspeciesName(pName);
        }

        private static bool IsBareXiaSubspeciesName(string pName)
        {
            if (string.IsNullOrEmpty(pName)) return true;
            return XiaNameRepairRules.IsInvalidXiaSubspeciesName(pName);
        }

        private static string GenerateIntegratedName(string pGeneratorId,
            long pObjectId, long pCultureId,
            Action<Dictionary<string, string>> pFillParameters)
        {
            try
            {
                AWNameGeneratorAsset generator =
                    AWNameGeneratorLibrary.Get(pGeneratorId);
                if (generator == null) return null;

                var parameters = new Dictionary<string, string>(
                    StringComparer.Ordinal);
                pFillParameters?.Invoke(parameters);
                long seed = AWNamingSeedRules.Combine(pObjectId, pCultureId,
                    pGeneratorId, 1);
                var context = new AWNameGenerationContext(seed, parameters,
                    AWNameParameterGetters.CreateGlobalSnapshot());
                string name = generator.GenerateName(context,
                    AWWordLibraryManager.Instance);
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Xia integrated naming failed: " +
                                    pGeneratorId + " - " + error.Message);
                return null;
            }
        }

        internal static string GenerateAllianceName(Alliance pAlliance)
        {
            long id = pAlliance?.getID() ?? 0L;
            string chineseName = GenerateIntegratedName(
                XiaNameSets.AllianceGenerator, id, -1L, p =>
            {
                AWNameGeneratorAsset generator = AWNameGeneratorLibrary.Get(
                    XiaNameSets.AllianceGenerator);
                AWNameParameterGetters.GetAllianceParameterGetter(
                    generator.ParameterGetter)(pAlliance, p);
            });
            if (IsUsefulAllianceName(chineseName))
            {
                chineseName = ResolveUniqueAllianceName(pAlliance, chineseName);
                ModClass.LogInfo("[Xia alliance naming] route=integrated alliance=" + id +
                                 " name=" + chineseName);
                return chineseName;
            }

            string name = GenerateVanillaName(XiaNameSets.AllianceGenerator, id);
            if (IsUsefulAllianceName(name))
            {
                name = ResolveUniqueAllianceName(pAlliance, name);
                ModClass.LogInfo("[Xia alliance naming] route=vanilla-fallback alliance=" + id +
                                 " name=" + name);
                return name;
            }
            string fallback = ResolveUniqueAllianceName(pAlliance, XiaFallbackNameRules.LocalAllianceName(id));
            ModClass.LogInfo("[Xia alliance naming] route=local-fallback alliance=" + id +
                             " name=" + fallback);
            return fallback;
        }

        private static bool IsUsefulAllianceName(string pName)
        {
            return !XiaNameRepairRules.IsInvalidGeneratedMetaName(pName) &&
                   !string.Equals(pName?.Trim(), "之盟", StringComparison.Ordinal);
        }

        private static string ResolveUniqueAllianceName(Alliance pAlliance, string pCandidate)
        {
            var used = new List<string>();
            if (World.world?.alliances != null)
            {
                foreach (Alliance alliance in World.world.alliances)
                {
                    if (alliance?.data == null || alliance == pAlliance || alliance.isRekt()) continue;
                    if (!string.IsNullOrEmpty(alliance.data.name)) used.Add(alliance.data.name);
                }
            }
            return XiaAllianceNamingRules.ResolveUniqueName(
                pCandidate, pAlliance?.getID() ?? 0L, used);
        }

        private static string GenerateVanillaName(string pGeneratorId, long pSeed)
        {
            if (!AssetManager.name_generator.has(pGeneratorId)) return null;

            try
            {
                string name = NameGenerator.getName(pGeneratorId, ActorSex.None, false, null, pSeed, true);
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Xia naming fallback failed: " + pGeneratorId + " - " + e.Message);
                return null;
            }
        }
    }
}
