using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class AncestryAnalysisService
    {
        private const int MAX_DEPTH = 8;

        public static bool HasAnalyzableAncestry(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (HasLiveAncestryData(pActor)) return true;

            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActor.data.id);
            if (row == null) return false;
            return row.lineage_id >= 0 || row.shi_id >= 0 || row.ever_noble_blood != 0 ||
                   row.parent_id_1 >= 0 || row.parent_id_2 >= 0 || row.original_clan_id >= 0 ||
                   row.subspecies_id >= 0 || !string.IsNullOrEmpty(row.subspecies_name);
        }

        private static bool HasLiveAncestryData(Actor pActor)
        {
            if (pActor?.data == null) return false;

            if (pActor.clan?.data != null || pActor.subspecies != null) return true;
            if (pActor.data.parent_id_1 >= 0 || pActor.data.parent_id_2 >= 0) return true;
            if (LineageQuery.GetParentIds(pActor.data.id).Count > 0) return true;

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pActor.data.get(LineageKeys.EVER_NOBLE_BLOOD, out bool everNoble, false);
            if (lineageId >= 0 || shiId >= 0 || everNoble) return true;

            pActor.data.get(LineageKeys.FAMILY_NAME, out string family, "");
            pActor.data.get(LineageKeys.CLAN_NAME, out string clan, "");
            return !string.IsNullOrEmpty(family) || !string.IsNullOrEmpty(clan);
        }

        public static AncestryReport BuildReport(long pActorId)
        {
            var report = new AncestryReport { actor_id = pActorId, max_depth = MAX_DEPTH };
            Actor actor = World.world?.units?.get(pActorId);
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);

            report.actor_name = NameOf(actor, row, pActorId);
            report.identity = ResolveIdentity(actor, row);
            report.noble_blood = ResolveNobleBlood(pActorId, actor, row);

            var social = new Dictionary<string, AncestryContribution>();
            AccumulateSocial(pActorId, 100f, 0, social, new HashSet<long>(), report);
            report.contributions = SortContributions(social);
            report.unknown_percent = CalculateUnknownPercent(report.contributions);

            var genetic = new Dictionary<string, AncestryContribution>();
            _ = AccumulateGenetic(pActorId, 100f, 0, genetic, new HashSet<long>());
            report.genetic_contributions = SortContributions(genetic);
            report.genetic_unknown_percent = CalculateUnknownPercent(report.genetic_contributions);
            report.autosomal_summary = BuildAutosomalSummary(report.genetic_contributions,
                report.genetic_unknown_percent);
            report.paternal_marker = ResolveDirectMarker(pActorId, pWantMale: true);
            report.maternal_marker = ResolveDirectMarker(pActorId, pWantMale: false);

            return report;
        }

        private static void AccumulateSocial(long pActorId, float pPercent, int pDepth,
            Dictionary<string, AncestryContribution> pAcc, HashSet<long> pPath, AncestryReport pReport)
        {
            if (pPercent <= 0.05f || pDepth > MAX_DEPTH || pActorId < 0 || !pPath.Add(pActorId)) return;

            List<long> parents = GetTraceParentIds(pActorId);
            if (parents.Count == 0 || pDepth == MAX_DEPTH)
            {
                AddSocialContribution(pActorId, pPercent, pAcc);
                if (pActorId != pReport.actor_id) pReport.known_ancestors++;
                pPath.Remove(pActorId);
                return;
            }

            float childShare = pPercent / parents.Count;
            foreach (long parent in parents)
                AccumulateSocial(parent, childShare, pDepth + 1, pAcc, pPath, pReport);
            pPath.Remove(pActorId);
        }

        private static bool AccumulateGenetic(long pActorId, float pPercent, int pDepth,
            Dictionary<string, AncestryContribution> pAcc, HashSet<long> pPath)
        {
            if (pPercent <= 0.05f || pDepth > MAX_DEPTH || pActorId < 0 || !pPath.Add(pActorId)) return false;

            List<long> parents = GetTraceParentIds(pActorId);
            if (parents.Count == 0 || pDepth == MAX_DEPTH)
            {
                bool known = AddGeneticContribution(pActorId, pPercent, pAcc);
                pPath.Remove(pActorId);
                return known;
            }

            float childShare = pPercent / parents.Count;
            bool addedKnown = false;
            foreach (long parent in parents)
            {
                var branch = new Dictionary<string, AncestryContribution>();
                bool branchKnown = AccumulateGenetic(parent, childShare, pDepth + 1, branch, pPath);
                if (!branchKnown)
                {
                    branch.Clear();
                    long fallbackId = pDepth > 0 ? pActorId : parent;
                    branchKnown = AddGeneticContribution(fallbackId, childShare, branch);
                    if (!branchKnown && fallbackId != parent)
                    {
                        branch.Clear();
                        branchKnown = AddGeneticContribution(parent, childShare, branch);
                    }
                }

                MergeContributions(pAcc, branch);
                addedKnown |= branchKnown;
            }

            pPath.Remove(pActorId);
            return addedKnown;
        }

        private static void AddSocialContribution(long pActorId, float pPercent,
            Dictionary<string, AncestryContribution> pAcc)
        {
            Actor actor = World.world?.units?.get(pActorId);
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            AncestryContribution c = BuildSocialContribution(actor, row, pActorId);
            AddContribution(pAcc, c, pPercent);
        }

        private static bool AddGeneticContribution(long pActorId, float pPercent,
            Dictionary<string, AncestryContribution> pAcc)
        {
            Actor actor = World.world?.units?.get(pActorId);
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            AncestryContribution c = BuildGeneticContribution(actor, row, pActorId);
            AddContribution(pAcc, c, pPercent);
            return c.kind != "unknown";
        }

        private static void MergeContributions(Dictionary<string, AncestryContribution> pAcc,
            Dictionary<string, AncestryContribution> pBranch)
        {
            foreach (AncestryContribution c in pBranch.Values)
                AddContribution(pAcc, c, c.percent);
        }

        private static void AddContribution(Dictionary<string, AncestryContribution> pAcc,
            AncestryContribution pContribution, float pPercent)
        {
            if (pAcc.TryGetValue(pContribution.key, out AncestryContribution existing))
            {
                existing.percent += pPercent;
                return;
            }

            pContribution.percent = pPercent;
            pAcc[pContribution.key] = pContribution;
        }

        private static AncestryContribution BuildSocialContribution(Actor pActor, ActorArchiveTableItem pRow,
            long pActorId)
        {
            string clan = LiveString(pActor, LineageKeys.CLAN_NAME, pRow?.clan_name);
            long shi = LiveLong(pActor, LineageKeys.SHI_ID, pRow?.shi_id ?? -1);
            if (!string.IsNullOrEmpty(clan) && shi >= 0)
                return NewContribution("shi:" + shi, clan + "\u6C0F", "shi", pActorId,
                    NameOf(pActor, pRow, pActorId), pRow?.kingdom_color ?? "");

            string family = LiveString(pActor, LineageKeys.FAMILY_NAME, pRow?.family_name);
            long lineage = LiveLong(pActor, LineageKeys.LINEAGE_ID, pRow?.lineage_id ?? -1);
            if (!string.IsNullOrEmpty(family) && lineage >= 0)
                return NewContribution("lineage:" + lineage, family + "\u59D3", "lineage", pActorId,
                    NameOf(pActor, pRow, pActorId), pRow?.kingdom_color ?? "");

            long originalClan = pActor?.clan?.data?.id ?? pRow?.original_clan_id ?? -1;
            if (originalClan >= 0)
                return NewContribution("original_clan:" + originalClan, "\u539F\u7248\u6C0F\u65CF " + originalClan,
                    "original_clan", pActorId, NameOf(pActor, pRow, pActorId),
                    pRow?.clan_color_text ?? "");

            string asset = pActor?.asset?.id ?? pRow?.asset_id ?? "";
            if (!string.IsNullOrEmpty(asset))
                return NewContribution("species:" + asset, asset, "species", pActorId,
                    NameOf(pActor, pRow, pActorId), "");

            return NewContribution("unknown:social", "\u672A\u77E5\u7956\u6E90", "unknown", -1, "", "");
        }

        private static AncestryContribution BuildGeneticContribution(Actor pActor, ActorArchiveTableItem pRow,
            long pActorId)
        {
            var subspecies = ResolveSubspecies(pActor, pRow);
            if (subspecies.id >= 0 || !string.IsNullOrEmpty(subspecies.name))
            {
                string key = subspecies.id >= 0
                    ? "subspecies:" + subspecies.id
                    : "subspecies_name:" + subspecies.name;
                string label = !string.IsNullOrEmpty(subspecies.name)
                    ? subspecies.name
                    : "\u4E9A\u79CD " + subspecies.id;
                return NewContribution(key, label, "subspecies", pActorId,
                    NameOf(pActor, pRow, pActorId), pRow?.kingdom_color ?? "");
            }

            string asset = pActor?.asset?.id ?? pRow?.asset_id ?? "";
            if (!string.IsNullOrEmpty(asset))
                return NewContribution("species:" + asset, asset, "species", pActorId,
                    NameOf(pActor, pRow, pActorId), "");

            return NewContribution("unknown:genetic", "\u672A\u77E5\u4E9A\u79CD", "unknown", -1, "", "");
        }

        private static AncestryMarker ResolveDirectMarker(long pActorId, bool pWantMale)
        {
            return ResolveDirectMarker(pActorId, pWantMale, 0, new HashSet<long>());
        }

        private static AncestryMarker ResolveDirectMarker(long pActorId, bool pWantMale, int pDistance,
            HashSet<long> pVisited)
        {
            if (pDistance >= MAX_DEPTH || pActorId < 0 || !pVisited.Add(pActorId))
                return new AncestryMarker();

            foreach (long parentId in GetTraceParentIds(pActorId))
            {
                if (!MatchesSex(parentId, pWantMale)) continue;

                Actor parent = World.world?.units?.get(parentId);
                ActorArchiveTableItem parentRow = LineageArchiveReader.ReadRow(parentId);
                var subspecies = ResolveSubspecies(parent, parentRow);
                if (subspecies.id >= 0 || !string.IsNullOrEmpty(subspecies.name))
                {
                    return new AncestryMarker
                    {
                        known = true,
                        label = !string.IsNullOrEmpty(subspecies.name)
                            ? subspecies.name
                            : "\u4E9A\u79CD " + subspecies.id,
                        source_actor_id = parentId,
                        source_actor_name = NameOf(parent, parentRow, parentId),
                        distance = pDistance + 1
                    };
                }

                AncestryMarker deeper = ResolveDirectMarker(parentId, pWantMale, pDistance + 1, pVisited);
                if (deeper.known) return deeper;
            }

            return new AncestryMarker();
        }

        private static bool MatchesSex(long pActorId, bool pWantMale)
        {
            Actor actor = World.world?.units?.get(pActorId);
            if (actor?.data != null) return actor.isSexMale() == pWantMale;

            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            if (row == null) return false;
            return pWantMale ? row.sex == 0 : row.sex == 1;
        }

        private static List<long> GetTraceParentIds(long pActorId)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();

            void Add(long id)
            {
                if (id >= 0 && seen.Add(id)) result.Add(id);
            }

            foreach (long id in LineageQuery.GetParentIds(pActorId))
                Add(id);

            if (result.Count > 0) return result;

            Actor live = World.world?.units?.get(pActorId);
            if (live?.data != null)
            {
                Add(live.data.parent_id_1);
                Add(live.data.parent_id_2);
            }

            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            if (row != null)
            {
                Add(row.parent_id_1);
                Add(row.parent_id_2);
            }

            return result;
        }

        private static (long id, string name) ResolveSubspecies(Actor pActor, ActorArchiveTableItem pRow)
        {
            long id = pActor?.subspecies?.getID() ?? pRow?.subspecies_id ?? -1L;
            string name = pActor?.subspecies?.data?.name ?? pRow?.subspecies_name ?? "";
            return (id, name ?? "");
        }

        private static List<AncestryContribution> SortContributions(Dictionary<string, AncestryContribution> pAcc)
        {
            return pAcc.Values
                .OrderBy(x => x.kind == "unknown" ? 1 : 0)
                .ThenByDescending(x => x.percent)
                .ThenBy(x => x.label)
                .ToList();
        }

        private static float CalculateUnknownPercent(List<AncestryContribution> pContributions)
        {
            if (pContributions == null || pContributions.Count == 0) return 0f;
            float known = pContributions.Where(x => x.kind != "unknown").Sum(x => x.percent);
            float explicitUnknown = pContributions.Where(x => x.kind == "unknown").Sum(x => x.percent);
            return Math.Max(0f, explicitUnknown + (100f - known - explicitUnknown));
        }

        private static string BuildAutosomalSummary(List<AncestryContribution> pContributions, float pUnknownPercent)
        {
            var parts = pContributions
                .Where(x => x.kind != "unknown")
                .OrderByDescending(x => x.percent)
                .Take(4)
                .Select(x => x.label + " " + x.percent.ToString("0.0") + "%")
                .ToList();

            if (pUnknownPercent > 0.05f)
                parts.Add("\u672A\u77E5 " + pUnknownPercent.ToString("0.0") + "%");

            return parts.Count == 0 ? "\u672A\u77E5" : string.Join(" / ", parts.ToArray());
        }

        private static NobleBloodEvidence ResolveNobleBlood(long pActorId, Actor pActor, ActorArchiveTableItem pRow)
        {
            if (pActor?.data != null)
            {
                pActor.data.get(LineageKeys.EVER_NOBLE_BLOOD, out bool ever, false);
                if (ever)
                {
                    pActor.data.get(LineageKeys.NOBLE_ORIGIN_ACTOR_ID, out long originId, -1L);
                    pActor.data.get(LineageKeys.NOBLE_ORIGIN_NAME, out string originName, "");
                    pActor.data.get(LineageKeys.NOBLE_ORIGIN_DISTANCE, out int distance, 99);
                    return new NobleBloodEvidence
                    {
                        has_noble_blood = true,
                        origin_actor_id = originId,
                        origin_name = originName,
                        distance = distance,
                        reason = "live_flag"
                    };
                }
            }

            if (pRow != null && pRow.ever_noble_blood != 0)
                return new NobleBloodEvidence
                {
                    has_noble_blood = true,
                    origin_actor_id = pRow.noble_origin_actor_id,
                    origin_name = pRow.noble_origin_name ?? "",
                    distance = pRow.noble_origin_distance,
                    reason = "archive_flag"
                };

            return SearchNobleAncestor(pActorId, 0, new HashSet<long>());
        }

        private static NobleBloodEvidence SearchNobleAncestor(long pActorId, int pDepth, HashSet<long> pVisited)
        {
            if (pDepth > MAX_DEPTH || pActorId < 0 || !pVisited.Add(pActorId))
                return new NobleBloodEvidence();

            Actor actor = World.world?.units?.get(pActorId);
            ActorArchiveTableItem row = LineageArchiveReader.ReadRow(pActorId);
            if (IsNobleEvidence(actor, row))
                return new NobleBloodEvidence
                {
                    has_noble_blood = true,
                    origin_actor_id = pActorId,
                    origin_name = NameOf(actor, row, pActorId),
                    distance = pDepth,
                    reason = "ancestor_noble"
                };

            foreach (long parent in GetTraceParentIds(pActorId))
            {
                NobleBloodEvidence found = SearchNobleAncestor(parent, pDepth + 1, pVisited);
                if (found.has_noble_blood) return found;
            }

            return new NobleBloodEvidence();
        }

        private static bool IsNobleEvidence(Actor pActor, ActorArchiveTableItem pRow)
        {
            if (pActor?.data != null)
            {
                pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
                pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int dist, 99);
                if (status == LineageStatus.NOBLE || dist < 99 || pActor.isKing() || pActor.isCityLeader())
                    return true;
                if (pActor.hasTrait(LineageKeys.TRAIT_GUIZU) || pActor.hasTrait(LineageKeys.TRAIT_ZHUHOU))
                    return true;
            }

            if (pRow == null) return false;
            return pRow.status == LineageStatus.NOBLE || pRow.noble_distance < 99 ||
                   pRow.lineage_id >= 0 || pRow.shi_id >= 0;
        }

        private static AncestryContribution NewContribution(string pKey, string pLabel, string pKind,
            long pActorId, string pName, string pColor)
        {
            return new AncestryContribution
            {
                key = pKey,
                label = pLabel,
                kind = pKind,
                source_actor_id = pActorId,
                source_actor_name = pName,
                color = pColor ?? ""
            };
        }

        private static string ResolveIdentity(Actor pActor, ActorArchiveTableItem pRow)
        {
            string status = "";
            if (pActor?.data != null) pActor.data.get(LineageKeys.LINEAGE_STATUS, out status, LineageStatus.NONE);
            if (string.IsNullOrEmpty(status) && pRow != null) status = pRow.status;
            return string.IsNullOrEmpty(status) ? LineageStatus.NONE : status;
        }

        private static string NameOf(Actor pActor, ActorArchiveTableItem pRow, long pId)
        {
            return pActor?.getName() ?? pRow?.display_name ?? pRow?.given_name ?? ("#" + pId);
        }

        private static string LiveString(Actor pActor, string pKey, string pFallback)
        {
            if (pActor?.data != null)
            {
                pActor.data.get(pKey, out string value, "");
                if (!string.IsNullOrEmpty(value)) return value;
            }

            return pFallback ?? "";
        }

        private static long LiveLong(Actor pActor, string pKey, long pFallback)
        {
            if (pActor?.data != null)
            {
                pActor.data.get(pKey, out long value, -1L);
                if (value >= 0) return value;
            }

            return pFallback;
        }
    }
}
