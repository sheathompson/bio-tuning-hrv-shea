using System;
using System.Collections.Generic;
using System.Linq;

namespace HRVMonitoringSystem
{
    public class SimpleEmulator
    {
        private Random random = new Random();
        private double currentTime = 0;
        private bool isRunning = false;
        private double lastBeatTime = 0;
        private double nextBeatInterval = 0.857; // ~70 BPM (60/70)
        private double lastScrTime = 0;
        private bool scrActive = false;
        private double scrAmplitudeValue = 0;
        private double ansPhase = 0;  // Add this property for ANS calculations
        private Queue<double> ansValueBuffer = new Queue<double>(50); // Buffer for smoothing ANS values
        private List<double> rrIntervals = new List<double>(50); // Store RR intervals for HRV calculation
        private List<double> edaValues = new List<double>(50); // Store recent EDA values

        // Respiratory rate detection
        private List<double> baselineValues = new List<double>(1000); // Store baseline wandering for respiratory analysis
        private List<double> respiratoryTimestamps = new List<double>(); // Timestamps of respiratory cycles
        private double lastRespiratoryPeak = 0;
        private bool respiratoryRising = true;

        public double HeartRate { get; private set; } = 70;
        public double StressLevel { get; private set; } = 0.3;
        public double RespiratoryRate { get; private set; } = 15;
        public double DetectedRespiratoryRate { get; private set; } = 0;
        public bool RpeakDetected { get; private set; } = false;
        public double AnsBalance { get; private set; } = 50;
        public double HrvIndex { get; private set; } = 0.5; // Heart rate variability index

        public void Start()
        {
            isRunning = true;
            currentTime = 0;
            lastBeatTime = -1;
            rrIntervals.Clear();
            edaValues.Clear();
            baselineValues.Clear();
            respiratoryTimestamps.Clear();
            lastRespiratoryPeak = 0;
            respiratoryRising = true;

            // Start with a random phase to avoid flat lines
            ansPhase = random.NextDouble() * 20;

            // Initialize ANS buffer with varied values
            ansValueBuffer.Clear();
            for (int i = 0; i < 20; i++)
            {
                ansValueBuffer.Enqueue(50 + random.NextDouble() * 20 - 10);
            }

            CalculateNextBeatInterval();
        }

        public void Stop()
        {
            isRunning = false;
        }

        public DataPacket GetLatestData()
        {
            var packet = new DataPacket();

            if (!isRunning)
                return packet;

            // Generate 10 new data points
            double avgEdaValue = 0;

            for (int i = 0; i < 10; i++)
            {
                currentTime += 0.001; // 1ms per sample (1000 Hz)

                // Calculate time since last beat
                double timeSinceLastBeat = currentTime - lastBeatTime;

                // Check if it's time for a new heartbeat
                RpeakDetected = false; // Reset R peak flag
                if (timeSinceLastBeat >= nextBeatInterval)
                {
                    // Record RR interval for HRV calculation
                    if (lastBeatTime > 0)
                    {
                        double rrInterval = nextBeatInterval * 1000; // Convert to ms
                        rrIntervals.Add(rrInterval);

                        // Keep only last 50 intervals
                        if (rrIntervals.Count > 50)
                            rrIntervals.RemoveAt(0);

                        // Recalculate HRV index
                        if (rrIntervals.Count >= 5)
                        {
                            double sdnn = CalculateStandardDeviation(rrIntervals);
                            HrvIndex = Math.Min(1.0, sdnn / 100.0); // Normalize (0-1 scale)
                        }
                    }

                    lastBeatTime = currentTime;
                    CalculateNextBeatInterval();
                    RpeakDetected = true; // Set flag when R peak occurs
                }

                // Generate ECG value
                double ecgValue = GenerateEcgValue(currentTime, lastBeatTime);

                // Add respiratory baseline wander - the actual respiratory component
                double respiratoryFrequency = RespiratoryRate / 60.0; // Breaths per second
                double respiratoryComponent = 0.05 * Math.Sin(2 * Math.PI * respiratoryFrequency * currentTime);

                // Store baseline for respiratory detection
                baselineValues.Add(respiratoryComponent);
                if (baselineValues.Count > 1000)
                    baselineValues.RemoveAt(0);

                // Detect respiratory cycle (simplified peak detection)
                DetectRespiratoryRate(respiratoryComponent, currentTime);

                // Add small noise
                double noise = (random.NextDouble() - 0.5) * 0.01;

                // Combine all components
                double finalEcgValue = ecgValue + respiratoryComponent + noise;

                // Add ECG data point
                packet.EcgPoints.Add(new DataPoint
                {
                    Time = currentTime,
                    Value = finalEcgValue
                });

                // Generate EDA (skin conductance) value
                double edaBaseLevel = 2.0 + StressLevel * 5.0; // Higher stress = higher conductance

                // Add slow varying component (SCL - Skin Conductance Level)
                double scl = edaBaseLevel + 0.2 * Math.Sin(2 * Math.PI * 0.05 * currentTime);

                // Add occasional SCRs (Skin Conductance Responses)
                double scr = 0;
                if (random.NextDouble() < StressLevel * 0.01) // Probability of SCR based on stress
                {
                    // SCR rises quickly and falls slowly
                    double scrAmplitude = 0.5 * StressLevel * (1 + random.NextDouble());

                    // Store SCR start time if new one triggered
                    lastScrTime = currentTime;
                    scrActive = true;
                    scrAmplitudeValue = scrAmplitude;
                }

                // If SCR is active, calculate its value
                if (scrActive)
                {
                    double scrTime = currentTime - lastScrTime;
                    if (scrTime < 5.0) // SCR lasts about 5 seconds
                    {
                        // Fast rise (0-1s), slow decay (1-5s)
                        if (scrTime < 1.0)
                        {
                            scr = scrAmplitudeValue * (scrTime / 1.0);
                        }
                        else
                        {
                            scr = scrAmplitudeValue * Math.Exp(-(scrTime - 1.0) / 2.0);
                        }
                    }
                    else
                    {
                        scrActive = false;
                    }
                }

                // Add small noise
                double edaNoise = (random.NextDouble() - 0.5) * 0.05;

                // Combine all EDA components
                double finalEdaValue = scl + scr + edaNoise;
                avgEdaValue += finalEdaValue;

                // Store EDA value for correlation analysis
                edaValues.Add(finalEdaValue);
                if (edaValues.Count > 50)
                    edaValues.RemoveAt(0);

                // Add EDA data point
                packet.EdaPoints.Add(new DataPoint
                {
                    Time = currentTime,
                    Value = finalEdaValue
                });
            }

            // Calculate average EDA for this batch
            avgEdaValue /= 10;

            // Force R-peak detection at end of each data batch
            if (isRunning && RpeakDetected == false &&
                currentTime - lastBeatTime >= nextBeatInterval * 0.9)
            {
                // Force a heartbeat if we're close to when one should occur
                lastBeatTime = currentTime;
                CalculateNextBeatInterval();
                RpeakDetected = true;

                // Add a strong R-peak at the end of this batch
                if (packet.EcgPoints.Count > 0)
                {
                    int lastIndex = packet.EcgPoints.Count - 1;
                    packet.EcgPoints[lastIndex] = new DataPoint
                    {
                        Time = packet.EcgPoints[lastIndex].Time,
                        Value = 1.2  // Strong R-peak value
                    };
                }
            }

            // Scientific correlation between ECG and GSR for ANS balance
            // Calculate ANS balance based on HRV and GSR data
            // Lower HRV (less variability) + higher GSR = more sympathetic
            // Higher HRV + lower GSR = more parasympathetic

            // Calculate GSR index from average EDA value
            double edaBaseline = 3.5; // Typical baseline
            double gsrIndex = Math.Min(1.0, avgEdaValue / (edaBaseline * 2)); // Normalize (0-1 scale)

            // Calculate weighted ANS balance (0-100 scale)
            // Higher values = more sympathetic
            // Lower values = more parasympathetic
            double ansValue;

            if (rrIntervals.Count >= 5) // If we have enough data for HRV assessment
            {
                // Use weighted combination of HRV and GSR
                // Note: HRV is inverted (1-HrvIndex) because HIGHER HRV = MORE parasympathetic
                ansValue = ((1 - HrvIndex) * 0.6 + gsrIndex * 0.4) * 100;
            }
            else
            {
                // Use slower artificial variation for initial values
                ansPhase += 0.002;
                double baseVariation = 50 + 40 * Math.Sin(ansPhase * 0.2);
                double mediumVariation = 20 * Math.Sin(ansPhase * 0.5);
                double quickVariation = 5 * Math.Sin(ansPhase * 2.0);
                double randomVariation = random.NextDouble() * 2 - 1;
                ansValue = Math.Max(10, Math.Min(90, baseVariation + quickVariation + mediumVariation + randomVariation));
            }

            // Add to buffer for smoothing
            ansValueBuffer.Enqueue(ansValue);
            if (ansValueBuffer.Count > 50)
                ansValueBuffer.Dequeue();

            // Calculate smoothed ANS balance (moving average)
            double sum = 0;
            foreach (var value in ansValueBuffer)
            {
                sum += value;
            }
            AnsBalance = sum / ansValueBuffer.Count;

            return packet;
        }

        private void DetectRespiratoryRate(double respiratorySignal, double timestamp)
        {
            // Simple peak detection for respiratory cycles
            if (respiratoryRising && respiratorySignal < 0)
            {
                respiratoryRising = false;
            }
            else if (!respiratoryRising && respiratorySignal > 0)
            {
                respiratoryRising = true;

                // Record a complete respiratory cycle
                if (lastRespiratoryPeak > 0)
                {
                    double cycleDuration = timestamp - lastRespiratoryPeak;

                    // Only add physiologically plausible cycles (2-60 breaths/min)
                    if (cycleDuration > 1.0 && cycleDuration < 30.0)
                    {
                        respiratoryTimestamps.Add(timestamp);

                        // Keep only recent timestamps for rate calculation
                        if (respiratoryTimestamps.Count > 10)
                            respiratoryTimestamps.RemoveAt(0);

                        // Calculate respiratory rate if we have enough cycles
                        if (respiratoryTimestamps.Count >= 3)
                        {
                            double totalDuration = respiratoryTimestamps.Last() - respiratoryTimestamps.First();
                            int numCycles = respiratoryTimestamps.Count - 1;
                            double calculatedRate = (numCycles / totalDuration) * 60.0; // Convert to breaths per minute

                            // Update with smoothing
                            DetectedRespiratoryRate = Math.Round(calculatedRate * 0.7 + DetectedRespiratoryRate * 0.3, 1);
                        }
                    }
                }

                lastRespiratoryPeak = timestamp;
            }
        }

        private void CalculateNextBeatInterval()
        {
            // Calculate base interval from heart rate
            HeartRate = 80 + Math.Sin(currentTime * 0.05) * 5;
            double baseInterval = 60.0 / HeartRate;

            // Calculate average EDA value if available
            double edaAvg = 3.5; // Default value
            if (edaValues.Count > 0)
            {
                edaAvg = edaValues.Average();
            }

            // Update stress level based on both heart rate and skin conductance
            // Higher heart rate + higher skin conductance = higher stress/sympathetic activation
            double edaInfluence = edaAvg / 5.0; // Normalize EDA value (typically 3-4 range)
            double hrInfluence = (HeartRate - 60) / 40.0; // Heart rate component (normalized to 0-1)

            // Weighted combination
            double combinedStress = (hrInfluence * 0.7) + (edaInfluence * 0.3);
            StressLevel = Math.Max(0, Math.Min(1, combinedStress));

            // Add heart rate variability influenced by autonomic state
            // More sympathetic (higher stress) = less variability
            // More parasympathetic (lower stress) = more variability
            double variabilityFactor = (1 - StressLevel) * 0.1; // 0-10% variability
            double hrvComponent = (random.NextDouble() - 0.5) * variabilityFactor * baseInterval;

            nextBeatInterval = baseInterval + hrvComponent;

            // Add respiratory sinus arrhythmia (RSA) - heart rate varies with breathing
            // This links respiration and heart rate naturally
            double respiratoryPhase = 2 * Math.PI * RespiratoryRate / 60.0 * currentTime;
            double rsaComponent = Math.Sin(respiratoryPhase) * 0.05 * baseInterval; // RSA effect
            nextBeatInterval += rsaComponent;
        }

        private double GenerateEcgValue(double currentTime, double lastBeatTime)
        {
            double timeSinceLastBeat = currentTime - lastBeatTime;
            double ecgValue = 0;

            // Generate PQRST complex
            if (timeSinceLastBeat < 0.5) // Within 500ms of beat
            {
                // P wave (0-80ms)
                if (timeSinceLastBeat < 0.08)
                {
                    double pPhase = timeSinceLastBeat / 0.08;
                    ecgValue = 0.15 * Math.Sin(Math.PI * pPhase);
                }
                // PR interval (80-120ms) - flat
                else if (timeSinceLastBeat < 0.12)
                {
                    ecgValue = 0;
                }
                // Q wave (120-140ms)
                else if (timeSinceLastBeat < 0.14)
                {
                    double qPhase = (timeSinceLastBeat - 0.12) / 0.02;
                    ecgValue = -0.1 * Math.Sin(Math.PI * qPhase);
                }
                // R wave (140-160ms)
                else if (timeSinceLastBeat < 0.16)
                {
                    double rPhase = (timeSinceLastBeat - 0.14) / 0.02;
                    ecgValue = 1.2 * Math.Sin(Math.PI * rPhase);
                }
                // S wave (160-180ms)
                else if (timeSinceLastBeat < 0.18)
                {
                    double sPhase = (timeSinceLastBeat - 0.16) / 0.02;
                    ecgValue = -0.2 * Math.Sin(Math.PI * sPhase);
                }
                // ST segment (180-280ms) - slight elevation
                else if (timeSinceLastBeat < 0.28)
                {
                    ecgValue = 0.02;
                }
                // T wave (280-400ms)
                else if (timeSinceLastBeat < 0.40)
                {
                    double tPhase = (timeSinceLastBeat - 0.28) / 0.12;
                    ecgValue = 0.3 * Math.Sin(Math.PI * tPhase);
                }
                // Return to baseline
                else
                {
                    ecgValue = 0;
                }
            }

            return ecgValue;
        }

        // Helper method to calculate standard deviation for HRV
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

    public class DataPacket
    {
        public List<DataPoint> EcgPoints { get; set; } = new List<DataPoint>();
        public List<DataPoint> EdaPoints { get; set; } = new List<DataPoint>();
    }
}