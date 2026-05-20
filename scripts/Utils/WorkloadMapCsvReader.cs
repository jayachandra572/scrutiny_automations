using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BatchProcessor.Utils
{
    /// <summary>
    /// Reads a two-column CSV that maps drawing filenames to WorkloadIDs.
    /// Expected columns: "Marking File Link" (or fallback) and "WorkloadID".
    /// Filename matching delegates to CsvParser for consistency with CsvParameterMapper.
    /// </summary>
    public class WorkloadMapCsvReader
    {
        private readonly string _csvFilePath;

        // key: filename variant → workload_id
        private Dictionary<string, string> _workloadMap;

        public WorkloadMapCsvReader(string csvFilePath)
        {
            _csvFilePath = csvFilePath;
            _workloadMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Load and parse the workload map CSV.
        /// Returns true on success.
        /// </summary>
        public bool LoadCsv()
        {
            try
            {
                if (!File.Exists(_csvFilePath))
                {
                    Console.WriteLine($"❌ Workload map CSV not found: {_csvFilePath}");
                    return false;
                }

                string fileContent = File.ReadAllText(_csvFilePath);
                var rows = CsvParser.ParseCsvRows(fileContent);

                Console.WriteLine($"📊 Workload map: parsed {rows.Count} total rows (including header)");

                if (rows.Count < 2)
                {
                    Console.WriteLine("❌ Workload map CSV must have at least a header row and one data row");
                    return false;
                }

                var headers = rows[0];
                int filenameColumnIndex = CsvParser.FindFilenameColumnIndex(headers);

                // Find WorkloadID column
                int workloadColumnIndex = headers.FindIndex(h =>
                    h.Equals("WorkloadID", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("Workload ID", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("WorkLoad_Id", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("work_load_id", StringComparison.OrdinalIgnoreCase));

                if (workloadColumnIndex == -1)
                {
                    Console.WriteLine("❌ Workload map CSV must contain a 'WorkloadID' column");
                    return false;
                }

                Console.WriteLine($"✅ Workload column: '{headers[workloadColumnIndex]}' (index {workloadColumnIndex})");

                int loadedCount = 0;
                int skippedCount = 0;

                for (int i = 1; i < rows.Count; i++)
                {
                    var values = rows[i];

                    if (values.Count == 0
                        || filenameColumnIndex >= values.Count
                        || workloadColumnIndex >= values.Count
                        || string.IsNullOrWhiteSpace(values[filenameColumnIndex])
                        || string.IsNullOrWhiteSpace(values[workloadColumnIndex]))
                    {
                        skippedCount++;
                        continue;
                    }

                    string markingFileLink = values[filenameColumnIndex].Trim();
                    string workloadId = values[workloadColumnIndex].Trim();

                    string filenameWithoutExt = Path.GetFileNameWithoutExtension(markingFileLink);
                    string filenameWithExt = markingFileLink.Contains('.')
                        ? markingFileLink
                        : markingFileLink + ".dwg";

                    // Store multiple key variations for flexible matching
                    _workloadMap[filenameWithExt] = workloadId;
                    _workloadMap[filenameWithoutExt] = workloadId;
                    _workloadMap[Path.GetFileName(filenameWithExt)] = workloadId;
                    _workloadMap[Path.GetFileName(filenameWithoutExt)] = workloadId;

                    loadedCount++;
                }

                Console.WriteLine($"✅ Loaded {loadedCount} workload ID mappings");
                if (skippedCount > 0)
                    Console.WriteLine($"⚠️  Skipped {skippedCount} empty or invalid rows");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading workload map CSV: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the WorkloadID for a drawing file path.
        /// Uses the same multi-strategy filename matching as CsvParameterMapper.
        /// Returns null if not found.
        /// </summary>
        public string GetWorkloadId(string drawingPath)
        {
            string result = CsvParser.FindValueForDrawing(_workloadMap, drawingPath);

            if (result == null)
                Console.WriteLine($"⚠️  No WorkloadID found for: {Path.GetFileName(drawingPath)}");

            return result;
        }

        /// <summary>
        /// Returns true if a workload ID exists for the given drawing.
        /// </summary>
        public bool HasWorkloadId(string drawingPath)
        {
            return GetWorkloadId(drawingPath) != null;
        }

        /// <summary>
        /// Get all drawing filenames (with .dwg) that have a workload ID mapping.
        /// </summary>
        public List<string> GetAllMappedDrawingFilenames()
        {
            return _workloadMap.Keys
                .Where(k => k.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Check which drawings from the given list are missing a workload ID mapping.
        /// </summary>
        public List<string> FindUnmappedDrawings(IEnumerable<string> drawingPaths)
        {
            return drawingPaths
                .Where(p => GetWorkloadId(p) == null)
                .Select(p => Path.GetFileName(p))
                .ToList();
        }
    }
}
