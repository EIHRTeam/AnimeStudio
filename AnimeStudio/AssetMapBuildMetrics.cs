using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace AnimeStudio
{
    internal enum AssetMapBuildStage
    {
        Loading,
        ObjectScanning,
        ContainerResolution,
        FilteringAndSpooling,
        XmlWriting,
        JsonWriting,
        MessagePackWriting
    }

    internal sealed class AssetMapBuildMetrics
    {
        private static readonly AssetMapBuildStage[] OrderedStages =
        [
            AssetMapBuildStage.Loading,
            AssetMapBuildStage.ObjectScanning,
            AssetMapBuildStage.ContainerResolution,
            AssetMapBuildStage.FilteringAndSpooling,
            AssetMapBuildStage.XmlWriting,
            AssetMapBuildStage.JsonWriting,
            AssetMapBuildStage.MessagePackWriting
        ];

        private readonly long[] elapsedTimestamps =
            new long[Enum.GetValues<AssetMapBuildStage>().Length];
        private readonly int[] measurementCounts =
            new int[Enum.GetValues<AssetMapBuildStage>().Length];

        internal IDisposable Measure(AssetMapBuildStage stage)
        {
            return new Measurement(this, stage, Stopwatch.GetTimestamp());
        }

        internal TimeSpan GetElapsed(AssetMapBuildStage stage)
        {
            return TimeSpan.FromSeconds(
                (double)elapsedTimestamps[(int)stage] / Stopwatch.Frequency);
        }

        internal int GetMeasurementCount(AssetMapBuildStage stage)
        {
            return measurementCounts[(int)stage];
        }

        internal IEnumerable<string> FormatSummary(long assetCount)
        {
            yield return $"AssetMap stage timings ({assetCount} assets):";
            foreach (var stage in OrderedStages)
            {
                var count = GetMeasurementCount(stage);
                var label = GetLabel(stage);
                if (count == 0)
                {
                    yield return $"  {label}: not run";
                    continue;
                }

                var milliseconds = GetElapsed(stage).TotalMilliseconds.ToString(
                    "F3",
                    CultureInfo.InvariantCulture);
                var passLabel = count == 1 ? "pass" : "passes";
                yield return $"  {label}: {milliseconds} ms ({count} {passLabel})";
            }
        }

        internal void LogSummary(long assetCount)
        {
            foreach (var line in FormatSummary(assetCount))
            {
                Logger.Info(line);
            }
        }

        private static string GetLabel(AssetMapBuildStage stage)
        {
            return stage switch
            {
                AssetMapBuildStage.Loading => "Loading",
                AssetMapBuildStage.ObjectScanning => "Object scanning",
                AssetMapBuildStage.ContainerResolution => "Container resolution",
                AssetMapBuildStage.FilteringAndSpooling => "Filtering/spooling",
                AssetMapBuildStage.XmlWriting => "XML writer",
                AssetMapBuildStage.JsonWriting => "JSON writer",
                AssetMapBuildStage.MessagePackWriting => "MessagePack writer",
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
            };
        }

        private void Complete(AssetMapBuildStage stage, long startedAt)
        {
            var index = (int)stage;
            elapsedTimestamps[index] = checked(
                elapsedTimestamps[index] + Stopwatch.GetTimestamp() - startedAt);
            measurementCounts[index] = checked(measurementCounts[index] + 1);
        }

        private sealed class Measurement : IDisposable
        {
            private readonly AssetMapBuildMetrics owner;
            private readonly AssetMapBuildStage stage;
            private readonly long startedAt;
            private bool disposed;

            internal Measurement(
                AssetMapBuildMetrics owner,
                AssetMapBuildStage stage,
                long startedAt)
            {
                this.owner = owner;
                this.stage = stage;
                this.startedAt = startedAt;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                owner.Complete(stage, startedAt);
            }
        }
    }
}
