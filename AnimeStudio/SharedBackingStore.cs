using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeStudio
{
    internal sealed class SharedBackingStore : Stream
    {
        private readonly object sync = new();
        private readonly Stream stream;
        private readonly FileStream fileStream;
        private readonly byte[] memoryBuffer;
        private readonly int memoryBufferOffset;
        private readonly long expectedLength;
        private readonly string temporaryPath;
        private readonly long memoryReservation;
        private readonly ContainerStorageManager manager;
        private long maximumWrittenPosition;
        private int referenceCount = 1;
        private int ownerReleased;
        private bool sealedForSlices;
        private bool backingDisposed;

        internal SharedBackingStore(
            Stream stream,
            long expectedLength,
            string temporaryPath,
            long memoryReservation,
            ContainerStorageManager manager)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            fileStream = stream as FileStream;
            if (stream is MemoryStream memoryStream
                && memoryStream.TryGetBuffer(out var memorySegment))
            {
                memoryBuffer = memorySegment.Array;
                memoryBufferOffset = memorySegment.Offset;
            }
            this.expectedLength = expectedLength;
            this.temporaryPath = temporaryPath;
            this.memoryReservation = memoryReservation;
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        internal bool IsFileBacked => temporaryPath != null;

        internal string TemporaryPath => temporaryPath;

        public override bool CanRead => ownerReleased == 0 && !backingDisposed && stream.CanRead;

        public override bool CanSeek => ownerReleased == 0 && !backingDisposed && stream.CanSeek;

        public override bool CanWrite =>
            ownerReleased == 0 && !backingDisposed && !sealedForSlices && stream.CanWrite;

        public override long Length
        {
            get
            {
                lock (sync)
                {
                    ThrowIfOwnerReleased();
                    ThrowIfBackingDisposed();
                    return stream.Length;
                }
            }
        }

        public override long Position
        {
            get
            {
                lock (sync)
                {
                    ThrowIfOwnerReleased();
                    ThrowIfBackingDisposed();
                    return stream.Position;
                }
            }
            set
            {
                lock (sync)
                {
                    ThrowIfOwnerReleased();
                    ThrowIfBackingDisposed();
                    stream.Position = value;
                }
            }
        }

        internal ReadOnlySliceStream CreateSlice(long offset, long length)
        {
            lock (sync)
            {
                ThrowIfOwnerReleased();
                ThrowIfBackingDisposed();
                if (!sealedForSlices)
                {
                    throw new InvalidOperationException("The container backing store must be sealed before slices are created.");
                }

                ValidateSlice(offset, length);
                checked
                {
                    referenceCount++;
                }

                return new ReadOnlySliceStream(this, offset, length);
            }
        }

        internal void Seal()
        {
            lock (sync)
            {
                ThrowIfOwnerReleased();
                ThrowIfBackingDisposed();
                if (sealedForSlices)
                {
                    return;
                }

                if (maximumWrittenPosition != expectedLength)
                {
                    throw new InvalidDataException(
                        $"Container decompression produced {maximumWrittenPosition} bytes; expected {expectedLength} bytes.");
                }

                stream.Flush();
                sealedForSlices = true;
            }
        }

        internal int ReadAt(long absoluteOffset, Span<byte> buffer)
        {
            if (absoluteOffset < 0 || absoluteOffset > expectedLength)
            {
                throw new ArgumentOutOfRangeException(nameof(absoluteOffset));
            }

            lock (sync)
            {
                ThrowIfBackingDisposed();
            }

            var available = expectedLength - absoluteOffset;
            var count = (int)Math.Min(buffer.Length, available);
            if (count == 0)
            {
                return 0;
            }

            if (fileStream != null)
            {
                var totalRead = 0;
                while (totalRead < count)
                {
                    var bytesRead = RandomAccess.Read(
                        fileStream.SafeFileHandle,
                        buffer.Slice(totalRead, count - totalRead),
                        absoluteOffset + totalRead);
                    if (bytesRead == 0)
                    {
                        break;
                    }
                    totalRead += bytesRead;
                }
                return totalRead;
            }

            if (memoryBuffer != null)
            {
                memoryBuffer.AsSpan(
                        checked(memoryBufferOffset + checked((int)absoluteOffset)),
                        count)
                    .CopyTo(buffer);
                return count;
            }

            lock (sync)
            {
                var previousPosition = stream.Position;
                try
                {
                    stream.Position = absoluteOffset;
                    return stream.Read(buffer[..count]);
                }
                finally
                {
                    stream.Position = previousPosition;
                }
            }
        }

        internal void ReleaseSlice()
        {
            ReleaseReference();
        }

        public override void Flush()
        {
            lock (sync)
            {
                ThrowIfOwnerReleased();
                ThrowIfBackingDisposed();
                stream.Flush();
            }
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Flush();
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            lock (sync)
            {
                ThrowIfOwnerReleased();
                ThrowIfBackingDisposed();
                return stream.Read(buffer);
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (sync)
            {
                ThrowIfOwnerReleased();
                ThrowIfBackingDisposed();
                return stream.Seek(offset, origin);
            }
        }

        public override void SetLength(long value)
        {
            lock (sync)
            {
                ThrowIfOwnerReleased();
                ThrowIfBackingDisposed();
                ThrowIfSealed();
                if (value < 0 || value > expectedLength)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                stream.SetLength(value);
                maximumWrittenPosition = Math.Min(maximumWrittenPosition, value);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (sync)
            {
                ThrowIfOwnerReleased();
                ThrowIfBackingDisposed();
                ThrowIfSealed();
                if (stream.Position != maximumWrittenPosition)
                {
                    throw new InvalidOperationException(
                        "Container backing stores only support sequential writes.");
                }

                var endPosition = checked(stream.Position + buffer.Length);
                if (endPosition > expectedLength)
                {
                    throw new InvalidDataException(
                        $"Container decompression exceeded its expected length of {expectedLength} bytes.");
                }

                stream.Write(buffer);
                maximumWrittenPosition = Math.Max(maximumWrittenPosition, endPosition);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref ownerReleased, 1) == 0)
            {
                ReleaseReference();
            }

            base.Dispose(disposing);
        }

        private void ValidateSlice(long offset, long length)
        {
            if (offset < 0)
            {
                throw new InvalidDataException($"Container entry offset cannot be negative: {offset}.");
            }

            if (length < 0)
            {
                throw new InvalidDataException($"Container entry length cannot be negative: {length}.");
            }

            var end = checked(offset + length);
            if (end > expectedLength)
            {
                throw new EndOfStreamException(
                    $"Container entry range [{offset}, {end}) exceeds backing length {expectedLength}.");
            }
        }

        private void ReleaseReference()
        {
            Stream streamToDispose = null;
            lock (sync)
            {
                referenceCount--;
                if (referenceCount < 0)
                {
                    throw new InvalidOperationException("Container backing store reference count underflow.");
                }

                if (referenceCount == 0)
                {
                    backingDisposed = true;
                    streamToDispose = stream;
                }
            }

            if (streamToDispose != null)
            {
                try
                {
                    streamToDispose.Dispose();
                }
                finally
                {
                    manager.ReleaseStore(temporaryPath, memoryReservation);
                }
            }
        }

        private void ThrowIfBackingDisposed()
        {
            if (backingDisposed)
            {
                throw new ObjectDisposedException(nameof(SharedBackingStore));
            }
        }

        private void ThrowIfOwnerReleased()
        {
            if (ownerReleased != 0)
            {
                throw new ObjectDisposedException(nameof(SharedBackingStore));
            }
        }

        private void ThrowIfSealed()
        {
            if (sealedForSlices)
            {
                throw new InvalidOperationException("The container backing store is sealed.");
            }
        }
    }
}
