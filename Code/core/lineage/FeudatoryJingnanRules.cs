namespace AncientWarfare3.core.lineage
{
    public enum JingnanWarWinner
    {
        None = 0,
        Attackers = 1,
        Defenders = 2
    }

    public enum FeudatoryJingnanSettlement
    {
        None = 0,
        PrinceTakesThrone = 1,
        CentralAbolishesFeudatory = 2,
        StalemateLeavesClaimants = 3
    }

    public readonly struct FeudatoryJingnanOrigin
    {
        public FeudatoryJingnanOrigin(long feudatoryId, long princeActorId,
            long originalEmpireId, long rebelKingdomId)
        {
            FeudatoryId = feudatoryId;
            PrinceActorId = princeActorId;
            OriginalEmpireId = originalEmpireId;
            RebelKingdomId = rebelKingdomId;
        }

        public long FeudatoryId { get; }
        public long PrinceActorId { get; }
        public long OriginalEmpireId { get; }
        public long RebelKingdomId { get; }

        public bool IsValid => FeudatoryId >= 0 && PrinceActorId >= 0 &&
                               OriginalEmpireId >= 0 && RebelKingdomId >= 0 &&
                               OriginalEmpireId != RebelKingdomId;
    }

    public static class FeudatoryJingnanRules
    {
        public const string WarTypeId = "jingnan_war";
        public const int SettlementReadBatchSize = 8;

        public static bool IsJingnanWar(string pWarType)
        {
            return pWarType == WarTypeId;
        }

        public static FeudatoryJingnanSettlement ResolveSettlement(
            bool rebelsAreAttackers, JingnanWarWinner pWinner)
        {
            if (pWinner == JingnanWarWinner.None)
                return FeudatoryJingnanSettlement.None;
            bool attackersWon = pWinner == JingnanWarWinner.Attackers;
            bool rebelsWon = attackersWon == rebelsAreAttackers;
            return rebelsWon
                ? FeudatoryJingnanSettlement.PrinceTakesThrone
                : FeudatoryJingnanSettlement.CentralAbolishesFeudatory;
        }

        public static FeudatoryJingnanSettlement ResolveWarEnd(
            bool rebelsAreAttackers, JingnanWarWinner pWinner)
        {
            FeudatoryJingnanSettlement decisive = ResolveSettlement(
                rebelsAreAttackers, pWinner);
            return decisive == FeudatoryJingnanSettlement.None
                ? FeudatoryJingnanSettlement.StalemateLeavesClaimants
                : decisive;
        }

        public static int CatalystDeltaForOutbreak(bool pFirstRebelInWar)
        {
            return pFirstRebelInWar ? 20 : 5;
        }

        public static int CatalystDeltaForCentralVictory()
        {
            return -15;
        }

        public static bool PrinceVictoryEntersRenewal(
            bool mandateActiveAfterSettlement)
        {
            return mandateActiveAfterSettlement;
        }

        public static bool ShouldFinalizeCapitalCapture(bool activeWar,
            bool jingnanWar, bool newOwnerIsAttacker,
            bool capturedRecordedCapital, bool victorUnset)
        {
            return activeWar && jingnanWar && newOwnerIsAttacker &&
                   capturedRecordedCapital && victorUnset;
        }

        public static bool ShouldRestoreRecordedCapital(bool empireHasCapital,
            bool recordedCapitalOwnedByEmpire)
        {
            return !empireHasCapital && recordedCapitalOwnedByEmpire;
        }

        public static bool ShouldReadNextSettlementBatch(int pRowsRead)
        {
            return pRowsRead >= SettlementReadBatchSize;
        }
    }
}
