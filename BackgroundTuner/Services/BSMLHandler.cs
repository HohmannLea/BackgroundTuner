using BackgroundTuner.Models;
using System;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Markup;
using System.Xaml;
using System.Xml.Linq;

namespace BackgroundTuner.Services
{
    public class BSMLHandler : IDisposable
    {
        private XDocument? _solutionData;
        private XElement? _calibratedMaterial;
        private XDocument? _calibrationData;
        private Dictionary<string, string[]>? _lineCalibration;
        public string[]? _calibrationDataParts;

        private readonly ArchiveHandler _archiveHandler;
        public string XMLFileName { get; } = "Experiment0/ExtBaseContainer.xml";
        public Stream ExtBaseContainerXmlStream { get; set; }
        public BSMLHandler(ArchiveHandler archiveHandler)
        {
            _archiveHandler = archiveHandler;
        }

        public Dataset LoadBSML(string filePath)
        {
            // Get XML from BSML using ArchiveHandler

            _archiveHandler.OpenArchive(filePath);
            ExtBaseContainerXmlStream = _archiveHandler.GetFileStream(XMLFileName)
                ?? throw new FileNotFoundException($"File entry '{XMLFileName}' was not found in archive '{filePath}'.");
            _solutionData = XDocument.Load(ExtBaseContainerXmlStream);

            //Create Dataset

            Dataset currentDataset = new Dataset();

            //Parse XML

            XElement root = _solutionData.Root;

            XElement solutionNode = root.Descendants("SolutionNode").FirstOrDefault(e => e.Element("SolutionNodeType")?.Value == "Solution");
            XDocument solutionInfo = XDocument.Parse(solutionNode.Element("SerializationData").Value);
            string serialNumber = solutionInfo.Root.Element("InstrumentSerialNumber").Value;
            currentDataset.instrumentNumber = serialNumber;

            _calibratedMaterial = root.Descendants("SolutionNode").FirstOrDefault(e => e.Element("Name")?.Value == "CalibratedMaterial");
            XElement calibrationNode = _calibratedMaterial.Descendants("SolutionNode").FirstOrDefault(e => e.Element("Name")?.Value == "Manage Standards");
            _calibrationData = XDocument.Parse(calibrationNode.Element("SerializationData").Value);
      
            XElement calibRoot = _calibrationData.Root;
            var lineData = calibRoot.Element("Lines").Value;

            _calibrationDataParts = lineData.Split(
                new[]
                    {
                        "[XrfLine]","[Filter]","[Sources]","[ExcitationCondition]","[Crystals]","[Detectors]","[WDXSettings]","[LineMeasurement]","[PositionSet]","[LineCalibration]","[DriftCorrectionSetup]","[DriftCorrectionLink]","[DriftCorrectionData]"
                    },
                StringSplitOptions.RemoveEmptyEntries);

            // Create dictionaries and lists for relevant data tables

            Dictionary<string, string[]> measuredLines = _calibrationDataParts[0].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split(new[] { "\t" }, StringSplitOptions.RemoveEmptyEntries)).ToDictionary(cols => cols[0]);
            Dictionary<string, string[]> lineList = _calibrationDataParts[7].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split(new[] { "\t" }, StringSplitOptions.RemoveEmptyEntries)).ToDictionary(cols => cols[0]);
            _lineCalibration = _calibrationDataParts[9].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split("\t")).ToDictionary(cols => cols[7]);
            Dictionary<string, List<string[]>> driftData = _calibrationDataParts[12].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split(new[] { "\t" }, StringSplitOptions.RemoveEmptyEntries)).GroupBy(cols => cols[5]).ToDictionary(g => g.Key, g => g.ToList());

            string[] driftCorrectionLink = _calibrationDataParts[11].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string[] driftCorrectionSetup = _calibrationDataParts[10].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // Set up drift data link between line index and drift setup ID, excluding possible Blanks

            var validdriftpoints = new List<string>();
            foreach (var d in driftCorrectionSetup.Skip(1))
            {
                string[] driftInfo = d.Split(new[] { "\t" }, StringSplitOptions.RemoveEmptyEntries);
                if (driftInfo[3] == "Drift") { validdriftpoints.Add(driftInfo[0]); }
            }

            var driftIndices = new Dictionary<string, string>();

            foreach (var d in driftCorrectionLink.Skip(1))
            {
                string[] driftLinkInfo = d.Split(new[] { "\t" }, StringSplitOptions.RemoveEmptyEntries);
                if (validdriftpoints.Contains(driftLinkInfo[1])) { driftIndices.Add(driftLinkInfo[2], driftLinkInfo[1]); }
            }

            //Filter lines by theoretical backgrounds and fill out dataset

            foreach (var l in lineList.Keys)
            {   
                if (lineList[l][3] == "theoreticalBackground")
                {
                    Line line = new Line { LineIndex = l };
                    currentDataset.Lines.Add(line);
                }
            }

            currentDataset.masterInstrument = _lineCalibration["1"][4];

            // Extract line information from data and fill out the line information

            foreach (Line line in currentDataset.Lines)
            {
                // Indices to reference XML data
                line.MeasurementIndex = lineList[line.LineIndex][2];
                string driftindex = driftIndices[line.LineIndex];

                // Full line name and SensorBoost use
                line.Name = measuredLines[line.MeasurementIndex][5] + "/" + lineList[line.LineIndex][6];
                if (lineList[line.LineIndex][5] == "PhaFittedFirstOrder"){line.HasSensorBoost = true;}

                // Drift entries
                var latestDriftEntry = driftData[driftindex].Where(row => row[8] == currentDataset.instrumentNumber).OrderByDescending(row => DateTimeOffset.Parse(row[6])).FirstOrDefault();
                line.CurrentDriftIntensity = double.Parse(latestDriftEntry[1], CultureInfo.InvariantCulture);

                var masterDriftEntries = driftData[driftindex].Where(row => row[8] == currentDataset.masterInstrument).OrderByDescending(row => DateTimeOffset.Parse(row[6])).ToList();
                var calibrationMeasDate = DateTimeOffset.Parse(_lineCalibration[line.LineIndex][2]);
                var calibDriftEntry = masterDriftEntries.Where(row => DateTimeOffset.Parse(row[6]) <= calibrationMeasDate).FirstOrDefault();
                if (calibDriftEntry == null)
                {
                    calibDriftEntry = masterDriftEntries.Where(row => DateTimeOffset.Parse(row[6]) > calibrationMeasDate).LastOrDefault();
                }
                line.MasterDriftIntensity = double.Parse(calibDriftEntry[1], CultureInfo.InvariantCulture);

                // Calibration

                var xmlBytes = Convert.FromBase64String(_lineCalibration[line.LineIndex][13]);
                line.Calibration = XDocument.Parse(Encoding.UTF8.GetString(xmlBytes, 0, xmlBytes.Length));
            }

            return currentDataset;
        }

        public void EditBSML()
        {

        }
        
        public void Dispose()
        {
            ExtBaseContainerXmlStream.Dispose();
            _archiveHandler.CloseArchive();
        }
    }
}
