using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class XiaizationService
    {
        public const int LevelNone = 0;
        public const int LevelForeignOccupier = 1;
        public const int LevelPseudoDynasty = 2;
        public const int LevelAdoptedRites = 3;
        public const int LevelXiaInstitutions = 4;
        public const int LevelXiaizedDynasty = 5;

        private const string TYPE_FOREIGN_ENTRY = "foreign_entry";
        private const string TYPE_PSEUDO_DYNASTY = "pseudo_dynasty";

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static bool _persistedCultureIntegrationsRestored;

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;
            ProjectCultureIntegration(pKingdom);
            if (IsNativePolicyKingdom(pKingdom)) return;

            int level = GetLevel(pKingdom);
            if (level <= LevelNone) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.XIAIZATION_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.XIAIZATION_LAST_YEAR, year);

            int stableYears = ReadStableYears(pKingdom) + 1;
            bool changed = false;
            if (KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_adopt_xia_rites"))
                changed |= TrySetLevel(pKingdom, LevelAdoptedRites, "adopt_xia_rites", false);
            if (KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_xia_law_institutions"))
                changed |= TrySetLevel(pKingdom, LevelXiaInstitutions, "xia_law_institutions", false);

            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate == pKingdom && level >= LevelXiaInstitutions && stableYears >= 40)
                changed |= TrySetLevel(pKingdom, LevelXiaizedDynasty, "stable_pseudo_dynasty", false);

            UpsertKingdomState(pKingdom, Math.Max(GetLevel(pKingdom), level), CurrentLegitimacy(pKingdom),
                adoptedRites: HasAdoptedRites(pKingdom), adoptedLaw: HasAdoptedLaw(pKingdom), stableYears);
            if (changed) TryEnablePolicySystem(pKingdom, pAi: true);
        }

        public static bool CanUseMandateSystem(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            return XiaizationEligibilityRules.CanUseMandateSystem(
                IsNativePolicyKingdom(pKingdom),
                GetLevel(pKingdom),
                IsForeignPseudoDynasty(pKingdom));
        }

        public static bool CanUsePolicySystem(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            return XiaizationEligibilityRules.CanUsePolicySystem(
                IsNativePolicyKingdom(pKingdom),
                GetLevel(pKingdom));
        }

        public static bool IsNativePolicyKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (LineageService.IsXiaKingdom(pKingdom)) return true;

            string resolvedActorAssetId = "";
            try { resolvedActorAssetId = pKingdom.getActorAsset()?.id ?? ""; }
            catch { resolvedActorAssetId = ""; }
            return CivMonkeyPolicyRules.IsNativePolicySpecies(
                pKingdom.data.original_actor_asset,
                pKingdom.asset?.id,
                resolvedActorAssetId);
        }

        public static bool DefaultPolicyEnabled(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            return IsNativePolicyKingdom(pKingdom) || GetLevel(pKingdom) >= LevelPseudoDynasty;
        }

        public static bool DefaultPolicyAIEnabled(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            return IsNativePolicyKingdom(pKingdom) || GetLevel(pKingdom) >= LevelPseudoDynasty;
        }

        public static float GetContactProgress(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            pKingdom.data.get(LineageKeys.XIA_CONTACT_PROGRESS, out float value, 0f);
            return Mathf.Clamp(value, 0f, XiaContactRules.PolicyUnlockProgress);
        }

        public static bool RegisterContactProgress(Kingdom pKingdom, float pGain, string pReason, bool pRecord)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                IsNativePolicyKingdom(pKingdom)) return false;
            if (pGain <= 0f) return false;

            float current = GetContactProgress(pKingdom);
            float next = Mathf.Clamp(current + pGain, 0f, XiaContactRules.PolicyUnlockProgress);
            int targetLevel = XiaContactRules.LevelForProgress(next);
            if (next <= current && GetLevel(pKingdom) >= targetLevel) return false;

            pKingdom.data.set(LineageKeys.XIA_CONTACT_PROGRESS, next);
            bool changed = targetLevel > 0 && TrySetLevel(pKingdom, targetLevel, pReason ?? "xia_contact", pRecord);
            if (targetLevel >= LevelPseudoDynasty)
                TryEnablePolicySystem(pKingdom, pAi: true);

            UpsertKingdomState(pKingdom, Math.Max(GetLevel(pKingdom), targetLevel), CurrentLegitimacy(pKingdom),
                HasAdoptedRites(pKingdom), HasAdoptedLaw(pKingdom), ReadStableYears(pKingdom));
            return changed || next > current;
        }

        public static bool IsForeignPseudoDynasty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || IsNativePolicyKingdom(pKingdom)) return false;
            int level = GetLevel(pKingdom);
            if (level < LevelPseudoDynasty) return false;
            pKingdom.data.get(LineageKeys.XIAIZATION_PSEUDO_DYNASTY, out bool pseudo, false);
            if (pseudo) return true;
            string legitimacy = CurrentLegitimacy(pKingdom);
            return legitimacy == "pseudo_mandate" || legitimacy == TYPE_PSEUDO_DYNASTY;
        }

        public static bool UsesXiaizedInstitutionSystem(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            return XiaizationEligibilityRules.CanUseInstitutionSystem(
                IsNativePolicyKingdom(pKingdom),
                GetLevel(pKingdom),
                IsForeignPseudoDynasty(pKingdom));
        }

        public static int GetLevel(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return LevelNone;
            if (IsNativePolicyKingdom(pKingdom)) return LevelXiaizedDynasty;

            pKingdom.data.get(LineageKeys.XIAIZATION_LEVEL, out int value, -1);
            if (value >= 0) return value;

            int dbValue = ReadKingdomLevel(pKingdom.id);
            if (dbValue >= 0)
                pKingdom.data.set(LineageKeys.XIAIZATION_LEVEL, dbValue);
            return Math.Max(LevelNone, dbValue);
        }

        public static bool ProjectCultureIntegration(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.culture?.data == null)
                return false;

            RestorePersistedCultureIntegrations();
            if (!KingdomPolicySplitInheritanceRules
                    .ShouldMarkCultureIntegrated(
                        XiaCultureIntegrationService.IsNativeXiaCulture(
                            pKingdom.culture),
                        LevelNone))
                return false;

            return XiaCultureIntegrationService.MarkIntegrated(
                pKingdom.culture);
        }

        public static void ResetCultureIntegrationProjection()
        {
            _persistedCultureIntegrationsRestored = false;
        }

        public static bool RestorePersistedCultureIntegrations()
        {
            if (_persistedCultureIntegrationsRestored) return true;
            if (!Ready || World.world?.cultures == null) return false;

            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    "SELECT DISTINCT COURT_CULTURE_ID FROM " +
                    KingdomXiaizationStateTableItem.GetTableName() +
                    " WHERE XIAIZATION_LEVEL>=@level " +
                    "AND COURT_CULTURE_ID<>''";
                cmd.Parameters.AddWithValue("@level", LevelXiaInstitutions);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                while (reader.Read())
                {
                    string raw = reader.IsDBNull(0)
                        ? ""
                        : Convert.ToString(reader.GetValue(0)) ?? "";
                    if (!long.TryParse(raw, out long cultureId)) continue;
                    Culture culture = World.world.cultures.get(cultureId);
                    XiaCultureIntegrationService.MarkIntegrated(culture);
                }

                _persistedCultureIntegrationsRestored = true;
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Culture Xia integration restore failed: " +
                    e.Message);
                return false;
            }
        }

        public static bool RestoreIdentityContinuity(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (IsNativePolicyKingdom(pKingdom))
            {
                pKingdom.data.set(LineageKeys.XIAIZATION_LEVEL, LevelXiaizedDynasty);
                pKingdom.data.set(LineageKeys.POLICY_ENABLED, true);
                pKingdom.data.set(LineageKeys.POLICY_AI_ENABLED, true);
                ProjectCultureIntegration(pKingdom);
                return true;
            }
            if (!Ready) return false;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT XIAIZATION_LEVEL, LEGITIMACY_TYPE FROM " +
                    $"{KingdomXiaizationStateTableItem.GetTableName()} WHERE KINGDOM_ID=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdom.id);
                using var reader = (SQLiteDataReader)cmd.ExecuteReader();
                if (!reader.Read()) return false;
                int level = reader.IsDBNull(0) ? LevelNone : Convert.ToInt32(reader.GetValue(0));
                string legitimacy = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1)) ?? "";
                pKingdom.data.set(LineageKeys.XIAIZATION_LEVEL, level);
                pKingdom.data.set(LineageKeys.XIAIZATION_LEGITIMACY, legitimacy);
                pKingdom.data.set(LineageKeys.XIAIZATION_PSEUDO_DYNASTY,
                    level >= LevelPseudoDynasty &&
                    (legitimacy == TYPE_PSEUDO_DYNASTY || legitimacy == "pseudo_mandate"));
                if (level >= LevelPseudoDynasty)
                {
                    pKingdom.data.set(LineageKeys.POLICY_ENABLED, true);
                    pKingdom.data.set(LineageKeys.POLICY_AI_ENABLED, true);
                }
                RestorePersistedCultureIntegrations();
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Kingdom Xiaization continuity read failed: " + e.Message);
                return false;
            }
        }

        public static bool InheritForSplit(Kingdom pNewKingdom,
            Kingdom pSource)
        {
            if (pNewKingdom?.data == null || pSource?.data == null ||
                pNewKingdom == pSource || pSource.isRekt()) return false;

            int level = KingdomPolicySplitInheritanceRules
                .NormalizeInheritedXiaizationLevel(GetLevel(pSource));
            pNewKingdom.data.set(LineageKeys.XIAIZATION_LEVEL, level);
            pNewKingdom.data.set(LineageKeys.XIAIZATION_LEGITIMACY,
                "culture_split");
            pNewKingdom.data.set(LineageKeys.XIAIZATION_PSEUDO_DYNASTY,
                false);
            pNewKingdom.data.set(LineageKeys.XIA_CONTACT_PROGRESS,
                GetContactProgress(pSource));

            if (level >= LevelPseudoDynasty)
                TryEnablePolicySystem(pNewKingdom, pAi: true);

            bool adoptedRites = level >= LevelAdoptedRites ||
                                HasAdoptedRites(pSource);
            bool adoptedLaw = level >= LevelXiaInstitutions ||
                              HasAdoptedLaw(pSource);
            UpsertKingdomState(pNewKingdom, level, "culture_split",
                adoptedRites, adoptedLaw, stableYears: 0);

            if (level >= LevelXiaizedDynasty)
                XiaNamingRepair.TryApplyFullyXiaizedKingdomName(
                    pNewKingdom);
            RulerAppellationService.RefreshLivingProjection(pNewKingdom);
            return true;
        }

        public static string GetLevelLabel(Kingdom pKingdom)
        {
            return LevelLabel(GetLevel(pKingdom));
        }

        public static string BuildTooltip(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            int level = GetLevel(pKingdom);
            bool nativeXiaCulture = IsNativePolicyKingdom(pKingdom);
            if (level <= LevelNone && !nativeXiaCulture) return "";
            var lines = new List<string>
            {
                AW_L10n.Text("aw_xiaization_level", "Xiaization level") + ": " + LevelLabel(level)
            };
            float contact = GetContactProgress(pKingdom);
            if (contact > 0.1f && !nativeXiaCulture)
                lines.Add(AW_L10n.Text("aw_xiaization_contact", "Xia contact: ") +
                          Mathf.RoundToInt(contact) + "%");
            string sourceText = ContactSourceText(pKingdom);
            if (!string.IsNullOrEmpty(sourceText))
                lines.Add(AW_L10n.Text("aw_xiaization_contact_source", "Contact sources: ") + sourceText);
            if (IsForeignPseudoDynasty(pKingdom))
                lines.Add(AW_L10n.Text("aw_xiaization_pseudo_detail",
                    "Pseudo dynasty: retains foreign ancestry while adopting Xia institutions"));
            else if (UsesXiaizedInstitutionSystem(pKingdom) && !nativeXiaCulture)
                lines.Add(AW_L10n.Text("aw_xiaization_institutions_detail",
                    "Xia institutions: retain original appearance and ancestry while adopting AW3 government"));
            float resentment = MaxCityResentment(pKingdom);
            if (resentment > 0.1f)
                lines.Add(AW_L10n.Text("aw_xiaization_max_resentment", "Highest resentment: ") +
                          Mathf.RoundToInt(resentment));
            return string.Join("\n", lines.ToArray());
        }

        public static void OnForeignOccupationTick(City pCity, Kingdom pOwner, string pOccupationType,
            double pAssimilationProgress, double pResentment)
        {
            if (pCity?.data == null || pOwner?.data == null || !IsXiaOccupationMode(pOccupationType)) return;
            int level = pOccupationType == TYPE_PSEUDO_DYNASTY ? LevelPseudoDynasty : LevelForeignOccupier;
            EnsureForeignOccupier(pOwner, pCity, pOccupationType, level);
            UpsertCityState(pCity, pOwner, pOccupationType, pAssimilationProgress, pAssimilationProgress, pResentment);
        }

        public static void OnPseudoMandateDeclared(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || IsNativePolicyKingdom(pKingdom)) return;
            pKingdom.data.set(LineageKeys.XIAIZATION_PSEUDO_DYNASTY, true);
            if (TrySetLevel(pKingdom, LevelPseudoDynasty, "pseudo_mandate", true))
                TryEnablePolicySystem(pKingdom, pAi: true);
            LineageService.EnsureForeignPseudoDynastyLineage(pKingdom);
        }

        public static void CompleteXiaizedCity(City pCity, Kingdom pOwner, string pOccupationType)
        {
            if (pCity?.data == null || pOwner?.data == null) return;
            int level = pOccupationType == TYPE_PSEUDO_DYNASTY ? LevelPseudoDynasty : LevelForeignOccupier;
            string mode = IsXiaOccupationMode(pOccupationType) ? pOccupationType : TYPE_FOREIGN_ENTRY;
            EnsureForeignOccupier(pOwner, pCity, mode, level);
            UpsertCityState(pCity, pOwner, mode + "_xiaized", 100.0, 100.0, GetCityResentment(pCity));
            HistoricalSchoolRuntime.RefreshLivingXiaCity(pCity);

            HistoryWriter.RecordCity(pCity, pOwner, CityEvent.XIAIZATION_PROGRESS,
                HistoryText.City(pCity, pOwner) +
                HistoryLocalizationRules.H("aw_hist_xiaized_city_mid") +
                HistoryText.Kingdom(pOwner) +
                HistoryLocalizationRules.H("aw_hist_xiaized_city_suffix"),
                HistoryTarget.City(pCity));
        }

        public static bool ApplyPolicyEffect(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return false;
            switch (pDef.Id)
            {
                case "aw_policy_adopt_xia_rites":
                    return TrySetLevel(pKingdom, LevelAdoptedRites, "adopt_xia_rites", true);
                case "aw_policy_xia_law_institutions":
                    bool changed = TrySetLevel(pKingdom, LevelXiaInstitutions, "xia_law_institutions", true);
                    LineageService.EnsureForeignPseudoDynastyLineage(pKingdom);
                    return changed;
                case "aw_decision_appease_xia_cities":
                    AppeaseXiaCities(pKingdom);
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsXiaizationPolicy(KingdomPolicyDef pDef)
        {
            return pDef != null &&
                   (pDef.Id == "aw_policy_adopt_xia_rites" ||
                    pDef.Id == "aw_policy_xia_law_institutions" ||
                    pDef.Id == "aw_decision_appease_xia_cities");
        }

        public static bool SpecialRequirementMet(Kingdom pKingdom, string pPolicyId)
        {
            if (pKingdom?.data == null) return false;
            switch (pPolicyId)
            {
                case "aw_policy_adopt_xia_rites":
                    return !IsNativePolicyKingdom(pKingdom) &&
                           GetLevel(pKingdom) >= LevelPseudoDynasty;
                case "aw_policy_xia_law_institutions":
                    return !IsNativePolicyKingdom(pKingdom) &&
                           GetLevel(pKingdom) >= LevelAdoptedRites;
                case "aw_decision_appease_xia_cities":
                    return GetLevel(pKingdom) >= LevelPseudoDynasty && MaxCityResentment(pKingdom) >= 15f;
                default:
                    return true;
            }
        }

        public static int ScoreResearch(Kingdom pKingdom, KingdomPolicyDef pDef)
        {
            if (pKingdom?.data == null || pDef == null) return 0;
            if (IsNativePolicyKingdom(pKingdom) ||
                GetLevel(pKingdom) < LevelPseudoDynasty) return 0;
            switch (pDef.Id)
            {
                case "aw_policy_adopt_xia_rites": return 520;
                case "aw_policy_xia_law_institutions": return 500;
                case "aw_decision_appease_xia_cities": return 460;
                default: return 0;
            }
        }

        private static void EnsureForeignOccupier(Kingdom pKingdom, City pCity, string pType, int pLevel)
        {
            if (pKingdom?.data == null || IsNativePolicyKingdom(pKingdom)) return;
            if (pType == TYPE_PSEUDO_DYNASTY)
                pKingdom.data.set(LineageKeys.XIAIZATION_PSEUDO_DYNASTY, true);
            bool changed = TrySetLevel(pKingdom, pLevel, pType, false);
            TryEnablePolicySystem(pKingdom, pAi: true);
            UpsertKingdomState(pKingdom, Math.Max(GetLevel(pKingdom), pLevel), pType,
                HasAdoptedRites(pKingdom), HasAdoptedLaw(pKingdom), ReadStableYears(pKingdom));
            if (!changed) return;

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.XIAIZATION_ADOPTED,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_xiaization_entered"),
                pCity?.data != null ? HistoryTarget.City(pCity) : HistoryTarget.Kingdom(pKingdom));
        }

        private static bool TrySetLevel(Kingdom pKingdom, int pLevel, string pLegitimacy, bool pRecord)
        {
            if (pKingdom?.data == null || pLevel <= GetLevel(pKingdom)) return false;
            pKingdom.data.set(LineageKeys.XIAIZATION_LEVEL, pLevel);
            pKingdom.data.set(LineageKeys.XIAIZATION_LEGITIMACY, pLegitimacy ?? "");
            if (KingdomPolicySplitInheritanceRules
                    .ShouldMarkCultureIntegrated(
                        XiaCultureIntegrationService.IsNativeXiaCulture(
                            pKingdom.culture), pLevel))
                XiaCultureIntegrationService.MarkIntegrated(
                    pKingdom.culture);
            UpsertKingdomState(pKingdom, pLevel, pLegitimacy, HasAdoptedRites(pKingdom), HasAdoptedLaw(pKingdom),
                ReadStableYears(pKingdom));
            if (pLevel >= LevelXiaizedDynasty)
                XiaNamingRepair.TryApplyFullyXiaizedKingdomName(pKingdom);
            RulerAppellationService.RefreshLivingProjection(pKingdom);

            if (pRecord)
            {
                HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.XIAIZATION_ADOPTED,
                    HistoryText.Kingdom(pKingdom) + " " + HistoryText.PlainText(LevelEventText(pLevel)),
                    HistoryTarget.Kingdom(pKingdom));
                Actor king = pKingdom.king;
                if (king?.data != null)
                    HistoryWriter.RecordPerson(king.data.id, pKingdom, king.getName(), PersonEvent.XIAIZATION_ADOPTED,
                        HistoryText.Actor(king) +
                        HistoryLocalizationRules.H("aw_hist_xiaization_presided_mid") +
                        HistoryText.PlainText(LevelEventText(pLevel)),
                        ChronicleCategory.HONOR,
                        HistoryTarget.Kingdom(pKingdom));
            }

            return true;
        }

        private static void TryEnablePolicySystem(Kingdom pKingdom, bool pAi)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.POLICY_ENABLED, true);
            pKingdom.data.set(LineageKeys.POLICY_AI_ENABLED, pAi);
            KingdomPolicyService.EnsureInitialized(pKingdom);
        }

        private static void AppeaseXiaCities(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !Ready) return;
            int changed = 0;
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                double current = GetCityResentment(city);
                if (current <= 0.0) continue;
                UpsertCityState(city, pKingdom, "appeased", 100.0, 100.0, Math.Max(0.0, current - 18.0));
                ForeignOccupationService.AdjustResentment(city, -18.0);
                changed++;
            }

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.XIAIZATION_ADOPTED,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_xiaization_appeased_mid") +
                HistoryText.PlainText(changed.ToString()) +
                HistoryLocalizationRules.H("aw_hist_xiaization_city_count_suffix"),
                HistoryTarget.Kingdom(pKingdom));
        }

        private static void UpsertKingdomState(Kingdom pKingdom, int pLevel, string pLegitimacy,
            bool adoptedRites, bool adoptedLaw, int stableYears)
        {
            if (!Ready || pKingdom?.data == null) return;
            string table = KingdomXiaizationStateTableItem.GetTableName();
            double now = LineageService.CurTime();
            var values = new[]
            {
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                ColumnVal.Create("ORIGINAL_SPECIES", ResolveSpecies(pKingdom)),
                ColumnVal.Create("XIAIZATION_LEVEL", pLevel),
                ColumnVal.Create("LEGITIMACY_TYPE", pLegitimacy ?? ""),
                ColumnVal.Create("COURT_CULTURE_ID", IdOf(pKingdom.culture)),
                ColumnVal.Create("COURT_LANGUAGE_ID", IdOf(pKingdom.language)),
                ColumnVal.Create("ADOPTED_RITES", adoptedRites ? 1 : 0),
                ColumnVal.Create("ADOPTED_LAW", adoptedLaw ? 1 : 0),
                ColumnVal.Create("STABLE_YEARS", stableYears),
                ColumnVal.Create("UPDATED_TIME", now)
            };

            try
            {
                if (DB.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id)))
                {
                    DB.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("KINGDOM_ID", pKingdom.id) },
                        values);
                }
                else
                {
                    var insert = new List<ColumnVal>
                    {
                        ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                        ColumnVal.Create("START_TIME", now)
                    };
                    insert.AddRange(values);
                    DB.Insert(table, insert.ToArray());
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("KingdomXiaizationState upsert failed: " + e.Message);
            }
        }

        private static void UpsertCityState(City pCity, Kingdom pKingdom, string pMode, double pXiaProgress,
            double pForeignEliteProgress, double pResentment)
        {
            if (!Ready || pCity?.data == null) return;
            string table = CityXiaizationStateTableItem.GetTableName();
            double now = LineageService.CurTime();
            var values = new[]
            {
                ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                ColumnVal.Create("KINGDOM_ID", pKingdom?.id ?? -1L),
                ColumnVal.Create("KINGDOM_NAME", pKingdom?.name ?? ""),
                ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                ColumnVal.Create("MODE", pMode ?? ""),
                ColumnVal.Create("XIA_PROGRESS", pXiaProgress),
                ColumnVal.Create("FOREIGN_ELITE_PROGRESS", pForeignEliteProgress),
                ColumnVal.Create("RESENTMENT", pResentment),
                ColumnVal.Create("ORIGINAL_CULTURE_ID", IdOf(pCity.culture)),
                ColumnVal.Create("ORIGINAL_LANGUAGE_ID", IdOf(pCity.language)),
                ColumnVal.Create("COURT_CULTURE_ID", IdOf(pKingdom?.culture)),
                ColumnVal.Create("COURT_LANGUAGE_ID", IdOf(pKingdom?.language)),
                ColumnVal.Create("UPDATED_TIME", now)
            };

            try
            {
                if (DB.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("CITY_ID", pCity.id)))
                {
                    DB.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("CITY_ID", pCity.id) },
                        values);
                }
                else
                {
                    var insert = new List<ColumnVal>
                    {
                        ColumnVal.Create("CITY_ID", pCity.id),
                        ColumnVal.Create("START_TIME", now)
                    };
                    insert.AddRange(values);
                    DB.Insert(table, insert.ToArray());
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning("CityXiaizationState upsert failed: " + e.Message);
            }
        }

        private static bool IsXiaOccupationMode(string pType)
        {
            return pType == TYPE_FOREIGN_ENTRY || pType == TYPE_PSEUDO_DYNASTY;
        }

        private static bool HasAdoptedRites(Kingdom pKingdom)
        {
            return GetLevel(pKingdom) >= LevelAdoptedRites ||
                   KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_adopt_xia_rites");
        }

        private static bool HasAdoptedLaw(Kingdom pKingdom)
        {
            return GetLevel(pKingdom) >= LevelXiaInstitutions ||
                   KingdomPolicyService.IsCompleted(pKingdom, PolicyNodeKind.Social, "aw_policy_xia_law_institutions");
        }

        private static string CurrentLegitimacy(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.XIAIZATION_LEGITIMACY, out string value, "");
            return value ?? "";
        }

        private static string ContactSourceText(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.XIA_CONTACT_LAST_SOURCE_MASK, out string raw, "");
            if (string.IsNullOrEmpty(raw)) return "";

            var labels = new List<string>();
            foreach (string part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part)
                {
                    case "border":
                        labels.Add(AW_L10n.Text("aw_xiaization_source_border", "Shared border"));
                        break;
                    case "nearby":
                        labels.Add(AW_L10n.Text("aw_xiaization_source_nearby", "Nearby"));
                        break;
                    case "diplomacy":
                        labels.Add(AW_L10n.Text("aw_xiaization_source_diplomacy", "Alliance"));
                        break;
                    case "vassal":
                        labels.Add(AW_L10n.Text("aw_xiaization_source_vassal", "Vassalage"));
                        break;
                    case "occupation":
                        labels.Add(AW_L10n.Text("aw_xiaization_source_occupation", "Rule over Xia lands"));
                        break;
                    case "mixed":
                        labels.Add(AW_L10n.Text("aw_xiaization_source_mixed", "Intermarriage"));
                        break;
                    case "official":
                        labels.Add(AW_L10n.Text("aw_xiaization_source_official", "Official contact"));
                        break;
                }
            }
            return labels.Count == 0 ? "" : string.Join("/", labels.ToArray());
        }

        private static int ReadStableYears(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null) return 0;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT STABLE_YEARS FROM " + KingdomXiaizationStateTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdom.id);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
            }
            catch { return 0; }
        }

        private static int ReadKingdomLevel(long pKingdomId)
        {
            if (!Ready || pKingdomId < 0) return LevelNone;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT XIAIZATION_LEVEL FROM " + KingdomXiaizationStateTableItem.GetTableName() +
                                  " WHERE KINGDOM_ID=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", pKingdomId);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? LevelNone : Convert.ToInt32(value);
            }
            catch { return LevelNone; }
        }

        private static double GetCityResentment(City pCity)
        {
            if (!Ready || pCity?.data == null) return 0.0;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT RESENTMENT FROM " + CityXiaizationStateTableItem.GetTableName() +
                                  " WHERE CITY_ID=@c LIMIT 1";
                cmd.Parameters.AddWithValue("@c", pCity.id);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0.0 : Convert.ToDouble(value);
            }
            catch { return 0.0; }
        }

        internal static bool IsFullyXiaizedCity(City pCity)
        {
            if (!Ready || pCity?.data == null) return false;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT XIA_PROGRESS FROM " +
                                  CityXiaizationStateTableItem.GetTableName() +
                                  " WHERE CITY_ID=@c LIMIT 1";
                cmd.Parameters.AddWithValue("@c", pCity.id);
                object value = cmd.ExecuteScalar();
                return value != null && value != DBNull.Value &&
                       Convert.ToDouble(value) >= 100.0;
            }
            catch
            {
                return false;
            }
        }

        private static float MaxCityResentment(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null) return 0f;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT COALESCE(MAX(RESENTMENT),0) FROM " +
                                  CityXiaizationStateTableItem.GetTableName() + " WHERE KINGDOM_ID=@k";
                cmd.Parameters.AddWithValue("@k", pKingdom.id);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0f : Convert.ToSingle(value);
            }
            catch { return 0f; }
        }

        private static string ResolveSpecies(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            try { return pKingdom.getActorAsset()?.id ?? pKingdom.asset?.id ?? ""; }
            catch { return pKingdom.asset?.id ?? ""; }
        }

        private static bool IsHumanKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (pKingdom.asset?.id == "human") return true;
            try
            {
                ActorAsset asset = pKingdom.getActorAsset();
                return asset?.id == "human" || asset?.banner_id == "human";
            }
            catch { return false; }
        }

        private static string IdOf(Culture pCulture)
        {
            return pCulture == null ? "" : pCulture.id.ToString();
        }

        private static string IdOf(Language pLanguage)
        {
            return pLanguage == null ? "" : pLanguage.id.ToString();
        }

        private static string LevelLabel(int pLevel)
        {
            switch (pLevel)
            {
                case LevelForeignOccupier:
                    return AW_L10n.Text("aw_xiaization_foreign_entry", "Entered Xia lands");
                case LevelPseudoDynasty:
                    return AW_L10n.Text("aw_xiaization_pseudo_dynasty", "Pseudo dynasty");
                case LevelAdoptedRites:
                    return AW_L10n.Text("aw_xiaization_adopted_rites", "Adopted Xia rites");
                case LevelXiaInstitutions:
                    return AW_L10n.Text("aw_xiaization_institutions", "Xia institutions");
                case LevelXiaizedDynasty:
                    return AW_L10n.Text("aw_xiaization_dynasty", "Xiaized dynasty");
                default:
                    return AW_L10n.Text("aw_xiaization_none", "Not Xiaized");
            }
        }

        private static string LevelEventText(int pLevel)
        {
            switch (pLevel)
            {
                case LevelForeignOccupier:
                    return HistoryLocalizationRules.Text("aw_hist_xiaization_level_foreign");
                case LevelPseudoDynasty:
                    return HistoryLocalizationRules.Text("aw_hist_xiaization_level_pseudo");
                case LevelAdoptedRites:
                    return HistoryLocalizationRules.Text("aw_hist_xiaization_level_rites");
                case LevelXiaInstitutions:
                    return HistoryLocalizationRules.Text("aw_hist_xiaization_level_institutions");
                case LevelXiaizedDynasty:
                    return HistoryLocalizationRules.Text("aw_hist_xiaization_level_dynasty");
                default:
                    return HistoryLocalizationRules.Text("aw_hist_xiaization_level_started");
            }
        }
    }
}
