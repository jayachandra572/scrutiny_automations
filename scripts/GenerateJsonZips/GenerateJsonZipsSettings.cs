namespace BatchProcessor.Scripts.GenerateJsonZips
{
    public class GenerateJsonZipsSettings
    {
        public string? DrawingsFolder { get; set; }
        public string? AppParamsCsvFile { get; set; }
        public string? WorkloadMapCsvFile { get; set; }
        public string? OutputFolder { get; set; }
        public string? AutoCADPath { get; set; }
        // Used for DrawingFileValidations + PreScrutiny passes
        public string? CommonUtilsDll { get; set; }
        // Used for ScrutinyReports pass
        public string? CrxDll { get; set; }
        public int MaxParallelProcesses { get; set; } = 4;
        public bool VerboseLogging { get; set; }
    }
}
