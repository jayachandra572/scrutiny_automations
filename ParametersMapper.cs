using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BatchProcessor
{
    /// <summary>
    /// Maps CSV data to ApplicationParameters.Parameters class
    /// </summary>
    public class ParametersMapper
    {
        private readonly Dictionary<string, string> _csvToPropertyMap;

        public ParametersMapper()
        {
            // Define mapping from CSV column names to Parameters property names
            _csvToPropertyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Direct mappings (same name)
                { "ProjectType", "ProjectType" },
                { "NatureOfDevelopment", "NatureOfDevelopment" },
                { "PlotUse", "PlotUse" },
                { "PlotSubUse", "PlotSubUse" },
                { "SpecialBuildingType", "SpecialBuildingType" },
                { "AvailTDR", "AvailTDR" },
                { "AvailRoadWideningConcession", "AvailRoadWideningConcession" },
                { "RoadWideningConcessionFor", "RoadWideningConcessionFor" },
                { "EffectedByNalaWidening", "EffectedByNalaWidening" },
                { "AvailNalaWideningConcession", "AvailNalaWideningConcession" },
                { "NalaWideningConcessionFor", "NalaWideningConcessionFor" },
                { "Authority", "Authority" },
                { "CategoryOfLayoutPermission", "CategoryOfLayoutPermission" },
                
                // Mappings with different names in CSV
                { "EffectedByRoadWidening", "EffectedbyRoadWidening" }, // Note: typo in property name
                { "DoYouWantToAvailExtraMortgageForNalaConversion", "AvailExtraMortgageForNalaConversion" },
                { "DoYouWantToAvailExtraMortgageForCityLevelImpactFee", "AvailExtraMortgageForCityLevelImpactFee" },
                { "DoYouWantToAvailExtraMortgageForCapitalizationCharges", "AvailExtraMortgageForCapitalizationCharges" },
            };
        }

        /// <summary>
        /// Convert CSV row data to Parameters JSON
        /// </summary>
        public string MapToParametersJson(Dictionary<string, string> csvRow, Dictionary<string, object> baseConfig = null)
        {
            var parameters = new Dictionary<string, object>();

            // Start with base config if provided
            if (baseConfig != null)
            {
                foreach (var kvp in baseConfig)
                {
                    parameters[kvp.Key] = kvp.Value;
                }
            }

            // Set default values for properties not in CSV
            if (!parameters.ContainsKey("ExtractBlockNames"))
                parameters["ExtractBlockNames"] = true;
            
            if (!parameters.ContainsKey("ExtractLayerNames"))
                parameters["ExtractLayerNames"] = true;
            
            if (!parameters.ContainsKey("layersToValidate"))
                parameters["layersToValidate"] = new List<string>();
            
            if (!parameters.ContainsKey("PluginVersion"))
                parameters["PluginVersion"] = "1.0";

            // Map CSV columns to Parameters properties
            foreach (var csvColumn in csvRow.Keys)
            {
                string csvValue = csvRow[csvColumn];
                
                // Skip empty values
                if (string.IsNullOrWhiteSpace(csvValue))
                    continue;

                // Find the corresponding property name
                string propertyName = null;
                if (_csvToPropertyMap.TryGetValue(csvColumn, out string mappedName))
                {
                    propertyName = mappedName;
                }
                else if (_csvToPropertyMap.ContainsValue(csvColumn))
                {
                    // Column name matches property name directly
                    propertyName = csvColumn;
                }

                if (propertyName != null)
                {
                    // Convert and set the value
                    object convertedValue = ConvertValue(csvValue, propertyName);
                    if (convertedValue != null)
                    {
                        parameters[propertyName] = convertedValue;
                    }
                }
            }

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null, // Keep original casing
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return JsonSerializer.Serialize(parameters, options);
        }

        /// <summary>
        /// Convert CSV string value to appropriate type based on property name
        /// </summary>
        private object ConvertValue(string value, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim();

            // Boolean properties
            if (IsBooleanProperty(propertyName))
            {
                if (value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value == "1")
                    return true;
                if (value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || value == "0")
                    return false;
                return false; // Default to false if unclear
            }

            // List properties (array in JSON)
            if (IsListProperty(propertyName))
            {
                // Handle JSON array format: ["item1", "item2"]
                if (value.StartsWith("[") && value.EndsWith("]"))
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(value);
                        return list ?? new List<string>();
                    }
                    catch
                    {
                        // If parsing fails, treat as empty list
                        return new List<string>();
                    }
                }
                // Handle comma-separated format
                else if (value.Contains(","))
                {
                    return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim().Trim('"'))
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .ToList();
                }
                // Single value - create list with one item
                else
                {
                    return new List<string> { value };
                }
            }

            // Numeric properties
            if (IsNumericProperty(propertyName))
            {
                if (double.TryParse(value, out double numValue))
                    return numValue;
                return value; // Return as string if not parseable
            }

            // String properties (default)
            return value;
        }

        /// <summary>
        /// Check if property is boolean type
        /// </summary>
        private bool IsBooleanProperty(string propertyName)
        {
            var boolProperties = new[]
            {
                "ExtractBlockNames",
                "ExtractLayerNames",
                "AvailTDR",
                "EffectedbyRoadWidening",
                "AvailRoadWideningConcession",
                "EffectedByNalaWidening",
                "AvailNalaWideningConcession",
                "AvailExtraMortgageForNalaConversion",
                "AvailExtraMortgageForCityLevelImpactFee",
                "AvailExtraMortgageForCapitalizationCharges"
            };

            return boolProperties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if property is list type
        /// </summary>
        private bool IsListProperty(string propertyName)
        {
            var listProperties = new[]
            {
                "RoadWideningConcessionFor",
                "NalaWideningConcessionFor",
                "layersToValidate"
            };

            return listProperties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if property is numeric type
        /// </summary>
        private bool IsNumericProperty(string propertyName)
        {
            var numericProperties = new[]
            {
                "PlotAreaAsPerDocument"
            };

            return numericProperties.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get mapping summary for display
        /// </summary>
        public string GetMappingSummary()
        {
            return $"CSV to Parameters Mapping:\n" +
                   $"  Boolean fields: {GetBooleanProperties().Count}\n" +
                   $"  List fields: {GetListProperties().Count}\n" +
                   $"  String fields: {GetStringProperties().Count}\n" +
                   $"  Total mapped columns: {_csvToPropertyMap.Count}";
        }

        private List<string> GetBooleanProperties()
        {
            return _csvToPropertyMap.Values.Where(IsBooleanProperty).Distinct().ToList();
        }

        private List<string> GetListProperties()
        {
            return _csvToPropertyMap.Values.Where(IsListProperty).Distinct().ToList();
        }

        private List<string> GetStringProperties()
        {
            return _csvToPropertyMap.Values
                .Where(p => !IsBooleanProperty(p) && !IsListProperty(p) && !IsNumericProperty(p))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Validate CSV has required columns
        /// </summary>
        public List<string> ValidateCsvColumns(List<string> csvHeaders)
        {
            var missingColumns = new List<string>();

            // Required columns
            var requiredColumns = new[] { "ProjectType", "PlotUse", "Authority" };

            foreach (var required in requiredColumns)
            {
                bool found = csvHeaders.Any(h => h.Equals(required, StringComparison.OrdinalIgnoreCase));
                if (!found)
                {
                    missingColumns.Add(required);
                }
            }

            return missingColumns;
        }
    }
}

