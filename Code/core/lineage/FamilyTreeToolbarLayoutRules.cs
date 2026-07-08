namespace AncientWarfare3.core.lineage
{
    public static class FamilyTreeToolbarLayoutRules
    {
        public static float RightAlignedX(float pRightInset)
        {
            return Abs(pRightInset);
        }

        public static bool StaysInsideRightEdge(float pWindowWidth, float pElementWidth, float pAnchoredX)
        {
            if (pWindowWidth <= 0f || pElementWidth <= 0f) return false;

            float half = pWindowWidth * 0.5f;
            float right = pAnchoredX + pElementWidth;
            float left = pAnchoredX;
            return right <= half && left >= -half;
        }

        private static float Abs(float pValue)
        {
            return pValue < 0f ? -pValue : pValue;
        }
    }
}
