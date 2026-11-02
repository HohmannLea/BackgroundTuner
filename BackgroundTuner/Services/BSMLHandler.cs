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
        private string? _extBaseContainerXmlRaw;
        private string? _manageStandardsSerializationDataRaw;
        private string? _linesDataRaw;

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
            using (var reader = new StreamReader(ExtBaseContainerXmlStream, Encoding.UTF8, true, 1024, leaveOpen: true))
            {
                _extBaseContainerXmlRaw = reader.ReadToEnd();
            }
            _solutionData = XDocument.Parse(_extBaseContainerXmlRaw);
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
            _manageStandardsSerializationDataRaw = calibrationNode.Element("SerializationData").Value;
            _calibrationData = XDocument.Parse(_manageStandardsSerializationDataRaw);
      
            XElement calibRoot = _calibrationData.Root;
            var lineData = calibRoot.Element("Lines").Value;
            _linesDataRaw = lineData;

            _calibrationDataParts = lineData.Split(
                _lineSectionHeaders,
                StringSplitOptions.RemoveEmptyEntries);

            // Create dictionaries and lists for relevant data tables

            Dictionary<string, string[]> measuredLines = _calibrationDataParts[0].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split(new[] { "\t" }, StringSplitOptions.None)).ToDictionary(cols => cols[0]);
            Dictionary<string, string[]> lineList = _calibrationDataParts[7].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split(new[] { "\t" }, StringSplitOptions.None)).ToDictionary(cols => cols[0]);
            _lineCalibration = _calibrationDataParts[9].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split("\t")).ToDictionary(cols => cols[7]);
            Dictionary<string, List<string[]>> driftData = _calibrationDataParts[12].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(row => row.Split(new[] { "\t" }, StringSplitOptions.None)).GroupBy(cols => cols[5]).ToDictionary(g => g.Key, g => g.ToList());

            string[] driftCorrectionLink = _calibrationDataParts[11].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string[] driftCorrectionSetup = _calibrationDataParts[10].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // Set up drift data link between line index and drift setup ID, excluding possible Blanks

            var validdriftpoints = new List<string>();
            foreach (var d in driftCorrectionSetup.Skip(1))
            {
                string[] driftInfo = d.Split(new[] { "\t" }, StringSplitOptions.None);
                if (driftInfo[3] == "Drift") { validdriftpoints.Add(driftInfo[0]); }
            }

            var driftIndices = new Dictionary<string, string>();

            foreach (var d in driftCorrectionLink.Skip(1))
            {
                string[] driftLinkInfo = d.Split(new[] { "\t" }, StringSplitOptions.None);
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
                var calibrationXml = Encoding.UTF8.GetString(xmlBytes, 0, xmlBytes.Length);
                line._calibrationRawXml = calibrationXml;
                line._calibration = XDocument.Parse(calibrationXml);
                line.ExtractTheoBGFactor();
            }

            return currentDataset;
        }

        public void EditBSML(Dataset dataset, string outputFilePath)
        {
            // Validate that the necessary data is loaded and the output file path is provided
            if (_solutionData == null || _lineCalibration == null || string.IsNullOrWhiteSpace(_sourceBsmlPath) || string.IsNullOrEmpty(_extBaseContainerXmlRaw) || string.IsNullOrEmpty(_manageStandardsSerializationDataRaw) || _linesDataRaw == null)
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
                var xmlString = line.UpdateTheoBGInCalibration();
                if (string.IsNullOrEmpty(xmlString))
                {
                    throw new InvalidOperationException($"Calibration XML is missing for line index '{line._lineIndex}'.");
                }
                var xmlBytes = Encoding.UTF8.GetBytes(xmlString);
                var base64String = Convert.ToBase64String(xmlBytes);
                _lineCalibration[line._lineIndex][13] = base64String;
            }

            int lineCalibrationHeaderStart = _linesDataRaw.IndexOf("[LineCalibration]", StringComparison.Ordinal);
            int nextHeaderStart = _linesDataRaw.IndexOf("[DriftCorrectionSetup]", lineCalibrationHeaderStart, StringComparison.Ordinal);
            if (lineCalibrationHeaderStart < 0 || nextHeaderStart < 0)
            {
                throw new InvalidOperationException("Could not locate LineCalibration section in BSML data.");
            }

            int lineCalibrationDataStart = lineCalibrationHeaderStart + "[LineCalibration]".Length;
            string lineCalibrationSection = _linesDataRaw.Substring(lineCalibrationDataStart, nextHeaderStart - lineCalibrationDataStart);

            var lineCalibrationRows = lineCalibrationSection.Split(new[] { "\n" }, StringSplitOptions.None).ToArray();

            for (int index = 1; index < lineCalibrationRows.Length; index++)
            {
                string row = lineCalibrationRows[index];
                if (string.IsNullOrWhiteSpace(row))
                {
                    continue;
                }

                string rowEnding = string.Empty;
                string rowContent = row;
                if (rowContent.EndsWith("\r", StringComparison.Ordinal))
                {
                    rowContent = rowContent[..^1];
                    rowEnding = "\r";
                }

                var columns = rowContent.Split('\t');
                if (columns.Length > 13 && _lineCalibration.TryGetValue(columns[7], out string[]? updatedColumns))
                {
                    columns[13] = updatedColumns[13];
                    lineCalibrationRows[index] = string.Join("\t", columns) + rowEnding;
                }
            }

            string updatedLineCalibrationSection = string.Join("\n", lineCalibrationRows);
            string updatedLinesData = _linesDataRaw.Substring(0, lineCalibrationDataStart)
                + updatedLineCalibrationSection
                + _linesDataRaw.Substring(nextHeaderStart);

            string updatedManageStandardsSerializationData = ReplaceOnceOrThrow(
                _manageStandardsSerializationDataRaw,
                _linesDataRaw,
                updatedLinesData,
                "Manage Standards Lines payload");

            string updatedExtBaseContainerXml = ReplaceManageStandardsSerializationDataOrThrow(
                _extBaseContainerXmlRaw,
                updatedManageStandardsSerializationData);

            _linesDataRaw = updatedLinesData;
            _manageStandardsSerializationDataRaw = updatedManageStandardsSerializationData;
            _extBaseContainerXmlRaw = updatedExtBaseContainerXml;

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
            writer.Write(_extBaseContainerXmlRaw);
        }

        private static string ReplaceOnceOrThrow(string source, string oldValue, string newValue, string context)
        {
            int index = source.IndexOf(oldValue, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException($"Could not find expected {context} for update.");
            }

            return source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
        }

        private static string ReplaceManageStandardsSerializationDataOrThrow(string sourceXml, string newSerializationDataRaw)
        {
            const string manageStandardsNameTag = "<Name>Manage Standards</Name>";
            const string solutionNodeStartTag = "<SolutionNode";
            const string solutionNodeEndTag = "</SolutionNode>";
            const string serializationDataStartTag = "<SerializationData>";
            const string serializationDataEndTag = "</SerializationData>";

            int nameIndex = sourceXml.IndexOf(manageStandardsNameTag, StringComparison.Ordinal);
            if (nameIndex < 0)
            {
                throw new InvalidOperationException("Could not find Manage Standards node in ExtBaseContainer XML.");
            }

            int nodeStart = sourceXml.LastIndexOf(solutionNodeStartTag, nameIndex, StringComparison.Ordinal);
            if (nodeStart < 0)
            {
                throw new InvalidOperationException("Could not locate start of Manage Standards SolutionNode.");
            }

            int nodeEnd = FindMatchingSolutionNodeEnd(sourceXml, nodeStart);
            string nodeXml = sourceXml.Substring(nodeStart, nodeEnd - nodeStart);

            int serializationStart = nodeXml.IndexOf(serializationDataStartTag, StringComparison.Ordinal);
            if (serializationStart < 0)
            {
                throw new InvalidOperationException("Could not find SerializationData start tag in Manage Standards node.");
            }

            int serializationValueStart = serializationStart + serializationDataStartTag.Length;
            int serializationEnd = nodeXml.IndexOf(serializationDataEndTag, serializationValueStart, StringComparison.Ordinal);
            if (serializationEnd < 0)
            {
                throw new InvalidOperationException("Could not find SerializationData end tag in Manage Standards node.");
            }

            string escapedValue = EscapeXmlForTextNode(newSerializationDataRaw);

            string updatedNodeXml = nodeXml.Substring(0, serializationValueStart)
                + escapedValue
                + nodeXml.Substring(serializationEnd);

            return sourceXml.Substring(0, nodeStart) + updatedNodeXml + sourceXml.Substring(nodeEnd);
        }

        private static int FindMatchingSolutionNodeEnd(string xml, int startIndex)
        {
            const string solutionNodeStartTag = "<SolutionNode";
            const string solutionNodeEndTag = "</SolutionNode>";

            int depth = 0;
            int index = startIndex;

            while (index < xml.Length)
            {
                int nextStart = xml.IndexOf(solutionNodeStartTag, index, StringComparison.Ordinal);
                int nextEnd = xml.IndexOf(solutionNodeEndTag, index, StringComparison.Ordinal);

                if (nextEnd < 0)
                {
                    break;
                }

                if (nextStart >= 0 && nextStart < nextEnd)
                {
                    depth++;
                    index = nextStart + solutionNodeStartTag.Length;
                    continue;
                }

                depth--;
                index = nextEnd + solutionNodeEndTag.Length;

                if (depth == 0)
                {
                    return index;
                }
            }

            throw new InvalidOperationException("Could not locate end of Manage Standards SolutionNode.");
        }

        private static string EscapeXmlForTextNode(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
        
        public void Dispose()
        {
            ExtBaseContainerXmlStream.Dispose();
            _archiveHandler.CloseArchive();
        }
    }
}
