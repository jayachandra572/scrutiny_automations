namespace BatchProcessor.Models
{
    public class PropertyDifference
    {
        public string Path { get; set; } = string.Empty;  // JSON path like "plotData.plotAreaData.grossPlotArea"
        public DifferenceType Type { get; set; }  // Added, Removed, Modified, TypeChanged
        public object? ReferenceValue { get; set; }
        public object? LatestValue { get; set; }
        public string? IdKey { get; set; }  // The key name of the ID field (e.g., "AbuttingRoadID")
        public string? IdValue { get; set; }  // The value of the ID field (e.g., "3560231")
        public string? ReferenceType { get; set; }  // Type of reference value (e.g., "float", "int", "string")
        public string? LatestType { get; set; }  // Type of latest value (e.g., "float", "int", "string")
        public bool IsArrayItemAdded { get; set; }  // True if this is an item added to an array
        public bool IsArrayItemRemoved { get; set; }  // True if this is an item removed from an array
    }

    public enum DifferenceType
    {
        Added,      // Property exists in latest but not in reference
        Removed,    // Property exists in reference but not in latest
        Modified,   // Property exists in both but values differ
        TypeChanged // Property exists in both but types differ
    }
}

