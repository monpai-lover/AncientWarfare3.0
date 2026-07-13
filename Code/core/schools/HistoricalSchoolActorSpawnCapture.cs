using System;

namespace AncientWarfare3.core.schools
{
    internal sealed class HistoricalSchoolActorSpawnCapture : IDisposable
    {
        [ThreadStatic]
        private static HistoricalSchoolActorSpawnCapture _current;

        private readonly ActorManager _manager;
        private readonly HistoricalSchoolActorSpawnCapture _previous;
        private FactoryFrame _currentFrame;
        private bool _disposed;

        private HistoricalSchoolActorSpawnCapture(ActorManager pManager)
        {
            _manager = pManager;
            _previous = _current;
            _current = this;
        }

        internal Actor CapturedActor { get; private set; }

        internal static HistoricalSchoolActorSpawnCapture Begin(ActorManager pManager)
        {
            return new HistoricalSchoolActorSpawnCapture(pManager);
        }

        internal static FactoryFrame EnterFactory(ActorManager pManager)
        {
            HistoricalSchoolActorSpawnCapture capture = _current;
            if (capture == null || capture._disposed ||
                !ReferenceEquals(capture._manager, pManager)) return null;

            var frame = new FactoryFrame(capture, capture._currentFrame);
            capture._currentFrame = frame;
            return frame;
        }

        internal static RegistrationFrame EnterRegistration(ActorManager pManager, Actor pActor)
        {
            HistoricalSchoolActorSpawnCapture capture = _current;
            if (capture == null || capture._disposed || pActor == null ||
                !ReferenceEquals(capture._manager, pManager)) return null;

            FactoryFrame factory = capture._currentFrame;
            if (factory == null || !factory.AllocationArmed) return null;
            var registration = new RegistrationFrame(capture, factory,
                factory.CurrentRegistration, pActor);
            factory.CurrentRegistration = registration;
            return registration;
        }

        internal static void ExitRegistration(RegistrationFrame pFrame)
        {
            if (pFrame == null)
            {
                HistoricalSchoolActorSpawnCapture current = _current;
                if (current?._currentFrame != null && !current._disposed)
                    current._currentFrame.CapturedActor = null;
                return;
            }

            HistoricalSchoolActorSpawnCapture capture = pFrame.Capture;
            FactoryFrame factory = pFrame.Factory;
            if (capture._disposed || !ReferenceEquals(_current, capture) ||
                !ReferenceEquals(capture._currentFrame, factory) ||
                !ReferenceEquals(factory.CurrentRegistration, pFrame)) return;

            factory.CurrentRegistration = pFrame.Parent;
            if (pFrame.Parent == null) factory.CapturedActor = pFrame.Actor;
        }

        internal static void ArmTargetFactoryAllocation()
        {
            HistoricalSchoolActorSpawnCapture capture = _current;
            if (capture == null || capture._disposed || capture._currentFrame == null) return;

            capture._currentFrame.AllocationArmed = true;
        }

        internal static bool IsTargetActor(Actor pActor)
        {
            HistoricalSchoolActorSpawnCapture capture = _current;
            FactoryFrame frame = capture?._currentFrame;
            return pActor != null && capture != null && !capture._disposed &&
                   frame != null && frame.Parent == null &&
                   ReferenceEquals(frame.CapturedActor, pActor);
        }

        internal static void ExitFactory(FactoryFrame pFrame)
        {
            if (pFrame == null)
            {
                if (_current != null && !_current._disposed) _current.CapturedActor = null;
                return;
            }
            HistoricalSchoolActorSpawnCapture capture = pFrame.Capture;
            if (capture._disposed || !ReferenceEquals(_current, capture) ||
                !ReferenceEquals(capture._currentFrame, pFrame)) return;

            capture._currentFrame = pFrame.Parent;
            if (pFrame.Parent == null) capture.CapturedActor = pFrame.CapturedActor;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _currentFrame = null;
            if (ReferenceEquals(_current, this)) _current = _previous;
        }

        internal sealed class FactoryFrame
        {
            internal FactoryFrame(HistoricalSchoolActorSpawnCapture pCapture,
                FactoryFrame pParent)
            {
                Capture = pCapture;
                Parent = pParent;
            }

            internal HistoricalSchoolActorSpawnCapture Capture { get; }
            internal FactoryFrame Parent { get; }
            internal bool AllocationArmed { get; set; }
            internal Actor CapturedActor { get; set; }
            internal RegistrationFrame CurrentRegistration { get; set; }
        }

        internal sealed class RegistrationFrame
        {
            internal RegistrationFrame(HistoricalSchoolActorSpawnCapture pCapture,
                FactoryFrame pFactory, RegistrationFrame pParent, Actor pActor)
            {
                Capture = pCapture;
                Factory = pFactory;
                Parent = pParent;
                Actor = pActor;
            }

            internal HistoricalSchoolActorSpawnCapture Capture { get; }
            internal FactoryFrame Factory { get; }
            internal RegistrationFrame Parent { get; }
            internal Actor Actor { get; }
        }
    }
}
