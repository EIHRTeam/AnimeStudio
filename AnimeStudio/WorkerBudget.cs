using System;

namespace AnimeStudio
{
    internal static class WorkerBudget
    {
        // Default true preserves the historical memory-stable behavior that
        // halves high-retention worker counts to hold peak RSS under the Phase 2
        // gate. The CLI sets this once at startup: fast/limit modes disable
        // halving to fill the resource budget; default keeps it on.
        private static volatile bool halveRetainedWorkers = true;

        internal static void ConfigureHalving(bool enabled)
        {
            halveRetainedWorkers = enabled;
        }

        internal static int GetMemoryStableWorkerCount(int requestedWorkers)
        {
            if (requestedWorkers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedWorkers));
            }

            if (!halveRetainedWorkers)
            {
                return requestedWorkers;
            }

            return requestedWorkers <= 2
                ? requestedWorkers
                : Math.Max(2, requestedWorkers / 2);
        }
    }
}
