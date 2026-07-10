using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimeStudio.CLI.Properties
{
    public sealed class TypeSetting
    {
        public bool? parse { get; set; }
        public bool? export { get; set; }
    }

    public sealed class UvSetting
    {
        public bool? enabled { get; set; }
        public int? channel { get; set; }
    }

    public sealed class StreamingSetting
    {
        public long containerMemoryThresholdMiB { get; set; } = 256;
        public string temporaryDirectory { get; set; }
    }

    public sealed class Settings
    {
        private const string FileName = "appsettings.json";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly Settings defaultInstance = Load();

        public static Settings Default => defaultInstance;

        public bool convertTexture { get; set; } = true;
        public bool convertAudio { get; set; } = true;
        public ImageFormat convertType { get; set; } = ImageFormat.Png;
        public bool eulerFilter { get; set; } = true;
        public decimal filterPrecision { get; set; } = 0.25m;
        public bool exportAllNodes { get; set; } = true;
        public bool exportSkins { get; set; } = true;
        public bool exportMaterials { get; set; }
        public bool collectAnimations { get; set; } = true;
        public bool exportAnimations { get; set; } = true;
        public decimal boneSize { get; set; } = 10m;
        public int fbxVersion { get; set; } = 3;
        public int fbxFormat { get; set; }
        public decimal scaleFactor { get; set; } = 1m;
        public bool exportBlendShape { get; set; } = true;
        public bool castToBone { get; set; }
        public bool restoreExtensionName { get; set; } = true;
        public bool enableFileLogging { get; set; }
        public bool minimalAssetMap { get; set; } = true;
        public bool allowDuplicates { get; set; }
        public bool scrapeMonos { get; set; }
        public StreamingSetting streaming { get; set; } = new();
        public Dictionary<ClassIDType, TypeSetting> types { get; set; } = CreateDefaultTypes();
        public Dictionary<string, UvSetting> uvs { get; set; } = CreateDefaultUvs();
        public Dictionary<string, int> texs { get; set; } = [];

        public Dictionary<ClassIDType, (bool, bool)> GetTypeFlags() =>
            types.ToDictionary(
                pair => pair.Key,
                pair => (pair.Value.parse.GetValueOrDefault(), pair.Value.export.GetValueOrDefault()));

        public Dictionary<string, (bool, int)> GetUvs() =>
            uvs.ToDictionary(
                pair => pair.Key,
                pair => (pair.Value.enabled.GetValueOrDefault(), pair.Value.channel.GetValueOrDefault()));

        public Dictionary<string, int> GetTextures() => new(texs);

        public ContainerStorageOptions GetContainerStorageOptions()
        {
            var thresholdMiB = streaming?.containerMemoryThresholdMiB ?? 256;
            if (thresholdMiB < 0)
            {
                throw new InvalidDataException(
                    $"streaming.containerMemoryThresholdMiB cannot be negative: {thresholdMiB}.");
            }

            var environmentDirectory = Environment.GetEnvironmentVariable("ANIMESTUDIO_TEMP_DIR");
            var temporaryDirectory = !string.IsNullOrWhiteSpace(environmentDirectory)
                ? environmentDirectory
                : streaming?.temporaryDirectory;

            return new ContainerStorageOptions
            {
                MemoryThresholdBytes = checked(thresholdMiB * 1024L * 1024L),
                TemporaryDirectory = string.IsNullOrWhiteSpace(temporaryDirectory)
                    ? null
                    : Path.GetFullPath(temporaryDirectory)
            };
        }

        private static Settings Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, FileName);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Configuration file not found at \"{path}\"; using defaults.");
                return new Settings();
            }

            try
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), SerializerOptions)
                    ?? new Settings();
                settings.types = MergeTypeSettings(CreateDefaultTypes(), settings.types);
                settings.uvs = MergeUvSettings(CreateDefaultUvs(), settings.uvs);
                settings.texs ??= [];
                settings.streaming ??= new StreamingSetting();
                return settings;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                Console.Error.WriteLine($"Unable to load \"{path}\"; using defaults. {e.Message}");
                return new Settings();
            }
        }

        private static Dictionary<ClassIDType, TypeSetting> MergeTypeSettings(
            Dictionary<ClassIDType, TypeSetting> defaults,
            Dictionary<ClassIDType, TypeSetting> configured)
        {
            if (configured == null)
            {
                return defaults;
            }

            foreach (var pair in configured)
            {
                if (pair.Value is null)
                {
                    continue;
                }

                defaults.TryGetValue(pair.Key, out var fallback);
                defaults[pair.Key] = new TypeSetting
                {
                    parse = pair.Value.parse ?? fallback?.parse ?? false,
                    export = pair.Value.export ?? fallback?.export ?? false
                };
            }

            return defaults;
        }

        private static Dictionary<string, UvSetting> MergeUvSettings(
            Dictionary<string, UvSetting> defaults,
            Dictionary<string, UvSetting> configured)
        {
            if (configured == null)
            {
                return defaults;
            }

            foreach (var pair in configured)
            {
                if (pair.Value is null)
                {
                    continue;
                }

                defaults.TryGetValue(pair.Key, out var fallback);
                defaults[pair.Key] = new UvSetting
                {
                    enabled = pair.Value.enabled ?? fallback?.enabled ?? false,
                    channel = pair.Value.channel ?? fallback?.channel ?? 0
                };
            }

            return defaults;
        }

        private static Dictionary<string, UvSetting> CreateDefaultUvs() =>
            new()
            {
                ["UV0"] = new UvSetting { enabled = true, channel = 0 },
                ["UV1"] = new UvSetting { enabled = true, channel = 1 },
                ["UV2"] = new UvSetting { enabled = false, channel = 0 },
                ["UV3"] = new UvSetting { enabled = false, channel = 0 },
                ["UV4"] = new UvSetting { enabled = false, channel = 0 },
                ["UV5"] = new UvSetting { enabled = false, channel = 0 },
                ["UV6"] = new UvSetting { enabled = false, channel = 0 },
                ["UV7"] = new UvSetting { enabled = false, channel = 0 }
            };

        private static Dictionary<ClassIDType, TypeSetting> CreateDefaultTypes() =>
            new()
            {
                [ClassIDType.Animation] = Type(true, false),
                [ClassIDType.AnimationClip] = Type(true, true),
                [ClassIDType.Animator] = Type(true, true),
                [ClassIDType.AnimatorController] = Type(true, false),
                [ClassIDType.AnimatorOverrideController] = Type(true, false),
                [ClassIDType.AssetBundle] = Type(true, false),
                [ClassIDType.AudioClip] = Type(true, true),
                [ClassIDType.Avatar] = Type(true, false),
                [ClassIDType.Font] = Type(true, true),
                [ClassIDType.GameObject] = Type(true, false),
                [ClassIDType.IndexObject] = Type(true, false),
                [ClassIDType.Material] = Type(true, true),
                [ClassIDType.Mesh] = Type(true, true),
                [ClassIDType.MeshFilter] = Type(true, false),
                [ClassIDType.MeshRenderer] = Type(true, false),
                [ClassIDType.MiHoYoBinData] = Type(true, true),
                [ClassIDType.MonoBehaviour] = Type(true, true),
                [ClassIDType.MonoScript] = Type(true, false),
                [ClassIDType.MovieTexture] = Type(true, true),
                [ClassIDType.PlayerSettings] = Type(true, false),
                [ClassIDType.RectTransform] = Type(true, false),
                [ClassIDType.Shader] = Type(true, true),
                [ClassIDType.SkinnedMeshRenderer] = Type(true, false),
                [ClassIDType.Sprite] = Type(true, true),
                [ClassIDType.SpriteAtlas] = Type(true, false),
                [ClassIDType.TextAsset] = Type(true, true),
                [ClassIDType.Texture2D] = Type(true, true),
                [ClassIDType.Transform] = Type(true, false),
                [ClassIDType.VideoClip] = Type(true, true),
                [ClassIDType.ResourceManager] = Type(true, false)
            };

        private static TypeSetting Type(bool parse, bool export) =>
            new() { parse = parse, export = export };
    }
}
