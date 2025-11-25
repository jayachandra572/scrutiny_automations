using System.Collections.Generic;

namespace BatchProcessor.Models
{
    public class JsonDiffResult
    {
        public string FileName { get; set; } = string.Empty;
        public bool FilesMatch { get; set; }
        public List<PropertyDifference> Differences { get; set; } = new List<PropertyDifference>();
        public string? ErrorMessage { get; set; }
        public bool IsMissingInLatest { get; set; }
        public bool IsMissingInReference { get; set; }
    }
}

