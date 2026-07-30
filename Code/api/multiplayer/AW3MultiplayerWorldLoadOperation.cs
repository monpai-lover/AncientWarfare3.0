using System;
using System.IO;

namespace AncientWarfare3.api.multiplayer
{
    internal sealed class AW3MultiplayerWorldLoadOperation
    {
        private readonly object _gate = new object();
        private readonly Guid _operationId;
        private readonly string _generationDirectory;
        private readonly DateTime _deadlineUtc;
        private AW3MultiplayerWorldLoadState _state;
        private AW3MultiplayerWorldLoadError _error;
        private string _detail = string.Empty;
        private long _revision;

        public AW3MultiplayerWorldLoadOperation(Guid operationId,
            string generationDirectory, DateTime deadlineUtc)
        {
            if (operationId == Guid.Empty)
                throw new ArgumentException("Operation ID is required.",
                    nameof(operationId));
            if (!TryCanonicalize(generationDirectory,
                    pRequireAlreadyCanonical: true, out string canonical))
                throw new ArgumentException(
                    "Generation directory must be an absolute canonical path.",
                    nameof(generationDirectory));
            if (deadlineUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Deadline must be UTC.",
                    nameof(deadlineUtc));

            _operationId = operationId;
            _generationDirectory = canonical;
            _deadlineUtc = deadlineUtc;
        }

        public AW3MultiplayerWorldLoadSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                    return new AW3MultiplayerWorldLoadSnapshot(_operationId,
                        _state, _error, _detail, _generationDirectory,
                        _revision);
            }
        }

        public bool ObserveWorldDataQueued(string pDirectory)
        {
            if (!TryCanonicalize(pDirectory, pRequireAlreadyCanonical: false,
                    out string canonical))
                return false;
            lock (_gate)
            {
                if (_state != AW3MultiplayerWorldLoadState.AwaitingWorldData ||
                    !string.Equals(_generationDirectory, canonical,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                return Transition(AW3MultiplayerWorldLoadState.LoadingWorld,
                    AW3MultiplayerWorldLoadError.None, string.Empty);
            }
        }

        public bool ObserveWorldLoaded()
        {
            lock (_gate)
            {
                if (_state != AW3MultiplayerWorldLoadState.LoadingWorld)
                    return false;
                return Transition(AW3MultiplayerWorldLoadState.RestoringAw3,
                    AW3MultiplayerWorldLoadError.None, string.Empty);
            }
        }

        public bool CompleteRestore()
        {
            lock (_gate)
            {
                if (_state != AW3MultiplayerWorldLoadState.RestoringAw3)
                    return false;
                return Transition(AW3MultiplayerWorldLoadState.Completed,
                    AW3MultiplayerWorldLoadError.None, string.Empty);
            }
        }

        public bool FailWorldLoadFallback(string pDetail)
        {
            lock (_gate)
            {
                if (IsTerminal(_state)) return false;
                return Transition(AW3MultiplayerWorldLoadState.Failed,
                    AW3MultiplayerWorldLoadError.WorldLoadFallback,
                    StableDetail(pDetail,
                        "WorldBox generated a fallback world."));
            }
        }

        public bool FailAw3Restore(string pDetail)
        {
            lock (_gate)
            {
                if (_state != AW3MultiplayerWorldLoadState.RestoringAw3)
                    return false;
                return Transition(AW3MultiplayerWorldLoadState.Failed,
                    AW3MultiplayerWorldLoadError.Aw3RestoreFailed,
                    StableDetail(pDetail, "AW3 runtime restoration failed."));
            }
        }

        public bool TryTimeout(DateTime pNowUtc)
        {
            lock (_gate)
            {
                if (IsTerminal(_state) || pNowUtc < _deadlineUtc) return false;
                return Transition(AW3MultiplayerWorldLoadState.Failed,
                    AW3MultiplayerWorldLoadError.WorldLoadTimeout,
                    "World load timed out.");
            }
        }

        public bool Cancel()
        {
            lock (_gate)
            {
                if (IsTerminal(_state)) return false;
                return Transition(AW3MultiplayerWorldLoadState.Cancelled,
                    AW3MultiplayerWorldLoadError.Cancelled,
                    "World load was cancelled.");
            }
        }

        private bool Transition(AW3MultiplayerWorldLoadState pState,
            AW3MultiplayerWorldLoadError pError, string pDetail)
        {
            _state = pState;
            _error = pError;
            _detail = pDetail ?? string.Empty;
            _revision++;
            return true;
        }

        private static bool IsTerminal(AW3MultiplayerWorldLoadState pState)
        {
            return pState == AW3MultiplayerWorldLoadState.Completed ||
                   pState == AW3MultiplayerWorldLoadState.Failed ||
                   pState == AW3MultiplayerWorldLoadState.Cancelled;
        }

        private static string StableDetail(string pDetail, string pFallback)
        {
            return string.IsNullOrWhiteSpace(pDetail) ? pFallback : pDetail;
        }

        private static bool TryCanonicalize(string pPath,
            bool pRequireAlreadyCanonical, out string pCanonical)
        {
            pCanonical = string.Empty;
            if (string.IsNullOrWhiteSpace(pPath) || !Path.IsPathRooted(pPath))
                return false;
            try
            {
                string supplied = TrimTrailingSeparators(pPath);
                string canonical = TrimTrailingSeparators(
                    Path.GetFullPath(pPath));
                if (pRequireAlreadyCanonical &&
                    !string.Equals(supplied, canonical,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                pCanonical = canonical;
                return canonical.Length > 0;
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is NotSupportedException ||
                                          error is PathTooLongException)
            {
                return false;
            }
        }

        private static string TrimTrailingSeparators(string pPath)
        {
            string result = pPath;
            string root = Path.GetPathRoot(result) ?? string.Empty;
            while (result.Length > root.Length &&
                   IsDirectorySeparator(result[result.Length - 1]))
                result = result.Substring(0, result.Length - 1);
            return result;
        }

        private static bool IsDirectorySeparator(char pValue)
        {
            return pValue == Path.DirectorySeparatorChar ||
                   pValue == Path.AltDirectorySeparatorChar;
        }
    }
}
