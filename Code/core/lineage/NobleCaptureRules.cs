using System;

namespace AncientWarfare3.core.lineage
{
    public static class NobleCaptureRules
    {
        public const float NobleChanceMultiplier = 0.25f;

        public static float ResolveChance(float baseChance,
            float captorBonus, bool noble)
        {
            float chance = Math.Max(0f, baseChance + captorBonus);
            if (noble) chance *= NobleChanceMultiplier;
            return Math.Min(0.95f, chance);
        }
    }
}
