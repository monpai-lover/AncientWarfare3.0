using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class LineageBirthArchivePersistence
    {
        private const string ActorArchive = "ActorArchive";
        private const string FamilyEdge = "FamilyEdge";

        internal static LineageBirthArchiveOutcome Execute(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            LineageBirthArchiveWrite pWrite)
        {
            if (pDb == null)
                throw new ArgumentNullException(nameof(pDb));
            if (pTransaction == null)
                throw new ArgumentNullException(nameof(pTransaction));
            if (pWrite == null)
                throw new ArgumentNullException(nameof(pWrite));

            bool archive = UpsertActorArchive(pDb, pTransaction,
                pWrite.Child);
            bool first = UpsertParentEdge(pDb, pTransaction,
                pWrite.Child.id, pWrite.ParentSlot1, 1,
                pWrite.Child.lineage_id, pWrite.CreatedTime);
            bool second = UpsertParentEdge(pDb, pTransaction,
                pWrite.Child.id, pWrite.ParentSlot2, 2,
                pWrite.Child.lineage_id, pWrite.CreatedTime);
            return new LineageBirthArchiveOutcome(pWrite.Child.id, archive,
                first, second);
        }

        private static bool UpsertActorArchive(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, ActorArchiveTableItem pChild)
        {
            using var update = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            update.CommandText = "UPDATE " + ActorArchive + " SET " +
                "GIVEN_NAME=@given,DISPLAY_NAME=@display," +
                "FAMILY_NAME=@family,CLAN_NAME=@clan," +
                "LINEAGE_ID=@lineage,SHI_ID=@shi,ASSET_ID=@asset," +
                "SUBSPECIES_ID=@subspeciesId," +
                "SUBSPECIES_NAME=@subspeciesName,SEX=@sex,STATUS=@status," +
                "KINGDOM_ID=@kingdomId,KINGDOM_NAME=@kingdomName," +
                "KINGDOM_COLOR=@kingdomColor,CITY_ID=@cityId," +
                "CITY_NAME=@cityName,SOCIAL_TITLE=@socialTitle," +
                "SOCIAL_TITLE_COLOR=@socialTitleColor," +
                "ORIGINAL_CLAN_ID=@originalClanId," +
                "CLAN_COLOR_TEXT=@clanColorText," +
                "CLAN_COLOR_ID=@clanColorId," +
                "CLAN_BANNER_ICON_ID=@clanBannerIconId," +
                "CLAN_BANNER_BACKGROUND_ID=@clanBannerBackgroundId," +
                "PARENT_ID_1=@parent1,PARENT_ID_2=@parent2," +
                "GENERATION=@generation,NOBLE_DISTANCE=@nobleDistance," +
                "EVER_NOBLE_BLOOD=@everNobleBlood," +
                "NOBLE_ORIGIN_ACTOR_ID=@nobleOriginActorId," +
                "NOBLE_ORIGIN_NAME=@nobleOriginName," +
                "NOBLE_ORIGIN_DISTANCE=@nobleOriginDistance," +
                "BIRTH_TIME=@birthTime,DEATH_TIME=@deathTime," +
                "DEATH_CAUSE=@deathCause,IS_ALIVE=@isAlive," +
                "NAME_INTEGRATED=@nameIntegrated,HEAD=@head,SKIN=@skin," +
                "SKIN_SET=@skinSet,AGE_OVERGROWTH=@ageOvergrowth," +
                "PHENOTYPE_INDEX=@phenotypeIndex," +
                "PHENOTYPE_SHADE=@phenotypeShade," +
                "FOUNDED_BRANCH_SHI_ID=@foundedBranchShiId WHERE ID=@id";
            BindActor(update, pChild);
            int affected = update.ExecuteNonQuery();
            if (affected == 1) return true;
            if (affected != 0)
                throw new InvalidOperationException(
                    "lineage birth archive update affected multiple rows");

            using var insert = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            insert.CommandText = "INSERT INTO " + ActorArchive + " (" +
                "ID,GIVEN_NAME,DISPLAY_NAME,FAMILY_NAME,CLAN_NAME," +
                "LINEAGE_ID,SHI_ID,ASSET_ID,SUBSPECIES_ID," +
                "SUBSPECIES_NAME,SEX,STATUS,KINGDOM_ID,KINGDOM_NAME," +
                "KINGDOM_COLOR,CITY_ID,CITY_NAME,SOCIAL_TITLE," +
                "SOCIAL_TITLE_COLOR,ORIGINAL_CLAN_ID,CLAN_COLOR_TEXT," +
                "CLAN_COLOR_ID,CLAN_BANNER_ICON_ID," +
                "CLAN_BANNER_BACKGROUND_ID,PARENT_ID_1,PARENT_ID_2," +
                "GENERATION,NOBLE_DISTANCE,EVER_NOBLE_BLOOD," +
                "NOBLE_ORIGIN_ACTOR_ID,NOBLE_ORIGIN_NAME," +
                "NOBLE_ORIGIN_DISTANCE,BIRTH_TIME,DEATH_TIME," +
                "DEATH_CAUSE,IS_ALIVE,NAME_INTEGRATED,HEAD,SKIN," +
                "SKIN_SET,AGE_OVERGROWTH,PHENOTYPE_INDEX," +
                "PHENOTYPE_SHADE,FOUNDED_BRANCH_SHI_ID) VALUES (" +
                "@id,@given,@display,@family,@clan,@lineage,@shi,@asset," +
                "@subspeciesId,@subspeciesName,@sex,@status,@kingdomId," +
                "@kingdomName,@kingdomColor,@cityId,@cityName," +
                "@socialTitle,@socialTitleColor,@originalClanId," +
                "@clanColorText,@clanColorId,@clanBannerIconId," +
                "@clanBannerBackgroundId,@parent1,@parent2,@generation," +
                "@nobleDistance,@everNobleBlood,@nobleOriginActorId," +
                "@nobleOriginName,@nobleOriginDistance,@birthTime," +
                "@deathTime,@deathCause,@isAlive,@nameIntegrated,@head," +
                "@skin,@skinSet,@ageOvergrowth,@phenotypeIndex," +
                "@phenotypeShade,@foundedBranchShiId)";
            BindActor(insert, pChild);
            return RequireOne(insert.ExecuteNonQuery(),
                "lineage birth archive insert");
        }

        private static bool UpsertParentEdge(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pChildId, long pParentId,
            int pParentSlot, long pChildLineageId, double pCreatedTime)
        {
            long edgeId = checked(pChildId * 10L + pParentSlot);
            using var update = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            update.CommandText = "UPDATE " + FamilyEdge + " SET " +
                "CHILD_ID=@child,PARENT_ID=@parent,PARENT_SLOT=@slot," +
                "CHILD_LINEAGE_ID=@lineage,CREATED_TIME=@time " +
                "WHERE EDGE_ID=@edge";
            BindEdge(update, edgeId, pChildId, pParentId, pParentSlot,
                pChildLineageId, pCreatedTime);
            int affected = update.ExecuteNonQuery();
            if (affected == 1) return true;
            if (affected != 0)
                throw new InvalidOperationException(
                    "lineage birth edge update affected multiple rows");

            using var insert = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            insert.CommandText = "INSERT INTO " + FamilyEdge + " (" +
                "EDGE_ID,CHILD_ID,PARENT_ID,PARENT_SLOT," +
                "CHILD_LINEAGE_ID,CREATED_TIME) VALUES (" +
                "@edge,@child,@parent,@slot,@lineage,@time)";
            BindEdge(insert, edgeId, pChildId, pParentId, pParentSlot,
                pChildLineageId, pCreatedTime);
            return RequireOne(insert.ExecuteNonQuery(),
                "lineage birth parent edge insert");
        }

        private static void BindEdge(SQLiteCommand pCommand, long pEdgeId,
            long pChildId, long pParentId, int pParentSlot,
            long pChildLineageId, double pCreatedTime)
        {
            pCommand.Parameters.AddWithValue("@edge", pEdgeId);
            pCommand.Parameters.AddWithValue("@child", pChildId);
            pCommand.Parameters.AddWithValue("@parent", pParentId);
            pCommand.Parameters.AddWithValue("@slot", pParentSlot);
            pCommand.Parameters.AddWithValue("@lineage", pChildLineageId);
            pCommand.Parameters.AddWithValue("@time", pCreatedTime);
        }

        private static void BindActor(SQLiteCommand pCommand,
            ActorArchiveTableItem pRow)
        {
            pCommand.Parameters.AddWithValue("@id", pRow.id);
            pCommand.Parameters.AddWithValue("@given", Text(pRow.given_name));
            pCommand.Parameters.AddWithValue("@display",
                Text(pRow.display_name));
            pCommand.Parameters.AddWithValue("@family",
                Text(pRow.family_name));
            pCommand.Parameters.AddWithValue("@clan", Text(pRow.clan_name));
            pCommand.Parameters.AddWithValue("@lineage", pRow.lineage_id);
            pCommand.Parameters.AddWithValue("@shi", pRow.shi_id);
            pCommand.Parameters.AddWithValue("@asset", Text(pRow.asset_id));
            pCommand.Parameters.AddWithValue("@subspeciesId",
                pRow.subspecies_id);
            pCommand.Parameters.AddWithValue("@subspeciesName",
                Text(pRow.subspecies_name));
            pCommand.Parameters.AddWithValue("@sex", pRow.sex);
            pCommand.Parameters.AddWithValue("@status", Text(pRow.status));
            pCommand.Parameters.AddWithValue("@kingdomId", pRow.kingdom_id);
            pCommand.Parameters.AddWithValue("@kingdomName",
                Text(pRow.kingdom_name));
            pCommand.Parameters.AddWithValue("@kingdomColor",
                Text(pRow.kingdom_color));
            pCommand.Parameters.AddWithValue("@cityId", pRow.city_id);
            pCommand.Parameters.AddWithValue("@cityName", Text(pRow.city_name));
            pCommand.Parameters.AddWithValue("@socialTitle",
                Text(pRow.social_title));
            pCommand.Parameters.AddWithValue("@socialTitleColor",
                Text(pRow.social_title_color));
            pCommand.Parameters.AddWithValue("@originalClanId",
                pRow.original_clan_id);
            pCommand.Parameters.AddWithValue("@clanColorText",
                Text(pRow.clan_color_text));
            pCommand.Parameters.AddWithValue("@clanColorId",
                pRow.clan_color_id);
            pCommand.Parameters.AddWithValue("@clanBannerIconId",
                pRow.clan_banner_icon_id);
            pCommand.Parameters.AddWithValue("@clanBannerBackgroundId",
                pRow.clan_banner_background_id);
            pCommand.Parameters.AddWithValue("@parent1", pRow.parent_id_1);
            pCommand.Parameters.AddWithValue("@parent2", pRow.parent_id_2);
            pCommand.Parameters.AddWithValue("@generation", pRow.generation);
            pCommand.Parameters.AddWithValue("@nobleDistance",
                pRow.noble_distance);
            pCommand.Parameters.AddWithValue("@everNobleBlood",
                pRow.ever_noble_blood);
            pCommand.Parameters.AddWithValue("@nobleOriginActorId",
                pRow.noble_origin_actor_id);
            pCommand.Parameters.AddWithValue("@nobleOriginName",
                Text(pRow.noble_origin_name));
            pCommand.Parameters.AddWithValue("@nobleOriginDistance",
                pRow.noble_origin_distance);
            pCommand.Parameters.AddWithValue("@birthTime", pRow.birth_time);
            pCommand.Parameters.AddWithValue("@deathTime", pRow.death_time);
            pCommand.Parameters.AddWithValue("@deathCause",
                Text(pRow.death_cause));
            pCommand.Parameters.AddWithValue("@isAlive", pRow.is_alive);
            pCommand.Parameters.AddWithValue("@nameIntegrated",
                pRow.name_integrated);
            pCommand.Parameters.AddWithValue("@head", pRow.head);
            pCommand.Parameters.AddWithValue("@skin", pRow.skin);
            pCommand.Parameters.AddWithValue("@skinSet", pRow.skin_set);
            pCommand.Parameters.AddWithValue("@ageOvergrowth",
                pRow.age_overgrowth);
            pCommand.Parameters.AddWithValue("@phenotypeIndex",
                pRow.phenotype_index);
            pCommand.Parameters.AddWithValue("@phenotypeShade",
                pRow.phenotype_shade);
            pCommand.Parameters.AddWithValue("@foundedBranchShiId",
                pRow.founded_branch_shi_id);
        }

        private static bool RequireOne(int pAffected, string pOperation)
        {
            if (pAffected != 1)
                throw new InvalidOperationException(pOperation +
                    " did not affect exactly one row");
            return true;
        }

        private static string Text(string pValue)
        {
            return pValue ?? string.Empty;
        }
    }
}
