using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.figures;

namespace AncientWarfare3.core.lineage
{
    public interface IHistoricalFigureCardRandom
    {
        int Next(int pMaximumExclusive);
    }

    internal sealed class HistoricalFigureCardRandom : IHistoricalFigureCardRandom
    {
        private readonly Random _random = new Random();

        public int Next(int pMaximumExclusive)
        {
            return _random.Next(pMaximumExclusive);
        }
    }

    public sealed class HistoricalFigureCardRevealResult
    {
        internal HistoricalFigureCardRevealResult(
            HistoricalFigureCardDefinition pWinner,
            IReadOnlyList<HistoricalFigureCardDefinition> pRollingCards,
            int pWinnerIndex, string pDrawId, bool pCommitted, string pError)
        {
            Winner = pWinner;
            RollingCards = pRollingCards ?? Array.Empty<HistoricalFigureCardDefinition>();
            WinnerIndex = pWinnerIndex;
            DrawId = pDrawId ?? "";
            IsCommitted = pCommitted;
            Error = pError ?? "";
        }

        public HistoricalFigureCardDefinition Winner { get; }
        public IReadOnlyList<HistoricalFigureCardDefinition> RollingCards { get; }
        public int WinnerIndex { get; }
        public string DrawId { get; }
        public bool IsCommitted { get; }
        public string Error { get; }
        public bool Succeeded => Winner != null && string.IsNullOrEmpty(Error);
    }

    public static class HistoricalFigureCardDrawService
    {
        public const int RollingCardCount = 50;
        public const int WinnerIndex = 42;
        private const int ProbabilityScale = 10000;

        public static HistoricalFigureCardRarity RarityForRoll(int pRoll)
        {
            int roll = Math.Max(0, Math.Min(ProbabilityScale - 1, pRoll));
            int boundary = 0;
            foreach (HistoricalFigureCardRarity rarity in HistoricalFigureCardRarity.All)
            {
                boundary += (int)Math.Round(rarity.Probability * ProbabilityScale,
                    MidpointRounding.AwayFromZero);
                if (roll < boundary) return rarity;
            }
            return HistoricalFigureCardRarity.Blue;
        }

        public static HistoricalFigureCardRevealResult BuildReveal(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            IHistoricalFigureCardRandom pRandom)
        {
            if (pCards == null || pCards.Count == 0)
                return Failure("card catalogue is empty");
            if (pRandom == null) return Failure("random source is missing");

            var candidates = pCards.Where(p => p != null).ToArray();
            if (candidates.Length == 0) return Failure("card catalogue is empty");
            HistoricalFigureCardRarity rarity = RarityForRoll(
                pRandom.Next(ProbabilityScale));
            HistoricalFigureCardDefinition[] rarityCards = candidates
                .Where(p => p.Rarity != null && p.Rarity.Equals(rarity)).ToArray();
            if (rarityCards.Length == 0)
                return Failure("selected rarity has no cards: " + rarity.Id);

            HistoricalFigureCardDefinition winner = rarityCards[
                pRandom.Next(rarityCards.Length)];
            HistoricalFigureCardDefinition[] alternatives = candidates
                .Where(p => !string.Equals(p.CardId, winner.CardId,
                    StringComparison.Ordinal)).ToArray();
            if (alternatives.Length == 0)
                alternatives = new[] { winner };

            var rolling = new HistoricalFigureCardDefinition[RollingCardCount];
            for (int i = 0; i < rolling.Length; i++)
                rolling[i] = i == WinnerIndex
                    ? winner
                    : alternatives[pRandom.Next(alternatives.Length)];
            return new HistoricalFigureCardRevealResult(winner, rolling,
                WinnerIndex, "", false, "");
        }

        public static HistoricalFigureCardRevealResult DrawAndCommit(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            HistoricalFigureCardCollectionStore pStore,
            IHistoricalFigureCardRandom pRandom = null,
            string pUtc = null)
        {
            if (pStore == null) return Failure("collection store is missing");
            HistoricalFigureCardRevealResult reveal = BuildReveal(pCards,
                pRandom ?? new HistoricalFigureCardRandom());
            if (!reveal.Succeeded) return reveal;
            string drawId = Guid.NewGuid().ToString("N");
            string utc = string.IsNullOrEmpty(pUtc)
                ? DateTime.UtcNow.ToString("o")
                : pUtc;
            bool committed = pStore.RecordDraw(drawId, reveal.Winner.CardId,
                reveal.Winner.Rarity.Id, utc);
            return committed
                ? new HistoricalFigureCardRevealResult(reveal.Winner,
                    reveal.RollingCards, reveal.WinnerIndex, drawId, true, "")
                : Failure("collection store rejected draw");
        }

        public static HistoricalFigureCardRevealResult Skip(
            HistoricalFigureCardRevealResult pResult)
        {
            return pResult;
        }

        private static HistoricalFigureCardRevealResult Failure(string pError)
        {
            return new HistoricalFigureCardRevealResult(null,
                Array.Empty<HistoricalFigureCardDefinition>(), -1, "", false,
                pError);
        }
    }
}
