using System;
namespace AnimeStudio
{
    public static class Progress
    {
        private static readonly object Sync = new();
        public static bool Silent = false;
        public static IProgress<int> Default = new Progress<int>();
        private static int preValue;

        public static void Reset()
        {
            lock (Sync)
            {
                if (Silent)
                {
                    return;
                }

                preValue = 0;
                Default.Report(0);
            }
        }

        public static void Report(int current, int total)
        {
            if (!Silent && total > 0)
            {
                var value = (int)(current * 100f / total);
                Report(value);
            }
        }

        private static void Report(int value)
        {
            lock (Sync)
            {
                if (!Silent && value > preValue)
                {
                    preValue = value;
                    Default.Report(value);
                }
            }
        }
    }
}
