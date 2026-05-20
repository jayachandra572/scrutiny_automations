using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BatchProcessor.Utils;

namespace BatchProcessor.PreScrutiny
{
    /// <summary>
    /// Maps CSV file data to drawing-specific application parameters.
    /// CSV parsing and filename matching are delegated to CsvParser (scripts/Utils).
    /// </summary>
    public class CsvParameterMapper
    {
        private readonly string _csvFilePath;
        private Dictionary<string, Dictionary<string, string>> _parameterMap;
        private List<string> _columnHeaders;
        private readonly ParametersMapper _parametersMapper;

        public CsvParameterMapper(string csvFilePath)
        {
            _csvFilePath = csvFilePath;
            _parameterMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            _columnHeaders = new List<string>();
            _parametersMapper = new ParametersMapper();
        }

        /// <summary>
        /// Load and parse the CSV file.
        /// </summary>
        public bool LoadCsv()
        {
            try
            {
                if (!File.Exists(_csvFilePath))
                {
                    Console.WriteLine($"❌ CSV file not found: {_csvFilePath}");
                    return false;
                }

                string fileContent = File.ReadAllText(_csvFilePath);
                var rows = CsvParser.ParseCsvRows(fileContent);

                Console.WriteLine($"📊 Parsed {rows.Count} total rows from CSV (including header)");

                if (rows.Count < 2)
                {
                    Console.WriteLine("❌ CSV file must have at least a header row and one data row");
                    return false;
                }

                _columnHeaders = rows[0];
                Console.WriteLine($"✅ Found {_columnHeaders.Count} columns in CSV");
                Console.WriteLine($"📝 Header: {string.Join(", ", _columnHeaders.Take(5))}...");

                int filenameColumnIndex = CsvParser.FindFilenameColumnIndex(_columnHeaders);

                var dataRows = rows.Skip(1).ToList();
                _parameterMap = CsvParser.BuildParameterMap(_columnHeaders, dataRows, filenameColumnIndex);

                int rowCount = _parameterMap.Keys
                    .Count(k => k.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase));
                int skippedRows = dataRows.Count(r =>
                    r.Count == 0 || filenameColumnIndex >= r.Count
                    || string.IsNullOrWhiteSpace(r[filenameColumnIndex]));

                Console.WriteLine($"✅ Loaded {rowCount} drawing configurations from CSV");
                if (skippedRows > 0)
                    Console.WriteLine($"⚠️  Skipped {skippedRows} empty or invalid rows");

                Console.WriteLine("\n📋 Loaded filenames from CSV:");
                var loadedFiles = _parameterMap.Keys
                    .Where(k => k.Contains(".dwg", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => k)
                    .ToList();
                foreach (var file in loadedFiles.Take(10))
                    Console.WriteLine($"   - {file}");
                if (loadedFiles.Count > 10)
                    Console.WriteLine($"   ... and {loadedFiles.Count - 10} more");

                var missingColumns = _parametersMapper.ValidateCsvColumns(_columnHeaders);
                if (missingColumns.Count > 0)
                    Console.WriteLine($"⚠️  Warning: Missing recommended columns: {string.Join(", ", missingColumns)}");

                Console.WriteLine(_parametersMapper.GetMappingSummary());

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading CSV: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Get parameters for a specific drawing file.
        /// </summary>
        public Dictionary<string, string> GetParametersForDrawing(string drawingPath)
        {
            string filename = Path.GetFileName(drawingPath);
            string filenameWithoutExt = Path.GetFileNameWithoutExtension(drawingPath);

            // Build a string→string projection for the generic finder, then look up the full dict
            // Strategy 1: exact with extension
            if (_parameterMap.TryGetValue(filename, out var parameters))
            {
                Console.WriteLine($"  ✅ Matched: {filename} (exact with extension)");
                return parameters;
            }

            // Strategy 2: without extension
            if (_parameterMap.TryGetValue(filenameWithoutExt, out parameters))
            {
                Console.WriteLine($"  ✅ Matched: {filenameWithoutExt} (without extension)");
                return parameters;
            }

            // Strategy 3: case-insensitive
            var matchingKey = _parameterMap.Keys.FirstOrDefault(k =>
                k.Equals(filename, StringComparison.OrdinalIgnoreCase) ||
                k.Equals(filenameWithoutExt, StringComparison.OrdinalIgnoreCase));

            if (matchingKey != null && _parameterMap.TryGetValue(matchingKey, out parameters))
            {
                Console.WriteLine($"  ✅ Matched: {matchingKey} (case-insensitive)");
                return parameters;
            }

            // Strategy 4: partial
            matchingKey = _parameterMap.Keys.FirstOrDefault(k =>
                filename.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                k.Contains(filenameWithoutExt, StringComparison.OrdinalIgnoreCase));

            if (matchingKey != null && _parameterMap.TryGetValue(matchingKey, out parameters))
            {
                Console.WriteLine($"  ✅ Matched: {matchingKey} (partial match)");
                return parameters;
            }

            Console.WriteLine($"⚠️  No parameters found in CSV for: {filename}");
            Console.WriteLine($"   Tried: '{filename}', '{filenameWithoutExt}'");
            Console.WriteLine($"   Available keys (first 5): {string.Join(", ", _parameterMap.Keys.Take(5))}");
            return null;
        }

        /// <summary>
        /// Generate a JSON config string for a specific drawing using ParametersMapper.
        /// </summary>
        public string GenerateConfigJson(string drawingPath, string templateConfigPath = null)
        {
            var csvRow = GetParametersForDrawing(drawingPath);
            if (csvRow == null)
                return null;

            Dictionary<string, object> baseConfig = null;
            if (!string.IsNullOrEmpty(templateConfigPath) && File.Exists(templateConfigPath))
            {
                try
                {
                    var templateJson = File.ReadAllText(templateConfigPath);
                    baseConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(templateJson);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  Warning: Could not load template config: {ex.Message}");
                }
            }

            return _parametersMapper.MapToParametersJson(csvRow, baseConfig);
        }

        /// <summary>
        /// Get list of all drawing filenames (with .dwg extension) loaded from the CSV.
        /// </summary>
        public List<string> GetAllDrawingFilenames()
        {
            return _parameterMap.Keys
                .Where(k => k.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Returns true if the drawing has a matching row in the CSV.
        /// </summary>
        public bool HasDrawing(string drawingPath)
        {
            return GetParametersForDrawing(drawingPath) != null;
        }

        /// <summary>
        /// Get column headers from the loaded CSV.
        /// </summary>
        public List<string> GetColumnHeaders()
        {
            return new List<string>(_columnHeaders);
        }

        /// <summary>
        /// Get statistics about the loaded CSV.
        /// </summary>
        public string GetStatistics()
        {
            int totalDrawings = GetAllDrawingFilenames().Count;
            return $"CSV Statistics:\n" +
                   $"  - Total Drawings: {totalDrawings}\n" +
                   $"  - Parameters per Drawing: {_columnHeaders.Count}\n" +
                   $"  - CSV File: {Path.GetFileName(_csvFilePath)}";
        }
    }
}
