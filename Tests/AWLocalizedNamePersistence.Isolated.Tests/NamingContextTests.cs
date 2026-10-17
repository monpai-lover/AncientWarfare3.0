using AncientWarfare3.core.naming;

internal static class NamingContextTests
{
    internal static void Run()
    {
        try
        {
            var actor = new Actor();
            World.world = null;
            Equal("species_name", AWNameParameterGetters.ResolveNameTemplate(actor, MetaType.Unit),
                "missing world uses species template without enumerating parents");
            World.world = new World { units = null };
            Equal("species_name", AWNameParameterGetters.ResolveNameTemplate(actor, MetaType.Unit),
                "loading world without unit manager uses species template");
            actor.culture = new Culture();
            Equal("culture_name", AWNameParameterGetters.ResolveNameTemplate(actor, MetaType.Unit),
                "existing actor culture remains usable before parent manager is ready");
            actor.culture.data = null;
            Equal("species_name", AWNameParameterGetters.ResolveNameTemplate(actor, MetaType.Unit),
                "disposed culture cannot be dereferenced through the actor");

            World.world = new World();
            actor.culture = null;
            actor.data.parent_id_1 = 1;
            actor.data.parent_id_2 = 2;
            World.world.units.Actors[1] = new Actor { data = null, culture = new Culture() };
            World.world.units.Actors[2] = new Actor { culture = new Culture { Template = "parent_name" } };
            Equal("parent_name", AWNameParameterGetters.ResolveNameTemplate(actor, MetaType.Unit),
                "invalid first parent cannot block valid second-parent culture");
            actor.data = null;
            Equal<string>(null, AWNameParameterGetters.ResolveNameTemplate(actor, MetaType.Unit),
                "disposed actors do not invoke the native parent iterator");
            AssertSharedParentBoundary();
        }
        finally { World.world = new World(); }
    }

    private static void AssertSharedParentBoundary()
    {
        string root = Environment.CurrentDirectory;
        string tradition = File.ReadAllText(Path.Combine(root, "Code", "core", "naming",
            "AWCultureNamingTraditionService.cs"));
        string projection = File.ReadAllText(Path.Combine(root, "Code", "core", "naming",
            "AWLocalizedNameService.cs"));
        if (!tradition.Contains("AWActorNamingContext.ResolveCulture(pActor)", StringComparison.Ordinal) ||
            !projection.Contains("AWActorNamingContext.GetParents(pActor)", StringComparison.Ordinal) ||
            tradition.Contains("pActor.getParents()", StringComparison.Ordinal) ||
            projection.Contains("pActor.getParents()", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Culture selection and inherited-family projection must share the tested parent-manager boundary.");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }
}
