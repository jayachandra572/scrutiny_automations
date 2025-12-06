using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BatchProcessor.Models;
// using JsonDiffPatch; // Temporarily disabled - package reference issue
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BatchProcessor
{
    public class JsonDiffComparer
    {
        /// <summary>
        /// Compare two folders of JSON files and generate a diff report
        /// </summary>
        /// <param name="referenceFolder">Folder containing reference/baseline JSON files</param>
        /// <param name="latestFolder">Folder containing latest/current JSON files</param>
        /// <param name="outputFolder">Optional output folder for diff files (if null, diff files won't be created)</param>
        /// <param name="createDiffFiles">If true, creates diff JSON files in a subfolder when differences are found</param>
        /// <param name="diffSubfolderName">Name of the subfolder for diff files (default: "diff")</param>
        /// <param name="ignoredKeys">List of JSON paths to ignore during comparison (e.g., "timestamp", "plotData.timestamp", "AbuttingRoad[].timestamp")</param>
        public DiffReport CompareFolders(string referenceFolder, string latestFolder, string? outputFolder = null, bool createDiffFiles = false, string diffSubfolderName = "diff", List<string>? ignoredKeys = null)
        {
            var report = new DiffReport
            {
                ReferenceFolder = referenceFolder,
                LatestFolder = latestFolder,
                ComparisonDate = DateTime.Now
            };

            if (!Directory.Exists(referenceFolder))
            {
                throw new DirectoryNotFoundException($"Reference folder not found: {referenceFolder}");
            }

            if (!Directory.Exists(latestFolder))
            {
                throw new DirectoryNotFoundException($"Latest folder not found: {latestFolder}");
            }

            // Create diff subfolder if needed
            string? diffSubfolder = null;
            if (createDiffFiles && !string.IsNullOrEmpty(outputFolder))
            {
                diffSubfolder = Path.Combine(outputFolder, diffSubfolderName);
                if (!Directory.Exists(diffSubfolder))
                {
                    Directory.CreateDirectory(diffSubfolder);
                }
            }

            // Get all JSON files from both folders
            var referenceFiles = Directory.GetFiles(referenceFolder, "*.json")
                .ToDictionary(f => GetBaseFileName(f), f => f, StringComparer.OrdinalIgnoreCase);

            var latestFiles = Directory.GetFiles(latestFolder, "*.json")
                .ToDictionary(f => GetBaseFileName(f), f => f, StringComparer.OrdinalIgnoreCase);

            // Get all unique base filenames
            var allBaseNames = referenceFiles.Keys.Union(latestFiles.Keys).ToList();
            report.TotalFiles = allBaseNames.Count;

            // Compare each file
            foreach (var baseName in allBaseNames)
            {
                bool hasReference = referenceFiles.TryGetValue(baseName, out string? referencePath);
                bool hasLatest = latestFiles.TryGetValue(baseName, out string? latestPath);

                var result = new JsonDiffResult
                {
                    FileName = baseName + ".json"
                };

                if (!hasReference)
                {
                    result.IsMissingInReference = true;
                    result.FilesMatch = false;
                    report.MissingInReference++;
                }
                else if (!hasLatest)
                {
                    result.IsMissingInLatest = true;
                    result.FilesMatch = false;
                    report.MissingInLatest++;
                }
                else
                {
                    // Both files exist, compare them
                    result = CompareFiles(referencePath!, latestPath!, ignoredKeys);

                    // Create diff file if differences found and output folder is specified
                    if (createDiffFiles && !result.FilesMatch && result.Differences != null && result.Differences.Count > 0 && diffSubfolder != null)
                    {
                        try
                        {
                            string diffFileName = $"{baseName}_diff.json";
                            string diffFilePath = Path.Combine(diffSubfolder, diffFileName);

                            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                            string diffJson = System.Text.Json.JsonSerializer.Serialize(result, options);
                            File.WriteAllText(diffFilePath, diffJson);
                        }
                        catch (Exception ex)
                        {
                            // Log error but continue processing
                            Console.WriteLine($"⚠️  Error creating diff file for {baseName}: {ex.Message}");
                        }
                    }
                }

                report.FileResults.Add(result);

                // Update statistics
                if (result.FilesMatch)
                {
                    report.MatchingFiles++;
                }
                else if (!result.IsMissingInLatest && !result.IsMissingInReference)
                {
                    report.DifferentFiles++;
                }
            }

            return report;
        }

        /// <summary>
        /// Compare two JSON files using JsonDiffPatch.NET
        /// </summary>
        /// <param name="referencePath">Path to reference JSON file</param>
        /// <param name="latestPath">Path to latest JSON file</param>
        /// <param name="ignoredKeys">List of JSON paths to ignore during comparison</param>
        public JsonDiffResult CompareFiles(string referencePath, string latestPath, List<string>? ignoredKeys = null)
        {
            var result = new JsonDiffResult
            {
                FileName = Path.GetFileName(referencePath)
            };

            try
            {
                string referenceJson = File.ReadAllText(referencePath);
                string latestJson = File.ReadAllText(latestPath);

                // Parse JSON using System.Text.Json for ID extraction
                using var referenceDoc = JsonDocument.Parse(referenceJson);
                using var latestDoc = JsonDocument.Parse(latestJson);

                // Extract ID from JSON (try common ID fields) - wrap in try-catch to prevent exceptions
                try
                {
                    var idInfo = ExtractIdFromJson(referenceDoc.RootElement) ?? ExtractIdFromJson(latestDoc.RootElement);
                    if (idInfo.HasValue)
                    {
                        result.Id = idInfo.Value.Value;
                        result.IdKey = idInfo.Value.Key;
                    }
                }
                catch (Exception idEx)
                {
                    // Log but don't fail - ID extraction is optional
                    Console.WriteLine($"⚠️  Warning: Could not extract ID from JSON: {idEx.Message}");
                }

                // Use JsonDiffPatch.NET for core comparison
                var differences = CompareUsingJsonDiffPatch(referenceJson, latestJson, referenceDoc, latestDoc, ignoredKeys);

                result.Differences = differences;
                result.FilesMatch = differences.Count == 0;
            }
            catch (Exception ex)
            {
                // If an exception occurs, add it as a difference so it's visible
                result.ErrorMessage = ex.Message;
                result.FilesMatch = false;
                
                // Add a difference entry for the error so it's visible in the diff file
                if (result.Differences == null)
                {
                    result.Differences = new List<PropertyDifference>();
                }
                result.Differences.Add(new PropertyDifference
                {
                    Path = "",
                    Type = DifferenceType.TypeChanged,
                    ReferenceValue = $"Error during comparison: {ex.Message}",
                    LatestValue = null,
                    ReferenceType = "error",
                    LatestType = null
                });
            }

            return result;
        }

        /// <summary>
        /// Compare two JSON strings using JsonDiffPatch.NET and convert to PropertyDifference list
        /// </summary>
        private List<PropertyDifference> CompareUsingJsonDiffPatch(string referenceJson, string latestJson, JsonDocument referenceDoc, JsonDocument latestDoc, List<string>? ignoredKeys)
        {
            var differences = new List<PropertyDifference>();
            
            // JsonDiffPatch temporarily disabled due to package reference issue
            // Using fallback manual comparison instead
            // TODO: Fix JsonDiffPatch.Net package reference
            /*
            var jdp = new JsonDiffPatch();

            try
            {
                // Parse JSON using Newtonsoft.Json for JsonDiffPatch
                JToken referenceToken = JToken.Parse(referenceJson);
                JToken latestToken = JToken.Parse(latestJson);

                // If we have ignored keys, remove them from both tokens before diffing
                // This is more efficient than filtering after the diff
                if (ignoredKeys != null && ignoredKeys.Count > 0)
                {
                    referenceToken = RemoveIgnoredKeys(referenceToken, ignoredKeys, "");
                    latestToken = RemoveIgnoredKeys(latestToken, ignoredKeys, "");
                }

                // Get the diff using JsonDiffPatch.NET
                JToken? diff = jdp.Diff(referenceToken, latestToken);

                if (diff == null || diff.Type == JTokenType.Null)
                {
                    // No differences found
                    return differences;
                }

                // Convert JsonDiffPatch diff format to our PropertyDifference format
                // Note: We still pass ignoredKeys to ConvertDiffToPropertyDifferences as a safety check
                ConvertDiffToPropertyDifferences(diff, referenceToken, latestToken, "", differences, referenceDoc, latestDoc, ignoredKeys);
            }
            catch (Exception ex)
            {
                // If JsonDiffPatch fails, fall back to manual comparison
                Console.WriteLine($"⚠️  Warning: JsonDiffPatch comparison failed, falling back to manual comparison: {ex.Message}");
                CompareJsonElements(referenceDoc.RootElement, latestDoc.RootElement, "", differences, null, null, ignoredKeys);
            }
            */
            
            // Use manual comparison as fallback
            CompareJsonElements(referenceDoc.RootElement, latestDoc.RootElement, "", differences, null, null, ignoredKeys);

            return differences;
        }

        /// <summary>
        /// Remove ignored keys from a JToken recursively
        /// For objects: removes ignored properties
        /// For arrays: processes elements but keeps array structure (doesn't remove entire elements to preserve indices)
        /// </summary>
        private JToken RemoveIgnoredKeys(JToken token, List<string> ignoredKeys, string currentPath)
        {
            if (token == null || token.Type == JTokenType.Null)
                return token;

            // Check if current path should be ignored (for root-level objects/arrays)
            if (!string.IsNullOrEmpty(currentPath) && IsPathIgnored(currentPath, ignoredKeys))
            {
                return JValue.CreateNull();
            }

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token.DeepClone();
                var propertiesToRemove = new List<string>();

                foreach (var property in obj.Properties())
                {
                    string propertyPath = string.IsNullOrEmpty(currentPath) ? property.Name : $"{currentPath}.{property.Name}";

                    // Check if this property path should be ignored
                    if (IsPathIgnored(propertyPath, ignoredKeys))
                    {
                        propertiesToRemove.Add(property.Name);
                    }
                    else
                    {
                        // Recursively process nested objects and arrays
                        obj[property.Name] = RemoveIgnoredKeys(property.Value, ignoredKeys, propertyPath);
                    }
                }

                // Remove ignored properties
                foreach (var propName in propertiesToRemove)
                {
                    obj.Remove(propName);
                }

                return obj;
            }
            else if (token.Type == JTokenType.Array)
            {
                var array = (JArray)token.DeepClone();
                var newArray = new JArray();

                for (int i = 0; i < array.Count; i++)
                {
                    string arrayPath = $"{currentPath}[{i}]";

                    // Check if entire array element should be ignored
                    // Note: We check for exact array path match (e.g., "ArrayName[0]") but not wildcard patterns here
                    // Wildcard patterns like "ArrayName[].property" are handled during diff conversion
                    if (IsPathIgnored(arrayPath, ignoredKeys))
                    {
                        // For exact array element matches, we can skip, but preserve structure
                        // Add a null placeholder to maintain array indices
                        newArray.Add(JValue.CreateNull());
                    }
                    else
                    {
                        // Recursively process array element (this handles nested ignored keys within the element)
                        var processedElement = RemoveIgnoredKeys(array[i], ignoredKeys, arrayPath);
                        newArray.Add(processedElement);
                    }
                }

                return newArray;
            }

            // For primitive values, return as-is
            return token;
        }

        /// <summary>
        /// Convert JsonDiffPatch diff format to PropertyDifference list
        /// </summary>
        private void ConvertDiffToPropertyDifferences(JToken diff, JToken reference, JToken latest, string currentPath, List<PropertyDifference> differences, JsonDocument referenceDoc, JsonDocument latestDoc, List<string>? ignoredKeys)
        {
            if (diff == null || diff.Type == JTokenType.Null)
                return;

            // Check if this path should be ignored
            if (IsPathIgnored(currentPath, ignoredKeys))
            {
                return;
            }

            if (diff.Type == JTokenType.Object)
            {
                var diffObj = (JObject)diff;
                foreach (var property in diffObj.Properties())
                {
                    string propertyPath = string.IsNullOrEmpty(currentPath) ? property.Name : $"{currentPath}.{property.Name}";
                    var diffValue = property.Value;

                    if (diffValue == null)
                        continue;

                    // JsonDiffPatch uses special keys: _t (type), _0 (old value), _1 (new value), etc.
                    if (property.Name == "_t")
                    {
                        // Type change - check if path should be ignored
                        if (!IsPathIgnored(currentPath, ignoredKeys))
                        {
                            string? oldValue = GetJTokenValue(reference)?.ToString();
                            string? newValue = GetJTokenValue(latest)?.ToString();
                            
                            var idInfo = ExtractIdFromJson(referenceDoc.RootElement) ?? ExtractIdFromJson(latestDoc.RootElement);
                            
                            differences.Add(new PropertyDifference
                            {
                                Path = currentPath,
                                Type = DifferenceType.TypeChanged,
                                ReferenceValue = oldValue,
                                LatestValue = newValue,
                                ReferenceType = GetJTokenType(reference),
                                LatestType = GetJTokenType(latest),
                                IdKey = idInfo?.Key,
                                IdValue = idInfo?.Value
                            });
                        }
                    }
                    else if (diffValue.Type == JTokenType.Array)
                    {
                        // Array diff: [_0, _1] where _0 is old value, _1 is new value
                        var diffArray = (JArray)diffValue;
                        
                        // Check if this property path should be ignored before processing
                        if (IsPathIgnored(propertyPath, ignoredKeys))
                        {
                            continue;
                        }
                        
                        if (diffArray.Count >= 2)
                        {
                            var oldValue = diffArray[0];
                            var newValue = diffArray[1];

                            // Try to extract entityID
                            var idInfo = ExtractIdFromJson(referenceDoc.RootElement) ?? ExtractIdFromJson(latestDoc.RootElement);
                            
                            // Determine if it's a modification or type change
                            bool isTypeChange = oldValue?.Type != newValue?.Type;
                            
                            differences.Add(new PropertyDifference
                            {
                                Path = propertyPath,
                                Type = isTypeChange ? DifferenceType.TypeChanged : DifferenceType.Modified,
                                ReferenceValue = GetJTokenValue(oldValue),
                                LatestValue = GetJTokenValue(newValue),
                                ReferenceType = GetJTokenType(oldValue),
                                LatestType = GetJTokenType(newValue),
                                IdKey = idInfo?.Key,
                                IdValue = idInfo?.Value
                            });
                        }
                        else if (diffArray.Count == 1)
                        {
                            // Single value means added or removed
                            var value = diffArray[0];
                            var idInfo = ExtractIdFromJson(referenceDoc.RootElement) ?? ExtractIdFromJson(latestDoc.RootElement);
                            
                            // Check if it exists in reference or latest to determine if added or removed
                            bool existsInReference = reference != null && reference.Type != JTokenType.Null;
                            
                            differences.Add(new PropertyDifference
                            {
                                Path = propertyPath,
                                Type = existsInReference ? DifferenceType.Removed : DifferenceType.Added,
                                ReferenceValue = existsInReference ? GetJTokenValue(value) : null,
                                LatestValue = existsInReference ? null : GetJTokenValue(value),
                                ReferenceType = existsInReference ? GetJTokenType(value) : null,
                                LatestType = existsInReference ? null : GetJTokenType(value),
                                IdKey = idInfo?.Key,
                                IdValue = idInfo?.Value
                            });
                        }
                    }
                    else if (diffValue.Type == JTokenType.Object)
                    {
                        // Nested object - check if path should be ignored before recursing
                        if (!IsPathIgnored(propertyPath, ignoredKeys))
                        {
                            JToken? refChild = reference?[property.Name];
                            JToken? latChild = latest?[property.Name];
                            ConvertDiffToPropertyDifferences(diffValue, refChild ?? JValue.CreateNull(), latChild ?? JValue.CreateNull(), propertyPath, differences, referenceDoc, latestDoc, ignoredKeys);
                        }
                    }
                    else
                    {
                        // Simple value change - check if path should be ignored
                        if (!IsPathIgnored(propertyPath, ignoredKeys))
                        {
                            var idInfo = ExtractIdFromJson(referenceDoc.RootElement) ?? ExtractIdFromJson(latestDoc.RootElement);
                            
                            differences.Add(new PropertyDifference
                            {
                                Path = propertyPath,
                                Type = DifferenceType.Modified,
                                ReferenceValue = GetJTokenValue(reference?[property.Name]),
                                LatestValue = GetJTokenValue(latest?[property.Name]),
                                ReferenceType = GetJTokenType(reference?[property.Name]),
                                LatestType = GetJTokenType(latest?[property.Name]),
                                IdKey = idInfo?.Key,
                                IdValue = idInfo?.Value
                            });
                        }
                    }
                }
            }
            else if (diff.Type == JTokenType.Array)
            {
                // Array-level diff
                var diffArray = (JArray)diff;
                var refArray = reference as JArray;
                var latArray = latest as JArray;

                int maxLength = Math.Max(refArray?.Count ?? 0, latArray?.Count ?? 0);

                for (int i = 0; i < maxLength; i++)
                {
                    string arrayPath = $"{currentPath}[{i}]";

                    if (i >= (refArray?.Count ?? 0))
                    {
                        // Added element
                        if (!IsPathIgnored(arrayPath, ignoredKeys))
                        {
                            var idInfo = ExtractIdFromJson(latestDoc.RootElement);
                            differences.Add(new PropertyDifference
                            {
                                Path = arrayPath,
                                Type = DifferenceType.Added,
                                ReferenceValue = null,
                                LatestValue = GetJTokenValue(latArray?[i]),
                                IdKey = idInfo?.Key,
                                IdValue = idInfo?.Value,
                                IsArrayItemAdded = true
                            });
                        }
                    }
                    else if (i >= (latArray?.Count ?? 0))
                    {
                        // Removed element
                        if (!IsPathIgnored(arrayPath, ignoredKeys))
                        {
                            var idInfo = ExtractIdFromJson(referenceDoc.RootElement);
                            differences.Add(new PropertyDifference
                            {
                                Path = arrayPath,
                                Type = DifferenceType.Removed,
                                ReferenceValue = GetJTokenValue(refArray?[i]),
                                LatestValue = null,
                                IdKey = idInfo?.Key,
                                IdValue = idInfo?.Value,
                                IsArrayItemRemoved = true
                            });
                        }
                    }
                    else
                    {
                        // Compare elements at same index
                        ConvertDiffToPropertyDifferences(diffArray[i] ?? JValue.CreateNull(), refArray?[i] ?? JValue.CreateNull(), latArray?[i] ?? JValue.CreateNull(), arrayPath, differences, referenceDoc, latestDoc, ignoredKeys);
                    }
                }
            }
        }

        /// <summary>
        /// Get a readable value from JToken
        /// </summary>
        private object? GetJTokenValue(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            return token.Type switch
            {
                JTokenType.String => token.ToString(),
                JTokenType.Integer => token.ToObject<long>(),
                JTokenType.Float => token.ToObject<decimal>(),
                JTokenType.Boolean => token.ToObject<bool>(),
                JTokenType.Object => token.ToString(Formatting.None),
                JTokenType.Array => token.ToString(Formatting.None),
                _ => token.ToString()
            };
        }

        /// <summary>
        /// Get type name from JToken
        /// </summary>
        private string GetJTokenType(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return "null";

            return token.Type switch
            {
                JTokenType.String => "string",
                JTokenType.Integer => "int",
                JTokenType.Float => "float",
                JTokenType.Boolean => "bool",
                JTokenType.Object => "object",
                JTokenType.Array => "array",
                _ => "unknown"
            };
        }

        /// <summary>
        /// Compare two JSON files and create a diff file in a subfolder if differences are found
        /// </summary>
        /// <param name="referencePath">Path to reference/baseline JSON file</param>
        /// <param name="latestPath">Path to latest/current JSON file</param>
        /// <param name="outputFolder">Output folder where diff subfolder will be created</param>
        /// <param name="diffSubfolderName">Name of the subfolder for diff files (default: "diff")</param>
        /// <param name="ignoredKeys">List of JSON paths to ignore during comparison</param>
        /// <returns>Path to the created diff file, or null if no differences found or error occurred</returns>
        public string? CompareFilesAndCreateDiff(string referencePath, string latestPath, string outputFolder, string diffSubfolderName = "diff", List<string>? ignoredKeys = null)
        {
            try
            {
                // Compare the files
                var diffResult = CompareFiles(referencePath, latestPath, ignoredKeys);

                // Only create diff file if differences are found
                if (!diffResult.FilesMatch && diffResult.Differences != null && diffResult.Differences.Count > 0)
                {
                    // Create diff subfolder
                    string diffSubfolder = Path.Combine(outputFolder, diffSubfolderName);
                    if (!Directory.Exists(diffSubfolder))
                    {
                        Directory.CreateDirectory(diffSubfolder);
                    }

                    // Create diff file path in subfolder
                    string baseFileName = Path.GetFileNameWithoutExtension(referencePath);
                    string diffFileName = $"{baseFileName}_diff.json";
                    string diffFilePath = Path.Combine(diffSubfolder, diffFileName);

                    // Serialize diff result to JSON
                    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    string diffJson = System.Text.Json.JsonSerializer.Serialize(diffResult, options);
                    File.WriteAllText(diffFilePath, diffJson);

                    return diffFilePath;
                }

                return null; // No differences found
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return null to indicate failure
                Console.WriteLine($"⚠️  Error creating diff file: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extract ID from JSON document (tries common ID field names and fields containing "ID")
        /// Returns a tuple with (Key, Value) or null if not found
        /// </summary>
        private (string Key, string Value)? ExtractIdFromJson(JsonElement root)
        {
            // Only process objects - skip strings, numbers, arrays, etc.
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            // First, try common ID field names at root level
            string[] idFieldNames = { "id", "ID", "Id", "plotID", "plotId", "projectID", "projectId", "workitem_id", "workitemId" };
            
            foreach (var fieldName in idFieldNames)
            {
                if (root.TryGetProperty(fieldName, out JsonElement idElement))
                {
                    if (idElement.ValueKind == JsonValueKind.String)
                    {
                        string? value = idElement.GetString();
                        if (!string.IsNullOrEmpty(value) && value != "0")
                        {
                            return (fieldName, value);
                        }
                    }
                    else if (idElement.ValueKind == JsonValueKind.Number)
                    {
                        // Check if value is not zero
                        if (idElement.TryGetInt64(out long longValue) && longValue != 0)
                        {
                            return (fieldName, longValue.ToString());
                        }
                        else if (idElement.TryGetDouble(out double doubleValue) && doubleValue != 0.0)
                        {
                            return (fieldName, doubleValue.ToString());
                        }
                    }
                }
            }

            // Then, check all properties for fields containing "ID" (case-insensitive)
            foreach (var property in root.EnumerateObject())
            {
                string propertyName = property.Name;
                
                // Check if property name contains "ID" (case-insensitive)
                if (propertyName.Contains("ID", StringComparison.OrdinalIgnoreCase) ||
                    propertyName.Contains("Id", StringComparison.OrdinalIgnoreCase))
                {
                    JsonElement idElement = property.Value;
                    
                    // Only extract if it's a simple value (string or number), not an object or array
                    if (idElement.ValueKind == JsonValueKind.String)
                    {
                        string? value = idElement.GetString();
                        if (!string.IsNullOrEmpty(value) && value != "0")
                        {
                            return (propertyName, value);
                        }
                    }
                    else if (idElement.ValueKind == JsonValueKind.Number)
                    {
                        // Check if value is not zero
                        if (idElement.TryGetInt64(out long longValue) && longValue != 0)
                        {
                            return (propertyName, longValue.ToString());
                        }
                        else if (idElement.TryGetDouble(out double doubleValue) && doubleValue != 0.0)
                        {
                            return (propertyName, doubleValue.ToString());
                        }
                    }
                }
            }

            // Try nested paths (e.g., plotData.plotID)
            if (root.TryGetProperty("plotData", out JsonElement plotData))
            {
                // Check for ID fields in plotData
                foreach (var property in plotData.EnumerateObject())
                {
                    string propertyName = property.Name;
                    
                    if (propertyName.Contains("ID", StringComparison.OrdinalIgnoreCase) ||
                        propertyName.Contains("Id", StringComparison.OrdinalIgnoreCase))
                    {
                        JsonElement idElement = property.Value;
                        
                        if (idElement.ValueKind == JsonValueKind.String)
                        {
                            string? value = idElement.GetString();
                            if (!string.IsNullOrEmpty(value) && value != "0")
                            {
                                return ("plotData." + propertyName, value);
                            }
                        }
                        else if (idElement.ValueKind == JsonValueKind.Number)
                        {
                            // Check if value is not zero
                            if (idElement.TryGetInt64(out long longValue) && longValue != 0)
                            {
                                return ("plotData." + propertyName, longValue.ToString());
                            }
                            else if (idElement.TryGetDouble(out double doubleValue) && doubleValue != 0.0)
                            {
                                return ("plotData." + propertyName, doubleValue.ToString());
                            }
                        }
                    }
                }
            }

            // Check arrays - look for ID fields in first element of arrays
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    var array = property.Value.EnumerateArray();
                    if (array.MoveNext())
                    {
                        var firstElement = array.Current;
                        if (firstElement.ValueKind == JsonValueKind.Object)
                        {
                            // Check for ID fields in the first array element
                            foreach (var arrayElementProperty in firstElement.EnumerateObject())
                            {
                                string propertyName = arrayElementProperty.Name;
                                
                                if (propertyName.Contains("ID", StringComparison.OrdinalIgnoreCase) ||
                                    propertyName.Contains("Id", StringComparison.OrdinalIgnoreCase))
                                {
                                    JsonElement idElement = arrayElementProperty.Value;
                                    
                                    if (idElement.ValueKind == JsonValueKind.String)
                                    {
                                        string? value = idElement.GetString();
                                        if (!string.IsNullOrEmpty(value) && value != "0")
                                        {
                                            return ($"{property.Name}[0].{propertyName}", value);
                                        }
                                    }
                                    else if (idElement.ValueKind == JsonValueKind.Number)
                                    {
                                        // Check if value is not zero - skip zero values
                                        if (idElement.TryGetInt64(out long longValue) && longValue != 0)
                                        {
                                            return ($"{property.Name}[0].{propertyName}", longValue.ToString());
                                        }
                                        else if (idElement.TryGetDouble(out double doubleValue) && doubleValue != 0.0)
                                        {
                                            return ($"{property.Name}[0].{propertyName}", doubleValue.ToString());
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Extract array name from path (e.g., "AbuttingRoad[0].prop" -> "AbuttingRoad")
        /// Handles nested paths like "plotData.AbuttingRoad[0].prop" -> "AbuttingRoad"
        /// </summary>
        private string? ExtractArrayNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Match pattern like "ArrayName[index]" or "ArrayName[index].property"
            // This will match the last array pattern in the path (for nested paths)
            var match = Regex.Match(path, @"([^\[\]\.]+)\[\d+\]");
            if (match.Success)
            {
                // Get the last match (for nested paths like "plotData.AbuttingRoad[0]")
                var matches = Regex.Matches(path, @"([^\[\]\.]+)\[\d+\]");
                if (matches.Count > 0)
                {
                    return matches[matches.Count - 1].Groups[1].Value;
                }
                return match.Groups[1].Value;
            }

            return null;
        }

        /// <summary>
        /// Construct entityIDKey from array name (e.g., "AbuttingRoad" -> "AbuttingRoadID")
        /// </summary>
        private string? ConstructEntityIdKey(string? arrayName)
        {
            if (string.IsNullOrEmpty(arrayName))
                return null;

            return arrayName + "ID";
        }

        /// <summary>
        /// Extract entityID from array element using the entityIDKey
        /// </summary>
        private (string? EntityIdKey, string? EntityIdValue) ExtractEntityIdFromArrayElement(JsonElement arrayElement, string? entityIdKey)
        {
            if (string.IsNullOrEmpty(entityIdKey) || arrayElement.ValueKind != JsonValueKind.Object)
                return (null, null);

            if (arrayElement.TryGetProperty(entityIdKey, out JsonElement idElement))
            {
                string? idValue = null;
                if (idElement.ValueKind == JsonValueKind.String)
                {
                    idValue = idElement.GetString();
                }
                else if (idElement.ValueKind == JsonValueKind.Number)
                {
                    if (idElement.TryGetInt64(out long longValue))
                    {
                        idValue = longValue.ToString();
                    }
                    else if (idElement.TryGetDouble(out double doubleValue))
                    {
                        idValue = doubleValue.ToString();
                    }
                }

                if (!string.IsNullOrEmpty(idValue) && idValue != "0")
                {
                    return (entityIdKey, idValue);
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Extract entityID and entityIDKey from path and array element
        /// </summary>
        private (string? EntityIdKey, string? EntityIdValue) ExtractEntityIdFromPath(JsonElement? arrayElement, string path)
        {
            if (arrayElement == null)
                return (null, null);

            string? arrayName = ExtractArrayNameFromPath(path);
            if (string.IsNullOrEmpty(arrayName))
                return (null, null);

            string? entityIdKey = ConstructEntityIdKey(arrayName);
            if (string.IsNullOrEmpty(entityIdKey))
                return (null, null);

            return ExtractEntityIdFromArrayElement(arrayElement.Value, entityIdKey);
        }

        /// <summary>
        /// Recursively compare JSON elements
        /// </summary>
        private void CompareJsonElements(JsonElement reference, JsonElement latest, string currentPath, List<PropertyDifference> differences, JsonElement? parentElement = null, JsonElement? arrayElement = null, List<string>? ignoredKeys = null)
        {
            try
            {
                // Check if this path should be ignored
                if (IsPathIgnored(currentPath, ignoredKeys))
                {
                    return;
                }

                // Handle different value kinds - this is a type change
                if (reference.ValueKind != latest.ValueKind)
                {
                    // Try to extract entityID from array element first, then fall back to general ID extraction
                    string? idKey = null;
                    string? idValue = null;
                    try
                    {
                        var entityIdInfo = ExtractEntityIdFromPath(arrayElement, currentPath);
                        var generalIdInfo = ExtractIdFromJson(parentElement ?? reference) ?? ExtractIdFromJson(latest);
                        idKey = entityIdInfo.EntityIdKey ?? generalIdInfo?.Key;
                        idValue = entityIdInfo.EntityIdValue ?? generalIdInfo?.Value;
                    }
                    catch
                    {
                        // Ignore ID extraction errors - they're optional
                    }

                    differences.Add(new PropertyDifference
                    {
                        Path = currentPath,
                        Type = DifferenceType.TypeChanged,
                        ReferenceValue = GetJsonValue(reference),
                        LatestValue = GetJsonValue(latest),
                        ReferenceType = GetJsonType(reference),
                        LatestType = GetJsonType(latest),
                        IdKey = idKey,
                        IdValue = idValue
                    });
                    return;
                }

                switch (reference.ValueKind)
                {
                    case JsonValueKind.Object:
                        CompareObjects(reference, latest, currentPath, differences, reference, arrayElement, ignoredKeys);
                        break;

                    case JsonValueKind.Array:
                        CompareArrays(reference, latest, currentPath, differences, ignoredKeys);
                        break;

                    default:
                        // Primitive values - check for type change within same ValueKind (e.g., float to int)
                        if (!AreValuesEqual(reference, latest))
                        {
                            // Try to extract entityID from array element first, then fall back to general ID extraction
                            string? idKey = null;
                            string? idValue = null;
                            try
                            {
                                var entityIdInfo = ExtractEntityIdFromPath(arrayElement, currentPath);
                                var generalIdInfo = ExtractIdFromJson(parentElement ?? reference) ?? ExtractIdFromJson(latest);
                                idKey = entityIdInfo.EntityIdKey ?? generalIdInfo?.Key;
                                idValue = entityIdInfo.EntityIdValue ?? generalIdInfo?.Value;
                            }
                            catch
                            {
                                // Ignore ID extraction errors - they're optional
                            }
                            
                            string refType = GetJsonType(reference);
                            string latType = GetJsonType(latest);
                            
                            // Check if it's a type change (e.g., float to int, both are numbers)
                            bool isTypeChange = refType != latType && 
                                               (reference.ValueKind == JsonValueKind.Number || 
                                                reference.ValueKind == JsonValueKind.String);
                            
                            differences.Add(new PropertyDifference
                            {
                                Path = currentPath,
                                Type = isTypeChange ? DifferenceType.TypeChanged : DifferenceType.Modified,
                                ReferenceValue = GetJsonValue(reference),
                                LatestValue = GetJsonValue(latest),
                                ReferenceType = refType,
                                LatestType = latType,
                                IdKey = idKey,
                                IdValue = idValue
                            });
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                // If an exception occurs during comparison, add it as a difference
                differences.Add(new PropertyDifference
                {
                    Path = currentPath,
                    Type = DifferenceType.TypeChanged,
                    ReferenceValue = $"Error: {ex.Message}",
                    LatestValue = null,
                    ReferenceType = "error",
                    LatestType = null
                });
            }
        }

        /// <summary>
        /// Compare JSON objects
        /// </summary>
        private void CompareObjects(JsonElement reference, JsonElement latest, string currentPath, List<PropertyDifference> differences, JsonElement? parentElement = null, JsonElement? arrayElement = null, List<string>? ignoredKeys = null)
        {
            // Ensure both are objects before trying to enumerate
            if (reference.ValueKind != JsonValueKind.Object || latest.ValueKind != JsonValueKind.Object)
            {
                // Type mismatch - add as difference
                differences.Add(new PropertyDifference
                {
                    Path = currentPath,
                    Type = DifferenceType.TypeChanged,
                    ReferenceValue = GetJsonValue(reference),
                    LatestValue = GetJsonValue(latest),
                    ReferenceType = GetJsonType(reference),
                    LatestType = GetJsonType(latest)
                });
                return;
            }

            var referenceProps = reference.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            var latestProps = latest.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

            // Try to extract entityID from array element first, then fall back to general ID extraction
            string? objectIdKey = null;
            string? objectIdValue = null;
            try
            {
                var entityIdInfo = ExtractEntityIdFromPath(arrayElement, currentPath);
                var generalIdInfo = ExtractIdFromJson(reference) ?? ExtractIdFromJson(latest);
                objectIdKey = entityIdInfo.EntityIdKey ?? generalIdInfo?.Key;
                objectIdValue = entityIdInfo.EntityIdValue ?? generalIdInfo?.Value;
            }
            catch
            {
                // Ignore ID extraction errors - they're optional
            }

            // Find properties in reference but not in latest (removed)
            foreach (var refProp in referenceProps)
            {
                if (!latestProps.ContainsKey(refProp.Key))
                {
                    string propPath = string.IsNullOrEmpty(currentPath) ? refProp.Key : $"{currentPath}.{refProp.Key}";
                    
                    // Skip if this path should be ignored
                    if (IsPathIgnored(propPath, ignoredKeys))
                    {
                        continue;
                    }
                    
                    // For nested properties in array elements, try to extract entityID from the array element
                    var propEntityIdInfo = ExtractEntityIdFromPath(arrayElement, propPath);
                    var propIdKey = propEntityIdInfo.EntityIdKey ?? objectIdKey;
                    var propIdValue = propEntityIdInfo.EntityIdValue ?? objectIdValue;

                    differences.Add(new PropertyDifference
                    {
                        Path = propPath,
                        Type = DifferenceType.Removed,
                        ReferenceValue = GetJsonValue(refProp.Value),
                        LatestValue = null,
                        IdKey = propIdKey,
                        IdValue = propIdValue
                    });
                }
            }

            // Find properties in latest but not in reference (added)
            foreach (var latProp in latestProps)
            {
                if (!referenceProps.ContainsKey(latProp.Key))
                {
                    string propPath = string.IsNullOrEmpty(currentPath) ? latProp.Key : $"{currentPath}.{latProp.Key}";
                    
                    // Skip if this path should be ignored
                    if (IsPathIgnored(propPath, ignoredKeys))
                    {
                        continue;
                    }
                    
                    // For nested properties in array elements, try to extract entityID from the array element
                    string? propIdKey = objectIdKey;
                    string? propIdValue = objectIdValue;
                    try
                    {
                        var propEntityIdInfo = ExtractEntityIdFromPath(arrayElement, propPath);
                        propIdKey = propEntityIdInfo.EntityIdKey ?? objectIdKey;
                        propIdValue = propEntityIdInfo.EntityIdValue ?? objectIdValue;
                    }
                    catch
                    {
                        // Ignore ID extraction errors - use parent IDs
                    }

                    differences.Add(new PropertyDifference
                    {
                        Path = propPath,
                        Type = DifferenceType.Added,
                        ReferenceValue = null,
                        LatestValue = GetJsonValue(latProp.Value),
                        IdKey = propIdKey,
                        IdValue = propIdValue
                    });
                }
            }

            // Compare common properties - pass the current object as parent for nested properties
            // Also pass arrayElement if we're inside an array element
            foreach (var refProp in referenceProps)
            {
                if (latestProps.TryGetValue(refProp.Key, out JsonElement latValue))
                {
                    string newPath = string.IsNullOrEmpty(currentPath) ? refProp.Key : $"{currentPath}.{refProp.Key}";
                    CompareJsonElements(refProp.Value, latValue, newPath, differences, reference, arrayElement, ignoredKeys);
                }
            }
        }

        /// <summary>
        /// Compare JSON arrays
        /// </summary>
        private void CompareArrays(JsonElement reference, JsonElement latest, string currentPath, List<PropertyDifference> differences, List<string>? ignoredKeys = null)
        {
            var refArray = reference.EnumerateArray().ToList();
            var latArray = latest.EnumerateArray().ToList();

            int maxLength = Math.Max(refArray.Count, latArray.Count);

            for (int i = 0; i < maxLength; i++)
            {
                string arrayPath = $"{currentPath}[{i}]";

                if (i >= refArray.Count)
                {
                    // Added element - check if entire array path should be ignored
                    if (!IsPathIgnored(arrayPath, ignoredKeys))
                    {
                        // Try to extract entityID from array element first
                        var entityIdInfo = ExtractEntityIdFromPath(latArray[i], arrayPath);
                        var generalIdInfo = ExtractIdFromJson(latArray[i]);
                        var idKey = entityIdInfo.EntityIdKey ?? generalIdInfo?.Key;
                        var idValue = entityIdInfo.EntityIdValue ?? generalIdInfo?.Value;

                        differences.Add(new PropertyDifference
                        {
                            Path = arrayPath,
                            Type = DifferenceType.Added,
                            ReferenceValue = null,
                            LatestValue = GetJsonValue(latArray[i]),
                            IdKey = idKey,
                            IdValue = idValue,
                            IsArrayItemAdded = true
                        });
                    }
                }
                else if (i >= latArray.Count)
                {
                    // Removed element - check if entire array path should be ignored
                    if (!IsPathIgnored(arrayPath, ignoredKeys))
                    {
                        // Try to extract entityID from array element first
                        var entityIdInfo = ExtractEntityIdFromPath(refArray[i], arrayPath);
                        var generalIdInfo = ExtractIdFromJson(refArray[i]);
                        var idKey = entityIdInfo.EntityIdKey ?? generalIdInfo?.Key;
                        var idValue = entityIdInfo.EntityIdValue ?? generalIdInfo?.Value;

                        differences.Add(new PropertyDifference
                        {
                            Path = arrayPath,
                            Type = DifferenceType.Removed,
                            ReferenceValue = GetJsonValue(refArray[i]),
                            LatestValue = null,
                            IdKey = idKey,
                            IdValue = idValue,
                            IsArrayItemRemoved = true
                        });
                    }
                }
                else
                {
                    // Compare elements at same index - pass the array element as parent for nested properties
                    // Also pass the array element itself so we can extract entityID from it
                    CompareJsonElements(refArray[i], latArray[i], arrayPath, differences, refArray[i], refArray[i], ignoredKeys);
                }
            }
        }

        /// <summary>
        /// Check if two JSON values are equal
        /// </summary>
        private bool AreValuesEqual(JsonElement reference, JsonElement latest)
        {
            if (reference.ValueKind != latest.ValueKind)
                return false;

            switch (reference.ValueKind)
            {
                case JsonValueKind.String:
                    return reference.GetString() == latest.GetString();
                case JsonValueKind.Number:
                    // Compare as strings to handle precision
                    return reference.GetRawText() == latest.GetRawText();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return reference.GetBoolean() == latest.GetBoolean();
                case JsonValueKind.Null:
                    return true;
                default:
                    return reference.GetRawText() == latest.GetRawText();
            }
        }

        /// <summary>
        /// Get the type name of a JsonElement
        /// </summary>
        private string GetJsonType(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                // Check if it's an integer or float
                if (element.TryGetInt64(out _))
                    return "int";
                if (element.TryGetDouble(out _))
                    return "float";
                return "number";
            }
            
            return element.ValueKind switch
            {
                JsonValueKind.String => "string",
                JsonValueKind.True => "bool",
                JsonValueKind.False => "bool",
                JsonValueKind.Null => "null",
                JsonValueKind.Object => "object",
                JsonValueKind.Array => "array",
                _ => "unknown"
            };
        }

        /// <summary>
        /// Get a readable value from JsonElement
        /// </summary>
        private object? GetJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Object => SerializeJsonElement(element),
                JsonValueKind.Array => SerializeJsonElement(element),
                _ => element.GetRawText()
            };
        }

        /// <summary>
        /// Serialize JsonElement to a dictionary or array
        /// </summary>
        private object? SerializeJsonElement(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = GetJsonValue(prop.Value);
                }
                return dict;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(GetJsonValue(item));
                }
                return list;
            }
            return null;
        }

        /// <summary>
        /// Extract base filename by removing timestamp pattern
        /// Pattern: DrawingName_YYYYMMDD_HHMMSS_mmm.json -> DrawingName
        /// </summary>
        private string GetBaseFileName(string fullPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(fullPath);
            
            // Remove timestamp pattern: _YYYYMMDD_HHMMSS_mmm
            var match = Regex.Match(fileName, @"^(.+?)_\d{8}_\d{6}_\d{3}$");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            
            return fileName;
        }

        /// <summary>
        /// Check if a JSON path should be ignored based on the ignored keys configuration
        /// 
        /// Supports three matching patterns:
        /// 1. Key name only (e.g., "AccessoryDeductibleArea") - matches the key anywhere in the JSON structure
        ///    Examples: "AccessoryDeductibleArea", "Accessory[0].AccessoryDeductibleArea", "Accessory[2].AccessoryDeductibleArea", "plotData.AccessoryDeductibleArea"
        /// 
        /// 2. Wildcard array pattern (e.g., "Accessory[].AccessoryDeductibleArea") - matches the key only within the specified array
        ///    Examples: "Accessory[0].AccessoryDeductibleArea", "Accessory[1].AccessoryDeductibleArea", "Accessory[2].AccessoryDeductibleArea"
        ///    Does NOT match: "AccessoryDeductibleArea" (root level), "plotData.AccessoryDeductibleArea"
        /// 
        /// 3. Full path (e.g., "Accessory[2].AccessoryDeductibleArea") - matches only that exact path
        ///    Example: Only "Accessory[2].AccessoryDeductibleArea"
        /// </summary>
        /// <param name="path">The JSON path to check (e.g., "plotData.timestamp", "Accessory[0].AccessoryDeductibleArea")</param>
        /// <param name="ignoredKeys">List of paths to ignore</param>
        /// <returns>True if the path should be ignored, false otherwise</returns>
        private bool IsPathIgnored(string path, List<string>? ignoredKeys)
        {
            if (ignoredKeys == null || ignoredKeys.Count == 0 || string.IsNullOrEmpty(path))
            {
                return false;
            }

            foreach (var ignoredKey in ignoredKeys)
            {
                if (string.IsNullOrEmpty(ignoredKey))
                {
                    continue;
                }

                // 1. Exact match (handles full paths like "Accessory[2].AccessoryDeductibleArea")
                if (path.Equals(ignoredKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 2. Handle wildcard array patterns: "ArrayName[].property" matches "ArrayName[0].property", "ArrayName[1].property", etc.
                if (ignoredKey.Contains("[]"))
                {
                    // Replace "[]" with a regex pattern that matches any array index
                    string pattern = Regex.Escape(ignoredKey).Replace(@"\[\]", @"\[\d+\]");
                    if (Regex.IsMatch(path, "^" + pattern + "$", RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }

                // 3. Key name only - check if path ends with the ignored key (matches anywhere in structure)
                // e.g., "AccessoryDeductibleArea" matches "AccessoryDeductibleArea", "Accessory[0].AccessoryDeductibleArea", etc.
                int index = path.LastIndexOf(ignoredKey, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    // Check if it's at the start or preceded by a dot or bracket
                    bool isValidStart = index == 0 || path[index - 1] == '.' || path[index - 1] == '[';
                    // Check if it's at the end or followed by a dot or bracket
                    int endIndex = index + ignoredKey.Length;
                    bool isValidEnd = endIndex >= path.Length || path[endIndex] == '.' || path[endIndex] == '[' || path[endIndex] == ']';
                    
                    if (isValidStart && isValidEnd)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

