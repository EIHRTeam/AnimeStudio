using System.Runtime.CompilerServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using AnimeStudio;
using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
    VerifyConcurrentDiskSlices();
    VerifyContainerBlockPipeline();
    VerifyBlockFileRangeDiscovery();
    VerifyConcurrentResourceReaders();
    VerifyConcurrentPPtrFileCache();
    VerifyBoundedParallelWorkerSlots();
    VerifyMultiWorkerObjectParsing();
    VerifyBatchedAssetMapObjectScanning();
    VerifyBackingHashesAndCleanup();
    VerifyAggregateMemoryBudget();
    VerifyAssetMapEntrySpool();
    VerifyAssetMapStringCache();
    VerifyAssetMapBuildMetrics();
    VerifySilentStateRestoredAfterLoadFailure();
    VerifyAssetMapCompatibilityFixtures();
    VerifyAssetMapStreamingBoundaryCompatibility();
    VerifyAssetMapMessagePackPoolBoundaryCompatibility();
    VerifyAssetMapStreamingReaders();
    VerifySyntheticLargeAssetMapStreaming();
    VerifyAssetMapStreamingExceptionalCleanup();
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

static void VerifyConcurrentDiskSlices()
{
    var root = CreateTemporaryRoot();
    try
    {
        var data = Enumerable.Range(0, 2 * 1024 * 1024)
            .Select(index => (byte)(index * 17))
            .ToArray();
        using var manager = new ContainerStorageManager(
            new ContainerStorageOptions
            {
                MemoryThresholdBytes = 0,
                TemporaryDirectory = root
            });
        using var store = manager.Create(data.Length, "parallel.bundle");
        store.PrepareForPositionedWrites();
        const int chunkCount = 256;
        var chunkLength = data.Length / chunkCount;
        Parallel.For(0, chunkCount, iteration =>
        {
            var chunkIndex = iteration * 73 % chunkCount;
            var offset = chunkIndex * chunkLength;
            store.WriteAt(
                offset,
                data.AsSpan(offset, chunkLength));
        });
        store.Seal();

        Parallel.For(0, 256, iteration =>
        {
            var offset = (iteration * 7919) % (data.Length - 4096);
            using var slice = store.CreateSlice(offset, 4096);
            var actual = new byte[4096];
            slice.ReadExactly(actual);
            Assert(
                actual.AsSpan().SequenceEqual(data.AsSpan(offset, 4096)),
                $"Concurrent disk slice {iteration} returned incorrect bytes.");
        });
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void VerifyContainerBlockPipeline()
{
    const int blockCount = 32;
    const int blockLength = 64 * 1024;
    var data = new byte[blockCount * blockLength];
    for (var index = 0; index < data.Length; index++)
    {
        data[index] = (byte)(index * 19);
    }

    var blocks = Enumerable.Range(0, blockCount)
        .Select(_ => new BundleFile.StorageBlock
        {
            compressedSize = blockLength,
            uncompressedSize = blockLength,
            flags = 0
        })
        .ToList();
    using var source = new MemoryStream(data, writable: false);
    using var reader = new EndianBinaryReader(
        source,
        EndianType.LittleEndian);
    using var manager = new ContainerStorageManager(
        new ContainerStorageOptions
        {
            MemoryThresholdBytes = data.Length + 1
        });
    using var store = manager.Create(data.Length, "pipeline.bundle");
    var workerThreads = new HashSet<int>();

    ContainerBlockPipeline.Process(
        reader,
        blocks,
        store,
        requestedWorkers: 4,
        CancellationToken.None,
        (
            blockIndex,
            block,
            compressedBuffer,
            compressedLength,
            uncompressedBuffer,
            uncompressedLength) =>
        {
            var compressed = compressedBuffer.AsSpan(0, compressedLength);
            var uncompressed = uncompressedBuffer.AsSpan(
                0,
                uncompressedLength);
            lock (workerThreads)
            {
                workerThreads.Add(Environment.CurrentManagedThreadId);
            }
            Thread.Sleep(2);
            compressed.CopyTo(uncompressed);
            return uncompressed.Length;
        });
    store.Seal();

    using var slice = store.CreateSlice(0, data.Length);
    var actual = new byte[data.Length];
    slice.ReadExactly(actual);
    Assert(
        actual.AsSpan().SequenceEqual(data),
        "Container block pipeline changed block order or bytes.");
    Assert(
        workerThreads.Count > 1,
        "Container block pipeline did not use multiple decode workers.");

    VerifySingleWorkerContainerPipeline(data, blocks);
    VerifyContainerPipelineCancellation(data, blocks);
    VerifyContainerPipelineFailureCleanup(data, blocks);
}

static void VerifyBlockFileRangeDiscovery()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "animestudio-block-ranges-"
        + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "ranges.chk");
    var expectedLengths = new long[] { 256, 384, 512, 640 };
    try
    {
        using (var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        {
            Span<byte> number = stackalloc byte[8];
            foreach (var length in expectedLengths)
            {
                var header = new MemoryStream();
                header.Write(Encoding.ASCII.GetBytes("UnityFS"));
                header.WriteByte(0);
                BinaryPrimitives.WriteUInt32BigEndian(number, 6);
                header.Write(number[..4]);
                header.Write(Encoding.ASCII.GetBytes("5.x.x"));
                header.WriteByte(0);
                header.Write(Encoding.ASCII.GetBytes("2021.3.3f5"));
                header.WriteByte(0);
                BinaryPrimitives.WriteInt64BigEndian(number, length);
                header.Write(number);
                Assert(
                    header.Length < length,
                    "Synthetic UnityFS header exceeds its range.");
                header.Position = 0;
                header.CopyTo(stream);
                stream.Write(new byte[length - header.Length]);
            }
        }

        using var reader = new FileReader(path);
        Assert(
            BlockFileRangeDiscovery.TryDiscover(
                reader,
                new Game(GameType.Normal, "range-smoke"),
                requestedOffsets: null,
                out var ranges),
            "Block-file range discovery rejected valid UnityFS ranges.");
        Assert(
            ranges.Count == expectedLengths.Length,
            "Block-file range discovery returned the wrong range count.");
        Assert(
            !BlockFileRangeDiscovery.TryDiscover(
                reader,
                new Game(GameType.UnityCN, "encrypted-range-smoke"),
                requestedOffsets: null,
                out _),
            "Encrypted UnityFS layout did not fall back to sequential load.");

        long expectedOffset = 0;
        for (var index = 0; index < ranges.Count; index++)
        {
            Assert(
                ranges[index].Offset == expectedOffset,
                "Block-file range discovery changed source order.");
            Assert(
                ranges[index].Length == expectedLengths[index],
                "Block-file range discovery returned the wrong length.");
            using var view = BlockFileRangeDiscovery.CreateView(
                reader.BaseStream,
                ranges[index].Offset,
                ranges[index].Length);
            Assert(
                view.Length == expectedLengths[index],
                "Independent block-file view has the wrong length.");
            expectedOffset += expectedLengths[index];
        }
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void VerifyConcurrentPPtrFileCache()
{
    var manager = new AssetsManager();
    var source = (SerializedFile)RuntimeHelpers.GetUninitializedObject(
        typeof(SerializedFile));
    var target = (SerializedFile)RuntimeHelpers.GetUninitializedObject(
        typeof(SerializedFile));
    source.assetsManager = manager;
    source.m_Externals =
    [
        new FileIdentifier
        {
            fileName = "CAB-concurrent-target"
        }
    ];
    target.fileName = "CAB-concurrent-target";
    target.ObjectsDic = new Dictionary<long, AnimeStudio.Object>();
    manager.assetsFileList.Add(target);

    Parallel.For(0, 1024, iteration =>
    {
        var pointer = new PPtr<AnimeStudio.Object>(
            1,
            42,
            source);
        pointer.TryGet(out AnimeStudio.Object ignored);
    });

    Assert(
        manager.assetsFileIndexCache.Count == 1,
        "Concurrent PPtr resolution inserted duplicate file-cache keys.");
    Assert(
        manager.assetsFileIndexCache.TryGetValue(
            "CAB-concurrent-target",
            out var index)
        && index == 0,
        "Concurrent PPtr resolution cached the wrong file index.");
}

static void VerifySingleWorkerContainerPipeline(
    byte[] data,
    IReadOnlyList<BundleFile.StorageBlock> blocks)
{
    using var source = new MemoryStream(data, writable: false);
    using var reader = new EndianBinaryReader(
        source,
        EndianType.LittleEndian);
    using var manager = new ContainerStorageManager(
        new ContainerStorageOptions
        {
            MemoryThresholdBytes = data.Length + 1
        });
    using var store = manager.Create(data.Length, "serial-pipeline.bundle");
    var decoderThreads = new HashSet<int>();

    ContainerBlockPipeline.Process(
        reader,
        blocks,
        store,
        requestedWorkers: 1,
        CancellationToken.None,
        (
            blockIndex,
            block,
            compressedBuffer,
            compressedLength,
            uncompressedBuffer,
            uncompressedLength) =>
        {
            decoderThreads.Add(Environment.CurrentManagedThreadId);
            compressedBuffer.AsSpan(0, compressedLength)
                .CopyTo(uncompressedBuffer.AsSpan(0, uncompressedLength));
            return uncompressedLength;
        });
    store.Seal();

    using var slice = store.CreateSlice(0, data.Length);
    var actual = new byte[data.Length];
    slice.ReadExactly(actual);
    Assert(
        actual.AsSpan().SequenceEqual(data),
        "Single-worker container pipeline changed bytes.");
    Assert(
        decoderThreads.Count == 1,
        "Single-worker container pipeline used multiple decoder threads.");
}

static void VerifyContainerPipelineCancellation(
    byte[] data,
    IReadOnlyList<BundleFile.StorageBlock> blocks)
{
    using var source = new MemoryStream(data, writable: false);
    using var reader = new EndianBinaryReader(
        source,
        EndianType.LittleEndian);
    using var manager = new ContainerStorageManager(
        new ContainerStorageOptions
        {
            MemoryThresholdBytes = data.Length + 1
        });
    using var store = manager.Create(
        data.Length,
        "cancelled-pipeline.bundle");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    AssertThrows<OperationCanceledException>(
        () => ContainerBlockPipeline.Process(
            reader,
            blocks,
            store,
            requestedWorkers: 4,
            cancellation.Token,
            (
                blockIndex,
                block,
                compressedBuffer,
                compressedLength,
                uncompressedBuffer,
                uncompressedLength) => uncompressedLength));
    Assert(
        store.Length == 0,
        "Pre-cancelled container pipeline modified its destination.");
}

static void VerifyContainerPipelineFailureCleanup(
    byte[] data,
    IReadOnlyList<BundleFile.StorageBlock> blocks)
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "animestudio-container-pipeline-failure-"
        + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        using var source = new MemoryStream(data, writable: false);
        using var reader = new EndianBinaryReader(
            source,
            EndianType.LittleEndian);
        using (var manager = new ContainerStorageManager(
            new ContainerStorageOptions
            {
                MemoryThresholdBytes = 1,
                TemporaryDirectory = root
            }))
        using (var store = manager.Create(
            data.Length,
            "failed-pipeline.bundle"))
        {
            AssertThrows<InvalidDataException>(
                () => ContainerBlockPipeline.Process(
                    reader,
                    blocks,
                    store,
                    requestedWorkers: 4,
                    CancellationToken.None,
                    (
                        blockIndex,
                        block,
                        compressedBuffer,
                        compressedLength,
                        uncompressedBuffer,
                        uncompressedLength) =>
                    {
                        if (blockIndex == 5)
                        {
                            throw new InvalidDataException(
                                "Synthetic decoder failure.");
                        }

                        compressedBuffer.AsSpan(0, compressedLength)
                            .CopyTo(
                                uncompressedBuffer.AsSpan(
                                    0,
                                    uncompressedLength));
                        return uncompressedLength;
                    }));
        }

        Assert(
            !Directory.EnumerateFiles(
                root,
                "*",
                SearchOption.AllDirectories).Any(),
            "Failed container pipeline left temporary files behind.");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void VerifyConcurrentResourceReaders()
{
    var data = Enumerable.Range(0, 1024 * 1024)
        .Select(index => (byte)(index * 29))
        .ToArray();
    using var stream = new MemoryStream(data, writable: false);
    using var reader = new BinaryReader(stream);

    Parallel.For(0, 256, iteration =>
    {
        var offset = (iteration * 3571) % (data.Length - 2048);
        var resourceReader = new ResourceReader(reader, offset, 2048);
        var actual = resourceReader.GetData();
        Assert(
            actual.AsSpan().SequenceEqual(data.AsSpan(offset, 2048)),
            $"Concurrent resource reader {iteration} returned incorrect bytes.");
    });
}

static void VerifyMultiWorkerObjectParsing()
{
    const int objectCount = 16;
    const int payloadSize = 512 * 1024;
    const int objectSize = sizeof(int) + sizeof(int) + payloadSize;
    var sourcePath = Path.Combine(
        Path.GetTempPath(),
        $"parallel-object-{Guid.NewGuid():N}.assets");
    var manager = new AssetsManager
    {
        Game = new Game(GameType.Normal, "parallel-smoke"),
        WorkerCount = 4,
    };
    var previousProgress = Progress.Default;
    var previousProgressSilent = Progress.Silent;
    var progress = new ThreadRecordingProgress();

    try
    {
        var data = new byte[objectCount * objectSize];
        for (var index = 0; index < objectCount; index++)
        {
            var objectOffset = index * objectSize;
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(objectOffset, sizeof(int)),
                0);
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(
                    objectOffset + sizeof(int),
                    sizeof(int)),
                payloadSize);
        }
        File.WriteAllBytes(sourcePath, data);
        var reader = new FileReader(
            sourcePath,
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.RandomAccess));
        reader.Endian = EndianType.LittleEndian;
        reader.Position = 0;

        var assetsFile = (SerializedFile)RuntimeHelpers
            .GetUninitializedObject(typeof(SerializedFile));
        assetsFile.assetsManager = manager;
        assetsFile.reader = reader;
        assetsFile.game = manager.Game;
        assetsFile.fullName = reader.FullPath;
        assetsFile.originalPath = reader.FullPath;
        assetsFile.fileName = reader.FileName;
        assetsFile.version = [2022, 3, 0, 0];
        assetsFile.buildType = new BuildType("f");
        assetsFile.m_TargetPlatform = BuildTarget.StandaloneWindows64;
        assetsFile.header = new SerializedFileHeader
        {
            m_Version = SerializedFileFormatVersion.LargeFilesSupport,
        };
        assetsFile.m_Externals = [];
        assetsFile.Objects = [];
        assetsFile.ObjectsDic = [];
        assetsFile.m_Objects = Enumerable.Range(0, objectCount)
            .Select(index => new ObjectInfo
            {
                byteStart = index * objectSize,
                byteSize = objectSize,
                classID = (int)ClassIDType.TextAsset,
                m_PathID = index + 1,
            })
            .ToList();
        manager.assetsFileList.Add(assetsFile);

        ThreadPool.GetMinThreads(
            out var minimumWorkerThreads,
            out var minimumCompletionPortThreads);
        if (minimumWorkerThreads < manager.WorkerCount)
        {
            ThreadPool.SetMinThreads(
                manager.WorkerCount,
                minimumCompletionPortThreads);
        }

        Progress.Default = progress;
        Progress.Silent = false;
        var readAssets = typeof(AssetsManager).GetMethod(
            "ReadAssets",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(
                typeof(AssetsManager).FullName,
                "ReadAssets");
        try
        {
            readAssets.Invoke(manager, null);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                "Multi-worker object parsing failed.",
                exception.InnerException ?? exception);
        }

        Assert(
            progress.WorkerThreadCount > 1,
            "One serialized file did not use more than one object worker.");
        Assert(
            assetsFile.Objects.Count == objectCount,
            "Multi-worker parsing lost or duplicated objects.");
        Assert(
            assetsFile.Objects
                .Select(obj => obj.m_PathID)
                .SequenceEqual(
                    Enumerable.Range(1, objectCount)
                        .Select(index => (long)index)),
            "Multi-worker parsing did not preserve object order.");
        Assert(
            assetsFile.ObjectsDic.Count == objectCount,
            "Multi-worker parsing produced duplicate object IDs.");
    }
    finally
    {
        Progress.Default = previousProgress;
        Progress.Silent = previousProgressSilent;
        manager.Clear();
        File.Delete(sourcePath);
    }
}

static void VerifyBoundedParallelWorkerSlots()
{
    const int workerCount = 4;
    const int iterationCount = 128;
    var ownerThreads = Enumerable.Repeat(-1, workerCount).ToArray();
    var processedByWorker = new int[workerCount];

    BoundedParallel.For(
        0,
        iterationCount,
        workerCount,
        CancellationToken.None,
        (workerIndex, _) =>
        {
            Assert(
                workerIndex >= 0 && workerIndex < workerCount,
                "Bounded parallel returned an invalid worker slot.");
            var threadId = Environment.CurrentManagedThreadId;
            var previousThread = Interlocked.CompareExchange(
                ref ownerThreads[workerIndex],
                threadId,
                -1);
            Assert(
                previousThread == -1 || previousThread == threadId,
                "A bounded parallel worker slot moved between threads.");
            Interlocked.Increment(ref processedByWorker[workerIndex]);
            Thread.SpinWait(10_000);
        });

    Assert(
        ownerThreads.Distinct().Count() == workerCount,
        "Bounded parallel worker slots did not use distinct threads.");
    Assert(
        processedByWorker.All(count => count > 0),
        "A bounded parallel worker slot did not process any work.");
}

static void VerifyBatchedAssetMapObjectScanning()
{
    var objectCount = AssetsHelper.AssetMapObjectBatchSize + 1;
    const int objectSize = sizeof(int);
    var root = CreateTemporaryRoot();
    var sourcePath = Path.Combine(root, "batched-asset-map.assets");
    var manager = new AssetsManager
    {
        Game = new Game(GameType.Normal, "batched-asset-map-smoke"),
        WorkerCount = 4,
        Silent = true,
        SkipProcess = true,
        ResolveDependencies = false,
        ContainerStorageOptions = new ContainerStorageOptions
        {
            TemporaryDirectory = root
        }
    };
    var managerField = typeof(AssetsHelper).GetField(
        "assetsManager",
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(
            typeof(AssetsHelper).FullName,
            "assetsManager");
    var previousManager = managerField.GetValue(null);

    try
    {
        File.WriteAllBytes(sourcePath, new byte[objectCount * objectSize]);
        var reader = new FileReader(
            sourcePath,
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.RandomAccess));
        reader.Endian = EndianType.LittleEndian;

        var assetsFile = (SerializedFile)RuntimeHelpers
            .GetUninitializedObject(typeof(SerializedFile));
        assetsFile.assetsManager = manager;
        assetsFile.reader = reader;
        assetsFile.game = manager.Game;
        assetsFile.fullName = reader.FullPath;
        assetsFile.originalPath = reader.FullPath;
        assetsFile.fileName = reader.FileName;
        assetsFile.version = [2022, 3, 0, 0];
        assetsFile.buildType = new BuildType("f");
        assetsFile.m_TargetPlatform = BuildTarget.StandaloneWindows64;
        assetsFile.header = new SerializedFileHeader
        {
            m_Version = SerializedFileFormatVersion.LargeFilesSupport,
        };
        assetsFile.m_Externals = [];
        assetsFile.Objects = [];
        assetsFile.ObjectsDic = [];
        assetsFile.m_Objects = Enumerable.Range(0, objectCount)
            .Select(index => new ObjectInfo
            {
                byteStart = index * objectSize,
                byteSize = objectSize,
                classID = (int)ClassIDType.TextAsset,
                m_PathID = index + 1,
            })
            .ToList();
        manager.assetsFileList.Add(assetsFile);
        managerField.SetValue(null, manager);

        using var spool =
            new AssetMapEntrySpool(manager.ContainerStorageOptions);
        using var stringCache =
            new AssetMapStringCache(manager.ContainerStorageOptions);
        var buildAssetMapFile = typeof(AssetsHelper).GetMethod(
            "BuildAssetMapFile",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(
                typeof(AssetsHelper).FullName,
                "BuildAssetMapFile");
        try
        {
            buildAssetMapFile.Invoke(
                null,
                [
                    sourcePath,
                    spool,
                    stringCache,
                    new AssetMapBuildMetrics(),
                    null,
                    null,
                    null
                ]);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                "Batched AssetMap object scanning failed.",
                exception.InnerException ?? exception);
        }

        Assert(
            spool.Count == objectCount,
            "Batched AssetMap scanning lost or duplicated entries.");
        Assert(
            assetsFile.Objects.Count == objectCount,
            "Batched AssetMap scanning lost or duplicated objects.");
        Assert(
            assetsFile.Objects
                .Select(obj => obj.m_PathID)
                .SequenceEqual(
                    Enumerable.Range(1, objectCount)
                        .Select(index => (long)index)),
            "Batched AssetMap scanning changed object order.");
    }
    finally
    {
        managerField.SetValue(null, previousManager);
        manager.Clear();
        StringCache.Clear();
        Directory.Delete(root, recursive: true);
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

static void VerifyAssetMapStringCache()
{
    const string first = "main/861b3a751571cd61a25f16ae.ab";
    const string collision = "Terrain_4_0_14_A";
    var root = CreateTemporaryRoot();
    try
    {
        using (var cache = new AssetMapStringCache(
            new ContainerStorageOptions
            {
                TemporaryDirectory = root
            },
            cacheByteLimit: 0,
            cacheEntryLimit: 0))
        {
            Assert(
                SevenZip.CRC.CalculateDigestUTF8(first)
                    == SevenZip.CRC.CalculateDigestUTF8(collision),
                "AssetMap string-cache collision fixture is invalid.");
            Assert(cache.Get(null) == null, "AssetMap string cache changed null.");
            Assert(cache.Get(first) == first, "AssetMap string cache changed its first value.");
            Assert(
                cache.Get(collision) == first,
                "Disk-backed AssetMap string cache did not preserve first-CRC semantics.");
            Assert(
                cache.Get(collision) == first,
                "Disk-backed AssetMap string cache changed after repeated disk lookup.");
            Assert(cache.Count == 1, "AssetMap string cache stored a CRC collision twice.");
            Assert(
                StringCache.Count == 0,
                "Disk-backed AssetMap string cache populated the process StringCache.");
        }

        Assert(
            !Directory.EnumerateDirectories(root, "run-*").Any(),
            "Disposed AssetMap string cache left a temporary workspace.");
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
        var writerMetrics = new AssetMapBuildMetrics();
        GenerateAssetMapFixtures(temporaryDirectory, writerMetrics);
        Assert(
            writerMetrics.GetMeasurementCount(AssetMapBuildStage.XmlWriting) == 1,
            "Legacy fixture generation did not record the XML writer.");
        Assert(
            writerMetrics.GetMeasurementCount(AssetMapBuildStage.JsonWriting) == 1,
            "Legacy fixture generation did not record the JSON writer.");
        Assert(
            writerMetrics.GetMeasurementCount(AssetMapBuildStage.MessagePackWriting) == 1,
            "Legacy fixture generation did not record the MessagePack writer.");
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

static void VerifyAssetMapStreamingBoundaryCompatibility()
{
    var root = CreateTemporaryRoot();
    var legacyDirectory = Path.Combine(root, "legacy");
    var streamingDirectory = Path.Combine(root, "streaming");
    var temporaryDirectory = Path.Combine(root, "temporary");
    var entries = CreateBoundaryAssetMapEntries();
    Directory.CreateDirectory(legacyDirectory);
    Directory.CreateDirectory(streamingDirectory);

    try
    {
        WriteLegacyAssetMaps(
            entries,
            GameType.ArknightsEndfield,
            "boundary-map",
            legacyDirectory);
        WriteStreamingAssetMaps(
            entries,
            GameType.ArknightsEndfield,
            "boundary-map",
            streamingDirectory,
            temporaryDirectory);

        Assert(
            File.ReadAllBytes(Path.Combine(legacyDirectory, "boundary-map.json"))
                .SequenceEqual(File.ReadAllBytes(
                    Path.Combine(streamingDirectory, "boundary-map.json"))),
            "Streaming JSON changed bytes across serialization boundaries.");
        Assert(
            File.ReadAllBytes(Path.Combine(legacyDirectory, "boundary-map.map"))
                .SequenceEqual(File.ReadAllBytes(
                    Path.Combine(streamingDirectory, "boundary-map.map"))),
            "Streaming MessagePack changed LZ4 block-array bytes.");

        var expectedXml = XDocument.Load(
            Path.Combine(legacyDirectory, "boundary-map.xml"));
        var actualXml = XDocument.Load(
            Path.Combine(streamingDirectory, "boundary-map.xml"));
        NormalizeAssetMapXml(expectedXml);
        NormalizeAssetMapXml(actualXml);
        Assert(
            XNode.DeepEquals(expectedXml, actualXml),
            "Streaming XML changed the normalized legacy structure.");
    }
    finally
    {
        StringCache.Clear();
        Directory.Delete(root, true);
    }
}

static void VerifyAssetMapMessagePackPoolBoundaryCompatibility()
{
    const int entryCount = 15_000;
    var root = CreateTemporaryRoot();
    var legacyDirectory = Path.Combine(root, "legacy");
    var streamingDirectory = Path.Combine(root, "streaming");
    var temporaryDirectory = Path.Combine(root, "temporary");
    var repeatedName = new string('p', 640);
    var entries = new List<AssetEntry>(entryCount);
    Directory.CreateDirectory(legacyDirectory);
    Directory.CreateDirectory(streamingDirectory);

    try
    {
        for (var index = 0; index < entryCount; index++)
        {
            entries.Add(new AssetEntry
            {
                Name = repeatedName,
                Container = $"pool/container/{index % 127:D3}",
                Source = $"input/{index % 31:D2}.assets",
                PathID = index,
                Type = ClassIDType.TextAsset,
                Hash = $"pool-hash-{index:D8}",
                Offset = index * 8L
            });
        }

        using (var mapFile = File.Create(
            Path.Combine(legacyDirectory, "pool-boundary.map")))
        {
            MessagePackSerializer.Serialize(
                mapFile,
                new AssetMap
                {
                    GameType = GameType.ArknightsEndfield,
                    AssetEntries = entries
                },
                MessagePackSerializerOptions.Standard.WithCompression(
                    MessagePackCompression.Lz4BlockArray));
        }

        using (var spool = new AssetMapEntrySpool(new ContainerStorageOptions
        {
            TemporaryDirectory = temporaryDirectory
        }))
        {
            foreach (var entry in entries)
            {
                spool.Append(AssetMapEntryRecord.FromAssetEntry(entry));
            }

            spool.Seal();
            AssetMapStreamingIO.WriteMaps(
                spool,
                new Game(GameType.ArknightsEndfield, "ArknightsEndfield"),
                "pool-boundary",
                streamingDirectory,
                ExportListType.MessagePack,
                new ContainerStorageOptions
                {
                    TemporaryDirectory = temporaryDirectory
                });
        }

        Assert(
            File.ReadAllBytes(Path.Combine(legacyDirectory, "pool-boundary.map"))
                .SequenceEqual(File.ReadAllBytes(
                    Path.Combine(streamingDirectory, "pool-boundary.map"))),
            "Streaming MessagePack changed bytes after exhausting pooled 32/64 KiB segments.");
    }
    finally
    {
        StringCache.Clear();
        Directory.Delete(root, true);
    }
}

static void VerifyAssetMapStreamingReaders()
{
    var root = CreateTemporaryRoot();
    var outputDirectory = Path.Combine(root, "output");
    var temporaryDirectory = Path.Combine(root, "temporary");
    var entries = new List<AssetEntry>
    {
        new()
        {
            Name = "AlphaTexture",
            Container = "characters/alpha",
            Source = "input/first.assets",
            PathID = 1,
            Type = ClassIDType.Texture2D,
            Hash = "a",
            Offset = 10
        },
        new()
        {
            Name = "BetaText",
            Container = "tables/beta",
            Source = "input/second.assets",
            PathID = 2,
            Type = ClassIDType.TextAsset,
            Hash = "b",
            Offset = 20
        },
        new()
        {
            Name = "AlphaDuplicateSource",
            Container = "characters/alpha/duplicate",
            Source = "input/first.assets",
            PathID = 3,
            Type = ClassIDType.Texture2D,
            Hash = "c",
            Offset = 30
        },
        new()
        {
            Name = "AlphaOther",
            Container = "effects/alpha",
            Source = "input/third.assets",
            PathID = 4,
            Type = ClassIDType.Texture2D,
            Hash = "d",
            Offset = 40
        }
    };
    Directory.CreateDirectory(outputDirectory);

    try
    {
        WriteStreamingAssetMaps(
            entries,
            GameType.ArknightsEndfield,
            "reader-map",
            outputDirectory,
            temporaryDirectory);
        AssetsHelper.SetContainerStorageOptions(new ContainerStorageOptions
        {
            TemporaryDirectory = temporaryDirectory
        });

        foreach (var format in new[]
        {
            (Type: ExportListType.XML, Extension: ".xml"),
            (Type: ExportListType.JSON, Extension: ".json"),
            (Type: ExportListType.MessagePack, Extension: ".map")
        })
        {
            var path = Path.Combine(outputDirectory, $"reader-map{format.Extension}");
            var allSources = AssetsHelper.ParseAssetMap(
                path,
                format.Type,
                [],
                [],
                []);
            Assert(
                allSources.SequenceEqual(
                    [
                        "input/first.assets",
                        "input/second.assets",
                        "input/third.assets"
                    ]),
                $"{format.Type} reader did not preserve first-source order.");

            var filteredSources = AssetsHelper.ParseAssetMap(
                path,
                format.Type,
                [ClassIDType.Texture2D],
                [new Regex("^Alpha", RegexOptions.IgnoreCase)],
                [new Regex("^characters/", RegexOptions.IgnoreCase)]);
            Assert(
                filteredSources.SequenceEqual(["input/first.assets"]),
                $"{format.Type} reader changed combined filter behavior.");
        }

        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        foreach (var format in new[]
        {
            (Type: ExportListType.XML, Extension: ".xml"),
            (Type: ExportListType.JSON, Extension: ".json"),
            (Type: ExportListType.MessagePack, Extension: ".map")
        })
        {
            var sources = AssetsHelper.ParseAssetMap(
                Path.Combine(
                    fixtureDirectory,
                    $"legacy-asset-map{format.Extension}"),
                format.Type,
                [],
                [],
                []);
            Assert(
                sources.SequenceEqual(
                    ["input/1001.assets", "input/1002.assets"]),
                $"{format.Type} streaming reader cannot read the legacy fixture.");
        }

        var lowercaseJsonPath = Path.Combine(outputDirectory, "lowercase.json");
        File.WriteAllText(
            lowercaseJsonPath,
            """
            {
              "assetentries": [
                {
                  "name": "Lowercase",
                  "container": "characters/lowercase",
                  "source": "input/lowercase.assets",
                  "type": "Texture2D"
                }
              ]
            }
            """);
        var lowercaseSources = AssetsHelper.ParseAssetMap(
            lowercaseJsonPath,
            ExportListType.JSON,
            [],
            [],
            []);
        Assert(
            lowercaseSources.SequenceEqual(["input/lowercase.assets"]),
            "JSON streaming reader lost case-insensitive property matching.");
    }
    finally
    {
        AssetsHelper.SetContainerStorageOptions(new ContainerStorageOptions());
        StringCache.Clear();
        Directory.Delete(root, true);
    }
}

static void VerifySyntheticLargeAssetMapStreaming()
{
    const int entryCount = 20_000;
    const int sourceCount = 64;
    var root = CreateTemporaryRoot();
    var outputDirectory = Path.Combine(root, "output");
    var temporaryDirectory = Path.Combine(root, "temporary");
    Directory.CreateDirectory(outputDirectory);
    StringCache.Clear();

    try
    {
        using (var spool = new AssetMapEntrySpool(new ContainerStorageOptions
        {
            TemporaryDirectory = temporaryDirectory
        }))
        {
            for (var index = 0; index < entryCount; index++)
            {
                spool.Append(new AssetMapEntryRecord
                {
                    Name = $"Unique-Name-{index:D6}",
                    Container = $"container/{index:D6}",
                    Source = $"input/{index % sourceCount:D2}.assets",
                    PathID = index,
                    Type = ClassIDType.TextAsset,
                    Hash = $"hash-{index:D6}",
                    Offset = index * 8L
                });
            }

            Assert(
                StringCache.Count == 0,
                "Synthetic AssetMap construction populated the process StringCache.");
            spool.Seal();

            for (var pass = 0; pass < 2; pass++)
            {
                long count = 0;
                foreach (var entry in spool.ReadEntries())
                {
                    count++;
                    Assert(
                        entry.Name.StartsWith("Unique-Name-", StringComparison.Ordinal),
                        "Synthetic spool returned a corrupted unique name.");
                }

                Assert(count == entryCount, "Synthetic spool pass lost entries.");
                Assert(
                    StringCache.Count == 0,
                    "Repeated spool enumeration retained strings globally.");
            }

            AssetMapStreamingIO.WriteMaps(
                spool,
                new Game(GameType.Normal, "Normal"),
                "synthetic-large",
                outputDirectory,
                ExportListType.XML
                    | ExportListType.JSON
                    | ExportListType.MessagePack,
                new ContainerStorageOptions
                {
                    TemporaryDirectory = temporaryDirectory
                });
            Assert(
                StringCache.Count == 0,
                "Streaming writers retained synthetic entries in StringCache.");
        }

        Assert(
            !Directory.EnumerateDirectories(temporaryDirectory, "run-*").Any(),
            "Synthetic AssetMap streaming left a temporary workspace.");
        foreach (var format in new[]
        {
            (Type: ExportListType.XML, Extension: ".xml"),
            (Type: ExportListType.JSON, Extension: ".json"),
            (Type: ExportListType.MessagePack, Extension: ".map")
        })
        {
            var sources = AssetMapStreamingIO.ReadSources(
                Path.Combine(
                    outputDirectory,
                    $"synthetic-large{format.Extension}"),
                format.Type,
                [],
                [],
                [],
                new ContainerStorageOptions
                {
                    TemporaryDirectory = temporaryDirectory
                });
            Assert(
                sources.Length == sourceCount,
                $"{format.Type} synthetic reader retained the wrong source set.");
        }

        Assert(
            StringCache.Count == 0,
            "Synthetic AssetMap readers retained strings globally.");
        Assert(
            !Directory.EnumerateDirectories(temporaryDirectory, "run-*").Any(),
            "Synthetic AssetMap readers left a temporary workspace.");
    }
    finally
    {
        StringCache.Clear();
        Directory.Delete(root, true);
    }
}

static void VerifyAssetMapStreamingExceptionalCleanup()
{
    VerifyAssetMapSpoolFaultCleanup(
        new IOException("Simulated disk-full failure."));
    VerifyAssetMapSpoolFaultCleanup(
        new OutOfMemoryException("Simulated AssetMap OOM."));

    var root = CreateTemporaryRoot();
    var outputDirectory = Path.Combine(root, "output");
    var temporaryDirectory = Path.Combine(root, "temporary");
    Directory.CreateDirectory(outputDirectory);
    try
    {
        using (var spool = new AssetMapEntrySpool(new ContainerStorageOptions
        {
            TemporaryDirectory = temporaryDirectory
        }))
        {
            spool.Append(AssetMapEntryRecord.FromAssetEntry(
                CreateAssetMapFixtureEntries()[0]));
            spool.Seal();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            AssertThrows<OperationCanceledException>(() =>
                AssetMapStreamingIO.WriteMaps(
                    spool,
                    new Game(GameType.Normal, "Normal"),
                    "cancelled",
                    outputDirectory,
                    ExportListType.MessagePack,
                    new ContainerStorageOptions
                    {
                        TemporaryDirectory = temporaryDirectory
                    },
                    cancellationToken: cancellation.Token));
        }

        Assert(
            !Directory.EnumerateDirectories(temporaryDirectory, "run-*").Any(),
            "Cancelled AssetMap streaming left a temporary workspace.");

        var malformed = Path.Combine(root, "malformed.map");
        File.WriteAllBytes(malformed, [0x92, 0xd4, 0x62, 0x01]);
        AssertThrows<Exception>(() =>
            AssetMapStreamingIO.ReadSources(
                malformed,
                ExportListType.MessagePack,
                [],
                [],
                [],
                new ContainerStorageOptions
                {
                    TemporaryDirectory = temporaryDirectory
                }));
        Assert(
            !Directory.EnumerateDirectories(temporaryDirectory, "run-*").Any(),
            "Malformed MessagePack parsing left a temporary workspace.");
    }
    finally
    {
        StringCache.Clear();
        Directory.Delete(root, true);
    }
}

static void VerifyAssetMapSpoolFaultCleanup(Exception expected)
{
    var root = CreateTemporaryRoot();
    try
    {
        try
        {
            using var spool = new AssetMapEntrySpool(new ContainerStorageOptions
            {
                TemporaryDirectory = root
            }, operation =>
            {
                if (operation == AssetMapSpoolOperation.Appending)
                {
                    throw expected;
                }
            });
            spool.Append(AssetMapEntryRecord.FromAssetEntry(
                CreateAssetMapFixtureEntries()[0]));
            throw new InvalidOperationException(
                "AssetMap spool fault injection did not run.");
        }
        catch (Exception actual) when (ReferenceEquals(actual, expected))
        {
        }

        Assert(
            !Directory.EnumerateDirectories(root, "run-*").Any(),
            $"{expected.GetType().Name} left an AssetMap spool workspace.");
    }
    finally
    {
        StringCache.Clear();
        Directory.Delete(root, true);
    }
}

static void VerifyAssetMapBuildMetrics()
{
    var metrics = new AssetMapBuildMetrics();
    using (metrics.Measure(AssetMapBuildStage.Loading))
    {
        Thread.SpinWait(10_000);
    }
    using (metrics.Measure(AssetMapBuildStage.Loading))
    {
        Thread.SpinWait(10_000);
    }
    using (metrics.Measure(AssetMapBuildStage.JsonWriting))
    {
        Thread.SpinWait(10_000);
    }

    Assert(
        metrics.GetMeasurementCount(AssetMapBuildStage.Loading) == 2,
        "AssetMap loading timing did not accumulate repeated passes.");
    Assert(
        metrics.GetElapsed(AssetMapBuildStage.Loading) > TimeSpan.Zero,
        "AssetMap loading timing did not record elapsed time.");

    var summary = metrics.FormatSummary(123).ToArray();
    Assert(
        summary[0] == "AssetMap stage timings (123 assets):",
        "AssetMap timing summary header changed.");
    Assert(
        summary.Any(line => line.StartsWith("  Loading: ", StringComparison.Ordinal)
            && line.EndsWith("(2 passes)", StringComparison.Ordinal)),
        "AssetMap loading timing summary is incorrect.");
    Assert(
        summary.Contains("  XML writer: not run"),
        "AssetMap timing summary does not distinguish an unselected writer.");
    Assert(
        summary.Any(line => line.StartsWith("  JSON writer: ", StringComparison.Ordinal)
            && line.EndsWith("(1 pass)", StringComparison.Ordinal)),
        "AssetMap JSON writer timing summary is incorrect.");
}

static void VerifySilentStateRestoredAfterLoadFailure()
{
    var root = CreateTemporaryRoot();
    var emptyFile = Path.Combine(root, "empty.bin");
    File.WriteAllBytes(emptyFile, []);
    var manager = new AssetsManager { Silent = true };

    try
    {
        Logger.Silent = false;
        Progress.Silent = false;
        AssertThrows<Exception>(() => manager.LoadFiles(emptyFile));
        Assert(!Logger.Silent, "LoadFiles failure left global logging silent.");
        Assert(!Progress.Silent, "LoadFiles failure left global progress silent.");

        Logger.Silent = true;
        Progress.Silent = true;
        AssertThrows<Exception>(() =>
            manager.LoadFolder(Path.Combine(root, "missing-directory")));
        Assert(Logger.Silent, "LoadFolder failure changed the prior logging state.");
        Assert(Progress.Silent, "LoadFolder failure changed the prior progress state.");
    }
    finally
    {
        Logger.Silent = false;
        Progress.Silent = false;
        manager.Clear();
        Directory.Delete(root, true);
    }
}

static void GenerateAssetMapFixtures(
    string outputDirectory,
    AssetMapBuildMetrics? metrics = null)
{
    Directory.CreateDirectory(outputDirectory);
    var entries = CreateAssetMapFixtureEntries();
    AssetsHelper.ExportAssetsMap(
            entries,
            new Game(GameType.ArknightsEndfield, "Arknights: Endfield"),
            "legacy-asset-map",
            outputDirectory,
            ExportListType.XML | ExportListType.JSON | ExportListType.MessagePack,
            metrics)
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

static List<AssetEntry> CreateBoundaryAssetMapEntries()
{
    var entries = new List<AssetEntry>(4096);
    for (var index = 0; index < entries.Capacity; index++)
    {
        var longName = index % 257 == 0
            ? new string((char)('a' + index % 26), 40_000)
            : $"Boundary-{index:D6}-{new string('n', index % 67)}";
        entries.Add(new AssetEntry
        {
            Name = longName,
            Container = $"container/{index % 31}/{new string('c', index % 71)}",
            Source = $"input/{index % 97:D3}.assets",
            PathID = index % 2 == 0 ? index : -index,
            Type = index % 3 == 0
                ? ClassIDType.Texture2D
                : ClassIDType.TextAsset,
            Hash = index % 11 == 0 ? null : $"hash-{index:D8}",
            Offset = index * 4096L
        });
    }

    return entries;
}

static void WriteLegacyAssetMaps(
    List<AssetEntry> entries,
    GameType gameType,
    string name,
    string outputDirectory)
{
    var previousCulture = Thread.CurrentThread.CurrentCulture;
    try
    {
        Thread.CurrentThread.CurrentCulture =
            new System.Globalization.CultureInfo("en-US");
        var xmlPath = Path.Combine(outputDirectory, $"{name}.xml");
        var xmlSettings = new XmlWriterSettings { Indent = true };
        using (var writer = XmlWriter.Create(xmlPath, xmlSettings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("Assets");
            writer.WriteAttributeString("filename", xmlPath);
            writer.WriteAttributeString("createdAt", DateTime.UtcNow.ToString("s"));
            foreach (var asset in entries)
            {
                writer.WriteStartElement("Asset");
                writer.WriteElementString("Name", asset.Name);
                writer.WriteElementString("Container", asset.Container);
                writer.WriteStartElement("Type");
                writer.WriteAttributeString("id", ((int)asset.Type).ToString());
                writer.WriteValue(asset.Type.ToString());
                writer.WriteEndElement();
                writer.WriteElementString("PathID", asset.PathID.ToString());
                writer.WriteElementString("Source", asset.Source);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        using (var file = File.CreateText(
            Path.Combine(outputDirectory, $"{name}.json")))
        {
            var serializer = new JsonSerializer
            {
                Formatting = Newtonsoft.Json.Formatting.Indented
            };
            serializer.Converters.Add(new StringEnumConverter());
            serializer.Serialize(file, new
            {
                GameType = gameType,
                AssetEntries = entries
            });
        }

        using var mapFile = File.Create(
            Path.Combine(outputDirectory, $"{name}.map"));
        MessagePackSerializer.Serialize(
            mapFile,
            new AssetMap
            {
                GameType = gameType,
                AssetEntries = entries
            },
            MessagePackSerializerOptions.Standard.WithCompression(
                MessagePackCompression.Lz4BlockArray));
    }
    finally
    {
        Thread.CurrentThread.CurrentCulture = previousCulture;
        StringCache.Clear();
    }
}

static void WriteStreamingAssetMaps(
    IEnumerable<AssetEntry> entries,
    GameType gameType,
    string name,
    string outputDirectory,
    string temporaryDirectory)
{
    using var spool = new AssetMapEntrySpool(new ContainerStorageOptions
    {
        TemporaryDirectory = temporaryDirectory
    });
    foreach (var entry in entries)
    {
        spool.Append(AssetMapEntryRecord.FromAssetEntry(entry));
    }

    spool.Seal();
    var previousCulture = Thread.CurrentThread.CurrentCulture;
    try
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        AssetMapStreamingIO.WriteMaps(
            spool,
            new Game(gameType, gameType.ToString()),
            name,
            outputDirectory,
            ExportListType.XML | ExportListType.JSON | ExportListType.MessagePack,
            new ContainerStorageOptions
            {
                TemporaryDirectory = temporaryDirectory
            });
        Assert(
            Thread.CurrentThread.CurrentCulture.Name == "fr-FR",
            "Streaming AssetMap writers changed the calling thread culture.");
    }
    finally
    {
        Thread.CurrentThread.CurrentCulture = previousCulture;
    }
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

sealed class ThreadRecordingProgress : IProgress<int>
{
    private readonly HashSet<int> workerThreads = [];

    internal int WorkerThreadCount
    {
        get
        {
            lock (workerThreads)
            {
                return workerThreads.Count;
            }
        }
    }

    public void Report(int value)
    {
        if (value <= 0)
        {
            return;
        }

        lock (workerThreads)
        {
            workerThreads.Add(Environment.CurrentManagedThreadId);
        }
        Thread.Sleep(1);
    }
}
