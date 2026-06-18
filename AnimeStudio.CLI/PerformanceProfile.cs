using System;
using System.IO;
using System.Text.Json;

namespace AnimeStudio.CLI.Properties
{
    // Performance is expressed as an optimization budget the tool fills to run as
    // fast as the budget allows, not as a throttle that holds the machine below
    // its capability. "limit" maximizes speed within the configured RAM/CPU
    // budget; "fast" maximizes speed within the whole machine (or budget);
    // "default" is the conservative, backward-compatible behavior.
    public enum PerformanceMode
    {
        Default,
        Limit,
        Fast
    }

    public sealed class AdvancedOverrides
    {
        // Tunable per-worker RAM estimate used when deriving a worker count from
        // maxMemoryKB. Null falls back to the resolver's built-in default.
        public long? perWorkerMemoryEstimateKB { get; set; }

        // Explicit ThreadPool minimum worker thread target. Null follows the
        // resolved worker count.
        public int? minimumWorkerThreads { get; set; }

        // Escape hatch that overrides the per-mode parse-worker halving policy.
        // Null follows the mode (fast/limit disable halving, default keeps it).
        public bool? halveParseWorkers { get; set; }
    }

    // User-level performance profile loaded from ~/.anime/config.json. Every
    // field is nullable so that an absent file or omitted key leaves the current
    // default behavior untouched. This is a plain DTO; validation and the
    // mode/budget resolution live in PerformanceResolver.
    public sealed class PerformanceConfig
    {
        private const string FileName = "config.json";
        private const string DirectoryName = ".anime";
        private const string PathEnvironmentVariable = "ANIMESTUDIO_CONFIG_PATH";

        public PerformanceMode? mode { get; set; }
        public long? maxMemoryKB { get; set; }
        public int? cpuCores { get; set; }
        public int? workers { get; set; }
        public long? containerMemoryThresholdMiB { get; set; }
        public AdvancedOverrides advanced { get; set; }

        // ANIMESTUDIO_CONFIG_PATH (when set) takes priority, mirroring the
        // ANIMESTUDIO_TEMP_DIR override pattern; otherwise the user profile
        // home is used. The path is resolved read-only; no directory is created.
        public static string GetConfigPath()
        {
            var overridePath = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, DirectoryName, FileName);
        }

        public static PerformanceConfig Load()
        {
            // GetConfigPath()/Path.GetFullPath are inside the try so a malformed
            // ANIMESTUDIO_CONFIG_PATH degrades to defaults instead of aborting the
            // run. The filter also covers the path-shaped exceptions that override
            // can raise (ArgumentException, NotSupportedException, SecurityException;
            // PathTooLongException derives from IOException).
            try
            {
                var path = GetConfigPath();
                if (!File.Exists(path))
                {
                    // A missing user-level file is the common case, not an error.
                    return new PerformanceConfig();
                }

                return JsonSerializer.Deserialize<PerformanceConfig>(
                    File.ReadAllText(path), Settings.SerializerOptions)
                    ?? new PerformanceConfig();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                or JsonException or ArgumentException or NotSupportedException
                or System.Security.SecurityException)
            {
                Console.Error.WriteLine(
                    $"Unable to load the performance config; defaults used. {e.Message}");
                return new PerformanceConfig();
            }
        }
    }
}
