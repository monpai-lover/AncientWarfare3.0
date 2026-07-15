namespace AncientWarfare3.core.lineage
{
    public static class HeirMinimapScaleRules
    {
        private const float IdleMultiplier = 0.9f;
        private const float IdleCityMarkScale = 0.5f;
        private const float FocusedCityMarkScale = 0.75f;

        public static float Calculate(
            float baseScale,
            float cameraScale,
            bool selectedCityScale,
            bool hasScaleCity,
            float cityMarkScale)
        {
            float scale = baseScale * cameraScale;
            if (selectedCityScale)
                scale *= hasScaleCity ? cityMarkScale : 0.5f;

            float focusProgress = 0f;
            if (selectedCityScale && hasScaleCity)
            {
                focusProgress = (cityMarkScale - IdleCityMarkScale) /
                                (FocusedCityMarkScale - IdleCityMarkScale);
                if (focusProgress < 0f) focusProgress = 0f;
                if (focusProgress > 1f) focusProgress = 1f;
            }

            float heirMultiplier = IdleMultiplier + (1f - IdleMultiplier) * focusProgress;
            return scale * heirMultiplier;
        }
    }
}
