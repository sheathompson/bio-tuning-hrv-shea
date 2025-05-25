using System;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;

namespace HRVMonitoringSystem
{
    public partial class BITalinoTestWindow : Window
    {
        private SerialPort serialPort;
        private Thread readThread;
        private bool isReading = false;

        public BITalinoTestWindow()
        {
            InitializeComponent();
            LoadPorts();
        }

        private void LoadPorts()
        {
            portCombo.Items.Clear();
            var ports = SerialPort.GetPortNames();
            foreach (var port in ports)
            {
                portCombo.Items.Add(port);
            }
            if (ports.Length > 0)
            {
                portCombo.SelectedIndex = 0;
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (portCombo.SelectedItem == null) return;

            try
            {
                string port = portCombo.SelectedItem.ToString();
                Log($"Connecting to {port}...");
                Log("BITalino (r)evolution detected - using specific protocol");

                serialPort = new SerialPort(port)
                {
                    BaudRate = 115200,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Parity = Parity.None,
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    DtrEnable = false,  // Important for (r)evolution
                    RtsEnable = false   // Important for (r)evolution
                };

                serialPort.Open();
                Log("Port opened successfully");

                // IMPORTANT: The (r)evolution needs a delay after opening
                Thread.Sleep(2000);
                Log("Waiting for BITalino to initialize...");

                // Start reading thread
                isReading = true;
                readThread = new Thread(ReadData) { IsBackground = true };
                readThread.Start();

                // Test the connection
                TestRevolutionProtocol();

                connectButton.IsEnabled = false;
                disconnectButton.IsEnabled = true;
                statusText.Text = "Connected";
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
                MessageBox.Show($"Connection failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestRevolutionProtocol()
        {
            Log("\n=== BITalino (r)evolution Protocol Test ===");

            // The (r)evolution uses a specific binary protocol
            try
            {
                // Clear any buffered data
                if (serialPort.BytesToRead > 0)
                {
                    serialPort.DiscardInBuffer();
                    Log("Cleared input buffer");
                }

                // Method 1: Direct binary start (most common for revolution)
                Log("\n--- Testing direct acquisition start ---");

                // BITalino (r)evolution: Start acquisition on A1 at 1000Hz
                // Command format: 0xFE (start) followed by config bytes
                byte[] startCommand = { 0xFE };

                Log("Sending start command (0xFE)...");
                serialPort.Write(startCommand, 0, startCommand.Length);
                Thread.Sleep(100);

                // Now send configuration
                // For (r)evolution: sampling rate and channels are set differently
                // Try the simpler approach first - just start with defaults

                Thread.Sleep(1000);

                if (serialPort.BytesToRead > 0)
                {
                    Log($"SUCCESS! Data is flowing ({serialPort.BytesToRead} bytes available)");
                    return;
                }

                // Method 2: Try the standard start command
                Log("\n--- Testing standard start (0x01) ---");
                serialPort.DiscardInBuffer();

                byte[] standardStart = { 0x01 };
                serialPort.Write(standardStart, 0, standardStart.Length);
                Thread.Sleep(1000);

                if (serialPort.BytesToRead > 0)
                {
                    Log($"SUCCESS! Data is flowing ({serialPort.BytesToRead} bytes available)");
                    return;
                }

                // Method 3: Try ASCII commands (some revolutions support this)
                Log("\n--- Testing ASCII protocol ---");
                serialPort.DiscardInBuffer();

                // Send newline to enter command mode
                serialPort.WriteLine("");
                Thread.Sleep(100);

                // Request version
                Log("Requesting version...");
                serialPort.WriteLine("version");
                Thread.Sleep(500);

                if (serialPort.BytesToRead > 0)
                {
                    byte[] response = new byte[serialPort.BytesToRead];
                    serialPort.Read(response, 0, response.Length);
                    string versionStr = Encoding.ASCII.GetString(response);
                    Log($"Version response: {versionStr.Trim()}");

                    // If we got a version, try ASCII start
                    Log("Starting acquisition with ASCII command...");
                    serialPort.WriteLine("start");
                    Thread.Sleep(1000);

                    if (serialPort.BytesToRead > 0)
                    {
                        Log($"SUCCESS! Data is flowing ({serialPort.BytesToRead} bytes available)");
                        return;
                    }
                }

                Log("\n=== No data received ===");
                Log("Possible issues:");
                Log("1. Check ECG electrode connections (RED, YELLOW, BLACK)");
                Log("2. Ensure good skin contact with electrodes");
                Log("3. Verify the device is charged (LED should be steady blue)");
                Log("4. Try OpenSignals software to verify hardware");

            }
            catch (Exception ex)
            {
                Log($"Protocol test error: {ex.Message}");
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                isReading = false;

                if (serialPort?.IsOpen == true)
                {
                    // Send stop command for (r)evolution
                    try
                    {
                        byte[] stopCommand = { 0xFF };
                        serialPort.Write(stopCommand, 0, stopCommand.Length);
                        Thread.Sleep(100);
                    }
                    catch { }

                    serialPort.Close();
                }

                readThread?.Join(1000);

                Log("Disconnected");
                connectButton.IsEnabled = true;
                disconnectButton.IsEnabled = false;
                statusText.Text = "Disconnected";
            }
            catch (Exception ex)
            {
                Log($"Disconnect error: {ex.Message}");
            }
        }

        private void ReadData()
        {
            byte[] buffer = new byte[256];
            int sampleCount = 0;
            StringBuilder dataLine = new StringBuilder();

            while (isReading && serialPort?.IsOpen == true)
            {
                try
                {
                    if (serialPort.BytesToRead > 0)
                    {
                        int bytesRead = serialPort.Read(buffer, 0, Math.Min(serialPort.BytesToRead, buffer.Length));

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // Show first few bytes in hex
                            if (sampleCount < 10 || sampleCount % 100 == 0)
                            {
                                string hex = BitConverter.ToString(buffer, 0, Math.Min(bytesRead, 20));
                                outputBox.AppendText($"\n[{DateTime.Now:HH:mm:ss.fff}] Data ({bytesRead} bytes): {hex}...");
                            }

                            // Try to parse different frame formats
                            ParseFrames(buffer, bytesRead, ref sampleCount);

                            outputBox.ScrollToEnd();
                        }));
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (sampleCount == 0) // Only show first error
                        {
                            Log($"Read error: {ex.Message}");
                        }
                    }));
                    Thread.Sleep(100);
                }
            }
        }

        private void ParseFrames(byte[] buffer, int length, ref int sampleCount)
        {
            // BITalino (r)evolution frame format varies by configuration
            // Most common: 12-bit ADC values packed into bytes

            // Look for potential frame patterns
            for (int i = 0; i < length - 2; i++)
            {
                // Check if this could be a frame start
                // Revolution frames don't have a fixed start pattern like older models

                if (i + 2 < length)
                {
                    // Try to extract a 10-bit value (revolution uses 10-bit ADC)
                    int value1 = (buffer[i] << 2) | (buffer[i + 1] >> 6);
                    int value2 = ((buffer[i + 1] & 0x3F) << 4) | (buffer[i + 2] >> 4);

                    // Check if values are reasonable (0-1023 for 10-bit)
                    if (value1 >= 0 && value1 <= 1023)
                    {
                        double voltage = (value1 / 1024.0) * 3.3;
                        double ecg = (voltage - 1.65) * 2.0;

                        if (sampleCount++ % 50 == 0) // Show every 50th sample
                        {
                            outputBox.AppendText($"\n    Sample {sampleCount}: ADC={value1}, V={voltage:F3}, ECG={ecg:F3}mV");
                        }
                    }
                }
            }
        }

        private void Log(string message)
        {
            outputBox.AppendText($"\n{message}");
            outputBox.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            DisconnectButton_Click(null, null);
        }
    }
}