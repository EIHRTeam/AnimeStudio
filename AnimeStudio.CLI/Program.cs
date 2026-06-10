using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnimeStudio.CLI.Properties;
using Newtonsoft.Json;
using static AnimeStudio.CLI.Studio;

namespace AnimeStudio.CLI 
{
    public class Program
    {
        public static int Main(string[] args) => CommandLine.Init(args);

        public static int Run(Options o)
        {
            try
            {
                var game = GameManager.GetGame(o.GameName);

                // See https://github.com/Eleiyas/Z3-Asset-Map 
                var mapsPath = Path.Combine(Environment.CurrentDirectory, "Maps");
                var assetIndexPath = Path.Combine(mapsPath, "Z3-AssetIndex-Eleiyas.json");
                var paths = File.Exists(assetIndexPath)
                    ? JsonConvert.DeserializeObject<Dictionary<ulong, string>>(File.ReadAllText(assetIndexPath))
                    : new Dictionary<ulong, string>();

                Studio.Paths = paths;
                AssetsHelper.Paths = paths;

                if (game == null)
                {
                    Console.WriteLine("Invalid Game !!");
                    Console.WriteLine(GameManager.SupportedGames());
                    return 1;
                }

                if (game is UnityCNGame unityCNGame)
                {
                    UnityCN.SetKey(unityCNGame.Key);
                    Logger.Info($"[UnityCN] Selected Key is {unityCNGame.Key.Name} - {unityCNGame.Key.Key}");
                }

                Studio.Game = game;
                Logger.Default = new ConsoleLogger();
                Logger.Flags = o.LoggerFlags.Aggregate((e, x) => e |= x);
                Logger.FileLogging = Settings.Default.enableFileLogging;
                AssetsHelper.Minimal = Settings.Default.minimalAssetMap;
                AssetsHelper.SetUnityVersion(o.UnityVersion);

                TypeFlags.SetTypes(Settings.Default.GetTypeFlags());

                var classTypeFilter = Array.Empty<ClassIDType>();
                if (!o.TypeFilter.IsNullOrEmpty())
                {
                    var exportTexture2D = false;
                    var exportMaterial = false;
                    var classTypeFilterList = new List<ClassIDType>();
                    for (int i = 0; i < o.TypeFilter.Length; i++)
                    {
                        var typeStr = o.TypeFilter[i];
                        var type = ClassIDType.UnknownType;
                        var flag = TypeFlag.Both;
                    
                        try
                        {
                            if (typeStr.Contains(':'))
                            {
                                var param = typeStr.Split(':');
                    
                                flag = (TypeFlag)Enum.Parse(typeof(TypeFlag), param[1], true);
                    
                                typeStr = param[0];
                            }
                    
                            type = (ClassIDType)Enum.Parse(typeof(ClassIDType), typeStr, true);

                            if (type == ClassIDType.Texture2D)
                            {
                                exportTexture2D = flag.HasFlag(TypeFlag.Export);
                            }
                            else if (type == ClassIDType.Material)
                            {
                                exportMaterial = flag.HasFlag(TypeFlag.Export);
                            }
                    
                            TypeFlags.SetType(type, flag.HasFlag(TypeFlag.Parse), flag.HasFlag(TypeFlag.Export));
                    
                            classTypeFilterList.Add(type);
                        }
                        catch(Exception e)
                        {
                            Logger.Error($"{typeStr} has invalid format, skipping...");
                            continue;
                        }
                    }

                    classTypeFilter = classTypeFilterList.ToArray();

                    if (ClassIDType.GameObject.CanExport() || ClassIDType.Animator.CanExport())
                    {
                        TypeFlags.SetType(ClassIDType.Texture2D, true, exportTexture2D);
                        if (Settings.Default.exportMaterials)
                        {
                            TypeFlags.SetType(ClassIDType.Material, true, exportMaterial);
                        }
                        if (ClassIDType.GameObject.CanExport())
                        {
                            TypeFlags.SetType(ClassIDType.Animator, true, false);
                        }
                        else if(ClassIDType.Animator.CanExport())
                        {
                            TypeFlags.SetType(ClassIDType.GameObject, true, false);
                        }
                    }
                }

                if (o.GroupAssetsType == AssetGroupOption.ByContainer)
                {
                    TypeFlags.SetType(ClassIDType.AssetBundle, true, false);
                }

                assetsManager.Silent = o.Silent;
                assetsManager.Game = game;
                assetsManager.SpecifyUnityVersion = o.UnityVersion;
                o.Output.Create();

                if (o.Key != default)
                {
                    MiHoYoBinData.Encrypted = true;
                    MiHoYoBinData.Key = o.Key;
                }

                if (o.AIFile != null && game.Type.IsGISubGroup())
                {
                    ResourceIndex.FromFile(o.AIFile.FullName);
                }

                if (o.DummyDllFolder != null)
                {
                    assemblyLoader.Load(o.DummyDllFolder.FullName);
                }

                Logger.Info("Scanning for files...");
                var files = o.Input.Attributes.HasFlag(FileAttributes.Directory) ? Directory.GetFiles(o.Input.FullName, "*.*", SearchOption.AllDirectories).OrderBy(x => x.Length).ToArray() : new string[] { o.Input.FullName };
                Logger.Info($"Found {files.Length} files");

                if (o.MapOp.HasFlag(MapOpType.CABMap))
                {
                    if (o.MapOp.HasFlag(MapOpType.Load))
                    {
                        AssetsHelper.BuildCABMap(files, o.MapName, o.Input.FullName, game);
                    }
                    else
                    {
                        AssetsHelper.LoadCABMapInternal(o.MapName);
                        assetsManager.ResolveDependencies = true;
                    }
                }
                if (o.MapOp.HasFlag(MapOpType.AssetMap))
                {
                    if (o.MapOp.HasFlag(MapOpType.Load))
                    {
                        files = AssetsHelper.ParseAssetMap(o.MapName, o.MapType, classTypeFilter, o.NameFilter, o.ContainerFilter);
                    }
                    else
                    {
                        Task.Run(() => AssetsHelper.BuildAssetMap(files, o.MapName, game, o.Output.FullName, o.MapType, classTypeFilter, o.NameFilter, o.ContainerFilter)).Wait();
                    }
                }
                if (o.MapOp.HasFlag(MapOpType.Both))
                {
                    Task.Run(() => AssetsHelper.BuildBoth(files, o.MapName, o.Input.FullName, game, o.Output.FullName, o.MapType, classTypeFilter, o.NameFilter, o.ContainerFilter)).Wait();
                }
                if (o.MapOp.Equals(MapOpType.None) || o.MapOp.HasFlag(MapOpType.Load))
                {
                    var i = 0;

                    var path = Path.GetDirectoryName(Path.GetFullPath(files[0]));
                    ImportHelper.MergeSplitAssets(path);
                    var toReadFile = ImportHelper.ProcessingSplitFiles(files.ToList());

                    var fileList = new List<string>(toReadFile);
                    foreach (var file in fileList)
                    {
                        try
                        {
                            assetsManager.LoadFiles(file);
                            if (assetsManager.assetsFileList.Count > 0)
                            {
                                BuildAssetData(classTypeFilter, o.NameFilter, o.ContainerFilter, ref i);
                                ExportAssets(o.Output.FullName, exportableAssets, o.GroupAssetsType, o.AssetExportType);
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"Failed to process \"{file}\": {e}");
                        }
                        finally
                        {
                            exportableAssets.Clear();
                            assetsManager.Clear();
                        }
                    }
                }
                if (Properties.Settings.Default.scrapeMonos)
                {
                    Directory.CreateDirectory(mapsPath);
                    File.WriteAllLines(Path.Combine(mapsPath, "PathStrings_Sorted.txt"), PathStrings.Distinct().OrderBy(p => p));
                    File.WriteAllLines(Path.Combine(mapsPath, "VOStrings_Sorted.txt"), VOStrings.Distinct().OrderBy(p => p));
                    File.WriteAllLines(Path.Combine(mapsPath, "EventStrings_Sorted.txt"), EventStrings.Distinct().OrderBy(p => p));
                }

                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 1;
            }
        }
    }
}
