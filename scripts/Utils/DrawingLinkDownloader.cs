using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BatchProcessor.Utils
{
    /// <summary>
    /// A download link paired with the local file name it will be saved under.
    /// </summary>
    public record DrawingLink(string Url, string FileName);

    /// <summary>
    /// Reads a links file (one URL per line, '#' for comments) and downloads the drawings.
    /// File names are resolved up front and deterministically, so the names used to
    /// validate CSV mappings are exactly the names the files are downloaded as.
    /// </summary>
    public static class DrawingLinkDownloader
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        /// <summary>
        /// Reads the links file and resolves a unique local file name for every link.
        /// Throws when the file can't be read or holds no links.
        /// </summary>
        public static List<DrawingLink> ReadLinks(string linksFile)
        {
            List<string> urls;
            try
            {
                urls = File.ReadAllLines(linksFile)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to read links file: {ex.Message}");
            }

            if (urls.Count == 0)
                throw new Exception("No valid download links found in the links file");

            // Two links can resolve to the same name - keep both instead of overwriting
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var links = new List<DrawingLink>(urls.Count);
            for (int i = 0; i < urls.Count; i++)
            {
                string fileName = GetFileNameFromLink(urls[i], i + 1);
                string candidate = fileName;
                int suffix = 1;
                while (!used.Add(candidate))
                {
                    candidate = $"{Path.GetFileNameWithoutExtension(fileName)}_{suffix}{Path.GetExtension(fileName)}";
                    suffix++;
                }
                links.Add(new DrawingLink(urls[i], candidate));
            }

            return links;
        }

        /// <summary>
        /// Derives a safe local .dwg file name from a download URL, falling back to a
        /// numbered name when the URL carries no usable name.
        /// </summary>
        public static string GetFileNameFromLink(string url, int index)
        {
            string fileName = "";
            try
            {
                fileName = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).LocalPath));
            }
            catch
            {
                // Fall through to the generated name below
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(invalid, '_');

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"drawing_{index}.dwg";
            else if (!Path.GetExtension(fileName).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
                fileName += ".dwg";

            return fileName;
        }

        /// <summary>
        /// Downloads every link into <paramref name="targetFolder"/> with up to
        /// <paramref name="maxParallel"/> concurrent downloads. Failed downloads leave no
        /// partial file behind. Returns the links that failed, with the error message.
        /// </summary>
        public static async Task<List<(DrawingLink Link, string Error)>> DownloadAllAsync(
            IReadOnlyList<DrawingLink> links,
            string targetFolder,
            int maxParallel,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(targetFolder);

            var failed = new List<(DrawingLink, string)>();
            var lockObj = new object();
            int done = 0;

            await Parallel.ForEachAsync(links, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxParallel),
                CancellationToken = cancellationToken
            },
            async (link, ct) =>
            {
                string filePath = Path.Combine(targetFolder, link.FileName);
                try
                {
                    using (var response = await Http.GetAsync(link.Url, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        response.EnsureSuccessStatusCode();
                        await using var source = await response.Content.ReadAsStreamAsync(ct);
                        await using var target = File.Create(filePath);
                        await source.CopyToAsync(target, ct);
                    }

                    int n = Interlocked.Increment(ref done);
                    log($"   [{n}/{links.Count}] ✅ Downloaded: {link.FileName}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    int n = Interlocked.Increment(ref done);
                    lock (lockObj) failed.Add((link, ex.Message));
                    log($"   [{n}/{links.Count}] ❌ Failed: {link.FileName} — {ex.Message}");
                    TryDelete(filePath);
                }
                catch
                {
                    TryDelete(filePath);
                    throw;
                }
            });

            return failed;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }
}
