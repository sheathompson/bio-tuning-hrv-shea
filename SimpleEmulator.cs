using System;
using System.Collections.Generic;

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

        public double HeartRate { get; private set; } = 70;
        public double StressLevel { get; private set; } = 0.3;
        public double RespiratoryRate { get; private set; } = 15;

        public void Start()
        {
            isRunning = true;
            currentTime = 0;
            lastBeatTime = -1;
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
            for (int i = 0; i < 10; i++)
            {
                currentTime += 0.01; // Changed from 0.001 to 0.01 seconds per sample (100 Hz)

                // Calculate time since last beat
                double timeSinceLastBeat = currentTime - lastBeatTime;

                // Check if it's time for a new heartbeat
                if (timeSinceLastBeat >= nextBeatInterval)
                {
                    lastBeatTime = currentTime;
                    CalculateNextBeatInterval();
                }

                // Generate ECG value
                double ecgValue = GenerateEcgValue(currentTime, lastBeatTime);

                // Add respiratory baseline wander
                double respiratoryComponent = 0.05 * Math.Sin(2 * Math.PI * RespiratoryRate / 60 * currentTime);

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

                // Add EDA data point
                packet.EdaPoints.Add(new DataPoint
                {
                    Time = currentTime,
                    Value = finalEdaValue
                });
            }

            return packet;
        }

        // In SimpleEmulator class
        private void CalculateNextBeatInterval()
        {
            // Base heart rate that changes slowly over time
            double baseHeartRate = 75.0 + 10.0 * Math.Sin(currentTime * 0.01);

            // Add respiratory sinus arrhythmia (RSA) - heart rate variability linked to breathing
            double rsaComponent = 5.0 * Math.Sin(2 * Math.PI * RespiratoryRate / 60 * currentTime);

            // Sometimes shift toward parasympathetic (lower HR) or sympathetic (higher HR)
            // This creates the oscillation in ANS balance
            double ansBalance = 8.0 * Math.Sin(currentTime * 0.005);

            // Calculate target heart rate with all components
            double targetHeartRate = baseHeartRate + rsaComponent + ansBalance;

            // Keep heart rate in physiological range
            if (targetHeartRate < 55.0) targetHeartRate = 55.0;
            if (targetHeartRate > 95.0) targetHeartRate = 95.0;

            // Calculate beat interval from heart rate (convert BPM to seconds)
            nextBeatInterval = 60.0 / targetHeartRate;

            // Add some random beat-to-beat variability (normal HRV)
            double randomVariability = (random.NextDouble() - 0.5) * 0.05 * nextBeatInterval;
            nextBeatInterval += randomVariability;

            // Store the actual heart rate
            HeartRate = 60.0 / nextBeatInterval;

            // Update LF/HF ratio to match current state (for ANS balance)
            // Higher heart rate = more sympathetic (higher LF/HF ratio)
            double normalizedHR = (HeartRate - 60.0) / 40.0; // 0 at 60 BPM, 1 at 100 BPM

            // Create StressLevel inverse to parasympathetic activity
            // Higher heart rate = more stress
            StressLevel = 0.3 + 0.4 * normalizedHR + 0.1 * Math.Sin(currentTime * 0.1);
            StressLevel = Math.Max(0.1, Math.Min(0.9, StressLevel));

            // Update respiratory rate with slow changes
            RespiratoryRate = 12.0 + 3.0 * Math.Sin(currentTime * 0.003) + (random.NextDouble() - 0.5);
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
    }

    public class DataPacket
    {
        public List<DataPoint> EcgPoints { get; set; } = new List<DataPoint>();
        public List<DataPoint> EdaPoints { get; set; } = new List<DataPoint>();
    }

    // Simple data point class
    public class DataPoint
    {
        public double Time { get; set; }
        public double Value { get; set; }
    }
}