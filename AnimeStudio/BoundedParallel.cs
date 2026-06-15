using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace AnimeStudio
{
    internal static class BoundedParallel
    {
        internal static void For(
            int fromInclusive,
            int toExclusive,
            int workerCount,
            CancellationToken cancellationToken,
            Action<int> body)
        {
            ArgumentNullException.ThrowIfNull(body);
            if (workerCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            }

            var count = checked(toExclusive - fromInclusive);
            if (count <= 0)
            {
                return;
            }

            var activeWorkerCount = Math.Min(workerCount, count);
            if (activeWorkerCount == 1)
            {
                for (var index = fromInclusive;
                    index < toExclusive;
                    index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    body(index);
                }

                return;
            }

            using var start = new ManualResetEventSlim();
            using var ready = new CountdownEvent(activeWorkerCount - 1);
            var threads = new Thread[activeWorkerCount - 1];
            var nextIndex = checked(fromInclusive + activeWorkerCount);
            ExceptionDispatchInfo failure = null;

            void RunWorker(int workerIndex)
            {
                try
                {
                    if (workerIndex > 0)
                    {
                        ready.Signal();
                        start.Wait(cancellationToken);
                    }

                    ProcessIndex(checked(fromInclusive + workerIndex));
                    while (Volatile.Read(ref failure) == null)
                    {
                        var index = Interlocked.Increment(ref nextIndex) - 1;
                        if (index >= toExclusive)
                        {
                            break;
                        }

                        ProcessIndex(index);
                    }
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(
                        ref failure,
                        ExceptionDispatchInfo.Capture(exception),
                        null);
                }
            }

            void ProcessIndex(int index)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref failure) == null)
                {
                    body(index);
                }
            }

            for (var workerIndex = 1;
                workerIndex < activeWorkerCount;
                workerIndex++)
            {
                var capturedWorkerIndex = workerIndex;
                threads[workerIndex - 1] = new Thread(
                    () => RunWorker(capturedWorkerIndex))
                {
                    IsBackground = true,
                    Name = $"AnimeStudio worker {capturedWorkerIndex}"
                };
                threads[workerIndex - 1].Start();
            }

            try
            {
                ready.Wait(cancellationToken);
                start.Set();
                RunWorker(0);
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(
                    ref failure,
                    ExceptionDispatchInfo.Capture(exception),
                    null);
                start.Set();
            }
            finally
            {
                foreach (var thread in threads)
                {
                    thread.Join();
                }
            }

            failure?.Throw();
        }
    }
}
