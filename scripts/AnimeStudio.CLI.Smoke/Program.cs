using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: AnimeStudio.CLI.Smoke <publish-directory> <rid>");
    return 2;
}

var publishDirectory = Path.GetFullPath(args[0]);
var rid = args[1];

try
{
    var runtimeCompatible = IsRuntimeCompatible(rid);
    VerifyPackageLayout(publishDirectory, rid);
    VerifyRuntimeConfiguration(publishDirectory);
    VerifyStreamingConfiguration(publishDirectory, runtimeCompatible);

    if (runtimeCompatible)
    {
        VerifyRunSummary(publishDirectory);
        VerifyAssetMapFailureTiming(publishDirectory);
        VerifyExplicitTypeFilter(publishDirectory);
        VerifyScrapeChunkMerge(publishDirectory);
        VerifyAclResultValidation(publishDirectory);
        VerifyFmodAudioConversion(publishDirectory, rid);
        VerifyFbxNativeSupport(publishDirectory);
        VerifyFbxFailureCleanup(publishDirectory);
        VerifyOptimizedAnimatorGuard(publishDirectory);

        if (rid == "win-x64")
        {
            VerifyWindowsNativeLibraries(publishDirectory);
            VerifyWindowsCapabilityPath(publishDirectory);
        }
        else
        {
            VerifyUnixDegradationPaths(publishDirectory);
        }
    }

    var scope = runtimeCompatible ? "package and runtime" : "cross-platform package";
    Console.WriteLine($"{scope} smoke checks passed for {rid}.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static void VerifyRuntimeConfiguration(string publishDirectory)
{
    var runtimeConfigPath = Path.Combine(
        publishDirectory,
        "AnimeStudio.CLI.runtimeconfig.json");
    Assert(File.Exists(runtimeConfigPath), "CLI runtime configuration is missing.");

    using var document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));
    var properties = document.RootElement
        .GetProperty("runtimeOptions")
        .GetProperty("configProperties");

    Assert(!properties.GetProperty("System.GC.Server").GetBoolean(), "Server GC must remain disabled.");
    Assert(properties.GetProperty("System.GC.Concurrent").GetBoolean(), "Concurrent GC must remain enabled.");
    Assert(
        properties.GetProperty("System.GC.HeapHardLimitPercent").GetInt32() == 75,
        "GC heap hard limit must be 75 percent.");
    Assert(properties.GetProperty("System.GC.RetainVM").GetBoolean(), "GC RetainVM must remain enabled.");
}

static bool IsRuntimeCompatible(string rid)
{
    return rid switch
    {
        "win-x64" => OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64,
        "linux-x64" => OperatingSystem.IsLinux()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64,
        "osx-arm64" => OperatingSystem.IsMacOS()
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
        _ => false
    };
}

static void VerifyStreamingConfiguration(string publishDirectory, bool verifyRuntimeOverride)
{
    using (var document = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(publishDirectory, "appsettings.json"))))
    {
        var streaming = document.RootElement.GetProperty("streaming");
        Assert(
            streaming.GetProperty("containerMemoryThresholdMiB").GetInt64() == 256,
            "The default container memory threshold must be 256 MiB.");
        Assert(
            streaming.GetProperty("temporaryDirectory").ValueKind == JsonValueKind.Null,
            "The default temporary directory must remain platform-resolved.");
    }

    if (!verifyRuntimeOverride)
    {
        return;
    }

    var previousDirectory = Environment.GetEnvironmentVariable("ANIMESTUDIO_TEMP_DIR");
    var configuredDirectory = Path.Combine(
        Path.GetTempPath(),
        $"animestudio-streaming-config-{Guid.NewGuid():N}");
    try
    {
        Environment.SetEnvironmentVariable("ANIMESTUDIO_TEMP_DIR", configuredDirectory);
        using var context = CreateLoadContext(publishDirectory);
        var cliAssembly = context.LoadFromAssemblyPath(
            Path.Combine(publishDirectory, "AnimeStudio.CLI.dll"));
        var settingsType = RequireType(cliAssembly, "AnimeStudio.CLI.Properties.Settings");
        var defaultSettings = settingsType.GetProperty(
            "Default",
            BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null)
            ?? throw new MissingMemberException(settingsType.FullName, "Default");
        var getOptionsMethod = settingsType.GetMethod(
            "GetContainerStorageOptions",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(settingsType.FullName, "GetContainerStorageOptions");
        var options = getOptionsMethod.Invoke(defaultSettings, null)
            ?? throw new InvalidOperationException("Streaming options were not created.");
        var optionsType = options.GetType();

        Assert(
            (long)(optionsType.GetProperty("MemoryThresholdBytes")?.GetValue(options)
                ?? throw new MissingMemberException(optionsType.FullName, "MemoryThresholdBytes"))
                == 256L * 1024 * 1024,
            "Streaming threshold was not converted from MiB to bytes.");
        Assert(
            Path.GetFullPath((string)(optionsType.GetProperty("TemporaryDirectory")?.GetValue(options)
                ?? throw new MissingMemberException(optionsType.FullName, "TemporaryDirectory")))
                == Path.GetFullPath(configuredDirectory),
            "ANIMESTUDIO_TEMP_DIR did not override the configured temporary directory.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("ANIMESTUDIO_TEMP_DIR", previousDirectory);
    }
}

static void VerifyExplicitTypeFilter(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var cliAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.CLI.dll"));
    var coreAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.dll"));
    var programType = RequireType(cliAssembly, "AnimeStudio.CLI.Program");
    var mainMethod = programType.GetMethod(
        "Main",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(programType.FullName, "Main");
    var classIdType = RequireType(coreAssembly, "AnimeStudio.ClassIDType");
    var typeFlagsType = RequireType(coreAssembly, "AnimeStudio.TypeFlags");
    var canParseMethod = typeFlagsType.GetMethod(
        "CanParse",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(typeFlagsType.FullName, "CanParse");
    var animatorController = Enum.Parse(classIdType, "AnimatorController");
    var mesh = Enum.Parse(classIdType, "Mesh");
    var temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"animestudio-cli-types-smoke-{Guid.NewGuid():N}");
    var inputDirectory = Path.Combine(temporaryDirectory, "input");
    var outputDirectory = Path.Combine(temporaryDirectory, "output");
    var originalOutput = Console.Out;
    using var capturedOutput = new StringWriter();

    try
    {
        Directory.CreateDirectory(inputDirectory);
        File.WriteAllBytes(Path.Combine(inputDirectory, "empty.bin"), []);
        Console.SetOut(capturedOutput);
        var exitCode = (int)mainMethod.Invoke(
            null,
            [
                new[]
                {
                    inputDirectory,
                    outputDirectory,
                    "--game",
                    "ArknightsEndfield",
                    "--types",
                    "AnimatorController:Both",
                    "--group_assets",
                    "ByType",
                }
            ])!;

        Assert(exitCode == 0, $"CLI type-filter smoke returned exit code {exitCode}.");
        Assert(
            (bool)canParseMethod.Invoke(null, [animatorController])!,
            "Explicit AnimatorController type was not enabled.");
        Assert(
            !(bool)canParseMethod.Invoke(null, [mesh])!,
            "Explicit type filters must disable unrequested default types.");
        var output = capturedOutput.ToString().TrimEnd();
        Assert(output.Contains("Run summary:", StringComparison.Ordinal), "CLI run summary is missing.");
        Assert(
            output.Contains("Input size before extraction: 0 B (0 bytes)", StringComparison.Ordinal),
            "CLI run summary has an incorrect input size.");
        Assert(
            output.Contains("Output files: 0", StringComparison.Ordinal),
            "CLI run summary has an incorrect output file count.");
        Assert(
            output.EndsWith("Output size: 0 B (0 bytes)", StringComparison.Ordinal),
            "CLI run summary is not the final CLI output.");
    }
    finally
    {
        Console.SetOut(originalOutput);
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}

static void VerifyRunSummary(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var cliAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.CLI.dll"));
    var summaryType = RequireType(cliAssembly, "AnimeStudio.CLI.RunSummary");
    var formatElapsedMethod = summaryType.GetMethod(
        "FormatElapsed",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(summaryType.FullName, "FormatElapsed");
    var formatByteSizeMethod = summaryType.GetMethod(
        "FormatByteSize",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(summaryType.FullName, "FormatByteSize");
    var measureDirectoryMethod = summaryType.GetMethod(
        "MeasureDirectory",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(summaryType.FullName, "MeasureDirectory");

    Assert(
        (string)formatElapsedMethod.Invoke(null, [TimeSpan.FromSeconds(12345)])!
            == "03:25:45 (12345s)",
        "Run summary elapsed-time format is incorrect.");
    Assert(
        (string)formatElapsedMethod.Invoke(null, [TimeSpan.FromSeconds(97261)])!
            == "27:01:01 (97261s)",
        "Run summary elapsed-time format must support more than 24 hours.");
    Assert(
        (string)formatByteSizeMethod.Invoke(null, [1536L])!
            == "1.50 KiB (1,536 bytes)",
        "Run summary byte-size format is incorrect.");

    var temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"animestudio-cli-summary-smoke-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(Path.Combine(temporaryDirectory, "nested"));
        File.WriteAllBytes(Path.Combine(temporaryDirectory, "first.bin"), new byte[3]);
        File.WriteAllBytes(Path.Combine(temporaryDirectory, "nested", "second.bin"), new byte[5]);
        var statistics = measureDirectoryMethod.Invoke(null, [temporaryDirectory])
            ?? throw new InvalidOperationException("Run summary returned no directory statistics.");
        var statisticsType = statistics.GetType();

        Assert(
            (long)(statisticsType.GetProperty("FileCount")?.GetValue(statistics)
                ?? throw new MissingMemberException(statisticsType.FullName, "FileCount")) == 2,
            "Run summary directory file count is incorrect.");
        Assert(
            (long)(statisticsType.GetProperty("TotalBytes")?.GetValue(statistics)
                ?? throw new MissingMemberException(statisticsType.FullName, "TotalBytes")) == 8,
            "Run summary directory byte count is incorrect.");
    }
    finally
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}

static void VerifyAssetMapFailureTiming(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var cliAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.CLI.dll"));
    var programType = RequireType(cliAssembly, "AnimeStudio.CLI.Program");
    var mainMethod = programType.GetMethod(
        "Main",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(programType.FullName, "Main");
    var temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"animestudio-asset-map-timing-smoke-{Guid.NewGuid():N}");
    var inputDirectory = Path.Combine(temporaryDirectory, "input");
    var outputDirectory = Path.Combine(temporaryDirectory, "output");
    var originalOutput = Console.Out;
    using var capturedOutput = new StringWriter();

    try
    {
        Directory.CreateDirectory(inputDirectory);
        File.WriteAllBytes(Path.Combine(inputDirectory, "empty.bin"), []);
        Console.SetOut(capturedOutput);
        var exitCode = (int)mainMethod.Invoke(
            null,
            [
                new[]
                {
                    inputDirectory,
                    outputDirectory,
                    "--game",
                    "ArknightsEndfield",
                    "--map_op",
                    "AssetMap",
                    "--map_type",
                    "XML",
                    "--map_name",
                    "timing-smoke",
                }
            ])!;

        Assert(exitCode == 0, $"CLI AssetMap timing smoke returned exit code {exitCode}.");
        var output = capturedOutput.ToString();
        var timingIndex = output.IndexOf(
            "AssetMap stage timings (0 assets):",
            StringComparison.Ordinal);
        var summaryIndex = output.IndexOf("Run summary:", StringComparison.Ordinal);
        Assert(
            output.Contains("AssetMap was not build", StringComparison.Ordinal),
            "AssetMap parse failure warning was suppressed.");
        Assert(timingIndex >= 0, "AssetMap parse failure did not print stage timings.");
        Assert(
            output.Contains("  Loading: ", StringComparison.Ordinal),
            "AssetMap loading timing is missing.");
        Assert(
            output.Contains("  XML writer: not run", StringComparison.Ordinal),
            "AssetMap timing did not identify the unrun XML writer.");
        Assert(
            summaryIndex > timingIndex,
            "AssetMap stage timings must be printed before the final run summary.");
    }
    finally
    {
        Console.SetOut(originalOutput);
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}

static void VerifyScrapeChunkMerge(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var cliAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.CLI.dll"));
    var studioType = RequireType(cliAssembly, "AnimeStudio.CLI.Studio");
    var resetMethod = studioType.GetMethod(
        "ResetScrapedStrings",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(studioType.FullName, "ResetScrapedStrings");
    var flushMethod = studioType.GetMethod(
        "FlushScrapedStrings",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(studioType.FullName, "FlushScrapedStrings");
    var completeMethod = studioType.GetMethod(
        "CompleteScrapedStrings",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(studioType.FullName, "CompleteScrapedStrings");
    var pathStrings = studioType.GetProperty(
        "PathStrings",
        BindingFlags.Public | BindingFlags.Static)
        ?.GetValue(null)
        ?? throw new MissingMemberException(studioType.FullName, "PathStrings");
    var addMethod = pathStrings.GetType().GetMethod("Add")
        ?? throw new MissingMethodException(pathStrings.GetType().FullName, "Add");
    var temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"animestudio-cli-smoke-{Guid.NewGuid():N}");

    try
    {
        resetMethod.Invoke(null, [temporaryDirectory]);
        addMethod.Invoke(pathStrings, ["zeta"]);
        addMethod.Invoke(pathStrings, ["alpha"]);
        flushMethod.Invoke(null, [temporaryDirectory]);

        addMethod.Invoke(pathStrings, ["beta"]);
        addMethod.Invoke(pathStrings, ["alpha"]);
        flushMethod.Invoke(null, [temporaryDirectory]);
        completeMethod.Invoke(null, [temporaryDirectory]);

        Assert(
            File.ReadAllLines(Path.Combine(temporaryDirectory, "PathStrings_Sorted.txt"))
                .SequenceEqual(["alpha", "beta", "zeta"]),
            "Scraped path strings were not globally sorted and de-duplicated.");
        Assert(
            File.ReadAllLines(Path.Combine(temporaryDirectory, "VOStrings_Sorted.txt")).Length == 0,
            "Empty scraped VO output is not empty.");
        Assert(
            !Directory.Exists(Path.Combine(temporaryDirectory, ".animestudio-scrape")),
            "Scrape chunk directory was not cleaned up.");
    }
    finally
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}

static void VerifyAclResultValidation(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var utilityAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.Utility.dll"));
    var clipType = RequireType(utilityAssembly, "ACLLibs.DecompressedClip");
    var resultType = RequireType(utilityAssembly, "ACLLibs.DecompressedClipResult");
    var copyMethod = resultType.GetMethod(
        "Copy",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(resultType.FullName, "Copy");
    var clip = Activator.CreateInstance(clipType)!;
    RequireField(clipType, "ValuesCount").SetValue(clip, -1);
    object?[] arguments = [clip, null, null];

    try
    {
        copyMethod.Invoke(null, arguments);
        throw new InvalidOperationException("ACL result validation accepted a negative element count.");
    }
    catch (TargetInvocationException exception)
    {
        Assert(
            exception.InnerException is InvalidDataException,
            $"ACL result validation returned {exception.InnerException?.GetType().Name}.");
    }
}

static void VerifyPackageLayout(string publishDirectory, string rid)
{
    Assert(Directory.Exists(publishDirectory), $"Publish directory does not exist: {publishDirectory}");
    Assert(File.Exists(Path.Combine(publishDirectory, "AnimeStudio.CLI.dll")), "CLI assembly is missing.");
    Assert(File.Exists(Path.Combine(publishDirectory, "appsettings.json")), "appsettings.json is missing.");
    Assert(File.Exists(Path.Combine(publishDirectory, "LICENSE")), "Project license is missing.");
    Assert(File.Exists(Path.Combine(publishDirectory, "THIRD_PARTY_NOTICES.md")), "Third-party notices are missing.");
    Assert(!File.Exists(Path.Combine(publishDirectory, "BinaryDecompiler.lib")), "Link-only BinaryDecompiler.lib was published.");

    var packageFiles = Directory
        .EnumerateFiles(publishDirectory, "*", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .Where(static file => file is not null)
        .Cast<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    string[] expected;
    string[] forbidden;

    switch (rid)
    {
        case "win-x64":
            expected =
            [
                "AnimeStudio.FBXNative.dll",
                "AnimeStudio.Ooz.dll",
                "Texture2DDecoderNative.dll",
                "HLSLDecompiler.dll",
                "acl.dll",
                "sracl.dll",
                "acldb.dll",
                "acldb_zzz.dll",
                "fmod.dll",
            ];
            forbidden =
            [
                "libAnimeStudio.FBXNative.so",
                "libAnimeStudio.Ooz.so",
                "libTexture2DDecoderNative.so",
                "libfmod.so",
                "libAnimeStudio.FBXNative.dylib",
                "libAnimeStudio.Ooz.dylib",
                "libTexture2DDecoderNative.dylib",
                "libfmod.dylib",
            ];
            break;
        case "linux-x64":
            expected =
            [
                "libAnimeStudio.FBXNative.so",
                "libAnimeStudio.Ooz.so",
                "libTexture2DDecoderNative.so",
                "libfmod.so",
            ];
            forbidden =
            [
                "AnimeStudio.FBXNative.dll",
                "AnimeStudio.Ooz.dll",
                "Texture2DDecoderNative.dll",
                "HLSLDecompiler.dll",
                "acl.dll",
                "sracl.dll",
                "acldb.dll",
                "acldb_zzz.dll",
                "fmod.dll",
                "libAnimeStudio.FBXNative.dylib",
                "libAnimeStudio.Ooz.dylib",
                "libTexture2DDecoderNative.dylib",
                "libfmod.dylib",
            ];
            break;
        case "osx-arm64":
            expected =
            [
                "libAnimeStudio.FBXNative.dylib",
                "libAnimeStudio.Ooz.dylib",
                "libTexture2DDecoderNative.dylib",
                "libfmod.dylib",
            ];
            forbidden =
            [
                "AnimeStudio.FBXNative.dll",
                "AnimeStudio.Ooz.dll",
                "Texture2DDecoderNative.dll",
                "HLSLDecompiler.dll",
                "acl.dll",
                "sracl.dll",
                "acldb.dll",
                "acldb_zzz.dll",
                "fmod.dll",
                "libAnimeStudio.FBXNative.so",
                "libAnimeStudio.Ooz.so",
                "libTexture2DDecoderNative.so",
                "libfmod.so",
            ];
            break;
        default:
            throw new InvalidOperationException($"Unsupported RID: {rid}");
    }

    foreach (var file in expected)
    {
        Assert(packageFiles.Contains(file), $"Expected native file is missing: {file}");
    }

    foreach (var file in forbidden)
    {
        Assert(!packageFiles.Contains(file), $"Foreign native file was published for {rid}: {file}");
    }
}

static void VerifyFbxNativeSupport(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var utilityAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.Utility.dll"));
    var capabilitiesType = RequireType(utilityAssembly, "AnimeStudio.PlatformCapabilities");
    var supportMethod = capabilitiesType.GetMethod(
        "TryGetFbxExportSupport",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(
            capabilitiesType.FullName,
            "TryGetFbxExportSupport");
    object?[] arguments = [null];

    Assert(
        (bool)supportMethod.Invoke(null, arguments)!,
        $"FBXNative capability failed: {arguments[0]}");
}

static void VerifyFbxFailureCleanup(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var wrapperAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.FBXWrapper.dll"));
    var coreAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.dll"));
    var exporterContextType = RequireType(
        wrapperAssembly,
        "AnimeStudio.FbxInterop.FbxExporterContext");
    var disposeMethod = exporterContextType.GetMethod(
        "Dispose",
        BindingFlags.Instance | BindingFlags.NonPublic,
        [typeof(bool)])
        ?? throw new MissingMethodException(exporterContextType.FullName, "Dispose");
    var partialContext = RuntimeHelpers.GetUninitializedObject(exporterContextType);

    disposeMethod.Invoke(partialContext, [false]);
    disposeMethod.Invoke(partialContext, [false]);

    var fbxType = RequireType(wrapperAssembly, "AnimeStudio.Fbx");
    var exporterType = fbxType.GetNestedType("Exporter", BindingFlags.Public)
        ?? throw new MissingMemberException(fbxType.FullName, "Exporter");
    var exportOptionsType = fbxType.GetNestedType("ExportOptions", BindingFlags.Public)
        ?? throw new MissingMemberException(fbxType.FullName, "ExportOptions");
    var importedType = RequireType(coreAssembly, "AnimeStudio.IImported");
    var exportMethod = exporterType.GetMethod(
        "Export",
        BindingFlags.Public | BindingFlags.Static,
        [typeof(string), importedType, exportOptionsType])
        ?? throw new MissingMethodException(exporterType.FullName, "Export");
    var options = Activator.CreateInstance(exportOptionsType)
        ?? throw new InvalidOperationException("Unable to create FBX export options.");
    var originalDirectory = Directory.GetCurrentDirectory();
    var temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"animestudio-fbx-cleanup-{Guid.NewGuid():N}");

    try
    {
        try
        {
            exportMethod.Invoke(
                null,
                [Path.Combine(temporaryDirectory, "failure.fbx"), null, options]);
            throw new InvalidOperationException("FBX export unexpectedly accepted a null model.");
        }
        catch (TargetInvocationException)
        {
        }

        Assert(
            Directory.GetCurrentDirectory() == originalDirectory,
            "FBX export failure did not restore the working directory.");
    }
    finally
    {
        Directory.SetCurrentDirectory(originalDirectory);
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}

static void VerifyOptimizedAnimatorGuard(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var cliAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.CLI.dll"));
    var coreAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.dll"));
    var exporterType = RequireType(cliAssembly, "AnimeStudio.CLI.Exporter");
    var animatorType = RequireType(coreAssembly, "AnimeStudio.Animator");
    var supportMethod = exporterType.GetMethod(
        "TryGetAnimatorConversionSupport",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(
            exporterType.FullName,
            "TryGetAnimatorConversionSupport");
    var animator = RuntimeHelpers.GetUninitializedObject(animatorType);
    RequireField(animatorType, "m_HasTransformHierarchy").SetValue(animator, false);
    object?[] arguments = [animator, null];

    Assert(
        !(bool)supportMethod.Invoke(null, arguments)!,
        "Optimized Animator without an Avatar was accepted.");
    Assert(
        arguments[1] is string reason && reason.Contains("Avatar", StringComparison.Ordinal),
        "Optimized Animator rejection did not explain the missing Avatar.");

    RequireField(animatorType, "m_HasTransformHierarchy").SetValue(animator, true);
    arguments = [animator, null];
    Assert(
        (bool)supportMethod.Invoke(null, arguments)!,
        "Animator with a transform hierarchy was rejected.");
}

static void VerifyFmodAudioConversion(string publishDirectory, string rid)
{
    using var context = CreateLoadContext(publishDirectory);
    var utilityAssembly = context.LoadFromAssemblyPath(
        Path.Combine(publishDirectory, "AnimeStudio.Utility.dll"));

    var capabilitiesType = RequireType(utilityAssembly, "AnimeStudio.PlatformCapabilities");
    var supportMethod = capabilitiesType.GetMethod(
        "TryGetFmodAudioConversionSupport",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(
            capabilitiesType.FullName,
            "TryGetFmodAudioConversionSupport");
    object?[] supportArguments = [null];
    var supported = (bool)supportMethod.Invoke(null, supportArguments)!;
    Assert(supported, $"FMOD capability failed: {supportArguments[0]}");

    var factoryType = RequireType(utilityAssembly, "FMOD.Factory");
    var resultType = RequireType(utilityAssembly, "FMOD.RESULT");
    var outputType = RequireType(utilityAssembly, "FMOD.OUTPUTTYPE");
    var initFlagsType = RequireType(utilityAssembly, "FMOD.INITFLAGS");
    var modeType = RequireType(utilityAssembly, "FMOD.MODE");
    var timeUnitType = RequireType(utilityAssembly, "FMOD.TIMEUNIT");
    var exInfoType = RequireType(utilityAssembly, "FMOD.CREATESOUNDEXINFO");

    object?[] createSystemArguments = [null];
    AssertFmodOk(
        factoryType.GetMethod("System_Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, createSystemArguments),
        resultType,
        "System_Create");
    var system = createSystemArguments[0]
        ?? throw new InvalidOperationException("FMOD returned a null system.");

    try
    {
        AssertFmodOk(
            Invoke(system, "setOutput", Enum.Parse(outputType, "NOSOUND")),
            resultType,
            "System_SetOutput");
        AssertFmodOk(
            Invoke(system, "init", 1, Enum.Parse(initFlagsType, "NORMAL"), IntPtr.Zero),
            resultType,
            "System_Init");

        object?[] versionArguments = [null];
        AssertFmodOk(
            InvokeWithArguments(system, "getVersion", versionArguments),
            resultType,
            "System_GetVersion");
        var version = (uint)versionArguments[0]!;
        const uint expectedVersion = 0x00020314;
        Assert(
            version == expectedVersion,
            $"FMOD runtime version is 0x{version:X8}; expected 0x{expectedVersion:X8} for {rid}.");

        var wav = CreatePcmWave();
        var exInfo = Activator.CreateInstance(exInfoType)!;
        RequireField(exInfoType, "cbsize").SetValue(exInfo, Marshal.SizeOf(exInfoType));
        RequireField(exInfoType, "length").SetValue(exInfo, (uint)wav.Length);

        object?[] createSoundArguments =
        [
            wav,
            Enum.Parse(modeType, "OPENMEMORY"),
            exInfo,
            null,
        ];
        AssertFmodOk(
            InvokeWithArguments(system, "createSound", createSoundArguments),
            resultType,
            "System_CreateSound");
        var sound = createSoundArguments[3]
            ?? throw new InvalidOperationException("FMOD returned a null sound.");

        try
        {
            object?[] formatArguments = [null, null, null, null];
            AssertFmodOk(
                InvokeWithArguments(sound, "getFormat", formatArguments),
                resultType,
                "Sound_GetFormat");
            Assert((int)formatArguments[2]! == 1, "FMOD decoded an unexpected channel count.");
            Assert((int)formatArguments[3]! == 16, "FMOD decoded an unexpected bit depth.");

            object?[] lengthArguments = [null, Enum.Parse(timeUnitType, "PCMBYTES")];
            AssertFmodOk(
                InvokeWithArguments(sound, "getLength", lengthArguments),
                resultType,
                "Sound_GetLength");
            var pcmLength = (uint)lengthArguments[0]!;
            Assert(pcmLength == 160, $"FMOD decoded {pcmLength} PCM bytes instead of 160.");

            object?[] lockArguments = [0u, pcmLength, null, null, null, null];
            AssertFmodOk(
                InvokeWithArguments(sound, "lock", lockArguments),
                resultType,
                "Sound_Lock");
            var len1 = (uint)lockArguments[4]!;
            var len2 = (uint)lockArguments[5]!;
            Assert(len1 + len2 == pcmLength, "FMOD did not expose the complete PCM buffer.");
            AssertFmodOk(
                Invoke(
                    sound,
                    "unlock",
                    (IntPtr)lockArguments[2]!,
                    (IntPtr)lockArguments[3]!,
                    len1,
                    len2),
                resultType,
                "Sound_Unlock");
        }
        finally
        {
            Invoke(sound, "release");
        }
    }
    finally
    {
        Invoke(system, "release");
    }
}

static object? Invoke(object instance, string methodName, params object?[] arguments)
{
    return InvokeWithArguments(instance, methodName, arguments);
}

static object? InvokeWithArguments(object instance, string methodName, object?[] arguments)
{
    var method = instance.GetType().GetMethod(
        methodName,
        BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
    return method.Invoke(instance, arguments);
}

static void AssertFmodOk(object? result, Type resultType, string operation)
{
    Assert(
        Convert.ToInt32(result) == Convert.ToInt32(Enum.Parse(resultType, "OK")),
        $"{operation} returned {result}.");
}

static byte[] CreatePcmWave()
{
    const int sampleRate = 8000;
    const short channels = 1;
    const short bits = 16;
    const int samples = 80;
    var pcmBytes = samples * channels * bits / 8;
    var data = new byte[44 + pcmBytes];

    "RIFF"u8.CopyTo(data);
    BitConverter.GetBytes(data.Length - 8).CopyTo(data, 4);
    "WAVEfmt "u8.CopyTo(data.AsSpan(8));
    BitConverter.GetBytes(16).CopyTo(data, 16);
    BitConverter.GetBytes((short)1).CopyTo(data, 20);
    BitConverter.GetBytes(channels).CopyTo(data, 22);
    BitConverter.GetBytes(sampleRate).CopyTo(data, 24);
    BitConverter.GetBytes(sampleRate * channels * bits / 8).CopyTo(data, 28);
    BitConverter.GetBytes((short)(channels * bits / 8)).CopyTo(data, 32);
    BitConverter.GetBytes(bits).CopyTo(data, 34);
    "data"u8.CopyTo(data.AsSpan(36));
    BitConverter.GetBytes(pcmBytes).CopyTo(data, 40);
    return data;
}

static void VerifyWindowsNativeLibraries(string publishDirectory)
{
    Assert(OperatingSystem.IsWindows(), "win-x64 smoke must run on Windows.");

    var exports = new Dictionary<string, string[]>
    {
        ["HLSLDecompiler.dll"] = ["Decompile"],
        ["acl.dll"] = ["DecompressAll", "Dispose"],
        ["sracl.dll"] = ["DecompressAll", "Dispose"],
        ["acldb.dll"] = ["DecompressTracks", "Dispose"],
        ["acldb_zzz.dll"] = ["DecompressTracks", "Dispose"],
        ["fmod.dll"] =
        [
            "FMOD5_System_Create",
            "FMOD5_System_GetVersion",
            "FMOD5_System_CreateSound",
            "FMOD5_Sound_Lock",
        ],
    };

    foreach (var (fileName, entryPoints) in exports)
    {
        var path = Path.Combine(publishDirectory, fileName);
        var handle = NativeLibrary.Load(path);
        try
        {
            foreach (var entryPoint in entryPoints)
            {
                Assert(
                    NativeLibrary.TryGetExport(handle, entryPoint, out var address) && address != IntPtr.Zero,
                    $"{fileName} does not export {entryPoint}.");
            }

            if (fileName is "acl.dll" or "sracl.dll" or "acldb.dll" or "acldb_zzz.dll")
            {
                var disposeAddress = NativeLibrary.GetExport(handle, "Dispose");
                var dispose = Marshal.GetDelegateForFunctionPointer<DisposeDelegate>(disposeAddress);
                var clip = new DecompressedClip();
                dispose(ref clip);
            }
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }
}

static void VerifyWindowsCapabilityPath(string publishDirectory)
{
    using var context = CreateLoadContext(publishDirectory);
    var utilityAssembly = context.LoadFromAssemblyPath(Path.Combine(publishDirectory, "AnimeStudio.Utility.dll"));

    var capabilitiesType = RequireType(utilityAssembly, "AnimeStudio.PlatformCapabilities");
    var supportMethod = capabilitiesType.GetMethod(
        "TryGetDirectXShaderDecompilationSupport",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(
            capabilitiesType.FullName,
            "TryGetDirectXShaderDecompilationSupport");

    object?[] supportArguments = [null];
    var supported = (bool)supportMethod.Invoke(null, supportArguments)!;
    Assert(supported, $"DirectX decompilation capability failed: {supportArguments[0]}");

    var decompilerType = RequireType(utilityAssembly, "AnimeStudio.HLSLDecompiler");
    var decompileMethod = decompilerType.GetMethod(
        "DecompileShader",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(decompilerType.FullName, "DecompileShader");

    try
    {
        object?[] decompileArguments = [new byte[] { 0 }, 1, null];
        decompileMethod.Invoke(null, decompileArguments);
    }
    catch (TargetInvocationException exception)
    {
        var cause = exception.InnerException ?? exception;
        Assert(cause is not DllNotFoundException, "HLSL decompiler exposed DllNotFoundException.");
        Assert(cause is not PlatformNotSupportedException, $"HLSL decompiler failed capability probing: {cause.Message}");
        Assert(cause is not BadImageFormatException, $"HLSL decompiler has the wrong architecture: {cause.Message}");
    }
}

static void VerifyUnixDegradationPaths(string publishDirectory)
{
    Assert(
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(),
        "Unix degradation smoke must run on Linux or macOS.");

    using var context = CreateLoadContext(publishDirectory);
    var coreAssembly = context.LoadFromAssemblyPath(Path.Combine(publishDirectory, "AnimeStudio.dll"));
    var utilityAssembly = context.LoadFromAssemblyPath(Path.Combine(publishDirectory, "AnimeStudio.Utility.dll"));

    var dxbc = ExportShaderProgram(
        coreAssembly,
        utilityAssembly,
        "DX11VertexSM50",
        [0, 1, 2, 3]);
    Assert(dxbc.Contains("// hash:", StringComparison.Ordinal), "DXBC degradation output has no hash.");
    Assert(dxbc.Contains("// unsupported:", StringComparison.Ordinal), "DXBC degradation output has no unsupported marker.");
    Assert(
        dxbc.Contains(OperatingSystem.IsLinux() ? "Linux" : "macOS", StringComparison.Ordinal),
        "DXBC degradation output does not identify the current platform.");

    var metal = ExportShaderProgram(
        coreAssembly,
        utilityAssembly,
        "MetalVS",
        [.. "main\0metal-code"u8]);
    Assert(metal.Contains("metal-code", StringComparison.Ordinal), "Metal shader conversion did not preserve its payload.");
    Assert(!metal.Contains("// unsupported:", StringComparison.Ordinal), "Metal shader incorrectly used DirectX degradation.");

    var spirv = ExportShaderProgram(
        coreAssembly,
        utilityAssembly,
        "SPIRV",
        [0, 0, 0, 0]);
    Assert(!spirv.Contains("// unsupported:", StringComparison.Ordinal), "SPIR-V shader incorrectly used DirectX degradation.");

    var aclType = RequireType(utilityAssembly, "ACLLibs.ACL");
    var decompressMethod = aclType.GetMethod(
        "DecompressAll",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMethodException(aclType.FullName, "DecompressAll");

    try
    {
        object?[] aclArguments = [new byte[] { 0 }, null, null];
        decompressMethod.Invoke(null, aclArguments);
        throw new InvalidOperationException("ACL decompression unexpectedly ran on an unsupported platform.");
    }
    catch (TargetInvocationException exception)
    {
        var cause = exception.InnerException ?? exception;
        Assert(cause is PlatformNotSupportedException, $"ACL degradation returned {cause.GetType().Name}.");
        Assert(cause is not DllNotFoundException, "ACL degradation exposed DllNotFoundException.");
    }
}

static string ExportShaderProgram(
    Assembly coreAssembly,
    Assembly utilityAssembly,
    string programTypeName,
    byte[] programCode)
{
    var programType = RequireType(utilityAssembly, "AnimeStudio.ShaderSubProgram");
    var shaderGpuProgramType = RequireType(coreAssembly, "AnimeStudio.ShaderGpuProgramType");
    var program = RuntimeHelpers.GetUninitializedObject(programType);

    RequireField(programType, "m_ProgramType").SetValue(program, Enum.Parse(shaderGpuProgramType, programTypeName));
    RequireField(programType, "m_Keywords").SetValue(program, Array.Empty<string>());
    RequireField(programType, "m_LocalKeywords").SetValue(program, Array.Empty<string>());
    RequireField(programType, "m_ProgramCode").SetValue(program, programCode);

    var exportMethod = programType.GetMethod(
        "Export",
        BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingMethodException(programType.FullName, "Export");
    return (string)exportMethod.Invoke(program, null)!;
}

static SmokeLoadContext CreateLoadContext(string publishDirectory)
{
    return new SmokeLoadContext(Path.Combine(publishDirectory, "AnimeStudio.CLI.dll"));
}

static Type RequireType(Assembly assembly, string name)
{
    return assembly.GetType(name, throwOnError: true)!;
}

static FieldInfo RequireField(Type type, string name)
{
    return type.GetField(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingFieldException(type.FullName, name);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

[StructLayout(LayoutKind.Sequential)]
struct DecompressedClip
{
    public IntPtr Values;
    public int ValuesCount;
    public IntPtr Times;
    public int TimesCount;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void DisposeDelegate(ref DecompressedClip clip);

sealed class SmokeLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly AssemblyDependencyResolver _resolver;

    public SmokeLoadContext(string componentAssemblyPath)
        : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(componentAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    public void Dispose()
    {
        Unload();
    }
}
