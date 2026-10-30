using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace BackgroundTuner.Models
{
    public class Line
    {
        public required string _lineIndex { get; set; }

        public string? _measurementIndex { get; set; }
        public string? _name { get; set; }
        public bool _hasSensorBoost { get; set; } = false;
        public double? _masterDriftIntensity { get; set; }
        public double? _currentDriftIntensity { get; set; }
        public double? _masterBlankIntensity { get; set; }
        public double? _currentBlankIntensity { get; set; }
        public XDocument? _calibration { get; set; }
        public double? _theoBGFactorOld { get; set; } 
        public double? _correctionFactor { get; set; }
        public double? _correctedBGFactor { get; set; }

        public void ExtractTheoBGFactor()
        {
            XElement root = _calibration.Root;
            XElement theoBGData = root.Descendants("CalibrationData").FirstOrDefault(e => e.Element("Type")?.Value == "computedBkg");
            _theoBGFactorOld = double.Parse(theoBGData.Element("Factor").Value, CultureInfo.InvariantCulture);
        }

        public XDocument UpdateTheoBGInCalibration()
        {
            XElement root = _calibration.Root;
            XElement theoBGData = root.Descendants("CalibrationData").FirstOrDefault(e => e.Element("Type")?.Value == "computedBkg");
            if (_correctedBGFactor.HasValue)
            {
                theoBGData.Element("Factor").Value = _correctedBGFactor.Value.ToString(CultureInfo.InvariantCulture);
            }
            return _calibration;
        }
    }
}
