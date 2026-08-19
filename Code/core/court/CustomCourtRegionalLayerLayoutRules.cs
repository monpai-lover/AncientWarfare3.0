using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtRegionalLayerLayoutRules
    {
        public const float VerticalGap = 150f;

        public static float AboveLocalOffices(IEnumerable<float> pOfficeY,
            float pFallbackY)
        {
            bool found = false;
            float minimum = 0f;
            if (pOfficeY != null)
                foreach (float y in pOfficeY)
                {
                    if (!found || y < minimum) minimum = y;
                    found = true;
                }
            return found ? minimum - VerticalGap : pFallbackY;
        }
    }
}
