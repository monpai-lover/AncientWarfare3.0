using System;
using System.Collections.Generic;

namespace AncientWarfare3.content.schools
{
    public readonly struct HistoricalSchoolAbilityProfile
    {
        public HistoricalSchoolAbilityProfile(float pStewardship, float pDiplomacy,
            float pWarfare, float pIntelligence)
        {
            Stewardship = pStewardship;
            Diplomacy = pDiplomacy;
            Warfare = pWarfare;
            Intelligence = pIntelligence;
        }

        public float Stewardship { get; }
        public float Diplomacy { get; }
        public float Warfare { get; }
        public float Intelligence { get; }

        public bool IsValid => IsAbility(Stewardship) && IsAbility(Diplomacy) &&
                               IsAbility(Warfare) && IsAbility(Intelligence);

        private static bool IsAbility(float pValue)
        {
            return pValue >= 0f && pValue <= 100f && !float.IsNaN(pValue) &&
                   !float.IsInfinity(pValue);
        }
    }

    public sealed class HistoricalSchoolMasterDefinition
    {
        public HistoricalSchoolMasterDefinition(int pRegistryIndex, string pId,
            string pCanonicalName, IEnumerable<string> pAliases, string pSchoolId, int pOrder,
            int pWave, IEnumerable<string> pPreferredStateNames, int pSpawnAge, bool pIsMale,
            HistoricalSchoolAbilityProfile pAbilities, IEnumerable<string> pDebateTopics,
            IEnumerable<string> pCanonicalWorks, string pInstitutionId,
            string pActorAssetId = "Xia", string pNameCultureId = "Xia",
            string pCanonicalShiName = "", string pCanonicalGivenName = "",
            string pCanonicalFamilyName = "",
            HistoricalMasterFamilyEvidence pFamilyEvidence =
                HistoricalMasterFamilyEvidence.Unknown,
            bool pMilitaryEligible = false,
            bool pAllowsClanLineage = false)
        {
            RegistryIndex = pRegistryIndex;
            Id = pId ?? "";
            CanonicalName = pCanonicalName ?? "";
            Aliases = Freeze(pAliases);
            SchoolId = pSchoolId ?? "";
            Order = pOrder;
            Wave = pWave;
            PreferredStateNames = Freeze(pPreferredStateNames);
            SpawnAge = pSpawnAge;
            IsMale = pIsMale;
            Abilities = pAbilities;
            DebateTopics = Freeze(pDebateTopics);
            CanonicalWorks = Freeze(pCanonicalWorks);
            InstitutionId = pInstitutionId ?? "";
            ActorAssetId = pActorAssetId ?? "";
            NameCultureId = pNameCultureId ?? "";
            CanonicalShiName = pCanonicalShiName ?? "";
            CanonicalGivenName = pCanonicalGivenName ?? "";
            CanonicalFamilyName = pCanonicalFamilyName ?? "";
            FamilyEvidence = pFamilyEvidence;
            MilitaryEligible = pMilitaryEligible;
            AllowsClanLineage = pAllowsClanLineage;
        }

        public int RegistryIndex { get; }
        public string Id { get; }
        public string CanonicalName { get; }
        public IReadOnlyList<string> Aliases { get; }
        public string SchoolId { get; }
        public int Order { get; }
        public int Wave { get; }
        public IReadOnlyList<string> PreferredStateNames { get; }
        public int SpawnAge { get; }
        public bool IsMale { get; }
        public HistoricalSchoolAbilityProfile Abilities { get; }
        public IReadOnlyList<string> DebateTopics { get; }
        public IReadOnlyList<string> CanonicalWorks { get; }
        public string InstitutionId { get; }
        public string ActorAssetId { get; }
        public string NameCultureId { get; }
        public string CanonicalShiName { get; }
        public string CanonicalGivenName { get; }
        public string CanonicalFamilyName { get; }
        public HistoricalMasterFamilyEvidence FamilyEvidence { get; }
        public bool MilitaryEligible { get; }
        public bool AllowsClanLineage { get; }

        private static IReadOnlyList<string> Freeze(IEnumerable<string> pValues)
        {
            if (pValues == null) return Array.Empty<string>();
            var values = new List<string>();
            foreach (string value in pValues)
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            return values.AsReadOnly();
        }
    }
}
