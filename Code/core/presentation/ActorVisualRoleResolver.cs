using System;
using System.Threading;

namespace AncientWarfare3.core.presentation
{
    public interface IActorVisualRoleProvider
    {
        bool TryResolve(Actor pActor, out ActorVisualRole pRole);
    }

    public static class ActorVisualRoleResolver
    {
        private static readonly object RegistrationGate = new object();
        private static IActorVisualRoleProvider[] _providers =
            Array.Empty<IActorVisualRoleProvider>();

        public static void Register(IActorVisualRoleProvider pProvider)
        {
            if (pProvider == null) throw new ArgumentNullException(nameof(pProvider));
            lock (RegistrationGate)
            {
                IActorVisualRoleProvider[] current = _providers;
                var next = new IActorVisualRoleProvider[current.Length + 1];
                Array.Copy(current, next, current.Length);
                next[current.Length] = pProvider;
                Volatile.Write(ref _providers, next);
            }
        }

        public static ActorVisualRole Resolve(Actor pActor)
        {
            return ResolveFrom(pActor, Volatile.Read(ref _providers));
        }

        internal static ActorVisualRole ResolveFrom(Actor pActor,
            IActorVisualRoleProvider[] pProviders)
        {
            if (pActor == null || pProviders == null)
                return ActorVisualRole.Default;
            for (int i = 0; i < pProviders.Length; i++)
            {
                IActorVisualRoleProvider provider = pProviders[i];
                if (provider == null) continue;
                try
                {
                    if (provider.TryResolve(pActor, out ActorVisualRole role) &&
                        role != ActorVisualRole.Default)
                        return role;
                }
                catch
                {
                    // Presentation providers are isolated from one another.
                }
            }
            return ActorVisualRole.Default;
        }
    }
}
