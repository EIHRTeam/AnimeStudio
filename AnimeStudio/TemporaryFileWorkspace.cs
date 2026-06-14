using System;
using System.Collections.Generic;
using System.IO;

namespace AnimeStudio
{
    internal sealed class TemporaryFileWorkspace : IDisposable
    {
        private const long RequiredFreeSpaceReserve = 1024L * 1024 * 1024;
        private static readonly TimeSpan StaleDirectoryAge = TimeSpan.FromDays(7);

        private readonly object sync = new();
        private readonly ContainerStorageOptions options;
        private string runDirectory;
        private FileStream lockStream;
        private bool disposed;

        internal TemporaryFileWorkspace(ContainerStorageOptions options)
        {
            this.options = (options ?? new ContainerStorageOptions()).Clone();
        }

        internal string DirectoryPath
        {
            get
            {
                lock (sync)
                {
                    ThrowIfDisposed();
                    return runDirectory;
                }
            }
        }

        internal string CreateFilePath(
            string sourceName,
            string extension,
            long expectedAdditionalLength)
        {
            lock (sync)
            {
                var directory = EnsureDirectoryCore(expectedAdditionalLength);
                var name = Path.GetFileName(sourceName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "temporary";
                }

                return Path.Combine(
                    directory,
                    $"{SanitizeFileName(name)}-{Guid.NewGuid():N}{extension}");
            }
        }

        internal void EnsureFreeSpace(long expectedAdditionalLength)
        {
            lock (sync)
            {
                EnsureDirectoryCore(expectedAdditionalLength);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                lockStream?.Dispose();
                lockStream = null;

                if (runDirectory == null)
                {
                    return;
                }

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
                        $"Failed to remove temporary workspace '{runDirectory}': {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    Logger.Warning(
                        $"Failed to remove temporary workspace '{runDirectory}': {exception.Message}");
                }
            }
        }

        private string EnsureDirectoryCore(long expectedAdditionalLength)
        {
            ThrowIfDisposed();
            if (expectedAdditionalLength < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedAdditionalLength),
                    expectedAdditionalLength,
                    "Expected temporary file growth cannot be negative.");
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

            EnsureVolumeFreeSpace(runDirectory, expectedAdditionalLength);
            return runDirectory;
        }

        private static void EnsureVolumeFreeSpace(string directory, long expectedAdditionalLength)
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
                        && !relativePath.StartsWith(
                            $"..{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                        && (drive == null
                            || candidate.RootDirectory.FullName.Length
                                > drive.RootDirectory.FullName.Length))
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

            var required = checked(expectedAdditionalLength + RequiredFreeSpaceReserve);
            if (drive.AvailableFreeSpace < required)
            {
                throw new IOException(
                    $"Insufficient temporary storage in '{directory}': " +
                    $"requires {required} bytes including reserve, " +
                    $"available {drive.AvailableFreeSpace} bytes.");
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

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(TemporaryFileWorkspace));
            }
        }
    }
}
