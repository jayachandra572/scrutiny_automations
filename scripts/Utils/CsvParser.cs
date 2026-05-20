using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BatchProcessor.Utils
{
    /// <summary>
    /// Shared CSV parsing and drawing filename matching utilities.
    /// Used by CsvParameterMapper, WorkloadMapCsvReader, and any future CSV-based readers.
    /// </summary>
    public static class CsvParser
    {
        /// <summary>
        /// Parse entire CSV content handling RFC 4180 quoted values and multiline cells.
        /// Returns rows as a list of string lists (row 0 is the header).
        /// </summary>
        public static List<List<string>> ParseCsvRows(string content)
        {
            var rows = new List<List<string>>();
            var currentRow = new List<string>();
            var currentCell = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                char nextChar = (i + 1 < content.Length) ? content[i + 1] : '\0';

                if (c == '"')
                {
                    if (inQuotes && nextChar == '"')
                    {
                        // Escaped quote — add one quote and skip next
                        currentCell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    currentRow.Add(currentCell.ToString().Trim());
                    currentCell.Clear();
                }
                else if ((c == '\r' || c == '\n') && !inQuotes)
                {
                    if (c == '\r' && nextChar == '\n')
                        i++; // skip \n in \r\n

                    if (currentCell.Length > 0 || currentRow.Count > 0)
                    {
                        currentRow.Add(currentCell.ToString().Trim());
                        currentCell.Clear();

                        if (currentRow.Count > 0)
                        {
                            rows.Add(currentRow);
                            currentRow = new List<string>();
                        }
                    }
                }
                else
                {
                    currentCell.Append(c);
                }
            }

            // Add final cell/row if any content remains
            if (currentCell.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentCell.ToString().Trim());
                if (currentRow.Count > 0)
                    rows.Add(currentRow);
            }

            return rows;
        }

        /// <summary>
        /// Find a value in a string-keyed dictionary using multi-strategy filename matching.
        /// Strategies (in order): exact with extension, without extension, case-insensitive, partial.
        /// Returns null if no match is found.
        /// </summary>
        public static string FindValueForDrawing(Dictionary<string, string> map, string drawingPath)
        {
            string filename = Path.GetFileName(drawingPath);
            string filenameWithoutExt = Path.GetFileNameWithoutExtension(drawingPath);

            if (map.TryGetValue(filename, out string value))
                return value;

            if (map.TryGetValue(filenameWithoutExt, out value))
                return value;

            // Case-insensitive
            var key = map.Keys.FirstOrDefault(k =>
                k.Equals(filename, StringComparison.OrdinalIgnoreCase) ||
                k.Equals(filenameWithoutExt, StringComparison.OrdinalIgnoreCase));

            if (key != null && map.TryGetValue(key, out value))
                return value;

            // Partial match
            key = map.Keys.FirstOrDefault(k =>
                filename.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                k.Contains(filenameWithoutExt, StringComparison.OrdinalIgnoreCase));

            if (key != null && map.TryGetValue(key, out value))
                return value;

            return null;
        }

        /// <summary>
        /// Find the index of the filename column using the standard priority order:
        /// "Marking File Link" > "MarkingFileLink" > "Filename" > "File" > "Drawing" > column 0.
        /// Logs to Console with the standard emoji prefix pattern.
        /// </summary>
        public static int FindFilenameColumnIndex(List<string> headers)
        {
            int index = headers.FindIndex(h =>
                h.Equals("Marking File Link", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("MarkingFileLink", StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                Console.WriteLine($"✅ Found filename column: 'Marking File Link' (index {index})");
                return index;
            }

            index = headers.FindIndex(h =>
                h.Equals("DrawingFile Name", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Drawing File Name", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("DrawingFileName", StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                Console.WriteLine($"✅ Found filename column: '{headers[index]}' (index {index})");
                return index;
            }

            index = headers.FindIndex(h =>
                h.Equals("Filename", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("File", StringComparison.OrdinalIgnoreCase) ||
                h.Equals("Drawing", StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                Console.WriteLine($"⚠️  Warning: Using '{headers[index]}' instead of 'Marking File Link'");
                return index;
            }

            Console.WriteLine("⚠️  Warning: No 'Marking File Link' column found, using first column");
            return 0;
        }

        /// <summary>
        /// Build a lookup dictionary from a parsed CSV with multiple key variations per row
        /// so that DrawingFileMatcher can match by filename with or without extension.
        /// Returns: key → column-value dictionary for each row.
        /// </summary>
        public static Dictionary<string, Dictionary<string, string>> BuildParameterMap(
            List<string> headers,
            List<List<string>> dataRows,
            int filenameColumnIndex)
        {
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var values in dataRows)
            {
                if (values.Count == 0 || filenameColumnIndex >= values.Count
                    || string.IsNullOrWhiteSpace(values[filenameColumnIndex]))
                    continue;

                string markingFileLink = values[filenameColumnIndex].Trim();
                string filenameWithoutExt = Path.GetFileNameWithoutExtension(markingFileLink);
                string filenameWithExt = markingFileLink.Contains('.')
                    ? markingFileLink
                    : markingFileLink + ".dwg";

                var parameters = new Dictionary<string, string>();
                for (int j = 0; j < Math.Min(headers.Count, values.Count); j++)
                    parameters[headers[j]] = values[j];

                map[filenameWithExt] = parameters;
                map[filenameWithoutExt] = parameters;
                map[Path.GetFileName(filenameWithExt)] = parameters;
                map[Path.GetFileName(filenameWithoutExt)] = parameters;
            }

            return map;
        }
    }
}
