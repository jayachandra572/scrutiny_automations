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

                // Read entire file to handle multiline cells properly
                string fileContent = File.ReadAllText(_csvFilePath);
                var rows = ParseCsvRows(fileContent);
                
                Console.WriteLine($"📊 Parsed {rows.Count} total rows from CSV (including header)");
                
                if (rows.Count < 2)
                {
                    Console.WriteLine("❌ CSV file must have at least a header row and one data row");
                    return false;
                }

                // Parse header row
                _columnHeaders = rows[0];
                Console.WriteLine($"✅ Found {_columnHeaders.Count} columns in CSV");
                Console.WriteLine($"📝 Header: {string.Join(", ", _columnHeaders.Take(5))}...");

                // Find the filename column - use "Marking File Link" as specified by user
                int filenameColumnIndex = _columnHeaders.FindIndex(h => 
                    h.Equals("Marking File Link", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("MarkingFileLink", StringComparison.OrdinalIgnoreCase));
                
                if (filenameColumnIndex == -1)
                {
                    // Fallback to other common column names
                    filenameColumnIndex = _columnHeaders.FindIndex(h => 
                        h.Equals("Filename", StringComparison.OrdinalIgnoreCase) ||
                        h.Equals("File", StringComparison.OrdinalIgnoreCase) ||
                        h.Equals("Drawing", StringComparison.OrdinalIgnoreCase));
                    
                    if (filenameColumnIndex == -1)
                    {
                        Console.WriteLine("⚠️  Warning: No 'Marking File Link' column found, using first column");
                        filenameColumnIndex = 0;
                    }
                    else
                    {
                        Console.WriteLine($"⚠️  Warning: Using '{_columnHeaders[filenameColumnIndex]}' instead of 'Marking File Link'");
                    }
                }
                else
                {
                    Console.WriteLine($"✅ Found filename column: 'Marking File Link' (index {filenameColumnIndex})");
                }

                // Parse data rows
                int rowCount = 0;
                int skippedRows = 0;
                for (int i = 1; i < rows.Count; i++)
                {
                    var values = rows[i];
                    if (values.Count == 0 || filenameColumnIndex >= values.Count || string.IsNullOrWhiteSpace(values[filenameColumnIndex]))
                    {
                        skippedRows++;
                        continue;
                    }

                    string markingFileLink = values[filenameColumnIndex].Trim();
                    
                    // Remove file format suffix (.dwg) as specified by user
                    string filenameWithoutExt = Path.GetFileNameWithoutExtension(markingFileLink);
                    string filenameWithExt = markingFileLink;
                    
                    // If no extension was present, add .dwg for matching
                    if (!markingFileLink.Contains("."))
                    {
                        filenameWithExt = markingFileLink + ".dwg";
                    }
                    
                    // Create parameter dictionary for this drawing
                    var parameters = new Dictionary<string, string>();
                    for (int j = 0; j < Math.Min(_columnHeaders.Count, values.Count); j++)
                    {
                        parameters[_columnHeaders[j]] = values[j];
                    }

                    // Store with multiple key variations for flexible matching
                    // Store with extension, without extension, and with full path variations
                    _parameterMap[filenameWithExt] = parameters;
                    _parameterMap[filenameWithoutExt] = parameters;
                    _parameterMap[Path.GetFileName(filenameWithExt)] = parameters;
                    _parameterMap[Path.GetFileName(filenameWithoutExt)] = parameters;
                    
                    rowCount++;
                }

                Console.WriteLine($"✅ Loaded {rowCount} drawing configurations from CSV");
                if (skippedRows > 0)
                {
                    Console.WriteLine($"⚠️  Skipped {skippedRows} empty or invalid rows");
                }
                
                // Debug: Show all loaded filenames
                Console.WriteLine("\n📋 Loaded filenames from CSV:");
                var loadedFiles = _parameterMap.Keys
                    .Where(k => k.Contains(".dwg", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => k)
                    .ToList();
                foreach (var file in loadedFiles.Take(10))
                {
                    Console.WriteLine($"   - {file}");
                }
                if (loadedFiles.Count > 10)
                {
                    Console.WriteLine($"   ... and {loadedFiles.Count - 10} more");
                }
                
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
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
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

            // Try multiple matching strategies
            // 1. Exact match with extension
            if (_parameterMap.TryGetValue(filename, out var parameters))
            {
                Console.WriteLine($"  ✅ Matched: {filename} (exact with extension)");
                return parameters;
            }

            // 2. Match without extension
            if (_parameterMap.TryGetValue(filenameWithoutExt, out parameters))
            {
                Console.WriteLine($"  ✅ Matched: {filenameWithoutExt} (without extension)");
                return parameters;
            }

            // 3. Try case-insensitive match
            var matchingKey = _parameterMap.Keys.FirstOrDefault(k => 
                k.Equals(filename, StringComparison.OrdinalIgnoreCase) ||
                k.Equals(filenameWithoutExt, StringComparison.OrdinalIgnoreCase));
            
            if (matchingKey != null && _parameterMap.TryGetValue(matchingKey, out parameters))
            {
                Console.WriteLine($"  ✅ Matched: {matchingKey} (case-insensitive)");
                return parameters;
            }

            // 4. Try partial match (filename contains key or key contains filename)
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
        /// Parse entire CSV content handling quoted values and multiline cells
        /// </summary>
        private List<List<string>> ParseCsvRows(string content)
        {
            var rows = new List<List<string>>();
            var currentRow = new List<string>();
            var currentCell = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                char nextChar = (i + 1 < content.Length) ? content[i + 1] : '\0';

                if (c == '"')
                {
                    if (inQuotes && nextChar == '"')
                    {
                        // Escaped quote - add one quote and skip next
                        currentCell.Append('"');
                        i++;
                    }
                    else
                    {
                        // Toggle quote state
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    // End of cell
                    currentRow.Add(currentCell.ToString().Trim());
                    currentCell.Clear();
                }
                else if ((c == '\r' || c == '\n') && !inQuotes)
                {
                    // End of row (outside quotes)
                    if (c == '\r' && nextChar == '\n')
                    {
                        i++; // Skip \n in \r\n
                    }

                    // Add current cell if we have data
                    if (currentCell.Length > 0 || currentRow.Count > 0)
                    {
                        currentRow.Add(currentCell.ToString().Trim());
                        currentCell.Clear();

                        // Add row if it has data
                        if (currentRow.Count > 0)
                        {
                            rows.Add(currentRow);
                            currentRow = new List<string>();
                        }
                    }
                }
                else
                {
                    // Regular character (including newlines inside quotes)
                    currentCell.Append(c);
                }
            }

            // Add final cell and row if any
            if (currentCell.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentCell.ToString().Trim());
                if (currentRow.Count > 0)
                {
                    rows.Add(currentRow);
                }
            }

            return rows;
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

