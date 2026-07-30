using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.presentation
{
    public static class ArmyRtsPlanPalette
    {
        private static readonly ArmyRtsPlanColor[] Entries = Build();

        public static IReadOnlyList<ArmyRtsPlanColor> Colors => Entries;

        public static byte IndexOf(ArmyRtsPlanColor pColor)
        {
            return (byte)((pColor.Red >> 5) << 5 |
                          (pColor.Green >> 5) << 2 |
                          pColor.Blue >> 6);
        }

        private static ArmyRtsPlanColor[] Build()
        {
            var result = new ArmyRtsPlanColor[256];
            for (int index = 0; index < result.Length; index++)
            {
                int red = index >> 5 & 0x07;
                int green = index >> 2 & 0x07;
                int blue = index & 0x03;
                result[index] = new ArmyRtsPlanColor(
                    (byte)Math.Round(red * 255d / 7d),
                    (byte)Math.Round(green * 255d / 7d),
                    (byte)Math.Round(blue * 255d / 3d));
            }
            return result;
        }
    }
}
