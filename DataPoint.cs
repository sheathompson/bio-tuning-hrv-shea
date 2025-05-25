using System;
using System.Collections.Generic;

namespace HRVMonitoringSystem
{
    public class DataPoint
    {
        public double Time { get; set; }
        public double Value { get; set; }
        public DateTime DateTime { get; set; }
        public string Category { get; set; }

        public DataPoint()
        {
            DateTime = DateTime.Now;
        }
    }

    public class DataPacket
    {
        public List<DataPoint> EcgPoints { get; set; } = new List<DataPoint>();
        public List<DataPoint> EdaPoints { get; set; } = new List<DataPoint>();
        public List<DataPoint> HeartRatePoints { get; set; } = new List<DataPoint>();
        public List<DataPoint> FrequencySpectrum { get; set; } = new List<DataPoint>();
        public double LfHfRatio { get; set; } = 0;
    }

    // Event args class
    public class BITalinoDataEventArgs : EventArgs
    {
        public List<DataPoint> ECGPoints { get; set; }
        public List<DataPoint> EDAPoints { get; set; }
        public double HeartRate { get; set; }
        public double StressLevel { get; set; }
    }

    /// <summary>
    /// COM port information
    /// </summary>
    public class ComPortInfo
    {
        public string PortName { get; set; }
        public string FriendlyName { get; set; }
        public string FullDescription { get; set; }
        public bool IsBitalino { get; set; }

        public override string ToString()
        {
            return $"{PortName} - {FriendlyName}";
        }
    }
}