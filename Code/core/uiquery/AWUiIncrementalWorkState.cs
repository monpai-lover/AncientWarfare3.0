using System;

namespace AncientWarfare3.core.uiquery
{
    internal readonly struct AWUiIncrementalTicket
    {
        public AWUiIncrementalTicket(long generation, long worldGeneration,
            long contentRevision)
        {
            Generation = generation;
            WorldGeneration = worldGeneration;
            ContentRevision = contentRevision;
        }

        public long Generation { get; }
        public long WorldGeneration { get; }
        public long ContentRevision { get; }
    }

    internal sealed class AWUiIncrementalWorkState
    {
        public const int DefaultStepsPerFrame = 8;

        private readonly int _stepsPerFrame;
        private long _generation;
        private AWUiIncrementalTicket _current;
        private bool _active;

        public AWUiIncrementalWorkState(
            int stepsPerFrame = DefaultStepsPerFrame)
        {
            _stepsPerFrame = Math.Max(1, stepsPerFrame);
        }

        public AWUiIncrementalTicket Begin(long worldGeneration,
            long contentRevision)
        {
            AdvanceGeneration();
            _current = new AWUiIncrementalTicket(_generation,
                worldGeneration, contentRevision);
            _active = true;
            return _current;
        }

        public bool Accept(AWUiIncrementalTicket ticket,
            long currentWorldGeneration, long currentContentRevision)
        {
            return _active &&
                   ticket.Generation == _current.Generation &&
                   ticket.WorldGeneration == _current.WorldGeneration &&
                   ticket.ContentRevision == _current.ContentRevision &&
                   ticket.WorldGeneration == currentWorldGeneration &&
                   ticket.ContentRevision == currentContentRevision;
        }

        public bool AcceptAcceptedSnapshot(AWUiIncrementalTicket ticket,
            long currentWorldGeneration)
        {
            return _active &&
                   ticket.Generation == _current.Generation &&
                   ticket.WorldGeneration == _current.WorldGeneration &&
                   ticket.WorldGeneration == currentWorldGeneration;
        }

        public int TakeFrameStepBudget(int remainingSteps)
        {
            return Math.Min(_stepsPerFrame, Math.Max(0, remainingSteps));
        }

        public void Cancel()
        {
            _active = false;
            AdvanceGeneration();
        }

        private void AdvanceGeneration()
        {
            _generation = _generation == long.MaxValue
                ? 1L
                : _generation + 1L;
        }
    }
}
