using System;
using System.Collections.Generic;
using System.Reflection;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolActorDestroyQueue
    {
        private static readonly FieldInfo ActorDestroyQueueField =
            typeof(SimSystemManager<Actor, ActorData>).GetField("_to_destroy_objects",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        internal static bool Queue(Actor pActor, string pFailureContext)
        {
            try
            {
                ActorManager units = World.world?.units;
                if (pActor?.data == null || units == null ||
                    units.get(pActor.data.id) != pActor) return false;
                if (!(ActorDestroyQueueField?.GetValue(units) is HashSet<Actor> queue))
                    throw new InvalidOperationException("actor destroy queue unavailable");
                queue.Add(pActor);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning((pFailureContext ?? "School actor destroy requeue failed") +
                                    ": " + error.Message);
                return false;
            }
        }
    }
}
