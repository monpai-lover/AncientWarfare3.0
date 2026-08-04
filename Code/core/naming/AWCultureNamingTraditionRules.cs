using System;

namespace AncientWarfare3.core.naming
{
    public enum AWNamingObjectKind
    {
        Actor,
        Alliance,
        Book,
        City,
        Clan,
        Culture,
        Item,
        Kingdom,
        Language,
        Religion,
        Subspecies,
        War
    }

    public static class AWCultureNamingTraditionRules
    {
        private const string WesternTraditionSeed =
            "aw_western_naming_tradition";

        public static WesternNamingTradition SelectWesternTradition(
            long pStableCultureId)
        {
            long seed = AWNamingSeedRules.Combine(pStableCultureId, 0L,
                WesternTraditionSeed, 1);
            int index = (int)((ulong)seed % 4UL);
            return (WesternNamingTradition)index;
        }

        public static bool IsWesternTradition(
            WesternNamingTradition pTradition)
        {
            return pTradition >= WesternNamingTradition.Von &&
                   pTradition <= WesternNamingTradition.Di;
        }

        public static bool ShouldPersistWesternTradition(
            NamingProfileId pProfile)
        {
            return pProfile == NamingProfileId.Western;
        }

        public static NamingProfileId ResolveEffectiveProfile(
            NamingProfileId pNaturalProfile,
            NamingProfileId pPersistedProfile, bool fullyXiaized)
        {
            if (pNaturalProfile == NamingProfileId.None)
                return NamingProfileId.None;
            if (pNaturalProfile == NamingProfileId.Xia ||
                pNaturalProfile == NamingProfileId.Monkey)
                return pNaturalProfile;
            if (fullyXiaized || pPersistedProfile == NamingProfileId.Xia)
                return NamingProfileId.Xia;
            if (pNaturalProfile == NamingProfileId.OrcNomadic)
                return NamingProfileId.OrcNomadic;
            return pPersistedProfile == NamingProfileId.None
                ? pNaturalProfile
                : pPersistedProfile;
        }

        public static NamingProfileId ResolveInheritedProfile(
            NamingProfileId pChildNaturalProfile,
            NamingProfileId pParentProfile, bool fullyXiaized)
        {
            if (pChildNaturalProfile == NamingProfileId.None)
                return NamingProfileId.None;
            if (pChildNaturalProfile == NamingProfileId.Xia ||
                pChildNaturalProfile == NamingProfileId.Monkey)
                return pChildNaturalProfile;
            if (fullyXiaized || pParentProfile == NamingProfileId.Xia)
                return NamingProfileId.Xia;
            if (pChildNaturalProfile == NamingProfileId.OrcNomadic)
                return NamingProfileId.OrcNomadic;
            return pParentProfile == NamingProfileId.None
                ? pChildNaturalProfile
                : pParentProfile;
        }

        public static NamingProfileId ResolveActorProfile(
            NamingProfileId pNaturalProfile,
            NamingProfileId pCultureProfile)
        {
            if (pNaturalProfile == NamingProfileId.None)
                return NamingProfileId.None;
            if (pNaturalProfile == NamingProfileId.Xia ||
                pNaturalProfile == NamingProfileId.Monkey)
                return pNaturalProfile;
            if (pCultureProfile == NamingProfileId.Xia)
                return NamingProfileId.Xia;
            if (pNaturalProfile == NamingProfileId.OrcNomadic)
                return NamingProfileId.OrcNomadic;
            return pCultureProfile == NamingProfileId.None
                ? pNaturalProfile
                : pCultureProfile;
        }

        public static WesternNamingTradition ResolveInheritedTradition(
            NamingProfileId pParentProfile,
            WesternNamingTradition pParentTradition,
            long stableCultureId)
        {
            return pParentProfile == NamingProfileId.Western &&
                   IsWesternTradition(pParentTradition)
                ? pParentTradition
                : SelectWesternTradition(stableCultureId);
        }

        public static WesternNamingTradition ResolvePersistedTradition(
            string pPersistedTradition,
            WesternNamingTradition? pInheritedTradition,
            long stableCultureId)
        {
            if (pInheritedTradition.HasValue &&
                IsWesternTradition(pInheritedTradition.Value))
                return pInheritedTradition.Value;
            return ParseTradition(pPersistedTradition,
                SelectWesternTradition(stableCultureId));
        }

        public static string SerializeProfile(NamingProfileId pProfile)
        {
            return pProfile switch
            {
                NamingProfileId.Xia => "xia",
                NamingProfileId.Monkey => "monkey",
                NamingProfileId.OrcNomadic => "orc_nomadic",
                NamingProfileId.Western => "western",
                _ => string.Empty
            };
        }

        public static NamingProfileId ParseProfile(string pValue)
        {
            return (pValue ?? string.Empty).Trim() switch
            {
                "xia" => NamingProfileId.Xia,
                "monkey" => NamingProfileId.Monkey,
                "orc_nomadic" => NamingProfileId.OrcNomadic,
                "western" => NamingProfileId.Western,
                _ => NamingProfileId.None
            };
        }

        public static string SerializeTradition(
            WesternNamingTradition pTradition)
        {
            return pTradition switch
            {
                WesternNamingTradition.Von => "von",
                WesternNamingTradition.De => "de",
                WesternNamingTradition.Van => "van",
                WesternNamingTradition.Di => "di",
                _ => string.Empty
            };
        }

        public static WesternNamingTradition ParseTradition(string pValue,
            WesternNamingTradition pFallback)
        {
            return (pValue ?? string.Empty).Trim() switch
            {
                "von" => WesternNamingTradition.Von,
                "de" => WesternNamingTradition.De,
                "van" => WesternNamingTradition.Van,
                "di" => WesternNamingTradition.Di,
                _ => pFallback
            };
        }

        public static string ResolveGeneratorId(NamingProfileId pProfile,
            WesternNamingTradition pTradition, AWNamingObjectKind pKind,
            string pSpeciesId, string pExplicitGeneratorId)
        {
            if (pKind == AWNamingObjectKind.Book ||
                pKind == AWNamingObjectKind.Item ||
                pKind == AWNamingObjectKind.War)
                return pExplicitGeneratorId ?? string.Empty;

            if (pKind == AWNamingObjectKind.Actor &&
                pProfile == NamingProfileId.Xia &&
                !IsXiaSpecies(pSpeciesId))
                return ResolveNativeActorGenerator(pSpeciesId, pTradition,
                    pExplicitGeneratorId);
            if (pProfile == NamingProfileId.Western &&
                !IsHumanSpecies(pSpeciesId))
                return ResolveNativeGenerator(pKind, pSpeciesId, pTradition,
                    pExplicitGeneratorId);

            switch (pProfile)
            {
                case NamingProfileId.OrcNomadic:
                    return AWOrcNomadicNamingRules.ResolveGeneratorId(pKind);
                case NamingProfileId.Western:
                    return ResolveWesternGenerator(pTradition, pKind,
                        pSpeciesId, pExplicitGeneratorId);
                case NamingProfileId.Xia:
                    return ResolveXiaGenerator(pKind, pExplicitGeneratorId);
                case NamingProfileId.Monkey:
                    return ResolveMonkeyGenerator(pKind,
                        pExplicitGeneratorId);
                case NamingProfileId.None:
                default:
                    return pExplicitGeneratorId ?? string.Empty;
            }
        }

        public static string ResolveFallbackGeneratorId(
            NamingProfileId pProfile, AWNamingObjectKind pKind,
            string pSpeciesId, string pExplicitGeneratorId)
        {
            if (pKind == AWNamingObjectKind.Book ||
                pKind == AWNamingObjectKind.Item ||
                pKind == AWNamingObjectKind.War)
                return pExplicitGeneratorId ?? string.Empty;
            if (pKind == AWNamingObjectKind.Actor &&
                pProfile == NamingProfileId.Xia &&
                !IsXiaSpecies(pSpeciesId))
                return ResolveNativeActorGenerator(pSpeciesId,
                    WesternNamingTradition.Von, pExplicitGeneratorId);
            if (pProfile == NamingProfileId.Western &&
                !IsHumanSpecies(pSpeciesId))
                return ResolveNativeGenerator(pKind, pSpeciesId,
                    WesternNamingTradition.Von, pExplicitGeneratorId);
            if (pProfile == NamingProfileId.OrcNomadic)
                return AWOrcNomadicNamingRules.ResolveFallbackGeneratorId(
                    pKind);
            if (pProfile == NamingProfileId.Xia)
                return ResolveXiaGenerator(pKind, pExplicitGeneratorId);
            if (pProfile == NamingProfileId.Monkey)
                return ResolveMonkeyGenerator(pKind, pExplicitGeneratorId);
            if (pProfile != NamingProfileId.Western)
                return pExplicitGeneratorId ?? string.Empty;
            if (pKind == AWNamingObjectKind.Actor)
            {
                if (UsesSpeciesGivenNameGenerator(pSpeciesId))
                    return pSpeciesId + "_given_name";
                if (!string.IsNullOrWhiteSpace(pExplicitGeneratorId))
                    return pExplicitGeneratorId;
            }

            return pKind switch
            {
                AWNamingObjectKind.Actor => "human_name",
                AWNamingObjectKind.Alliance => "alliance_name",
                AWNamingObjectKind.City => "human_city",
                AWNamingObjectKind.Clan => "human_clan",
                AWNamingObjectKind.Culture => "human_culture",
                AWNamingObjectKind.Kingdom => "human_kingdom",
                AWNamingObjectKind.Language => "human_lang",
                AWNamingObjectKind.Religion => "human_religion",
                AWNamingObjectKind.Subspecies => "default_species",
                _ => string.Empty
            };
        }

        public static string ResolveAvailableGeneratorId(string pSelected,
            bool pSelectedAvailable, string pFallback,
            bool pFallbackAvailable)
        {
            if (pSelectedAvailable && !string.IsNullOrWhiteSpace(pSelected))
                return pSelected;
            if (pFallbackAvailable && !string.IsNullOrWhiteSpace(pFallback))
                return pFallback;
            return string.Empty;
        }

        private static string ResolveXiaGenerator(AWNamingObjectKind pKind,
            string pExplicitGeneratorId)
        {
            return pKind switch
            {
                AWNamingObjectKind.Actor => "Xia_name",
                AWNamingObjectKind.Alliance => "Xia_alliance",
                AWNamingObjectKind.City => "Xia_city",
                AWNamingObjectKind.Clan => "Xia_clan",
                AWNamingObjectKind.Culture => "Xia_culture",
                AWNamingObjectKind.Kingdom => "Xia_kingdom",
                AWNamingObjectKind.Language => "Xia_language",
                AWNamingObjectKind.Religion => "Xia_religion",
                AWNamingObjectKind.Subspecies => "Xia_subspecies",
                _ => pExplicitGeneratorId ?? string.Empty
            };
        }

        private static string ResolveMonkeyGenerator(AWNamingObjectKind pKind,
            string pExplicitGeneratorId)
        {
            return pKind switch
            {
                AWNamingObjectKind.Actor => "civ_monkey_name",
                AWNamingObjectKind.City => "civ_monkey_city",
                AWNamingObjectKind.Clan => "civ_monkey_clan",
                AWNamingObjectKind.Kingdom => "civ_monkey_kingdom",
                _ => pExplicitGeneratorId ?? string.Empty
            };
        }

        private static string ResolveWesternGenerator(
            WesternNamingTradition pTradition, AWNamingObjectKind pKind,
            string pSpeciesId, string pExplicitGeneratorId)
        {
            if (pKind == AWNamingObjectKind.Actor)
                return "western_" + SerializeTradition(pTradition) +
                       "_name";

            return pKind switch
            {
                AWNamingObjectKind.Alliance => "western_alliance",
                AWNamingObjectKind.City => "western_city",
                AWNamingObjectKind.Clan => "western_clan",
                AWNamingObjectKind.Culture => "western_culture",
                AWNamingObjectKind.Kingdom => "western_kingdom",
                AWNamingObjectKind.Language => "western_language",
                AWNamingObjectKind.Religion => "western_religion",
                AWNamingObjectKind.Subspecies => "western_subspecies",
                _ => pExplicitGeneratorId ?? string.Empty
            };
        }

        private static string ResolveNativeGenerator(AWNamingObjectKind pKind,
            string pSpeciesId, WesternNamingTradition pTradition,
            string pExplicitGeneratorId)
        {
            return pKind == AWNamingObjectKind.Actor
                ? ResolveNativeActorGenerator(pSpeciesId, pTradition,
                    pExplicitGeneratorId)
                : pExplicitGeneratorId ?? string.Empty;
        }

        private static string ResolveNativeActorGenerator(string pSpeciesId,
            WesternNamingTradition pTradition, string pExplicitGeneratorId)
        {
            if (IsHumanSpecies(pSpeciesId))
                return "western_" + SerializeTradition(pTradition) +
                       "_name";
            if (UsesSpeciesGivenNameGenerator(pSpeciesId))
                return pSpeciesId + "_given_name";
            if (string.Equals(pSpeciesId, "orc", StringComparison.Ordinal))
                return AWOrcNomadicNamingRules.ResolveGeneratorId(
                    AWNamingObjectKind.Actor);
            return pExplicitGeneratorId ?? string.Empty;
        }

        private static bool IsHumanSpecies(string pSpeciesId)
        {
            return string.Equals(pSpeciesId, "human",
                StringComparison.Ordinal);
        }

        private static bool IsXiaSpecies(string pSpeciesId)
        {
            return string.Equals(pSpeciesId, "Xia",
                StringComparison.Ordinal);
        }

        private static bool UsesSpeciesGivenNameGenerator(string pSpeciesId)
        {
            return string.Equals(pSpeciesId, "elf",
                       StringComparison.Ordinal) ||
                   string.Equals(pSpeciesId, "dwarf",
                       StringComparison.Ordinal);
        }
    }
}
