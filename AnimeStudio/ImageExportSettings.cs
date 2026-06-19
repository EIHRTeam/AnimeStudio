using SixLabors.ImageSharp.Formats.Png;

namespace AnimeStudio
{
    // Production image-encoder settings applied once at CLI startup from the
    // resolved performance mode. Lives in the core assembly so ImageExtensions
    // can read it without depending on the CLI, and so the CLI/resolver can stay
    // free of SixLabors types by configuring it through simple int/string values.
    //
    // Null Png level/filter => the encoder uses the baseline SaveAsPng() path,
    // keeping `--mode default` output bit-identical to historical behavior. The
    // resolver enables a faster encoder (level 1 + Sub) only for limit/fast.
    public static class ImageExportSettings
    {
        // Null = baseline (SaveAsPng defaults: level 6 + Adaptive). Set by the CLI.
        public static PngCompressionLevel? PngLevel { get; private set; }
        public static PngFilterMethod? PngFilter { get; private set; }

        // Configure from simple values so callers need no SixLabors reference.
        // level: 0-9 (PNG DEFLATE level); null/out-of-range leaves PngLevel null.
        // filter: none|sub|up|average|paeth|adaptive; null/unknown leaves it null.
        public static void ConfigurePng(int? level, string filter)
        {
            PngLevel = level is >= 0 and <= 9 ? (PngCompressionLevel)level.Value : null;
            PngFilter = ParseFilter(filter);
        }

        public static PngFilterMethod? ParseFilter(string name)
        {
            return name?.Trim().ToLowerInvariant() switch
            {
                "none" => PngFilterMethod.None,
                "sub" => PngFilterMethod.Sub,
                "up" => PngFilterMethod.Up,
                "average" => PngFilterMethod.Average,
                "paeth" => PngFilterMethod.Paeth,
                "adaptive" => PngFilterMethod.Adaptive,
                _ => null
            };
        }
    }
}
