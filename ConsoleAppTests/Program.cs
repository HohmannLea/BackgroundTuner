// See https://aka.ms/new-console-template for more information
using BackgroundTuner.Models;
using BackgroundTuner.Services;

    internal class Test
{
    public static void Main(string[] args)
    {
        Test test = new Test();
        test.RunTest();
    }
    public void RunTest()
    {
        // Create an instance of BSMLEditor
        BSMLEditor editor = new BSMLEditor("C:\\Users\\Lea.Hohmann\\Documents\\GEO QUANT Traces\\BSML Parsing Tests\\GQT_Test.bsml");

        //Retrieve the calib data
        string[] linelist = editor.CreateLines();
        foreach (var lineMeasurement in linelist)
        {
            Console.Write(lineMeasurement.Replace("\r", "\\r").Replace("\n", "\\n\n").Replace("\t", "\\t"));
        }
    }
}

