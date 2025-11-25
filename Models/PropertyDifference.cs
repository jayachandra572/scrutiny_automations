namespace BatchProcessor.Models
{
    public class PropertyDifference
    {
        public string Path { get; set; } = string.Empty;  // JSON path like "plotData.plotAreaData.grossPlotArea"
        public DifferenceType Type { get; set; }  // Added, Removed, Modified
        public object? ReferenceValue { get; set; }
        public object? LatestValue { get; set; }
    }

    public enum DifferenceType
    {
        Added,      // Property exists in latest but not in reference
        Removed,    // Property exists in reference but not in latest
        Modified    // Property exists in both but values differ
    }
}

