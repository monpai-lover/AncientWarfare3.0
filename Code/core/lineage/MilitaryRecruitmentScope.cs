using System;

namespace AncientWarfare3.core.lineage
{
    internal enum MilitaryRecruitmentKind
    {
        None,
        StandingArmy,
        TemporaryLevy,
        SlaveVanguard,
        ExistingSpecialArmy
    }

    internal static class MilitaryRecruitmentScope
    {
        [ThreadStatic]
        private static MilitaryRecruitmentKind _current;

        public static MilitaryRecruitmentKind Current => _current;
        public static bool AllowsVanillaTryToMakeWarrior => _current != MilitaryRecruitmentKind.None;
        public static bool BypassesWarriorCapacity =>
            _current == MilitaryRecruitmentKind.SlaveVanguard ||
            _current == MilitaryRecruitmentKind.ExistingSpecialArmy;
        public static bool SuppressesPermanentEnlistmentHistory =>
            _current == MilitaryRecruitmentKind.TemporaryLevy ||
            _current == MilitaryRecruitmentKind.SlaveVanguard;

        public static IDisposable Open(MilitaryRecruitmentKind pKind)
        {
            MilitaryRecruitmentKind previous = _current;
            _current = pKind;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly MilitaryRecruitmentKind _previous;
            private bool _disposed;

            public Scope(MilitaryRecruitmentKind pPrevious)
            {
                _previous = pPrevious;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _current = _previous;
            }
        }
    }
}
