using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct TributarySettlementResult
    {
        internal TributarySettlementResult(long relationId, int year,
            float powerRatio, int factorPercent, float politicalTransferred,
            int goldTransferred, string outcome, string offeringOutcome)
        {
            RelationId = relationId;
            Year = year;
            PowerRatio = powerRatio;
            FactorPercent = factorPercent;
            PoliticalTransferred = politicalTransferred;
            GoldTransferred = goldTransferred;
            Outcome = outcome ?? "";
            OfferingOutcome = offeringOutcome ?? "";
        }

        internal long RelationId { get; }
        internal int Year { get; }
        internal float PowerRatio { get; }
        internal int FactorPercent { get; }
        internal float PoliticalTransferred { get; }
        internal int GoldTransferred { get; }
        internal string Outcome { get; }
        internal string OfferingOutcome { get; }
    }

    internal static class TributarySettlementService
    {
        private readonly struct DueRelation
        {
            internal DueRelation(long relationId, long tributaryId,
                int tributeRate)
            {
                RelationId = relationId;
                TributaryId = tributaryId;
                TributeRate = tributeRate;
            }

            internal long RelationId { get; }
            internal long TributaryId { get; }
            internal int TributeRate { get; }
        }

        internal static void SettleDueRelations(Kingdom pSuzerain)
        {
            SQLiteConnection db =
                LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || pSuzerain?.data == null ||
                pSuzerain.isRekt()) return;

            int year = Date.getCurrentYear();
            foreach (DueRelation relation in ReadRelations(db,
                         pSuzerain.id))
            {
                try
                {
                    Kingdom tributary = ResolveKingdom(
                        relation.TributaryId);
                    if (!IsCurrentProjection(tributary, pSuzerain,
                            relation.RelationId)) continue;
                    if (!TributarySettlementPersistence.TryBeginAttempt(
                            db, relation.RelationId, year)) continue;

                    TributarySettlementResult result = SettleAttempt(db,
                        relation, tributary, pSuzerain, year);
                    RecordSettlement(tributary, pSuzerain, result);
                    ModClass.LogInfo("[TributarySettlement] relation=" +
                        result.RelationId + " tributary=" + tributary.id +
                        " suzerain=" + pSuzerain.id + " year=" + year +
                        " factor=" + result.FactorPercent + " political=" +
                        result.PoliticalTransferred.ToString("0.0") +
                        " gold=" + result.GoldTransferred + " outcome=" +
                        result.Outcome + " offering=" + result.OfferingOutcome);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "Tributary settlement failed: relation=" +
                        relation.RelationId + " tributary=" +
                        relation.TributaryId + " suzerain=" +
                        pSuzerain.id + " year=" + year + " error=" +
                        error.Message);
                }
            }
        }

        private static TributarySettlementResult SettleAttempt(
            SQLiteConnection db, DueRelation relation,
            Kingdom tributary, Kingdom pSuzerain, int year)
        {
            float tributaryPower = VassalService.GetWarPowerScore(tributary, pIncludeVassals: true);
            float suzerainPower = VassalService.GetWarPowerScore(pSuzerain, pIncludeVassals: true);
            float ratio = TributaryPaymentRules.PowerRatio(
                tributaryPower, suzerainPower);
            int factor = TributaryPaymentRules.FactorPercent(
                tributaryPower, suzerainPower);
            if (factor == 0)
            {
                VassalService.EndVassal(tributary,
                    "tribute_refused_power");
                return new TributarySettlementResult(relation.RelationId,
                    year, ratio, factor, 0f, 0,
                    "tribute_refused_power", "");
            }

            CityEconomyService.TryGetLatestCachedTaxContribution(
                tributary, out float annualTax);
            float politicalBalance =
                KingdomPolicyService.GetPoliticalPoints(tributary);
            float basePolitical = VassalFiscalRules.PoliticalTribute(
                annualTax, relation.TributeRate, politicalBalance,
                KingdomPolicyService.GetPoliticalPoints(pSuzerain),
                VassalFiscalRules.MaximumPoliticalBalance);
            float requestedPolitical =
                TributaryPaymentRules.ScalePolitical(basePolitical,
                    factor, politicalBalance);
            float politicalTransferred =
                KingdomPolicyService.TransferPoliticalPoints(tributary,
                    pSuzerain, requestedPolitical);

            int availableGold =
                VassalService.GetTributaryCapitalGold(tributary);
            int baseGold = VassalFiscalRules.GoldTribute(annualTax,
                relation.TributeRate, availableGold);
            int requestedGold = TributaryPaymentRules.ScaleGold(baseGold,
                factor, availableGold);
            int goldTransferred =
                VassalService.TransferTributaryCapitalGold(tributary,
                    pSuzerain, requestedGold);

            if (!TributaryPaymentRules.IsPaid(politicalTransferred,
                    goldTransferred))
            {
                VassalService.EndVassal(tributary,
                    "tribute_unpaid");
                return new TributarySettlementResult(relation.RelationId,
                    year, ratio, factor, politicalTransferred,
                    goldTransferred, "tribute_unpaid", "");
            }

            TributarySettlementPersistence.MarkPaid(db,
                relation.RelationId, year, factor);
            string offeringOutcome =
                TributaryHouseholdOfferingService.TryOffer(tributary,
                    pSuzerain, relation.RelationId, year);
            return new TributarySettlementResult(relation.RelationId,
                year, ratio, factor, politicalTransferred,
                goldTransferred, "paid", offeringOutcome);
        }

        private static List<DueRelation> ReadRelations(
            SQLiteConnection db, long suzerainId)
        {
            var result = new List<DueRelation>();
            using var command = new SQLiteCommand(db);
            command.CommandText =
                "SELECT RELATION_ID,VASSAL_ID,TRIBUTE_RATE FROM " +
                VassalRelationTableItem.GetTableName() +
                " WHERE SUZERAIN_ID=@s AND ACTIVE=1 AND END_TIME<0 " +
                "AND CONTRACT_TIER=@tier ORDER BY RELATION_ID";
            command.Parameters.AddWithValue("@s", suzerainId);
            command.Parameters.AddWithValue("@tier",
                VassalContractTierRules.Tributary);
            using var reader = (SQLiteDataReader)command.ExecuteReader();
            while (reader.Read())
                result.Add(new DueRelation(reader.GetInt64(0),
                    reader.GetInt64(1), reader.IsDBNull(2)
                        ? 0
                        : (int)reader.GetInt64(2)));
            return result;
        }

        private static Kingdom ResolveKingdom(long kingdomId)
        {
            if (kingdomId < 0 || World.world?.kingdoms == null)
                return null;
            try
            {
                return World.world.kingdoms.get(kingdomId);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsCurrentProjection(Kingdom tributary,
            Kingdom suzerain, long relationId)
        {
            if (tributary?.data == null || tributary.isRekt() ||
                suzerain?.data == null || suzerain.isRekt()) return false;
            tributary.data.get(LineageKeys.TRIBUTARY_RELATION_ID,
                out long projectedRelationId, -1L);
            return projectedRelationId == relationId &&
                   VassalService.GetTributarySuzerainId(tributary) ==
                   suzerain.id;
        }

        private static void RecordSettlement(Kingdom tributary,
            Kingdom suzerain, TributarySettlementResult result)
        {
            string eventId = result.Outcome == "paid"
                ? "tributary_paid"
                : result.Outcome == "tribute_refused_power"
                    ? "tributary_refused_power"
                    : "tributary_unpaid";
            string label = HistoryLocalizationRules.Text(
                "aw_hist_" + eventId);
            string text = (tributary?.name ?? "") + " -> " +
                (suzerain?.name ?? "") + " " + label +
                " political=" + result.PoliticalTransferred.ToString("0.0") +
                " gold=" + result.GoldTransferred + " factor=" +
                result.FactorPercent;
            HistoryWriter.RecordKingdom(tributary, eventId, text,
                HistoryTarget.Kingdom(suzerain));
            HistoryWriter.RecordKingdom(suzerain, eventId, text,
                HistoryTarget.Kingdom(tributary));
        }
    }
}
