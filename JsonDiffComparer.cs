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
        public DiffReport CompareFolders(string referenceFolder, string latestFolder, string? outputFolder = null)
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

                var differences = new List<PropertyDifference>();
                CompareJsonElements(referenceDoc.RootElement, latestDoc.RootElement, "", differences);

                result.Differences = differences;
                result.FilesMatch = differences.Count == 0;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.FilesMatch = false;
            }

            return result;
        }

        /// <summary>
        /// Recursively compare JSON elements
        /// </summary>
        private void CompareJsonElements(JsonElement reference, JsonElement latest, string currentPath, List<PropertyDifference> differences)
        {
            // Handle different value kinds
            if (reference.ValueKind != latest.ValueKind)
            {
                differences.Add(new PropertyDifference
                {
                    Path = currentPath,
                    Type = DifferenceType.Modified,
                    ReferenceValue = GetJsonValue(reference),
                    LatestValue = GetJsonValue(latest)
                });
                return;
            }

            switch (reference.ValueKind)
            {
                case JsonValueKind.Object:
                    CompareObjects(reference, latest, currentPath, differences);
                    break;

                case JsonValueKind.Array:
                    CompareArrays(reference, latest, currentPath, differences);
                    break;

                default:
                    // Primitive values
                    if (!AreValuesEqual(reference, latest))
                    {
                        differences.Add(new PropertyDifference
                        {
                            Path = currentPath,
                            Type = DifferenceType.Modified,
                            ReferenceValue = GetJsonValue(reference),
                            LatestValue = GetJsonValue(latest)
                        });
                    }
                    break;
            }
        }

        /// <summary>
        /// Compare JSON objects
        /// </summary>
        private void CompareObjects(JsonElement reference, JsonElement latest, string currentPath, List<PropertyDifference> differences)
        {
            var referenceProps = reference.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
            var latestProps = latest.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

            // Find properties in reference but not in latest (removed)
            foreach (var refProp in referenceProps)
            {
                if (!latestProps.ContainsKey(refProp.Key))
                {
                    differences.Add(new PropertyDifference
                    {
                        Path = string.IsNullOrEmpty(currentPath) ? refProp.Key : $"{currentPath}.{refProp.Key}",
                        Type = DifferenceType.Removed,
                        ReferenceValue = GetJsonValue(refProp.Value),
                        LatestValue = null
                    });
                }
            }

            // Find properties in latest but not in reference (added)
            foreach (var latProp in latestProps)
            {
                if (!referenceProps.ContainsKey(latProp.Key))
                {
                    differences.Add(new PropertyDifference
                    {
                        Path = string.IsNullOrEmpty(currentPath) ? latProp.Key : $"{currentPath}.{latProp.Key}",
                        Type = DifferenceType.Added,
                        ReferenceValue = null,
                        LatestValue = GetJsonValue(latProp.Value)
                    });
                }
            }

            // Compare common properties
            foreach (var refProp in referenceProps)
            {
                if (latestProps.TryGetValue(refProp.Key, out JsonElement latValue))
                {
                    string newPath = string.IsNullOrEmpty(currentPath) ? refProp.Key : $"{currentPath}.{refProp.Key}";
                    CompareJsonElements(refProp.Value, latValue, newPath, differences);
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
                    // Added element
                    differences.Add(new PropertyDifference
                    {
                        Path = arrayPath,
                        Type = DifferenceType.Added,
                        ReferenceValue = null,
                        LatestValue = GetJsonValue(latArray[i])
                    });
                }
                else if (i >= latArray.Count)
                {
                    // Removed element
                    differences.Add(new PropertyDifference
                    {
                        Path = arrayPath,
                        Type = DifferenceType.Removed,
                        ReferenceValue = GetJsonValue(refArray[i]),
                        LatestValue = null
                    });
                }
                else
                {
                    // Compare elements at same index
                    CompareJsonElements(refArray[i], latArray[i], arrayPath, differences);
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
                JsonValueKind.Object => "[Object]",
                JsonValueKind.Array => "[Array]",
                _ => element.GetRawText()
            };
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

