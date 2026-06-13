using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace AnimeStudio
{
    internal sealed class ContainerStorageManager : IDisposable
    {
        private const long RequiredFreeSpaceReserve = 1024L * 1024 * 1024;
        private static readonly TimeSpan StaleDirectoryAge = TimeSpan.FromDays(7);

        private readonly object sync = new();
        private readonly ContainerStorageOptions options;
        private string runDirectory;
        private FileStream lockStream;
        private int activeStores;
        private long activeMemoryBytes;
        private bool disposeRequested;
        private bool cleanupComplete;

        internal ContainerStorageManager(ContainerStorageOptions options)
        {
            this.options = (options ?? new ContainerStorageOptions()).Clone();
        }

        internal SharedBackingStore Create(long expectedLength, string sourcePath)
        {
            if (expectedLength < 0)
            {
                throw new InvalidDataException($"Container decompressed length cannot be negative: {expectedLength}.");
            }

            if (TryReserveMemory(expectedLength))
            {
                MemoryStream memoryStream = null;
                try
                {
                    memoryStream = new MemoryStream(checked((int)expectedLength));
                    return new SharedBackingStore(
                        memoryStream,
                        expectedLength,
                        null,
                        expectedLength,
                        this);
                }
                catch
                {
                    memoryStream?.Dispose();
                    ReleaseReservation(expectedLength);
                    throw;
                }
            }

            var directory = EnsureRunDirectory(expectedLength);
            var sourceName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = "container";
            }

            var temporaryPath = Path.Combine(
                directory,
                $"{SanitizeFileName(sourceName)}-{Guid.NewGuid():N}.bin");

            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    1,
                    FileOptions.RandomAccess);
                RegisterFileStore();
                return new SharedBackingStore(
                    fileStream,
                    expectedLength,
                    temporaryPath,
                    0,
                    this);
            }
            catch
            {
                fileStream?.Dispose();
                TryDeleteFile(temporaryPath);
                throw;
            }
        }

        internal void ReleaseStore(string temporaryPath, long memoryReservation)
        {
            if (temporaryPath != null)
            {
                TryDeleteFile(temporaryPath);
            }

            lock (sync)
            {
                activeStores--;
                activeMemoryBytes -= memoryReservation;
                if (activeStores < 0)
                {
                    throw new InvalidOperationException("Container backing store reference count underflow.");
                }
                if (activeMemoryBytes < 0)
                {
                    throw new InvalidOperationException("Container memory reservation underflow.");
                }

                TryCompleteCleanup();
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                disposeRequested = true;
                TryCompleteCleanup();
            }
        }

        private bool TryReserveMemory(long expectedLength)
        {
            lock (sync)
            {
                if (disposeRequested)
                {
                    throw new ObjectDisposedException(nameof(ContainerStorageManager));
                }

                if (expectedLength > int.MaxValue
                    || expectedLength >= options.MemoryThresholdBytes
                    || activeMemoryBytes > options.MemoryThresholdBytes - expectedLength)
                {
                    return false;
                }

                activeStores++;
                activeMemoryBytes += expectedLength;
                return true;
            }
        }

        private void RegisterFileStore()
        {
            lock (sync)
            {
                if (disposeRequested)
                {
                    throw new ObjectDisposedException(nameof(ContainerStorageManager));
                }

                activeStores++;
            }
        }

        private void ReleaseReservation(long expectedLength)
        {
            lock (sync)
            {
                activeStores--;
                activeMemoryBytes -= expectedLength;
                if (activeStores < 0 || activeMemoryBytes < 0)
                {
                    throw new InvalidOperationException("Container memory reservation underflow.");
                }

                TryCompleteCleanup();
            }
        }

        private string EnsureRunDirectory(long expectedLength)
        {
            lock (sync)
            {
                if (disposeRequested)
                {
                    throw new ObjectDisposedException(nameof(ContainerStorageManager));
                }

                if (runDirectory == null)
                {
                    var rootDirectory = string.IsNullOrWhiteSpace(options.TemporaryDirectory)
                        ? ContainerStorageOptions.GetDefaultTemporaryDirectory()
                        : Path.GetFullPath(options.TemporaryDirectory);

                    Directory.CreateDirectory(rootDirectory);
                    CleanupStaleDirectories(rootDirectory);

                    runDirectory = Path.Combine(
                        rootDirectory,
                        $"run-{Environment.ProcessId}-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(runDirectory);

                    var lockPath = Path.Combine(runDirectory, ".lock");
                    lockStream = new FileStream(
                        lockPath,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.Read);
                    using var writer = new StreamWriter(lockStream, leaveOpen: true);
                    writer.WriteLine(Environment.ProcessId);
                    writer.WriteLine(DateTimeOffset.UtcNow.ToString("O"));
                    writer.Flush();
                    lockStream.Flush(true);
                }

                EnsureFreeSpace(runDirectory, expectedLength);
                return runDirectory;
            }
        }

        private static void EnsureFreeSpace(string directory, long expectedLength)
        {
            var fullPath = Path.GetFullPath(directory);
            DriveInfo drive = null;
            foreach (var candidate in DriveInfo.GetDrives())
            {
                try
                {
                    if (!candidate.IsReady)
                    {
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(candidate.RootDirectory.FullName, fullPath);
                    if (relativePath != ".."
                        && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && (drive == null
                            || candidate.RootDirectory.FullName.Length > drive.RootDirectory.FullName.Length))
                    {
                        drive = candidate;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            if (drive == null)
            {
                throw new IOException($"Cannot determine the storage volume for '{directory}'.");
            }

            var required = checked(expectedLength + RequiredFreeSpaceReserve);
            if (drive.AvailableFreeSpace < required)
            {
                throw new IOException(
                    $"Insufficient container temporary storage in '{directory}': " +
                    $"requires {required} bytes including reserve, available {drive.AvailableFreeSpace} bytes.");
            }
        }

        private static void CleanupStaleDirectories(string rootDirectory)
        {
            foreach (var directory in Directory.EnumerateDirectories(rootDirectory, "run-*"))
            {
                try
                {
                    var info = new DirectoryInfo(directory);
                    if (DateTime.UtcNow - info.LastWriteTimeUtc < StaleDirectoryAge)
                    {
                        continue;
                    }

                    var lockPath = Path.Combine(directory, ".lock");
                    using (new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None))
                    {
                    }

                    Directory.Delete(directory, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void TryCompleteCleanup()
        {
            if (cleanupComplete || !disposeRequested || activeStores != 0)
            {
                return;
            }

            cleanupComplete = true;
            lockStream?.Dispose();
            lockStream = null;

            if (runDirectory != null)
            {
                try
                {
                    Directory.Delete(runDirectory, true);
                }
                catch (DirectoryNotFoundException)
                {
                }
                catch (IOException exception)
                {
                    Logger.Warning(
                        $"Failed to remove container temporary directory '{runDirectory}': {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    Logger.Warning(
                        $"Failed to remove container temporary directory '{runDirectory}': {exception.Message}");
                }
            }
        }

        private static string SanitizeFileName(string value)
        {
            var invalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars());
            var characters = value.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (invalidCharacters.Contains(characters[index]))
                {
                    characters[index] = '_';
                }
            }

            return new string(characters);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException exception)
            {
                Logger.Warning($"Failed to remove container backing file '{path}': {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                Logger.Warning($"Failed to remove container backing file '{path}': {exception.Message}");
            }
        }
    }
}
