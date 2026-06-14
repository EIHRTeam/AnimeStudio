using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AnimeStudio
{
    internal enum AssetMapSpoolOperation
    {
        Created,
        Appending,
        Sealing
    }

    internal sealed class AssetMapEntrySpool : IDisposable
    {
        private const int FormatVersion = 1;
        private const int MaximumStringBytes = 16 * 1024 * 1024;
        private const int MaximumRecordBytes = 64 * 1024 * 1024;
        private const int FreeSpaceCheckInterval = 64 * 1024 * 1024;
        private const long RecordCountOffset = 12;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ASMAPSPL");
        private static readonly UTF8Encoding Utf8 = new(false, true);

        private readonly TemporaryFileWorkspace workspace;
        private readonly string path;
        private readonly Action<AssetMapSpoolOperation> faultInjector;
        private FileStream writeStream;
        private BinaryWriter writer;
        private long count;
        private long remainingCheckedBytes = FreeSpaceCheckInterval;
        private bool sealedForReading;
        private bool disposed;

        internal AssetMapEntrySpool(
            ContainerStorageOptions options,
            Action<AssetMapSpoolOperation> faultInjector = null)
        {
            this.faultInjector = faultInjector;
            workspace = new TemporaryFileWorkspace(options);
            try
            {
                path = workspace.CreateFilePath(
                    "asset-map-entries",
                    ".spool",
                    FreeSpaceCheckInterval);
                writeStream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.SequentialScan);
                writer = new BinaryWriter(writeStream, Utf8, leaveOpen: true);
                writer.Write(Magic);
                writer.Write(FormatVersion);
                writer.Write(0L);
                faultInjector?.Invoke(AssetMapSpoolOperation.Created);
            }
            catch
            {
                writeStream?.Dispose();
                workspace.Dispose();
                throw;
            }
        }

        internal long Count
        {
            get
            {
                ThrowIfDisposed();
                return count;
            }
        }

        internal string TemporaryPath
        {
            get
            {
                ThrowIfDisposed();
                return path;
            }
        }

        internal string WorkspaceDirectory
        {
            get
            {
                ThrowIfDisposed();
                return workspace.DirectoryPath;
            }
        }

        internal void Append(AssetMapEntryRecord entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ThrowIfDisposed();
            if (sealedForReading)
            {
                throw new InvalidOperationException("The AssetMap entry spool is sealed.");
            }

            faultInjector?.Invoke(AssetMapSpoolOperation.Appending);
            var estimatedLength = EstimateRecordLength(entry);
            if (estimatedLength > remainingCheckedBytes)
            {
                workspace.EnsureFreeSpace(Math.Max(FreeSpaceCheckInterval, estimatedLength));
                remainingCheckedBytes = Math.Max(FreeSpaceCheckInterval, estimatedLength);
            }
            remainingCheckedBytes -= estimatedLength;

            var lengthPosition = writeStream.Position;
            writer.Write(0);
            var recordPosition = writeStream.Position;

            WriteNullableString(writer, entry.Name);
            WriteNullableString(writer, entry.Container);
            WriteNullableString(writer, entry.Source);
            writer.Write(entry.PathID);
            writer.Write((int)entry.Type);
            WriteNullableString(writer, entry.Hash);
            writer.Write(entry.Offset);

            var endPosition = writeStream.Position;
            var recordLength = checked((int)(endPosition - recordPosition));
            if (recordLength > MaximumRecordBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap spool record exceeds {MaximumRecordBytes} bytes.");
            }

            writeStream.Position = lengthPosition;
            writer.Write(recordLength);
            writeStream.Position = endPosition;
            count = checked(count + 1);
        }

        internal void Append(AssetEntry entry)
        {
            Append(AssetMapEntryRecord.FromAssetEntry(entry));
        }

        internal void Seal()
        {
            ThrowIfDisposed();
            if (sealedForReading)
            {
                return;
            }

            faultInjector?.Invoke(AssetMapSpoolOperation.Sealing);
            var endPosition = writeStream.Position;
            writeStream.Position = RecordCountOffset;
            writer.Write(count);
            writeStream.Position = endPosition;
            writer.Flush();
            writeStream.Flush(true);
            writer.Dispose();
            writer = null;
            writeStream.Dispose();
            writeStream = null;
            sealedForReading = true;
        }

        internal AssetMapEntryEnumerable ReadEntries()
        {
            ThrowIfDisposed();
            if (!sealedForReading)
            {
                throw new InvalidOperationException(
                    "The AssetMap entry spool must be sealed before reading.");
            }

            return new AssetMapEntryEnumerable(path, count);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            writer?.Dispose();
            writeStream?.Dispose();
            workspace.Dispose();
        }

        private static IEnumerable<AssetMapEntryRecord> ReadEntriesCore(
            string path,
            long expectedCount)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Utf8, leaveOpen: false);

            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length || !magic.SequenceEqual(Magic))
            {
                throw new InvalidDataException("AssetMap spool header is invalid.");
            }

            var version = reader.ReadInt32();
            if (version != FormatVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported AssetMap spool version {version}.");
            }

            var recordCount = reader.ReadInt64();
            if (recordCount < 0 || recordCount != expectedCount)
            {
                throw new InvalidDataException(
                    $"AssetMap spool record count {recordCount} does not match {expectedCount}.");
            }

            for (long index = 0; index < recordCount; index++)
            {
                var recordLength = reader.ReadInt32();
                if (recordLength < 0 || recordLength > MaximumRecordBytes)
                {
                    throw new InvalidDataException(
                        $"AssetMap spool record {index} has invalid length {recordLength}.");
                }

                var recordEnd = checked(stream.Position + recordLength);
                if (recordEnd > stream.Length)
                {
                    throw new EndOfStreamException(
                        $"AssetMap spool record {index} exceeds the file boundary.");
                }

                var entry = new AssetMapEntryRecord
                {
                    Name = ReadNullableString(reader),
                    Container = ReadNullableString(reader),
                    Source = ReadNullableString(reader),
                    PathID = reader.ReadInt64(),
                    Type = (ClassIDType)reader.ReadInt32(),
                    Hash = ReadNullableString(reader),
                    Offset = reader.ReadInt64()
                };

                if (stream.Position != recordEnd)
                {
                    throw new InvalidDataException(
                        $"AssetMap spool record {index} consumed " +
                        $"{stream.Position - (recordEnd - recordLength)} of {recordLength} bytes.");
                }

                yield return entry;
            }

            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("AssetMap spool contains trailing data.");
            }
        }

        private static int EstimateRecordLength(AssetMapEntryRecord entry)
        {
            var length = sizeof(int);
            length = checked(length + GetStringStorageLength(entry.Name));
            length = checked(length + GetStringStorageLength(entry.Container));
            length = checked(length + GetStringStorageLength(entry.Source));
            length = checked(length + sizeof(long));
            length = checked(length + sizeof(int));
            length = checked(length + GetStringStorageLength(entry.Hash));
            length = checked(length + sizeof(long));
            if (length - sizeof(int) > MaximumRecordBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap spool record exceeds {MaximumRecordBytes} bytes.");
            }

            return length;
        }

        private static int GetStringStorageLength(string value)
        {
            if (value == null)
            {
                return sizeof(int);
            }

            var byteCount = Utf8.GetByteCount(value);
            if (byteCount > MaximumStringBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap spool string exceeds {MaximumStringBytes} UTF-8 bytes.");
            }

            return checked(sizeof(int) + byteCount);
        }

        private static void WriteNullableString(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(-1);
                return;
            }

            var bytes = Utf8.GetBytes(value);
            if (bytes.Length > MaximumStringBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap spool string exceeds {MaximumStringBytes} UTF-8 bytes.");
            }

            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadNullableString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length == -1)
            {
                return null;
            }

            if (length < 0 || length > MaximumStringBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap spool string has invalid length {length}.");
            }

            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException(
                    $"AssetMap spool string expected {length} bytes but read {bytes.Length}.");
            }

            return Utf8.GetString(bytes);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AssetMapEntrySpool));
            }
        }

        internal sealed class AssetMapEntryEnumerable :
            IEnumerable<AssetMapEntryRecord>,
            IEnumerable<AssetEntry>
        {
            private readonly string path;
            private readonly long expectedCount;

            internal AssetMapEntryEnumerable(string path, long expectedCount)
            {
                this.path = path;
                this.expectedCount = expectedCount;
            }

            public IEnumerator<AssetMapEntryRecord> GetEnumerator()
            {
                return ReadEntriesCore(path, expectedCount).GetEnumerator();
            }

            IEnumerator<AssetEntry> IEnumerable<AssetEntry>.GetEnumerator()
            {
                foreach (var entry in ReadEntriesCore(path, expectedCount))
                {
                    yield return entry.ToAssetEntry();
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
