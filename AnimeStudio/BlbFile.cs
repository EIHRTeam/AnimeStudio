using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AnimeStudio
{
    public class Blb3File
    {
        private List<BundleFile.StorageBlock> m_BlocksInfo;
        private List<BundleFile.Node> m_DirectoryInfo;
        private byte[] Header;
        private readonly ContainerStorageManager storageManager;
        private readonly int workerCount;
        private readonly CancellationToken cancellationToken;

        public BundleFile.Header m_Header;
        public List<StreamFile> fileList;
        public long Offset;

        public Blb3File(FileReader reader, string path)
            : this(
                reader,
                path,
                new ContainerStorageManager(new ContainerStorageOptions()),
                Math.Max(1, Environment.ProcessorCount),
                CancellationToken.None,
                true)
        {
        }

        internal Blb3File(FileReader reader, string path, ContainerStorageManager storageManager)
            : this(
                reader,
                path,
                storageManager,
                Math.Max(1, Environment.ProcessorCount),
                CancellationToken.None,
                false)
        {
        }

        internal Blb3File(
            FileReader reader,
            string path,
            ContainerStorageManager storageManager,
            int workerCount,
            CancellationToken cancellationToken)
            : this(
                reader,
                path,
                storageManager,
                workerCount,
                cancellationToken,
                false)
        {
        }

        private Blb3File(
            FileReader reader,
            string path,
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
                BlbUtils.InitKeys(CryptoHelper.Blb3RC4Key, CryptoHelper.Blb3SBox, CryptoHelper.Blb3ShiftRow, CryptoHelper.Blb3Key, CryptoHelper.Blb3Mul);

                Offset = reader.Position;
                reader.Endian = EndianType.LittleEndian;

                var signature = reader.ReadStringToNull(4);
                Logger.Verbose($"Parsed signature {signature}");
                if (signature != "Blb\x03")
                    throw new Exception("not a Blb3 file");

                var size = reader.ReadUInt32();
                m_Header = new BundleFile.Header
                {
                    version = 6,
                    unityVersion = "5.x.x",
                    unityRevision = "2017.4.30f1",
                    flags = 0
                };
                m_Header.compressedBlocksInfoSize = size;
                m_Header.uncompressedBlocksInfoSize = size;

                Logger.Verbose($"Header: {m_Header}");
                reader.ReadUInt32();
                Header = reader.ReadBytes(16);

                var header = reader.ReadBytes((int)m_Header.compressedBlocksInfoSize);

                BlbUtils.Decrypt(Header, header);

                ReadBlocksInfoAndDirectory(header);
                using var blocksStream = CreateBlocksStream(path);
                ReadBlocks(reader, blocksStream);
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

        private void ReadBlocksInfoAndDirectory(byte[] header)
        {
            using var stream = new MemoryStream(header);
            using var reader = new EndianBinaryReader(stream, EndianType.LittleEndian);

            m_Header.size = reader.ReadUInt32();
            var lastUncompressedSize = reader.ReadUInt32();

            reader.Position += 4;
            var blobOffset = reader.ReadInt32();
            var blobSize = reader.ReadUInt32();
            var compressionType = (CompressionType)reader.ReadByte();
            var uncompressedSize = (uint)1 << reader.ReadByte();
            reader.AlignStream();

            var blocksInfoCount = reader.ReadInt32();
            var nodesCount = reader.ReadInt32();

            var blocksInfoOffset = reader.Position + reader.ReadInt64();
            var nodesInfoOffset = reader.Position + reader.ReadInt64();
            var flagInfoOffset = reader.Position + reader.ReadInt64();

            reader.Position = blocksInfoOffset;
            m_BlocksInfo = new List<BundleFile.StorageBlock>();
            Logger.Verbose($"Blocks count: {blocksInfoCount}");
            for (int i = 0; i < blocksInfoCount; i++)
            {
                m_BlocksInfo.Add(new BundleFile.StorageBlock
                {
                    compressedSize = reader.ReadUInt32(),
                    uncompressedSize = i == blocksInfoCount - 1 ? lastUncompressedSize : uncompressedSize,
                    flags = (StorageBlockFlags)compressionType
                });

                Logger.Verbose($"Block {i} Info: {m_BlocksInfo[i]}");
            }
            for (int i = m_BlocksInfo.Count - 1; i > 0; i--)
            {
                m_BlocksInfo[i].compressedSize -= m_BlocksInfo[i - 1].compressedSize;
                m_BlocksInfo[i].flags = (StorageBlockFlags)(m_BlocksInfo[i].compressedSize == m_BlocksInfo[i].uncompressedSize ? CompressionType.None : compressionType);
            }

            reader.Position = nodesInfoOffset;
            m_DirectoryInfo = new List<BundleFile.Node>();
            Logger.Verbose($"Directory count: {nodesCount}");
            for (int i = 0; i < nodesCount; i++)
            {
                m_DirectoryInfo.Add(new BundleFile.Node
                {
                    offset = reader.ReadInt32(),
                    size = reader.ReadInt32()
                });

                var pos = reader.Position;
                reader.Position = flagInfoOffset;
                var flag = reader.ReadUInt32();
                if (i >= 0x20)
                {
                    flag = reader.ReadUInt32();
                }
                m_DirectoryInfo[i].flags = (uint)(flag & (1 << i)) * 4;
                reader.Position = pos;

                var pathOffset = reader.Position + reader.ReadInt64();

                pos = reader.Position;
                reader.Position = pathOffset;
                m_DirectoryInfo[i].path = reader.ReadStringToNull();
                reader.Position = pos;

                Logger.Verbose($"Directory {i} Info: {m_DirectoryInfo[i]}");
            }
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
            SharedBackingStore blocksStream)
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
                    var compressionType = (CompressionType)(
                        blockInfo.flags
                        & StorageBlockFlags.CompressionTypeMask);
                    Logger.Verbose(
                        $"Block {blockIndex} compression type " +
                        $"{compressionType}");
                    switch (compressionType)
                    {
                        case CompressionType.None:
                            if (compressedLength != uncompressedLength)
                            {
                                throw new InvalidDataException(
                                    $"Uncompressed BLB block {blockIndex} " +
                                    $"contains {compressedLength} bytes; " +
                                    $"expected {uncompressedLength} bytes.");
                            }
                            BlbUtils.Decrypt(Header, compressed);
                            compressed.CopyTo(uncompressed);
                            return uncompressedLength;
                        case CompressionType.Oodle:
                            if (compressedLength > 6)
                            {
                                BlbUtils.Decrypt(Header, compressed);
                            }
                            return OodleHelper.Decompress(
                                compressed,
                                uncompressed);
                        case CompressionType.Lzma:
                            using (var input = new MemoryStream(
                                compressedBuffer,
                                0,
                                compressedLength,
                                writable: false,
                                publiclyVisible: true))
                            using (var output = new MemoryStream(
                                uncompressedBuffer,
                                0,
                                uncompressedLength,
                                writable: true,
                                publiclyVisible: true))
                            {
                                output.SetLength(0);
                                SevenZipHelper.StreamDecompress(
                                    input,
                                    output,
                                    compressedLength,
                                    uncompressedLength);
                                return checked((int)output.Position);
                            }
                        case CompressionType.Lz4:
                        case CompressionType.Lz4HC:
                            BlbUtils.Decrypt(Header, compressed);
                            return LZ4.Instance.Decompress(
                                compressed,
                                uncompressed);
                        default:
                            throw new InvalidDataException(
                                $"Unsupported BLB block compression type " +
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
