using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnimeStudio
{
    public class VFSFile
    {
        private List<BundleFile.StorageBlock> m_BlocksInfo;
        private List<BundleFile.Node> m_DirectoryInfo;
        private readonly ContainerStorageManager storageManager;

        public BundleFile.Header m_Header;
        public List<StreamFile> fileList;
        public long Offset;

        public VFSFile(FileReader reader, string path, GameType game)
            : this(reader, path, game, new ContainerStorageManager(new ContainerStorageOptions()), true)
        {
        }

        internal VFSFile(
            FileReader reader,
            string path,
            GameType game,
            ContainerStorageManager storageManager)
            : this(reader, path, game, storageManager, false)
        {
        }

        private VFSFile(
            FileReader reader,
            string path,
            GameType game,
            ContainerStorageManager storageManager,
            bool ownsStorageManager)
        {
            this.storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
            try
            {
                Offset = reader.Position;
                reader.Endian = EndianType.BigEndian;

                if (!VFSUtils.IsValidHeader(reader, game))
                {
                    throw new Exception("Not a VFS file / VFS version mismatch");
                }

                // read header
                reader.ReadBytes(8);
                m_Header = VFSUtils.ReadHeader(reader, game);
                Logger.Verbose($"Header : {m_Header.ToString()}");

                // go to blocks info
                uint blockInfosOffset;

                if ((m_Header.flags & ArchiveFlags.BlocksInfoAtTheEnd) != 0)
                    blockInfosOffset = (uint)(m_Header.size) - m_Header.compressedBlocksInfoSize;
                else
                {
                    if (m_Header.encFlags >= 7)
                        blockInfosOffset = 48;
                    else
                        blockInfosOffset = 40;
                }

                reader.Position = Offset + blockInfosOffset;
                ReadBlocksInfoAndDirectory(reader, game);

                // go to data
                uint dataOffset;

                if (m_Header.encFlags >= 7)
                    dataOffset = 48;
                else
                    dataOffset = 40;
                if (((m_Header.flags) & ArchiveFlags.BlocksInfoAtTheEnd) == 0)
                {
                    var temp = m_Header.compressedBlocksInfoSize;
                    if (((m_Header.flags) & ArchiveFlags.BlockInfoNeedPaddingAtStart) != 0)
                        temp = (temp + 15) & 0xFFFFFFF0;
                    dataOffset += temp;
                }

                reader.Position = Offset + dataOffset;

                using var blocksStream = CreateBlocksStream(path);
                ReadBlocks(reader, blocksStream, game);
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

        private void ReadBlocksInfoAndDirectory(FileReader reader, GameType game)
        {
            byte[] blocksInfoBytes = reader.ReadBytes((int)m_Header.compressedBlocksInfoSize);

            MemoryStream blocksInfoUncompressedStream;
            if (((int)m_Header.flags & 0x3F) != 0)
            {
                // compressed + encrypted
                VFSUtils.DecryptBlock(blocksInfoBytes, game);

                var uncompressedSize = m_Header.uncompressedBlocksInfoSize;
                var blocksInfoBytesSpan = blocksInfoBytes.AsSpan(0, blocksInfoBytes.Length);
                var uncompressedBytes = new byte[checked((int)uncompressedSize)];
                try
                {
                    var uncompressedBytesSpan = uncompressedBytes.AsSpan();
                    // normal LZ4
                    var numWrite = LZ4.Instance.Decompress(blocksInfoBytesSpan, uncompressedBytesSpan);

                    if (numWrite != uncompressedSize)
                    {
                        throw new IOException($"Lz4 decompression error, write {numWrite} bytes but expected {uncompressedSize} bytes");
                    }
                    blocksInfoUncompressedStream = new MemoryStream(uncompressedBytes, writable: false);
                } catch (Exception e) when (e is not OutOfMemoryException)
                {
                    throw new IOException($"Lz4 decompression error {e.Message}");
                }
            } else
            {
                blocksInfoUncompressedStream = new MemoryStream(blocksInfoBytes);
            }

            // read
            using (var blocksInfoReader = new EndianBinaryReader(blocksInfoUncompressedStream))
            {
                reader.Endian = EndianType.BigEndian;
                m_BlocksInfo = VFSUtils.ReadBlocksInfos(blocksInfoReader, game);
                m_DirectoryInfo = VFSUtils.ReadDirectoryInfos(blocksInfoReader, game);
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

        private void ReadBlocks(FileReader reader, Stream blocksStream, GameType game)
        {
            foreach (var blockInfo in m_BlocksInfo)
            {
                var compressionType = (int)blockInfo.flags; // no mask
                Logger.Verbose($"Block compression type {compressionType}");

                switch (compressionType)
                {
                    case 0:
                        var size = (int)blockInfo.uncompressedSize;
                        var buffer = reader.ReadBytes(size);
                        blocksStream.Write(buffer);
                        break;
                    case 5:
                        var compressedSize = (int)blockInfo.compressedSize;
                        var uncompressedSize = (int)blockInfo.uncompressedSize;

                        var compressedBytes = ArrayPool<byte>.Shared.Rent(compressedSize);
                        var uncompressedBytes = ArrayPool<byte>.Shared.Rent(uncompressedSize);

                        var compressedBytesSpan = compressedBytes.AsSpan(0, compressedSize);
                        var uncompressedBytesSpan = uncompressedBytes.AsSpan(0, uncompressedSize);

                        try
                        {
                            reader.Read(compressedBytesSpan);

                            VFSUtils.DecryptBlock(compressedBytesSpan, game);

                            // LZ4Inv this time
                            var numWrite = LZ4Inv.Instance.Decompress(compressedBytesSpan, uncompressedBytesSpan);
                            if (numWrite != uncompressedSize)
                            {
                                Logger.Warning($"Lz4 decompression error, write {numWrite} bytes but expected {uncompressedSize} bytes");
                            }
                        }
                        catch (Exception e) when (e is not OutOfMemoryException)
                        {
                            Logger.Error($"Lz4 decompression error : {e.Message}");
                        }
                        finally
                        {
                            blocksStream.Write(uncompressedBytesSpan);
                            ArrayPool<byte>.Shared.Return(compressedBytes, true);
                            ArrayPool<byte>.Shared.Return(uncompressedBytes, true);
                        }

                        break;
                    default:
                        throw new Exception($"Unsupported block compression type {compressionType}");
                }
            }
        }

        private void ReadFiles(SharedBackingStore blocksStream)
        {
            Logger.Verbose($"Writing files from blocks stream...");
            fileList = ContainerFileStreams.Create(blocksStream, m_DirectoryInfo);
        }
    }
}
