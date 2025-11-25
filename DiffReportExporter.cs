using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BatchProcessor.Models;

namespace BatchProcessor
{
    public class DiffReportExporter
    {
        /// <summary>
        /// Save diff report to output folder (JSON and Text formats)
        /// Returns the path to the saved JSON file
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
            string baseFileName = $"diff_report_{timestamp}";

            // Save JSON format
            string jsonPath = Path.Combine(outputFolder, $"{baseFileName}.json");
            ExportToJson(report, jsonPath);

            // Save Text format
            string textPath = Path.Combine(outputFolder, $"{baseFileName}.txt");
            ExportToText(report, textPath);

            return jsonPath;
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

