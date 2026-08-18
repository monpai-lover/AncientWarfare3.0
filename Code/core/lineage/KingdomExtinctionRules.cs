namespace AncientWarfare3.core.lineage
{
    public static class KingdomExtinctionRules
    {
        public static bool ShouldDeferRemovalVerification(
            bool cityIndexStable)
        {
            return !cityIndexStable;
        }

        public static bool ShouldDeferRemovalVerification(
            bool cityIndexStable, bool actorKingdomIndexStable)
        {
            return !cityIndexStable;
        }

        public static bool ShouldDisbandSurvivors(
            bool isCivilization,
            bool cityIndexStable,
            bool hasCities)
        {
            return isCivilization && cityIndexStable && !hasCities;
        }

        public static bool ShouldForceImmediateRemoval(
            bool isCivilization, bool cityIndexStable, int liveCityCount)
        {
            return isCivilization && cityIndexStable && liveCityCount <= 0;
        }

        /// <summary>
        ///     零城文明「到达灭亡条件」与「现在就能安全 Dispose」是两件事。
        ///
        ///     原版 <c>Kingdom.isReadyForRemoval()</c> 除了城池,还挡住
        ///     <c>_force_preserve_alive</c> / <c>units.Count</c> /
        ///     <c>buildings.Count</c> / 活跃弹道。其中 preserve-alive 由
        ///     <c>KingdomManager.updateDirtyUnits()</c> 对每个 units_only_dying
        ///     单位调用 <c>kingdom.preserveAlive()</c> 置上——这正是原版保证
        ///     「尸体还指着的王国绝不会被 Dispose」的唯一闸门。
        ///
        ///     <c>Kingdom.Dispose()</c> 会把 <c>asset</c> 置 null,而
        ///     <c>Actor.die()→clearManagers()</c> 故意保留 <c>kingdom</c>
        ///     (尸体渲染要用)。所以一旦跳过该闸门强行移除,尸体仍留在
        ///     <c>visible_units</c> 里,下一帧 <c>precalculateRenderDataParallel</c>
        ///     → <c>isColoredSpriteNeedsCheck</c> → <c>Kingdom.getColor()</c>
        ///     解 null asset,每帧 NullReferenceException。批量擦除小人会在同一帧
        ///     同时造出大量尸体并让城池归零,必然踩中。
        ///
        ///     因此强制移除只在原版活引用闸门本身已放开时才生效;未放开时交回原版
        ///     判定(它会返回 false),等尸体消失后的某一帧再移除。
        /// </summary>
        public static bool ShouldForceImmediateRemoval(
            bool isCivilization, bool cityIndexStable, int liveCityCount,
            bool vanillaLiveReferencesCleared)
        {
            return ShouldForceImmediateRemoval(isCivilization,
                       cityIndexStable, liveCityCount) &&
                   vanillaLiveReferencesCleared;
        }

        /// <summary>
        ///     原版活引用闸门是否已放开(可安全 Dispose)。
        ///     对齐 <c>Kingdom.isReadyForRemoval()</c> + <c>MetaObject</c> 基类。
        /// </summary>
        public static bool AreVanillaLiveReferencesCleared(
            bool forcePreserveAlive, int unitCount, int buildingCount,
            bool hasActiveProjectiles)
        {
            return !forcePreserveAlive && unitCount <= 0 &&
                   buildingCount <= 0 && !hasActiveProjectiles;
        }

        public static bool ShouldDemobilizeFallenRealmWarrior(
            bool recordedForFallenRealm, bool stillInFallenRealm,
            bool currentRealmIsCivilized)
        {
            return recordedForFallenRealm &&
                   (stillInFallenRealm || !currentRealmIsCivilized);
        }

        public static bool ShouldTreatAsHavingCities(
            bool cityIndexStable, int liveCityCount)
        {
            return liveCityCount > 0;
        }

        public static bool ShouldQueueVerification(
            bool isCivilization, bool cityIndexStable, int liveCityCount)
        {
            return isCivilization && !cityIndexStable && liveCityCount <= 0;
        }
    }
}
