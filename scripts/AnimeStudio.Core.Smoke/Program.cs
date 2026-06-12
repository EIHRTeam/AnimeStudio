using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text;
using AnimeStudio;

try
{
    VerifyEndfieldTosData(GameType.ArknightsEndfield);
    VerifyEndfieldTosData(GameType.ArknightsEndfieldCB3);
    VerifyTraditionalTosMap();
    VerifyControllerSizeMismatch();
    VerifyNegativeArrayLength();
    VerifyObjectBoundedArrayLength();
    VerifyObjectBoundedStringLength();
    VerifyLargeContainerUsesTemporaryFile(typeof(MhyFile));
    VerifyLargeContainerUsesTemporaryFile(typeof(Blb3File));
    VerifyLargeContainerUsesTemporaryFile(typeof(HygFile));
    VerifyLargeContainerUsesTemporaryFile(typeof(VFSFile));

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

static void VerifyLargeContainerUsesTemporaryFile(Type containerType)
{
    var instance = RuntimeHelpers.GetUninitializedObject(containerType);
    var blocksField = containerType.GetField(
        "m_BlocksInfo",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(containerType.FullName, "m_BlocksInfo");
    blocksField.SetValue(
        instance,
        new List<BundleFile.StorageBlock>
        {
            new() { uncompressedSize = 1_200_000_000 },
            new() { uncompressedSize = 1_200_000_000 },
        });

    var createMethod = containerType.GetMethod(
        "CreateBlocksStream",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(containerType.FullName, "CreateBlocksStream");
    var path = Path.Combine(
        Path.GetTempPath(),
        $"animestudio-{containerType.Name}-{Guid.NewGuid():N}");

    using var stream = (Stream)(createMethod.Invoke(instance, [path])
        ?? throw new InvalidOperationException($"{containerType.Name} returned a null blocks stream."));
    Assert(stream is FileStream, $"{containerType.Name} did not use a temporary file above 2 GiB.");
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
