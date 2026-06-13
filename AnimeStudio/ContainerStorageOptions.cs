using System;
using System.IO;

namespace AnimeStudio
{
    public sealed class ContainerStorageOptions
    {
        public const long DefaultMemoryThresholdBytes = 256L * 1024 * 1024;

        public long MemoryThresholdBytes { get; set; } = DefaultMemoryThresholdBytes;

        public string TemporaryDirectory { get; set; }

        internal ContainerStorageOptions Clone()
        {
            if (MemoryThresholdBytes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MemoryThresholdBytes),
                    MemoryThresholdBytes,
                    "The container memory threshold cannot be negative.");
            }

            return new ContainerStorageOptions
            {
                MemoryThresholdBytes = MemoryThresholdBytes,
                TemporaryDirectory = TemporaryDirectory
            };
        }

        internal static string GetDefaultTemporaryDirectory()
        {
            var environmentDirectory = Environment.GetEnvironmentVariable("ANIMESTUDIO_TEMP_DIR");
            if (!string.IsNullOrWhiteSpace(environmentDirectory))
            {
                return Path.GetFullPath(environmentDirectory);
            }

            if (OperatingSystem.IsWindows())
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(localAppData))
                {
                    throw new IOException("LOCALAPPDATA is unavailable for container temporary storage.");
                }

                return Path.Combine(localAppData, "AnimeStudio", "Temp");
            }

            var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrWhiteSpace(cacheHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home))
                {
                    throw new IOException("The user home directory is unavailable for container temporary storage.");
                }

                cacheHome = Path.Combine(home, ".cache");
            }

            return Path.Combine(cacheHome, "animestudio", "tmp");
        }
    }
}
