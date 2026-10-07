using System;
using System.ComponentModel.Design.Serialization;
using System.IO;
using System.Windows.Markup;
using System.Xaml;
using System.Xml.Linq;
using BackgroundTuner.Models;

namespace BackgroundTuner.Services
{
    public class BSMLHandler : IDisposable
    {
        private XDocument? _solutionData;
        private XElement? _calibratedMaterial;
        private XDocument? _calibrationData;
        private Dictionary<string, string[]>? _lineCalibration;
        public string[]? _calibrationDataParts;

        public Dataset _dataset = new Dataset();

        private readonly ArchiveHandler archiveHandler;
        public string FilePath { get; }
        public string XMLFileName { get; } = "Experiment0/ExtBaseContainer.xml";
        public Stream ExtBaseContainerXmlStream { get; }
        public BSMLHandler(string filePath)
        {
            FilePath = filePath;
            archiveHandler = new ArchiveHandler();
            archiveHandler.OpenArchive(FilePath);
            ExtBaseContainerXmlStream = archiveHandler.GetFileStream(XMLFileName)
                ?? throw new FileNotFoundException($"File entry '{XMLFileName}' was not found in archive '{FilePath}'.");
            _solutionData = XDocument.Load(ExtBaseContainerXmlStream);

            XElement root = _solutionData.Root;

            XElement solutionNode = root.Descendants("SolutionNode").FirstOrDefault(e => e.Element("SolutionNodeType")?.Value == "Solution");
            XDocument solutionInfo = XDocument.Parse(solutionNode.Element("SerializationData").Value);
            string serialNumber = solutionInfo.Root.Element("InstrumentSerialNumber").Value;
            _dataset.instrumentNumber = serialNumber;

            _calibratedMaterial = root.Descendants("SolutionNode").FirstOrDefault(e => e.Element("Name")?.Value == "CalibratedMaterial");
            XElement calibrationNode = _calibratedMaterial.Descendants("SolutionNode").FirstOrDefault(e => e.Element("Name")?.Value == "Manage Standards");
            _calibrationData = XDocument.Parse(calibrationNode.Element("SerializationData").Value);
        }
        public void CreateLines()
        {
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
            _lineCalibration = _calibrationDataParts[9].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split(new[] { "\t" }, StringSplitOptions.RemoveEmptyEntries)).ToDictionary(cols => cols[7]);
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

            //Filter by theoretical backgrounds and fill out dataset

            foreach (var l in lineList.Keys)
            {   
                if (lineList[l][3] == "theoreticalBackground")
                {
                    Line line = new Line { LineIndex = l };
                    _dataset.Lines.Add(line);
                }
            }

            _dataset.masterInstrument = _lineCalibration["1"][4];

            // Extract line information from data and fill out the line instances

            foreach (Line line in _dataset.Lines)
            {
                // Indices to reference XML data
                line.MeasurementIndex = lineList[line.LineIndex][2];
                string driftindex = driftIndices[line.LineIndex];

                // Full line name and SensorBoost use
                line.Name = measuredLines[line.MeasurementIndex][4] + "/" + lineList[line.LineIndex][6];
                if (lineList[line.LineIndex][5] == "PhaFittedFirstOrder"){line.HasSensorBoost = true;}

                // Drift entries
                var latestDriftEntry = driftData[driftindex].Where(row => row[8] == _dataset.instrumentNumber).OrderByDescending(row => DateTimeOffset.Parse(row[6])).FirstOrDefault();
                line.CurrentDriftIntensity = double.Parse(latestDriftEntry[1]);

                var masterDriftEntries = driftData[driftindex].Where(row => row[8] == _dataset.masterInstrument).OrderByDescending(row => DateTimeOffset.Parse(row[6])).ToList();
                var calibrationMeasDate = DateTimeOffset.Parse(_lineCalibration[line.LineIndex][2]);
                var calibDriftEntry = masterDriftEntries.Where(row => DateTimeOffset.Parse(row[6]) <= calibrationMeasDate).FirstOrDefault();
                if (calibDriftEntry == null)
                {
                    calibDriftEntry = masterDriftEntries.Where(row => DateTimeOffset.Parse(row[6]) > calibrationMeasDate).LastOrDefault();
                }
                line.MasterDriftIntensity = double.Parse(calibDriftEntry[1]);
            }
        }
       
        public void Dispose()
        {
            ExtBaseContainerXmlStream.Dispose();
            archiveHandler.CloseArchive();
        }
    }
}
