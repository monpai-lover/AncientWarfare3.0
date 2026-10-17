using System.Collections.Generic;

namespace AncientWarfare3.core.naming
{
    internal static class AWActorNamingContext
    {
        internal static Culture ResolveCulture(Actor pActor)
        {
            if (pActor?.data == null) return null;
            if (pActor.culture?.data != null) return pActor.culture;
            foreach (Actor parent in GetParents(pActor))
                if (parent.culture?.data != null) return parent.culture;
            return null;
        }

        internal static IEnumerable<Actor> GetParents(Actor pActor)
        {
            // The native iterator dereferences World.world.units on its
            // first MoveNext, including actors with no recorded parents.
            if (pActor?.data == null || World.world?.units == null)
                yield break;
            foreach (Actor parent in pActor.getParents())
                if (parent?.data != null) yield return parent;
        }
    }
}
