using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace AnimeStudio
{
    internal delegate int ContainerBlockDecoder(
        int blockIndex,
        BundleFile.StorageBlock block,
        byte[] compressedBuffer,
        int compressedLength,
        byte[] uncompressedBuffer,
        int uncompressedLength);

    internal static class ContainerBlockPipeline
    {
        private const long ScratchBudgetBytes = 256L * 1024 * 1024;

        internal static void Process(
            EndianBinaryReader reader,
            IReadOnlyList<BundleFile.StorageBlock> blocks,
            SharedBackingStore destination,
            int requestedWorkers,
            CancellationToken cancellationToken,
            ContainerBlockDecoder decoder)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(blocks);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(decoder);
            if (requestedWorkers < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedWorkers));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var workerCount = GetWorkerCount(blocks, requestedWorkers);
            destination.PrepareForPositionedWrites();
            if (blocks.Count == 0)
            {
                return;
            }

            var outputOffsets = BuildOutputOffsets(blocks);
            if (workerCount == 1)
            {
                for (var index = 0; index < blocks.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var item = ReadBlock(
                        reader,
                        index,
                        blocks[index]);
                    ProcessBlock(
                        item,
                        outputOffsets[index],
                        destination,
                        decoder);
                }

                return;
            }

            using var queue = new BlockingCollection<BlockWorkItem>(
                boundedCapacity: Math.Max(1, workerCount - 1));
            using var failureCancellation = new CancellationTokenSource();
            using var linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    failureCancellation.Token);
            var threads = new Thread[workerCount];
            ExceptionDispatchInfo failure = null;

            void RecordFailure(Exception exception)
            {
                if (Interlocked.CompareExchange(
                    ref failure,
                    ExceptionDispatchInfo.Capture(exception),
                    null) == null)
                {
                    failureCancellation.Cancel();
                }
            }

            void RunWorker()
            {
                try
                {
                    foreach (var item in queue.GetConsumingEnumerable())
                    {
                        try
                        {
                            if (Volatile.Read(ref failure) == null)
                            {
                                ProcessBlock(
                                    item,
                                    outputOffsets[item.Index],
                                    destination,
                                    decoder);
                            }
                        }
                        catch (Exception exception)
                        {
                            RecordFailure(exception);
                        }
                        finally
                        {
                            item.Dispose();
                        }
                    }
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                }
            }

            for (var workerIndex = 0;
                workerIndex < threads.Length;
                workerIndex++)
            {
                var capturedWorkerIndex = workerIndex;
                threads[workerIndex] = new Thread(
                    RunWorker)
                {
                    IsBackground = true,
                    Name = $"AnimeStudio container {capturedWorkerIndex}"
                };
                threads[workerIndex].Start();
            }

            try
            {
                for (var index = 0; index < blocks.Count; index++)
                {
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    var item = ReadBlock(reader, index, blocks[index]);
                    try
                    {
                        queue.Add(item, linkedCancellation.Token);
                    }
                    catch
                    {
                        item.Dispose();
                        throw;
                    }
                }
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref failure) == null)
                {
                    RecordFailure(exception);
                }
            }
            finally
            {
                queue.CompleteAdding();
                foreach (var thread in threads)
                {
                    thread.Join();
                }
            }

            failure?.Throw();
        }

        private static long[] BuildOutputOffsets(
            IReadOnlyList<BundleFile.StorageBlock> blocks)
        {
            var offsets = new long[blocks.Count];
            long offset = 0;
            for (var index = 0; index < blocks.Count; index++)
            {
                offsets[index] = offset;
                offset = checked(offset + blocks[index].uncompressedSize);
            }

            return offsets;
        }

        private static int GetWorkerCount(
            IReadOnlyList<BundleFile.StorageBlock> blocks,
            int requestedWorkers)
        {
            long maximumScratchBytes = 1;
            foreach (var block in blocks)
            {
                var scratchBytes = checked(
                    (long)block.uncompressedSize
                    + 2L * block.compressedSize);
                maximumScratchBytes = Math.Max(
                    maximumScratchBytes,
                    scratchBytes);
            }

            var memoryLimitedWorkers = Math.Max(
                1,
                (int)Math.Min(
                    int.MaxValue,
                    ScratchBudgetBytes / maximumScratchBytes));
            return Math.Min(
                blocks.Count,
                Math.Min(requestedWorkers, memoryLimitedWorkers));
        }

        private static BlockWorkItem ReadBlock(
            EndianBinaryReader reader,
            int index,
            BundleFile.StorageBlock block)
        {
            var compressedLength = checked((int)block.compressedSize);
            var buffer = ArrayPool<byte>.Shared.Rent(compressedLength);
            try
            {
                reader.BaseStream.ReadExactly(
                    buffer.AsSpan(0, compressedLength));
                return new BlockWorkItem(
                    index,
                    block,
                    buffer,
                    compressedLength);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                throw;
            }
        }

        private static void ProcessBlock(
            BlockWorkItem item,
            long outputOffset,
            SharedBackingStore destination,
            ContainerBlockDecoder decoder)
        {
            var uncompressedLength =
                checked((int)item.Block.uncompressedSize);
            var uncompressed =
                ArrayPool<byte>.Shared.Rent(uncompressedLength);
            try
            {
                var written = decoder(
                    item.Index,
                    item.Block,
                    item.Buffer,
                    item.Length,
                    uncompressed,
                    uncompressedLength);
                if (written != uncompressedLength)
                {
                    throw new InvalidDataException(
                        $"Container block {item.Index} decoded to {written} " +
                        $"bytes; expected {uncompressedLength} bytes.");
                }

                destination.WriteAt(
                    outputOffset,
                    uncompressed.AsSpan(0, uncompressedLength));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(
                    uncompressed,
                    clearArray: true);
            }
        }

        private sealed class BlockWorkItem : IDisposable
        {
            internal BlockWorkItem(
                int index,
                BundleFile.StorageBlock block,
                byte[] buffer,
                int length)
            {
                Index = index;
                Block = block;
                Buffer = buffer;
                Length = length;
            }

            internal int Index { get; }

            internal BundleFile.StorageBlock Block { get; }

            internal byte[] Buffer;

            internal int Length { get; }

            public void Dispose()
            {
                var buffer = Interlocked.Exchange(ref Buffer, null);
                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(
                        buffer,
                        clearArray: true);
                }
            }
        }
    }
}
