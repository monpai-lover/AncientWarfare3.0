using System.Collections.Generic;

namespace AncientWarfare3.ui
{
    public readonly struct AWLineageTabButtonSlot
    {
        public AWLineageTabButtonSlot(string pGroupId, int pIndex, float pX, float pY)
        {
            GroupId = pGroupId;
            Index = pIndex;
            X = pX;
            Y = pY;
        }

        public string GroupId { get; }
        public int Index { get; }
        public float X { get; }
        public float Y { get; }
    }

    public sealed class AWLineageTabLayoutPlan
    {
        public List<AWLineageTabButtonSlot> Buttons { get; } = new();
        public List<float> Dividers { get; } = new();
    }

    public static class AWLineageTabLayoutRules
    {
        public const string Manual = "manual";
        public const string XiaSpawn = "xia_spawn";
        public const string Archives = "archives";
        public const string Schools = "schools";
        public const string Territory = "territory";
        public const string Mandate = "mandate";
        public const string Administration = "administration";
        public const string Settings = "settings";

        public static IReadOnlyList<string> OrderedGroups { get; } =
            new[]
            {
                XiaSpawn, Archives, Schools, Territory,
                Mandate, Administration, Settings
            };

        public static AWLineageTabLayoutPlan BuildLayout(
            IReadOnlyDictionary<string, int> pButtonCounts)
        {
            var plan = new AWLineageTabLayoutPlan();
            float firstColumnX = 16f;
            bool hasPreviousGroup = false;

            foreach (string groupId in OrderedGroups)
            {
                int count = 0;
                if (pButtonCounts != null &&
                    pButtonCounts.TryGetValue(groupId, out int registered))
                    count = registered > 0 ? registered : 0;
                if (count == 0) continue;

                if (hasPreviousGroup)
                {
                    plan.Dividers.Add(firstColumnX + 24f);
                    firstColumnX += 48f;
                }

                for (int index = 0; index < count; index++)
                {
                    int column = index / 2;
                    float y = index % 2 == 0 ? 18f : -18f;
                    plan.Buttons.Add(new AWLineageTabButtonSlot(
                        groupId, index, firstColumnX + column * 36f, y));
                }

                int columns = (count + 1) / 2;
                firstColumnX += (columns - 1) * 36f;
                hasPreviousGroup = true;
            }

            return plan;
        }
    }
}
