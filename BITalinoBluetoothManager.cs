using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HRVMonitoringSystem
{
    public class BITalinoBluetoothManager : IDisposable
    {
        private SerialPort serialPort;
        private Thread readThread;
        private bool isRunning;
        private Queue<double> ecgBuffer = new Queue<double>(100);
        private List<double> rawBuffer = new List<double>();

        // Events
        public event EventHandler<BITalinoDataEventArgs> DataReceived;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<Exception> ErrorOccurred;

        // Properties
        public double HeartRate { get; private set; } = 70;
        public double StressLevel { get; private set; } = 0.3;
        public bool IsConnected => serialPort?.IsOpen ?? false;

        /// <summary>
        /// Find all Bluetooth COM ports with friendly names
        /// </summary>
        public static List<ComPortInfo> FindBluetoothPorts()
        {
            var portList = new List<ComPortInfo>();

            try
            {
                // Use WMI to get detailed port information
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'"))
                {
                    var ports = searcher.Get().Cast<ManagementObject>().ToList();

                    foreach (var port in ports)
                    {
                        var caption = port["Caption"]?.ToString() ?? "";

                        // Extract COM port number
                        int startIndex = caption.IndexOf("(COM");
                        if (startIndex >= 0)
                        {
                            int endIndex = caption.IndexOf(")", startIndex);
                            if (endIndex > startIndex)
                            {
                                string comPort = caption.Substring(startIndex + 1, endIndex - startIndex - 1);
                                string friendlyName = caption.Substring(0, startIndex).Trim();

                                // Check if it's likely a BITalino Bluetooth port
                                bool isBitalino = caption.ToLower().Contains("bluetooth") &&
                                                 (caption.Contains("Outgoing") || !caption.Contains("Incoming"));

                                portList.Add(new ComPortInfo
                                {
                                    PortName = comPort,
                                    FriendlyName = friendlyName,
                                    FullDescription = caption,
                                    IsBitalino = isBitalino
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback to basic port list
                var ports = SerialPort.GetPortNames();
                foreach (var port in ports)
                {
                    // Check if port exists
                    try
                    {
                        using (var testPort = new SerialPort(port))
                        {
                            // Just checking if we can create it
                        }

                        portList.Add(new ComPortInfo
                        {
                            PortName = port,
                            FriendlyName = $"Serial Port {port}",
                            FullDescription = port,
                            IsBitalino = false
                        });
                    }
                    catch
                    {
                        // Skip ports that don't exist
                    }
                }
            }

            return portList.OrderBy(p => p.PortName).ToList();
        }

        /// <summary>
        /// Show port selection dialog and connect
        /// </summary>
        public async Task<bool> ConnectWithDialogAsync()
        {
            var ports = FindBluetoothPorts();

            if (ports.Count == 0)
            {
                MessageBox.Show(
                    "No COM ports found.\n\n" +
                    "For Bluetooth connection:\n" +
                    "1. Turn on BITalino (blue LED blinking)\n" +
                    "2. Pair in Windows Bluetooth settings\n" +
                    "3. Look for 'BITalino-XX-XX'\n" +
                    "4. Use the Outgoing COM port\n\n" +
                    "For USB: Connect cable and install drivers",
                    "No Ports Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            // Show selection dialog
            var dialog = new PortSelectionDialog(ports);
            if (dialog.ShowDialog() == true && dialog.SelectedPort != null)
            {
                return await ConnectAsync(dialog.SelectedPort.PortName);
            }

            return false;
        }

        /// <summary>
        /// Connect to specific COM port
        /// </summary>
        public async Task<bool> ConnectAsync(string comPort)
        {
            try
            {
                StatusChanged?.Invoke(this, $"Connecting to {comPort}...");
                // Create and configure serial port for BITalino (r)evolution
                serialPort = new SerialPort(comPort)
                {
                    BaudRate = 115200,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Parity = Parity.None,
                    ReadTimeout = 5000,  // Longer timeout for Bluetooth
                    WriteTimeout = 5000,
                    DtrEnable = false,   // Important for BITalino
                    RtsEnable = false    // Important for BITalino
                };
                // Open port
                serialPort.Open();
                StatusChanged?.Invoke(this, "Port opened, initializing BITalino...");
                // CRITICAL: Wait for BITalino to initialize after connection
                await Task.Delay(5000); // 3 seconds for Bluetooth
                                        // Clear any garbage data
                if (serialPort.BytesToRead > 0)
                {
                    serialPort.DiscardInBuffer();
                }
                // Start acquisition with simple command
                StatusChanged?.Invoke(this, "Starting data acquisition...");
                // For BITalino (r)evolution, just send start command
                byte[] startCmd = { 0x01 }; // Changed back to 0x01 for A1 only
                serialPort.Write(startCmd, 0, startCmd.Length);
                await Task.Delay(3000); // Increased delay to 1 second
                                        // Check if we're getting data
                if (serialPort.BytesToRead > 0)
                {
                    StatusChanged?.Invoke(this, "BITalino is streaming data!");
                    // Start reading thread
                    isRunning = true;
                    readThread = new Thread(ReadLoop) { IsBackground = true };
                    readThread.Start();
                    return true;
                }
                else
                {
                    StatusChanged?.Invoke(this, "Waiting for data...");
                }

                // Start reading thread anyway
                isRunning = true;
                readThread = new Thread(ReadLoop) { IsBackground = true };
                readThread.Start();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                ErrorOccurred?.Invoke(this, new Exception($"Port {comPort} is in use by another program"));
                StatusChanged?.Invoke(this, "Port is already in use");
                return false;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                StatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
                if (serialPort?.IsOpen == true)
                {
                    try { serialPort.Close(); } catch { }
                }
                return false;
            }
        }

        /// <summary>
        /// Main read loop for serial data - simplified approach
        /// </summary>
        private void ReadLoop()
        {
            byte[] buffer = new byte[256];
            int sampleCount = 0;
            double lastTimestamp = 0;

            while (isRunning && serialPort?.IsOpen == true)
            {
                try
                {
                    if (serialPort.BytesToRead > 0)
                    {
                        int bytesRead = serialPort.Read(buffer, 0, Math.Min(serialPort.BytesToRead, buffer.Length));

                        // Simple approach: process bytes as potential 10-bit values
                        for (int i = 0; i < bytesRead - 2; i++)
                        {
                            // Try to extract 10-bit values from different byte positions
                            // This handles various packing formats

                            // Method 1: Two bytes, LSB first
                            int value1 = (buffer[i] | (buffer[i + 1] << 8)) & 0x3FF;

                            // Method 2: Two bytes, MSB first  
                            int value2 = ((buffer[i] << 2) | (buffer[i + 1] >> 6)) & 0x3FF;

                            // Method 3: Packed format
                            int value3 = ((buffer[i + 1] & 0xFF) | ((buffer[i + 2] & 0x03) << 8));

                            // Use the value that's in the valid ADC range (0-1023)
                            int analogValue = value1;
                            if (value1 > 1023) analogValue = value2;
                            if (value2 > 1023) analogValue = value3;

                            // Skip if still invalid
                            if (analogValue > 1023) continue;

                            // Convert to ECG value using BITalino formula
                            double ecgMillivolts = ((analogValue / 1024.0 - 0.5) * 3.3) / 1.1;

                            // Basic filtering - ignore extreme values
                            if (Math.Abs(ecgMillivolts) < 5.0)
                            {
                                rawBuffer.Add(ecgMillivolts);

                                // Process every 10 samples to reduce noise
                                if (rawBuffer.Count >= 10)
                                {
                                    double avgValue = rawBuffer.Average();
                                    rawBuffer.Clear();

                                    // Add to ECG buffer
                                    ecgBuffer.Enqueue(avgValue);
                                    if (ecgBuffer.Count > 100) ecgBuffer.Dequeue();

                                    // Create timestamp
                                    double timestamp = DateTime.Now.Subtract(DateTime.Today).TotalSeconds;

                                    // Only send data points at reasonable intervals
                                    if (timestamp - lastTimestamp > 0.01) // 100Hz max
                                    {
                                        var ecgPoints = new List<DataPoint>
                                        {
                                            new DataPoint { Time = timestamp, Value = avgValue }
                                        };

                                        // Calculate heart rate periodically
                                        if (++sampleCount % 50 == 0)
                                        {
                                            HeartRate = CalculateHeartRate();
                                        }

                                        // Raise event
                                        DataReceived?.Invoke(this, new BITalinoDataEventArgs
                                        {
                                            ECGPoints = ecgPoints,
                                            EDAPoints = new List<DataPoint>(),
                                            HeartRate = HeartRate,
                                            StressLevel = StressLevel
                                        });

                                        lastTimestamp = timestamp;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
                catch (TimeoutException)
                {
                    Thread.Sleep(10);
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        System.Diagnostics.Debug.WriteLine($"Read error: {ex.Message}");
                    }
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Improved heart rate calculation
        /// </summary>
        private double CalculateHeartRate()
        {
            if (ecgBuffer.Count < 50) return 70;

            var ecgArray = ecgBuffer.ToArray();

            // Find mean and standard deviation
            double mean = ecgArray.Average();
            double std = Math.Sqrt(ecgArray.Select(x => Math.Pow(x - mean, 2)).Average());

            // Dynamic threshold based on signal
            double threshold = mean + std * 1.5;

            // Count peaks
            int peakCount = 0;
            bool inPeak = false;
            double lastPeakTime = 0;
            List<double> peakIntervals = new List<double>();

            for (int i = 1; i < ecgArray.Length - 1; i++)
            {
                // Look for local maxima above threshold
                if (!inPeak && ecgArray[i] > threshold &&
                    ecgArray[i] > ecgArray[i - 1] &&
                    ecgArray[i] > ecgArray[i + 1])
                {
                    peakCount++;
                    inPeak = true;

                    // Calculate interval
                    if (lastPeakTime > 0)
                    {
                        double interval = (i - lastPeakTime) / 100.0; // Convert to seconds
                        if (interval > 0.3 && interval < 2.0) // Reasonable heart rate range
                        {
                            peakIntervals.Add(interval);
                        }
                    }
                    lastPeakTime = i;
                }
                else if (inPeak && ecgArray[i] < mean)
                {
                    inPeak = false;
                }
            }

            // Calculate heart rate from intervals
            if (peakIntervals.Count > 0)
            {
                double avgInterval = peakIntervals.Average();
                double hr = 60.0 / avgInterval;
                return Math.Max(40, Math.Min(200, hr));
            }

            // Fallback calculation
            double duration = ecgBuffer.Count / 100.0; // seconds
            double fallbackHr = (peakCount / duration) * 60.0;
            return Math.Max(40, Math.Min(200, fallbackHr));
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
                    // Send stop command
                    try
                    {
                        byte[] stopCmd = { 0x00 };
                        serialPort.Write(stopCmd, 0, stopCmd.Length);
                        Thread.Sleep(100);
                    }
                    catch { }

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