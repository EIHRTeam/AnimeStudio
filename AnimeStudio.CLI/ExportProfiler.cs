using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AnimeStudio.CLI
{
    // Experiment-only per-asset/per-phase profiler. Enabled by env var so the
    // instrumented binary behaves identically to baseline when off. Records
    // wall-clock ticks per "Type/Phase" key with thread-safe accumulation.
    internal static class ExportProfiler
    {
        internal static readonly bool Enabled =
            Environment.GetEnvironmentVariable("ANIMESTUDIO_EXP_PROFILE") == "1";

        private sealed class Acc
        {
            public long Count;
            public long Ticks;
        }

        private static readonly ConcurrentDictionary<string, Acc> Data = new();

        internal static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0L;

        internal static void Add(string type, string phase, long startTs)
        {
            if (!Enabled)
            {
                return;
            }

            var elapsed = Stopwatch.GetTimestamp() - startTs;
            var acc = Data.GetOrAdd(type + "/" + phase, _ => new Acc());
            Interlocked.Increment(ref acc.Count);
            Interlocked.Add(ref acc.Ticks, elapsed);
        }

        internal static void Report(TextWriter w, double exportWallMs)
        {
            if (!Enabled)
            {
                return;
            }

            double tickMs = 1000.0 / Stopwatch.Frequency;
            w.WriteLine();
            w.WriteLine("=== EXPORT PROFILE ===");
            w.WriteLine($"export_phase_wall_ms: {exportWallMs:F0}");
            w.WriteLine(
                $"{"Type/Phase",-34} {"Count",10} {"TotalMs",12} {"AvgUs",12} {"%wall",7}");
            foreach (var kv in Data.OrderByDescending(k => k.Value.Ticks))
            {
                double ms = kv.Value.Ticks * tickMs;
                double avgUs = kv.Value.Count == 0
                    ? 0
                    : ms * 1000.0 / kv.Value.Count;
                double pct = exportWallMs <= 0 ? 0 : ms * 100.0 / exportWallMs;
                w.WriteLine(
                    $"{kv.Key,-34} {kv.Value.Count,10} {ms,12:F1} {avgUs,12:F1} {pct,7:F1}");
            }

            w.WriteLine("=== END EXPORT PROFILE ===");
        }
    }
}
