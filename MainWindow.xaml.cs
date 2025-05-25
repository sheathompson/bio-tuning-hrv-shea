using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.IO.Ports;

namespace HRVMonitoringSystem
{
    public partial class MainWindow : Window
    {
        // Observable collections for data binding
        public ObservableCollection<DataPoint> EcgData { get; set; }
        public ObservableCollection<DataPoint> HeartRateData { get; set; }
        public ObservableCollection<DataPoint> LFHFData { get; set; }
        public ObservableCollection<FrequencyPoint> VLFData { get; set; }
        public ObservableCollection<FrequencyPoint> LFData { get; set; }
        public ObservableCollection<FrequencyPoint> HFData { get; set; }

        // Additional collections for missing charts
        public ObservableCollection<DataPoint> EdaData { get; set; }
        public ObservableCollection<DataPoint> FrequencyData { get; set; }

        // Timers and managers
        private DispatcherTimer updateTimer;
        private SimpleEmulator emulator;
        private BITalinoBluetoothManager bitalino;
        private HrvAnalyzer hrvAnalyzer;

        // Data buffers
        private Queue<double> ecgBuffer = new Queue<double>(500);
        private Queue<double> rrIntervals = new Queue<double>(100);
        private int dataPointCount = 0;
        private const int MaxEcgPoints = 500; // Show last 5 seconds at 100Hz
        private const int MaxHeartRatePoints = 300; // 5 minutes of HR data

        // Session tracking
        private DateTime sessionStartTime;
        private bool isRecording = false;
        private bool useHardware = true;

        // Current metrics
        private double currentHeartRate = 0;
        private double currentLFHFRatio = 1.0;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                InitializeData();
                InitializeComponents();

                this.Loaded += MainWindow_Loaded;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization Error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeData()
        {
            // Initialize all data collections
            EcgData = new ObservableCollection<DataPoint>();
            HeartRateData = new ObservableCollection<DataPoint>();
            EdaData = new ObservableCollection<DataPoint>();
            FrequencyData = new ObservableCollection<DataPoint>();
            LFHFData = new ObservableCollection<DataPoint>
            {
                new DataPoint { Category = "LF/HF", Value = 1.0 }
            };

            // Initialize frequency spectrum data
            VLFData = new ObservableCollection<FrequencyPoint>();
            LFData = new ObservableCollection<FrequencyPoint>();
            HFData = new ObservableCollection<FrequencyPoint>();

            // Initialize frequency bands
            InitializeFrequencyBands();

            // Set data context for binding
            DataContext = this;
        }

        private void InitializeFrequencyBands()
        {
            // VLF: 0.003-0.04 Hz
            for (double f = 0.003; f <= 0.04; f += 0.001)
            {
                VLFData.Add(new FrequencyPoint { Frequency = f, Power = 0 });
            }

            // LF: 0.04-0.15 Hz
            for (double f = 0.04; f <= 0.15; f += 0.001)
            {
                LFData.Add(new FrequencyPoint { Frequency = f, Power = 0 });
            }

            // HF: 0.15-0.4 Hz
            for (double f = 0.15; f <= 0.4; f += 0.001)
            {
                HFData.Add(new FrequencyPoint { Frequency = f, Power = 0 });
            }
        }

        private void InitializeComponents()
        {
            // Initialize managers
            emulator = new SimpleEmulator();
            bitalino = new BITalinoBluetoothManager();
            hrvAnalyzer = new HrvAnalyzer();

            // Set up BITalino event handlers
            bitalino.DataReceived += OnBITalinoDataReceived;
            bitalino.StatusChanged += OnBITalinoStatusChanged;
            bitalino.ErrorOccurred += OnBITalinoError;

            // Set up update timer
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromMilliseconds(50); // 20 FPS update rate
            updateTimer.Tick += UpdateTimer_Tick;

            // Set up radio button events
            hardwareRadio.Checked += (s, e) => useHardware = true;
            emulatorRadio.Checked += (s, e) => useHardware = false;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CheckForBITalino();
        }

        private void CheckForBITalino()
        {
            Task.Run(() =>
            {
                var ports = SerialPort.GetPortNames();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ports.Length > 0)
                    {
                        string portList = string.Join(", ", ports);
                        statusText.Text = $"Status: COM ports available: {portList}";
                    }
                    else
                    {
                        statusText.Text = "Status: No COM ports detected - Using emulator";
                        emulatorRadio.IsChecked = true;
                    }
                }));
            });
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear existing data
                EcgData.Clear();
                HeartRateData.Clear();
                EdaData.Clear();
                FrequencyData.Clear();
                ecgBuffer.Clear();
                rrIntervals.Clear();
                dataPointCount = 0;
                dataPointsText.Text = "0";

                // Reset LF/HF display
                LFHFData.Clear();
                LFHFData.Add(new DataPoint { Category = "LF/HF", Value = 1.0 });

                // Reset session
                sessionStartTime = DateTime.Now;

                // Start based on selected mode
                if (useHardware)
                {
                    statusText.Text = "Status: Selecting BITalino port...";

                    bool connected = await bitalino.ConnectWithDialogAsync();

                    if (!connected)
                    {
                        var result = MessageBox.Show(
                            "Could not connect to BITalino.\n\n" +
                            "Make sure:\n" +
                            "• BITalino is powered on\n" +
                            "• Paired in Windows Bluetooth settings\n" +
                            "• Selected the correct COM port\n\n" +
                            "Use emulator mode instead?",
                            "Connection Failed",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            emulatorRadio.IsChecked = true;
                            emulator.Start();
                            statusText.Text = "Status: Recording (Emulator Mode)";
                        }
                        else
                        {
                            statusText.Text = "Status: Ready";
                            return;
                        }
                    }
                    else
                    {
                        statusText.Text = "Status: Recording (BITalino Hardware)";
                    }
                }
                else
                {
                    emulator.Start();
                    statusText.Text = "Status: Recording (Emulator Mode)";
                }

                // Start recording
                isRecording = true;
                updateTimer.Start();

                // Update UI
                startButton.IsEnabled = false;
                stopButton.IsEnabled = true;
                hardwareRadio.IsEnabled = false;
                emulatorRadio.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting recording: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                statusText.Text = "Status: Error - " + ex.Message;
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Stop recording
            isRecording = false;
            updateTimer.Stop();

            // Stop data source
            if (useHardware)
            {
                bitalino.Disconnect();
            }
            else
            {
                emulator.Stop();
            }

            // Update UI
            statusText.Text = "Status: Stopped";
            startButton.IsEnabled = true;
            stopButton.IsEnabled = false;
            hardwareRadio.IsEnabled = true;
            emulatorRadio.IsEnabled = true;

            // Show summary
            var elapsed = DateTime.Now - sessionStartTime;
            var avgHR = HeartRateData.Count > 0 ? HeartRateData.Average(d => d.Value) : 0;

            MessageBox.Show(
                $"Recording Complete!\n\n" +
                $"Duration: {elapsed:mm\\:ss}\n" +
                $"Data Points: {dataPointCount}\n" +
                $"Average Heart Rate: {avgHR:F0} BPM\n" +
                $"Average LF/HF Ratio: {currentLFHFRatio:F2}",
                "Session Summary",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnBITalinoDataReceived(object sender, BITalinoDataEventArgs e)
        {
            // Handle data from BITalino on UI thread
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (isRecording && e.ECGPoints != null && e.ECGPoints.Count > 0)
                {
                    ProcessECGData(e.ECGPoints);
                    UpdateMetrics(e.HeartRate, e.StressLevel);
                }
            }));
        }

        private void OnBITalinoStatusChanged(object sender, string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                statusText.Text = $"Status: {status}";
            }));
        }

        private void OnBITalinoError(object sender, Exception error)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show($"BITalino Error: {error.Message}", "Hardware Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                if (isRecording)
                {
                    StopButton_Click(null, null);
                    emulatorRadio.IsChecked = true;
                }
            }));
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!isRecording) return;

            try
            {
                // Get data from emulator if in emulator mode
                if (!useHardware)
                {
                    var newData = emulator.GetLatestData();
                    ProcessEmulatorData(newData);
                }

                // Update session time
                var elapsed = DateTime.Now - sessionStartTime;
                elapsedTimeText.Text = elapsed.ToString(@"mm\:ss");

                // Update data point count
                dataPointsText.Text = dataPointCount.ToString();

                // Update frequency spectrum periodically
                if (dataPointCount % 100 == 0)
                {
                    UpdateFrequencySpectrum();
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
        }

        private void ProcessEmulatorData(DataPacket packet)
        {
            if (packet == null) return;

            // Process ECG data
            if (packet.EcgPoints != null && packet.EcgPoints.Count > 0)
            {
                ProcessECGData(packet.EcgPoints);
            }

            // Process EDA data
            foreach (var point in packet.EdaPoints)
            {
                EdaData.Add(point);
                if (EdaData.Count > 1000)
                    EdaData.RemoveAt(0);
            }

            // Process heart rate data from emulator
            foreach (var point in packet.HeartRatePoints)
            {
                HeartRateData.Add(point);
                if (HeartRateData.Count > 300)
                    HeartRateData.RemoveAt(0);
            }

            // Update frequency spectrum
            if (packet.FrequencySpectrum != null && packet.FrequencySpectrum.Count > 0)
            {
                FrequencyData.Clear();
                foreach (var point in packet.FrequencySpectrum)
                {
                    FrequencyData.Add(point);
                }

                // Update the frequency band displays
                UpdateFrequencyBands(packet.FrequencySpectrum);
            }

            // Update LF/HF ratio
            if (packet.LfHfRatio > 0)
            {
                currentLFHFRatio = packet.LfHfRatio;
                LFHFData[0].Value = currentLFHFRatio;
                lfhfValueText.Text = $"{currentLFHFRatio:F2}";

                // Color code based on ratio
                if (currentLFHFRatio < 0.5)
                    lfhfValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);
                else if (currentLFHFRatio > 2.0)
                    lfhfValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                else
                    lfhfValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }

            // Update metrics
            UpdateMetrics(emulator.HeartRate, emulator.StressLevel);
        }

        private void ProcessECGData(List<DataPoint> ecgPoints)
        {
            foreach (var point in ecgPoints)
            {
                // Add to display collection
                EcgData.Add(point);

                // Maintain maximum points for display
                if (EcgData.Count > MaxEcgPoints)
                    EcgData.RemoveAt(0);

                // Add to analysis buffer
                ecgBuffer.Enqueue(point.Value);
                if (ecgBuffer.Count > 500)
                    ecgBuffer.Dequeue();

                dataPointCount++;

                // Detect R-peaks for HRV analysis
                if (ecgBuffer.Count >= 100)
                {
                    double[] ecgArray = ecgBuffer.ToArray();
                    double peakTime = hrvAnalyzer.DetectRPeaks(ecgArray, point.Time);

                    if (peakTime > 0)
                    {
                        hrvAnalyzer.AddRPeak(peakTime);

                        // Calculate instantaneous heart rate
                        var metrics = hrvAnalyzer.CalculateMetrics();
                        if (metrics.HeartRate > 0)
                        {
                            currentHeartRate = metrics.HeartRate;

                            // Add to heart rate chart
                            var hrPoint = new DataPoint
                            {
                                Time = DateTime.Now.Subtract(sessionStartTime).TotalSeconds,
                                Value = currentHeartRate,
                                DateTime = DateTime.Now
                            };
                            HeartRateData.Add(hrPoint);

                            if (HeartRateData.Count > MaxHeartRatePoints)
                                HeartRateData.RemoveAt(0);

                            // Update heart rate display
                            heartRateText.Text = $"{currentHeartRate:F0} BPM";
                        }

                        // Update HRV metrics
                        UpdateHrvMetricsDisplay(metrics);
                    }
                }
            }
        }

        private void UpdateHrvMetricsDisplay(HrvMetrics metrics)
        {
            if (metrics.SDNN > 0)
            {
                sdnnText.Text = $"{metrics.SDNN:F1}";
                rmssdText.Text = $"{metrics.RMSSD:F1}";
                pnn50Text.Text = $"{metrics.pNN50:F1}";

                // Update LF/HF ratio if available
                if (metrics.LFHFRatio > 0)
                {
                    currentLFHFRatio = metrics.LFHFRatio;
                    LFHFData[0].Value = currentLFHFRatio;
                    lfhfValueText.Text = $"{currentLFHFRatio:F2}";
                }
            }
        }

        private void UpdateMetrics(double heartRate, double stressLevel)
        {
            // Update heart rate display
            if (heartRate > 0)
            {
                heartRateText.Text = $"{heartRate:F0} BPM";

                // Color code based on values
                if (heartRate > 100)
                    heartRateText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                else if (heartRate < 60)
                    heartRateText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);
                else
                    heartRateText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
            }
        }

        private void UpdateFrequencyBands(List<DataPoint> spectrum)
        {
            double vlfPower = 0, lfPower = 0, hfPower = 0;

            foreach (var point in spectrum)
            {
                if (point.Time < 0.04)
                    vlfPower += point.Value;
                else if (point.Time < 0.15)
                    lfPower += point.Value;
                else if (point.Time < 0.4)
                    hfPower += point.Value;
            }

            // Update frequency band data
            foreach (var vlfPoint in VLFData)
            {
                var specPoint = spectrum.FirstOrDefault(p => Math.Abs(p.Time - vlfPoint.Frequency) < 0.001);
                if (specPoint != null)
                    vlfPoint.Power = specPoint.Value;
            }

            foreach (var lfPoint in LFData)
            {
                var specPoint = spectrum.FirstOrDefault(p => Math.Abs(p.Time - lfPoint.Frequency) < 0.001);
                if (specPoint != null)
                    lfPoint.Power = specPoint.Value;
            }

            foreach (var hfPoint in HFData)
            {
                var specPoint = spectrum.FirstOrDefault(p => Math.Abs(p.Time - hfPoint.Frequency) < 0.001);
                if (specPoint != null)
                    hfPoint.Power = specPoint.Value;
            }

            // Update power displays
            if (lfPowerText != null) lfPowerText.Text = $"{lfPower:F0}";
            if (hfPowerText != null) hfPowerText.Text = $"{hfPower:F0}";
        }

        private void UpdateFrequencySpectrum()
        {
            // This is called periodically to update any additional frequency analysis
            // The actual spectrum is updated via ProcessEmulatorData
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            var testWindow = new BITalinoTestWindow();
            testWindow.ShowDialog();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Clean up resources
            updateTimer?.Stop();
            bitalino?.Dispose();

            if (emulator != null && isRecording)
            {
                emulator.Stop();
            }
        }
    }

    // Helper class for frequency spectrum
    public class FrequencyPoint
    {
        public double Frequency { get; set; }
        public double Power { get; set; }
    }
}