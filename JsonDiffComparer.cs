using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BatchProcessor.Models;

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
        public DiffReport CompareFolders(string referenceFolder, string latestFolder, string? outputFolder = null, bool createDiffFiles = false, string diffSubfolderName = "diff")
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
                    result = CompareFiles(referencePath!, latestPath!);

                    // Create diff file if differences found and output folder is specified
                    if (createDiffFiles && !result.FilesMatch && result.Differences != null && result.Differences.Count > 0 && diffSubfolder != null)
                    {
                        try
                        {
                            string diffFileName = $"{baseName}_diff.json";
                            string diffFilePath = Path.Combine(diffSubfolder, diffFileName);

                            var options = new JsonSerializerOptions { WriteIndented = true };
                            string diffJson = JsonSerializer.Serialize(result, options);
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
        /// Compare two JSON files
        /// </summary>
        public JsonDiffResult CompareFiles(string referencePath, string latestPath)
        {
            var result = new JsonDiffResult
            {
                FileName = Path.GetFileName(referencePath)
            };

            try
            {
                string referenceJson = File.ReadAllText(referencePath);
                string latestJson = File.ReadAllText(latestPath);

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

                var differences = new List<PropertyDifference>();
                CompareJsonElements(referenceDoc.RootElement, latestDoc.RootElement, "", differences, null);

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
        /// Compare two JSON files and create a diff file in a subfolder if differences are found
        /// </summary>
        /// <param name="referencePath">Path to reference/baseline JSON file</param>
        /// <param name="latestPath">Path to latest/current JSON file</param>
        /// <param name="outputFolder">Output folder where diff subfolder will be created</param>
        /// <param name="diffSubfolderName">Name of the subfolder for diff files (default: "diff")</param>
        /// <returns>Path to the created diff file, or null if no differences found or error occurred</returns>
        public string? CompareFilesAndCreateDiff(string referencePath, string latestPath, string outputFolder, string diffSubfolderName = "diff")
        {
            try
            {
                // Compare the files
                var diffResult = CompareFiles(referencePath, latestPath);

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
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string diffJson = JsonSerializer.Serialize(diffResult, options);
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
        private void CompareJsonElements(JsonElement reference, JsonElement latest, string currentPath, List<PropertyDifference> differences, JsonElement? parentElement = null, JsonElement? arrayElement = null)
        {
            try
            {
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
                        CompareObjects(reference, latest, currentPath, differences, reference, arrayElement);
                        break;

                    case JsonValueKind.Array:
                        CompareArrays(reference, latest, currentPath, differences);
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
        private void CompareObjects(JsonElement reference, JsonElement latest, string currentPath, List<PropertyDifference> differences, JsonElement? parentElement = null, JsonElement? arrayElement = null)
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
                    CompareJsonElements(refProp.Value, latValue, newPath, differences, reference, arrayElement);
                }
            }
        }

        /// <summary>
        /// Compare JSON arrays
        /// </summary>
        private void CompareArrays(JsonElement reference, JsonElement latest, string currentPath, List<PropertyDifference> differences)
        {
            var refArray = reference.EnumerateArray().ToList();
            var latArray = latest.EnumerateArray().ToList();

            int maxLength = Math.Max(refArray.Count, latArray.Count);

            for (int i = 0; i < maxLength; i++)
            {
                string arrayPath = $"{currentPath}[{i}]";

                if (i >= refArray.Count)
                {
                    // Added element - try to extract entityID from array element first
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
                else if (i >= latArray.Count)
                {
                    // Removed element - try to extract entityID from array element first
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
                else
                {
                    // Compare elements at same index - pass the array element as parent for nested properties
                    // Also pass the array element itself so we can extract entityID from it
                    CompareJsonElements(refArray[i], latArray[i], arrayPath, differences, refArray[i], refArray[i]);
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
    }
}

