using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class ArmyOperationalThresholds
    {
        public const int LowSupply = 30;
        public const int CriticalSupply = 10;
        public const int RetreatOrganization = 25;
        public const int RegroupOrganization = 60;
        public const int FullRegroupRecovery = 12;
        public const int LowSupplyRegroupRecovery = 6;
    }

    public sealed class ArmyOperationalDirectorFacts
    {
        public ArmyOperationalDirectorFacts(int unitCount, int supply,
            int organization)
        {
            UnitCount = Math.Max(0, unitCount);
            Supply = Math.Max(0, Math.Min(100, supply));
            Organization = Math.Max(0, Math.Min(100, organization));
        }

        public int UnitCount { get; }
        public int Supply { get; }
        public int Organization { get; }
    }

    public readonly struct ArmyOperationalDirectorProjection
    {
        public ArmyOperationalDirectorProjection(int pEffectiveForce,
            bool pGoodSupply, bool pLowSupply, bool pPoorOrganization,
            int pRegroupRecovery)
        {
            EffectiveForce = Math.Max(0, pEffectiveForce);
            GoodSupply = pGoodSupply;
            LowSupply = pLowSupply;
            PoorOrganization = pPoorOrganization;
            RegroupRecovery = Math.Max(0, pRegroupRecovery);
        }

        public int EffectiveForce { get; }
        public bool GoodSupply { get; }
        public bool LowSupply { get; }
        public bool PoorOrganization { get; }
        public int RegroupRecovery { get; }
    }

    public static class ArmyOperationalDirectorRules
    {
        public static ArmyOperationalDirectorProjection Project(
            ArmyOperationalDirectorFacts pFacts)
        {
            if (pFacts == null)
                return new ArmyOperationalDirectorProjection(0, false,
                    true, true, 0);
            int effectiveSupply = ArmyLogisticsRules.EffectiveSupply(
                pFacts.Supply);
            int supplyFactor = effectiveSupply <=
                               ArmyOperationalThresholds.CriticalSupply
                ? 50
                : effectiveSupply <= ArmyOperationalThresholds.LowSupply
                    ? 75
                    : 100;
            int organizationFactor = pFacts.Organization <=
                                     ArmyOperationalThresholds.
                                         RetreatOrganization
                ? 50
                : pFacts.Organization < ArmyOperationalThresholds.
                    RegroupOrganization
                    ? 75
                    : 100;
            long scaled = (long)pFacts.UnitCount * supplyFactor *
                          organizationFactor;
            int effective = pFacts.UnitCount <
                            ArmyLogisticsRules.MinimumOperationalForce
                ? 0
                : scaled >= int.MaxValue * 10_000L
                    ? int.MaxValue
                    : (int)(scaled / 10_000L);
            bool lowSupply = effectiveSupply <=
                             ArmyOperationalThresholds.LowSupply;
            bool poorOrganization = pFacts.Organization <
                                    ArmyOperationalThresholds.
                                        RegroupOrganization;
            return new ArmyOperationalDirectorProjection(effective,
                pGoodSupply: !lowSupply,
                pLowSupply: lowSupply,
                pPoorOrganization: poorOrganization,
                pRegroupRecovery: RegroupRecoveryForSupply(
                    effectiveSupply));
        }

        public static int RegroupRecoveryForSupply(int supply)
        {
            int effectiveSupply = ArmyLogisticsRules.EffectiveSupply(supply);
            if (effectiveSupply <=
                ArmyOperationalThresholds.CriticalSupply)
                return 0;
            return effectiveSupply <= ArmyOperationalThresholds.LowSupply
                ? ArmyOperationalThresholds.LowSupplyRegroupRecovery
                : ArmyOperationalThresholds.FullRegroupRecovery;
        }
    }

    public sealed class ArmyOrganizationFacts
    {
        public int CurrentOrganization { get; set; } = 100;
        public int RecentCasualties { get; set; }
        public bool CaptainLost { get; set; }
        public int Supply { get; set; } = 100;
        public bool Regrouping { get; set; }
        public bool NearbySupport { get; set; }
        public bool UninterruptedMarch { get; set; }
    }

    public sealed class ArmyPursuitFacts
    {
        public double ElapsedTime { get; set; }
        public double DistanceTiles { get; set; }
        public bool InCorridor { get; set; } = true;
        public int Supply { get; set; } = 100;
        public bool NeedsRegroup { get; set; }
        public bool RouteArrived { get; set; }
    }

    public sealed class ArmyConnectivityFacts
    {
        public bool MissionConnected { get; set; }
        public bool CurrentCityInCorridor { get; set; }
        public bool NearRouteAnchor { get; set; }
        public bool FriendlySupplyCity { get; set; }
        public bool AlliedSupplyCity { get; set; }
        public bool FrozenControlledSupplyCity { get; set; }
    }

    public readonly struct ArmyConnectivityResult
    {
        public ArmyConnectivityResult(bool pConnectedSupply,
            bool pInCorridor)
        {
            ConnectedSupply = pConnectedSupply;
            InCorridor = pInCorridor;
        }

        public bool ConnectedSupply { get; }
        public bool InCorridor { get; }
    }

    public readonly struct ArmyPursuitEndpointCandidate
    {
        public ArmyPursuitEndpointCandidate(int pTileId,
            double pDistanceTiles, bool sameIsland, bool inCorridor)
        {
            TileId = pTileId;
            DistanceTiles = Math.Max(0d, pDistanceTiles);
            SameIsland = sameIsland;
            InCorridor = inCorridor;
        }

        public int TileId { get; }
        public double DistanceTiles { get; }
        public bool SameIsland { get; }
        public bool InCorridor { get; }
    }

    public sealed class ArmyLogisticsControllerSample
    {
        public long ArmyId { get; set; } = -1L;
        public long KingdomId { get; set; } = -1L;
        public long WarId { get; set; } = -1L;
        public long CurrentCityId { get; set; } = -1L;
        public long CurrentCityKingdomId { get; set; } = -1L;
        public int CurrentTileId { get; set; } = -1;
        public bool CurrentCitySafe { get; set; }
        public bool NearRouteAnchor { get; set; }
        public int Living { get; set; }
        public int Rallied { get; set; }
    }

    public sealed class ArmyPursuitRouteState
    {
        public int StartTileId { get; private set; } = -1;
        public int EndpointTileId { get; private set; } = -1;
        public double StartTime { get; private set; } = -1d;
        public bool Active { get; private set; }
        public bool Completed { get; private set; }

        public bool TryBegin(int startTileId, double startTime,
            IReadOnlyList<ArmyPursuitEndpointCandidate> pCandidates)
        {
            if (Completed || Active || startTileId < 0 ||
                double.IsNaN(startTime) ||
                double.IsInfinity(startTime)) return false;
            int endpoint = ArmyLogisticsRules.SelectPursuitEndpoint(
                pCandidates);
            if (endpoint < 0) return false;
            StartTileId = startTileId;
            EndpointTileId = endpoint;
            StartTime = Math.Max(0d, startTime);
            Active = true;
            return true;
        }

        public void Complete()
        {
            Active = false;
            Completed = true;
        }

        public bool ReplaceEndpoint(int endpointTileId)
        {
            if (!Active || Completed || endpointTileId < 0) return false;
            EndpointTileId = endpointTileId;
            return true;
        }

        public void Reset()
        {
            StartTileId = -1;
            EndpointTileId = -1;
            StartTime = -1d;
            Active = false;
            Completed = false;
        }
    }

    public static class ArmyLogisticsRules
    {
        public static bool SupplySimulationEnabled => false;
        public const double WorldTimePerLogisticsPeriod = 5d;
        public const int MinimumSupply = 0;
        public const int MaximumSupply = 100;
        public const int LowSupply = ArmyOperationalThresholds.LowSupply;
        public const int CriticalSupply =
            ArmyOperationalThresholds.CriticalSupply;
        public const int ConnectedRecovery = 12;
        public const int MarchSupplyCost = -1;
        public const int PursuitSupplyCost = -2;
        public const int AssaultSupplyCost = -3;
        public const int IsolatedSupplyPenalty = -2;
        public const int OrganizationPerCasualty = -5;
        public const int CaptainLossOrganization = -20;
        public const int CriticalSupplyOrganization = -10;
        public const int RegroupOrganizationRecovery =
            ArmyOperationalThresholds.FullRegroupRecovery;
        public const int NearbySupportOrganizationRecovery = 4;
        public const int MarchOrganizationRecovery = 1;
        public const int RetreatOrganization =
            ArmyOperationalThresholds.RetreatOrganization;
        public const int RegroupOrganization =
            ArmyOperationalThresholds.RegroupOrganization;
        public const double PursuitTimeBudget = 10d;
        public const double PursuitDistanceBudget = 12d;
        public const int MinimumOperationalForce = 2;

        public static int EffectiveSupply(int observedSupply)
        {
            return SupplySimulationEnabled
                ? Math.Max(MinimumSupply,
                    Math.Min(MaximumSupply, observedSupply))
                : MaximumSupply;
        }

        public static bool EffectiveSupplyConnection(
            bool observedConnection)
        {
            return !SupplySimulationEnabled || observedConnection;
        }

        public static long LogisticsPeriodForWorldTime(double pWorldTime)
        {
            if (double.IsNaN(pWorldTime) || pWorldTime <= 0d) return 0L;
            if (double.IsPositiveInfinity(pWorldTime)) return long.MaxValue;
            double period = Math.Floor(pWorldTime /
                                       WorldTimePerLogisticsPeriod);
            return period >= long.MaxValue ? long.MaxValue : (long)period;
        }

        public static int StateSupplyDelta(ArmyRtsState pState)
        {
            return pState switch
            {
                ArmyRtsState.March => MarchSupplyCost,
                ArmyRtsState.Pursue => PursuitSupplyCost,
                ArmyRtsState.Assault => AssaultSupplyCost,
                _ => 0
            };
        }

        public static int UpdateSupply(int pCurrentSupply,
            ArmyRtsState pState, bool connectedSupply, bool inCorridor,
            bool strategicMovementProgressed = true)
        {
            if (!SupplySimulationEnabled) return MaximumSupply;
            int delta = StateSupplyDelta(pState);
            if (pState == ArmyRtsState.March &&
                !strategicMovementProgressed)
                delta = 0;
            if (connectedSupply) delta += ConnectedRecovery;
            if (!inCorridor) delta += IsolatedSupplyPenalty;
            long value = (long)pCurrentSupply + delta;
            if (value <= MinimumSupply) return MinimumSupply;
            return value >= MaximumSupply ? MaximumSupply : (int)value;
        }

        public static int UpdateOrganization(ArmyOrganizationFacts pFacts)
        {
            if (pFacts == null) return MaximumSupply;
            long delta = (long)Math.Max(0, pFacts.RecentCasualties) *
                         OrganizationPerCasualty;
            if (pFacts.CaptainLost) delta += CaptainLossOrganization;
            int effectiveSupply = EffectiveSupply(pFacts.Supply);
            if (effectiveSupply <= CriticalSupply)
                delta += CriticalSupplyOrganization;
            if (pFacts.Regrouping)
                delta += ArmyOperationalDirectorRules.
                    RegroupRecoveryForSupply(effectiveSupply);
            if (pFacts.NearbySupport)
                delta += NearbySupportOrganizationRecovery;
            if (pFacts.UninterruptedMarch)
                delta += MarchOrganizationRecovery;
            long value = pFacts.CurrentOrganization + delta;
            if (value <= 0L) return 0;
            return value >= 100L ? 100 : (int)value;
        }

        public static bool ShouldRetreat(int organization,
            bool capitalSurvivalException)
        {
            return organization <= RetreatOrganization &&
                   !capitalSurvivalException;
        }

        public static bool HasMinimumOperationalForce(int living)
        {
            return living >= MinimumOperationalForce;
        }

        public static bool IsAlliedSupplyCity(bool sameWarSide,
            bool currentCitySafe)
        {
            return sameWarSide && currentCitySafe;
        }

        public static bool CanCompleteRegroup(int organization, int supply,
            bool minimumForceReady = true)
        {
            return organization >= RegroupOrganization &&
                   EffectiveSupply(supply) > CriticalSupply &&
                   minimumForceReady;
        }

        public static ArmyConnectivityResult ResolveConnectivity(
            ArmyConnectivityFacts pFacts)
        {
            if (pFacts == null)
                return new ArmyConnectivityResult(false, false);
            bool directSupplyAnchor = pFacts.FriendlySupplyCity ||
                                      pFacts.AlliedSupplyCity ||
                                      pFacts.FrozenControlledSupplyCity;
            // The director has already verified the mission's territorial
            // corridor. Do not lose that result merely because a marching
            // Army is between city zones or route waypoints this period.
            bool inCorridor = directSupplyAnchor ||
                              pFacts.MissionConnected ||
                              pFacts.CurrentCityInCorridor ||
                              pFacts.NearRouteAnchor;
            return new ArmyConnectivityResult(inCorridor &&
                                                directSupplyAnchor,
                inCorridor);
        }

        public static ArmyRtsState ResolvePursuit(
            ArmyPursuitFacts pFacts)
        {
            if (pFacts == null) return ArmyRtsState.Hold;
            if (EffectiveSupply(pFacts.Supply) <= CriticalSupply ||
                pFacts.NeedsRegroup)
                return ArmyRtsState.Regroup;
            if (pFacts.RouteArrived ||
                !EffectiveSupplyConnection(pFacts.InCorridor) ||
                pFacts.ElapsedTime >= PursuitTimeBudget ||
                pFacts.DistanceTiles >= PursuitDistanceBudget)
                return ArmyRtsState.Hold;
            return ArmyRtsState.Pursue;
        }

        public static int SelectPursuitEndpoint(
            IReadOnlyList<ArmyPursuitEndpointCandidate> pCandidates)
        {
            if (pCandidates == null) return -1;
            int selected = -1;
            double selectedDistance = -1d;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                ArmyPursuitEndpointCandidate candidate = pCandidates[i];
                if (candidate.TileId < 0 || !candidate.SameIsland ||
                    !EffectiveSupplyConnection(candidate.InCorridor) ||
                    candidate.DistanceTiles <= 0d ||
                    candidate.DistanceTiles > PursuitDistanceBudget)
                    continue;
                if (candidate.DistanceTiles < selectedDistance ||
                    candidate.DistanceTiles == selectedDistance &&
                    candidate.TileId >= selected) continue;
                selected = candidate.TileId;
                selectedDistance = candidate.DistanceTiles;
            }
            return selected;
        }

        public static bool CanUsePursuitEndpoint(bool tileValid,
            bool ground, bool liquid, bool ocean, bool lava,
            bool blocked, bool walled, bool cityCenter,
            bool sameIsland, bool inCorridor)
        {
            return tileValid && ground && !liquid && !ocean && !lava &&
                   !blocked && !walled && !cityCenter && sameIsland &&
                   EffectiveSupplyConnection(inCorridor);
        }

    }
}
