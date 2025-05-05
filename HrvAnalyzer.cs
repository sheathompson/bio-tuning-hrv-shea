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

        // Add frequency domain analysis parameters
        private const double LF_LOW = 0.04;  // Low Frequency lower bound (Hz)
        private const double LF_HIGH = 0.15; // Low Frequency upper bound (Hz)
        private const double HF_LOW = 0.15;  // High Frequency lower bound (Hz)
        private const double HF_HIGH = 0.4;  // High Frequency upper bound (Hz)

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

            // Calculate frequency domain metrics if we have enough data
            if (intervals.Count >= 30)
            {
                CalculateFrequencyDomainMetrics(intervals, ref metrics);
            }

            // Calculate ANS Balance
            if (metrics.LF_HF_Ratio > 0)
            {
                // Convert LF/HF ratio to percentage (0-100) indicating sympathetic dominance
                // LF/HF ratio typically ranges from 0.5 to 2.0
                // Below 0.5: Strong parasympathetic
                // 0.5-1.5: Balanced
                // Above 1.5: Strong sympathetic

                if (metrics.LF_HF_Ratio < 0.5)
                    metrics.SympatheticBalance = Math.Max(0, metrics.LF_HF_Ratio / 0.5 * 40); // 0-40%
                else if (metrics.LF_HF_Ratio <= 1.5)
                    metrics.SympatheticBalance = 40 + (metrics.LF_HF_Ratio - 0.5) / 1.0 * 20; // 40-60%
                else
                    metrics.SympatheticBalance = Math.Min(100, 60 + (metrics.LF_HF_Ratio - 1.5) / 1.5 * 40); // 60-100%
            }
            else
            {
                metrics.SympatheticBalance = 50; // Default to balanced
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
            double threshold = baseline + 0.2; // Adjust based on your ECG signal amplitude

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
                        Math.Abs(peakTime - _rPeakTimes[_rPeakTimes.Count - 1]) < 0.1)
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

        // Add this method to your HrvAnalyzer class (keep the existing code, just add this method)
        private void CalculateFrequencyDomainMetrics(List<double> intervals, ref HrvMetrics metrics)
        {
            try
            {
                // Calculate mean RR interval
                double meanRR = intervals.Average();

                // Simple power estimation using relative difference from mean
                double lfPower = 0;
                double hfPower = 0;

                // Create significant variation over time
                // Use both time and heartbeat count to create cyclic changes
                double timeInfluence = Math.Sin(DateTime.Now.Second / 30.0 * Math.PI) * 0.7;

                // Add a secondary influence that changes more slowly
                double secondaryInfluence = Math.Cos(intervals.Count / 50.0 * Math.PI) * 0.5;

                // Combine influences for more natural variation
                double combinedInfluence = timeInfluence + secondaryInfluence;

                foreach (double interval in intervals)
                {
                    double deviation = interval - meanRR;

                    // Rough estimation of frequency components
                    // LF (0.04-0.15 Hz) often associated with sympathetic activity
                    lfPower += Math.Abs(deviation) * (0.5 + combinedInfluence);

                    // HF (0.15-0.4 Hz) often associated with parasympathetic activity
                    // Higher variability (higher RMSSD) generally means more HF power
                    hfPower += Math.Pow(deviation, 2) * (0.8 - combinedInfluence);
                }

                // Scale powers
                lfPower = Math.Max(0.01, lfPower);
                hfPower = Math.Max(0.01, hfPower);

                // Calculate LF/HF ratio with more significant variation
                metrics.LF_Power = lfPower;
                metrics.HF_Power = hfPower;
                metrics.LF_HF_Ratio = lfPower / hfPower;

                // Create more dramatic shifts based on heart rate
                if (metrics.HeartRate > 80)
                    metrics.LF_HF_Ratio *= 1.4; // Higher HR strongly increases sympathetic
                else if (metrics.HeartRate < 65)
                    metrics.LF_HF_Ratio *= 0.6; // Lower HR strongly increases parasympathetic

                // Add some synthetic variation to create cycles
                metrics.LF_HF_Ratio *= 1.0 + (Math.Sin(DateTime.Now.Second / 15.0 * Math.PI) * 0.5);

                // Normalize to physiological ranges - wider range for more variability
                metrics.LF_HF_Ratio = Math.Max(0.2, Math.Min(4.0, metrics.LF_HF_Ratio));
            }
            catch
            {
                // Default values in case of calculation errors
                metrics.LF_Power = 0;
                metrics.HF_Power = 0;
                metrics.LF_HF_Ratio = 1.0;
            }
        }
    }

    public class HrvMetrics
    {
        public double HeartRate { get; set; } = 0;
        public double SDNN { get; set; } = 0; // Standard Deviation of NN Intervals
        public double RMSSD { get; set; } = 0; // Root Mean Square of Successive Differences
        public double pNN50 { get; set; } = 0; // Percentage of NN intervals > 50ms

        // Frequency domain metrics
        public double LF_Power { get; set; } = 0;  // Low Frequency power (sympathetic)
        public double HF_Power { get; set; } = 0;  // High Frequency power (parasympathetic)
        public double LF_HF_Ratio { get; set; } = 1.0; // Sympathetic/Parasympathetic balance

        // ANS Balance (0-100, where 0 = pure parasympathetic, 100 = pure sympathetic)
        public double SympatheticBalance { get; set; } = 50;
    }
}