using System;
using AnimeStudio.CLI.Properties;

namespace AnimeStudio.CLI
{
    // Immutable outcome of resolving a performance mode and soft budget into the
    // concrete knobs the CLI applies at startup.
    public sealed record ResolvedPerformance(
        PerformanceMode Mode,
        int EffectiveWorkers,
        bool HalveParseWorkers,
        long ContainerThresholdMiB,
        int MinimumWorkerThreads,
        string Explanation,
        // PNG encoder for limit/fast (null in default => baseline SaveAsPng()).
        int? PngCompressionLevel = null,
        string PngFilterMethod = null);

    // Central resolver that turns (CLI --mode/--workers, user config, machine
    // facts) into effective settings. Performance metrics are treated as an
    // optimization budget to fill, not a throttle: limit/fast derive the highest
    // worker count the budget allows and disable the memory-stable halving.
    // default is a conservative anchor that reproduces today's behavior exactly
    // and ignores every config optimization knob, so it keeps the RSS gates even
    // when a ~/.anime/config.json is present. Only an explicit --workers overrides
    // the worker count in any mode.
    public static class PerformanceResolver
    {
        // Conservative per-worker RAM estimate (≈1 GiB) used to derive a worker
        // ceiling from maxMemoryKB. Sourced from STATUS.md peak-RSS measurements;
        // a heuristic, not a guarantee. Overridable via advanced settings.
        internal const long DefaultPerWorkerMemoryKB = 1_048_576;

        public static ResolvedPerformance Resolve(
            PerformanceMode? cliMode,
            int? cliWorkersExplicit,
            PerformanceConfig config,
            int processorCount,
            long? machineMemoryKB,
            long appsettingsThresholdMiB)
        {
            config ??= new PerformanceConfig();
            processorCount = Math.Max(1, processorCount);

            var mode = cliMode ?? config.mode ?? PerformanceMode.Default;
            var explicitWorkers = cliWorkersExplicit is { } cw && cw >= 1 ? cw : (int?)null;

            // Default is the conservative anchor: identical to today regardless of
            // any config optimization knobs. Only explicit --workers changes it.
            if (mode == PerformanceMode.Default)
            {
                var defaultWorkers = explicitWorkers ?? processorCount;
                return new ResolvedPerformance(
                    PerformanceMode.Default,
                    defaultWorkers,
                    HalveParseWorkers: true,
                    ContainerThresholdMiB: appsettingsThresholdMiB,
                    MinimumWorkerThreads: defaultWorkers,
                    Explanation: Describe(
                        PerformanceMode.Default, defaultWorkers, true, appsettingsThresholdMiB, defaultWorkers));
            }

            // limit/fast: honor the config budget and overrides. Ignore invalid
            // values rather than throwing; this is an optional user-level file.
            var cpuCores = config.cpuCores is { } c && c >= 1 ? c : (int?)null;
            var configWorkers = config.workers is { } w && w >= 1 ? w : (int?)null;
            var maxMemoryKB = config.maxMemoryKB is { } m && m > 0 ? m : (long?)null;
            var thresholdOverride =
                config.containerMemoryThresholdMiB is { } t && t >= 0 ? t : (long?)null;
            var perWorkerKB = config.advanced?.perWorkerMemoryEstimateKB is { } p && p > 0
                ? p
                : DefaultPerWorkerMemoryKB;

            int effectiveWorkers;
            if (explicitWorkers is { } requestedWorkers)
            {
                effectiveWorkers = requestedWorkers;
            }
            else if (mode == PerformanceMode.Fast)
            {
                effectiveWorkers = Clamp(Math.Min(
                    cpuCores ?? processorCount,
                    MemoryWorkerCap(MinNullable(maxMemoryKB, machineMemoryKB), perWorkerKB)));
            }
            else // Limit
            {
                effectiveWorkers = Clamp(Min3(
                    configWorkers ?? cpuCores ?? processorCount,
                    cpuCores ?? processorCount,
                    MemoryWorkerCap(maxMemoryKB, perWorkerKB)));
            }

            // Non-default modes disable the halving to fill the budget; the
            // advanced escape hatch can force it back on.
            var halveParseWorkers = config.advanced?.halveParseWorkers ?? false;
            var containerThresholdMiB = thresholdOverride ?? appsettingsThresholdMiB;
            var minimumWorkerThreads =
                config.advanced?.minimumWorkerThreads is { } mt && mt >= 1 ? mt : effectiveWorkers;

            // limit/fast use the faster PNG encoder. Default level 1 (BestSpeed) +
            // Sub filter measured 4.0x faster encode at +29% size on the Debian
            // texture corpus, and Sub is both faster and smaller than None there.
            // Config can override either; default mode never sets these (baseline).
            var pngLevel = config.advanced?.pngCompressionLevel is { } pl && pl >= 0 && pl <= 9
                ? pl
                : 1;
            var pngFilter = !string.IsNullOrWhiteSpace(config.advanced?.pngFilterMethod)
                ? config.advanced.pngFilterMethod
                : "sub";

            return new ResolvedPerformance(
                mode,
                effectiveWorkers,
                halveParseWorkers,
                containerThresholdMiB,
                minimumWorkerThreads,
                Describe(mode, effectiveWorkers, halveParseWorkers, containerThresholdMiB, minimumWorkerThreads, pngLevel, pngFilter),
                pngLevel,
                pngFilter);
        }

        private static string Describe(
            PerformanceMode mode, int workers, bool halve, long thresholdMiB, int minThreads,
            int? pngLevel = null, string pngFilter = null) =>
            $"mode={mode}; workers={workers} (parse halving {(halve ? "on" : "off")}); "
            + $"container budget {thresholdMiB} MiB; min threads {minThreads}"
            + (pngLevel is { } pl ? $"; png level {pl}+{pngFilter}" : "; png baseline");

        private static int Clamp(int value) => Math.Max(1, value);

        private static int Min3(int a, int b, int c) => Math.Min(a, Math.Min(b, c));

        private static long? MinNullable(long? a, long? b)
        {
            if (a is null)
            {
                return b;
            }

            if (b is null)
            {
                return a;
            }

            return Math.Min(a.Value, b.Value);
        }

        // Largest worker count whose estimated RAM stays within the budget.
        // No budget (null/<=0) means no memory-derived cap.
        private static int MemoryWorkerCap(long? budgetKB, long perWorkerKB)
        {
            if (budgetKB is not { } budget || budget <= 0)
            {
                return int.MaxValue;
            }

            var cap = budget / Math.Max(1, perWorkerKB);
            if (cap < 1)
            {
                return 1;
            }

            return cap > int.MaxValue ? int.MaxValue : (int)cap;
        }
    }
}
