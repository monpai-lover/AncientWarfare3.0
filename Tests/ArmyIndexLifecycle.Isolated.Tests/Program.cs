using AncientWarfare3.core.lineage;

internal static class Program
{
    private static int failures;
    private static int total;

    private static int Main()
    {
        Run("registration populates both indexes and city lookup", Registration);
        var notifications = new (string Name, Action<Army> Notify)[]
        {
            ("strategic registration", ArmyStrategicIndexService.OnArmyRegistered),
            ("strategic roster", ArmyStrategicIndexService.OnArmyRosterChanged),
            ("strategic owner", ArmyStrategicIndexService.OnArmyKingdomChanged),
            ("strategic disposal", ArmyStrategicIndexService.OnArmyDisposed),
            ("field change", ArmyFieldIndexService.OnArmyChanged),
            ("field disposal", ArmyFieldIndexService.OnArmyDisposed)
        };
        foreach (var notification in notifications)
        {
            Run("disposed late " + notification.Name, () =>
            {
                var (kingdom, city, army) = Fixture();
                ArmyStrategicIndexService.OnArmyRegistered(army);
                ArmyStrategicIndexService.OnArmyDisposed(army);
                army.Dispose();
                Notices.Calls.Clear();
                notification.Notify(army);
                Equal(0, Notices.Calls.Count, "late notification must not reach dependent services");
                Equal(0, ArmyFieldIndexService.Count(kingdom), "disposed field index");
                Equal(0, StrategicIds(kingdom).Count, "disposed strategic index");
            });
        }
        Run("normal disposal cleans one army and preserves another", NormalDisposal);
        Run("dead army with data still removes its indexes", DeadWithData);
        Run("rebuild skips disposed manager entries", Rebuild);
        Run("owner transfer updates both indexes and notifies both owners", OwnerTransfer);
        Run("known-id resolution prunes missed disposal", MissedDisposal);
        Run("invalid kingdom removes indexed army", InvalidKingdom);
        Console.WriteLine($"Army index lifecycle tests: {total - failures}/{total} passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Registration()
    {
        var (kingdom, city, army) = Fixture();
        ArmyStrategicIndexService.OnArmyRegistered(army);
        Equal(army.id, StrategicIds(kingdom).Single(), "registered strategic id");
        Equal(1, ArmyFieldIndexService.Count(kingdom), "registered field count");
        True(ArmyFieldIndexService.TryGetCityArmy(city, out Army found) && found == army,
            "registered city army");
    }

    private static void NormalDisposal()
    {
        var (kingdom, city, army) = Fixture();
        var secondCity = new City { data = new EntityData { id = 12 }, kingdom = kingdom };
        Army second = NewArmy(102, kingdom, secondCity);
        ArmyStrategicIndexService.OnArmyRegistered(army);
        ArmyStrategicIndexService.OnArmyRegistered(second);
        ArmyStrategicIndexService.OnArmyDisposed(army);
        True(Notices.Calls.Contains("invalidate:101"), "coalition receives valid old identity");
        True(Notices.Calls.Contains("dispose:101"), "replenishment receives valid old identity");
        army.Dispose();
        Equal(second.id, StrategicIds(kingdom).Single(), "unrelated strategic entry");
        Equal(1, ArmyFieldIndexService.Count(kingdom), "unrelated field entry");
        True(!ArmyFieldIndexService.TryGetCityArmy(city, out _), "old city mapping removed");
        True(ArmyFieldIndexService.TryGetCityArmy(secondCity, out Army found) && found == second,
            "unrelated city mapping retained");
    }

    private static void DeadWithData()
    {
        var (kingdom, city, army) = Fixture();
        ArmyStrategicIndexService.OnArmyRegistered(army);
        army.Alive = false;
        ArmyStrategicIndexService.OnArmyRosterChanged(army);
        Equal(0, StrategicIds(kingdom).Count, "dead strategic entry");
        Equal(0, ArmyFieldIndexService.Count(kingdom), "dead field entry");
        True(!ArmyFieldIndexService.TryGetCityArmy(city, out _), "dead city mapping");
    }

    private static void Rebuild()
    {
        var (kingdom, city, army) = Fixture();
        Army disposed = NewArmy(102, kingdom, null);
        disposed.Dispose();
        ArmyStrategicIndexService.RebuildRuntime();
        Equal(army.id, StrategicIds(kingdom).Single(), "valid army survives rebuild");
        Equal(1, ArmyFieldIndexService.Count(kingdom), "rebuild field count");
    }

    private static void OwnerTransfer()
    {
        var (kingdom, city, army) = Fixture();
        ArmyStrategicIndexService.OnArmyRegistered(army);
        var next = new Kingdom { data = new EntityData { id = 2 } };
        World.world.kingdoms.Add(next);
        army.Kingdom = next;
        city.kingdom = next;
        Notices.Calls.Clear();
        ArmyStrategicIndexService.OnArmyKingdomChanged(army);
        Equal(0, StrategicIds(kingdom).Count, "previous strategic owner");
        Equal(0, ArmyFieldIndexService.Count(kingdom), "previous field owner");
        Equal(army.id, StrategicIds(next).Single(), "new strategic owner");
        Equal(1, ArmyFieldIndexService.Count(next), "new field owner");
        True(Notices.Calls.Contains("war:1") && Notices.Calls.Contains("war:2"),
            "both war directors notified");
    }

    private static void MissedDisposal()
    {
        var (kingdom, city, army) = Fixture();
        ArmyStrategicIndexService.OnArmyRegistered(army);
        long id = army.id;
        army.Dispose();
        True(ArmyFieldIndexService.ResolveIndexedArmy(id, kingdom.id) == null,
            "disposed id never resolves");
        Equal(0, StrategicIds(kingdom).Count, "known-id strategic pruning");
        Equal(0, ArmyFieldIndexService.Count(kingdom), "known-id field pruning");
        True(!ArmyFieldIndexService.TryGetCityArmy(city, out _), "stale city never resolves");
        Army replacement = NewArmy(102, kingdom, city);
        ArmyStrategicIndexService.OnArmyRegistered(replacement);
        True(ArmyFieldIndexService.TryGetCityArmy(city, out Army found) && found == replacement,
            "new city army replaces stale mapping");
    }

    private static void InvalidKingdom()
    {
        var (kingdom, city, army) = Fixture();
        ArmyStrategicIndexService.OnArmyRegistered(army);
        kingdom.Rekt = true;
        ArmyStrategicIndexService.OnArmyRosterChanged(army);
        Equal(0, StrategicIds(kingdom).Count, "invalid kingdom strategic removal");
        Equal(0, ArmyFieldIndexService.Count(kingdom), "invalid kingdom field removal");
    }

    private static (Kingdom, City, Army) Fixture()
    {
        World.world = new World();
        ArmyStrategicIndexService.ClearRuntime();
        Notices.Calls.Clear();
        var kingdom = new Kingdom { data = new EntityData { id = 1 } };
        World.world.kingdoms.Add(kingdom);
        var city = new City { data = new EntityData { id = 11 }, kingdom = kingdom };
        return (kingdom, city, NewArmy(101, kingdom, city));
    }

    private static Army NewArmy(long id, Kingdom kingdom, City city)
    {
        var army = new Army { data = new EntityData { id = id }, Kingdom = kingdom, Anchor = city };
        World.world.armies.Add(army);
        if (city != null) city.Army = army;
        return army;
    }

    private static IReadOnlyList<long> StrategicIds(Kingdom kingdom) =>
        ArmyStrategicIndexService.CreateSnapshotCursor(kingdom).Take(int.MaxValue);

    private static void Run(string name, Action action)
    {
        total++;
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message}: expected {expected}, actual {actual}");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
