using AncientWarfare3.core.db;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class FamilyTreeSnapshotOverlayService
    {
        public static void ReconcileReadModel(FamilyTreeNode pNode,
            LineageTreeNodeSnapshot pSnapshot)
        {
            if (pNode == null || pSnapshot == null) return;

            bool hasPendingArchive = ActorArchivePendingStore.TryRead(
                pNode.id, out ActorArchiveTableItem pendingArchive) &&
                pendingArchive != null;
            if (hasPendingArchive)
                ApplyPendingArchive(pNode, pendingArchive);

            Actor live = World.world?.units?.get(pNode.id);
            bool liveKnownDead = live?.data != null &&
                                 (!live.isAlive() || live.isRekt());
            bool runtimeAuthorityReady = Config.game_loaded &&
                                         !SmoothLoader.isLoading() &&
                                         !AW3MultiplayerReplicaScope.IsApplying &&
                                         !AW3MultiplayerReplicaScope.IsReplicaSession;
            bool pendingArchiveAlive = pendingArchive != null &&
                                       pendingArchive.is_alive != 0 &&
                                       pendingArchive.death_time <= 0d;
            pNode.is_alive = FamilyTreeSnapshotOverlayRules.ResolveAlive(
                pSnapshot.IsAlive, hasPendingArchive,
                pendingArchiveAlive, liveKnownDead,
                runtimeAuthorityReady, live == null);
            RulerAppellationService.ProjectFamilyTreeRitualAppellation(pNode);
        }

        private static void ApplyPendingArchive(FamilyTreeNode pNode,
            ActorArchiveTableItem pArchive)
        {
            if (!string.IsNullOrWhiteSpace(pArchive.asset_id))
                pNode.asset_id = pArchive.asset_id;
            pNode.subspecies_id = pArchive.subspecies_id;
            ShiBranchInfo branch = pArchive.shi_id >= 0L
                ? LineageQuery.GetShiBranchInfo(pArchive.shi_id)
                : null;
            pNode.display_name = LineageDisplayNameRules.ProjectArchive(
                pArchive.display_name, pArchive.given_name,
                pArchive.family_name, pArchive.clan_name, pArchive.status,
                pArchive.sex == 0, pArchive.name_integrated != 0,
                branch?.naming_profile, branch?.western_naming_tradition,
                branch?.origin_city_name ?? branch?.origin_city_chinese_name,
                branch?.display_stem);
            pNode.sex = pArchive.sex;
            pNode.status = pArchive.status ?? string.Empty;
            pNode.clan_name = pArchive.clan_name ?? string.Empty;
            pNode.shi_id = pArchive.shi_id;
            pNode.noble_distance = pArchive.noble_distance;
            pNode.birth_time = pArchive.birth_time;
            pNode.death_time = pArchive.death_time;
            pNode.kingdom_id = pArchive.kingdom_id;
            pNode.kingdom_name = pArchive.kingdom_name ?? string.Empty;
            pNode.kingdom_color = pArchive.kingdom_color ?? string.Empty;
            pNode.original_clan_id = pArchive.original_clan_id;
            pNode.clan_color_text = pArchive.clan_color_text ?? string.Empty;
            pNode.clan_color_id = pArchive.clan_color_id;
            pNode.clan_banner_icon_id = pArchive.clan_banner_icon_id;
            pNode.clan_banner_background_id =
                pArchive.clan_banner_background_id;
            pNode.city_name = pArchive.city_name ?? string.Empty;
            pNode.social_title = pArchive.social_title ?? string.Empty;
            pNode.social_title_color =
                pArchive.social_title_color ?? string.Empty;
            pNode.head = pArchive.head;
            pNode.skin = pArchive.skin;
            pNode.skin_set = pArchive.skin_set;
            pNode.age_overgrowth = pArchive.age_overgrowth;
            pNode.phenotype_index = pArchive.phenotype_index;
            pNode.phenotype_shade = pArchive.phenotype_shade;
            pNode.founded_branch_shi_id = pArchive.founded_branch_shi_id;
            pNode.death_cause = pArchive.death_cause ?? string.Empty;
        }
    }
}
