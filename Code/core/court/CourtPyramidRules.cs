using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public readonly struct CourtPyramidCanvasBounds
    {
        public readonly float Width;
        public readonly float Height;
        public readonly float OffsetX;
        public readonly float OffsetY;

        public CourtPyramidCanvasBounds(float width, float height, float offsetX, float offsetY)
        {
            Width = width;
            Height = height;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }
    }

    public readonly struct CourtPyramidLinkSegment
    {
        public readonly float FromX;
        public readonly float FromY;
        public readonly float ToX;
        public readonly float ToY;

        public CourtPyramidLinkSegment(float fromX, float fromY, float toX, float toY)
        {
            FromX = fromX;
            FromY = fromY;
            ToX = toX;
            ToY = toY;
        }

        public bool IsHorizontal => Math.Abs(FromY - ToY) < 0.001f;
        public bool IsVertical => Math.Abs(FromX - ToX) < 0.001f;
        public float MinX => Math.Min(FromX, ToX);
        public float MaxX => Math.Max(FromX, ToX);
    }

    public static class CourtPyramidRoleId
    {
        public const string King = "king";
        public const string Heir = "heir";
        public const string General = "general";
        public const string Governor = "governor";
    }

    public sealed class CourtPyramidNodeModel
    {
        public long ActorId;
        public string ActorName = "";
        public string OfficeId = "";
        public string RoleId = "";
        public string SchoolId = "";
        public string SchoolIconPath = "";
        public long CityId = -1L;
        public string CityName = "";
        public int AppointmentYear = -1;
        public float Influence;
        public int Merit;
        public int Rank;
        public int StableOrder;
        public bool IsVacancy;
        public float X;
        public float Y;
        public List<string> Roles = new List<string>();

        public CourtPyramidNodeModel(long actorId, string officeId, string roleId,
            int rank, int stableOrder, bool isVacancy)
        {
            ActorId = actorId;
            OfficeId = officeId ?? "";
            RoleId = roleId ?? "";
            Rank = rank;
            StableOrder = stableOrder;
            IsVacancy = isVacancy;
        }

        public CourtPyramidNodeModel Clone()
        {
            return new CourtPyramidNodeModel(ActorId, OfficeId, RoleId, Rank, StableOrder, IsVacancy)
            {
                ActorName = ActorName,
                SchoolId = SchoolId,
                SchoolIconPath = SchoolIconPath,
                CityId = CityId,
                CityName = CityName,
                AppointmentYear = AppointmentYear,
                Influence = Influence,
                Merit = Merit,
                X = X,
                Y = Y,
                Roles = new List<string>(Roles)
            };
        }
    }

    public static class CourtPyramidRules
    {
        public const int KingRank = 0;
        public const int HeirRank = 10;
        public const int HighOfficeRank = 10;
        public const int MinistryRank = 20;
        public const int SpecialistRank = 30;
        public const int GeneralRank = 40;
        public const int GovernorRank = 50;

        public static bool ShouldAddStandaloneHeir(string pTier, bool hasValidHeir)
        {
            return hasValidHeir && pTier == CourtTier.Primitive;
        }

        public static bool ShouldResetCanvas(bool switchedKingdom, bool openingWindow)
        {
            return switchedKingdom || openingWindow;
        }

        public static List<CourtPyramidNodeModel> BuildLayout(
            IEnumerable<CourtPyramidNodeModel> pSeeds, float horizontalSpacing, float verticalSpacing)
        {
            List<CourtPyramidNodeModel> seeds = (pSeeds ?? Array.Empty<CourtPyramidNodeModel>())
                .Where(p => p != null)
                .ToList();
            var assignedOffices = new HashSet<string>(seeds
                .Where(p => !p.IsVacancy && p.ActorId >= 0 && !string.IsNullOrEmpty(p.OfficeId))
                .Select(p => p.OfficeId));
            var result = new List<CourtPyramidNodeModel>();

            foreach (IGrouping<long, CourtPyramidNodeModel> group in seeds
                         .Where(p => !p.IsVacancy && p.ActorId >= 0)
                         .GroupBy(p => p.ActorId))
            {
                List<CourtPyramidNodeModel> ordered = group
                    .OrderBy(p => p.Rank)
                    .ThenBy(p => p.StableOrder)
                    .ThenBy(p => p.OfficeId, StringComparer.Ordinal)
                    .ToList();
                CourtPyramidNodeModel merged = ordered[0].Clone();
                merged.Roles.Clear();
                foreach (CourtPyramidNodeModel item in ordered)
                {
                    string role = string.IsNullOrEmpty(item.RoleId) ? item.OfficeId : item.RoleId;
                    if (!string.IsNullOrEmpty(role) && !merged.Roles.Contains(role)) merged.Roles.Add(role);
                    if (string.IsNullOrEmpty(merged.SchoolId) && !string.IsNullOrEmpty(item.SchoolId))
                    {
                        merged.SchoolId = item.SchoolId;
                        merged.SchoolIconPath = item.SchoolIconPath;
                    }
                    if (merged.CityId < 0 && item.CityId >= 0)
                    {
                        merged.CityId = item.CityId;
                        merged.CityName = item.CityName;
                    }
                    if (item.AppointmentYear >= 0 &&
                        (merged.AppointmentYear < 0 || item.AppointmentYear < merged.AppointmentYear))
                        merged.AppointmentYear = item.AppointmentYear;
                    merged.Influence = Math.Max(merged.Influence, item.Influence);
                    merged.Merit = Math.Max(merged.Merit, item.Merit);
                }
                result.Add(merged);
            }

            foreach (CourtPyramidNodeModel vacancy in seeds
                         .Where(p => p.IsVacancy || p.ActorId < 0)
                         .Where(p => string.IsNullOrEmpty(p.OfficeId) || !assignedOffices.Contains(p.OfficeId))
                         .GroupBy(p => p.OfficeId ?? "")
                         .Select(p => p.OrderBy(v => v.StableOrder).First()))
            {
                CourtPyramidNodeModel node = vacancy.Clone();
                node.IsVacancy = true;
                node.Roles = string.IsNullOrEmpty(node.RoleId)
                    ? new List<string>()
                    : new List<string> { node.RoleId };
                result.Add(node);
            }

            result = result
                .OrderBy(p => p.Rank)
                .ThenBy(p => p.StableOrder)
                .ThenBy(p => p.IsVacancy)
                .ThenBy(p => p.ActorId)
                .ToList();
            float xSpacing = Math.Max(1f, horizontalSpacing);
            float ySpacing = Math.Max(1f, verticalSpacing);
            int rowIndex = 0;
            foreach (IGrouping<int, CourtPyramidNodeModel> row in result.GroupBy(p => p.Rank))
            {
                CourtPyramidNodeModel[] items = row.ToArray();
                float startX = -(items.Length - 1) * xSpacing * 0.5f;
                for (int i = 0; i < items.Length; i++)
                {
                    items[i].X = startX + i * xSpacing;
                    items[i].Y = -rowIndex * ySpacing;
                }
                rowIndex++;
            }
            return result;
        }

        public static CourtPyramidCanvasBounds CalculateCanvasBounds(
            IEnumerable<CourtPyramidNodeModel> pNodes, float nodeWidth, float nodeHeight, float padding)
        {
            List<CourtPyramidNodeModel> nodes = (pNodes ?? Array.Empty<CourtPyramidNodeModel>())
                .Where(p => p != null)
                .ToList();
            float safeWidth = Math.Max(1f, nodeWidth);
            float safeHeight = Math.Max(1f, nodeHeight);
            float safePadding = Math.Max(0f, padding);
            if (nodes.Count == 0)
                return new CourtPyramidCanvasBounds(safePadding * 2f, safePadding * 2f, 0f, -safePadding);

            float minX = nodes.Min(p => p.X - safeWidth * 0.5f);
            float maxX = nodes.Max(p => p.X + safeWidth * 0.5f);
            float topY = nodes.Max(p => p.Y);
            float bottomY = nodes.Min(p => p.Y - safeHeight);
            return new CourtPyramidCanvasBounds(
                maxX - minX + safePadding * 2f,
                topY - bottomY + safePadding * 2f,
                safePadding - minX,
                -safePadding - topY);
        }

        public static List<CourtPyramidLinkSegment> BuildOrthogonalLinks(
            IEnumerable<CourtPyramidNodeModel> pNodes, float nodeHeight)
        {
            var segments = new List<CourtPyramidLinkSegment>();
            List<IGrouping<int, CourtPyramidNodeModel>> rows =
                (pNodes ?? Array.Empty<CourtPyramidNodeModel>())
                .Where(p => p != null)
                .GroupBy(p => p.Rank)
                .OrderBy(p => p.Key)
                .ToList();
            float safeHeight = Math.Max(1f, nodeHeight);

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                CourtPyramidNodeModel[] parents = rows[rowIndex - 1].ToArray();
                CourtPyramidNodeModel[] children = rows[rowIndex].ToArray();
                if (parents.Length == 0 || children.Length == 0) continue;

                foreach (IGrouping<CourtPyramidNodeModel, CourtPyramidNodeModel> group in children
                             .GroupBy(child => parents
                                 .OrderBy(parent => Math.Abs(parent.X - child.X))
                                 .ThenBy(parent => parent.StableOrder)
                                 .First()))
                {
                    CourtPyramidNodeModel parent = group.Key;
                    CourtPyramidNodeModel[] assigned = group.OrderBy(p => p.X).ToArray();
                    float parentBottomY = parent.Y - safeHeight;
                    float childTopY = assigned[0].Y;
                    float busY = (parentBottomY + childTopY) * 0.5f;
                    AddSegment(segments, parent.X, parentBottomY, parent.X, busY);

                    float minChildX = assigned.Min(p => p.X);
                    float maxChildX = assigned.Max(p => p.X);
                    AddSegment(segments, minChildX, busY, maxChildX, busY);
                    foreach (CourtPyramidNodeModel child in assigned)
                        AddSegment(segments, child.X, busY, child.X, child.Y);
                }
            }

            return segments;
        }

        private static void AddSegment(List<CourtPyramidLinkSegment> pSegments,
            float pFromX, float pFromY, float pToX, float pToY)
        {
            if (Math.Abs(pFromX - pToX) < 0.001f && Math.Abs(pFromY - pToY) < 0.001f) return;
            pSegments.Add(new CourtPyramidLinkSegment(pFromX, pFromY, pToX, pToY));
        }

        public static int NextBatchEnd(int startIndex, int totalCount, int batchSize)
        {
            int total = Math.Max(0, totalCount);
            int start = Math.Max(0, Math.Min(total, startIndex));
            return Math.Min(total, start + Math.Max(1, batchSize));
        }

        public static int RankForOffice(string pOfficeId)
        {
            switch (pOfficeId ?? "")
            {
                case CourtOfficeId.Chancellor:
                case CourtOfficeId.Censor:
                case CourtOfficeId.Marshal:
                case CourtOfficeId.Zhongshu:
                case CourtOfficeId.Menxia:
                case CourtOfficeId.Shangshu:
                    return HighOfficeRank;
                case CourtOfficeId.ImperialPhysician:
                case CourtOfficeId.ImperialAstrologer:
                    return SpecialistRank;
                default:
                    return MinistryRank;
            }
        }

        public static string SchoolIconPath(string pSchoolId)
        {
            switch (pSchoolId ?? "")
            {
                case CourtSchoolId.Ru: return "ui/Icons/traits/iconRujia";
                case CourtSchoolId.Legalist: return "ui/Icons/traits/iconfajia";
                case CourtSchoolId.Dao: return "ui/Icons/traits/icontao";
                case CourtSchoolId.Mohist: return "ui/Icons/traits/iconmo";
                case CourtSchoolId.Military: return "ui/Icons/traits/iconbinfa";
                case CourtSchoolId.Diplomat: return "ui/Icons/traits/iconzonheng";
                case CourtSchoolId.Agrarian: return "ui/Icons/traits/iconnong";
                case CourtSchoolId.YinYang: return "ui/Icons/traits/iconyingyang";
                case CourtSchoolId.Logician: return "ui/Icons/traits/iconmingjia";
                case CourtSchoolId.Medical: return "ui/Icons/traits/iconoisha";
                case CourtSchoolId.Syncretist: return "ui/Icons/traits/iconzajia";
                case CourtSchoolId.Merchant: return "ui/Icons/traits/iconshangjia";
                case CourtSchoolId.Craftsman: return "ui/Icons/traits/icongongjia";
                case CourtSchoolId.Historian: return "ui/Icons/traits/iconshijia";
                default: return "";
            }
        }
    }
}
