using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

#if 一米_中文名
using Chinese_Name;
#endif

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

        internal static int EnsureWorldNames()
        {
            int changed = 0;

            if (World.world?.kingdoms != null)
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (TryApplyFullyXiaizedKingdomName(kingdom))
                    {
                        changed++;
                        continue;
                    }

                    bool applied = false;
                    kingdom?.data?.get(LineageKeys.XIA_FULL_NAME_APPLIED, out applied, false);
                    bool originalXia = kingdom?.data?.original_actor_asset == XiaRace.ID ||
                                       kingdom?.asset?.id == XiaRace.ID;
                    if (XiaizedKingdomNamingRules.ShouldRunOrdinaryRepair(originalXia,
                            XiaizationService.GetLevel(kingdom), XiaizationService.LevelXiaizedDynasty, applied) &&
                        TryRenameKingdom(kingdom, null, pForce: false)) changed++;
                }
            }

            if (World.world?.cultures != null)
            {
                foreach (Culture culture in World.world.cultures)
                {
                    if (TryRenameCulture(culture, null, pForce: false)) changed++;
                }
            }

            if (World.world?.languages != null)
            {
                foreach (Language language in World.world.languages)
                {
                    if (TryRenameLanguage(language, null, pForce: false)) changed++;
                }
            }

            if (World.world?.subspecies != null)
            {
                foreach (Subspecies subspecies in World.world.subspecies)
                {
                    if (TryRenameSubspecies(subspecies, null, pForce: false)) changed++;
                }
            }

            if (World.world?.religions != null)
            {
                foreach (Religion religion in World.world.religions)
                {
                    if (TryRenameReligion(religion, null, pForce: false)) changed++;
                }
            }

            return changed;
        }

        internal static bool TryRenameKingdom(Kingdom pKingdom, Actor pActor, bool pForce)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            if (!IsXiaKingdom(pKingdom, pActor)) return false;
            if (!pForce && !XiaNameRepairRules.IsInvalidGeneratedMetaName(pKingdom?.data?.name)) return false;

            string name = GenerateKingdomName(pKingdom);
            if (XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return false;

            pKingdom.setName(name, pTrack: false);
            return true;
        }

        internal static bool TryApplyFullyXiaizedKingdomName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            pKingdom.data.get(LineageKeys.XIA_FULL_NAME_APPLIED, out bool applied, false);
            bool originalXia = pKingdom.data.original_actor_asset == XiaRace.ID ||
                               pKingdom.asset?.id == XiaRace.ID;
            if (!XiaizedKingdomNamingRules.ShouldApply(originalXia,
                    XiaizationService.GetLevel(pKingdom), XiaizationService.LevelXiaizedDynasty, applied))
                return false;
            if (!TryRenameKingdom(pKingdom, pKingdom.king, pForce: true)) return false;
            pKingdom.data.set(LineageKeys.XIA_FULL_NAME_APPLIED, true);
            return true;
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
#if 一米_中文名
            string chineseName = GenerateChineseName(XiaNameSets.KingdomGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.KingdomGenerator);
                ParameterGetters.GetKingdomParameterGetter(generator.parameter_getter)(pKingdom, p);
            });
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(chineseName)) return chineseName;
#endif

            string name = GenerateVanillaName(XiaNameSets.KingdomGenerator, pKingdom?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return name;

            return XiaFallbackNameRules.LocalKingdomName(pKingdom?.getID() ?? 0L);
        }

        private static string GenerateLanguageName(Language pLanguage)
        {
#if 一米_中文名
            string chineseName = GenerateChineseName(XiaNameSets.LanguageGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.LanguageGenerator);
                ParameterGetters.GetLanguageParameterGetter(generator.parameter_getter)(pLanguage, p);
            });
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(chineseName)) return chineseName;
#endif

            string name = GenerateVanillaName(XiaNameSets.LanguageGenerator, pLanguage?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return name;

            return XiaFallbackNameRules.LocalLanguageName(pLanguage?.getID() ?? 0L);
        }

        private static string GenerateReligionName(Religion pReligion)
        {
#if 一米_中文名
            string chineseName = GenerateChineseName(XiaNameSets.ReligionGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.ReligionGenerator);
                ParameterGetters.GetReligionParameterGetter(generator.parameter_getter)(pReligion, p);
            });
            if (!XiaNameRepairRules.IsInvalidXiaReligionName(chineseName)) return chineseName;
#endif

            string name = GenerateVanillaName(XiaNameSets.ReligionGenerator, pReligion?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidXiaReligionName(name)) return name;

            return XiaFallbackNameRules.LocalReligionName(pReligion?.getID() ?? 0L);
        }

        private static string GenerateCultureName(Culture pCulture)
        {
            string originName = GenerateOriginCultureName(pCulture);
            if (!string.IsNullOrEmpty(originName)) return originName;

#if 一米_中文名
            string chineseName = GenerateChineseName(XiaNameSets.CultureGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.CultureGenerator);
                ParameterGetters.GetCultureParameterGetter(generator.parameter_getter)(pCulture, p);
            });
            if (!IsInvalidGeneratedName(chineseName)) return chineseName;
#endif

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
#if 一米_中文名
            string chineseName = GenerateChineseName(XiaNameSets.SubspeciesGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.SubspeciesGenerator);
                ParameterGetters.GetSubspeciesParameterGetter(generator.parameter_getter)(pSubspecies, p);
            });
            if (IsUsefulSubspeciesName(chineseName)) return chineseName;
#endif

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

#if 一米_中文名
        private static string GenerateChineseName(string pGeneratorId, Action<Dictionary<string, string>> pFillParameters)
        {
            try
            {
                var generator = CN_NameGeneratorLibrary.Get(pGeneratorId);
                if (generator == null) return null;

                var parameters = new Dictionary<string, string>();
                pFillParameters(parameters);
                string name = generator.GenerateName(parameters);
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Xia Chinese naming failed: " + pGeneratorId + " - " + e.Message);
                return null;
            }
        }
#endif

        internal static string GenerateAllianceName(Alliance pAlliance)
        {
            long id = pAlliance?.getID() ?? 0L;
#if 一米_中文名
            string chineseName = GenerateChineseName(XiaNameSets.AllianceGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.AllianceGenerator);
                ParameterGetters.GetAllianceParameterGetter(generator.parameter_getter)(pAlliance, p);
            });
            if (IsUsefulAllianceName(chineseName))
            {
                chineseName = ResolveUniqueAllianceName(pAlliance, chineseName);
                ModClass.LogInfo("[Xia alliance naming] route=ChineseName alliance=" + id +
                                 " name=" + chineseName);
                return chineseName;
            }
#endif

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
