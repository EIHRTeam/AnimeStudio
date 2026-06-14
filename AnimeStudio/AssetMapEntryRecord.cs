using System;

namespace AnimeStudio
{
    internal sealed class AssetMapEntryRecord
    {
        public string Name { get; set; }

        public string Container { get; set; }

        public string Source { get; set; }

        public long PathID { get; set; }

        public ClassIDType Type { get; set; }

        public string Hash { get; set; }

        public long Offset { get; set; } = -1;

        internal static AssetMapEntryRecord FromAssetEntry(AssetEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return new AssetMapEntryRecord
            {
                Name = entry.Name,
                Container = entry.Container,
                Source = entry.Source,
                PathID = entry.PathID,
                Type = entry.Type,
                Hash = entry.Hash,
                Offset = entry.Offset
            };
        }

        internal AssetEntry ToAssetEntry()
        {
            return new AssetEntry
            {
                Name = Name,
                Container = Container,
                Source = Source,
                PathID = PathID,
                Type = Type,
                Hash = Hash,
                Offset = Offset
            };
        }

        public static implicit operator AssetEntry(AssetMapEntryRecord entry)
        {
            return entry?.ToAssetEntry();
        }
    }
}
