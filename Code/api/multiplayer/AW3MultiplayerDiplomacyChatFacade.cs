using System;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerDiplomacyChatFacade
    {
        private static readonly object Gate = new object();
        private static IAW3DiplomacyChatProvider _current;
        private static long _ownershipRevision;

        public static event Action Changed;

        public static IAW3DiplomacyChatProvider Current
        {
            get
            {
                lock (Gate) return _current;
            }
        }

        public static bool Register(IAW3DiplomacyChatProvider provider)
        {
            if (provider == null) return false;
            long revision;
            lock (Gate)
            {
                if (ReferenceEquals(_current, provider)) return true;
                if (_current != null) return false;
                _current = provider;
                revision = ++_ownershipRevision;
            }

            try
            {
                provider.Changed += OnProviderChanged;
            }
            catch
            {
                lock (Gate)
                    if (ReferenceEquals(_current, provider) &&
                        _ownershipRevision == revision)
                    {
                        _current = null;
                        _ownershipRevision++;
                    }
                return false;
            }

            bool stillOwned;
            lock (Gate)
                stillOwned = ReferenceEquals(_current, provider) &&
                             _ownershipRevision == revision;
            if (!stillOwned)
            {
                try { provider.Changed -= OnProviderChanged; }
                catch { }
                return false;
            }
            RaiseChanged();
            return true;
        }

        public static bool Unregister(IAW3DiplomacyChatProvider provider)
        {
            if (provider == null) return false;
            lock (Gate)
            {
                if (!ReferenceEquals(_current, provider)) return false;
                _current = null;
                _ownershipRevision++;
            }
            try { provider.Changed -= OnProviderChanged; }
            catch { }
            RaiseChanged();
            return true;
        }

        private static void OnProviderChanged()
        {
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            Action changed;
            lock (Gate) changed = Changed;
            if (changed == null) return;
            Delegate[] listeners = changed.GetInvocationList();
            for (var index = 0; index < listeners.Length; index++)
                try { ((Action)listeners[index])(); }
                catch { }
        }
    }
}
