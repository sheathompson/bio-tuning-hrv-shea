using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;

namespace HRVMonitoringSystem
{
    public class LfHfRatioPoint
    {
        public double Time { get; set; }
        public double Ratio { get; set; }

        // For visualization
        public bool IsParasympathetic => Ratio < 1.0;
    }
}
