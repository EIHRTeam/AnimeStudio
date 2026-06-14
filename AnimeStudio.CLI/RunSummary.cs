using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AnimeStudio.CLI
{
    internal readonly struct FileTreeStatistics
    {
        public FileTreeStatistics(long fileCount, long totalBytes)
        {
            FileCount = fileCount;
            TotalBytes = totalBytes;
        }

        public long FileCount { get; }

        public long TotalBytes { get; }
    }

    internal static class RunSummary
    {
        private static readonly EnumerationOptions OutputEnumerationOptions = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        internal static bool TryMeasureFiles(
            IEnumerable<string> filePaths,
            out FileTreeStatistics statistics,
            out string error)
        {
            try
            {
                long fileCount = 0;
                long totalBytes = 0;
                foreach (var filePath in filePaths)
                {
                    checked
                    {
                        fileCount++;
                        totalBytes += new FileInfo(filePath).Length;
                    }
                }

                statistics = new FileTreeStatistics(fileCount, totalBytes);
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                OverflowException)
            {
                statistics = default;
                error = exception.Message;
                return false;
            }
        }

        internal static bool TryMeasureDirectory(
            string directoryPath,
            out FileTreeStatistics statistics,
            out string error)
        {
            try
            {
                statistics = MeasureDirectory(directoryPath);
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                OverflowException)
            {
                statistics = default;
                error = exception.Message;
                return false;
            }
        }

        internal static FileTreeStatistics MeasureDirectory(string directoryPath)
        {
            long fileCount = 0;
            long totalBytes = 0;
            foreach (var filePath in Directory.EnumerateFiles(
                directoryPath,
                "*",
                OutputEnumerationOptions))
            {
                checked
                {
                    fileCount++;
                    totalBytes += new FileInfo(filePath).Length;
                }
            }

            return new FileTreeStatistics(fileCount, totalBytes);
        }

        internal static string FormatElapsed(TimeSpan elapsed)
        {
            var totalSeconds = Math.Max(0L, (long)elapsed.TotalSeconds);
            var hours = totalSeconds / 3600;
            var minutes = totalSeconds % 3600 / 60;
            var seconds = totalSeconds % 60;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{hours:D2}:{minutes:D2}:{seconds:D2} ({totalSeconds}s)");
        }

        internal static string FormatByteSize(long byteCount)
        {
            string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
            var value = (double)byteCount;
            var unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            var readableSize = unitIndex == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{value:0} {units[unitIndex]}")
                : string.Create(CultureInfo.InvariantCulture, $"{value:0.00} {units[unitIndex]}");
            var exactSize = byteCount.ToString("N0", CultureInfo.InvariantCulture);
            return $"{readableSize} ({exactSize} bytes)";
        }

        internal static void Write(
            TextWriter writer,
            TimeSpan elapsed,
            string outputDirectory,
            FileTreeStatistics? inputStatistics,
            string inputStatisticsError,
            FileTreeStatistics? outputStatistics,
            string outputStatisticsError)
        {
            writer.WriteLine();
            writer.WriteLine("Run summary:");
            writer.WriteLine($"  Elapsed time: {FormatElapsed(elapsed)}");
            writer.WriteLine($"  Output directory: {Path.GetFullPath(outputDirectory)}");

            if (inputStatistics.HasValue)
            {
                writer.WriteLine(
                    $"  Input files: {inputStatistics.Value.FileCount.ToString("N0", CultureInfo.InvariantCulture)}");
                writer.WriteLine(
                    $"  Input size before extraction: {FormatByteSize(inputStatistics.Value.TotalBytes)}");
            }
            else
            {
                writer.WriteLine("  Input files: unavailable");
                writer.WriteLine(
                    $"  Input size before extraction: unavailable ({inputStatisticsError})");
            }

            if (outputStatistics.HasValue)
            {
                writer.WriteLine(
                    $"  Output files: {outputStatistics.Value.FileCount.ToString("N0", CultureInfo.InvariantCulture)}");
                writer.WriteLine(
                    $"  Output size: {FormatByteSize(outputStatistics.Value.TotalBytes)}");
            }
            else
            {
                writer.WriteLine("  Output files: unavailable");
                writer.WriteLine($"  Output size: unavailable ({outputStatisticsError})");
            }
        }
    }
}
