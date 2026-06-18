using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AnimeStudio.CLI.Properties;

namespace AnimeStudio.CLI
{
    public static class CommandLine
    {
        public static int Init(string[] args)
        {
            var rootCommand = RegisterOptions();
            return rootCommand.Parse(args).Invoke();
        }

        public static RootCommand RegisterOptions()
        {
            var options = new CommandOptions();
            var rootCommand = new RootCommand();

            rootCommand.Options.Add(options.Silent);
            rootCommand.Options.Add(options.LoggerFlags);
            rootCommand.Options.Add(options.TypeFilter);
            rootCommand.Options.Add(options.NameFilter);
            rootCommand.Options.Add(options.ContainerFilter);
            rootCommand.Options.Add(options.GameName);
            rootCommand.Options.Add(options.MapOp);
            rootCommand.Options.Add(options.MapType);
            rootCommand.Options.Add(options.MapName);
            rootCommand.Options.Add(options.UnityVersion);
            rootCommand.Options.Add(options.GroupAssetsType);
            rootCommand.Options.Add(options.AssetExportType);
            rootCommand.Options.Add(options.Workers);
            rootCommand.Options.Add(options.Mode);
            rootCommand.Options.Add(options.Key);
            rootCommand.Options.Add(options.AIFile);
            rootCommand.Options.Add(options.DummyDllFolder);
            rootCommand.Arguments.Add(options.Input);
            rootCommand.Arguments.Add(options.Output);

            rootCommand.SetAction(parseResult => Program.Run(options.Bind(parseResult)));
            return rootCommand;
        }
    }

    public class Options
    {
        public bool Silent { get; set; }
        public LoggerEvent[] LoggerFlags { get; set; }
        public string[] TypeFilter { get; set; }
        public Regex[] NameFilter { get; set; }
        public Regex[] ContainerFilter { get; set; }
        public string GameName { get; set; }
        public MapOpType MapOp { get; set; }
        public ExportListType MapType { get; set; }
        public string MapName { get; set; }
        public string UnityVersion { get; set; }
        public AssetGroupOption GroupAssetsType { get; set; }
        public ExportType AssetExportType { get; set; }
        public int Workers { get; set; }
        public bool WorkersExplicitlySet { get; set; }
        public PerformanceMode? Mode { get; set; }
        public byte Key { get; set; }
        public FileInfo AIFile { get; set; }
        public DirectoryInfo DummyDllFolder { get; set; }
        public FileSystemInfo Input { get; set; }
        public DirectoryInfo Output { get; set; }
    }

    public sealed class CommandOptions
    {
        public readonly Option<bool> Silent;
        public readonly Option<LoggerEvent[]> LoggerFlags;
        public readonly Option<string[]> TypeFilter;
        public readonly Option<Regex[]> NameFilter;
        public readonly Option<Regex[]> ContainerFilter;
        public readonly Option<string> GameName;
        public readonly Option<MapOpType> MapOp;
        public readonly Option<ExportListType> MapType;
        public readonly Option<string> MapName;
        public readonly Option<string> UnityVersion;
        public readonly Option<AssetGroupOption> GroupAssetsType;
        public readonly Option<ExportType> AssetExportType;
        public readonly Option<int> Workers;
        public readonly Option<PerformanceMode?> Mode;
        public readonly Option<byte> Key;
        public readonly Option<FileInfo> AIFile;
        public readonly Option<DirectoryInfo> DummyDllFolder;
        public readonly Argument<FileSystemInfo> Input;
        public readonly Argument<DirectoryInfo> Output;

        public CommandOptions()
        {
            Silent = new Option<bool>("--silent")
            {
                Description = "Hide log messages."
            };
            LoggerFlags = new Option<LoggerEvent[]>("--logger_flags")
            {
                Description = "Flags to control toggle log events.",
                HelpName = "Verbose|Debug|Info|etc..",
                AllowMultipleArgumentsPerToken = true,
                DefaultValueFactory = _ =>
                [
                    LoggerEvent.Debug,
                    LoggerEvent.Info,
                    LoggerEvent.Warning,
                    LoggerEvent.Error
                ]
            };
            TypeFilter = new Option<string[]>("--types")
            {
                Description = "Specify unity class type(s)",
                HelpName = "Texture2D|Shader:Parse|Sprite:Both|etc..",
                AllowMultipleArgumentsPerToken = true
            };
            NameFilter = CreateRegexOption("--names", "Specify name regex filter(s).");
            ContainerFilter = CreateRegexOption("--containers", "Specify container regex filter(s).");
            GameName = new Option<string>("--game")
            {
                Description = "Specify Game.",
                Required = true
            };
            MapOp = new Option<MapOpType>("--map_op")
            {
                Description = "Specify which map to build.",
                DefaultValueFactory = _ => MapOpType.None
            };
            MapType = new Option<ExportListType>("--map_type")
            {
                Description = "AssetMap output type.",
                DefaultValueFactory = _ => ExportListType.XML
            };
            MapName = new Option<string>("--map_name")
            {
                Description = "Specify AssetMap file name.",
                DefaultValueFactory = _ => "assets_map"
            };
            UnityVersion = new Option<string>("--unity_version")
            {
                Description = "Specify Unity version."
            };
            GroupAssetsType = new Option<AssetGroupOption>("--group_assets")
            {
                Description = "Specify how exported assets should be grouped.",
                DefaultValueFactory = _ => AssetGroupOption.ByType
            };
            AssetExportType = new Option<ExportType>("--export_type")
            {
                Description = "Specify how assets should be exported.",
                DefaultValueFactory = _ => ExportType.Convert
            };
            Workers = new Option<int>("--workers")
            {
                Description = "Maximum parsing and export workers. Defaults to the logical CPU count.",
                DefaultValueFactory = _ => Environment.ProcessorCount
            };
            Mode = new Option<PerformanceMode?>("--mode")
            {
                Description = "Performance mode: fast | limit | default. "
                    + "Overrides the mode in ~/.anime/config.json. "
                    + "fast maximizes use of the machine; limit stays within the "
                    + "configured RAM/CPU budget; default keeps the conservative behavior."
                // No DefaultValueFactory: an unset value stays null so an explicit
                // "--mode default" can be distinguished from no flag.
            };
            Key = new Option<byte>("--key")
            {
                Description = "XOR key to decrypt MiHoYoBinData.",
                CustomParser = ParseKey
            };
            AIFile = new Option<FileInfo>("--ai_file")
            {
                Description = "Specify asset_index json file path (to recover GI containers)."
            };
            DummyDllFolder = new Option<DirectoryInfo>("--dummy_dlls")
            {
                Description = "Specify DummyDll path."
            };
            Input = new Argument<FileSystemInfo>("input_path")
            {
                Description = "Input file/folder."
            };
            Output = new Argument<DirectoryInfo>("output_path")
            {
                Description = "Output folder."
            };

            LoggerFlags.Validators.Add(ValidateNonEmpty);
            TypeFilter.Validators.Add(ValidateNonEmpty);
            NameFilter.Validators.Add(ValidateNonEmpty);
            ContainerFilter.Validators.Add(ValidateNonEmpty);
            Workers.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<int>() < 1)
                {
                    result.AddError("--workers must be at least 1.");
                }
            });
            Key.Validators.Add(ValidateKey);

            GameName.AcceptOnlyFromAmong(GameManager.GetGameNames());
            AIFile.AcceptExistingOnly();
            DummyDllFolder.AcceptExistingOnly();
            Input.AcceptExistingOnly();
            Output.AcceptLegalFilePathsOnly();
        }

        public Options Bind(ParseResult parseResult) =>
            new()
            {
                Silent = parseResult.GetValue(Silent),
                LoggerFlags = parseResult.GetValue(LoggerFlags) ?? [],
                TypeFilter = parseResult.GetValue(TypeFilter) ?? [],
                NameFilter = parseResult.GetValue(NameFilter) ?? [],
                ContainerFilter = parseResult.GetValue(ContainerFilter) ?? [],
                GameName = parseResult.GetValue(GameName),
                MapOp = parseResult.GetValue(MapOp),
                MapType = parseResult.GetValue(MapType),
                MapName = parseResult.GetValue(MapName),
                UnityVersion = parseResult.GetValue(UnityVersion),
                GroupAssetsType = parseResult.GetValue(GroupAssetsType),
                AssetExportType = parseResult.GetValue(AssetExportType),
                Workers = parseResult.GetValue(Workers),
                WorkersExplicitlySet = parseResult.GetResult(Workers) is { Tokens.Count: > 0 },
                Mode = parseResult.GetValue(Mode),
                Key = parseResult.GetValue(Key),
                AIFile = parseResult.GetValue(AIFile),
                DummyDllFolder = parseResult.GetValue(DummyDllFolder),
                Input = parseResult.GetRequiredValue(Input),
                Output = parseResult.GetRequiredValue(Output)
            };

        private static Option<Regex[]> CreateRegexOption(string name, string description) =>
            new(name)
            {
                Description = description,
                AllowMultipleArgumentsPerToken = true,
                CustomParser = ParseRegexFilters
            };

        private static Regex[] ParseRegexFilters(ArgumentResult result)
        {
            var values = result.Tokens.Select(token => token.Value).ToArray();
            if (values.Length == 1 && File.Exists(values[0]))
            {
                return File.ReadLines(values[0])
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(TryCreateRegex)
                    .Where(regex => regex != null)
                    .ToArray();
            }

            var regexes = new List<Regex>(values.Length);
            foreach (var value in values)
            {
                try
                {
                    regexes.Add(new Regex(value, RegexOptions.IgnoreCase));
                }
                catch (ArgumentException e)
                {
                    result.AddError("Invalid Regex.\n" + e.Message);
                    return [];
                }
            }

            return regexes.ToArray();
        }

        private static Regex TryCreateRegex(string value)
        {
            try
            {
                return new Regex(value, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static byte ParseKey(ArgumentResult result)
        {
            var value = result.Tokens.Single().Value;
            try
            {
                return ParseKey(value);
            }
            catch (Exception e) when (e is FormatException or OverflowException)
            {
                result.AddError("Invalid byte value.\n" + e.Message);
                return default;
            }
        }

        private static byte ParseKey(string value)
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToByte(value[2..], 16);
            }

            return byte.Parse(value);
        }

        private static void ValidateKey(OptionResult result)
        {
            if (result.Tokens.Count == 0)
            {
                return;
            }

            try
            {
                ParseKey(result.Tokens.Single().Value);
            }
            catch (Exception e) when (e is FormatException or OverflowException)
            {
                result.AddError("Invalid byte value.\n" + e.Message);
            }
        }

        private static void ValidateNonEmpty(OptionResult result)
        {
            if (result.Tokens.Any(token => string.IsNullOrWhiteSpace(token.Value)))
            {
                result.AddError("Empty string.");
            }
        }
    }
}
