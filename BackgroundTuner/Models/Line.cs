using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Text.RegularExpressions;
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
        public string? _calibrationRawXml { get; set; }
        public double? _theoBGFactorOld { get; set; } 
        public double? _correctionFactor { get; set; }
        public double? _correctedBGFactor { get; set; }

        public void ExtractTheoBGFactor()
        {
            XElement root = _calibration.Root;
            XElement theoBGData = root.Descendants("CalibrationData").FirstOrDefault(e => e.Element("Type")?.Value == "computedBkg");
            _theoBGFactorOld = double.Parse(theoBGData.Element("Factor").Value, CultureInfo.InvariantCulture);
        }

        public string UpdateTheoBGInCalibration()
        {
            if (!_correctedBGFactor.HasValue)
            {
                return _calibrationRawXml ?? _calibration?.ToString(SaveOptions.DisableFormatting) ?? string.Empty;
            }

            string newFactor = _correctedBGFactor.Value.ToString("G15", CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(_calibrationRawXml))
            {
                var calibrationDataPattern = new Regex(
                    @"(<CalibrationData\b[^>]*>.*?<Type>\s*computedBkg\s*</Type>.*?<Factor>)([^<]*)(</Factor>)",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant);

                if (calibrationDataPattern.IsMatch(_calibrationRawXml))
                {
                    _calibrationRawXml = calibrationDataPattern.Replace(_calibrationRawXml, $"$1{newFactor}$3", 1);
                }
            }

            if (_calibration != null)
            {
                XElement? root = _calibration.Root;
                XElement? theoBGData = root?.Descendants("CalibrationData").FirstOrDefault(e => e.Element("Type")?.Value == "computedBkg");
                if (theoBGData?.Element("Factor") != null)
                {
                    theoBGData.Element("Factor")!.Value = newFactor;
                }
            }

            return _calibrationRawXml ?? _calibration?.ToString(SaveOptions.DisableFormatting) ?? string.Empty;
        }
    }
}
