using System;

namespace AnimeStudio
{
    internal static class WorkerBudget
    {
        internal static int GetMemoryStableWorkerCount(int requestedWorkers)
        {
            if (requestedWorkers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(requestedWorkers));
            }

            return requestedWorkers <= 2
                ? requestedWorkers
                : requestedWorkers - 1;
        }
    }
}
