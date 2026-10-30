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
            if (line._masterDriftIntensity.HasValue && line._currentDriftIntensity.HasValue && line._masterBlankIntensity.HasValue && line._currentBlankIntensity.HasValue)
            {
                double DCfactor = line._masterDriftIntensity.Value / line._currentDriftIntensity.Value; 
                double PredictedBlank = line._masterBlankIntensity.Value / DCfactor;
                line._correctionFactor = line._currentBlankIntensity.Value / PredictedBlank;
                line._correctedBGFactor = line._theoBGFactorOld.Value * line._correctionFactor.Value;

                return true;

            }

            else
            { return false; }
            
        }

            
    }
}
