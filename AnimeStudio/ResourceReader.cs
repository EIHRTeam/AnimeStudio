using System;
using System.IO;

namespace AnimeStudio
{
    public class ResourceReader
    {
        private bool needSearch;
        private string path;
        private SerializedFile assetsFile;
        private long offset;
        private long size;
        private BinaryReader reader;

        public int Size { get => (int)size; }

        public ResourceReader(string path, SerializedFile assetsFile, long offset, long size)
        {
            needSearch = true;
            this.path = path;
            this.assetsFile = assetsFile;
            this.offset = offset;
            this.size = size;
        }

        public ResourceReader(BinaryReader reader, long offset, long size)
        {
            this.reader = reader;
            this.offset = offset;
            this.size = size;
        }

        private BinaryReader GetReader()
        {
            if (needSearch)
            {
                var resourceFileName = Path.GetFileName(path);
                if (assetsFile.assetsManager.TryGetResourceFileReader(
                    resourceFileName,
                    out reader))
                {
                    needSearch = false;
                    return reader;
                }
                var assetsFileDirectory = Path.GetDirectoryName(assetsFile.fullName);
                var resourceFilePath = Path.Combine(assetsFileDirectory, resourceFileName);
                if (!File.Exists(resourceFilePath))
                {
                    var findFiles = Directory.GetFiles(assetsFileDirectory, resourceFileName, SearchOption.AllDirectories);
                    if (findFiles.Length > 0)
                    {
                        resourceFilePath = findFiles[0];
                    }
                }
                if (File.Exists(resourceFilePath))
                {
                    needSearch = false;
                    reader = assetsFile.assetsManager.GetOrAddResourceFileReader(
                        resourceFileName,
                        () => new BinaryReader(File.OpenRead(resourceFilePath)));
                    return reader;
                }
                throw new FileNotFoundException($"Can't find the resource file {resourceFileName}");
            }
            else
            {
                return reader;
            }
        }

        public byte[] GetData()
        {
            var binaryReader = GetReader();
            lock (binaryReader)
            {
                binaryReader.BaseStream.Position = offset;
                var data = GC.AllocateUninitializedArray<byte>(
                    checked((int)size));
                binaryReader.BaseStream.ReadExactly(data);
                return data;
            }
        }

        public void GetData(byte[] buff)
        {
            ArgumentNullException.ThrowIfNull(buff);
            if (buff.Length < size)
            {
                throw new ArgumentException(
                    "The destination buffer is smaller than the resource.",
                    nameof(buff));
            }

            var binaryReader = GetReader();
            lock (binaryReader)
            {
                binaryReader.BaseStream.Position = offset;
                binaryReader.BaseStream.ReadExactly(
                    buff.AsSpan(0, checked((int)size)));
            }
        }

        public void WriteData(string path)
        {
            var binaryReader = GetReader();
            lock (binaryReader)
            {
                binaryReader.BaseStream.Position = offset;
                using var writer = File.OpenWrite(path);
                binaryReader.BaseStream.CopyTo(writer, size);
            }
        }
    }
}
