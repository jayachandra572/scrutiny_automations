using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BatchProcessor.JsonDiff.Models;

namespace BatchProcessor.JsonDiff
{
    public class DiffReportExporter
    {
        /// <summary>
        /// Save individual diff files to output folder
        /// Creates a subfolder for individual file diff logs
        /// Returns the path to the subfolder containing the diff files
        /// </summary>
        public string SaveDiffReport(DiffReport report, string outputFolder, bool saveOnlyIfDifferent = true)
        {
            if (saveOnlyIfDifferent)
            {
                bool hasDifferences = report.DifferentFiles > 0 || 
                                     report.MissingInLatest > 0 || 
                                     report.MissingInReference > 0;
                
                if (!hasDifferences)
                {
                    return string.Empty; // No differences, don't save
                }
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Create subfolder for individual file diff logs
            string diffLogsSubfolder = Path.Combine(outputFolder, $"diff_logs_{timestamp}");
            Directory.CreateDirectory(diffLogsSubfolder);

            // Save individual file diff logs in subfolder (JSON format only)
            SaveIndividualFileDiffLogs(report, diffLogsSubfolder);

            // Return the subfolder path instead of a summary file path
            return diffLogsSubfolder;
        }

        /// <summary>
        /// Save individual file diff logs in the subfolder (JSON format only)
        /// Returns the number of files saved
        /// </summary>
        private int SaveIndividualFileDiffLogs(DiffReport report, string subfolder)
        {
            var filesWithDifferences = report.FileResults
                .Where(r => !r.FilesMatch && !r.IsMissingInLatest && !r.IsMissingInReference)
                .ToList();

            int filesSaved = 0;
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            foreach (var result in filesWithDifferences)
            {
                if (result.Differences.Count == 0)
                    continue;

                // Create safe filename (remove invalid characters)
                string safeFileName = Path.GetFileNameWithoutExtension(result.FileName);
                string fileName = $"{safeFileName}_diff.json";

                // Remove invalid path characters
                char[] invalidChars = Path.GetInvalidFileNameChars();
                foreach (char c in invalidChars)
                {
                    fileName = fileName.Replace(c, '_');
                }

                string filePath = Path.Combine(subfolder, fileName);

                // Build differences array in the new format
                var differences = new List<object>();

                foreach (var diff in result.Differences)
                {
                    // Convert DifferenceType to string
                    string typeString = diff.Type switch
                    {
                        DifferenceType.Added => "Added",
                        DifferenceType.Removed => "Removed",
                        DifferenceType.Modified => "Modified",
                        DifferenceType.TypeChanged => "TypeChanged",
                        _ => "Unknown"
                    };

                    // Build the difference object
                    var diffObj = new Dictionary<string, object?>
                    {
                        ["path"] = diff.Path,
                        ["type"] = typeString
                    };

                    // Add actualValue (ReferenceValue) and modifiedValue (LatestValue)
                    if (diff.ReferenceValue != null)
                    {
                        diffObj["actualValue"] = diff.ReferenceValue;
                    }
                    if (diff.LatestValue != null)
                    {
                        diffObj["modifiedValue"] = diff.LatestValue;
                    }

                    // Add entityID and entityIDKey if available
                    if (!string.IsNullOrEmpty(diff.IdValue))
                    {
                        // Try to parse as number if possible, otherwise keep as string
                        if (long.TryParse(diff.IdValue, out long longId))
                        {
                            diffObj["entityID"] = longId;
                        }
                        else if (double.TryParse(diff.IdValue, out double doubleId))
                        {
                            diffObj["entityID"] = doubleId;
                        }
                        else
                        {
                            diffObj["entityID"] = diff.IdValue;
                        }
                    }

                    if (!string.IsNullOrEmpty(diff.IdKey))
                    {
                        diffObj["entityIDKey"] = diff.IdKey;
                    }

                    differences.Add(diffObj);
                }

                // Build the output JSON structure
                var output = new Dictionary<string, object>
                {
                    ["differences"] = differences
                };

                string json = JsonSerializer.Serialize(output, options);
                File.WriteAllText(filePath, json);
                filesSaved++;
            }

            return filesSaved;
        }

        /// <summary>
        /// Format path to root['key'][index]['property'] format with ID if available
        /// </summary>
        private string FormatPath(string path, string? idValue)
        {
            if (string.IsNullOrEmpty(path))
                return "root";

            // Convert dot notation to bracket notation
            // e.g., "Building[0].MinClearanceBetweenBuildingAndTotLot" -> "root['Building'][0]['MinClearanceBetweenBuildingAndTotLot']"
            string formatted = "root";
            
            // Use regex to split by dots and brackets while preserving them
            var parts = System.Text.RegularExpressions.Regex.Split(path, @"(\[|\]|\.)");
            
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;
                    
                if (part == ".")
                {
                    formatted += "']['";
                }
                else if (part == "[")
                {
                    formatted += "'][";
                }
                else if (part == "]")
                {
                    formatted += "]";
                }
                else
                {
                    // Check if it's a number (array index) or a property name
                    if (int.TryParse(part, out _))
                    {
                        formatted += part;
                    }
                    else
                    {
                        if (!formatted.EndsWith("'") && !formatted.EndsWith("]"))
                        {
                            formatted += "']['";
                        }
                        formatted += part;
                    }
                }
            }
            
            if (!formatted.EndsWith("']") && !formatted.EndsWith("]"))
            {
                formatted += "']";
            }

            // Add ID if available
            if (!string.IsNullOrEmpty(idValue))
            {
                formatted += $" (ID: {idValue})";
            }

            return formatted;
        }

        /// <summary>
        /// Export diff report to JSON format
        /// </summary>
        public void ExportToJson(DiffReport report, string outputPath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string json = JsonSerializer.Serialize(report, options);
            File.WriteAllText(outputPath, json);
        }

        /// <summary>
        /// Export diff report to human-readable text format
        /// </summary>
        public void ExportToText(DiffReport report, string outputPath)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("JSON Diff Comparison Report");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Comparison Date: {report.ComparisonDate:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Reference Folder: {report.ReferenceFolder}");
            sb.AppendLine($"Latest Folder: {report.LatestFolder}");
            sb.AppendLine();

            // Summary
            sb.AppendLine("Summary:");
            sb.AppendLine($"  Total Files: {report.TotalFiles}");
            sb.AppendLine($"  Matching Files: {report.MatchingFiles}");
            sb.AppendLine($"  Different Files: {report.DifferentFiles}");
            sb.AppendLine($"  Missing in Latest: {report.MissingInLatest}");
            sb.AppendLine($"  Missing in Reference: {report.MissingInReference}");
            sb.AppendLine();

            // Files with differences
            var filesWithDifferences = report.FileResults
                .Where(r => !r.FilesMatch && !r.IsMissingInLatest && !r.IsMissingInReference)
                .ToList();

            if (filesWithDifferences.Any())
            {
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine("DIFFERENCES FOUND");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine();

                foreach (var result in filesWithDifferences)
                {
                    sb.AppendLine($"📄 {result.FileName}");
                    if (!string.IsNullOrEmpty(result.Id))
                    {
                        sb.AppendLine($"   ID: {result.Id}");
                    }
                    sb.AppendLine("───────────────────────────────────────────────────────────────");

                    foreach (var diff in result.Differences)
                    {
                        switch (diff.Type)
                        {
                            case DifferenceType.Modified:
                                sb.AppendLine($"  MODIFIED: {diff.Path}");
                                sb.AppendLine($"    Reference: {FormatValue(diff.ReferenceValue)}");
                                sb.AppendLine($"    Latest:    {FormatValue(diff.LatestValue)}");
                                break;

                            case DifferenceType.Added:
                                sb.AppendLine($"  ADDED: {diff.Path}");
                                sb.AppendLine($"    Latest: {FormatValue(diff.LatestValue)}");
                                break;

                            case DifferenceType.Removed:
                                sb.AppendLine($"  REMOVED: {diff.Path}");
                                sb.AppendLine($"    Reference: {FormatValue(diff.ReferenceValue)}");
                                break;
                        }
                        sb.AppendLine();
                    }
                }
            }

            // Missing files
            var missingInLatest = report.FileResults.Where(r => r.IsMissingInLatest).ToList();
            if (missingInLatest.Any())
            {
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine("FILES MISSING IN LATEST");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                foreach (var result in missingInLatest)
                {
                    sb.AppendLine($"  - {result.FileName}");
                }
                sb.AppendLine();
            }

            var missingInReference = report.FileResults.Where(r => r.IsMissingInReference).ToList();
            if (missingInReference.Any())
            {
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine("FILES MISSING IN REFERENCE");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                foreach (var result in missingInReference)
                {
                    sb.AppendLine($"  - {result.FileName}");
                }
                sb.AppendLine();
            }

            // Files with errors
            var filesWithErrors = report.FileResults.Where(r => !string.IsNullOrEmpty(r.ErrorMessage)).ToList();
            if (filesWithErrors.Any())
            {
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine("FILES WITH ERRORS");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                foreach (var result in filesWithErrors)
                {
                    sb.AppendLine($"  - {result.FileName}: {result.ErrorMessage}");
                }
                sb.AppendLine();
            }

            if (report.DifferentFiles == 0 && report.MissingInLatest == 0 && report.MissingInReference == 0)
            {
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
                sb.AppendLine("✅ NO DIFFERENCES FOUND - All files match!");
                sb.AppendLine("═══════════════════════════════════════════════════════════════");
            }

            File.WriteAllText(outputPath, sb.ToString());
        }

        private string FormatValue(object? value)
        {
            if (value == null)
                return "null";

            if (value is string str)
                return $"\"{str}\"";

            if (value is decimal dec)
                return dec.ToString("F2");

            return value.ToString() ?? "null";
        }
    }
}

