using System;

namespace AncientWarfare3.core.grandstrategy
{
    public static class GrandStrategyLedgerRules
    {
        public static bool TryRaise(GrandStrategyKingdomLedger ledger, int amount,
            out string error)
        {
            error = string.Empty;
            if (ledger == null) { error = "ledger_missing"; return false; }
            if (amount <= 0) { error = "amount_must_be_positive"; return false; }
            if (amount > ledger.AvailableManpower) { error = "insufficient_manpower"; return false; }
            ledger.AvailableManpower -= amount;
            ledger.RaisedManpower += amount;
            return true;
        }

        public static bool ApplyCasualties(GrandStrategyKingdomLedger ledger,
            string transactionKey, int permanentDeaths, int wounded,
            int dispersed, int prisoners, out string error)
        {
            error = string.Empty;
            if (ledger == null) { error = "ledger_missing"; return false; }
            if (string.IsNullOrWhiteSpace(transactionKey)) { error = "transaction_key_missing"; return false; }
            if (ledger.HasCommitted(transactionKey)) return true;
            if (permanentDeaths < 0 || wounded < 0 || dispersed < 0 || prisoners < 0)
            { error = "casualties_must_be_non_negative"; return false; }
            int total = permanentDeaths + wounded + dispersed + prisoners;
            if (total > ledger.RaisedManpower)
            { error = "casualties_exceed_raised_manpower"; return false; }
            ledger.RaisedManpower -= total;
            ledger.PermanentDeaths += permanentDeaths;
            ledger.WoundedManpower += wounded;
            ledger.DispersedManpower += dispersed;
            ledger.Prisoners += prisoners;
            ledger.Commit(transactionKey);
            return true;
        }

        public static int RecoverWounded(GrandStrategyKingdomLedger ledger, int requested)
        {
            if (ledger == null || requested <= 0) return 0;
            int amount = Math.Min(requested, ledger.WoundedManpower);
            ledger.WoundedManpower -= amount;
            ledger.AvailableManpower += amount;
            return amount;
        }

        public static int RecoverDispersed(GrandStrategyKingdomLedger ledger, int requested)
        {
            if (ledger == null || requested <= 0) return 0;
            int amount = Math.Min(requested, ledger.DispersedManpower);
            ledger.DispersedManpower -= amount;
            ledger.AvailableManpower += amount;
            return amount;
        }
    }
}
