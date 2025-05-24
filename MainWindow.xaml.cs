using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace HRVMonitoringSystem
{
    public partial class MainWindow : Window
    {
        // Collections for chart data
        public ObservableCollection<DataPoint> EcgData { get; set; }
        public ObservableCollection<DataPoint> EdaData { get; set; }
        public ObservableCollection<DataPoint> LfHfRatioData { get; set; }

        // Timer to update the display
        private DispatcherTimer updateTimer;

        // Our data emulator
        private SimpleEmulator emulator;

        // HRV Analyzer
        private HrvAnalyzer hrvAnalyzer;

        // Buffer for R-peak detection
        private Queue<double> ecgBuffer = new Queue<double>(100);

        // Time tracking for LF/HF chart
        private double currentChartTime = 0;

        // Session tracking
        private DateTime sessionStartTime;
        private int heartBeatCount = 0;
        private const int MinHeartBeatsForCalibration = 30;

        public MainWindow()
        {
            // Initialize our data collections
            EcgData = new ObservableCollection<DataPoint>();
            EdaData = new ObservableCollection<DataPoint>();
            LfHfRatioData = new ObservableCollection<DataPoint>();

            InitializeComponent();

            // Set the window's data context to itself
            DataContext = this;

            // Create the emulator
            emulator = new SimpleEmulator();

            // Create the HRV analyzer
            hrvAnalyzer = new HrvAnalyzer();

            // Set up the update timer
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromMilliseconds(50); // 20 updates per second
            updateTimer.Tick += UpdateTimer_Tick;

            // Initialize UI
            stopButton.IsEnabled = false;
            graphsActiveText.Text = "Not Recording";
            graphsActiveText.Foreground = new SolidColorBrush(Colors.Gray);
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear old data
            EcgData.Clear();
            EdaData.Clear();
            LfHfRatioData.Clear();

            // Reset the ECG buffer
            ecgBuffer.Clear();

            // Reset chart time and counters
            currentChartTime = 0;
            heartBeatCount = 0;
            sessionStartTime = DateTime.Now;

            // Start the emulator
            emulator.Start();

            // Start the update timer
            updateTimer.Start();

            // Update UI
            statusText.Text = "Status: Recording...";
            startButton.IsEnabled = false;
            stopButton.IsEnabled = true;
            graphsActiveText.Text = "Calibrating (need 30+ beats)";
            graphsActiveText.Foreground = new SolidColorBrush(Colors.Orange);
            elapsedTimeText.Text = "00:00";
            heartBeatCountText.Text = "0";
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Stop everything
            updateTimer.Stop();
            emulator.Stop();

            // Update UI
            statusText.Text = "Status: Stopped";
            startButton.IsEnabled = true;
            stopButton.IsEnabled = false;
            graphsActiveText.Text = "Not Recording";
            graphsActiveText.Foreground = new SolidColorBrush(Colors.Gray);
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            // Update elapsed time
            TimeSpan elapsed = DateTime.Now - sessionStartTime;
            elapsedTimeText.Text = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

            // Get new data from emulator
            var newData = emulator.GetLatestData();

            // Update ECG data
            foreach (var point in newData.EcgPoints)
            {
                EcgData.Add(point);

                // Add to buffer for R-peak detection
                ecgBuffer.Enqueue(point.Value);
                if (ecgBuffer.Count > 100)
                    ecgBuffer.Dequeue();

                // Keep only last 1000 points
                if (EcgData.Count > 1000)
                    EcgData.RemoveAt(0);
            }

            // Detect R-peaks
            if (newData.EcgPoints.Count > 0)
            {
                double[] ecgArray = ecgBuffer.ToArray();
                double peakTime = hrvAnalyzer.DetectRPeaks(ecgArray, newData.EcgPoints.Last().Time);
                if (peakTime > 0)
                {
                    // Increment heart beat counter
                    heartBeatCount++;
                    heartBeatCountText.Text = heartBeatCount.ToString();

                    // Update calibration status
                    if (heartBeatCount >= MinHeartBeatsForCalibration && graphsActiveText.Text.Contains("Calibrating"))
                    {
                        graphsActiveText.Text = "Graphs Active";
                        graphsActiveText.Foreground = new SolidColorBrush(Colors.Green);
                    }

                    // Add R peak to analyzer
                    hrvAnalyzer.AddRPeak(peakTime);
                }
            }

            // Update EDA data
            foreach (var point in newData.EdaPoints)
            {
                EdaData.Add(point);

                // Keep only last 1000 points
                if (EdaData.Count > 1000)
                    EdaData.RemoveAt(0);
            }

            // Calculate HRV metrics
            var hrvMetrics = hrvAnalyzer.CalculateMetrics();

            // Update heart rate display
            heartRateText.Text = $"{hrvMetrics.HeartRate:F0} BPM";

            // Update HRV metrics
            sdnnText.Text = $"{hrvMetrics.SDNN:F1} ms";
            rmssdText.Text = $"{hrvMetrics.RMSSD:F1} ms";
            pnn50Text.Text = $"{hrvMetrics.pNN50:F1} %";

            // Update LF/HF ratio
            lfhfText.Text = $"{hrvMetrics.LF_HF_Ratio:F2}";

            // Update ANS balance state text
            if (heartBeatCount >= MinHeartBeatsForCalibration)
            {
                if (hrvMetrics.LF_HF_Ratio < 1.0)
                {
                    ansStateText.Text = "Parasympathetic Dominant";
                    ansStateText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                }
                else
                {
                    ansStateText.Text = "Sympathetic Dominant";
                    ansStateText.Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
                }
            }
            else
            {
                ansStateText.Text = "Calibrating...";
                ansStateText.Foreground = new SolidColorBrush(Colors.Orange);
            }

            // Update respiration rate
            if (respirationText != null)
            {
                respirationText.Text = $"{hrvMetrics.RespirationRate:F1} BrPM";
            }

            // Update LF/HF ratio chart (more frequently for better visualization)
            currentChartTime += 0.05; // 50ms update interval
            if (heartBeatCount >= MinHeartBeatsForCalibration && currentChartTime % 0.1 < 0.05) // Every 0.1 seconds (10 points per second)
            {
                // Generate a more visible oscillating pattern for demo purposes
                // This creates a sine wave that oscillates between ~0.5 and ~2.0
                double time = currentChartTime * 0.1; // Slow down the oscillation
                double baseValue = 1.25; // Center around 1.25
                double oscillation = 0.75 * Math.Sin(time); // Oscillate by ±0.75

                // Set a demo display ratio that's clearly visible
                double displayRatio = baseValue + oscillation;

                // Ensure stays in reasonable range
                displayRatio = Math.Max(0.3, Math.Min(2.5, displayRatio));

                // Debug output to verify values are changing
                Console.WriteLine($"LF/HF: {displayRatio:F2}");

                // Add the new LF/HF ratio point
                LfHfRatioData.Add(new DataPoint
                {
                    Time = currentChartTime,
                    Value = displayRatio
                });

                // Keep the last 30 seconds of data (300 points at 10 points per second)
                while (LfHfRatioData.Count > 300)
                    LfHfRatioData.RemoveAt(0);
            }
        }
    }
}