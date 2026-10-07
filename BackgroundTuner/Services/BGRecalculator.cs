using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BackgroundTuner.Models;

namespace BackgroundTuner.Services
{
    public class BGRecalculator
    {
        public bool Calculate(Line line) {
            if (line.MasterDriftIntensity.HasValue && line.CurrentDriftIntensity.HasValue && line.MasterBlankIntensity.HasValue && line.CurrentBlankIntensity.HasValue)
            {
                double DCfactor = line.MasterDriftIntensity.Value / line.CurrentDriftIntensity.Value; 
                double PredictedBlank = line.MasterBlankIntensity.Value / DCfactor;
                line.CorrectionFactor = line.CurrentBlankIntensity.Value / PredictedBlank;
                line.CorrectedBGFactor = line.TheoBGFactorOld.Value * line.CorrectionFactor.Value;

                return true;

            }

            else
            { return false; }
            
        }

            
    }
}
