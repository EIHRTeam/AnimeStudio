using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: AnimeStudio.CLI.Smoke <publish-directory> <rid>");
    return 2;
}

var publishDirectory = Path.GetFullPath(args[0]);
var rid = args[1];

try
{
    VerifyPackageLayout(publishDirectory, rid);

    if (rid == "win-x64")
    {
        VerifyWindowsNativeLibraries(publishDirectory);
        VerifyWindowsCapabilityPath(publishDirectory);
    }
    else
    {
        VerifyUnixDegradationPaths(publishDirectory);
    }

    Console.WriteLine($"Package smoke checks passed for {rid}.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
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
                "libAnimeStudio.FBXNative.dylib",
                "libAnimeStudio.Ooz.dylib",
                "libTexture2DDecoderNative.dylib",
            ];
            break;
        case "linux-x64":
            expected =
            [
                "libAnimeStudio.FBXNative.so",
                "libAnimeStudio.Ooz.so",
                "libTexture2DDecoderNative.so",
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
            ];
            break;
        case "osx-arm64":
            expected =
            [
                "libAnimeStudio.FBXNative.dylib",
                "libAnimeStudio.Ooz.dylib",
                "libTexture2DDecoderNative.dylib",
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
