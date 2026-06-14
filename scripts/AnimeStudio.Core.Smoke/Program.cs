using System.Runtime.CompilerServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AnimeStudio;
using MessagePack;
using Newtonsoft.Json;

if (args.Length == 2 && args[0] == "--update-asset-map-fixtures")
{
    try
    {
        GenerateAssetMapFixtures(args[1]);
        Console.WriteLine($"AssetMap fixtures updated in {args[1]}.");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        return 1;
    }
}

try
{
    VerifyEndfieldTosData(GameType.ArknightsEndfield);
    VerifyEndfieldTosData(GameType.ArknightsEndfieldCB3);
    VerifyTraditionalTosMap();
    VerifyControllerSizeMismatch();
    VerifyNegativeArrayLength();
    VerifyObjectBoundedArrayLength();
    VerifyObjectBoundedStringLength();
    VerifySharedSlices();
    VerifyBackingHashesAndCleanup();
    VerifyAggregateMemoryBudget();
    VerifyAssetMapEntrySpool();
    VerifyAssetMapCompatibilityFixtures();
    VerifyFailedSliceHandoffCleanup();
    VerifyExceptionalCleanup<OperationCanceledException>();
    VerifyExceptionalCleanup<OutOfMemoryException>();
    VerifyStaleDirectoryCleanup();
    VerifyContainerSlices(typeof(BundleFile));
    VerifyContainerSlices(typeof(MhyFile));
    VerifyContainerSlices(typeof(Blb3File));
    VerifyContainerSlices(typeof(HygFile));
    VerifyContainerSlices(typeof(VFSFile));

    Console.WriteLine("Core parser smoke checks passed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static void VerifyEndfieldTosData(GameType gameType)
{
    var clips = new[]
    {
        (FileId: 2, PathId: 1234567890123456789L),
        (FileId: 3, PathId: -987654321098765432L),
    };
    var data = BuildAnimatorController(
        gameType,
        ["duplicate", "duplicate", "unique"],
        [],
        clips);

    using var reader = CreateObjectReader(data, gameType, ClassIDType.AnimatorController);
    var controller = new AnimatorController(reader);

    Assert(controller.m_TOS.Count == 0, $"{gameType} unexpectedly populated the TOS map.");
    Assert(
        controller.m_TOSData.SequenceEqual(["duplicate", "duplicate", "unique"]),
        $"{gameType} did not preserve TOSData order and duplicates.");
    Assert(controller.m_AnimationClips.Count == clips.Length, $"{gameType} clip count is incorrect.");
    for (var i = 0; i < clips.Length; i++)
    {
        Assert(controller.m_AnimationClips[i].m_FileID == clips[i].FileId, $"{gameType} clip file ID is incorrect.");
        Assert(controller.m_AnimationClips[i].m_PathID == clips[i].PathId, $"{gameType} clip path ID is incorrect.");
    }
}

static void VerifyTraditionalTosMap()
{
    var tos = new[]
    {
        (Key: 11u, Value: "first"),
        (Key: 22u, Value: "second"),
    };
    var data = BuildAnimatorController(GameType.Normal, [], tos, []);

    using var reader = CreateObjectReader(data, GameType.Normal, ClassIDType.AnimatorController);
    var controller = new AnimatorController(reader);

    Assert(controller.m_TOSData.Count == 0, "Traditional controller unexpectedly populated TOSData.");
    Assert(controller.m_TOS.Count == tos.Length, "Traditional TOS map count is incorrect.");
    foreach (var entry in tos)
    {
        Assert(controller.m_TOS[entry.Key] == entry.Value, "Traditional TOS map entry is incorrect.");
    }
}

static void VerifyControllerSizeMismatch()
{
    var data = BuildAnimatorController(
        GameType.ArknightsEndfield,
        [],
        [],
        [],
        controllerSizeAdjustment: -4);

    using var reader = CreateObjectReader(data, GameType.ArknightsEndfield, ClassIDType.AnimatorController);
    var exception = AssertThrows<InvalidDataException>(() => new AnimatorController(reader));
    Assert(exception.Message.Contains("declared 36 bytes, read 40 bytes", StringComparison.Ordinal), "Size mismatch diagnostic is incomplete.");
}

static void VerifyNegativeArrayLength()
{
    using var stream = new MemoryStream(BitConverter.GetBytes(-1));
    using var reader = new EndianBinaryReader(stream, EndianType.LittleEndian);
    AssertThrows<InvalidDataException>(() => reader.ReadInt32Array());
}

static void VerifyObjectBoundedArrayLength()
{
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(3);
        writer.Write(10);
        writer.Write(20);
        writer.Write(30);
    }

    var data = stream.ToArray();
    using var reader = CreateObjectReader(
        data,
        GameType.Normal,
        ClassIDType.UnknownType,
        byteSize: sizeof(int) * 3);
    Assert(reader.Remaining == sizeof(int) * 3, "ObjectReader Remaining is not object-bounded.");
    AssertThrows<EndOfStreamException>(() => reader.ReadInt32Array());
}

static void VerifyObjectBoundedStringLength()
{
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(8);
        writer.Write(Encoding.UTF8.GetBytes("12345678"));
    }

    using var reader = CreateObjectReader(
        stream.ToArray(),
        GameType.Normal,
        ClassIDType.UnknownType,
        byteSize: sizeof(int) + 2);
    AssertThrows<EndOfStreamException>(() => reader.ReadAlignedString());
}

static void VerifySharedSlices()
{
    var data = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
    using var manager = new ContainerStorageManager(new ContainerStorageOptions
    {
        MemoryThresholdBytes = data.Length + 1
    });
    using var store = manager.Create(data.Length, "memory.bundle");
    store.Write(data);
    store.Seal();

    using var first = store.CreateSlice(100, 700);
    using var second = store.CreateSlice(900, 800);

    first.Position = 25;
    second.Position = 35;
    Assert(first.ReadByte() == data[125], "First slice position was not independent.");
    Assert(second.ReadByte() == data[935], "Second slice position was not independent.");
    Assert(first.Position == 26, "Reading the second slice changed the first slice position.");

    first.Seek(-1, SeekOrigin.End);
    Assert(first.ReadByte() == data[799], "End-relative slice seek returned the wrong byte.");
    Assert(first.ReadByte() == -1, "Slice read crossed its upper boundary.");
    AssertThrows<IOException>(() => first.Seek(1, SeekOrigin.End));
    AssertThrows<ArgumentOutOfRangeException>(() => first.Position = -1);
    AssertThrows<NotSupportedException>(() => first.WriteByte(1));
    AssertThrows<EndOfStreamException>(() => store.CreateSlice(data.Length - 10, 11));

    Parallel.For(0, 64, iteration =>
    {
        using var slice = store.CreateSlice(iteration, 512);
        var actual = new byte[512];
        slice.ReadExactly(actual);
        Assert(
            actual.SequenceEqual(data.AsSpan(iteration, 512).ToArray()),
            $"Concurrent slice {iteration} returned incorrect bytes.");
    });

    using var cancelled = store.CreateSlice(0, 1);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    AssertThrows<OperationCanceledException>(
        () => cancelled.ReadAsync(new Memory<byte>(new byte[1]), cancellation.Token)
            .AsTask()
            .GetAwaiter()
            .GetResult());

    store.Dispose();
    AssertThrows<ObjectDisposedException>(() => store.CreateSlice(0, 1));
    second.Position = 0;
    Assert(second.ReadByte() == data[900], "Disposing the owner invalidated a live slice.");
}

static void VerifyBackingHashesAndCleanup()
{
    var root = CreateTemporaryRoot();
    try
    {
        var data = Enumerable.Range(0, 1024 * 1024)
            .Select(index => (byte)(index * 31))
            .ToArray();
        var memoryHash = ReadBackingHash(data, data.Length + 1, root, out var memoryWasFileBacked);
        var diskHash = ReadBackingHash(data, 0, root, out var diskWasFileBacked);

        Assert(!memoryWasFileBacked, "Memory backing unexpectedly used a temporary file.");
        Assert(diskWasFileBacked, "Disk backing did not use a temporary file.");
        Assert(memoryHash.SequenceEqual(diskHash), "Memory and disk backing hashes differ.");
        Assert(!Directory.EnumerateDirectories(root, "run-*").Any(), "A completed backing store left a run directory.");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void VerifyAggregateMemoryBudget()
{
    var root = CreateTemporaryRoot();
    try
    {
        using var manager = new ContainerStorageManager(new ContainerStorageOptions
        {
            MemoryThresholdBytes = 1024,
            TemporaryDirectory = root
        });

        var firstStore = manager.Create(700, "first.bundle");
        firstStore.Write(new byte[700]);
        firstStore.Seal();
        var firstSlice = firstStore.CreateSlice(0, 700);
        firstStore.Dispose();

        using (var secondStore = manager.Create(400, "second.bundle"))
        {
            Assert(secondStore.IsFileBacked,
                "A store exceeding the aggregate memory budget did not use disk.");
            secondStore.Write(new byte[400]);
            secondStore.Seal();
        }

        firstSlice.Dispose();

        using var thirdStore = manager.Create(400, "third.bundle");
        Assert(!thirdStore.IsFileBacked,
            "Released slice memory was not returned to the aggregate budget.");
        thirdStore.Write(new byte[400]);
        thirdStore.Seal();
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void VerifyAssetMapEntrySpool()
{
    var root = CreateTemporaryRoot();
    try
    {
        string workspaceDirectory;
        using (var spool = new AssetMapEntrySpool(new ContainerStorageOptions
        {
            TemporaryDirectory = root
        }))
        {
            workspaceDirectory = spool.WorkspaceDirectory;
            Assert(
                Path.GetFullPath(workspaceDirectory).StartsWith(
                    Path.GetFullPath(root),
                    StringComparison.Ordinal),
                "AssetMap spool was not created under the configured temporary root.");
            Assert(File.Exists(spool.TemporaryPath), "AssetMap spool file was not created.");
            Assert(
                File.Exists(Path.Combine(workspaceDirectory, ".lock")),
                "AssetMap spool workspace lock was not created.");
            AssertThrows<InvalidOperationException>(() => spool.ReadEntries());

            var first = new AssetEntry
            {
                Name = "First",
                Container = "characters/first",
                Source = "input/001.assets",
                PathID = 101,
                Type = ClassIDType.Texture2D,
                Hash = "001122",
                Offset = 17
            };
            var second = new AssetEntry
            {
                Name = null,
                Container = "",
                Source = "input/002.assets",
                PathID = -202,
                Type = ClassIDType.AnimatorController,
                Hash = null,
                Offset = -1
            };
            spool.Append(first);
            spool.Append(second);

            const int syntheticCount = 2048;
            for (var index = 0; index < syntheticCount; index++)
            {
                spool.Append(new AssetEntry
                {
                    Name = $"Synthetic-{index % 8}",
                    Container = $"container/{index % 4}",
                    Source = $"input/{index % 16}.assets",
                    PathID = index,
                    Type = ClassIDType.TextAsset,
                    Hash = "repeated-hash",
                    Offset = index * 4L
                });
            }

            Assert(
                spool.Count == syntheticCount + 2,
                "AssetMap spool record count is incorrect.");
            spool.Seal();
            AssertThrows<InvalidOperationException>(() => spool.Append(first));

            var firstPass = ReadSpoolSummary(spool, first, second);
            var secondPass = ReadSpoolSummary(spool, first, second);
            Assert(firstPass == secondPass, "Repeated AssetMap spool enumeration changed results.");
            Assert(
                firstPass.Count == syntheticCount + 2,
                "AssetMap spool enumeration returned the wrong record count.");

            using (var append = new FileStream(
                spool.TemporaryPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read))
            {
                append.WriteByte(0x7f);
            }
            AssertThrows<InvalidDataException>(() => Consume(spool.ReadEntries()));
        }

        Assert(
            !Directory.EnumerateDirectories(root, "run-*").Any(),
            "Disposed AssetMap spool left a temporary run directory.");
    }
    finally
    {
        StringCache.Clear();
        Directory.Delete(root, true);
    }
}

static void VerifyAssetMapCompatibilityFixtures()
{
    var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    var fixtureBase = Path.Combine(fixtureDirectory, "legacy-asset-map");
    var manifestPath = Path.Combine(fixtureDirectory, "legacy-asset-map.sha256");
    Assert(File.Exists(manifestPath), "AssetMap fixture hash manifest is missing.");

    foreach (var line in File.ReadLines(manifestPath))
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var separator = line.IndexOf("  ", StringComparison.Ordinal);
        Assert(separator > 0, $"Invalid AssetMap fixture hash line: {line}");
        var expectedHash = line[..separator];
        var fileName = line[(separator + 2)..];
        var path = Path.Combine(fixtureDirectory, fileName);
        Assert(File.Exists(path), $"AssetMap fixture is missing: {fileName}");
        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            .ToLowerInvariant();
        Assert(
            actualHash == expectedHash,
            $"AssetMap fixture hash changed for {fileName}: {actualHash}.");
    }

    var expectedEntries = CreateAssetMapFixtureEntries();
    using (var stream = File.OpenRead($"{fixtureBase}.map"))
    {
        var map = MessagePackSerializer.Deserialize<AssetMap>(
            stream,
            MessagePackSerializerOptions.Standard.WithCompression(
                MessagePackCompression.Lz4BlockArray));
        Assert(map.GameType == GameType.ArknightsEndfield, "MessagePack fixture game changed.");
        AssertAssetEntryListsEqual(expectedEntries, map.AssetEntries, "MessagePack fixture");
    }

    var json = JsonConvert.DeserializeObject<AssetMap>(
        File.ReadAllText($"{fixtureBase}.json"))
        ?? throw new InvalidDataException("JSON fixture did not deserialize.");
    Assert(json.GameType == GameType.ArknightsEndfield, "JSON fixture game changed.");
    AssertAssetEntryListsEqual(expectedEntries, json.AssetEntries, "JSON fixture");

    var temporaryDirectory = CreateTemporaryRoot();
    try
    {
        GenerateAssetMapFixtures(temporaryDirectory);
        Assert(
            File.ReadAllBytes($"{fixtureBase}.json")
                .SequenceEqual(File.ReadAllBytes(
                    Path.Combine(temporaryDirectory, "legacy-asset-map.json"))),
            "Current JSON AssetMap writer differs from the legacy fixture.");
        Assert(
            File.ReadAllBytes($"{fixtureBase}.map")
                .SequenceEqual(File.ReadAllBytes(
                    Path.Combine(temporaryDirectory, "legacy-asset-map.map"))),
            "Current MessagePack AssetMap writer differs from the legacy fixture.");

        var expectedXml = XDocument.Load($"{fixtureBase}.xml");
        var actualXml = XDocument.Load(
            Path.Combine(temporaryDirectory, "legacy-asset-map.xml"));
        NormalizeAssetMapXml(expectedXml);
        NormalizeAssetMapXml(actualXml);
        Assert(
            XNode.DeepEquals(expectedXml, actualXml),
            "Current XML AssetMap writer differs from the normalized legacy fixture.");
    }
    finally
    {
        StringCache.Clear();
        Directory.Delete(temporaryDirectory, true);
    }
}

static void GenerateAssetMapFixtures(string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    var entries = CreateAssetMapFixtureEntries();
    AssetsHelper.ExportAssetsMap(
            entries,
            new Game(GameType.ArknightsEndfield, "Arknights: Endfield"),
            "legacy-asset-map",
            outputDirectory,
            ExportListType.XML | ExportListType.JSON | ExportListType.MessagePack)
        .GetAwaiter()
        .GetResult();
    StringCache.Clear();
}

static List<AssetEntry> CreateAssetMapFixtureEntries()
{
    return
    [
        new AssetEntry
        {
            Name = "Texture_A",
            Container = "characters/alpha/texture_a",
            Source = "input/1001.assets",
            PathID = 123456789,
            Type = ClassIDType.Texture2D,
            Hash = "00112233445566778899aabbccddeeff",
            Offset = 4096
        },
        new AssetEntry
        {
            Name = "Controller_B",
            Container = "",
            Source = "input/1002.assets",
            PathID = -987654321,
            Type = ClassIDType.AnimatorController,
            Hash = null,
            Offset = -1
        }
    ];
}

static void AssertAssetEntryListsEqual(
    IReadOnlyList<AssetEntry> expected,
    IReadOnlyList<AssetEntry>? actual,
    string label)
{
    if (actual == null)
    {
        throw new InvalidDataException($"{label} entries are missing.");
    }

    Assert(expected.Count == actual.Count, $"{label} entry count changed.");
    for (var index = 0; index < expected.Count; index++)
    {
        AssertAssetEntriesEqual(expected[index], actual[index], $"{label} entry {index}");
    }
}

static void NormalizeAssetMapXml(XDocument document)
{
    var root = document.Root
        ?? throw new InvalidDataException("AssetMap XML fixture has no root element.");
    root.SetAttributeValue("filename", "<normalized>");
    root.SetAttributeValue("createdAt", "<normalized>");
}

static (long Count, long PathIdSum) ReadSpoolSummary(
    AssetMapEntrySpool spool,
    AssetEntry expectedFirst,
    AssetEntry expectedSecond)
{
    long count = 0;
    long pathIdSum = 0;
    foreach (var entry in spool.ReadEntries())
    {
        if (count == 0)
        {
            AssertAssetEntriesEqual(expectedFirst, entry, "first");
        }
        else if (count == 1)
        {
            AssertAssetEntriesEqual(expectedSecond, entry, "second");
        }

        count++;
        pathIdSum = checked(pathIdSum + entry.PathID);
    }

    return (count, pathIdSum);
}

static void AssertAssetEntriesEqual(AssetEntry expected, AssetEntry actual, string label)
{
    Assert(expected.Name == actual.Name, $"AssetMap spool {label} name changed.");
    Assert(expected.Container == actual.Container, $"AssetMap spool {label} container changed.");
    Assert(expected.Source == actual.Source, $"AssetMap spool {label} source changed.");
    Assert(expected.PathID == actual.PathID, $"AssetMap spool {label} PathID changed.");
    Assert(expected.Type == actual.Type, $"AssetMap spool {label} type changed.");
    Assert(expected.Hash == actual.Hash, $"AssetMap spool {label} hash changed.");
    Assert(expected.Offset == actual.Offset, $"AssetMap spool {label} offset changed.");
}

static void Consume(IEnumerable<AssetEntry> entries)
{
    foreach (var _ in entries)
    {
    }
}

static byte[] ReadBackingHash(
    byte[] data,
    long threshold,
    string root,
    out bool wasFileBacked)
{
    var manager = new ContainerStorageManager(new ContainerStorageOptions
    {
        MemoryThresholdBytes = threshold,
        TemporaryDirectory = root
    });
    var store = manager.Create(data.Length, "hash.bundle");
    store.Write(data);
    store.Seal();
    var slice = store.CreateSlice(0, data.Length);
    wasFileBacked = store.IsFileBacked;
    var temporaryPath = store.TemporaryPath;

    manager.Dispose();
    store.Dispose();
    if (temporaryPath != null)
    {
        Assert(File.Exists(temporaryPath), "Backing file was deleted while a slice still owned it.");
    }

    var hash = SHA256.HashData(slice);
    slice.Dispose();
    if (temporaryPath != null)
    {
        Assert(!File.Exists(temporaryPath), "Backing file remained after the final slice closed.");
    }

    return hash;
}

static void VerifyFailedSliceHandoffCleanup()
{
    var root = CreateTemporaryRoot();
    try
    {
        using var manager = new ContainerStorageManager(new ContainerStorageOptions
        {
            MemoryThresholdBytes = 0,
            TemporaryDirectory = root
        });
        using var store = manager.Create(32, "failure.bundle");
        store.Write(Enumerable.Range(0, 32).Select(index => (byte)index).ToArray());

        var directory = new[]
        {
            new BundleFile.Node { path = "first", offset = 0, size = 16 },
            new BundleFile.Node { path = "invalid", offset = 24, size = 16 }
        };
        AssertThrows<EndOfStreamException>(() => ContainerFileStreams.Create(store, directory));
        store.Dispose();
        manager.Dispose();

        Assert(!Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories).Any(),
            "Failed slice handoff left a backing file.");
        Assert(!Directory.EnumerateDirectories(root, "run-*").Any(),
            "Failed slice handoff left a run directory.");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void VerifyExceptionalCleanup<TException>()
    where TException : Exception, new()
{
    var root = CreateTemporaryRoot();
    try
    {
        try
        {
            using var manager = new ContainerStorageManager(new ContainerStorageOptions
            {
                MemoryThresholdBytes = 0,
                TemporaryDirectory = root
            });
            using var store = manager.Create(32, $"{typeof(TException).Name}.bundle");
            store.Write(new byte[32]);
            throw new TException();
        }
        catch (TException)
        {
        }

        Assert(!Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories).Any(),
            $"{typeof(TException).Name} cleanup left a backing file.");
        Assert(!Directory.EnumerateDirectories(root, "run-*").Any(),
            $"{typeof(TException).Name} cleanup left a run directory.");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void VerifyStaleDirectoryCleanup()
{
    var root = CreateTemporaryRoot();
    var staleDirectory = Path.Combine(root, "run-stale");
    var activeDirectory = Path.Combine(root, "run-active");
    Directory.CreateDirectory(staleDirectory);
    Directory.CreateDirectory(activeDirectory);
    File.WriteAllText(Path.Combine(staleDirectory, ".lock"), "stale");
    var activeLockPath = Path.Combine(activeDirectory, ".lock");
    File.WriteAllText(activeLockPath, "active");
    Directory.SetLastWriteTimeUtc(staleDirectory, DateTime.UtcNow.AddDays(-8));
    Directory.SetLastWriteTimeUtc(activeDirectory, DateTime.UtcNow.AddDays(-8));

    using var activeLock = new FileStream(
        activeLockPath,
        FileMode.Open,
        FileAccess.ReadWrite,
        FileShare.Read);
    try
    {
        using var manager = new ContainerStorageManager(new ContainerStorageOptions
        {
            MemoryThresholdBytes = 0,
            TemporaryDirectory = root
        });
        using var store = manager.Create(16, "cleanup.bundle");
        store.Write(new byte[16]);
        manager.Dispose();
        store.Dispose();

        Assert(!Directory.Exists(staleDirectory), "Unlocked stale run directory was not removed.");
        Assert(Directory.Exists(activeDirectory), "A locked stale run directory was removed.");
    }
    finally
    {
        activeLock.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}

static void VerifyContainerSlices(Type containerType)
{
    var data = Enumerable.Range(0, 2048).Select(index => (byte)(index % 239)).ToArray();
    using var manager = new ContainerStorageManager(new ContainerStorageOptions
    {
        MemoryThresholdBytes = data.Length + 1
    });
    using var store = manager.Create(data.Length, $"{containerType.Name}.bundle");
    store.Write(data);

    var nodes = new List<BundleFile.Node>
    {
        new() { path = "CAB-first", offset = 17, size = 333 },
        new() { path = "archive/resource.resS", offset = 777, size = 512 }
    };
    var instance = RuntimeHelpers.GetUninitializedObject(containerType);
    var directoryField = containerType.GetField(
        "m_DirectoryInfo",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(containerType.FullName, "m_DirectoryInfo");
    directoryField.SetValue(instance, nodes);

    var readFilesMethod = containerType.GetMethod(
        "ReadFiles",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(containerType.FullName, "ReadFiles");
    readFilesMethod.Invoke(instance, [store]);

    var filesField = containerType.GetField(
        "fileList",
        BindingFlags.Instance | BindingFlags.Public)
        ?? throw new MissingFieldException(containerType.FullName, "fileList");
    var files = (List<StreamFile>)(filesField.GetValue(instance)
        ?? throw new InvalidOperationException($"{containerType.Name} did not create files."));

    try
    {
        Assert(files.Count == nodes.Count, $"{containerType.Name} returned the wrong file count.");
        for (var index = 0; index < files.Count; index++)
        {
            Assert(files[index].stream is ReadOnlySliceStream,
                $"{containerType.Name} copied an entry instead of returning a bounded slice.");
            var actualHash = SHA256.HashData(files[index].stream);
            var expectedHash = SHA256.HashData(
                data.AsSpan((int)nodes[index].offset, (int)nodes[index].size));
            Assert(actualHash.SequenceEqual(expectedHash),
                $"{containerType.Name} entry hash differs from the source range.");
        }
    }
    finally
    {
        foreach (var file in files)
        {
            file.stream.Dispose();
        }
    }
}

static string CreateTemporaryRoot()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        $"animestudio-core-smoke-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    return root;
}

static byte[] BuildAnimatorController(
    GameType gameType,
    IReadOnlyList<string> tosData,
    IReadOnlyList<(uint Key, string Value)> tosMap,
    IReadOnlyList<(int FileId, long PathId)> clips,
    int controllerSizeAdjustment = 0)
{
    var isEndfield = gameType is GameType.ArknightsEndfield or GameType.ArknightsEndfieldCB3;
    using var controllerStream = new MemoryStream();
    using (var controllerWriter = new BinaryWriter(controllerStream, Encoding.UTF8, leaveOpen: true))
    {
        controllerWriter.Write(0); // m_LayerArray
        controllerWriter.Write(0); // m_StateMachineArray
        controllerWriter.Write(0); // m_Values
        controllerWriter.Write(0); // m_PositionValues
        controllerWriter.Write(0); // m_QuaternionValues
        controllerWriter.Write(0); // m_ScaleValues
        controllerWriter.Write(0); // m_FloatValues
        controllerWriter.Write(0); // m_IntValues
        controllerWriter.Write(0); // m_BoolValues
        if (isEndfield)
        {
            controllerWriter.Write(0); // m_ClothCalculatorType
        }
    }

    using var objectStream = new MemoryStream();
    using (var writer = new BinaryWriter(objectStream, Encoding.UTF8, leaveOpen: true))
    {
        WriteAlignedString(writer, "SyntheticController");
        writer.Write(checked((uint)(controllerStream.Length + controllerSizeAdjustment)));
        writer.Write(controllerStream.ToArray());

        if (isEndfield)
        {
            writer.Write(tosData.Count);
            foreach (var value in tosData)
            {
                WriteAlignedString(writer, value);
            }
            writer.Write(0); // m_AnimationCurveMask
        }
        else
        {
            writer.Write(tosMap.Count);
            foreach (var (key, value) in tosMap)
            {
                writer.Write(key);
                WriteAlignedString(writer, value);
            }
        }

        writer.Write(clips.Count);
        foreach (var (fileId, pathId) in clips)
        {
            writer.Write(fileId);
            writer.Write(pathId);
        }
    }

    return objectStream.ToArray();
}

static void WriteAlignedString(BinaryWriter writer, string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    writer.Write(bytes.Length);
    writer.Write(bytes);
    while (writer.BaseStream.Position % 4 != 0)
    {
        writer.Write((byte)0);
    }
}

static ObjectReader CreateObjectReader(
    byte[] data,
    GameType gameType,
    ClassIDType classId,
    uint? byteSize = null)
{
    var stream = new MemoryStream(data);
    var sourceReader = new EndianBinaryReader(stream, EndianType.LittleEndian, leaveOpen: true);
    var assetsFile = (SerializedFile)RuntimeHelpers.GetUninitializedObject(typeof(SerializedFile));
    assetsFile.version = [2022, 3, 0, 0];
    assetsFile.buildType = new BuildType("f");
    assetsFile.m_TargetPlatform = BuildTarget.StandaloneWindows64;
    assetsFile.header = new SerializedFileHeader
    {
        m_Version = SerializedFileFormatVersion.LargeFilesSupport,
    };
    assetsFile.fileName = "synthetic.assets";
    assetsFile.m_Externals = [];
    assetsFile.ObjectsDic = [];

    var objectInfo = new ObjectInfo
    {
        byteStart = 0,
        byteSize = byteSize ?? checked((uint)data.Length),
        classID = (int)classId,
        m_PathID = 42,
        serializedType = new SerializedType
        {
            classID = (int)classId,
            m_OldTypeHash = new byte[16],
        },
    };

    return new ObjectReader(
        sourceReader,
        assetsFile,
        objectInfo,
        new Game(gameType, gameType.ToString()));
}

static TException AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
