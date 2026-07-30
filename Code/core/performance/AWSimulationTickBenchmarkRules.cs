using System;

namespace AncientWarfare3.core.performance
{
    public readonly struct AWSimulationTickSample
    {
        public AWSimulationTickSample(double workSeconds,
            double maxSliceSeconds, double simulatedSeconds, int frames,
            double latencySeconds, AWSimulationMode mode)
        {
            WorkSeconds = Math.Max(0d, workSeconds);
            MaxSliceSeconds = Math.Max(0d, maxSliceSeconds);
            SimulatedSeconds = Math.Max(0d, simulatedSeconds);
            Frames = Math.Max(1, frames);
            LatencySeconds = Math.Max(0d, latencySeconds);
            Mode = mode;
        }

        public double WorkSeconds { get; }
        public double MaxSliceSeconds { get; }
        public double SimulatedSeconds { get; }
        public int Frames { get; }
        public double LatencySeconds { get; }
        public AWSimulationMode Mode { get; }
    }

    public readonly struct AWSimulationTickWindowStats
    {
        public AWSimulationTickWindowStats(int count,
            double averageWorkSeconds, double maximumWorkSeconds,
            double maximumSliceSeconds, double averageSimulatedSeconds,
            double averageFrames, double averageLatencySeconds,
            AWSimulationMode lastMode)
        {
            Count = count;
            AverageWorkSeconds = averageWorkSeconds;
            MaximumWorkSeconds = maximumWorkSeconds;
            MaximumSliceSeconds = maximumSliceSeconds;
            AverageSimulatedSeconds = averageSimulatedSeconds;
            AverageFrames = averageFrames;
            AverageLatencySeconds = averageLatencySeconds;
            LastMode = lastMode;
        }

        public int Count { get; }
        public double AverageWorkSeconds { get; }
        public double MaximumWorkSeconds { get; }
        public double MaximumSliceSeconds { get; }
        public double AverageSimulatedSeconds { get; }
        public double AverageFrames { get; }
        public double AverageLatencySeconds { get; }
        public AWSimulationMode LastMode { get; }
        public double TheoreticalTicksPerSecond =>
            AverageWorkSeconds > 0d ? 1d / AverageWorkSeconds : 0d;
        public double TheoreticalSpeed => AverageWorkSeconds > 0d
            ? AverageSimulatedSeconds / AverageWorkSeconds
            : 0d;
    }

    public struct AWSimulationTickWindowAccumulator
    {
        private int _count;
        private double _totalWorkSeconds;
        private double _maximumWorkSeconds;
        private double _maximumSliceSeconds;
        private double _totalSimulatedSeconds;
        private double _totalFrames;
        private double _totalLatencySeconds;
        private AWSimulationMode _lastMode;

        public void Add(AWSimulationTickSample pSample)
        {
            _count++;
            _totalWorkSeconds += pSample.WorkSeconds;
            _maximumWorkSeconds = Math.Max(_maximumWorkSeconds,
                pSample.WorkSeconds);
            _maximumSliceSeconds = Math.Max(_maximumSliceSeconds,
                pSample.MaxSliceSeconds);
            _totalSimulatedSeconds += pSample.SimulatedSeconds;
            _totalFrames += pSample.Frames;
            _totalLatencySeconds += pSample.LatencySeconds;
            _lastMode = pSample.Mode;
        }

        public AWSimulationTickWindowStats GetStats()
        {
            if (_count == 0) return default;
            return new AWSimulationTickWindowStats(_count,
                _totalWorkSeconds / _count, _maximumWorkSeconds,
                _maximumSliceSeconds, _totalSimulatedSeconds / _count,
                _totalFrames / _count, _totalLatencySeconds / _count,
                _lastMode);
        }
    }
}
