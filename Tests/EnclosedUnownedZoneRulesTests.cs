using System;
using AncientWarfare3.core.lineage;

public static class EnclosedUnownedZoneRulesTests
{
    public static int Main()
    {
        try
        {
            OneCityEnclosure();
            SharedSidesWin();
            NearestCentreWinsTie();
            StableCityIdWinsFinalTie();
            MixedKingdomsRemainDisputed();
            OpenExitRemainsUnowned();
            InvalidZonesRemainUnowned();
            WorkBudgetsRemainBounded();
            Console.WriteLine("Enclosed unowned Zone rule tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void OneCityEnclosure()
    {
        EnclosedZoneNeighbourFacts city = Neighbour(10L, 1L, 8, 8);
        Equal(10L, Select(city, city, city, city),
            "one city enclosing all sides is selected");
    }

    private static void SharedSidesWin()
    {
        EnclosedZoneNeighbourFacts city20 = Neighbour(20L, 2L, 2, 2);
        EnclosedZoneNeighbourFacts city21 = Neighbour(21L, 2L, 10, 9);
        Equal(20L, Select(city20, city20, city20, city21),
            "same-kingdom city sharing three sides wins");
    }

    private static void NearestCentreWinsTie()
    {
        EnclosedZoneNeighbourFacts near = Neighbour(30L, 3L, 9, 10);
        EnclosedZoneNeighbourFacts far = Neighbour(31L, 3L, 20, 20);
        Equal(30L, Select(near, far, near, far),
            "equal shared sides use nearest centre");
    }

    private static void StableCityIdWinsFinalTie()
    {
        EnclosedZoneNeighbourFacts lower = Neighbour(40L, 4L, 9, 10);
        EnclosedZoneNeighbourFacts higher = Neighbour(41L, 4L, 11, 10);
        Equal(40L, Select(lower, higher, lower, higher),
            "equal distance uses lowest stable city id");
    }

    private static void MixedKingdomsRemainDisputed()
    {
        EnclosedZoneNeighbourFacts first = Neighbour(50L, 5L, 9, 10);
        EnclosedZoneNeighbourFacts second = Neighbour(51L, 6L, 11, 10);
        Equal(-1L, Select(first, first, second, second),
            "mixed kingdoms remain disputed");
    }

    private static void OpenExitRemainsUnowned()
    {
        EnclosedZoneNeighbourFacts city = Neighbour(60L, 7L, 9, 10);
        var open = new EnclosedZoneNeighbourFacts(false, false, -1L, -1L,
            0, 0);
        Equal(-1L, Select(city, city, city, open),
            "an unowned cardinal exit remains open");
    }

    private static void InvalidZonesRemainUnowned()
    {
        EnclosedZoneNeighbourFacts city = Neighbour(70L, 8L, 9, 10);
        EnclosedZoneNeighbourFacts[] neighbours = { city, city, city, city };
        Equal(-1L, EnclosedUnownedZoneRules.SelectTargetCity(
                true, false, 32, 4, 10, 10, neighbours),
            "an already owned zone is unchanged");
        Equal(-1L, EnclosedUnownedZoneRules.SelectTargetCity(
                false, true, 32, 4, 10, 10, neighbours),
            "world-edge zone cannot be enclosed");
        Equal(-1L, EnclosedUnownedZoneRules.SelectTargetCity(
                false, false, 0, 4, 10, 10, neighbours),
            "groundless zone is not assigned");
        Equal(-1L, EnclosedUnownedZoneRules.SelectTargetCity(
                false, false, 32, 3, 10, 10, neighbours),
            "missing cardinal side is not enclosed");
    }

    private static void WorkBudgetsRemainBounded()
    {
        Equal(8, EnclosedUnownedZoneRules.ResolveDrainCount(20, 8),
            "queue drain obeys fixed budget");
        Equal(3, EnclosedUnownedZoneRules.ResolveDrainCount(3, 8),
            "queue drain stops at pending count");
        Equal(3, EnclosedUnownedZoneRules.ResolveSweepCount(100, 97, 64),
            "initial sweep stops at list end");
        Equal(0, EnclosedUnownedZoneRules.ResolveSweepCount(100, 100, 64),
            "completed initial sweep performs no work");
    }

    private static long Select(params EnclosedZoneNeighbourFacts[] neighbours)
    {
        return EnclosedUnownedZoneRules.SelectTargetCity(
            zoneAlreadyOwned: false,
            worldEdge: false,
            groundTileCount: 32,
            cardinalNeighbourCount: 4,
            zoneX: 10,
            zoneY: 10,
            neighbours: neighbours);
    }

    private static EnclosedZoneNeighbourFacts Neighbour(long cityId,
        long kingdomId, int centerX, int centerY)
    {
        return new EnclosedZoneNeighbourFacts(true, true, cityId, kingdomId,
            centerX, centerY);
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(name + ": expected " +
                                                expected + ", got " + actual);
    }
}
