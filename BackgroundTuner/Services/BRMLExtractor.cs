using BackgroundTuner.Models;
using BrukerAXS.IO.SimpleBrmlReader;
using BrukerAXS.IO.SimpleBrmlReader.Xrf;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;

namespace BackgroundTuner.Services
{
    public class BRMLExtractor
    {
        public Dictionary<string, double> LoadBRML(string filePath, Dictionary<string, bool> lineDict)
        {
            Dictionary<string, double> intensities = new Dictionary<string, double>();
            BrmlFile brml = new BrmlReader().Read(filePath);
            var ranges = brml.Ranges.Where(r => lineDict.Keys.Contains(r.Name)).Where(r => r.IsBackground == false).ToList();

            foreach (string key in lineDict.Keys)
            {
                BrmlRange range = ranges.FirstOrDefault(r => r.Name == key);
                var info = range.Infos;
                string detectorname = info.Where(i => i.ToString().Contains("Detector Name")).First().ToString();
                var livetime = range.Recordings[0].Value;

                if (detectorname.Contains("HighSenseXE"))
                {
                    intensities[key] = range.IntegratedCounts[0].Counts / livetime;
                }

                else if (lineDict[key] == true)
                {
                    intensities[key] = range.Recordings[2].Value / livetime;
                }

                else
                {
                    intensities[key] = range.RecommendedIntegratedCounts.Counts / 16 / livetime;
                }

            }

            return intensities;
        }

    }
}
