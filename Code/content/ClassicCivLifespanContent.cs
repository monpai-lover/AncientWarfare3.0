using System;

namespace AncientWarfare3.content
{
    internal static class ClassicCivLifespanContent
    {
        internal const float TargetLifespan = 70f;

        private static readonly string[] TargetRaceIds =
        {
            "human",
            "elf",
            "dwarf"
        };

        public static void Init()
        {
            for (int index = 0; index < TargetRaceIds.Length; index++)
            {
                string raceId = TargetRaceIds[index];
                ActorAsset actor = AssetManager.actor_library.get(raceId);
                if (actor == null)
                {
                    ModClass.LogWarning("[classic lifespan] Missing actor asset: " +
                                        raceId);
                    continue;
                }

                SetGenomeLifespan(actor);
            }
        }

        private static void SetGenomeLifespan(ActorAsset pActor)
        {
            if (pActor?.genome_parts == null) return;

            GenomePart existing = default(GenomePart);
            bool found = false;
            foreach (GenomePart part in pActor.genome_parts)
            {
                if (!string.Equals(part.id, "lifespan",
                        StringComparison.Ordinal)) continue;
                existing = part;
                found = true;
                break;
            }

            if (found) pActor.genome_parts.Remove(existing);
            pActor.genome_parts.Add(new GenomePart("lifespan", TargetLifespan));
        }
    }
}
