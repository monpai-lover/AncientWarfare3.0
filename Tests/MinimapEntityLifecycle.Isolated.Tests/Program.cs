using AncientWarfare3.core.lineage;
using AncientWarfare3.patch;

internal static class Program
{
    private static int Main()
    {
        Action[] tests = { FigureDisposedCurrentKingdomFallsBackToCity, FigureDisposedCityKingdomKeepsCurrent,
            FigureBothDisposedUsesUncoloredIcon, FigureUninitializedAssetFallsBackToCity,
            HeirDisposedCandidateIsRemoved, HeirUninitializedCandidateIsRemoved,
            HeirDisposedCurrentKingdomUsesRegisteredKingdom, HeirValidCurrentKingdomRetainsPriority,
            DisabledMarkersRemainDisabled, InitialIndexSkipsIncompleteKingdom,
            IndexRefreshRemovesIncompleteKingdom };
        int failed = 0;
        foreach (Action test in tests)
        {
            World.world = new();
            HeirMinimapMarkerIndex.Reset();
            DynamicSprites.ColorOwners.Clear();
            PlayerConfig.Enabled = true;
            try { test(); Console.WriteLine("PASS " + test.Method.Name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + error); }
        }
        Console.WriteLine($"Minimap entity lifecycle: {tests.Length - failed}/{tests.Length} passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void FigureDisposedCurrentKingdomFallsBackToCity()
    {
        Kingdom legal = Kingdom(2), current = Kingdom(3);
        current.Dispose();
        DrawFigure(Actor(legal, current));
        Equal("2", string.Join(",", DynamicSprites.ColorOwners), "disposed current kingdom falls back to live city owner");
    }
    private static void FigureDisposedCityKingdomKeepsCurrent()
    {
        Kingdom legal = Kingdom(2), current = Kingdom(3);
        Actor actor = Actor(legal, current);
        legal.Dispose();
        DrawFigure(actor);
        Equal("3", string.Join(",", DynamicSprites.ColorOwners), "disposed city owner does not block current owner");
    }
    private static void FigureBothDisposedUsesUncoloredIcon()
    {
        Kingdom legal = Kingdom(2), current = Kingdom(3);
        Actor actor = Actor(legal, current);
        legal.Dispose(); current.Dispose();
        QuantumSpriteAsset output = DrawFigure(actor);
        Equal(0, DynamicSprites.ColorOwners.Count, "no destroyed kingdom is queried for a color");
        Equal("civ/icons/minimap_figure", output.group_system.Drawn.Single().Sprite.Path, "uncolored figure remains visible");
    }
    private static void FigureUninitializedAssetFallsBackToCity()
    {
        Kingdom legal = Kingdom(2), current = Kingdom(3);
        current.asset = null;
        DrawFigure(Actor(legal, current));
        Equal("2", string.Join(",", DynamicSprites.ColorOwners), "incomplete kingdom is not used for coloring");
    }
    private static void HeirDisposedCandidateIsRemoved()
    {
        Kingdom legal = RegisterHeir();
        legal.Dispose();
        var output = new QuantumSpriteAsset();
        AW_HeirMinimapPatch.DrawKings_Heir_Postfix(output);
        Equal(0, output.group_system.Drawn.Count, "disposed registered kingdom is skipped");
        Equal(0, HeirMinimapMarkerIndex.GetCandidateKingdomIds().Count, "stale candidate removed");
    }
    private static void HeirUninitializedCandidateIsRemoved()
    {
        Kingdom legal = RegisterHeir();
        legal.asset = null;
        AW_HeirMinimapPatch.DrawKings_Heir_Postfix(new());
        Equal(0, HeirMinimapMarkerIndex.GetCandidateKingdomIds().Count, "missing asset never reaches isCiv");
    }
    private static void HeirDisposedCurrentKingdomUsesRegisteredKingdom()
    {
        Kingdom legal = RegisterHeir();
        var current = Kingdom(3);
        current.Dispose();
        World.world.units.get(11).kingdom = current;
        AW_HeirMinimapPatch.DrawKings_Heir_Postfix(new());
        Equal("2", string.Join(",", DynamicSprites.ColorOwners), "heir uses valid registered kingdom");
    }
    private static void HeirValidCurrentKingdomRetainsPriority()
    {
        RegisterHeir();
        World.world.units.get(11).kingdom = Kingdom(3);
        AW_HeirMinimapPatch.DrawKings_Heir_Postfix(new());
        Equal("3", string.Join(",", DynamicSprites.ColorOwners), "current kingdom priority is preserved");
    }
    private static void DisabledMarkersRemainDisabled()
    {
        RegisterHeir();
        PlayerConfig.Enabled = false;
        var output = new QuantumSpriteAsset();
        AW_HeirMinimapPatch.DrawKings_Heir_Postfix(output);
        Equal(0, output.group_system.Drawn.Count, "disabled marker option is preserved");
    }
    private static void InitialIndexSkipsIncompleteKingdom()
    {
        Kingdom legal = Kingdom(2);
        legal.data.HeirId = 11;
        legal.asset = null;
        World.world.kingdoms.Entries[2] = legal;
        AW_HeirMinimapPatch.DrawKings_Heir_Postfix(new());
        Equal(0, HeirMinimapMarkerIndex.GetCandidateKingdomIds().Count, "initial load ignores kingdoms without assets");
    }
    private static void IndexRefreshRemovesIncompleteKingdom()
    {
        Kingdom legal = RegisterHeir();
        legal.asset = null;
        HeirMinimapMarkerIndex.Refresh(legal);
        Equal(0, HeirMinimapMarkerIndex.GetCandidateKingdomIds().Count, "refresh removes invalid presentation candidates");
    }
    private static Kingdom RegisterHeir()
    {
        var kingdom = Kingdom(2);
        kingdom.data.HeirId = 11;
        World.world.kingdoms.Entries[2] = kingdom;
        World.world.units.Entries[11] = Actor(kingdom, kingdom);
        Equal(1, HeirMinimapMarkerIndex.GetCandidateKingdomIds().Count, "live candidate initially indexed");
        return kingdom;
    }
    private static QuantumSpriteAsset DrawFigure(Actor actor)
    {
        World.world.units.visible_units_with_favorite = new() { array = new[] { actor }, count = 1 };
        var output = new QuantumSpriteAsset();
        Equal(false, AW_FigurePatch.DrawFavoritesMap_Figure_Prefix(output), "production prefix draws favorites");
        Equal(1, output.group_system.Drawn.Count, "one marker drawn");
        return output;
    }
    private static Kingdom Kingdom(long id)
    {
        var kingdom = new Kingdom { data = new() { id = id } };
        kingdom.capital = new City { kingdom = kingdom };
        return kingdom;
    }
    private static Actor Actor(Kingdom legal, Kingdom current) => new() { city = legal.capital, kingdom = current };
    private static void Equal<T>(T expected, T actual, string context)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException($"{context}: expected {expected}, got {actual}");
    }
}
