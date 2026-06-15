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

        internal static EndianBinaryReader CreateIndependentReader(
            SerializedFile assetsFile)
        {
            Stream stream;
            var sourceStream = assetsFile.reader.BaseStream;
            if (sourceStream is ReadOnlySliceStream sliceStream)
            {
                stream = sliceStream.CreateView(0, sliceStream.Length);
            }
            else if (sourceStream is FileStream fileStream)
            {
                stream = new ReadOnlyRandomAccessStream(
                    fileStream.SafeFileHandle,
                    0,
                    fileStream.Length);
            }
            else if (sourceStream is MemoryStream memoryStream
                && memoryStream.TryGetBuffer(out var memorySegment))
            {
                stream = new ReadOnlyRandomAccessStream(
                    memorySegment.Array,
                    memorySegment.Offset,
                    checked((int)memoryStream.Length));
            }
            else
            {
                throw new NotSupportedException(
                    $"Stream type {sourceStream.GetType().Name} does not " +
                    "support independent object reads.");
            }

            return new EndianBinaryReader(
                stream,
                assetsFile.reader.Endian);
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
