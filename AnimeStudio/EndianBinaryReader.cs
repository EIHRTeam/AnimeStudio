using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AnimeStudio
{
    public class EndianBinaryReader : BinaryReader
    {
        private readonly byte[] buffer;

        public EndianType Endian;

        public EndianBinaryReader(Stream stream, EndianType endian = EndianType.BigEndian, bool leaveOpen = false) : base(stream, Encoding.UTF8, leaveOpen)
        {
            Endian = endian;
            buffer = new byte[8];
        }

        public long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        public long Length => BaseStream.Length;
        public virtual long Remaining => Length - Position;

        public override short ReadInt16()
        {
            if (Endian == EndianType.BigEndian)
            {
                Read(buffer, 0, 2);
                return BinaryPrimitives.ReadInt16BigEndian(buffer);
            }
            return base.ReadInt16();
        }

        public override int ReadInt32()
        {
            if (Endian == EndianType.BigEndian)
            {
                Read(buffer, 0, 4);
                return BinaryPrimitives.ReadInt32BigEndian(buffer);
            }
            return base.ReadInt32();
        }

        public override long ReadInt64()
        {
            if (Endian == EndianType.BigEndian)
            {
                Read(buffer, 0, 8);
                return BinaryPrimitives.ReadInt64BigEndian(buffer);
            }
            return base.ReadInt64();
        }

        public override ushort ReadUInt16()
        {
            if (Endian == EndianType.BigEndian)
            {
                Read(buffer, 0, 2);
                return BinaryPrimitives.ReadUInt16BigEndian(buffer);
            }
            return base.ReadUInt16();
        }

        public override uint ReadUInt32()
        {
            if (Endian == EndianType.BigEndian)
            {
                Read(buffer, 0, 4);
                return BinaryPrimitives.ReadUInt32BigEndian(buffer);
            }
            return base.ReadUInt32();
        }

        public override ulong ReadUInt64()
        {
            if (Endian == EndianType.BigEndian)
            {
                Read(buffer, 0, 8);
                return BinaryPrimitives.ReadUInt64BigEndian(buffer);
            }
            return base.ReadUInt64();
        }

        public override float ReadSingle()
        {
            if (Endian == EndianType.BigEndian)
            {
                Read(buffer, 0, 4);
                Array.Reverse(buffer, 0, 4);
                return BitConverter.ToSingle(buffer, 0);
            }
            return base.ReadSingle();
        }

        public override double ReadDouble()
        {
            if (Endian == EndianType.BigEndian)
            {
                Read(buffer, 0, 8);
                Array.Reverse(buffer);
                return BitConverter.ToDouble(buffer, 0);
            }
            return base.ReadDouble();
        }
        public override byte[] ReadBytes(int count)
        {
            ValidateLength(count, 1, "byte sequence");
            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            var result = GC.AllocateUninitializedArray<byte>(count);
            var offset = 0;
            while (offset < result.Length)
            {
                var read = Read(result, offset, result.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unable to read the requested number of bytes.");
                }
                offset += read;
            }
            return result;
        }

        public virtual void AlignStream()
        {
            AlignStream(4);
        }

        public virtual void AlignStream(int alignment)
        {
            if (alignment <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be positive.");
            }

            var pos = Position;
            var mod = pos % alignment;
            if (mod != 0)
            {
                var skip = alignment - mod;
                if (skip > Remaining)
                {
                    throw new EndOfStreamException("Unable to align beyond the end of the stream.");
                }
                Position += skip;
            }
        }

        public string ReadAlignedString()
        {
            var length = ReadInt32();
            ValidateLength(length, 1, "aligned string");
            var stringData = ReadBytes(length);
            var result = Encoding.UTF8.GetString(stringData);
            AlignStream();
            return result;
        }

        public string ReadStringToNull(int maxLength = 32767)
        {
            var bytes = new List<byte>();
            int count = 0;
            while (Remaining > 0 && count < maxLength)
            {
                var b = ReadByte();
                if (b == 0)
                {
                    break;
                }
                bytes.Add(b);
                count++;
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public Quaternion ReadQuaternion()
        {
            return new Quaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
        }

        public Vector2 ReadVector2()
        {
            return new Vector2(ReadSingle(), ReadSingle());
        }

        public Vector4 ReadVector4()
        {
            return new Vector4(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
        }

        public Color ReadColor4()
        {
            return new Color(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
        }

        public Matrix4x4 ReadMatrix()
        {
            return new Matrix4x4(ReadSingleArray(16));
        }

        public Float ReadFloat()
        {
            return new Float(ReadSingle());
        }

        public int ReadMhyInt()
        {
            var buffer = ReadBytes(6);
            return buffer[2] | (buffer[4] << 8) | (buffer[0] << 0x10) | (buffer[5] << 0x18);
        }

        public uint ReadMhyUInt()
        {
            var buffer = ReadBytes(7);
            return (uint)(buffer[1] | (buffer[6] << 8) | (buffer[3] << 0x10) | (buffer[2] << 0x18));
        }

        public string ReadMhyString()
        {
            var pos = BaseStream.Position;
            var str = ReadStringToNull();
            BaseStream.Position += 0x105 - (BaseStream.Position - pos);
            return str;
        }

        public int ReadArrayLength(int minimumElementSize = 1, string fieldName = "array")
        {
            var length = ReadInt32();
            ValidateLength(length, minimumElementSize, fieldName);
            return length;
        }

        internal T[] ReadArray<T>(Func<T> del, int length, int minimumElementSize = 1)
        {
            ValidateLength(length, minimumElementSize, "array");
            var array = new T[length];
            for (int i = 0; i < length; i++)
            {
                array[i] = del();
            }
            return array;
        }

        private void ValidateLength(int length, int minimumElementSize, string fieldName)
        {
            if (length < 0)
            {
                throw new InvalidDataException($"Invalid negative {fieldName} length: {length}.");
            }
            if (minimumElementSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumElementSize),
                    minimumElementSize,
                    "Minimum element size must be positive.");
            }
            if (length > Remaining / minimumElementSize)
            {
                throw new EndOfStreamException(
                    $"{fieldName} length {length} exceeds the remaining {Remaining} bytes.");
            }
        }

        public bool[] ReadBooleanArray(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(1, "Boolean array");
            }
            return ReadArray(ReadBoolean, length, 1);
        }

        public byte[] ReadUInt8Array(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(1, "byte array");
            }
            return ReadBytes(length);
        }

        public short[] ReadInt16Array(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(short), "Int16 array");
            }
            return ReadArray(ReadInt16, length, sizeof(short));
        }

        public ushort[] ReadUInt16Array(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(ushort), "UInt16 array");
            }
            return ReadArray(ReadUInt16, length, sizeof(ushort));
        }

        public int[] ReadInt32Array(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(int), "Int32 array");
            }
            return ReadArray(ReadInt32, length, sizeof(int));
        }

        public uint[] ReadUInt32Array(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(uint), "UInt32 array");
            }
            return ReadArray(ReadUInt32, length, sizeof(uint));
        }

        public ulong[] ReadUInt64Array(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(ulong), "UInt64 array");
            }
            return ReadArray(ReadUInt64, length, sizeof(ulong));
        }

        public uint[][] ReadUInt32ArrayArray(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(int), "nested UInt32 array");
            }
            return ReadArray(() => ReadUInt32Array(), length, sizeof(int));
        }

        public float[] ReadSingleArray(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(float), "Single array");
            }
            return ReadArray(ReadSingle, length, sizeof(float));
        }

        public string[] ReadStringArray(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(int), "string array");
            }
            return ReadArray(ReadAlignedString, length, sizeof(int));
        }

        public Vector2[] ReadVector2Array(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(float) * 2, "Vector2 array");
            }
            return ReadArray(ReadVector2, length, sizeof(float) * 2);
        }

        public Vector4[] ReadVector4Array(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(float) * 4, "Vector4 array");
            }
            return ReadArray(ReadVector4, length, sizeof(float) * 4);
        }

        public Matrix4x4[] ReadMatrixArray(int length = -1)
        {
            if (length == -1)
            {
                length = ReadArrayLength(sizeof(float) * 16, "Matrix4x4 array");
            }
            return ReadArray(ReadMatrix, length, sizeof(float) * 16);
        }
    }
}
