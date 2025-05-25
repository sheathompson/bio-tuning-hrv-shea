using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HRVMonitoringSystem
{
    /// <summary>
    /// Direct serial connection to BITalino - no Python required!
    /// </summary>
    public class BITalinoDirectManager : IDisposable
    {
        private SerialPort serialPort;
        private Thread readThread;
        private bool isRunning;
        private Queue<double> ecgBuffer = new Queue<double>(100);
        private Queue<double> edaBuffer = new Queue<double>(100);

        // Configuration
        private int samplingRate = 100; // Hz
        private int[] channels = { 0, 1 }; // A1 (ECG), A2 (EDA)

        // Events
        public event EventHandler<BITalinoDataEventArgs> DataReceived;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<Exception> ErrorOccurred;

        // Properties
        public double HeartRate { get; private set; } = 70;
        public double StressLevel { get; private set; } = 0.3;
        public bool IsConnected => serialPort?.IsOpen ?? false;

        /// <summary>
        /// Find available COM ports with BITalino devices
        /// </summary>
        public static string[] FindBITalinoPorts()
        {
            var ports = SerialPort.GetPortNames();
            var bitalinoports = new List<string>();

            foreach (var port in ports)
            {
                try
                {
                    using (var testPort = new SerialPort(port, 115200))
                    {
                        testPort.ReadTimeout = 1000;
                        testPort.Open();

                        // Try to get version
                        testPort.Write(new byte[] { 0x07 }, 0, 1);
                        Thread.Sleep(100);

                        if (testPort.BytesToRead > 0)
                        {
                            var response = testPort.ReadLine();
                            if (response.Contains("BITalino"))
                            {
                                bitalinoports.Add(port);
                            }
                        }

                        testPort.Close();
                    }
                }
                catch { }
            }

            return bitalinoports.ToArray();
        }

        /// <summary>
        /// Connect to BITalino via serial port
        /// </summary>
        public async Task<bool> ConnectAsync(string comPort = null)
        {
            try
            {
                StatusChanged?.Invoke(this, "Searching for BITalino...");

                // If no port specified, try to find one
                if (string.IsNullOrEmpty(comPort))
                {
                    var ports = FindBITalinoPorts();
                    if (ports.Length == 0)
                    {
                        StatusChanged?.Invoke(this, "No BITalino found on any COM port");
                        return false;
                    }
                    comPort = ports[0];
                }

                StatusChanged?.Invoke(this, $"Connecting to BITalino on {comPort}...");

                // Create and configure serial port
                serialPort = new SerialPort(comPort)
                {
                    BaudRate = 115200,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Parity = Parity.None,
                    ReadTimeout = 5000,
                    WriteTimeout = 5000
                };

                // Open port
                serialPort.Open();

                // Wait a bit for connection to stabilize
                await Task.Delay(1000);

                // Configure acquisition
                ConfigureAcquisition();

                // Start reading thread
                isRunning = true;
                readThread = new Thread(ReadLoop) { IsBackground = true };
                readThread.Start();

                StatusChanged?.Invoke(this, $"Connected to BITalino on {comPort}!");
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                StatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Configure BITalino acquisition settings
        /// </summary>
        private void ConfigureAcquisition()
        {
            // Stop any ongoing acquisition
            serialPort.Write(new byte[] { 0x00 }, 0, 1);
            Thread.Sleep(100);

            // Set sampling rate (100Hz = 0x03)
            byte srCommand = (byte)(0x80 | 0x03); // 100Hz
            serialPort.Write(new byte[] { srCommand }, 0, 1);
            Thread.Sleep(100);

            // Configure channels (A1 and A2)
            byte channelMask = 0x03; // Channels 0 and 1
            byte startCommand = (byte)(0x10 | channelMask);
            serialPort.Write(new byte[] { startCommand }, 0, 1);
        }

        /// <summary>
        /// Main read loop for serial data
        /// </summary>
        private void ReadLoop()
        {
            byte[] buffer = new byte[8]; // Frame size for 2 channels
            int bytesRead = 0;

            try
            {
                while (isRunning && serialPort.IsOpen)
                {
                    // Read frame
                    while (bytesRead < buffer.Length && serialPort.IsOpen)
                    {
                        bytesRead += serialPort.Read(buffer, bytesRead, buffer.Length - bytesRead);
                    }

                    if (bytesRead == buffer.Length)
                    {
                        ProcessFrame(buffer);
                        bytesRead = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                if (isRunning)
                {
                    ErrorOccurred?.Invoke(this, ex);
                }
            }
        }

        /// <summary>
        /// Process a data frame from BITalino
        /// </summary>
        private void ProcessFrame(byte[] frame)
        {
            // Extract 10-bit channel values
            // Frame format depends on number of channels
            // For 2 channels at 100Hz, we get specific byte arrangement

            int ecgRaw = ((frame[1] & 0x03) << 8) | frame[2]; // Channel 0 (A1)
            int edaRaw = ((frame[3] & 0x0F) << 6) | ((frame[4] & 0xFC) >> 2); // Channel 1 (A2)

            // Convert to voltage (0-3.3V for 10-bit ADC)
            double ecgVoltage = (ecgRaw / 1024.0) * 3.3;
            double edaVoltage = (edaRaw / 1024.0) * 3.3;

            // Apply sensor-specific conversions
            double ecgValue = (ecgVoltage - 1.65) * 2.0; // Center around 0
            double edaValue = (edaVoltage / 3.3) * 10.0; // Convert to microsiemens

            // Add to buffers
            ecgBuffer.Enqueue(ecgValue);
            if (ecgBuffer.Count > 100) ecgBuffer.Dequeue();

            edaBuffer.Enqueue(edaValue);
            if (edaBuffer.Count > 100) edaBuffer.Dequeue();

            // Calculate metrics periodically
            if (ecgBuffer.Count % 10 == 0)
            {
                HeartRate = CalculateHeartRate();
                StressLevel = edaBuffer.Average() / 10.0;
            }

            // Create data points
            double timestamp = DateTime.Now.Subtract(DateTime.Today).TotalSeconds;

            var ecgPoints = new List<DataPoint>
            {
                new DataPoint { Time = timestamp, Value = ecgValue }
            };

            var edaPoints = new List<DataPoint>
            {
                new DataPoint { Time = timestamp, Value = edaValue }
            };

            // Raise event
            DataReceived?.Invoke(this, new BITalinoDataEventArgs
            {
                ECGPoints = ecgPoints,
                EDAPoints = edaPoints,
                HeartRate = HeartRate,
                StressLevel = StressLevel
            });
        }

        /// <summary>
        /// Simple heart rate calculation
        /// </summary>
        private double CalculateHeartRate()
        {
            if (ecgBuffer.Count < 50) return 70;

            var ecgArray = ecgBuffer.ToArray();
            double mean = ecgArray.Average();
            double std = Math.Sqrt(ecgArray.Select(x => Math.Pow(x - mean, 2)).Average());
            double threshold = mean + std * 0.6;

            var peaks = new List<int>();
            for (int i = 1; i < ecgArray.Length - 1; i++)
            {
                if (ecgArray[i] > threshold &&
                    ecgArray[i] > ecgArray[i - 1] &&
                    ecgArray[i] > ecgArray[i + 1])
                {
                    peaks.Add(i);
                }
            }

            if (peaks.Count >= 2)
            {
                double avgInterval = peaks.Skip(1).Select((p, i) => p - peaks[i]).Average() / (double)samplingRate;
                double hr = 60.0 / avgInterval;
                return Math.Max(40, Math.Min(200, hr));
            }

            return 70;
        }

        /// <summary>
        /// Disconnect from BITalino
        /// </summary>
        public void Disconnect()
        {
            isRunning = false;

            try
            {
                if (serialPort?.IsOpen == true)
                {
                    // Stop acquisition
                    serialPort.Write(new byte[] { 0x00 }, 0, 1);
                    Thread.Sleep(100);

                    serialPort.Close();
                }
            }
            catch { }

            readThread?.Join(1000);
            serialPort?.Dispose();

            StatusChanged?.Invoke(this, "Disconnected from BITalino");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}