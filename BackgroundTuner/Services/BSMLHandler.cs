using BackgroundTuner.Models;
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace BackgroundTuner.Services
{
    public class BSMLHandler : IDisposable
    {
        private static readonly string[] _lineSectionHeaders =
        {
            "[XrfLine]",
            "[Filter]",
            "[Sources]",
            "[ExcitationCondition]",
            "[Crystals]",
            "[Detectors]",
            "[WDXSettings]",
            "[LineMeasurement]",
            "[PositionSet]",
            "[LineCalibration]",
            "[DriftCorrectionSetup]",
            "[DriftCorrectionLink]",
            "[DriftCorrectionData]"
        };

        private XDocument? _solutionData;
        private XElement? _calibratedMaterial;
        private XDocument? _calibrationData;
        private Dictionary<string, string[]>? _lineCalibration;
        public string[]? _calibrationDataParts;
        private string? _sourceBsmlPath;

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

            _sourceBsmlPath = filePath;
            _archiveHandler.OpenArchive(filePath);
            ExtBaseContainerXmlStream = _archiveHandler.GetFileStream(XMLFileName)
                ?? throw new FileNotFoundException($"File entry '{XMLFileName}' was not found in archive '{filePath}'.");
            _solutionData = XDocument.Load(ExtBaseContainerXmlStream);
            _archiveHandler.CloseArchive();

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
                _lineSectionHeaders,
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
                    Line line = new Line { _lineIndex = l };
                    currentDataset.Lines.Add(line);
                }
            }

            currentDataset.masterInstrument = _lineCalibration["1"][4];

            // Extract line information from data and fill out the line information

            foreach (Line line in currentDataset.Lines)
            {
                // Indices to reference XML data
                line._measurementIndex = lineList[line._lineIndex][2];
                string driftindex = driftIndices[line._lineIndex];

                // Full line name and SensorBoost use
                line._name = measuredLines[line._measurementIndex][5] + "/" + lineList[line._lineIndex][6];
                if (lineList[line._lineIndex][5] == "PhaFittedFirstOrder"){line._hasSensorBoost = true;}

                // Drift entries
                var latestDriftEntry = driftData[driftindex].Where(row => row[13] == "True").Where(row => row[8] == currentDataset.instrumentNumber).OrderByDescending(row => DateTimeOffset.Parse(row[6])).FirstOrDefault();
                line._currentDriftIntensity = double.Parse(latestDriftEntry[1], CultureInfo.InvariantCulture);

                var masterDriftEntries = driftData[driftindex].Where(row => row[13] == "True").Where(row => row[8] == currentDataset.masterInstrument).OrderByDescending(row => DateTimeOffset.Parse(row[6])).ToList();
                var calibrationMeasDate = DateTimeOffset.Parse(_lineCalibration[line._lineIndex][2]);
                var calibDriftEntry = masterDriftEntries.Where(row => DateTimeOffset.Parse(row[6]) <= calibrationMeasDate).FirstOrDefault();
                if (calibDriftEntry == null)
                {
                    calibDriftEntry = masterDriftEntries.Where(row => DateTimeOffset.Parse(row[6]) > calibrationMeasDate).LastOrDefault();
                }
                line._masterDriftIntensity = double.Parse(calibDriftEntry[1], CultureInfo.InvariantCulture);

                // Calibration

                var xmlBytes = Convert.FromBase64String(_lineCalibration[line._lineIndex][13]);
                line._calibration = XDocument.Parse(Encoding.UTF8.GetString(xmlBytes, 0, xmlBytes.Length));
                line.ExtractTheoBGFactor();
            }

            return currentDataset;
        }

        public void EditBSML(Dataset dataset, string outputFilePath)
        {
            // Validate that the necessary data is loaded and the output file path is provided
            if (_solutionData == null || _calibrationData == null || _calibratedMaterial == null || _lineCalibration == null || _calibrationDataParts == null || string.IsNullOrWhiteSpace(_sourceBsmlPath))
            {
                throw new InvalidOperationException("No BSML file is currently loaded.");
            }

            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw new ArgumentException("Output file path is required.", nameof(outputFilePath));
            }

            // Update the calibration data for each line in the dataset
            foreach (Line line in dataset.Lines)
            {
                line.UpdateTheoBGInCalibration();
                var xmlString = line._calibration.ToString(SaveOptions.DisableFormatting);
                var xmlBytes = Encoding.UTF8.GetBytes(xmlString);
                var base64String = Convert.ToBase64String(xmlBytes);
                _lineCalibration[line._lineIndex][13] = base64String;
            }

            //Update the relevant XML objects
            var lineCalibrationRows = _calibrationDataParts[9]
                .Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            for (int index = 1; index < lineCalibrationRows.Length; index++)
            {
                var columns = lineCalibrationRows[index].Split("\t");
                if (columns.Length > 13 && _lineCalibration.TryGetValue(columns[7], out string[]? updatedColumns))
                {
                    columns[13] = updatedColumns[13];
                    lineCalibrationRows[index] = string.Join("\t", columns);
                }
            }

            _calibrationDataParts[9] = string.Join("\n", lineCalibrationRows) + "\n";

            var rebuiltLines = new StringBuilder();
            for (int index = 0; index < _lineSectionHeaders.Length; index++)
            {
                rebuiltLines.Append(_lineSectionHeaders[index]);
                rebuiltLines.Append(_calibrationDataParts[index]);
            }

            XElement calibrationRoot = _calibrationData.Root;
            calibrationRoot.Element("Lines").Value = rebuiltLines.ToString();

            XElement calibrationNode = _calibratedMaterial
                .Descendants("SolutionNode")
                .First(e => e.Element("Name")?.Value == "Manage Standards");
            calibrationNode.Element("SerializationData").Value = _calibrationData.ToString(SaveOptions.DisableFormatting);

            // Save the updated XML back to the BSML file
            if (File.Exists(outputFilePath))
            {
                File.Delete(outputFilePath);
            }

            File.Copy(_sourceBsmlPath, outputFilePath, true);

            using ZipArchive outputArchive = ZipFile.Open(outputFilePath, ZipArchiveMode.Update);
            var existingEntry = outputArchive.GetEntry(XMLFileName)
                ?? throw new FileNotFoundException($"File entry '{XMLFileName}' was not found in archive '{outputFilePath}'.");
            existingEntry.Delete();

            var updatedEntry = outputArchive.CreateEntry(XMLFileName, CompressionLevel.Optimal);
            using var writer = new StreamWriter(updatedEntry.Open(), new UTF8Encoding(false));
            writer.Write(_solutionData.ToString(SaveOptions.DisableFormatting));
        }
        
        public void Dispose()
        {
            ExtBaseContainerXmlStream.Dispose();
            _archiveHandler.CloseArchive();
        }
    }
}
