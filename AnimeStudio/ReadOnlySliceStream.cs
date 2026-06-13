using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeStudio
{
    internal sealed class ReadOnlySliceStream : Stream
    {
        private readonly SharedBackingStore backingStore;
        private readonly long offset;
        private readonly long length;
        private long position;
        private int disposed;

        internal ReadOnlySliceStream(SharedBackingStore backingStore, long offset, long length)
        {
            this.backingStore = backingStore;
            this.offset = offset;
            this.length = length;
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

        public override int Read(byte[] buffer, int bufferOffset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(bufferOffset, count));
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
            var bytesRead = backingStore.ReadAt(checked(offset + position), buffer[..count]);
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

        public override long Seek(long seekOffset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            var newPosition = origin switch
            {
                SeekOrigin.Begin => seekOffset,
                SeekOrigin.Current => checked(position + seekOffset),
                SeekOrigin.End => checked(length + seekOffset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (newPosition < 0 || newPosition > length)
            {
                throw new IOException(
                    $"Cannot seek outside the slice boundary [0, {length}]: requested {newPosition}.");
            }

            position = newPosition;
            return position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Container slices are read-only.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Container slices are read-only.");
        }

        protected override void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                backingStore.ReleaseSlice();
            }

            base.Dispose(disposing);
        }

        private void ThrowIfDisposed()
        {
            if (disposed != 0)
            {
                throw new ObjectDisposedException(nameof(ReadOnlySliceStream));
            }
        }
    }
}
