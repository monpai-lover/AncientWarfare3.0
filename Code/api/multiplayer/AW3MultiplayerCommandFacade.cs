using System;
using AncientWarfare3.core.multiplayer.commands;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerCommandFacade
    {
        private static readonly object Gate = new object();
        private static IAW3CommandDispatcher _dispatcher;
        private static long _ownershipRevision;

        public static event Action Changed;

        public static IAW3CommandDispatcher Current
        {
            get
            {
                lock (Gate) return _dispatcher;
            }
        }

        public static bool Register(IAW3CommandDispatcher dispatcher)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            long revision;
            lock (Gate)
            {
                if (ReferenceEquals(_dispatcher, dispatcher)) return true;
                if (_dispatcher != null) return false;
                _dispatcher = dispatcher;
                revision = ++_ownershipRevision;
            }
            try { dispatcher.Changed += OnDispatcherChanged; }
            catch
            {
                lock (Gate)
                    if (ReferenceEquals(_dispatcher, dispatcher) &&
                        _ownershipRevision == revision)
                    {
                        _dispatcher = null;
                        _ownershipRevision++;
                    }
                return false;
            }
            bool stillOwned;
            lock (Gate)
                stillOwned = ReferenceEquals(_dispatcher, dispatcher) &&
                             _ownershipRevision == revision;
            if (!stillOwned)
            {
                try { dispatcher.Changed -= OnDispatcherChanged; }
                catch { }
                return false;
            }
            OnDispatcherChanged();
            return true;
        }

        public static bool Unregister(IAW3CommandDispatcher dispatcher)
        {
            if (dispatcher == null) return false;
            lock (Gate)
            {
                if (!ReferenceEquals(_dispatcher, dispatcher)) return false;
                _dispatcher = null;
                _ownershipRevision++;
            }
            try { dispatcher.Changed -= OnDispatcherChanged; }
            catch { }
            OnDispatcherChanged();
            return true;
        }

        public static AW3CommandResult DispatchFromUi(
            AW3CommandRequest request)
        {
            if (request == null || !request.IsValid)
                return InvalidRequest();
            IAW3CommandDispatcher dispatcher;
            lock (Gate) dispatcher = _dispatcher;
            if (dispatcher == null)
            {
                if (AW3MultiplayerReplicaScope.IsReplicaSession)
                    return AW3CommandResult.Rejected(
                        AW3CommandError.ProviderUnavailable,
                        "aw3_command_dispatcher_missing");
                return DispatchAuthoritative(request);
            }
            try
            {
                return dispatcher.Dispatch(request) ?? ExecutionFailed();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("AW3 command dispatcher failed: kind=" +
                                    request.Kind + " exception=" +
                                    exception.GetType().Name + ": " +
                                    exception.Message);
                return ExecutionFailed();
            }
        }

        public static AW3CommandResult DispatchAuthoritative(
            AW3CommandRequest request)
        {
            if (request == null || !request.IsValid)
                return InvalidRequest();
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return AW3CommandResult.Rejected(
                    AW3CommandError.Unauthorized,
                    "aw3_command_replica_read_only");
            try
            {
                return AW3AuthoritativeCommandRouter.Dispatch(request) ??
                       ExecutionFailed();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("AW3 authoritative command failed: kind=" +
                                    request.Kind + " exception=" +
                                    exception.GetType().Name + ": " +
                                    exception.Message);
                return ExecutionFailed();
            }
        }

        private static AW3CommandResult InvalidRequest() =>
            AW3CommandResult.Rejected(AW3CommandError.InvalidRequest,
                "aw3_command_invalid_request");

        private static AW3CommandResult ExecutionFailed() =>
            AW3CommandResult.Rejected(AW3CommandError.ExecutionFailed,
                "aw3_command_execution_failed");

        private static void OnDispatcherChanged()
        {
            Delegate[] callbacks = Changed?.GetInvocationList();
            if (callbacks == null) return;
            foreach (Delegate callback in callbacks)
            {
                try { ((Action)callback)(); }
                catch { }
            }
        }
    }
}
