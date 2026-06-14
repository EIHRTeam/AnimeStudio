using System;
using System.IO;

namespace AnimeStudio
{
    internal sealed class ContainerStorageManager : IDisposable
    {
        private readonly object sync = new();
        private readonly ContainerStorageOptions options;
        private TemporaryFileWorkspace workspace;
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

            var temporaryPath = CreateTemporaryPath(expectedLength, sourcePath);

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

        private string CreateTemporaryPath(long expectedLength, string sourcePath)
        {
            lock (sync)
            {
                if (disposeRequested)
                {
                    throw new ObjectDisposedException(nameof(ContainerStorageManager));
                }

                workspace ??= new TemporaryFileWorkspace(options);
                return workspace.CreateFilePath(sourcePath, ".bin", expectedLength);
            }
        }

        private void TryCompleteCleanup()
        {
            if (cleanupComplete || !disposeRequested || activeStores != 0)
            {
                return;
            }

            cleanupComplete = true;
            workspace?.Dispose();
            workspace = null;
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
