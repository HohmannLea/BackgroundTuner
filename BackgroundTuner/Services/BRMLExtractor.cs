using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BackgroundTuner.Models;
using System.ComponentModel.Design.Serialization;
using System.Xml.Linq;
using System.Runtime.CompilerServices;

using BrukerAXS.IO.SimpleBrmlReader;
using BrukerAXS.IO.SimpleBrmlReader.Xrf;

namespace BackgroundTuner.Services
{
    internal class BRMLExtractor
    {
        private readonly ArchiveHandler _archiveHandler;
        public Dictionary<string, double> LoadBRML(string filePath, string blankType, List<string> lineList)
        {
            BrmlFile brml = new BrmlReader().Read(filePath);
        }  

    }
}
