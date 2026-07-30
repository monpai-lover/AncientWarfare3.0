using System;

namespace AncientWarfare3.core.lineage
{
    public static class FeudatoryJingnanRiskRules
    {
        public const int RevocationRevoltThreshold = 90;
        public const int ProactiveRevoltThreshold = 105;
        public const int MaximumRisk = 120;
        public const int AiRevocationCooldownYears = 12;

        public static bool IsDirectAgnaticAncestor(int rulerDepth,
            int princeDepth)
        {
            return rulerDepth == 0 && princeDepth > 0;
        }

        public static bool ShouldRevoltOnRevocation(int projectedRisk,
            bool rulerIsDirectAgnaticAncestor)
        {
            if (rulerIsDirectAgnaticAncestor) return false;
            return projectedRisk >= RevocationRevoltThreshold;
        }

        public static bool ShouldProactivelyRevolt(int projectedRisk,
            bool rulerIsDirectAgnaticAncestor)
        {
            if (rulerIsDirectAgnaticAncestor) return false;
            return projectedRisk >= ProactiveRevoltThreshold;
        }

        public static bool CanAiAttemptRevocation(int projectedRisk,
            bool rulerIsDirectAgnaticAncestor)
        {
            return rulerIsDirectAgnaticAncestor ||
                   projectedRisk < RevocationRevoltThreshold;
        }

        public static bool ShouldAiConsiderRevocation(int currentYear,
            int lastActionYear, bool realmAtWar, int autonomy, int loyalty)
        {
            if (realmAtWar || autonomy < 70 || loyalty > 30) return false;
            return lastActionYear < 0 ||
                   currentYear - lastActionYear >= AiRevocationCooldownYears;
        }

        public static FeudatoryRevocationAction SelectAiRevocationAction(
            int autonomy, int loyalty, int cityCount,
            bool relocationAvailable)
        {
            if (relocationAvailable)
                return FeudatoryRevocationAction.Relocate;
            if (cityCount > 1)
                return FeudatoryRevocationAction.ReclaimCity;
            return autonomy >= 90 && loyalty <= 10
                ? FeudatoryRevocationAction.Abolish
                : FeudatoryRevocationAction.None;
        }

        // Positive values deter rebellion. Negative values embolden the prince.
        public static int KinshipDeterrence(int generationDelta,
            bool rulerOlderSameGeneration)
        {
            if (generationDelta > 0)
                return Math.Min(16, generationDelta * 8);
            if (generationDelta < 0)
                return -Math.Min(12, -generationDelta * 6);
            return rulerOlderSameGeneration ? 4 : -3;
        }

        public static int AbilityDeterrence(float warfare, float diplomacy,
            float stewardship)
        {
            float weighted = Math.Max(0f, warfare) * 0.40f +
                             Math.Max(0f, diplomacy) * 0.35f +
                             Math.Max(0f, stewardship) * 0.25f;
            if (weighted >= 24f) return 22;
            if (weighted >= 18f) return 16;
            if (weighted >= 13f) return 9;
            if (weighted >= 9f) return 3;
            return -8;
        }

        public static int PersonalityAmbition(int baseAmbition,
            bool ambitious, bool content, bool greedy, bool deceitful)
        {
            int ambition = Clamp(baseAmbition, 0, 100);
            if (ambitious) ambition += 30;
            if (content) ambition -= 20;
            if (greedy) ambition += 8;
            if (deceitful) ambition += 10;
            return Clamp(ambition, 0, 100);
        }

        public static int LegitimacyDeterrence(int mandateValue,
            int imperialAuthority)
        {
            int deterrence = mandateValue >= 80 ? 8 :
                mandateValue >= 60 ? 5 :
                mandateValue >= 40 ? 2 :
                mandateValue < 0 ? -10 : -5;
            deterrence += imperialAuthority >= 80 ? 12 :
                imperialAuthority >= 60 ? 8 :
                imperialAuthority >= 40 ? 4 :
                imperialAuthority < 25 ? -10 : 0;
            return deterrence;
        }

        public static int CentralCrisis(bool hasRuler, bool adultRuler,
            int rulerAge, bool successionUnstable, int ministerialPower,
            bool capitalThreatened)
        {
            if (!hasRuler) return 35;
            int crisis = !adultRuler ? 20 : rulerAge >= 75 ? 8 : 0;
            if (successionUnstable) crisis += 15;
            crisis += ministerialPower >= 80 ? 20 :
                ministerialPower >= 60 ? 12 :
                ministerialPower >= 40 ? 6 : 0;
            if (capitalThreatened) crisis += 12;
            return Clamp(crisis, 0, 60);
        }

        public static int GarrisonThreat(int garrisonSize,
            int centralWarriors)
        {
            int garrison = Math.Max(0, garrisonSize);
            int central = Math.Max(0, centralWarriors);
            if (garrison == 0) return 0;
            if (central == 0) return 25;
            double share = (double)garrison / central;
            if (share >= 0.50d) return 25;
            if (share >= 0.30d) return 18;
            if (share >= 0.15d) return 10;
            return 0;
        }

        public static int CalculateRisk(int ambition, int loyalty,
            int autonomy, int garrisonSize, int centralWarriors,
            int revocationIntensity, int centralCrisis,
            int rulerDeterrence)
        {
            int normalizedAmbition = Clamp(ambition, 0, 100);
            int normalizedLoyalty = Clamp(loyalty, 0, 100);
            int normalizedAutonomy = Clamp(autonomy, 0, 100);
            int normalizedIntensity = Math.Max(0, revocationIntensity);
            int risk = normalizedAmbition - normalizedLoyalty +
                       normalizedAutonomy / 2 +
                       GarrisonThreat(garrisonSize, centralWarriors) +
                       normalizedIntensity + centralCrisis -
                       rulerDeterrence;
            return Clamp(risk, 0, MaximumRisk);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
