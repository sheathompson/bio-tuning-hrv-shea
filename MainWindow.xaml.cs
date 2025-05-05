using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Charts;

namespace HRVMonitoringSystem
{
    public partial class MainWindow : Window
    {
        // This will hold our data points
        public ObservableCollection<DataPoint> EcgData { get; set; }
        public ObservableCollection<DataPoint> EdaData { get; set; }
        public ObservableCollection<DataPoint> AnsBalanceData { get; set; }
        public ObservableCollection<DataPoint> SympatheticData { get; set; }
        public ObservableCollection<DataPoint> ParasympatheticData { get; set; }

        // Timer to update the display
        private DispatcherTimer updateTimer;

        // Our data emulator
        private SimpleEmulator emulator;

        // HRV Analyzer
        private HrvAnalyzer hrvAnalyzer;

        // Buffer for R-peak detection
        private Queue<double> ecgBuffer = new Queue<double>(100);

        // Buffer for additional smoothing of ANS display
        private Queue<DataPoint> ansDisplayBuffer = new Queue<DataPoint>(20);

        // Counters for heartbeats and time
        private DateTime startTime;
        private int heartbeatCount = 0;

        // To control update frequency - don't update UI at every tick
        private int updateCounter = 0;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize our data collection
            EcgData = new ObservableCollection<DataPoint>();
            EdaData = new ObservableCollection<DataPoint>();
            AnsBalanceData = new ObservableCollection<DataPoint>();
            SympatheticData = new ObservableCollection<DataPoint>();  // Initialize sympathetic data
            ParasympatheticData = new ObservableCollection<DataPoint>();  // Initialize parasympathetic data

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
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            // Clear old data
            EcgData.Clear();
            EdaData.Clear();
            AnsBalanceData.Clear();
            SympatheticData.Clear();  // Clear sympathetic data
            ParasympatheticData.Clear();  // Clear parasympathetic data
            ansDisplayBuffer.Clear();  // Clear ANS buffer

            // Reset the ECG buffer
            ecgBuffer.Clear();

            // Reset counters
            startTime = DateTime.Now;
            heartbeatCount = 0;
            updateCounter = 0;

            // Start the emulator
            emulator.Start();

            // Start the update timer
            updateTimer.Start();

            statusText.Text = "Status: Recording...";
            startButton.IsEnabled = false;
            stopButton.IsEnabled = true;
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            // Stop everything
            updateTimer.Stop();
            emulator.Stop();

            statusText.Text = "Status: Stopped";
            startButton.IsEnabled = true;
            stopButton.IsEnabled = false;
        }

        // Helper method to smooth ANS data for better visualization
        private List<DataPoint> SmoothData(IEnumerable<DataPoint> data, int windowSize = 5)
        {
            var result = new List<DataPoint>();
            var window = new LinkedList<DataPoint>();

            foreach (var point in data)
            {
                window.AddLast(point);
                if (window.Count > windowSize)
                    window.RemoveFirst();

                // Calculate average value
                double avgValue = window.Average(p => p.Value);

                result.Add(new DataPoint
                {
                    Time = point.Time,
                    Value = avgValue
                });
            }

            return result;
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
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
                    hrvAnalyzer.AddRPeak(peakTime);
                    heartbeatCount++; // Increment heartbeat counter
                }
            }

            // Update elapsed time
            TimeSpan elapsed = DateTime.Now - startTime;
            elapsedTimeText.Text = string.Format("{0:00}:{1:00}",
                                    Math.Floor(elapsed.TotalMinutes),
                                    elapsed.Seconds);

            // Update heartbeat count
            heartbeatCountText.Text = heartbeatCount.ToString();

            // Update EDA data
            foreach (var point in newData.EdaPoints)
            {
                EdaData.Add(point);

                // Keep only last 1000 points
                if (EdaData.Count > 1000)
                    EdaData.RemoveAt(0);
            }

            // Update ANS balance data
            if (newData.EcgPoints.Count > 0)
            {
                // Get the latest data point
                double time = newData.EcgPoints.Last().Time;
                double balanceValue = emulator.AnsBalance;

                // Add to ANS buffer for additional smoothing
                ansDisplayBuffer.Enqueue(new DataPoint { Time = time, Value = balanceValue });
                if (ansDisplayBuffer.Count > 20)
                    ansDisplayBuffer.Dequeue();

                // Only update visualization every several ticks to reduce visual noise
                updateCounter++;
                if (updateCounter >= 3)  // Update every 3 ticks
                {
                    updateCounter = 0;

                    // Clear existing data
                    AnsBalanceData.Clear();
                    SympatheticData.Clear();
                    ParasympatheticData.Clear();

                    // Apply additional smoothing for visualization
                    var smoothedPoints = SmoothData(ansDisplayBuffer, 10);

                    // Add smoothed data to the UI collections
                    foreach (var point in smoothedPoints)
                    {
                        // Add to main balance data
                        AnsBalanceData.Add(point);

                        // Add to sympathetic data (above 50)
                        SympatheticData.Add(new DataPoint
                        {
                            Time = point.Time,
                            Value = point.Value > 50 ? point.Value : 50
                        });

                        // Add to parasympathetic data (below 50)
                        ParasympatheticData.Add(new DataPoint
                        {
                            Time = point.Time,
                            Value = point.Value < 50 ? point.Value : 0
                        });
                    }
                }
            }

            // Update LF/HF display - calculate based on ANS balance
            double lfhfValue = emulator.AnsBalance < 50 ?
                0.5 * (emulator.AnsBalance / 50) :
                0.5 + 2.5 * ((emulator.AnsBalance - 50) / 50);
            lfhfText.Text = $"{lfhfValue:F2}";

            // Calculate HRV metrics
            var hrvMetrics = hrvAnalyzer.CalculateMetrics();

            // Update ANS state text and color based on data availability
            if (heartbeatCount < 30)
            {
                stateText.Text = "Collecting Data...";
                stateText.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                if (emulator.AnsBalance < 40)
                {
                    stateText.Text = "Parasympathetic Dominant";
                    stateText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (emulator.AnsBalance > 60)
                {
                    stateText.Text = "Sympathetic Dominant";
                    stateText.Foreground = System.Windows.Media.Brushes.Red;
                }
                else
                {
                    stateText.Text = "Balanced";
                    stateText.Foreground = System.Windows.Media.Brushes.DarkGoldenrod;
                }
            }

            // Update heart rate display (use calculated HR if available, otherwise use emulator HR)
            if (hrvMetrics.HeartRate > 0)
                heartRateText.Text = $"{hrvMetrics.HeartRate:F0} BPM";
            else
                heartRateText.Text = $"{emulator.HeartRate:F0} BPM";

            // Update HRV metrics
            sdnnText.Text = $"{hrvMetrics.SDNN:F1} ms";
            rmssdText.Text = $"{hrvMetrics.RMSSD:F1} ms";
            pnn50Text.Text = $"{hrvMetrics.pNN50:F1} %";

            // Update stress display
            stressText.Text = $"{emulator.StressLevel * 100:F0}%";
        }
    }

    // Simple data point class
    public class DataPoint
    {
        public double Time { get; set; }
        public double Value { get; set; }
    }
}