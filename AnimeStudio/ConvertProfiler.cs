using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AnimeStudio
{
    // Experiment-only sub-stage profiler living in the core assembly so both the
    // Utility conversion layer (AnimationClip/YAML) and the CLI can record into a
    // single accumulator. Env-gated by ANIMESTUDIO_EXP_PROFILE=1; every path
    // degrades to a no-op when disabled, so the instrumented binary is
    // bit-identical in behavior to baseline when off.
    //
    // It also centralizes the experimental PNG encoder knobs so ImageExtensions
    // (core) can read them without depending on the CLI's ExportProfiler.
    public static class ConvertProfiler
    {
        public static readonly bool Enabled =
            Environment.GetEnvironmentVariable("ANIMESTUDIO_EXP_PROFILE") == "1";

        // PNG encoder sweep. Level: -1 = unset (baseline/production encoder).
        // Filter: null = unset. These let one binary walk the speed/size Pareto
        // frontier during investigation; production fast/limit encoding now ships
        // via ImageExportSettings (the former ANIMESTUDIO_EXP_PNG_FAST shortcut,
        // L1+None, is reproducible as PNG_LEVEL=1 PNG_FILTER=none). The allocation-
        // free YAML float emit is likewise productized (always on in Emitter), so
        // it no longer has a knob here.
        public static readonly int PngLevel = ParseLevel();
        public static readonly string PngFilter =
            Environment.GetEnvironmentVariable("ANIMESTUDIO_EXP_PNG_FILTER");

        private static int ParseLevel()
        {
            var s = Environment.GetEnvironmentVariable("ANIMESTUDIO_EXP_PNG_LEVEL");
            return int.TryParse(s, out var v) && v >= 0 && v <= 9 ? v : -1;
        }

        private sealed class Acc
        {
            public long Count;
            public long Ticks;
            public long Bytes;
        }

        private static readonly ConcurrentDictionary<string, Acc> Data = new();

        public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0L;

        public static void Add(string key, long startTs, long bytes = 0)
        {
            if (!Enabled)
            {
                return;
            }

            var elapsed = Stopwatch.GetTimestamp() - startTs;
            var acc = Data.GetOrAdd(key, _ => new Acc());
            Interlocked.Increment(ref acc.Count);
            Interlocked.Add(ref acc.Ticks, elapsed);
            if (bytes != 0)
            {
                Interlocked.Add(ref acc.Bytes, bytes);
            }
        }

        public static void Report(TextWriter w)
        {
            if (!Enabled)
            {
                return;
            }

            double tickMs = 1000.0 / Stopwatch.Frequency;
            w.WriteLine();
            w.WriteLine("=== CONVERT SUB-STAGE PROFILE ===");
            w.WriteLine(
                $"png: level={PngLevel} filter={PngFilter ?? "(unset)"}");
            w.WriteLine(
                $"{"Key",-30} {"Count",9} {"TotalMs",12} {"AvgMs",10} {"OutMiB",10}");
            foreach (var kv in Data.OrderByDescending(k => k.Value.Ticks))
            {
                double ms = kv.Value.Ticks * tickMs;
                double avgMs = kv.Value.Count == 0 ? 0 : ms / kv.Value.Count;
                double mib = kv.Value.Bytes / 1048576.0;
                w.WriteLine(
                    $"{kv.Key,-30} {kv.Value.Count,9} {ms,12:F1} {avgMs,10:F2} {mib,10:F2}");
            }

            w.WriteLine("=== END CONVERT SUB-STAGE PROFILE ===");
        }
    }
}
