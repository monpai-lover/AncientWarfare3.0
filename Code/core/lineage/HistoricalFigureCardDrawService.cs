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
            : this(pWinner, pRollingCards, pWinnerIndex, pDrawId, pCommitted,
                pError, "")
        {
        }

        internal HistoricalFigureCardRevealResult(
            HistoricalFigureCardDefinition pWinner,
            IReadOnlyList<HistoricalFigureCardDefinition> pRollingCards,
            int pWinnerIndex, string pDrawId, bool pCommitted, string pError,
            string pCrateId)
        {
            Winner = pWinner;
            RollingCards = pRollingCards ?? Array.Empty<HistoricalFigureCardDefinition>();
            WinnerIndex = pWinnerIndex;
            DrawId = pDrawId ?? "";
            IsCommitted = pCommitted;
            Error = pError ?? "";
            CrateId = pCrateId ?? "";
        }

        public HistoricalFigureCardDefinition Winner { get; }
        public IReadOnlyList<HistoricalFigureCardDefinition> RollingCards { get; }
        public int WinnerIndex { get; }
        public string DrawId { get; }
        public bool IsCommitted { get; }
        public string Error { get; }
        public string CrateId { get; }
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

        public static HistoricalFigureCardRarity RarityForRoll(int pRoll,
            IReadOnlyList<HistoricalFigureCardDefinition> pLocalCards,
            IReadOnlyList<HistoricalFigureCardDefinition> pSharedGoldCards)
        {
            IReadOnlyDictionary<HistoricalFigureCardRarity,
                HistoricalFigureCardDefinition[]> pools = BuildPools(pLocalCards,
                    pSharedGoldCards);
            HistoricalFigureCardRarity requested = RarityForRoll(pRoll);
            int requestedIndex = 0;
            while (!HistoricalFigureCardRarity.All[requestedIndex].Equals(
                       requested)) requestedIndex++;
            for (int i = requestedIndex;
                 i < HistoricalFigureCardRarity.All.Count; i++)
            {
                HistoricalFigureCardRarity rarity =
                    HistoricalFigureCardRarity.All[i];
                if (pools[rarity].Length > 0) return rarity;
            }
            for (int i = requestedIndex - 1; i >= 0; i--)
            {
                HistoricalFigureCardRarity rarity =
                    HistoricalFigureCardRarity.All[i];
                if (pools[rarity].Length > 0) return rarity;
            }
            return null;
        }

        public static HistoricalFigureCardRevealResult BuildReveal(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            IHistoricalFigureCardRandom pRandom)
        {
            return BuildReveal(pCards, pRandom, "");
        }

        public static HistoricalFigureCardRevealResult BuildReveal(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            IHistoricalFigureCardRandom pRandom, string pCrateId)
        {
            if (pCards == null || pCards.Count == 0)
                return Failure("card catalogue is empty");
            if (pRandom == null) return Failure("random source is missing");

            HistoricalFigureCardDefinition[] candidates = pCards
                .Where(p => p != null).ToArray();
            if (candidates.Length == 0) return Failure("card catalogue is empty");
            HistoricalFigureCardDefinition[] sharedGold =
                string.IsNullOrEmpty(pCrateId)
                    ? candidates.Where(p => p.Rarity != null &&
                        p.Rarity.Equals(HistoricalFigureCardRarity.Gold)).ToArray()
                    : HistoricalFigureCardCatalog.All.Where(p => p != null &&
                        p.Rarity != null &&
                        p.Rarity.Equals(HistoricalFigureCardRarity.Gold)).ToArray();
            IReadOnlyDictionary<HistoricalFigureCardRarity,
                HistoricalFigureCardDefinition[]> pools = BuildPools(candidates,
                    sharedGold);
            HistoricalFigureCardRarity rarity = RarityForRoll(
                pRandom.Next(ProbabilityScale), candidates, sharedGold);
            if (rarity == null) return Failure("selected crate has no cards");
            HistoricalFigureCardDefinition[] rarityCards = pools[rarity];

            HistoricalFigureCardDefinition winner = rarityCards[
                pRandom.Next(rarityCards.Length)];
            HistoricalFigureCardDefinition[] allTrackCards = pools.Values
                .SelectMany(p => p).GroupBy(p => p.CardId,
                    StringComparer.Ordinal).Select(p => p.First()).ToArray();
            HistoricalFigureCardDefinition[] alternatives = allTrackCards
                .Where(p => !string.Equals(p.CardId, winner.CardId,
                    StringComparison.Ordinal)).ToArray();
            if (alternatives.Length == 0)
                alternatives = new[] { winner };

            var rolling = new HistoricalFigureCardDefinition[RollingCardCount];
            for (int i = 0; i < rolling.Length; i++)
            {
                if (i == WinnerIndex)
                {
                    rolling[i] = winner;
                    continue;
                }
                HistoricalFigureCardRarity trackRarity = RarityForRoll(
                    pRandom.Next(ProbabilityScale), candidates, sharedGold);
                HistoricalFigureCardDefinition[] trackPool = pools[trackRarity];
                HistoricalFigureCardDefinition card = trackPool[
                    pRandom.Next(trackPool.Length)];
                rolling[i] = string.Equals(card.CardId, winner.CardId,
                    StringComparison.Ordinal)
                    ? alternatives[pRandom.Next(alternatives.Length)]
                    : card;
            }
            return new HistoricalFigureCardRevealResult(winner, rolling,
                WinnerIndex, "", false, "", pCrateId);
        }

        public static HistoricalFigureCardRevealResult DrawAndCommit(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            HistoricalFigureCardCollectionStore pStore,
            IHistoricalFigureCardRandom pRandom = null,
            string pUtc = null)
        {
            return DrawAndCommit(pCards, "", pStore, pRandom, pUtc);
        }

        public static HistoricalFigureCardRevealResult DrawAndCommit(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            string pCrateId,
            HistoricalFigureCardCollectionStore pStore,
            IHistoricalFigureCardRandom pRandom = null,
            string pUtc = null)
        {
            if (pStore == null) return Failure("collection store is missing");
            HistoricalFigureCardRevealResult reveal = BuildReveal(pCards,
                pRandom ?? new HistoricalFigureCardRandom(), pCrateId);
            if (!reveal.Succeeded) return reveal;
            string drawId = Guid.NewGuid().ToString("N");
            string utc = string.IsNullOrEmpty(pUtc)
                ? DateTime.UtcNow.ToString("o")
                : pUtc;
            bool committed = pStore.RecordDraw(drawId, reveal.Winner.CardId,
                reveal.Winner.Rarity.Id, utc, pCrateId);
            return committed
                ? new HistoricalFigureCardRevealResult(reveal.Winner,
                    reveal.RollingCards, reveal.WinnerIndex, drawId, true, "",
                    pCrateId)
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
                pError, "");
        }

        private static IReadOnlyDictionary<HistoricalFigureCardRarity,
            HistoricalFigureCardDefinition[]> BuildPools(
            IReadOnlyList<HistoricalFigureCardDefinition> pLocalCards,
            IReadOnlyList<HistoricalFigureCardDefinition> pSharedGoldCards)
        {
            HistoricalFigureCardDefinition[] local = (pLocalCards ??
                Array.Empty<HistoricalFigureCardDefinition>())
                .Where(p => p != null && p.Rarity != null).ToArray();
            HistoricalFigureCardDefinition[] sharedGold = (pSharedGoldCards ??
                Array.Empty<HistoricalFigureCardDefinition>())
                .Where(p => p != null && p.Rarity != null &&
                    p.Rarity.Equals(HistoricalFigureCardRarity.Gold)).ToArray();
            return HistoricalFigureCardRarity.All.ToDictionary(p => p, p =>
                p.Equals(HistoricalFigureCardRarity.Gold)
                    ? sharedGold
                    : local.Where(c => c.Rarity.Equals(p)).ToArray());
        }

        private static int RarityWeight(HistoricalFigureCardRarity pRarity)
        {
            return (int)Math.Round(pRarity.Probability * ProbabilityScale,
                MidpointRounding.AwayFromZero);
        }
    }
}
