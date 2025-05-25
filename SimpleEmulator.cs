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

        // Properties for display
        public double HeartRate { get; private set; } = 70;
        public double StressLevel { get; private set; } = 0.3;
        public double RespiratoryRate { get; private set; } = 15;
        public bool RpeakDetected { get; private set; } = false;

        // Additional metrics - ONLY DECLARED ONCE
        public double CurrentRRInterval { get; private set; } = 857; // ms
        public double CurrentHeartRate { get; private set; } = 70;

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
                currentTime += 0.001; // 1ms per sample (1000 Hz)

                // Calculate time since last beat
                double timeSinceLastBeat = currentTime - lastBeatTime;

                // Check if it's time for a new heartbeat
                RpeakDetected = false;
                if (timeSinceLastBeat >= nextBeatInterval)
                {
                    lastBeatTime = currentTime;
                    CalculateNextBeatInterval();
                    RpeakDetected = true;

                    // Update current metrics
                    CurrentRRInterval = nextBeatInterval * 1000; // Convert to ms
                    CurrentHeartRate = 60.0 / nextBeatInterval;
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
                double edaBaseLevel = 2.0 + StressLevel * 5.0;
                double scl = edaBaseLevel + 0.2 * Math.Sin(2 * Math.PI * 0.05 * currentTime);

                // Add occasional SCRs
                double scr = 0;
                if (random.NextDouble() < StressLevel * 0.01)
                {
                    lastScrTime = currentTime;
                    scrActive = true;
                    scrAmplitudeValue = 0.5 * StressLevel * (1 + random.NextDouble());
                }

                if (scrActive)
                {
                    double scrTime = currentTime - lastScrTime;
                    if (scrTime < 5.0)
                    {
                        if (scrTime < 1.0)
                            scr = scrAmplitudeValue * (scrTime / 1.0);
                        else
                            scr = scrAmplitudeValue * Math.Exp(-(scrTime - 1.0) / 2.0);
                    }
                    else
                    {
                        scrActive = false;
                    }
                }

                double edaNoise = (random.NextDouble() - 0.5) * 0.05;
                double finalEdaValue = scl + scr + edaNoise;

                packet.EdaPoints.Add(new DataPoint
                {
                    Time = currentTime,
                    Value = finalEdaValue
                });

                // Add heart rate data point (only when R-peak detected)
                if (RpeakDetected)
                {
                    packet.HeartRatePoints.Add(new DataPoint
                    {
                        Time = currentTime,
                        Value = CurrentHeartRate
                    });
                }

                // Generate frequency data periodically (every 100ms)
                if ((int)(currentTime * 10) % 1 == 0 && i == 0)
                {
                    GenerateFrequencyData(packet);
                }
            }

            return packet;
        }

        private void GenerateFrequencyData(DataPacket packet)
        {
            // Generate simulated frequency spectrum
            var spectrum = new List<DataPoint>();

            // Create frequency bins from 0 to 0.5 Hz
            for (double freq = 0; freq <= 0.5; freq += 0.001)
            {
                double power = 0;

                // VLF component (0-0.04 Hz)
                if (freq < 0.04)
                {
                    power += 50 * Math.Exp(-Math.Pow((freq - 0.02) / 0.01, 2));
                }

                // LF component (0.04-0.15 Hz) - influenced by stress
                if (freq >= 0.04 && freq < 0.15)
                {
                    double lfCenter = 0.1;
                    power += (30 + StressLevel * 40) * Math.Exp(-Math.Pow((freq - lfCenter) / 0.03, 2));
                }

                // HF component (0.15-0.4 Hz) - influenced by breathing
                if (freq >= 0.15 && freq < 0.4)
                {
                    double hfCenter = RespiratoryRate / 60.0; // Respiratory frequency in Hz
                    power += (40 - StressLevel * 20) * Math.Exp(-Math.Pow((freq - hfCenter) / 0.05, 2));
                }

                // Add some noise
                power += (random.NextDouble() - 0.5) * 2;
                power = Math.Max(0, power);

                spectrum.Add(new DataPoint { Time = freq, Value = power });
            }

            packet.FrequencySpectrum = spectrum;

            // Calculate LF/HF ratio
            double lfPower = 0, hfPower = 0;
            foreach (var point in spectrum)
            {
                if (point.Time >= 0.04 && point.Time < 0.15)
                    lfPower += point.Value;
                else if (point.Time >= 0.15 && point.Time < 0.4)
                    hfPower += point.Value;
            }

            if (hfPower > 0)
            {
                packet.LfHfRatio = lfPower / hfPower;
            }
        }

        private void CalculateNextBeatInterval()
        {
            double baseInterval = 60.0 / HeartRate;
            double hrvComponent = (random.NextDouble() - 0.5) * 0.1 * baseInterval;
            hrvComponent *= (1 - StressLevel * 0.5);

            nextBeatInterval = baseInterval + hrvComponent;

            // Update heart rate with more realistic variation
            HeartRate = 70 + Math.Sin(currentTime * 0.05) * 5 + (random.NextDouble() - 0.5) * 2;
            StressLevel = Math.Max(0, Math.Min(1, StressLevel + (random.NextDouble() - 0.5) * 0.01));

            // Update respiratory rate
            RespiratoryRate = 15 + Math.Sin(currentTime * 0.02) * 3;
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
}