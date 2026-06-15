using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeStudio
{
    internal sealed class ReadOnlyRandomAccessStream : Stream
    {
        private readonly SafeFileHandle fileHandle;
        private readonly byte[] memoryBuffer;
        private readonly int memoryOffset;
        private readonly long sourceOffset;
        private readonly long length;
        private long position;
        private int disposed;

        internal ReadOnlyRandomAccessStream(
            SafeFileHandle fileHandle,
            long sourceOffset,
            long length)
        {
            this.fileHandle = fileHandle
                ?? throw new ArgumentNullException(nameof(fileHandle));
            this.sourceOffset = sourceOffset;
            this.length = length;
            ValidateRange(sourceOffset, length);
        }

        internal ReadOnlyRandomAccessStream(
            byte[] memoryBuffer,
            int memoryOffset,
            int length)
        {
            this.memoryBuffer = memoryBuffer
                ?? throw new ArgumentNullException(nameof(memoryBuffer));
            this.memoryOffset = memoryOffset;
            this.length = length;
            if (memoryOffset < 0
                || length < 0
                || memoryOffset > memoryBuffer.Length - length)
            {
                throw new ArgumentOutOfRangeException(nameof(memoryOffset));
            }
        }

        public override bool CanRead => disposed == 0;

        public override bool CanSeek => disposed == 0;

        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                ThrowIfDisposed();
                return length;
            }
        }

        public override long Position
        {
            get
            {
                ThrowIfDisposed();
                return position;
            }
            set
            {
                ThrowIfDisposed();
                if (value < 0 || value > length)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                position = value;
            }
        }

        public override void Flush()
        {
            ThrowIfDisposed();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            ThrowIfDisposed();
            var remaining = length - position;
            if (remaining == 0 || buffer.Length == 0)
            {
                return 0;
            }

            var count = (int)Math.Min(buffer.Length, remaining);
            int bytesRead;
            if (memoryBuffer != null)
            {
                memoryBuffer.AsSpan(
                        checked(memoryOffset + checked((int)position)),
                        count)
                    .CopyTo(buffer);
                bytesRead = count;
            }
            else
            {
                bytesRead = RandomAccess.Read(
                    fileHandle,
                    buffer[..count],
                    checked(sourceOffset + position));
            }

            position += bytesRead;
            return bytesRead;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            var newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(position + offset),
                SeekOrigin.End => checked(length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (newPosition < 0 || newPosition > length)
            {
                throw new IOException(
                    $"Cannot seek outside the range [0, {length}]: " +
                    $"requested {newPosition}.");
            }

            position = newPosition;
            return position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("The range is read-only.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The range is read-only.");
        }

        protected override void Dispose(bool disposing)
        {
            Interlocked.Exchange(ref disposed, 1);
            base.Dispose(disposing);
        }

        private static void ValidateRange(long offset, long length)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            _ = checked(offset + length);
        }

        private void ThrowIfDisposed()
        {
            if (disposed != 0)
            {
                throw new ObjectDisposedException(
                    nameof(ReadOnlyRandomAccessStream));
            }
        }
    }
}
