#if 一米_中文名
using System;
using System.Collections.Generic;
using Chinese_Name;

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
                    if (TryRenameKingdom(kingdom, null, pForce: false)) changed++;
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
            if (!IsXiaKingdom(pKingdom, pActor)) return false;
            if (!pForce && !XiaNameRepairRules.IsInvalidGeneratedMetaName(pKingdom?.data?.name)) return false;

            string name = GenerateKingdomName(pKingdom);
            if (XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return false;

            pKingdom.setName(name, pTrack: false);
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
            return pKingdom?.data?.original_actor_asset == XiaRace.ID ||
                   pKingdom?.getActorAsset()?.id == XiaRace.ID;
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
            string name = GenerateChineseName(XiaNameSets.KingdomGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.KingdomGenerator);
                ParameterGetters.GetKingdomParameterGetter(generator.parameter_getter)(pKingdom, p);
            });
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return name;

            name = GenerateVanillaName(XiaNameSets.KingdomGenerator, pKingdom?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return name;

            return GenerateLocalKingdomName(pKingdom);
        }

        private static string GenerateLanguageName(Language pLanguage)
        {
            string name = GenerateChineseName(XiaNameSets.LanguageGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.LanguageGenerator);
                ParameterGetters.GetLanguageParameterGetter(generator.parameter_getter)(pLanguage, p);
            });
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return name;

            name = GenerateVanillaName(XiaNameSets.LanguageGenerator, pLanguage?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidGeneratedMetaName(name)) return name;

            return "夏语";
        }

        private static string GenerateReligionName(Religion pReligion)
        {
            string name = GenerateChineseName(XiaNameSets.ReligionGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.ReligionGenerator);
                ParameterGetters.GetReligionParameterGetter(generator.parameter_getter)(pReligion, p);
            });
            if (!XiaNameRepairRules.IsInvalidXiaReligionName(name)) return name;

            name = GenerateVanillaName(XiaNameSets.ReligionGenerator, pReligion?.getID() ?? 0L);
            if (!XiaNameRepairRules.IsInvalidXiaReligionName(name)) return name;

            return GenerateLocalReligionName(pReligion);
        }

        private static string GenerateCultureName(Culture pCulture)
        {
            string originName = GenerateOriginCultureName(pCulture);
            if (!string.IsNullOrEmpty(originName)) return originName;

            string name = GenerateChineseName(XiaNameSets.CultureGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.CultureGenerator);
                ParameterGetters.GetCultureParameterGetter(generator.parameter_getter)(pCulture, p);
            });
            if (!IsInvalidGeneratedName(name)) return name;

            name = GenerateVanillaName(XiaNameSets.CultureGenerator, pCulture?.getID() ?? 0L);
            if (!IsInvalidGeneratedName(name)) return name;

            return GenerateLocalCultureName(pCulture);
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
            string name = GenerateChineseName(XiaNameSets.SubspeciesGenerator, p =>
            {
                var generator = CN_NameGeneratorLibrary.Get(XiaNameSets.SubspeciesGenerator);
                ParameterGetters.GetSubspeciesParameterGetter(generator.parameter_getter)(pSubspecies, p);
            });
            if (IsUsefulSubspeciesName(name)) return name;

            name = GenerateVanillaName(XiaNameSets.SubspeciesGenerator, pSubspecies?.getID() ?? 0L);
            if (IsUsefulSubspeciesName(name)) return name;

            return GenerateLocalSubspeciesName(pSubspecies);
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

        private static string GenerateLocalKingdomName(Kingdom pKingdom)
        {
            string[] fixedNames =
            {
                "夏", "商", "周", "秦", "汉", "魏", "晋", "楚", "齐", "鲁", "燕", "赵",
                "宋", "郑", "卫", "陈", "蔡", "吴", "越", "唐", "虞"
            };

            long id = pKingdom?.getID() ?? DateTime.UtcNow.Ticks;
            var random = new System.Random(unchecked((int)(id * 1103515245L + 12345L)));
            return fixedNames[random.Next(fixedNames.Length)];
        }

        private static string GenerateLocalReligionName(Religion pReligion)
        {
            string[] fixedNames =
            {
                "社稷礼", "宗庙礼", "礼乐祖祀", "华夏大礼", "诸夏礼",
                "天命礼", "王畿祖祀", "河洛礼", "先王祀典", "九州王礼"
            };

            long id = pReligion?.getID() ?? DateTime.UtcNow.Ticks;
            var random = new System.Random(unchecked((int)(id * 1103515245L + 12345L)));
            return fixedNames[random.Next(fixedNames.Length)];
        }

        private static string GenerateLocalSubspeciesName(Subspecies pSubspecies)
        {
            string[] fixedNames =
            {
                "华夏人", "诸夏人", "河洛夏人", "中原夏人", "九州夏人",
                "王畿夏人", "礼乐夏人", "玄鸟夏人", "青铜夏人", "邦国夏人"
            };
            string[] prefixes =
            {
                "河洛", "中原", "九州", "王畿", "礼乐", "玄鸟", "青铜", "邦国", "洛邑", "镐京"
            };

            long id = pSubspecies?.getID() ?? DateTime.UtcNow.Ticks;
            var random = new System.Random(unchecked((int)(id * 1103515245L + 12345L)));
            if (random.NextDouble() < 0.45)
                return fixedNames[random.Next(fixedNames.Length)];
            return prefixes[random.Next(prefixes.Length)] + "夏人";
        }

        private static string GenerateLocalCultureName(Culture pCulture)
        {
            string[] fixedNames =
            {
                "诸夏文化", "华夏文化", "中原礼制", "河洛雅风", "九州礼乐",
                "青铜礼制", "王畿雅风", "邦国礼制"
            };

            long id = pCulture?.getID() ?? DateTime.UtcNow.Ticks;
            var random = new System.Random(unchecked((int)(id * 1103515245L + 12345L)));
            return fixedNames[random.Next(fixedNames.Length)];
        }

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
#endif
