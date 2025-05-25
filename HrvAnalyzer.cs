using System;
using System.Collections.Generic;
using System.Linq;

namespace HRVMonitoringSystem
{
    public class HrvAnalyzer
    {
        private List<double> _rPeakTimes = new List<double>();
        private List<double> _rrIntervals = new List<double>();
        private const int MaxIntervals = 300; // Store ~5 minutes of data

        public HrvMetrics CalculateMetrics()
        {
            var metrics = new HrvMetrics();

            if (_rrIntervals.Count < 2)
                return metrics;

            // Use only the last 5 minutes of data for calculations
            var intervals = _rrIntervals.Skip(Math.Max(0, _rrIntervals.Count - MaxIntervals)).ToList();

            // Heart Rate
            metrics.HeartRate = 60000 / intervals.Average();

            // SDNN - Standard Deviation of NN Intervals
            metrics.SDNN = CalculateStandardDeviation(intervals);

            // RMSSD - Root Mean Square of Successive Differences
            var differences = new List<double>();
            for (int i = 1; i < intervals.Count; i++)
            {
                differences.Add(intervals[i] - intervals[i - 1]);
            }

            if (differences.Count > 0)
            {
                metrics.RMSSD = Math.Sqrt(differences.Select(d => d * d).Average());

                // pNN50 - Percentage of successive NN intervals > 50ms
                int nn50Count = differences.Count(d => Math.Abs(d) > 50);
                metrics.pNN50 = (double)nn50Count / differences.Count * 100;
            }

            // Calculate frequency domain metrics (simplified)
            if (intervals.Count > 10)
            {
                // Simulate LF/HF ratio based on variability
                double variance = CalculateStandardDeviation(intervals);
                metrics.LFHFRatio = 1.0 + (variance / 20.0); // Simplified calculation
            }

            return metrics;
        }

        public void AddRPeak(double timestamp)
        {
            _rPeakTimes.Add(timestamp);

            if (_rPeakTimes.Count > 1)
            {
                // Calculate RR interval in milliseconds
                double rrInterval = (_rPeakTimes[_rPeakTimes.Count - 1] -
                                    _rPeakTimes[_rPeakTimes.Count - 2]) * 1000;

                // Only add physiologically plausible intervals (250-1500ms)
                if (rrInterval >= 250 && rrInterval <= 1500)
                {
                    _rrIntervals.Add(rrInterval);

                    // Limit the number of stored intervals
                    if (_rrIntervals.Count > MaxIntervals)
                        _rrIntervals.RemoveAt(0);
                }
            }

            // Limit the number of stored R peaks
            if (_rPeakTimes.Count > MaxIntervals)
                _rPeakTimes.RemoveAt(0);
        }

        public double DetectRPeaks(double[] recentEcg, double currentTime)
        {
            // Simple threshold-based R peak detection
            if (recentEcg.Length < 10)
                return -1;

            // Calculate baseline and threshold
            double baseline = recentEcg.Average();
            double std = Math.Sqrt(recentEcg.Select(x => Math.Pow(x - baseline, 2)).Average());
            double threshold = baseline + std * 0.6;

            // Look for R peaks (must be higher than neighbors and above threshold)
            for (int i = 5; i < recentEcg.Length - 5; i++)
            {
                if (recentEcg[i] > threshold &&
                    recentEcg[i] > recentEcg[i - 1] &&
                    recentEcg[i] > recentEcg[i - 2] &&
                    recentEcg[i] > recentEcg[i + 1] &&
                    recentEcg[i] > recentEcg[i + 2])
                {
                    // Found an R peak, calculate its timestamp
                    double peakTime = currentTime - (recentEcg.Length - i) * 0.001;

                    // Check if we've already detected this peak (within 200ms)
                    if (_rPeakTimes.Count > 0 &&
                        Math.Abs(peakTime - _rPeakTimes[_rPeakTimes.Count - 1]) < 0.2)
                    {
                        continue;
                    }

                    return peakTime;
                }
            }

            return -1; // No peak found
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count <= 1)
                return 0;

            double avg = values.Average();
            double sumOfSquaresOfDifferences = values.Sum(val => Math.Pow(val - avg, 2));
            double sd = Math.Sqrt(sumOfSquaresOfDifferences / (values.Count - 1));

            return sd;
        }
    }

    public class HrvMetrics
    {
        public double HeartRate { get; set; } = 0;
        public double SDNN { get; set; } = 0; // Standard Deviation of NN Intervals
        public double RMSSD { get; set; } = 0; // Root Mean Square of Successive Differences
        public double pNN50 { get; set; } = 0; // Percentage of NN intervals > 50ms
        public double LFHFRatio { get; set; } = 0; // Low Frequency / High Frequency ratio
    }
}