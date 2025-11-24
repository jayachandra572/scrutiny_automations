using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BatchProcessor
{
    /// <summary>
    /// Maps CSV file data to drawing-specific application parameters
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
        /// Load and parse the CSV file
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

                var lines = File.ReadAllLines(_csvFilePath);
                if (lines.Length < 2)
                {
                    Console.WriteLine("❌ CSV file must have at least a header row and one data row");
                    return false;
                }

                // Parse header row
                _columnHeaders = ParseCsvLine(lines[0]);
                Console.WriteLine($"✅ Found {_columnHeaders.Count} columns in CSV");

                // Find the filename column (should be first column or named "Filename")
                int filenameColumnIndex = 0;
                if (!_columnHeaders[0].Equals("Filename", StringComparison.OrdinalIgnoreCase))
                {
                    filenameColumnIndex = _columnHeaders.FindIndex(h => 
                        h.Equals("Filename", StringComparison.OrdinalIgnoreCase) ||
                        h.Equals("File", StringComparison.OrdinalIgnoreCase) ||
                        h.Equals("Drawing", StringComparison.OrdinalIgnoreCase));
                    
                    if (filenameColumnIndex == -1)
                    {
                        Console.WriteLine("⚠️  Warning: No 'Filename' column found, using first column");
                        filenameColumnIndex = 0;
                    }
                }

                // Parse data rows
                int rowCount = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    var values = ParseCsvLine(lines[i]);
                    if (values.Count == 0 || string.IsNullOrWhiteSpace(values[filenameColumnIndex]))
                        continue;

                    string filename = values[filenameColumnIndex].Trim();
                    
                    // Create parameter dictionary for this drawing
                    var parameters = new Dictionary<string, string>();
                    for (int j = 0; j < Math.Min(_columnHeaders.Count, values.Count); j++)
                    {
                        parameters[_columnHeaders[j]] = values[j];
                    }

                    // Store with multiple key variations for flexible matching
                    _parameterMap[filename] = parameters;
                    _parameterMap[Path.GetFileNameWithoutExtension(filename)] = parameters;
                    
                    rowCount++;
                }

                Console.WriteLine($"✅ Loaded {rowCount} drawing configurations from CSV");
                
                // Validate required columns
                var missingColumns = _parametersMapper.ValidateCsvColumns(_columnHeaders);
                if (missingColumns.Count > 0)
                {
                    Console.WriteLine($"⚠️  Warning: Missing recommended columns: {string.Join(", ", missingColumns)}");
                }
                
                // Show mapping summary
                Console.WriteLine(_parametersMapper.GetMappingSummary());
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading CSV: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get parameters for a specific drawing file
        /// </summary>
        public Dictionary<string, string> GetParametersForDrawing(string drawingPath)
        {
            string filename = Path.GetFileName(drawingPath);
            string filenameWithoutExt = Path.GetFileNameWithoutExtension(drawingPath);

            // Try exact match first
            if (_parameterMap.TryGetValue(filename, out var parameters))
                return parameters;

            // Try without extension
            if (_parameterMap.TryGetValue(filenameWithoutExt, out parameters))
                return parameters;

            Console.WriteLine($"⚠️  No parameters found in CSV for: {filename}");
            return null;
        }

        /// <summary>
        /// Generate a JSON config file for a specific drawing using Parameters mapping
        /// </summary>
        public string GenerateConfigJson(string drawingPath, string templateConfigPath = null)
        {
            var csvRow = GetParametersForDrawing(drawingPath);
            if (csvRow == null)
                return null;

            // Load template config if provided
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

            // Use ParametersMapper to convert CSV row to Parameters JSON
            return _parametersMapper.MapToParametersJson(csvRow, baseConfig);
        }

        /// <summary>
        /// Get list of all drawing filenames in the CSV
        /// </summary>
        public List<string> GetAllDrawingFilenames()
        {
            return _parameterMap.Keys
                .Where(k => k.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Get statistics about the loaded CSV
        /// </summary>
        public string GetStatistics()
        {
            int totalDrawings = GetAllDrawingFilenames().Count;
            return $"CSV Statistics:\n" +
                   $"  - Total Drawings: {totalDrawings}\n" +
                   $"  - Parameters per Drawing: {_columnHeaders.Count}\n" +
                   $"  - CSV File: {Path.GetFileName(_csvFilePath)}";
        }

        /// <summary>
        /// Parse a CSV line handling quoted values
        /// </summary>
        private List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            bool inQuotes = false;
            var currentValue = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    values.Add(currentValue.ToString().Trim());
                    currentValue.Clear();
                }
                else
                {
                    currentValue.Append(c);
                }
            }

            // Add the last value
            values.Add(currentValue.ToString().Trim());

            return values;
        }

        /// <summary>
        /// Validate if a drawing exists in the CSV
        /// </summary>
        public bool HasDrawing(string drawingPath)
        {
            return GetParametersForDrawing(drawingPath) != null;
        }

        /// <summary>
        /// Get column headers from CSV
        /// </summary>
        public List<string> GetColumnHeaders()
        {
            return new List<string>(_columnHeaders);
        }
    }
}

