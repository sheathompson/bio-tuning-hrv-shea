import bitalino
import socket
import json
import threading
import time
import numpy as np
from collections import deque

class BITalinoBridgeServer:
    """Python server that connects to BITalino and streams data to C# application"""
    
    def __init__(self, mac_address="98:D3:51:FE:86:88", port=5555):
        self.mac_address = mac_address
        self.port = port
        self.device = None
        self.server_socket = None
        self.client_socket = None
        self.running = False
        self.sampling_rate = 100  # Hz
        self.channels = [0, 1]  # A1 (ECG), A2 (EDA)
        
        # Data buffers for processing
        self.ecg_buffer = deque(maxlen=100)
        self.eda_buffer = deque(maxlen=100)
        
    def start_server(self):
        """Start the TCP server to communicate with C# application"""
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server_socket.bind(('localhost', self.port))
        self.server_socket.listen(1)
        print(f"Bridge server listening on port {self.port}")
        
    def connect_bitalino(self):
        """Connect to BITalino device"""
        try:
            print(f"Connecting to BITalino at {self.mac_address}...")
            self.device = bitalino.BITalino(self.mac_address)
            print("Connected to BITalino!")
            
            # Start acquisition
            self.device.start(self.sampling_rate, self.channels)
            print(f"Started acquisition at {self.sampling_rate}Hz on channels {self.channels}")
            return True
            
        except Exception as e:
            print(f"Error connecting to BITalino: {e}")
            return False
    
    def process_data(self, raw_data):
        """Process raw BITalino data into meaningful values"""
        processed = []
        
        for sample in raw_data:
            # Convert ADC values to voltage (0-3.3V range for 10-bit ADC)
            ecg_voltage = (sample[5] / 1024.0) * 3.3  # Channel A1
            eda_voltage = (sample[6] / 1024.0) * 3.3  # Channel A2
            
            # Apply sensor-specific conversions
            # ECG: Center around 0, amplify signal
            ecg_value = (ecg_voltage - 1.65) * 2.0
            
            # EDA: Convert to microsiemens (µS)
            # Assuming 0-10µS range mapped to 0-3.3V
            eda_value = (eda_voltage / 3.3) * 10.0
            
            processed.append({
                'timestamp': time.time(),
                'ecg': ecg_value,
                'eda': eda_value,
                'seq': sample[0]  # Sequence number for checking dropped packets
            })
            
            # Add to buffers for additional processing
            self.ecg_buffer.append(ecg_value)
            self.eda_buffer.append(eda_value)
            
        return processed
    
    def calculate_heart_rate(self):
        """Simple heart rate calculation from ECG buffer"""
        if len(self.ecg_buffer) < 50:
            return 70  # Default HR
            
        # Simple peak detection (you can improve this)
        ecg_array = np.array(self.ecg_buffer)
        mean_val = np.mean(ecg_array)
        threshold = mean_val + np.std(ecg_array) * 0.6
        
        peaks = []
        for i in range(1, len(ecg_array) - 1):
            if ecg_array[i] > threshold and ecg_array[i] > ecg_array[i-1] and ecg_array[i] > ecg_array[i+1]:
                peaks.append(i)
        
        if len(peaks) >= 2:
            # Calculate average RR interval
            intervals = np.diff(peaks) * (1.0 / self.sampling_rate)
            avg_interval = np.mean(intervals)
            heart_rate = 60.0 / avg_interval if avg_interval > 0 else 70
            return np.clip(heart_rate, 40, 200)  # Reasonable HR range
        
        return 70
    
    def run(self):
        """Main server loop"""
        self.start_server()
        
        if not self.connect_bitalino():
            return
        
        print("Waiting for C# client connection...")
        self.client_socket, addr = self.server_socket.accept()
        print(f"Client connected from {addr}")
        
        self.running = True
        
        try:
            while self.running:
                # Read data from BITalino (10 samples at a time)
                raw_data = self.device.read(10)
                
                # Process the data
                processed_data = self.process_data(raw_data)
                
                # Calculate additional metrics
                heart_rate = self.calculate_heart_rate()
                stress_level = np.mean(list(self.eda_buffer)) / 10.0 if self.eda_buffer else 0.3
                
                # Prepare data packet for C#
                packet = {
                    'samples': processed_data,
                    'heart_rate': heart_rate,
                    'stress_level': stress_level
                }
                
                # Send to C# client
                json_data = json.dumps(packet) + '\n'
                self.client_socket.send(json_data.encode())
                
        except Exception as e:
            print(f"Error in main loop: {e}")
            
        finally:
            self.cleanup()
    
    def cleanup(self):
        """Clean up resources"""
        self.running = False
        
        if self.device:
            try:
                self.device.stop()
                self.device.close()
            except:
                pass
                
        if self.client_socket:
            self.client_socket.close()
            
        if self.server_socket:
            self.server_socket.close()
            
        print("Bridge server shut down")

if __name__ == "__main__":
    # Configure your BITalino MAC address here
    MAC_ADDRESS = "98:D3:51:FE:86:88"
    
    server = BITalinoBridgeServer(MAC_ADDRESS)
    
    try:
        server.run()
    except KeyboardInterrupt:
        print("\nShutting down...")
        server.cleanup()
