using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace AnimeStudio
{
    internal static class AssetMapStreamingIO
    {
        private const int MaximumStringBytes = 16 * 1024 * 1024;
        private const int MaximumRecordCount = 100_000_000;
        private const int MaximumBlockCount = 1_000_000;
        private const int MaximumBlockBytes = 64 * 1024 * 1024;
        private const int FreeSpaceCheckInterval = 64 * 1024 * 1024;
        private const long MaximumJsonOrXmlFileBytes = 64L * 1024 * 1024 * 1024;
        private const sbyte Lz4BlockArrayTypeCode = 98;
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private static readonly int CompressionMinLength =
            MessagePackSerializerOptions.Standard.CompressionMinLength;

        internal static void WriteMaps(
            AssetMapEntrySpool spool,
            Game game,
            string name,
            string savePath,
            ExportListType exportListType,
            ContainerStorageOptions storageOptions,
            AssetMapBuildMetrics metrics = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(spool);
            ArgumentNullException.ThrowIfNull(game);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(savePath);

            cancellationToken.ThrowIfCancellationRequested();
            var previousCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

                if (exportListType.HasFlag(ExportListType.XML))
                {
                    using var measurement =
                        metrics?.Measure(AssetMapBuildStage.XmlWriting);
                    WriteXml(
                        spool,
                        Path.Combine(savePath, $"{name}.xml"),
                        cancellationToken);
                }

                if (exportListType.HasFlag(ExportListType.JSON))
                {
                    using var measurement =
                        metrics?.Measure(AssetMapBuildStage.JsonWriting);
                    WriteJson(
                        spool,
                        game.Type,
                        Path.Combine(savePath, $"{name}.json"),
                        cancellationToken);
                }

                if (exportListType.HasFlag(ExportListType.MessagePack))
                {
                    using var measurement =
                        metrics?.Measure(AssetMapBuildStage.MessagePackWriting);
                    WriteMessagePack(
                        spool,
                        game.Type,
                        Path.Combine(savePath, $"{name}.map"),
                        storageOptions,
                        cancellationToken);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
            }
        }

        internal static string[] ReadSources(
            string mapName,
            ExportListType mapType,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            ContainerStorageOptions storageOptions,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
            typeFilter ??= Array.Empty<ClassIDType>();
            nameFilter ??= Array.Empty<Regex>();
            containerFilter ??= Array.Empty<Regex>();

            cancellationToken.ThrowIfCancellationRequested();
            return mapType switch
            {
                ExportListType.XML => ReadXmlSources(
                    mapName,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    cancellationToken),
                ExportListType.JSON => ReadJsonSources(
                    mapName,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    cancellationToken),
                ExportListType.MessagePack => ReadMessagePackSources(
                    mapName,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    storageOptions,
                    cancellationToken),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(mapType),
                    mapType,
                    "Exactly one AssetMap format must be selected.")
            };
        }

        private static void WriteXml(
            AssetMapEntrySpool spool,
            string filename,
            CancellationToken cancellationToken)
        {
            try
            {
                var settings = new XmlWriterSettings { Indent = true };
                using var writer = XmlWriter.Create(filename, settings);
                writer.WriteStartDocument();
                writer.WriteStartElement("Assets");
                writer.WriteAttributeString("filename", filename);
                writer.WriteAttributeString("createdAt", DateTime.UtcNow.ToString("s"));
                foreach (var asset in spool.ReadEntries())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.WriteStartElement("Asset");
                    writer.WriteElementString("Name", asset.Name);
                    writer.WriteElementString("Container", asset.Container);
                    writer.WriteStartElement("Type");
                    writer.WriteAttributeString("id", ((int)asset.Type).ToString());
                    writer.WriteValue(asset.Type.ToString());
                    writer.WriteEndElement();
                    writer.WriteElementString("PathID", asset.PathID.ToString());
                    writer.WriteElementString("Source", asset.Source);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            catch
            {
                DeletePartialFile(filename);
                throw;
            }
        }

        private static void WriteJson(
            AssetMapEntrySpool spool,
            GameType gameType,
            string filename,
            CancellationToken cancellationToken)
        {
            try
            {
                using var file = File.CreateText(filename);
                using var writer = new JsonTextWriter(file)
                {
                    Formatting = Newtonsoft.Json.Formatting.Indented
                };
                var serializer = CreateJsonSerializer();

                writer.WriteStartObject();
                writer.WritePropertyName(nameof(AssetMap.GameType));
                serializer.Serialize(writer, gameType);
                writer.WritePropertyName(nameof(AssetMap.AssetEntries));
                writer.WriteStartArray();
                foreach (var entry in spool.ReadEntries())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    serializer.Serialize(writer, entry);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            catch
            {
                DeletePartialFile(filename);
                throw;
            }
        }

        private static void WriteMessagePack(
            AssetMapEntrySpool spool,
            GameType gameType,
            string filename,
            ContainerStorageOptions storageOptions,
            CancellationToken cancellationToken)
        {
            if (spool.Count > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"AssetMap contains {spool.Count} records; MessagePack supports at most " +
                    $"{int.MaxValue} entries in this schema.");
            }

            try
            {
                using var workspace = new TemporaryFileWorkspace(storageOptions);
                var canonicalPath = workspace.CreateFilePath(
                    "asset-map-canonical",
                    ".msgpack",
                    FreeSpaceCheckInterval);
                var segmentLengthsPath = workspace.CreateFilePath(
                    "asset-map-segments",
                    ".lengths",
                    FreeSpaceCheckInterval);

                using var canonicalStream = new FileStream(
                    canonicalPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.SequentialScan);
                using var segmentLengthsStream = new FileStream(
                    segmentLengthsPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.SequentialScan);
                using var segmentedWriter = new SegmentedCanonicalBufferWriter(
                    canonicalStream,
                    segmentLengthsStream,
                    workspace);

                var writer = new MessagePackWriter(segmentedWriter)
                {
                    CancellationToken = cancellationToken
                };
                writer.WriteArrayHeader(2);
                writer.Write((int)gameType);
                writer.WriteArrayHeader((int)spool.Count);
                foreach (var entry in spool.ReadEntries())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteMessagePackEntry(ref writer, entry);
                }

                writer.Flush();
                segmentedWriter.Complete();
                canonicalStream.Flush(true);
                segmentLengthsStream.Flush(true);

                canonicalStream.Position = 0;
                segmentLengthsStream.Position = 0;
                using var output = new FileStream(
                    filename,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.SequentialScan);

                if (segmentedWriter.TotalLength < CompressionMinLength)
                {
                    CopyTo(
                        canonicalStream,
                        output,
                        segmentedWriter.TotalLength,
                        cancellationToken);
                }
                else
                {
                    WriteCompressedMessagePack(
                        canonicalStream,
                        segmentLengthsStream,
                        segmentedWriter,
                        output,
                        cancellationToken);
                }

                output.Flush(true);
            }
            catch
            {
                DeletePartialFile(filename);
                throw;
            }
        }

        private static void WriteMessagePackEntry(
            ref MessagePackWriter writer,
            AssetMapEntryRecord entry)
        {
            writer.WriteArrayHeader(7);
            writer.Write(entry.Name);
            writer.Write(entry.Container);
            writer.Write(entry.Source);
            writer.Write(entry.PathID);
            writer.Write((int)entry.Type);
            writer.Write(entry.Hash);
            writer.Write(entry.Offset);
        }

        private static void WriteCompressedMessagePack(
            Stream canonicalStream,
            Stream segmentLengthsStream,
            SegmentedCanonicalBufferWriter segmentedWriter,
            Stream output,
            CancellationToken cancellationToken)
        {
            if (segmentedWriter.SegmentCount >= int.MaxValue)
            {
                throw new InvalidDataException(
                    "AssetMap MessagePack has too many compression blocks.");
            }

            using var outputBuffer = new StreamBufferWriter(output);
            var writer = new MessagePackWriter(outputBuffer)
            {
                CancellationToken = cancellationToken
            };
            writer.WriteArrayHeader(checked(segmentedWriter.SegmentCount + 1));
            writer.WriteExtensionFormatHeader(
                new ExtensionHeader(
                    Lz4BlockArrayTypeCode,
                    segmentedWriter.ExtensionHeaderSize));

            using (var lengthReader = new BinaryReader(
                segmentLengthsStream,
                Utf8,
                leaveOpen: true))
            {
                for (var index = 0; index < segmentedWriter.SegmentCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.Write(lengthReader.ReadInt32());
                }
            }

            segmentLengthsStream.Position = 0;
            var bin32Header = new byte[5];
            bin32Header[0] = MessagePackCode.Bin32;
            using (var lengthReader = new BinaryReader(
                segmentLengthsStream,
                Utf8,
                leaveOpen: true))
            {
                for (var index = 0; index < segmentedWriter.SegmentCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var uncompressedLength = lengthReader.ReadInt32();
                    var maximumCompressedLength =
                        Lz4CodecBindings.Instance.MaximumOutputLength(uncompressedLength);
                    var uncompressed = ArrayPool<byte>.Shared.Rent(uncompressedLength);
                    var compressed = ArrayPool<byte>.Shared.Rent(maximumCompressedLength);
                    try
                    {
                        ReadExactly(
                            canonicalStream,
                            uncompressed.AsSpan(0, uncompressedLength));
                        var compressedLength = Lz4CodecBindings.Instance.Encode(
                            uncompressed.AsSpan(0, uncompressedLength),
                            compressed.AsSpan(0, maximumCompressedLength));
                        if (compressedLength <= 0
                            || compressedLength > maximumCompressedLength)
                        {
                            throw new InvalidDataException(
                                $"LZ4 encoder returned invalid length {compressedLength}.");
                        }

                        BinaryPrimitives.WriteUInt32BigEndian(
                            bin32Header.AsSpan(1),
                            (uint)compressedLength);
                        writer.WriteRaw(bin32Header);
                        writer.WriteRaw(compressed.AsSpan(0, compressedLength));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(uncompressed);
                        ArrayPool<byte>.Shared.Return(compressed);
                    }
                }
            }

            if (canonicalStream.Position != canonicalStream.Length)
            {
                throw new InvalidDataException(
                    "Canonical MessagePack segment lengths do not cover the staging file.");
            }

            writer.Flush();
            outputBuffer.Flush();
        }

        private static string[] ReadXmlSources(
            string mapName,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            CancellationToken cancellationToken)
        {
            EnsureReasonableInputLength(mapName);
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumJsonOrXmlFileBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };

            using var stream = File.OpenRead(mapName);
            using var reader = XmlReader.Create(stream, settings);
            cancellationToken.ThrowIfCancellationRequested();
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Assets")
            {
                throw new InvalidDataException("AssetMap XML root element must be 'Assets'.");
            }

            if (reader.IsEmptyElement)
            {
                reader.Read();
                if (reader.MoveToContent() != XmlNodeType.None)
                {
                    throw new InvalidDataException(
                        "AssetMap XML contains trailing content.");
                }

                return Array.Empty<string>();
            }

            reader.ReadStartElement("Assets");
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.LocalName != "Asset")
                {
                    throw new InvalidDataException(
                        $"Unexpected AssetMap XML element '{reader.LocalName}'.");
                }

                ReadXmlEntry(
                    reader,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    seen,
                    ordered);
            }

            reader.ReadEndElement();
            if (reader.MoveToContent() != XmlNodeType.None)
            {
                throw new InvalidDataException("AssetMap XML contains trailing content.");
            }

            return ordered.ToArray();
        }

        private static void ReadXmlEntry(
            XmlReader reader,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            HashSet<string> seen,
            List<string> ordered)
        {
            string name = null;
            string container = null;
            string source = null;
            var type = default(ClassIDType);
            var sawName = false;
            var sawContainer = false;
            var sawSource = false;
            var sawType = false;

            if (reader.IsEmptyElement)
            {
                throw new InvalidDataException("AssetMap XML entry cannot be empty.");
            }

            reader.ReadStartElement("Asset");
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "Name":
                        name = ReadBoundedXmlString(reader);
                        sawName = true;
                        break;
                    case "Container":
                        container = ReadBoundedXmlString(reader);
                        sawContainer = true;
                        break;
                    case "Type":
                        var typeId = reader.GetAttribute("id");
                        var typeName = ReadBoundedXmlString(reader);
                        type = ParseClassIdType(typeName, typeId);
                        sawType = true;
                        break;
                    case "Source":
                        source = ReadBoundedXmlString(reader);
                        sawSource = true;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            reader.ReadEndElement();
            if (!sawName || !sawContainer || !sawType || !sawSource)
            {
                throw new InvalidDataException(
                    "AssetMap XML entry is missing a required field.");
            }

            AddIfMatch(
                name,
                container,
                source,
                type,
                typeFilter,
                nameFilter,
                containerFilter,
                seen,
                ordered);
        }

        private static string ReadBoundedXmlString(XmlReader reader)
        {
            var value = reader.ReadElementContentAsString();
            EnsureStringLength(value);
            return value;
        }

        private static string[] ReadJsonSources(
            string mapName,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            CancellationToken cancellationToken)
        {
            EnsureReasonableInputLength(mapName);
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            using var stream = File.OpenRead(mapName);
            using var text = new StreamReader(
                stream,
                Utf8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 64 * 1024,
                leaveOpen: false);
            using var reader = new JsonTextReader(text)
            {
                DateParseHandling = DateParseHandling.None,
                MaxDepth = 16,
                SupportMultipleContent = false
            };

            ReadRequiredJsonToken(reader);
            if (reader.TokenType == JsonToken.StartObject)
            {
                ReadJsonWrapper(
                    reader,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    seen,
                    ordered,
                    cancellationToken);
            }
            else if (reader.TokenType == JsonToken.StartArray)
            {
                ReadJsonEntries(
                    reader,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    seen,
                    ordered,
                    cancellationToken);
            }
            else
            {
                throw new InvalidDataException(
                    "AssetMap JSON must be a wrapper object or an entry array.");
            }

            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.Comment)
                {
                    throw new InvalidDataException(
                        "AssetMap JSON contains trailing content.");
                }
            }

            return ordered.ToArray();
        }

        private static void ReadJsonWrapper(
            JsonTextReader reader,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            HashSet<string> seen,
            List<string> ordered,
            CancellationToken cancellationToken)
        {
            var sawEntries = false;
            while (ReadRequiredJsonToken(reader) != JsonToken.EndObject)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.TokenType != JsonToken.PropertyName)
                {
                    throw new InvalidDataException(
                        "AssetMap JSON wrapper contains a non-property token.");
                }

                var propertyName = (string)reader.Value;
                ReadRequiredJsonToken(reader);
                if (string.Equals(
                    propertyName,
                    nameof(AssetMap.AssetEntries),
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (sawEntries || reader.TokenType != JsonToken.StartArray)
                    {
                        throw new InvalidDataException(
                            "AssetMap JSON has an invalid AssetEntries property.");
                    }

                    sawEntries = true;
                    ReadJsonEntries(
                        reader,
                        typeFilter,
                        nameFilter,
                        containerFilter,
                        seen,
                        ordered,
                        cancellationToken);
                }
                else
                {
                    reader.Skip();
                }
            }

            if (!sawEntries)
            {
                throw new InvalidDataException(
                    "AssetMap JSON wrapper has no AssetEntries property.");
            }
        }

        private static void ReadJsonEntries(
            JsonTextReader reader,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            HashSet<string> seen,
            List<string> ordered,
            CancellationToken cancellationToken)
        {
            var count = 0;
            while (ReadRequiredJsonToken(reader) != JsonToken.EndArray)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.TokenType != JsonToken.StartObject)
                {
                    throw new InvalidDataException(
                        "AssetMap JSON entries must be objects.");
                }

                count = checked(count + 1);
                if (count > MaximumRecordCount)
                {
                    throw new InvalidDataException(
                        $"AssetMap JSON exceeds {MaximumRecordCount} entries.");
                }

                ReadJsonEntry(
                    reader,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    seen,
                    ordered);
            }
        }

        private static void ReadJsonEntry(
            JsonTextReader reader,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            HashSet<string> seen,
            List<string> ordered)
        {
            string name = null;
            string container = null;
            string source = null;
            var type = default(ClassIDType);
            var sawName = false;
            var sawContainer = false;
            var sawSource = false;
            var sawType = false;

            while (ReadRequiredJsonToken(reader) != JsonToken.EndObject)
            {
                if (reader.TokenType != JsonToken.PropertyName)
                {
                    throw new InvalidDataException(
                        "AssetMap JSON entry contains a non-property token.");
                }

                var propertyName = (string)reader.Value;
                ReadRequiredJsonToken(reader);
                if (string.Equals(
                    propertyName,
                    nameof(AssetEntry.Name),
                    StringComparison.OrdinalIgnoreCase))
                {
                    name = ReadNullableJsonString(reader);
                    sawName = true;
                }
                else if (string.Equals(
                    propertyName,
                    nameof(AssetEntry.Container),
                    StringComparison.OrdinalIgnoreCase))
                {
                    container = ReadNullableJsonString(reader);
                    sawContainer = true;
                }
                else if (string.Equals(
                    propertyName,
                    nameof(AssetEntry.Source),
                    StringComparison.OrdinalIgnoreCase))
                {
                    source = ReadNullableJsonString(reader);
                    sawSource = true;
                }
                else if (string.Equals(
                    propertyName,
                    nameof(AssetEntry.Type),
                    StringComparison.OrdinalIgnoreCase))
                {
                    type = ReadJsonClassIdType(reader);
                    sawType = true;
                }
                else
                {
                    reader.Skip();
                }
            }

            if (!sawName || !sawContainer || !sawType || !sawSource)
            {
                throw new InvalidDataException(
                    "AssetMap JSON entry is missing a required field.");
            }

            AddIfMatch(
                name,
                container,
                source,
                type,
                typeFilter,
                nameFilter,
                containerFilter,
                seen,
                ordered);
        }

        private static JsonToken ReadRequiredJsonToken(JsonTextReader reader)
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.Comment)
                {
                    return reader.TokenType;
                }
            }

            throw new EndOfStreamException("AssetMap JSON ended unexpectedly.");
        }

        private static string ReadNullableJsonString(JsonTextReader reader)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonToken.String)
            {
                throw new InvalidDataException(
                    $"Expected AssetMap JSON string, found {reader.TokenType}.");
            }

            var value = (string)reader.Value;
            EnsureStringLength(value);
            return value;
        }

        private static ClassIDType ReadJsonClassIdType(JsonTextReader reader)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var value = (string)reader.Value;
                if (Enum.TryParse<ClassIDType>(value, true, out var type))
                {
                    return type;
                }
            }
            else if (reader.TokenType == JsonToken.Integer)
            {
                try
                {
                    return (ClassIDType)Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                }
                catch (Exception exception)
                    when (exception is FormatException
                        or InvalidCastException
                        or OverflowException)
                {
                }
            }

            throw new InvalidDataException(
                $"Invalid AssetMap JSON type value '{reader.Value}'.");
        }

        private static string[] ReadMessagePackSources(
            string mapName,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            ContainerStorageOptions storageOptions,
            CancellationToken cancellationToken)
        {
            using var stream = File.OpenRead(mapName);
            var reader = new MessagePackStreamTokenReader(stream, cancellationToken);
            var outerCount = reader.ReadArrayHeader();
            if (reader.NextTokenIsExtension)
            {
                return ReadCompressedMessagePackSources(
                    reader,
                    outerCount,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    storageOptions,
                    cancellationToken);
            }

            return ReadCanonicalMessagePackSources(
                reader,
                outerCount,
                typeFilter,
                nameFilter,
                containerFilter,
                cancellationToken);
        }

        private static string[] ReadCompressedMessagePackSources(
            MessagePackStreamTokenReader reader,
            int outerCount,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            ContainerStorageOptions storageOptions,
            CancellationToken cancellationToken)
        {
            if (outerCount < 2)
            {
                throw new InvalidDataException(
                    "Compressed AssetMap MessagePack has no data blocks.");
            }

            var blockCount = outerCount - 1;
            if (blockCount > MaximumBlockCount)
            {
                throw new InvalidDataException(
                    $"Compressed AssetMap MessagePack exceeds {MaximumBlockCount} blocks.");
            }

            var extension = reader.ReadExtensionHeader();
            if (extension.TypeCode != Lz4BlockArrayTypeCode)
            {
                throw new InvalidDataException(
                    $"Unsupported MessagePack extension type {extension.TypeCode}.");
            }

            using var workspace = new TemporaryFileWorkspace(storageOptions);
            var lengthsPath = workspace.CreateFilePath(
                "asset-map-lz4-lengths",
                ".lengths",
                FreeSpaceCheckInterval);
            var canonicalPath = workspace.CreateFilePath(
                "asset-map-decompressed",
                ".msgpack",
                FreeSpaceCheckInterval);

            using var lengths = new FileStream(
                lengthsPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            var payloadStart = reader.BytesConsumed;
            using (var lengthWriter = new BinaryWriter(lengths, Utf8, leaveOpen: true))
            {
                for (var index = 0; index < blockCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var length = reader.ReadInt32();
                    if (length <= 0 || length > MaximumBlockBytes)
                    {
                        throw new InvalidDataException(
                            $"AssetMap LZ4 block {index} has invalid output length {length}.");
                    }

                    lengthWriter.Write(length);
                }
            }

            if (reader.BytesConsumed - payloadStart != extension.Length)
            {
                throw new InvalidDataException(
                    "AssetMap LZ4 extension payload length is inconsistent.");
            }

            lengths.Flush(true);
            lengths.Position = 0;
            using var canonical = new FileStream(
                canonicalPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using (var lengthReader = new BinaryReader(lengths, Utf8, leaveOpen: true))
            {
                for (var index = 0; index < blockCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var uncompressedLength = lengthReader.ReadInt32();
                    var compressedLength = reader.ReadBinaryLength();
                    var maximumCompressedLength =
                        Lz4CodecBindings.Instance.MaximumOutputLength(uncompressedLength);
                    if (compressedLength <= 0
                        || compressedLength > maximumCompressedLength)
                    {
                        throw new InvalidDataException(
                            $"AssetMap LZ4 block {index} has invalid compressed length " +
                            $"{compressedLength}.");
                    }

                    workspace.EnsureFreeSpace(uncompressedLength);
                    var compressed = ArrayPool<byte>.Shared.Rent(compressedLength);
                    var uncompressed = ArrayPool<byte>.Shared.Rent(uncompressedLength);
                    try
                    {
                        reader.ReadExactly(compressed.AsSpan(0, compressedLength));
                        var decodedLength = Lz4CodecBindings.Instance.Decode(
                            compressed.AsSpan(0, compressedLength),
                            uncompressed.AsSpan(0, uncompressedLength));
                        if (decodedLength != uncompressedLength)
                        {
                            throw new InvalidDataException(
                                $"AssetMap LZ4 block {index} decoded to {decodedLength} bytes, " +
                                $"expected {uncompressedLength}.");
                        }

                        canonical.Write(uncompressed, 0, uncompressedLength);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(compressed);
                        ArrayPool<byte>.Shared.Return(uncompressed);
                    }
                }
            }

            reader.EnsureEnd();
            canonical.Flush(true);
            canonical.Position = 0;
            var canonicalReader =
                new MessagePackStreamTokenReader(canonical, cancellationToken);
            var canonicalOuterCount = canonicalReader.ReadArrayHeader();
            return ReadCanonicalMessagePackSources(
                canonicalReader,
                canonicalOuterCount,
                typeFilter,
                nameFilter,
                containerFilter,
                cancellationToken);
        }

        private static string[] ReadCanonicalMessagePackSources(
            MessagePackStreamTokenReader reader,
            int outerCount,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            CancellationToken cancellationToken)
        {
            if (outerCount != 2)
            {
                throw new InvalidDataException(
                    $"AssetMap MessagePack root must have 2 elements, found {outerCount}.");
            }

            reader.ReadInt32();
            var entryCount = reader.ReadArrayHeader();
            if (entryCount > MaximumRecordCount)
            {
                throw new InvalidDataException(
                    $"AssetMap MessagePack exceeds {MaximumRecordCount} entries.");
            }

            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < entryCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fieldCount = reader.ReadArrayHeader();
                if (fieldCount != 7)
                {
                    throw new InvalidDataException(
                        $"AssetMap MessagePack entry {index} has {fieldCount} fields; expected 7.");
                }

                string name;
                if (nameFilter.Length == 0)
                {
                    reader.SkipStringOrNil();
                    name = null;
                }
                else
                {
                    name = reader.ReadNullableString();
                }

                string container;
                if (containerFilter.Length == 0)
                {
                    reader.SkipStringOrNil();
                    container = null;
                }
                else
                {
                    container = reader.ReadNullableString();
                }

                var source = reader.ReadNullableString();
                reader.ReadInt64();
                var type = (ClassIDType)reader.ReadInt32();
                reader.SkipStringOrNil();
                reader.ReadInt64();

                AddIfMatch(
                    name,
                    container,
                    source,
                    type,
                    typeFilter,
                    nameFilter,
                    containerFilter,
                    seen,
                    ordered);
            }

            reader.EnsureEnd();
            return ordered.ToArray();
        }

        private static void AddIfMatch(
            string name,
            string container,
            string source,
            ClassIDType type,
            ClassIDType[] typeFilter,
            Regex[] nameFilter,
            Regex[] containerFilter,
            HashSet<string> seen,
            List<string> ordered)
        {
            var nameMatches = nameFilter.Length == 0
                || MatchesAny(nameFilter, name);
            var containerMatches = containerFilter.Length == 0
                || MatchesAny(containerFilter, container);
            var typeMatches = typeFilter.Length == 0
                || Array.IndexOf(typeFilter, type) >= 0;
            if (nameMatches
                && containerMatches
                && typeMatches
                && seen.Add(source))
            {
                ordered.Add(source);
            }
        }

        private static bool MatchesAny(Regex[] filters, string value)
        {
            value ??= string.Empty;
            foreach (var filter in filters)
            {
                if (filter == null)
                {
                    throw new ArgumentException(
                        "AssetMap regex filters cannot contain null values.");
                }

                if (filter.IsMatch(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static ClassIDType ParseClassIdType(string name, string id)
        {
            if (Enum.TryParse<ClassIDType>(name, true, out var type))
            {
                return type;
            }

            if (int.TryParse(
                id,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var typeId))
            {
                return (ClassIDType)typeId;
            }

            throw new InvalidDataException(
                $"Invalid AssetMap XML type '{name}' with id '{id}'.");
        }

        private static JsonSerializer CreateJsonSerializer()
        {
            var serializer = new JsonSerializer
            {
                Formatting = Newtonsoft.Json.Formatting.Indented
            };
            serializer.Converters.Add(new StringEnumConverter());
            return serializer;
        }

        private static void EnsureReasonableInputLength(string path)
        {
            var length = new FileInfo(path).Length;
            if (length > MaximumJsonOrXmlFileBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap input exceeds {MaximumJsonOrXmlFileBytes} bytes.");
            }
        }

        private static void EnsureStringLength(string value)
        {
            if (value != null && Utf8.GetByteCount(value) > MaximumStringBytes)
            {
                throw new InvalidDataException(
                    $"AssetMap string exceeds {MaximumStringBytes} UTF-8 bytes.");
            }
        }

        private static void CopyTo(
            Stream source,
            Stream destination,
            long expectedLength,
            CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                long copied = 0;
                while (copied < expectedLength)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = source.Read(
                        buffer,
                        0,
                        (int)Math.Min(buffer.Length, expectedLength - copied));
                    if (read == 0)
                    {
                        throw new EndOfStreamException(
                            $"Expected {expectedLength} canonical MessagePack bytes, " +
                            $"read {copied}.");
                    }

                    destination.Write(buffer, 0, read);
                    copied += read;
                }

                if (source.ReadByte() != -1)
                {
                    throw new InvalidDataException(
                        "Canonical MessagePack staging file contains trailing data.");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static void ReadExactly(Stream stream, Span<byte> destination)
        {
            while (!destination.IsEmpty)
            {
                var read = stream.Read(destination);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Expected {destination.Length} more bytes.");
                }

                destination = destination[read..];
            }
        }

        private static void DeletePartialFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private sealed class SegmentedCanonicalBufferWriter :
            IBufferWriter<byte>,
            IDisposable
        {
            private const int InitialSegmentLength = 4096;
            private const int MinimumSegmentLength = 32 * 1024;
            private const int MaximumPooledSegmentLength = 64 * 1024;
            private const int MaximumOutstandingSegmentsPerBucket = 100;
            private const int MinimumArrayPoolBucketLength = 16;

            private readonly Stream output;
            private readonly BinaryWriter segmentLengths;
            private readonly TemporaryFileWorkspace workspace;
            private readonly int[] outstandingSegmentsByBucket = new int[13];
            private byte[] buffer;
            private int written;
            private long remainingCheckedBytes = FreeSpaceCheckInterval;
            private bool completed;
            private bool disposed;

            internal SegmentedCanonicalBufferWriter(
                Stream output,
                Stream segmentLengths,
                TemporaryFileWorkspace workspace)
            {
                this.output = output;
                this.segmentLengths = new BinaryWriter(
                    segmentLengths,
                    Utf8,
                    leaveOpen: true);
                this.workspace = workspace;
            }

            internal long TotalLength { get; private set; }

            internal int SegmentCount { get; private set; }

            internal int ExtensionHeaderSize { get; private set; }

            public void Advance(int count)
            {
                ThrowIfCompletedOrDisposed();
                if (buffer == null
                    || count < 0
                    || count > buffer.Length - written)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                written += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                EnsureBuffer(sizeHint);
                return buffer.AsMemory(written);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                EnsureBuffer(sizeHint);
                return buffer.AsSpan(written);
            }

            internal void Complete()
            {
                ThrowIfDisposed();
                if (completed)
                {
                    return;
                }

                FlushSegment();
                segmentLengths.Flush();
                completed = true;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                buffer = null;
                segmentLengths.Dispose();
            }

            private void EnsureBuffer(int sizeHint)
            {
                ThrowIfCompletedOrDisposed();
                if (sizeHint < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizeHint));
                }

                if (buffer != null && buffer.Length - written >= sizeHint
                    && (sizeHint != 0 || buffer.Length != written))
                {
                    return;
                }

                FlushSegment();
                var requestedLength = sizeHint == 0
                    ? InitialSegmentLength
                    : Math.Max(MinimumSegmentLength, sizeHint);
                var segmentLength = RentCompatibleSegmentLength(requestedLength);
                if (buffer == null || buffer.Length != segmentLength)
                {
                    buffer = new byte[segmentLength];
                }
            }

            private void FlushSegment()
            {
                if (buffer == null)
                {
                    return;
                }

                if (written > 0)
                {
                    EnsureFreeSpace(written);
                    output.Write(buffer, 0, written);
                    segmentLengths.Write(written);
                    TotalLength = checked(TotalLength + written);
                    SegmentCount = checked(SegmentCount + 1);
                    ExtensionHeaderSize = checked(
                        ExtensionHeaderSize + GetUInt32WriteSize((uint)written));
                }

                written = 0;
            }

            private int RentCompatibleSegmentLength(int requestedLength)
            {
                var bucketLength = MinimumArrayPoolBucketLength;
                var bucketIndex = 0;
                while (bucketLength < requestedLength
                    && bucketLength < MaximumPooledSegmentLength)
                {
                    bucketLength *= 2;
                    bucketIndex++;
                }

                if (bucketLength < requestedLength)
                {
                    return requestedLength;
                }

                while (bucketIndex < outstandingSegmentsByBucket.Length)
                {
                    if (outstandingSegmentsByBucket[bucketIndex]
                        < MaximumOutstandingSegmentsPerBucket)
                    {
                        outstandingSegmentsByBucket[bucketIndex]++;
                        return bucketLength;
                    }

                    if (bucketLength == MaximumPooledSegmentLength)
                    {
                        break;
                    }

                    bucketLength *= 2;
                    bucketIndex++;
                }

                return requestedLength;
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

            private void ThrowIfCompletedOrDisposed()
            {
                ThrowIfDisposed();
                if (completed)
                {
                    throw new InvalidOperationException(
                        "Canonical MessagePack writer is complete.");
                }
            }

            private void ThrowIfDisposed()
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(SegmentedCanonicalBufferWriter));
                }
            }
        }

        private sealed class StreamBufferWriter : IBufferWriter<byte>, IDisposable
        {
            private readonly Stream output;
            private byte[] buffer;
            private int written;
            private bool disposed;

            internal StreamBufferWriter(Stream output)
            {
                this.output = output;
                buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            }

            public void Advance(int count)
            {
                ThrowIfDisposed();
                if (count < 0 || count > buffer.Length - written)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }

                written += count;
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                Ensure(sizeHint);
                return buffer.AsMemory(written);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                Ensure(sizeHint);
                return buffer.AsSpan(written);
            }

            internal void Flush()
            {
                ThrowIfDisposed();
                if (written > 0)
                {
                    output.Write(buffer, 0, written);
                    written = 0;
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                try
                {
                    Flush();
                }
                finally
                {
                    disposed = true;
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = null;
                }
            }

            private void Ensure(int sizeHint)
            {
                ThrowIfDisposed();
                if (sizeHint < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(sizeHint));
                }

                if (buffer.Length - written >= sizeHint
                    && (sizeHint != 0 || buffer.Length != written))
                {
                    return;
                }

                Flush();
                if (buffer.Length >= sizeHint)
                {
                    return;
                }

                ArrayPool<byte>.Shared.Return(buffer);
                buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            }

            private void ThrowIfDisposed()
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(StreamBufferWriter));
                }
            }
        }

        private sealed class MessagePackStreamTokenReader
        {
            private readonly Stream stream;
            private readonly CancellationToken cancellationToken;
            private readonly byte[] skipBuffer = new byte[8192];
            private int bufferedByte = -1;

            internal MessagePackStreamTokenReader(
                Stream stream,
                CancellationToken cancellationToken)
            {
                this.stream = stream;
                this.cancellationToken = cancellationToken;
            }

            internal long BytesConsumed { get; private set; }

            internal bool NextTokenIsExtension =>
                IsExtensionCode(PeekCode());

            internal int ReadArrayHeader()
            {
                var code = ReadCode();
                uint count;
                if (code >= MessagePackCode.MinFixArray
                    && code <= MessagePackCode.MaxFixArray)
                {
                    count = (uint)(code & 0x0f);
                }
                else if (code == MessagePackCode.Array16)
                {
                    count = ReadUInt16();
                }
                else if (code == MessagePackCode.Array32)
                {
                    count = ReadUInt32();
                }
                else
                {
                    throw UnexpectedCode("array", code);
                }

                if (count > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"MessagePack array length {count} exceeds {int.MaxValue}.");
                }

                return (int)count;
            }

            internal int ReadInt32()
            {
                var value = ReadInt64();
                if (value < int.MinValue || value > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"MessagePack integer {value} does not fit Int32.");
                }

                return (int)value;
            }

            internal long ReadInt64()
            {
                var code = ReadCode();
                if (code <= MessagePackCode.MaxFixInt)
                {
                    return code;
                }

                if (code >= MessagePackCode.MinNegativeFixInt)
                {
                    return unchecked((sbyte)code);
                }

                return code switch
                {
                    MessagePackCode.UInt8 => ReadCode(),
                    MessagePackCode.UInt16 => ReadUInt16(),
                    MessagePackCode.UInt32 => ReadUInt32(),
                    MessagePackCode.UInt64 => ReadCheckedUInt64(),
                    MessagePackCode.Int8 => unchecked((sbyte)ReadCode()),
                    MessagePackCode.Int16 => ReadInt16(),
                    MessagePackCode.Int32 => ReadSignedInt32(),
                    MessagePackCode.Int64 => ReadSignedInt64(),
                    _ => throw UnexpectedCode("integer", code)
                };
            }

            internal string ReadNullableString()
            {
                var code = ReadCode();
                if (code == MessagePackCode.Nil)
                {
                    return null;
                }

                var length = ReadStringLength(code);
                if (length > MaximumStringBytes)
                {
                    throw new InvalidDataException(
                        $"MessagePack string exceeds {MaximumStringBytes} bytes.");
                }

                var bytes = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    ReadExactly(bytes.AsSpan(0, length));
                    return Utf8.GetString(bytes, 0, length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                }
            }

            internal void SkipStringOrNil()
            {
                var code = ReadCode();
                if (code == MessagePackCode.Nil)
                {
                    return;
                }

                var length = ReadStringLength(code);
                if (length > MaximumStringBytes)
                {
                    throw new InvalidDataException(
                        $"MessagePack string exceeds {MaximumStringBytes} bytes.");
                }

                SkipBytes(length);
            }

            internal (sbyte TypeCode, int Length) ReadExtensionHeader()
            {
                var code = ReadCode();
                uint length = code switch
                {
                    MessagePackCode.FixExt1 => 1,
                    MessagePackCode.FixExt2 => 2,
                    MessagePackCode.FixExt4 => 4,
                    MessagePackCode.FixExt8 => 8,
                    MessagePackCode.FixExt16 => 16,
                    MessagePackCode.Ext8 => (uint)ReadCode(),
                    MessagePackCode.Ext16 => ReadUInt16(),
                    MessagePackCode.Ext32 => ReadUInt32(),
                    _ => throw UnexpectedCode("extension", code)
                };

                if (length > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"MessagePack extension length {length} exceeds {int.MaxValue}.");
                }

                return (unchecked((sbyte)ReadCode()), (int)length);
            }

            internal int ReadBinaryLength()
            {
                var code = ReadCode();
                uint length = code switch
                {
                    MessagePackCode.Bin8 => (uint)ReadCode(),
                    MessagePackCode.Bin16 => ReadUInt16(),
                    MessagePackCode.Bin32 => ReadUInt32(),
                    _ => throw UnexpectedCode("binary", code)
                };

                if (length > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"MessagePack binary length {length} exceeds {int.MaxValue}.");
                }

                return (int)length;
            }

            internal void ReadExactly(Span<byte> destination)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (bufferedByte >= 0 && !destination.IsEmpty)
                {
                    destination[0] = (byte)bufferedByte;
                    bufferedByte = -1;
                    BytesConsumed++;
                    destination = destination[1..];
                }

                while (!destination.IsEmpty)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = stream.Read(destination);
                    if (read == 0)
                    {
                        throw new EndOfStreamException(
                            $"MessagePack ended with {destination.Length} bytes missing.");
                    }

                    BytesConsumed = checked(BytesConsumed + read);
                    destination = destination[read..];
                }
            }

            internal void EnsureEnd()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (PeekRawByte() != -1)
                {
                    throw new InvalidDataException(
                        "MessagePack contains trailing data.");
                }
            }

            private int ReadStringLength(byte code)
            {
                uint length;
                if (code >= MessagePackCode.MinFixStr
                    && code <= MessagePackCode.MaxFixStr)
                {
                    length = (uint)(code & 0x1f);
                }
                else
                {
                    length = code switch
                    {
                        MessagePackCode.Str8 => (uint)ReadCode(),
                        MessagePackCode.Str16 => ReadUInt16(),
                        MessagePackCode.Str32 => ReadUInt32(),
                        _ => throw UnexpectedCode("string", code)
                    };
                }

                if (length > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"MessagePack string length {length} exceeds {int.MaxValue}.");
                }

                return (int)length;
            }

            private byte PeekCode()
            {
                var value = PeekRawByte();
                if (value < 0)
                {
                    throw new EndOfStreamException("MessagePack ended unexpectedly.");
                }

                return (byte)value;
            }

            private byte ReadCode()
            {
                cancellationToken.ThrowIfCancellationRequested();
                int value;
                if (bufferedByte >= 0)
                {
                    value = bufferedByte;
                    bufferedByte = -1;
                }
                else
                {
                    value = stream.ReadByte();
                }

                if (value < 0)
                {
                    throw new EndOfStreamException("MessagePack ended unexpectedly.");
                }

                BytesConsumed = checked(BytesConsumed + 1);
                return (byte)value;
            }

            private int PeekRawByte()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (bufferedByte < 0)
                {
                    bufferedByte = stream.ReadByte();
                }

                return bufferedByte;
            }

            private ushort ReadUInt16()
            {
                Span<byte> bytes = stackalloc byte[2];
                ReadExactly(bytes);
                return BinaryPrimitives.ReadUInt16BigEndian(bytes);
            }

            private uint ReadUInt32()
            {
                Span<byte> bytes = stackalloc byte[4];
                ReadExactly(bytes);
                return BinaryPrimitives.ReadUInt32BigEndian(bytes);
            }

            private ulong ReadUInt64()
            {
                Span<byte> bytes = stackalloc byte[8];
                ReadExactly(bytes);
                return BinaryPrimitives.ReadUInt64BigEndian(bytes);
            }

            private long ReadCheckedUInt64()
            {
                var value = ReadUInt64();
                if (value > long.MaxValue)
                {
                    throw new InvalidDataException(
                        $"MessagePack integer {value} does not fit Int64.");
                }

                return (long)value;
            }

            private short ReadInt16()
            {
                Span<byte> bytes = stackalloc byte[2];
                ReadExactly(bytes);
                return BinaryPrimitives.ReadInt16BigEndian(bytes);
            }

            private int ReadSignedInt32()
            {
                Span<byte> bytes = stackalloc byte[4];
                ReadExactly(bytes);
                return BinaryPrimitives.ReadInt32BigEndian(bytes);
            }

            private long ReadSignedInt64()
            {
                Span<byte> bytes = stackalloc byte[8];
                ReadExactly(bytes);
                return BinaryPrimitives.ReadInt64BigEndian(bytes);
            }

            private void SkipBytes(int length)
            {
                while (length > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunk = Math.Min(length, skipBuffer.Length);
                    ReadExactly(skipBuffer.AsSpan(0, chunk));
                    length -= chunk;
                }
            }

            private static bool IsExtensionCode(byte code)
            {
                return code == MessagePackCode.FixExt1
                    || code == MessagePackCode.FixExt2
                    || code == MessagePackCode.FixExt4
                    || code == MessagePackCode.FixExt8
                    || code == MessagePackCode.FixExt16
                    || code == MessagePackCode.Ext8
                    || code == MessagePackCode.Ext16
                    || code == MessagePackCode.Ext32;
            }

            private static InvalidDataException UnexpectedCode(
                string expected,
                byte code)
            {
                return new InvalidDataException(
                    $"Expected MessagePack {expected}, found code 0x{code:x2}.");
            }
        }

        private sealed class Lz4CodecBindings
        {
            private delegate int Transform(
                ReadOnlySpan<byte> input,
                Span<byte> output);

            private delegate int MaximumOutputLengthDelegate(int inputLength);

            private static readonly Lazy<Lz4CodecBindings> LazyInstance =
                new(Create, LazyThreadSafetyMode.ExecutionAndPublication);
            private readonly Transform encode;
            private readonly Transform decode;
            private readonly MaximumOutputLengthDelegate maximumOutputLength;

            private Lz4CodecBindings(
                Transform encode,
                Transform decode,
                MaximumOutputLengthDelegate maximumOutputLength)
            {
                this.encode = encode;
                this.decode = decode;
                this.maximumOutputLength = maximumOutputLength;
            }

            internal static Lz4CodecBindings Instance => LazyInstance.Value;

            internal int Encode(
                ReadOnlySpan<byte> input,
                Span<byte> output)
            {
                return encode(input, output);
            }

            internal int Decode(
                ReadOnlySpan<byte> input,
                Span<byte> output)
            {
                return decode(input, output);
            }

            internal int MaximumOutputLength(int inputLength)
            {
                return maximumOutputLength(inputLength);
            }

            private static Lz4CodecBindings Create()
            {
                var assembly = typeof(MessagePackSerializer).Assembly;
                var informationalVersion = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                if (informationalVersion == null
                    || !informationalVersion.StartsWith(
                        "3.1.4",
                        StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        "AssetMap streaming requires MessagePack-CSharp 3.1.4; " +
                        $"loaded '{informationalVersion ?? "unknown"}'.");
                }

                var codecType = assembly.GetType(
                    "MessagePack.LZ4.LZ4Codec",
                    throwOnError: false);
                if (codecType == null)
                {
                    throw new NotSupportedException(
                        "MessagePack-CSharp 3.1.4 LZ4 codec type is unavailable.");
                }

                try
                {
                    var encode = codecType.GetMethod(
                        "Encode",
                        BindingFlags.Public | BindingFlags.Static,
                        binder: null,
                        [typeof(ReadOnlySpan<byte>), typeof(Span<byte>)],
                        modifiers: null);
                    var decode = codecType.GetMethod(
                        "Decode",
                        BindingFlags.Public | BindingFlags.Static,
                        binder: null,
                        [typeof(ReadOnlySpan<byte>), typeof(Span<byte>)],
                        modifiers: null);
                    var maximumOutputLength = codecType.GetMethod(
                        "MaximumOutputLength",
                        BindingFlags.Public | BindingFlags.Static,
                        binder: null,
                        [typeof(int)],
                        modifiers: null);
                    if (encode == null || decode == null || maximumOutputLength == null)
                    {
                        throw new MissingMethodException(
                            codecType.FullName,
                            "Encode/Decode/MaximumOutputLength");
                    }

                    return new Lz4CodecBindings(
                        encode.CreateDelegate<Transform>(),
                        decode.CreateDelegate<Transform>(),
                        maximumOutputLength.CreateDelegate<
                            MaximumOutputLengthDelegate>());
                }
                catch (Exception exception)
                    when (exception is ArgumentException
                        or MethodAccessException
                        or MissingMethodException)
                {
                    throw new NotSupportedException(
                        "Cannot bind the fixed MessagePack-CSharp 3.1.4 LZ4 codec.",
                        exception);
                }
            }
        }

        private static int GetUInt32WriteSize(uint value)
        {
            if (value <= MessagePackRange.MaxFixPositiveInt)
            {
                return 1;
            }

            if (value <= byte.MaxValue)
            {
                return 2;
            }

            if (value <= ushort.MaxValue)
            {
                return 3;
            }

            return 5;
        }
    }
}
