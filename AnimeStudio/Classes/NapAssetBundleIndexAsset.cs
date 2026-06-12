using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AnimeStudio
{
    public sealed class NapAssetBundleIndexAsset : NamedObject
    {
        public List<IndexAssetRef> m_AssetArray;
        public List<IndexBundleRef> m_BundleArray;
        public List<IndexBlockRef> m_BlockArray;
        public List<uint> m_ChildrenIndexArray;

        public NapAssetBundleIndexAsset(ObjectReader reader) : base(reader)
        {
            var m_AssetArraySize = reader.ReadArrayLength(
                sizeof(uint) + sizeof(long),
                "Nap asset references");
            m_AssetArray = new List<IndexAssetRef>(m_AssetArraySize);
            for (int i = 0; i < m_AssetArraySize; i++)
                m_AssetArray.Add(new IndexAssetRef(reader));

            var m_BundleArraySize = reader.ReadArrayLength(
                sizeof(uint) * 5 + sizeof(ulong) * 2,
                "Nap bundle references");
            m_BundleArray = new List<IndexBundleRef>(m_BundleArraySize);
            for (int i = 0; i < m_BundleArraySize; i++)
                m_BundleArray.Add(new IndexBundleRef(reader));

            var m_BlockArraySize = reader.ReadArrayLength(
                sizeof(ulong) + sizeof(byte),
                "Nap block references");
            m_BlockArray = new List<IndexBlockRef>(m_BlockArraySize);
            for (int i = 0; i < m_BlockArraySize; i++)
                m_BlockArray.Add(new IndexBlockRef(reader));

            reader.AlignStream();

            var m_ChildrenIndexArraySize = reader.ReadArrayLength(
                sizeof(uint),
                "Nap child indices");
            m_ChildrenIndexArray = new List<uint>(m_ChildrenIndexArraySize);
            for (int i = 0; i < m_ChildrenIndexArraySize; i++)
                m_ChildrenIndexArray.Add(reader.ReadUInt32());
        }

        public class IndexAssetRef
        {
            public uint bundle;
            public long pathHash;
            public IndexAssetRef(ObjectReader reader)
            {
                bundle = reader.ReadUInt32();
                pathHash = reader.ReadInt64();
            }
        }

        public class IndexBundleRef
        {
            public uint blockIndex;
            public ulong bundleHashName;
            public ulong bundleHash;
            public uint offset;
            public uint childrenStartIndex;
            public uint childrenEndIndex;
            public uint fileSize;
            public IndexBundleRef(ObjectReader reader)
            {
                blockIndex = reader.ReadUInt32();
                bundleHashName = reader.ReadUInt64();
                bundleHash = reader.ReadUInt64();
                offset = reader.ReadUInt32();
                childrenStartIndex = reader.ReadUInt32();
                childrenEndIndex = reader.ReadUInt32();
                fileSize = reader.ReadUInt32();
            }
        }

        public class IndexBlockRef
        {
            public ulong blockHashName;
            public byte location;
            public IndexBlockRef(ObjectReader reader)
            {
                blockHashName = reader.ReadUInt64();
                location = reader.ReadByte();
            }
        }
    }
}
