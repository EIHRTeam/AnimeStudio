using System;
using System.IO;

namespace AnimeStudio
{
    public class ObjectReader : EndianBinaryReader
    {
        public SerializedFile assetsFile;
        public Game Game;
        public long m_PathID;
        public long byteStart;
        public uint byteSize;
        public ClassIDType type;
        public SerializedType serializedType;
        public BuildTarget platform;
        public SerializedFileFormatVersion m_Version;
        public long SourceByteStart { get; }

        public int[] version => assetsFile.version;
        public BuildType buildType => assetsFile.buildType;
        public override long Remaining => byteStart + byteSize - Position;

        public ObjectReader(
            EndianBinaryReader reader,
            SerializedFile assetsFile,
            ObjectInfo objectInfo,
            Game game)
            : this(
                reader.BaseStream,
                reader.Endian,
                assetsFile,
                objectInfo,
                game,
                objectInfo.byteStart)
        {
        }

        private ObjectReader(
            Stream stream,
            EndianType endian,
            SerializedFile assetsFile,
            ObjectInfo objectInfo,
            Game game,
            long sourceByteStart)
            : base(stream, endian)
        {
            this.assetsFile = assetsFile;
            Game = game;
            m_PathID = objectInfo.m_PathID;
            byteStart = objectInfo.byteStart;
            byteSize = objectInfo.byteSize;
            SourceByteStart = sourceByteStart;
            if (Enum.IsDefined(typeof(ClassIDType), objectInfo.classID))
            {
                type = (ClassIDType)objectInfo.classID;
            }
            else
            {
                type = ClassIDType.UnknownType;
                Logger.Warning($"Unknown ClassIDType {objectInfo.classID} for object with PathID {m_PathID} in file {assetsFile.fileName}");
            }
            serializedType = objectInfo.serializedType;
            platform = assetsFile.m_TargetPlatform;
            m_Version = assetsFile.header.m_Version;

            Logger.Verbose($"Initialized reader for {type} object with {m_PathID} in file {assetsFile.fileName} !!");
        }

        internal static bool SupportsIndependentReading(SerializedFile assetsFile)
        {
            var stream = assetsFile.reader.BaseStream;
            return stream is ReadOnlySliceStream
                || stream is FileStream
                || stream is MemoryStream memoryStream
                    && memoryStream.TryGetBuffer(out _);
        }

        internal static ObjectReader CreateIndependent(
            SerializedFile assetsFile,
            ObjectInfo objectInfo,
            Game game)
        {
            Stream objectStream;
            var sourceStream = assetsFile.reader.BaseStream;
            if (sourceStream is ReadOnlySliceStream sliceStream)
            {
                objectStream = sliceStream.CreateView(
                    objectInfo.byteStart,
                    objectInfo.byteSize);
            }
            else if (sourceStream is FileStream fileStream)
            {
                objectStream = new ReadOnlyRandomAccessStream(
                    fileStream.SafeFileHandle,
                    objectInfo.byteStart,
                    objectInfo.byteSize);
            }
            else if (sourceStream is MemoryStream memoryStream
                && memoryStream.TryGetBuffer(out var memorySegment))
            {
                objectStream = new ReadOnlyRandomAccessStream(
                    memorySegment.Array,
                    checked(
                        memorySegment.Offset
                        + checked((int)objectInfo.byteStart)),
                    checked((int)objectInfo.byteSize));
            }
            else
            {
                throw new NotSupportedException(
                    $"Stream type {sourceStream.GetType().Name} does not " +
                    "support independent object reads.");
            }

            var relativeObjectInfo = new ObjectInfo
            {
                byteStart = 0,
                byteSize = objectInfo.byteSize,
                typeID = objectInfo.typeID,
                classID = objectInfo.classID,
                isDestroyed = objectInfo.isDestroyed,
                stripped = objectInfo.stripped,
                m_PathID = objectInfo.m_PathID,
                serializedType = objectInfo.serializedType
            };
            return new ObjectReader(
                objectStream,
                assetsFile.reader.Endian,
                assetsFile,
                relativeObjectInfo,
                game,
                objectInfo.byteStart);
        }

        public override int Read(byte[] buffer, int index, int count)
        {
            var pos = Position - byteStart;
            if (pos < 0 || count < 0 || pos > byteSize - count)
            {
                throw new EndOfStreamException("Unable to read beyond the end of the stream.");
            }
            return base.Read(buffer, index, count);
        }

        public override void AlignStream(int alignment)
        {
            if (alignment <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be positive.");
            }

            var sourcePosition = checked(
                SourceByteStart + (Position - byteStart));
            var mod = sourcePosition % alignment;
            var skip = mod == 0 ? 0 : alignment - mod;
            if (skip > Remaining)
            {
                throw new EndOfStreamException("Unable to align beyond the end of the object.");
            }
            Position += skip;
        }

        public void Reset()
        {
            Logger.Verbose(
                $"Resetting reader position to object offset " +
                $"0x{SourceByteStart:X8}...");
            Position = byteStart;
        }

        public int BytesLeft()
        {
            return (int)(byteSize - (Position - byteStart));
        }

        public Vector3 ReadVector3()
        {
            if (version[0] > 5 || (version[0] == 5 && version[1] >= 4))
            {
                return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
            }
            else
            {
                return new Vector4(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
            }
        }

        public XForm ReadXForm()
        {
            var t = ReadVector3();
            var q = ReadQuaternion();
            var s = ReadVector3();

            return new XForm(t, q, s);
        }

        public XForm ReadXForm4()
        {
            var t = ReadVector4();
            var q = ReadQuaternion();
            var s = ReadVector4();

            return new XForm(t, q, s);
        }

        public Vector3[] ReadVector3Array(int length = 0)
        {
            if (length == 0)
            {
                length = ReadArrayLength(
                    version[0] > 5 || (version[0] == 5 && version[1] >= 4)
                        ? sizeof(float) * 3
                        : sizeof(float) * 4,
                    "Vector3 array");
            }
            var elementSize = version[0] > 5 || (version[0] == 5 && version[1] >= 4)
                ? sizeof(float) * 3
                : sizeof(float) * 4;
            return ReadArray(ReadVector3, length, elementSize);
        }

        public XForm[] ReadXFormArray()
        {
            var elementSize = version[0] > 5 || (version[0] == 5 && version[1] >= 4)
                ? sizeof(float) * 10
                : sizeof(float) * 12;
            return ReadArray(
                ReadXForm,
                ReadArrayLength(elementSize, "XForm array"),
                elementSize);
        }
    }
}
