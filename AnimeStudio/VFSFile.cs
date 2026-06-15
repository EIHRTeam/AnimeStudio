using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AnimeStudio
{
    public class VFSFile
    {
        internal sealed class Layout
        {
            internal BundleFile.Header Header;
            internal List<BundleFile.StorageBlock> Blocks;
            internal List<BundleFile.Node> Directory;
            internal long DataOffset;
            internal long TotalLength;
        }

        private List<BundleFile.StorageBlock> m_BlocksInfo;
        private List<BundleFile.Node> m_DirectoryInfo;
        private readonly ContainerStorageManager storageManager;
        private readonly int workerCount;
        private readonly CancellationToken cancellationToken;

        public BundleFile.Header m_Header;
        public List<StreamFile> fileList;
        public long Offset;

        public VFSFile(FileReader reader, string path, GameType game)
            : this(
                reader,
                path,
                game,
                new ContainerStorageManager(new ContainerStorageOptions()),
                Math.Max(1, Environment.ProcessorCount),
                CancellationToken.None,
                true)
        {
        }

        internal VFSFile(
            FileReader reader,
            string path,
            GameType game,
            ContainerStorageManager storageManager)
            : this(
                reader,
                path,
                game,
                storageManager,
                Math.Max(1, Environment.ProcessorCount),
                CancellationToken.None,
                false)
        {
        }

        internal VFSFile(
            FileReader reader,
            string path,
            GameType game,
            ContainerStorageManager storageManager,
            int workerCount,
            CancellationToken cancellationToken)
            : this(
                reader,
                path,
                game,
                storageManager,
                workerCount,
                cancellationToken,
                false)
        {
        }

        private VFSFile(
            FileReader reader,
            string path,
            GameType game,
            ContainerStorageManager storageManager,
            int workerCount,
            CancellationToken cancellationToken,
            bool ownsStorageManager)
        {
            this.storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
            this.workerCount = Math.Max(1, workerCount);
            this.cancellationToken = cancellationToken;
            try
            {
                Offset = reader.Position;
                var layout = ReadLayout(
                    reader,
                    game,
                    includeDirectory: true);
                m_Header = layout.Header;
                m_BlocksInfo = layout.Blocks;
                m_DirectoryInfo = layout.Directory;
                Logger.Verbose($"Header : {m_Header.ToString()}");
                reader.Position = checked(Offset + layout.DataOffset);

                using var blocksStream = CreateBlocksStream(path);
                ReadBlocks(reader, blocksStream, game);
                reader.Position = checked(Offset + layout.TotalLength);
                ReadFiles(blocksStream);
            }
            finally
            {
                if (ownsStorageManager)
                {
                    storageManager.Dispose();
                }
            }
        }

        internal static long ReadContainerLength(
            FileReader reader,
            GameType game)
        {
            return ReadLayout(
                reader,
                game,
                includeDirectory: false).TotalLength;
        }

        private static Layout ReadLayout(
            FileReader reader,
            GameType game,
            bool includeDirectory)
        {
            var offset = reader.Position;
            reader.Endian = EndianType.BigEndian;
            if (!VFSUtils.IsValidHeader(reader, game))
            {
                throw new InvalidDataException(
                    "Not a VFS file / VFS version mismatch.");
            }

            reader.ReadBytes(8);
            var header = VFSUtils.ReadHeader(reader, game);
            var blockInfosOffset =
                (header.flags & ArchiveFlags.BlocksInfoAtTheEnd) != 0
                    ? checked(header.size - header.compressedBlocksInfoSize)
                    : header.encFlags >= 7
                        ? 48L
                        : 40L;
            if (blockInfosOffset < 0)
            {
                throw new InvalidDataException(
                    $"VFS block-info offset is negative: " +
                    $"{blockInfosOffset}.");
            }

            reader.Position = checked(offset + blockInfosOffset);
            var blocksInfoBytes = reader.ReadBytes(
                checked((int)header.compressedBlocksInfoSize));

            MemoryStream blocksInfoUncompressedStream;
            if (((int)header.flags & 0x3F) != 0)
            {
                VFSUtils.DecryptBlock(blocksInfoBytes, game);

                var uncompressedSize =
                    header.uncompressedBlocksInfoSize;
                var blocksInfoBytesSpan = blocksInfoBytes.AsSpan();
                var uncompressedBytes = new byte[checked((int)uncompressedSize)];
                try
                {
                    var uncompressedBytesSpan = uncompressedBytes.AsSpan();
                    var numWrite = LZ4.Instance.Decompress(
                        blocksInfoBytesSpan,
                        uncompressedBytesSpan);

                    if (numWrite != uncompressedSize)
                    {
                        throw new InvalidDataException(
                            $"VFS block-info decompression wrote " +
                            $"{numWrite} bytes; expected " +
                            $"{uncompressedSize} bytes.");
                    }
                    blocksInfoUncompressedStream = new MemoryStream(
                        uncompressedBytes,
                        writable: false);
                }
                catch (Exception exception)
                    when (exception is not OutOfMemoryException)
                {
                    throw new IOException(
                        $"VFS block-info decompression failed: " +
                        $"{exception.Message}",
                        exception);
                }
            }
            else
            {
                blocksInfoUncompressedStream = new MemoryStream(blocksInfoBytes);
            }

            List<BundleFile.StorageBlock> blocks;
            List<BundleFile.Node> directory;
            using (blocksInfoUncompressedStream)
            using (var blocksInfoReader = new EndianBinaryReader(
                blocksInfoUncompressedStream))
            {
                blocks = VFSUtils.ReadBlocksInfos(
                    blocksInfoReader,
                    game);
                directory = includeDirectory
                    ? VFSUtils.ReadDirectoryInfos(
                        blocksInfoReader,
                        game)
                    : new List<BundleFile.Node>();
            }

            var dataOffset = header.encFlags >= 7 ? 48L : 40L;
            if ((header.flags & ArchiveFlags.BlocksInfoAtTheEnd) == 0)
            {
                var blockInfoLength =
                    (long)header.compressedBlocksInfoSize;
                if ((header.flags
                        & ArchiveFlags.BlockInfoNeedPaddingAtStart) != 0)
                {
                    blockInfoLength = checked(
                        (blockInfoLength + 15) & ~15L);
                }

                dataOffset = checked(dataOffset + blockInfoLength);
            }

            var compressedDataLength = blocks.Aggregate(
                0L,
                (total, block) =>
                    checked(total + block.compressedSize));
            var dataEnd = checked(dataOffset + compressedDataLength);
            var blockInfoEnd = checked(
                blockInfosOffset + header.compressedBlocksInfoSize);
            var totalLength = Math.Max(dataEnd, blockInfoEnd);
            var availableLength = reader.Length - offset;
            if (totalLength <= 0 || totalLength > availableLength)
            {
                throw new InvalidDataException(
                    $"VFS container length {totalLength} is outside " +
                    $"the available range {availableLength}.");
            }

            return new Layout
            {
                Header = header,
                Blocks = blocks,
                Directory = directory,
                DataOffset = dataOffset,
                TotalLength = totalLength
            };
        }

        private SharedBackingStore CreateBlocksStream(string path)
        {
            var uncompressedSizeSum = m_BlocksInfo.Aggregate(
                0L,
                (total, block) => checked(total + block.uncompressedSize));
            Logger.Verbose($"Total size of decompressed blocks: 0x{uncompressedSizeSum:X}");
            return storageManager.Create(uncompressedSizeSum, path);
        }

        private void ReadBlocks(
            FileReader reader,
            SharedBackingStore blocksStream,
            GameType game)
        {
            ContainerBlockPipeline.Process(
                reader,
                m_BlocksInfo,
                blocksStream,
                workerCount,
                cancellationToken,
                (
                    blockIndex,
                    blockInfo,
                    compressedBuffer,
                    compressedLength,
                    uncompressedBuffer,
                    uncompressedLength) =>
                {
                    var compressed = compressedBuffer.AsSpan(
                        0,
                        compressedLength);
                    var uncompressed = uncompressedBuffer.AsSpan(
                        0,
                        uncompressedLength);
                    var compressionType = (int)blockInfo.flags;
                    Logger.Verbose(
                        $"Block {blockIndex} compression type " +
                        $"{compressionType}");
                    switch (compressionType)
                    {
                        case 0:
                        {
                            if (compressed.Length != uncompressed.Length)
                            {
                                throw new InvalidDataException(
                                    $"Uncompressed VFS block {blockIndex} " +
                                    $"contains {compressed.Length} bytes; " +
                                    $"expected {uncompressed.Length} bytes.");
                            }

                            compressed.CopyTo(uncompressed);
                            return uncompressed.Length;
                        }
                        case 5:
                        {
                            VFSUtils.DecryptBlock(compressed, game);
                            return LZ4Inv.Instance.Decompress(
                                compressed,
                                uncompressed);
                        }
                        default:
                            throw new InvalidDataException(
                                $"Unsupported VFS block compression type " +
                                $"{compressionType}.");
                    }
                });
        }

        private void ReadFiles(SharedBackingStore blocksStream)
        {
            Logger.Verbose($"Writing files from blocks stream...");
            fileList = ContainerFileStreams.Create(blocksStream, m_DirectoryInfo);
        }
    }
}
