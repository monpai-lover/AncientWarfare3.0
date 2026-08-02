using System;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsRole
    {
        Assault = 0,
        Defense = 1,
        Reserve = 2,
        Reinforcement = 3,
        TemporaryGarrisonSortie = 4
    }

    public enum ArmyRtsState
    {
        Idle = 0,
        Rally = 1,
        March = 2,
        Deploy = 3,
        Assault = 4,
        Hold = 5,
        Pursue = 6,
        Retreat = 7,
        Regroup = 8,
        Replenish = 9
    }

    public enum ArmyRtsPosture
    {
        Automatic = 0,
        Attack = 1,
        Defend = 2,
        Retreat = 3
    }

    public sealed class ArmyRtsTransitionFacts
    {
        public ArmyRtsState CurrentState { get; set; }
        public ArmyRtsRole Role { get; set; }
        public ArmyRtsPosture Posture { get; set; }
        public bool HasMission { get; set; }
        public bool FrontHold { get; set; }
        public bool TargetValid { get; set; }
        public bool FormationObservationComplete { get; set; } = true;
        public bool RallyReady { get; set; } = true;
        public bool RouteArrived { get; set; }
        public bool DeploymentReady { get; set; }
        public bool EnemyContact { get; set; }
        public bool ForceReady { get; set; }
        public bool MinimumForceReady { get; set; } = true;
        public bool NeedsReplenishment { get; set; }
        public bool TargetComplete { get; set; }
        public bool HoldRequired { get; set; }
        public bool PursuitAllowed { get; set; }
        public bool RetreatArrived { get; set; }
        public bool RegroupReady { get; set; }
        public bool RegroupRecoveryStalled { get; set; }
        public bool SurvivalException { get; set; }
        public bool PursuitComplete { get; set; }
        public bool PursuitRequiresRegroup { get; set; }
        public bool LocalForceAdvantage { get; set; }
        public bool OpenObjective { get; set; }
        public int Supply { get; set; } = 100;
        public int Organization { get; set; } = 100;
    }

    public sealed class ArmyRtsMission
    {
        public long ArmyId { get; set; } = -1L;
        public long KingdomId { get; set; } = -1L;
        public long WarId { get; set; } = -1L;
        public long FrontId { get; set; } = -1L;
        public long TargetCityId { get; set; } = -1L;
        public int TargetStrength { get; set; }
        public ArmyRtsProposalKind ProposalKind { get; set; } =
            ArmyRtsProposalKind.Attack;
        public ArmyRtsRole Role { get; set; }
        public ArmyRtsPosture Posture { get; set; }
        public bool PlayerOrder { get; set; }
        public double IssuedTime { get; set; } = -1d;
    }

    public sealed class ArmyStrategicFacts
    {
        public ArmyStrategicFacts(long pArmyId, long pKingdomId,
            long pAnchorCityId, long pCaptainId, long pCurrentTargetCityId,
            int pUnitCount, string pLegacyRole, bool pCaptainAlive,
            bool pRoyalGuard, bool pDedicatedGarrison, int pSupply = 100,
            int pOrganization = 100, int pCaptainX = int.MinValue,
            int pCaptainY = int.MinValue, bool pSpecialArmy = false)
        {
            ArmyId = pArmyId;
            KingdomId = pKingdomId;
            AnchorCityId = pAnchorCityId;
            CaptainId = pCaptainId;
            CurrentTargetCityId = pCurrentTargetCityId;
            UnitCount = pUnitCount;
            LegacyRole = pLegacyRole ?? string.Empty;
            CaptainAlive = pCaptainAlive;
            RoyalGuard = pRoyalGuard;
            DedicatedGarrison = pDedicatedGarrison;
            SpecialArmy = pSpecialArmy;
            Supply = Math.Max(0, Math.Min(100, pSupply));
            Organization = Math.Max(0, Math.Min(100, pOrganization));
            CaptainX = pCaptainX;
            CaptainY = pCaptainY;
        }

        public long ArmyId { get; }
        public long KingdomId { get; }
        public long AnchorCityId { get; }
        public long CaptainId { get; }
        public long CurrentTargetCityId { get; }
        public int UnitCount { get; }
        public string LegacyRole { get; }
        public bool CaptainAlive { get; }
        public bool RoyalGuard { get; }
        public bool DedicatedGarrison { get; }
        public bool SpecialArmy { get; }
        public int Supply { get; }
        public int Organization { get; }
        public int CaptainX { get; }
        public int CaptainY { get; }
        public bool HasCaptainPosition =>
            CaptainX != int.MinValue && CaptainY != int.MinValue;
    }
}
