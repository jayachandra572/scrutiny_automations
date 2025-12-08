using System;
using System.Collections.Generic;

namespace BatchProcessor.JsonDiff.Models
{
    public class DiffReport
    {
        public string ReferenceFolder { get; set; } = string.Empty;
        public string LatestFolder { get; set; } = string.Empty;
        public DateTime ComparisonDate { get; set; } = DateTime.Now;
        public int TotalFiles { get; set; }
        public int MatchingFiles { get; set; }
        public int DifferentFiles { get; set; }
        public int MissingInLatest { get; set; }
        public int MissingInReference { get; set; }
        public List<JsonDiffResult> FileResults { get; set; } = new List<JsonDiffResult>();
    }
}

