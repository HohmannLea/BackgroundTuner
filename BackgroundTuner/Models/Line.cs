using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackgroundTuner.Models
{
    public class Line
    {
        public required string LineIndex { get; set; }

        public string? MeasurementIndex { get; set; }
        public string? Name { get; set; }
        public bool HasSensorBoost { get; set; } = false;
        public double? MasterDriftIntensity { get; set; }
        public double? CurrentDriftIntensity { get; set; }
        public double? MasterBlankIntensity { get; set; }
        public double? CurrentBlankIntensity { get; set; }
        public double? TheoBGFactorOld { get; set; } 
        public double? CorrectionFactor { get; set; }
        public double? CorrectedBGFactor { get; set; }

    }
}
