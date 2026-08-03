using System;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal sealed class LineageBirthArchiveWrite
    {
        internal LineageBirthArchiveWrite(ActorArchiveTableItem pChild,
            long pParentSlot1, long pParentSlot2, double pCreatedTime)
        {
            if (pChild == null)
                throw new ArgumentNullException(nameof(pChild));

            Child = Clone(pChild);
            ParentSlot1 = pParentSlot1;
            ParentSlot2 = pParentSlot2;
            CreatedTime = pCreatedTime;
        }

        internal ActorArchiveTableItem Child { get; }
        internal long ParentSlot1 { get; }
        internal long ParentSlot2 { get; }
        internal double CreatedTime { get; }

        private static ActorArchiveTableItem Clone(ActorArchiveTableItem pRow)
        {
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

    internal readonly struct LineageBirthArchiveOutcome
    {
        internal LineageBirthArchiveOutcome(long pChildId,
            bool pArchiveWritten, bool pParentSlot1Written,
            bool pParentSlot2Written)
        {
            ChildId = pChildId;
            ArchiveWritten = pArchiveWritten;
            ParentSlot1Written = pParentSlot1Written;
            ParentSlot2Written = pParentSlot2Written;
        }

        internal long ChildId { get; }
        internal bool ArchiveWritten { get; }
        internal bool ParentSlot1Written { get; }
        internal bool ParentSlot2Written { get; }
    }
}
