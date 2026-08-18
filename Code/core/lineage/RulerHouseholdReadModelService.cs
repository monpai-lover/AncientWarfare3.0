using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class RulerHouseholdReadModelService
    {
        public static RulerHouseholdSnapshot Build(Kingdom pKingdom)
        {
            var snapshot = new RulerHouseholdSnapshot();
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.king?.data == null)
            {
                snapshot.Reason = "invalid_household_realm";
                return snapshot;
            }

            SQLiteConnection db =
                LineageArchiveManager.Instance?.OperatingDB;
            if (db == null)
            {
                snapshot.Reason = "household_not_ready";
                return snapshot;
            }

            Actor ruler = pKingdom.king;
            RulerHouseholdRealmTier tier =
                RulerHouseholdService.ResolveRealmTier(pKingdom);
            if (tier == RulerHouseholdRealmTier.Empire)
                RulerHouseholdService.NormalizeImperialRanks(pKingdom);
            int capacity = RulerHouseholdRules.ConsortCapacity(tier);
            IReadOnlyList<RulerHouseholdRecord> records =
                new RulerHouseholdQuery(db).ReadActiveByRuler(
                    ruler.data.id, capacity);

            snapshot.Available = true;
            snapshot.Reason = "";
            snapshot.KingdomId = pKingdom.id;
            snapshot.RulerActorId = ruler.data.id;
            snapshot.RulerName = ruler.getName() ?? "";
            snapshot.RulerTitle =
                RulerAppellationService.GetFullLivingAppellation(pKingdom);
            snapshot.RealmName =
                RulerAppellationService.GetProjectedStateName(pKingdom);
            snapshot.RulerIsFemale = ruler.isSexFemale();
            snapshot.ConsortCapacity = capacity;

            Actor vanillaSpouse = LivingMutualSpouse(ruler);
            RulerHouseholdRecord principalRecord =
                DeduplicatePrincipalWife(vanillaSpouse, records);
            if (vanillaSpouse?.data != null)
                snapshot.PrincipalWife = BuildRow(db, vanillaSpouse,
                    principalRecord, pKingdom.id, tier,
                    RulerHouseholdKind.PrincipalWife,
                    snapshot.RulerIsFemale);
            else if (principalRecord != null)
                snapshot.PrincipalWife = BuildRow(db,
                    FindActor(principalRecord.PartnerActorId),
                    principalRecord, principalRecord.SourceKingdomId, tier,
                    RulerHouseholdKind.PrincipalWife,
                    snapshot.RulerIsFemale);

            long principalId = snapshot.PrincipalWife?.ActorId ?? -1L;
            for (int i = 0; i < records.Count; i++)
            {
                RulerHouseholdRecord record = records[i];
                if (record == null ||
                    record.Kind != RulerHouseholdKind.Consort ||
                    record.PartnerActorId == principalId)
                    continue;
                snapshot.Consorts.Add(BuildRow(db,
                    FindActor(record.PartnerActorId), record,
                    record.SourceKingdomId, tier,
                    RulerHouseholdKind.Consort,
                    snapshot.RulerIsFemale));
            }
            return snapshot;
        }

        private static RulerHouseholdRecord DeduplicatePrincipalWife(
            Actor pVanillaSpouse,
            IReadOnlyList<RulerHouseholdRecord> pRecords)
        {
            RulerHouseholdRecord fallback = null;
            long spouseId = pVanillaSpouse?.data?.id ?? -1L;
            for (int i = 0; i < pRecords.Count; i++)
            {
                RulerHouseholdRecord record = pRecords[i];
                if (record == null ||
                    record.Kind != RulerHouseholdKind.PrincipalWife)
                    continue;
                if (record.PartnerActorId == spouseId) return record;
                fallback ??= record;
            }
            return spouseId >= 0L ? null : fallback;
        }

        private static RulerHouseholdDisplayRow BuildRow(SQLiteConnection pDb,
            Actor pActor, RulerHouseholdRecord pRecord,
            long pFallbackOriginKingdomId, RulerHouseholdRealmTier pTier,
            RulerHouseholdKind pKind, bool pRulerIsFemale)
        {
            long actorId = pActor?.data?.id ??
                           pRecord?.PartnerActorId ?? -1L;
            ActorArchiveTableItem archive = actorId >= 0L
                ? LineageArchiveReader.ReadRow(actorId)
                : null;
            string name = pActor?.getName();
            if (string.IsNullOrWhiteSpace(name))
                name = !string.IsNullOrWhiteSpace(archive?.display_name)
                    ? archive.display_name
                    : archive?.given_name ?? "";
            string lineage = BuildLineageLabel(archive);
            long originId = pRecord?.SourceKingdomId ??
                            pFallbackOriginKingdomId;
            return new RulerHouseholdDisplayRow
            {
                RelationshipId = pRecord?.RelationshipId ?? -1L,
                ActorId = actorId,
                ActorName = name ?? "",
                TitleKey = ResolveTitleKey(pRecord, pTier, pKind,
                    pRulerIsFemale),
                RankCode = pRecord?.RankCode ?? "",
                OriginRealmName = ReadRealmName(pDb, originId),
                LineageLabel = lineage,
                Age = SafeAge(pActor),
                EntryYear = pRecord?.StartYear ?? -1,
                LivingChildren = CountLivingChildren(pDb, actorId),
                Alive = pActor?.data != null && pActor.isAlive() &&
                        !pActor.isRekt(),
                Kind = pKind
            };
        }

        private static string ResolveTitleKey(
            RulerHouseholdRecord pRecord, RulerHouseholdRealmTier pTier,
            RulerHouseholdKind pKind, bool pRulerIsFemale)
        {
            if (!pRulerIsFemale && pTier == RulerHouseholdRealmTier.Empire)
            {
                string fixedTitle = RulerHouseholdRankRules.TitleKey(
                    pRecord?.RankCode);
                if (!string.IsNullOrEmpty(fixedTitle)) return fixedTitle;
            }
            return RulerHouseholdRules.TitleKey(pTier, pKind,
                pRulerIsFemale);
        }

        private static string BuildLineageLabel(ActorArchiveTableItem pRow)
        {
            if (pRow == null) return "";
            string branch = AncestryDisplayRules.FormatLineageLabel(
                pRow.city_name, pRow.clan_name);
            string family = (pRow.family_name ?? "").Trim();
            if (string.IsNullOrEmpty(family)) return branch;
            if (string.IsNullOrEmpty(branch)) return family;
            return family + " / " + branch;
        }

        private static int CountLivingChildren(SQLiteConnection pDb,
            long pActorId)
        {
            if (pDb == null || pActorId < 0L) return 0;
            using var command = new SQLiteCommand(
                "SELECT COUNT(*) FROM ActorArchive WHERE IS_ALIVE=1 AND " +
                "(PARENT_ID_1=@id OR PARENT_ID_2=@id)", pDb);
            command.Parameters.AddWithValue("@id", pActorId);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static string ReadRealmName(SQLiteConnection pDb,
            long pKingdomId)
        {
            if (pKingdomId < 0L) return "";
            Kingdom live = World.world?.kingdoms?.get(pKingdomId);
            if (live?.data != null && !live.isRekt())
                return RulerAppellationService.GetProjectedStateName(live);
            using var command = new SQLiteCommand(
                "SELECT IFNULL(KINGDOM_NAME,'') FROM KingdomArchive " +
                "WHERE KINGDOM_ID=@id LIMIT 1", pDb);
            command.Parameters.AddWithValue("@id", pKingdomId);
            return Convert.ToString(command.ExecuteScalar()) ?? "";
        }

        private static Actor LivingMutualSpouse(Actor pRuler)
        {
            Actor spouse = pRuler?.lover;
            return spouse?.data != null && spouse.isAlive() &&
                   !spouse.isRekt() && spouse.lover == pRuler
                ? spouse
                : null;
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0L) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor?.data == null ? -1 : pActor.getAge(); }
            catch { return -1; }
        }
    }
}
