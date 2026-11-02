// See https://aka.ms/new-console-template for more information
using BackgroundTuner.Models;
using BackgroundTuner.Services;
using BrukerAXS.IO.SimpleBrmlReader.Xrf;
using System.Printing.IndexedProperties;

internal class Test
{
    public static void Main(string[] args)
    {
        Test test = new Test();
        //test.RunBSMLTest();
        test.RunBRMLTest();
    }
    public void RunBSMLTest()
    {
        // Create an instance of ArchiveHandler and BSMLHandler
        ArchiveHandler archiveHandler = new ArchiveHandler();
        BSMLHandler bsmlHandler = new BSMLHandler(archiveHandler);

        //Retrieve the dataset
        Dataset dataset = bsmlHandler.LoadBSML("C:\\Users\\Lea.Hohmann\\Documents\\GEO QUANT Traces\\BSML Parsing Tests\\GQT_Test.bsml");

        foreach (Line l in dataset.Lines)
        {
            Console.WriteLine(l._name);
            Console.WriteLine(l._calibration.ToString());
        }
    }

    public void RunBRMLTest()
    {
        // Create an instance of BRMLExtractor
        BRMLExtractor brmlExtractor = new BRMLExtractor();
        //Retrieve ranges from the BRML file
        var linedict = new Dictionary<string, bool>()
        {
            {"Nd LA1/GEO-QUANT TRACES", true },
            { "Ge KA1/GEO-QUANT TRACES", false },
            { "Sn KA1/GEO-QUANT TRACES", false },
            { "Al KA1/GEO-QUANT TRACES", true },
            { "Si KA1/GEO-QUANT TRACES", false }
        };
        var intensities = brmlExtractor.LoadBRML("C:\\Users\\Lea.Hohmann\\source\\repos\\BackgroundTuner\\TestData\\GQT-Blank-GQT0-MasterBlank_260922.brml", linedict);
        foreach (string key in intensities.Keys)
        {
            double intensity = intensities[key];
            Console.WriteLine($"{key}: {intensity.ToString()}");
        }
       
    }
}

