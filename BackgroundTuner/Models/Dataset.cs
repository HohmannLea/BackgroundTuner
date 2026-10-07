using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackgroundTuner.Models
{
    public class Dataset
    {
        public List<Line> Lines { get; } = new();
        public string instrumentNumber { get; set; }
        public string masterInstrument { get; set; }
    }
}
