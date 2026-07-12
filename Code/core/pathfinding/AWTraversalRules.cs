// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;

namespace AncientWarfare3.core.pathfinding
{
    public static class AWTraversalRules
    {
        public static bool CanEnter(AWTileTraversalSnapshot pTile,
            AWActorTraversalProfile pActor, AWPathRequestOptions pOptions)
        {
            if (!pTile.Exists) return false;
            if (pActor.CanFly) return true;
            if (pActor.IsBoat) return pTile.GoodForBoat || pTile.Ocean;
            if (pTile.Block && !pOptions.WalkOnBlocks) return false;
            if (pTile.Lava && pActor.DiesInLava && !pOptions.WalkOnLava) return false;
            if (!pTile.Ocean && !pTile.Liquid) return pTile.Ground || pTile.Block || pTile.Lava;
            if (pActor.IsWaterCreature) return true;
            if (pActor.StartsInLiquid) return true;
            return pOptions.PathOnWater && !pActor.DamagedByOcean;
        }

        public static AWTraversalEstimate Estimate(AWTileTraversalSnapshot pFrom,
            AWTileTraversalSnapshot pTo, AWActorTraversalProfile pActor,
            AWPathRequestOptions pOptions, AWPathfindingConfig pConfig = null)
        {
            AWPathfindingConfig config = pConfig ?? AWPathfindingConfig.Default;
            if (!CanEnter(pTo, pActor, pOptions))
                return new AWTraversalEstimate(float.PositiveInfinity, 0f, pActor.Health,
                    float.PositiveInfinity, AWHazardFlags.None);

            float distance = Distance(pFrom.X, pFrom.Y, pTo.X, pTo.Y);
            bool water = pTo.Ocean || pTo.Liquid;
            AWMovementMethod method = pActor.IsBoat
                ? AWMovementMethod.Sail
                : water ? AWMovementMethod.Swim : AWMovementMethod.Walk;
            float scale = method == AWMovementMethod.Sail
                ? config.SailSpeedScale
                : method == AWMovementMethod.Swim ? config.SwimSpeedScale : config.WalkSpeedScale;
            float speed = Math.Max(0.01f, pActor.MovementSpeed * scale * pTo.WalkMultiplier);
            float time = distance / speed;
            float stamina = 0f;
            float health = 0f;
            float risk = 0f;
            AWHazardFlags hazards = AWHazardFlags.None;

            if (pTo.Block)
            {
                hazards |= AWHazardFlags.Block;
                risk += config.BlockRiskCost;
            }
            if (pTo.Lava)
            {
                hazards |= AWHazardFlags.Lava;
                risk += config.LavaRiskCost;
            }
            if (pTo.Fire && !pActor.ImmuneToFire && !pActor.Burning)
            {
                hazards |= AWHazardFlags.Fire;
                risk += config.FireRiskCost;
            }
            if (pTo.DamageUnits && pTo.TerrainDamage > 0f)
            {
                hazards |= AWHazardFlags.TerrainDamage;
                health += pTo.TerrainDamage * config.DamageUnitsTicksPerSecond * time;
                risk += config.TerrainDamageRiskCost;
            }
            if (water && !pActor.IsBoat && !pActor.IsWaterCreature)
            {
                hazards |= AWHazardFlags.Ocean | AWHazardFlags.StaminaDrain;
                stamina += Math.Max(0f,
                    config.WaterStaminaDrainPerSecond - pActor.StaminaRegeneration) * time;
                risk += config.OceanRiskCost;
                if (stamina > pActor.Stamina)
                {
                    hazards |= AWHazardFlags.Drowning;
                    float exhaustedSeconds = (stamina - pActor.Stamina) /
                                             Math.Max(0.01f, config.WaterStaminaDrainPerSecond);
                    health += (pActor.WaterDamage > 0f
                        ? pActor.WaterDamage
                        : config.DrowningDamagePerSecond) * exhaustedSeconds;
                }
            }
            if (health >= pActor.Health)
            {
                hazards |= AWHazardFlags.LowHealth;
                risk += config.DeathRiskCost;
            }
            else if (pActor.Health - health < pActor.MaxHealth * 0.2f)
            {
                hazards |= AWHazardFlags.LowHealth;
                risk += config.LowHealthRiskCost;
            }

            risk += stamina * config.StaminaCostWeight + health * config.HealthCostWeight;
            return new AWTraversalEstimate(time, stamina, health, risk, hazards);
        }

        public static float Distance(int pX1, int pY1, int pX2, int pY2)
        {
            long dx = pX2 - pX1;
            long dy = pY2 - pY1;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static bool Dominates(float timeA, float staminaA, float healthA, float riskA,
            float timeB, float staminaB, float healthB, float riskB)
        {
            bool noWorse = timeA <= timeB && staminaA <= staminaB &&
                           healthA <= healthB && riskA <= riskB;
            bool better = timeA < timeB || staminaA < staminaB ||
                          healthA < healthB || riskA < riskB;
            return noWorse && better;
        }

        public static bool IsInsideFallbackCorridor(int pX, int pY, int pStartX, int pStartY,
            int pTargetX, int pTargetY, float pMaximumDetour)
        {
            float direct = Distance(pStartX, pStartY, pTargetX, pTargetY);
            float via = Distance(pStartX, pStartY, pX, pY) +
                        Distance(pX, pY, pTargetX, pTargetY);
            return via <= direct + Math.Max(0f, pMaximumDetour);
        }
    }
}
