using SevenZip;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AnimeStudio
{
    internal sealed class AssetMapStringCache : IDisposable
    {
        private const int BucketCount = 1 << 20;
        private const int BucketMask = BucketCount - 1;
        private const int NodeLength = 24;
        private const int MaximumStringBytes = 16 * 1024 * 1024;
        private const int FreeSpaceCheckInterval = 64 * 1024 * 1024;
        private const long DefaultCacheByteLimit = 32L * 1024 * 1024;
        private const int DefaultCacheEntryLimit = 65_536;
        private static readonly UTF8Encoding Utf8 = new(false, true);

        private readonly TemporaryFileWorkspace workspace;
        private readonly FileStream indexStream;
        private readonly FileStream valueStream;
        private readonly long[] bucketHeads = new long[BucketCount];
        private readonly Dictionary<uint, LinkedListNode<CachedValue>> cachedByKey = new();
        private readonly LinkedList<CachedValue> cacheLru = new();
        private readonly long cacheByteLimit;
        private readonly int cacheEntryLimit;
        private long indexLength;
        private long valueLength;
        private long cachedBytes;
        private long remainingCheckedBytes = FreeSpaceCheckInterval;
        private int count;
        private bool disposed;

        internal AssetMapStringCache(
            ContainerStorageOptions options,
            long cacheByteLimit = DefaultCacheByteLimit,
            int cacheEntryLimit = DefaultCacheEntryLimit)
        {
            if (cacheByteLimit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cacheByteLimit));
            }
            if (cacheEntryLimit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cacheEntryLimit));
            }

            this.cacheByteLimit = cacheByteLimit;
            this.cacheEntryLimit = cacheEntryLimit;
            Array.Fill(bucketHeads, -1);
            workspace = new TemporaryFileWorkspace(options);
            try
            {
                var indexPath = workspace.CreateFilePath(
                    "asset-map-string-index",
                    ".cache",
                    FreeSpaceCheckInterval);
                var valuePath = workspace.CreateFilePath(
                    "asset-map-string-values",
                    ".cache",
                    FreeSpaceCheckInterval);
                indexStream = new FileStream(
                    indexPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    1,
                    FileOptions.RandomAccess);
                valueStream = new FileStream(
                    valuePath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    1,
                    FileOptions.RandomAccess);
            }
            catch
            {
                indexStream?.Dispose();
                valueStream?.Dispose();
                workspace.Dispose();
                throw;
            }
        }

        internal int Count
        {
            get
            {
                ThrowIfDisposed();
                return count;
            }
        }

        internal string Get(string value)
        {
            if (value == null)
            {
                return null;
            }

            ThrowIfDisposed();
            var key = CRC.CalculateDigestUTF8(value);
            if (cachedByKey.TryGetValue(key, out var cachedNode))
            {
                cacheLru.Remove(cachedNode);
                cacheLru.AddFirst(cachedNode);
                return cachedNode.Value.Value;
            }

            var nodeOffset = bucketHeads[key & BucketMask];
            Span<byte> nodeBytes = stackalloc byte[NodeLength];
            while (nodeOffset >= 0)
            {
                ReadExactlyAt(indexStream, nodeBytes, nodeOffset);
                var storedKey = BinaryPrimitives.ReadUInt32LittleEndian(nodeBytes);
                if (storedKey == key)
                {
                    var byteLength = BinaryPrimitives.ReadInt32LittleEndian(
                        nodeBytes[4..]);
                    var stringOffset = BinaryPrimitives.ReadInt64LittleEndian(
                        nodeBytes[8..]);
                    var storedValue = ReadString(stringOffset, byteLength);
                    Cache(key, storedValue);
                    return storedValue;
                }

                nodeOffset = BinaryPrimitives.ReadInt64LittleEndian(nodeBytes[16..]);
            }

            Add(key, value);
            Cache(key, value);
            return value;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cachedByKey.Clear();
            cacheLru.Clear();
            indexStream.Dispose();
            valueStream.Dispose();
            workspace.Dispose();
        }

        private void Add(uint key, string value)
        {
            var byteLength = Utf8.GetByteCount(value);
            if (byteLength > MaximumStringBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap cached string exceeds {MaximumStringBytes} UTF-8 bytes.");
            }

            var growth = checked(NodeLength + byteLength);
            EnsureFreeSpace(growth);
            var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(1, byteLength));
            try
            {
                var encodedLength = Utf8.GetBytes(
                    value.AsSpan(),
                    bytes.AsSpan(0, byteLength));
                if (encodedLength != byteLength)
                {
                    throw new InvalidDataException(
                        "AssetMap cached string encoding length changed unexpectedly.");
                }
                if (byteLength > 0)
                {
                    RandomAccess.Write(
                        valueStream.SafeFileHandle,
                        bytes.AsSpan(0, byteLength),
                        valueLength);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            Span<byte> nodeBytes = stackalloc byte[NodeLength];
            BinaryPrimitives.WriteUInt32LittleEndian(nodeBytes, key);
            BinaryPrimitives.WriteInt32LittleEndian(nodeBytes[4..], byteLength);
            BinaryPrimitives.WriteInt64LittleEndian(nodeBytes[8..], valueLength);
            var bucket = (int)(key & BucketMask);
            BinaryPrimitives.WriteInt64LittleEndian(nodeBytes[16..], bucketHeads[bucket]);
            RandomAccess.Write(indexStream.SafeFileHandle, nodeBytes, indexLength);

            bucketHeads[bucket] = indexLength;
            indexLength = checked(indexLength + NodeLength);
            valueLength = checked(valueLength + byteLength);
            count = checked(count + 1);
        }

        private string ReadString(long offset, int byteLength)
        {
            if (byteLength < 0 || byteLength > MaximumStringBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap cached string has invalid length {byteLength}.");
            }
            if (offset < 0 || offset > valueLength - byteLength)
            {
                throw new InvalidDataException(
                    "AssetMap cached string offset is outside the value file.");
            }
            if (byteLength == 0)
            {
                return string.Empty;
            }

            var bytes = ArrayPool<byte>.Shared.Rent(byteLength);
            try
            {
                ReadExactlyAt(
                    valueStream,
                    bytes.AsSpan(0, byteLength),
                    offset);
                return Utf8.GetString(bytes, 0, byteLength);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        private void Cache(uint key, string value)
        {
            var estimatedBytes = checked((long)value.Length * sizeof(char));
            if (cacheByteLimit == 0
                || cacheEntryLimit == 0
                || estimatedBytes > cacheByteLimit)
            {
                return;
            }

            while (cacheLru.Count >= cacheEntryLimit
                || cachedBytes > cacheByteLimit - estimatedBytes)
            {
                var last = cacheLru.Last;
                if (last == null)
                {
                    break;
                }

                cacheLru.RemoveLast();
                cachedByKey.Remove(last.Value.Key);
                cachedBytes -= last.Value.EstimatedBytes;
            }

            var cached = new CachedValue(key, value, estimatedBytes);
            var node = cacheLru.AddFirst(cached);
            cachedByKey.Add(key, node);
            cachedBytes = checked(cachedBytes + estimatedBytes);
        }

        private void EnsureFreeSpace(int length)
        {
            if (length > remainingCheckedBytes)
            {
                workspace.EnsureFreeSpace(
                    Math.Max(FreeSpaceCheckInterval, length));
                remainingCheckedBytes =
                    Math.Max(FreeSpaceCheckInterval, length);
            }

            remainingCheckedBytes -= length;
        }

        private static void ReadExactlyAt(
            FileStream stream,
            Span<byte> destination,
            long offset)
        {
            while (!destination.IsEmpty)
            {
                var read = RandomAccess.Read(
                    stream.SafeFileHandle,
                    destination,
                    offset);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"AssetMap string cache ended with {destination.Length} bytes missing.");
                }

                destination = destination[read..];
                offset = checked(offset + read);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AssetMapStringCache));
            }
        }

        private sealed record CachedValue(
            uint Key,
            string Value,
            long EstimatedBytes);
    }
}
