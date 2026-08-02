namespace AncientWarfare3.content.figures
{
    public static class HistoricalFigureSpawnRules
    {
        public const int Available = 0;
        public const int Committed = 1;
        public const int Pending = 2;

        public static int NormalizeLoadedSpawnState(int spawnState)
        {
            return spawnState == Committed ? Committed : Available;
        }

        public static int ReserveSpawnState(int spawnState)
        {
            return spawnState == Available ? Pending : spawnState;
        }

        public static int CommitSpawnState(int spawnState)
        {
            return spawnState == Pending ? Committed : spawnState;
        }

        public static int AbortSpawnState(int spawnState)
        {
            return spawnState == Pending ? Available : spawnState;
        }

        public static bool IsCommittedAlive(int spawnState, bool dead)
        {
            return spawnState == Committed && !dead;
        }

        public static bool CanEvaluate(bool persistenceReady)
        {
            return persistenceReady;
        }

        public static bool CanMutate(bool reservationCommitted)
        {
            return reservationCommitted;
        }

        public static bool IsFemale(HistoricalFigureSex sex)
        {
            return sex == HistoricalFigureSex.Female;
        }

        public static bool ShouldUseIntegratedName(bool requiresIntegration,
            bool integrationReady)
        {
            return requiresIntegration && integrationReady;
        }

        public static string FormatLocalizedLabel(string localizedName,
            string localizedDynasty)
        {
            return (localizedName ?? "") + " · " +
                   (localizedDynasty ?? "");
        }

        public static bool IsDefinitionSpawnable(string id,
            int registryIndex, int spawnOrder, float chance)
        {
            return !string.IsNullOrWhiteSpace(id) && registryIndex >= 0 &&
                   spawnOrder >= 0 && chance > 0f && chance <= 1f &&
                   !float.IsNaN(chance) && !float.IsInfinity(chance);
        }

        public static bool CanAttemptDefinition(bool requiresIntegration,
            bool integrationReady, float chance)
        {
            return chance > 0f && chance <= 1f &&
                   !float.IsNaN(chance) && !float.IsInfinity(chance) &&
                   (!requiresIntegration || integrationReady);
        }

        public static int SelectCandidate(bool jiFaCommitted,
            int[] registryIndices, int[] spawnStates, bool[] eligible,
            int randomOrdinal)
        {
            if (registryIndices == null || spawnStates == null ||
                eligible == null || spawnStates.Length != eligible.Length)
                return -1;

            var candidates = new System.Collections.Generic.List<int>();
            bool jiFaAvailable = false;
            for (int i = 0; i < registryIndices.Length; i++)
            {
                int registryIndex = registryIndices[i];
                if (registryIndex < 0 || registryIndex >= spawnStates.Length)
                    return -1;
                int state = spawnStates[registryIndex];
                if (state == Pending) return -1;
                if (registryIndex == 0)
                {
                    jiFaAvailable = state == Available &&
                                    eligible[registryIndex];
                    continue;
                }
                if (jiFaCommitted && state == Available &&
                    eligible[registryIndex])
                    candidates.Add(registryIndex);
            }

            if (!jiFaCommitted) return jiFaAvailable ? 0 : -1;
            if (candidates.Count == 0) return -1;
            int index = (int)((uint)randomOrdinal % (uint)candidates.Count);
            return candidates[index];
        }

        public static string ProjectStateName(string dynastyName,
            string kingdomName)
        {
            return string.IsNullOrWhiteSpace(kingdomName)
                ? dynastyName ?? ""
                : kingdomName;
        }

        public static string ProjectLocalizedStateName(
            string canonicalStateName, string localizedDynastyName,
            bool chinesePresentation)
        {
            string canonical = canonicalStateName ?? string.Empty;
            if (chinesePresentation ||
                string.IsNullOrWhiteSpace(localizedDynastyName))
                return canonical;
            return localizedDynastyName;
        }

        public static int NextSpawnableRegistryIndex(int[] registryOrder,
            bool[] spawned, bool[] dead)
        {
            if (registryOrder == null || spawned == null || dead == null ||
                spawned.Length != dead.Length)
                return -1;

            int previousRegistryIndex = -1;
            for (int i = 0; i < registryOrder.Length; i++)
            {
                int registryIndex = registryOrder[i];
                if (registryIndex < 0 || registryIndex >= spawned.Length)
                    return -1;
                if (spawned[registryIndex])
                {
                    previousRegistryIndex = registryIndex;
                    continue;
                }
                if (previousRegistryIndex < 0 || dead[previousRegistryIndex])
                    return registryIndex;
                return -1;
            }
            return -1;
        }

        public static int NextSpawnableRegistryIndex(int[] registryOrder,
            int[] spawnStates, bool[] dead)
        {
            if (registryOrder == null || spawnStates == null || dead == null ||
                spawnStates.Length != dead.Length)
                return -1;

            int previousRegistryIndex = -1;
            for (int i = 0; i < registryOrder.Length; i++)
            {
                int registryIndex = registryOrder[i];
                if (registryIndex < 0 || registryIndex >= spawnStates.Length)
                    return -1;

                int state = spawnStates[registryIndex];
                if (state == Pending) return -1;
                if (state == Committed)
                {
                    previousRegistryIndex = registryIndex;
                    continue;
                }
                if (state != Available) return -1;
                if (previousRegistryIndex < 0 ||
                    IsCommittedAlive(spawnStates[previousRegistryIndex],
                        dead[previousRegistryIndex]) == false)
                    return registryIndex;
                return -1;
            }
            return -1;
        }
    }
}
