using System;
using System.IO;
using SevenZip.Compression.LZMA;


namespace AnimeStudio
{
    public static class SevenZipHelper
    {
        public static MemoryStream StreamDecompress(MemoryStream inStream)
        {
            var decoder = new Decoder();

            inStream.Seek(0, SeekOrigin.Begin);
            var properties = new byte[5];
            if (inStream.Read(properties, 0, 5) != 5)
                throw new Exception("input .lzma is too short");
            long outSize = 0;
            for (var i = 0; i < 8; i++)
            {
                var v = inStream.ReadByte();
                if (v < 0)
                    throw new Exception("Can't Read 1");
                outSize |= ((long)(byte)v) << (8 * i);
            }
            decoder.SetDecoderProperties(properties);

            if (outSize < 0 || outSize > int.MaxValue)
                throw new IOException($"LZMA output is too large for a MemoryStream: {outSize} bytes.");

            var newOutStream = new MemoryStream((int)outSize);
            var compressedSize = inStream.Length - inStream.Position;
            decoder.Code(inStream, newOutStream, compressedSize, outSize, null);

            newOutStream.Position = 0;
            return newOutStream;
        }

        public static void StreamDecompressWithHeader(Stream inStream, Stream outStream)
        {
            var decoder = new Decoder();
            var properties = new byte[5];
            if (inStream.Read(properties, 0, properties.Length) != properties.Length)
                throw new InvalidDataException("LZMA input is too short.");

            long outSize = 0;
            for (var index = 0; index < sizeof(long); index++)
            {
                var value = inStream.ReadByte();
                if (value < 0)
                    throw new InvalidDataException("LZMA input does not contain an output length.");
                outSize |= (long)(byte)value << (8 * index);
            }

            if (outSize < 0)
                throw new InvalidDataException($"LZMA output length cannot be negative: {outSize}.");

            decoder.SetDecoderProperties(properties);
            var compressedSize = inStream.Length - inStream.Position;
            decoder.Code(inStream, outStream, compressedSize, outSize, null);
        }

        public static void StreamDecompress(Stream compressedStream, Stream decompressedStream, long compressedSize, long decompressedSize)
        {
            var basePosition = compressedStream.Position;
            var decoder = new Decoder();
            var properties = new byte[5];
            if (compressedStream.Read(properties, 0, 5) != 5)
                throw new Exception("input .lzma is too short");
            decoder.SetDecoderProperties(properties);
            decoder.Code(compressedStream, decompressedStream, compressedSize - 5, decompressedSize, null);
            compressedStream.Position = basePosition + compressedSize;
        }
    }
}
