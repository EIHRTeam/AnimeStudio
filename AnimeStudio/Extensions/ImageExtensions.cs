using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AnimeStudio
{
    public static class ImageExtensions
    {
        public static void WriteToStream(this Image image, Stream stream, ImageFormat imageFormat)
        {
            switch (imageFormat)
            {
                case ImageFormat.Jpeg:
                    image.SaveAsJpeg(stream);
                    break;
                case ImageFormat.Png:
                    var pngEncoder = ResolvePngEncoder();
                    if (pngEncoder != null)
                    {
                        image.Save(stream, pngEncoder);
                    }
                    else
                    {
                        // Baseline: bit-identical to historical SaveAsPng()
                        // (level 6 + Adaptive). This is `--mode default`.
                        image.SaveAsPng(stream);
                    }
                    break;
                case ImageFormat.Bmp:
                    image.Save(stream, new BmpEncoder
                    {
                        BitsPerPixel = BmpBitsPerPixel.Pixel32,
                        SupportTransparency = true
                    });
                    break;
                case ImageFormat.Tga:
                    image.Save(stream, new TgaEncoder
                    {
                        BitsPerPixel = TgaBitsPerPixel.Pixel32,
                        Compression = TgaCompression.None
                    });
                    break;
            }
        }

        // Resolves the PNG encoder, or null to mean "use the baseline
        // SaveAsPng() path" for bit-identical default-mode output. Precedence:
        // experiment env knobs (ConvertProfiler, perf sweeps) > production
        // ImageExportSettings (set from the resolved --mode) > baseline.
        private static PngEncoder ResolvePngEncoder()
        {
            // Experiment sweep: an explicit level and/or filter env knob overrides
            // everything so one binary can walk the speed/size Pareto frontier. An
            // unset level keeps the default (6); an unset filter keeps Adaptive. The
            // retired PngFast shortcut (L1+None) is reproducible as PNG_LEVEL=1
            // PNG_FILTER=none.
            if (ConvertProfiler.PngLevel >= 0 || ConvertProfiler.PngFilter != null)
            {
                var level = ConvertProfiler.PngLevel >= 0
                    ? (PngCompressionLevel)ConvertProfiler.PngLevel
                    : PngCompressionLevel.DefaultCompression;
                var filter = ImageExportSettings.ParseFilter(ConvertProfiler.PngFilter)
                    ?? PngFilterMethod.Adaptive;
                return new PngEncoder { CompressionLevel = level, FilterMethod = filter };
            }

            if (ImageExportSettings.PngLevel is { } productionLevel)
            {
                return new PngEncoder
                {
                    CompressionLevel = productionLevel,
                    FilterMethod = ImageExportSettings.PngFilter ?? PngFilterMethod.None
                };
            }

            return null;
        }

        public static MemoryStream ConvertToStream(this Image image, ImageFormat imageFormat)
        {
            var stream = new MemoryStream();
            image.WriteToStream(stream, imageFormat);
            return stream;
        }

        public static byte[] ConvertToBytes<TPixel>(this Image<TPixel> image) where TPixel : unmanaged, IPixel<TPixel>
        {
            if (image.DangerousTryGetSinglePixelMemory(out var pixelSpan))
            {
                return MemoryMarshal.AsBytes(pixelSpan.Span).ToArray();

            }
            return null;
        }
    }
}
