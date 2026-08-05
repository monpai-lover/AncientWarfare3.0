using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class ActorArchivePendingStore
    {
        private static readonly HistoricalPendingStateStore<long,
            ActorArchiveTableItem> Store = new HistoricalPendingStateStore<long,
                ActorArchiveTableItem>(Clone);

        public static void Publish(long pActorId, long pSequence,
            ActorArchiveTableItem pSnapshot)
        {
            Store.Publish(pActorId, pSequence, pSnapshot);
        }

        public static bool TryRead(long pActorId,
            out ActorArchiveTableItem pSnapshot)
        {
            return Store.TryRead(pActorId, out pSnapshot);
        }

        public static void Complete(long pActorId, long pSequence)
        {
            Store.Complete(pActorId, pSequence);
        }

        public static void Clear()
        {
            Store.Clear();
        }

        private static ActorArchiveTableItem Clone(ActorArchiveTableItem pRow)
        {
            if (pRow == null) return null;
            return new ActorArchiveTableItem
            {
                id = pRow.id,
                given_name = pRow.given_name,
                display_name = pRow.display_name,
                family_name = pRow.family_name,
                clan_name = pRow.clan_name,
                lineage_id = pRow.lineage_id,
                shi_id = pRow.shi_id,
                asset_id = pRow.asset_id,
                subspecies_id = pRow.subspecies_id,
                subspecies_name = pRow.subspecies_name,
                sex = pRow.sex,
                status = pRow.status,
                kingdom_id = pRow.kingdom_id,
                kingdom_name = pRow.kingdom_name,
                kingdom_color = pRow.kingdom_color,
                city_id = pRow.city_id,
                city_name = pRow.city_name,
                social_title = pRow.social_title,
                social_title_color = pRow.social_title_color,
                primary_ceremonial_title = pRow.primary_ceremonial_title,
                original_clan_id = pRow.original_clan_id,
                clan_color_text = pRow.clan_color_text,
                clan_color_id = pRow.clan_color_id,
                clan_banner_icon_id = pRow.clan_banner_icon_id,
                clan_banner_background_id = pRow.clan_banner_background_id,
                parent_id_1 = pRow.parent_id_1,
                parent_id_2 = pRow.parent_id_2,
                generation = pRow.generation,
                noble_distance = pRow.noble_distance,
                ever_noble_blood = pRow.ever_noble_blood,
                noble_origin_actor_id = pRow.noble_origin_actor_id,
                noble_origin_name = pRow.noble_origin_name,
                noble_origin_distance = pRow.noble_origin_distance,
                birth_time = pRow.birth_time,
                death_time = pRow.death_time,
                death_cause = pRow.death_cause,
                is_alive = pRow.is_alive,
                name_integrated = pRow.name_integrated,
                head = pRow.head,
                skin = pRow.skin,
                skin_set = pRow.skin_set,
                age_overgrowth = pRow.age_overgrowth,
                phenotype_index = pRow.phenotype_index,
                phenotype_shade = pRow.phenotype_shade,
                founded_branch_shi_id = pRow.founded_branch_shi_id
            };
        }
    }
}
