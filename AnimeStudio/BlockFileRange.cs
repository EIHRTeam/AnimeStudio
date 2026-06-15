using System;
using System.Collections.Generic;
using System.IO;

namespace AnimeStudio
{
    internal readonly record struct BlockFileRange(
        long Offset,
        long Length);

    internal static class BlockFileRangeDiscovery
    {
        internal static bool TryDiscover(
            FileReader sourceReader,
            Game game,
            IReadOnlyList<long> requestedOffsets,
            out List<BlockFileRange> ranges)
        {
            ArgumentNullException.ThrowIfNull(sourceReader);
            ArgumentNullException.ThrowIfNull(game);
            ranges = null;
            if (!CanCreateIndependentViews(sourceReader.BaseStream))
            {
                return false;
            }

            try
            {
                var sourceLength = sourceReader.BaseStream.Length;
                var discovered = new List<BlockFileRange>();
                if (requestedOffsets != null
                    && requestedOffsets.Count > 0)
                {
                    foreach (var offset in requestedOffsets)
                    {
                        if (offset < 0 || offset >= sourceLength)
                        {
                            throw new InvalidDataException(
                                $"Requested container offset {offset} is " +
                                $"outside source length {sourceLength}.");
                        }

                        discovered.Add(new BlockFileRange(
                            offset,
                            sourceLength - offset));
                    }
                }
                else
                {
                    long offset = 0;
                    while (offset < sourceLength)
                    {
                        using var stream = CreateView(
                            sourceReader.BaseStream,
                            offset,
                            sourceLength - offset);
                        var dummyPath = Path.Combine(
                            Path.GetDirectoryName(sourceReader.FullPath)
                                ?? string.Empty,
                            offset.ToString("X8"));
                        using var reader = new FileReader(
                            dummyPath,
                            stream,
                            leaveOpen: true);
                        var length = ReadContainerLength(reader, game);
                        if (length <= 0
                            || length > sourceLength - offset)
                        {
                            throw new InvalidDataException(
                                $"Container at offset {offset} reported " +
                                $"invalid length {length}.");
                        }

                        discovered.Add(new BlockFileRange(
                            offset,
                            length));
                        offset = checked(offset + length);
                    }
                }

                if (discovered.Count == 0)
                {
                    return false;
                }

                ranges = discovered;
                return true;
            }
            catch (Exception exception)
                when (exception is not OutOfMemoryException)
            {
                Logger.Verbose(
                    $"Independent block-file range discovery failed: " +
                    $"{exception.Message}");
                ranges = null;
                return false;
            }
        }

        internal static Stream CreateView(
            Stream source,
            long offset,
            long length)
        {
            if (source is FileStream fileStream)
            {
                return new ReadOnlyRandomAccessStream(
                    fileStream.SafeFileHandle,
                    offset,
                    length);
            }

            if (source is MemoryStream memoryStream
                && memoryStream.TryGetBuffer(out var segment))
            {
                return new ReadOnlyRandomAccessStream(
                    segment.Array,
                    checked(segment.Offset + checked((int)offset)),
                    checked((int)length));
            }

            throw new NotSupportedException(
                "The block file cannot create independent read views.");
        }

        private static bool CanCreateIndependentViews(Stream source)
        {
            return source is FileStream
                || source is MemoryStream memoryStream
                    && memoryStream.TryGetBuffer(out _);
        }

        private static long ReadContainerLength(
            FileReader reader,
            Game game)
        {
            return reader.FileType switch
            {
                FileType.VFSFile => VFSFile.ReadContainerLength(
                    reader,
                    game.Type),
                FileType.BundleFile => ReadUnityFsLength(
                    reader,
                    game),
                FileType.ENCRFile => ReadEncrLength(reader),
                _ => throw new NotSupportedException(
                    $"Container range discovery does not support " +
                    $"{reader.FileType}.")
            };
        }

        private static long ReadUnityFsLength(
            FileReader reader,
            Game game)
        {
            if (game.Type.IsBH3Group()
                || game.Type.IsBH3PrePre()
                || game.Type.IsNaraka()
                || game.IsUnityCN()
                || game.Type.IsAzurPromiliaCBT2())
            {
                throw new NotSupportedException(
                    "Encrypted or transformed UnityFS headers require " +
                    "sequential discovery.");
            }

            reader.Position = 0;
            var signature = reader.ReadStringToNull(20);
            if (signature != "UnityFS")
            {
                throw new NotSupportedException(
                    $"Bundle signature {signature} does not expose a " +
                    "UnityFS range length.");
            }

            reader.ReadUInt32();
            reader.ReadStringToNull();
            reader.ReadStringToNull();
            return reader.ReadInt64();
        }

        private static long ReadEncrLength(FileReader reader)
        {
            reader.Position = 0;
            var signature = reader.ReadStringToNull(20);
            if (signature != "ENCR")
            {
                throw new InvalidDataException(
                    $"Expected ENCR signature, found {signature}.");
            }

            return reader.ReadInt64();
        }
    }
}
