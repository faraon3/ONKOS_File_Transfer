using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ONKOS.Core.Transfers
{
    public sealed class PathSizeSummary
    {
        public long TotalBytes { get; set; }
        public int FileCount { get; set; }
        public DateTime ComputedUtc { get; set; }
    }

    public static class PathSizeCache
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, PathSizeSummary> Cache =
            new Dictionary<string, PathSizeSummary>(StringComparer.OrdinalIgnoreCase);

        public static void Invalidate(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            lock (Sync)
            {
                Cache.Remove(path.Trim());
            }
        }

        public static async Task<PathSizeSummary> GetOrComputeAsync(string path, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new PathSizeSummary { ComputedUtc = DateTime.UtcNow };

            string key = path.Trim();
            DateTime currentWriteUtc = GetLastWriteUtcSafe(key);

            lock (Sync)
            {
                if (Cache.TryGetValue(key, out PathSizeSummary cached) &&
                    cached.ComputedUtc >= currentWriteUtc &&
                    (DateTime.UtcNow - cached.ComputedUtc).TotalSeconds < 30)
                {
                    return cached;
                }
            }

            PathSizeSummary computed = await Task.Run(() => ComputeInternal(key, token), token).ConfigureAwait(false);
            lock (Sync)
            {
                Cache[key] = computed;
            }

            return computed;
        }

        private static PathSizeSummary ComputeInternal(string path, CancellationToken token)
        {
            long bytes = 0L;
            int files = 0;

            if (File.Exists(path))
            {
                try
                {
                    bytes = new FileInfo(path).Length;
                    files = 1;
                }
                catch (IOException)
                {
                }
            }
            else if (Directory.Exists(path))
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        bytes += new FileInfo(file).Length;
                        files++;
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            return new PathSizeSummary
            {
                TotalBytes = bytes,
                FileCount = files,
                ComputedUtc = DateTime.UtcNow
            };
        }

        private static DateTime GetLastWriteUtcSafe(string path)
        {
            try
            {
                if (File.Exists(path))
                    return File.GetLastWriteTimeUtc(path);
                if (Directory.Exists(path))
                    return Directory.GetLastWriteTimeUtc(path);
            }
            catch (IOException)
            {
            }

            return DateTime.UtcNow;
        }
    }
}
