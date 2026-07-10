> Historical document. Current sources of truth are `/ROADMAP.md`, `/PLAN.md`,
> and `/STATUS.md`.

# AnimeStudio CLI 内存泄漏 / OOM 风险研究报告

> **性质：只研究，不做实际更改。** 本文档是研究结论 + 优化路线图，供后续决策。
> **目标平台：** Debian 13 x64 服务器，16 GB RAM，.NET 10 运行时（SDK 10.0.301），批量提取数十~数百 GB 游戏 bundle，进程可能长跑。
> **范围：** CLI 依赖链 = AnimeStudio.CLI → AnimeStudio(Core) / AnimeStudio.Utility / AnimeStudio.PInvoke / AnimeStudio.FBXWrapper（含原生 Oodle/FBXNative）。排除 GUI / Patcher。

## Context

用户要求对（1）当前未提交的更改、（2）CLI 及依赖链代码，做深入内存审计：泄漏点、不健全/可优化的内存策略、OOM 成因与优化方法。

**审计方法（ultracode 多代理）：** 7 维度并行扫描（diff 加固、容器解压、资产生命周期、CLI 主流程、Classes 反序列化、原生互操作、运行时/GC 配置）→ 完备性批判补漏（FBX IR / YAML / Sprite / Shader 四个盲区）→ 每条发现经 1~2 个对抗性验证者双视角（代码准确性 + 现实影响）复核。共 111 个 Opus 子代理，70 条原始发现 → **52 确认 / 4 存疑 / 14 驳回**。主会话另行亲自核实了骨架级发现（NapAssetBundleIndexAsset、Clear()、MhyFile、BundleFile、Program/Studio 主循环）。

**采纳验证者改判后的最终严重度分布：1 high + 30 medium + 21 low。** 没有"单点必死"的 critical —— 真实画像是：**大量结构性"全量物化到托管堆"策略叠加 + 进程零 GC 配置 + 默认 Workstation GC 无堆上限，在 16GB 机上批处理大 bundle 时，峰值 RSS 可逼近物理上限，被 Linux OOM killer 直接 SIGKILL（而非抛 OutOfMemoryException，数据无法优雅落盘）。**

## 核心结论（先看这里）

1. **不是泄漏问题，是峰值与策略问题。** CLI 主循环逐顶层文件 `LoadFiles → BuildAssetData → ExportAssets`，`finally` 里 `exportableAssets.Clear()` + `assetsManager.Clear()`（`Program.cs:185-205`）。`Clear()` 确定性清空所有集合并 Close 所有 reader（`AssetsManager.cs:597-627`），内存按单容器规模周期性复位。验证者驳回了多条"泄漏/无界增长"误报（StreamFile 常驻、依赖闭包递归），它们本质是"有 Clear 的工作内存"。**真正无界跨整轮累积的只有：资产图构建列表、scrapeMonos 静态字符串列表、StringCache 静态字典**（均仅在特定模式下触发）。

2. **峰值由"单容器全量物化 + 多层缓冲"决定。** 单个 ZZZ .blk / SR Mhy 容器解压后整份留在托管堆（`new MemoryStream`），且 `blocksStream` 与逐节点 `StreamFile` MemoryStream 在 `ReadFiles` 期间双倍共存；该容器全部 CAB 资源流 + 全部反序列化对象同时驻留到 `Clear()`。叠加导出路径（FBX IR 三份共存、YAML/OBJ/Shader 全量字符串缓冲），单容器瞬时可达数 GB。

3. **零 GC 配置是最高杠杆的"不改业务代码"优化。** 整条链 + csproj + Directory.Build.props + 发布脚本无任何 GC/运行时内存配置（`ServerGarbageCollection`/`GCHeapHardLimit`/`RetainVM`/`MALLOC_ARENA_MAX` 全零命中），且 `Clear()` 尾部 `GC.Collect` 被注释（`AssetsManager.cs:625-626`）。在 cgroup v2 的 Debian 上无堆硬上限 → OOM killer 风险。**这是路线图第一优先级。**

## 已确认发现（按最终严重度，采纳验证者改判）

### A. 未提交 diff 的定性 + 加固残留

未提交 diff 本身是一次**正向的 OOM 防护加固**，方向正确：新增 `ReadArrayLength/ValidateLength`（分配前用 `length > Remaining/minElementSize` 拦截恶意长度）；`ReadBytes` 改为前置校验 + `GC.AllocateUninitializedArray` 一次分配（消除旧的 ArrayPool+List+ToArray 双倍缓冲）；`ObjectReader.Remaining` 覆写为对象切片边界；`ReadArray<T>` 删除 `>0x1000` 的 List 分支消除大数组双倍缓冲；`AnimatorController` 全部长度读取迁移到 `ReadArrayLength`。

**残留与边界（均 medium/low）：**

- **`new List<T>(rawReadInt32())` 容量预分配绕过加固**（原 critical→**medium**）。`Classes/NapAssetBundleIndexAsset.cs:22-43` 4 处用未校验 `ReadInt32()` 直接作容量；`Avatar.cs:203/362/368`、`IImported.cs:48` 同型。主会话已亲验属实。降级理由：`List<T>(capacity)` 对引用类型只分配 `object[capacity]`（8 字节/项指针）非元素本体，且 `ObjectReader.Read` 切片边界会在后续逐元素读取时抛异常。但预分配先于边界检查发生。**修复：** 改 `ReadArrayLength(elementWireSize,...)`，或 `new List<T>()` 不预设容量靠切片自然限流。
- **`ReadAlignedString` 由静默容忍变抛异常**（确认非内存问题）。异常被 `ReadAssets` per-object try/catch 捕获跳过，不中断批处理、不泄漏；语义从"出半个对象"变"丢弃整对象"，属可接受收紧。
- **验证者驳回的相关误报**（不写入修复清单）："大量 Classes/ 裸 ReadInt32 作循环计数"和"ValidateLength 上界过松致放大" —— 均因循环是 stream-bounded 逐元素读取、受切片硬限流，非预分配。

### B. 峰值内存 —— 容器解压与全量物化（medium 为主）

峰值主来源。容器链（Bundle/Mhy/Blb/Hyg/VFS/Web）几乎无流式，全部"解压后整份进托管堆"：

- **`blocksStream` 整份解压 bundle 物化到托管堆，临时文件阈值 2GB**（原 high→medium）。`BundleFile.cs:261-277`：`uncompressedSizeSum < int.MaxValue` 时 `new MemoryStream`，2GB 以内整份留堆，扩容还翻倍。
- **`ReadFiles` 双倍缓冲**（原 high→medium）。`BundleFile.cs:311-339`：`blocksStream` 尚活时又为每 node `new MemoryStream`，同份数据内存两份。
- **单容器全部 CAB 资源流 + 反序列化对象同时驻留到 Clear()**（原 high→medium）。`AssetsManager.cs:437-543/629-700`：ZZZ .blk/SR Mhy 单容器含成百上千 CAB，`resourceFileReaders` 缓存全部 .resS 流，`ReadAssets` 全量反序列化所有 object。**验证者驳回了"4-5GB 双缓冲/泄漏"定性** —— `blocksStream` 在 using 块内 `ReadFiles` 后即释放，`Clear()` 确定性回收，属必要工作内存，单容器现实峰值约 1.5-3GB。
- **Mhy/Blb/Hyg/VFS 临时文件兜底是死代码**（原 high→**low**，真实 bug）。`MhyFile.cs:162-167`：`(int)m_BlocksInfo.Sum(...)` 先 long→int 强转，再 `if (>= int.MaxValue)` 永不成立（溢出变负），恒走 `new MemoryStream`。主会话已亲验。对比 `BundleFile.cs:264` 是 long 比较、正确。
- **解压缓冲低效（medium/low）：** Zstd 分支多一次 `ToArray()` 全块 LOH 拷贝（`BundleFile.cs:808`）；UnityWeb/Raw 旧路径每块 `ReadBytes+ToArray`（`:279-294`）；`OodleHelper.cs:19-32` 写出缓冲多 64 字节；`ImportHelper.cs:97-226` 整文件解密 `ReadBytes(Length)+增长 MemoryStream` 致翻倍。
  - **正面：** `ReadBlocks` 各分支用 `ArrayPool.Rent/Return + try/finally`，健康；唯块 `uncompressedSize>1MB`（ArrayPool `maxArrayLength=2^20`）时退化为 LOH 直分配 + Return 丢弃，Oodle 大块产生 LOH 抖动。

### C. 跨整轮真正无界累积（medium，重点）

少数"不随单容器 Clear() 复位、随整个 run 增长"的：

- **资产图构建全量累积**（原 high→medium）。`AssetsHelper.cs:322-344/521-527/649-716`：`new List<AssetEntry>()` 跨所有文件累积，`LoadFiles` 每文件 `Clear()` 但不触碰该列表，末尾一次性序列化。**修复：** 流式输出（每文件增量写 JSON/XML streaming writer 或 MessagePack 分段，Source 路径 intern/索引化）。
- **`scrapeMonos` 静态字符串列表整轮不清空**（原 high→**medium**，唯一真 leak 类）。`Studio.cs:34-36` 静态三列表，`Exporter.cs` 每匹配 `.Add`，`Program.cs:202-204` finally 不清，末尾 `.Distinct().OrderBy()` 再整表复制。仅 scrapeMonos 开启触发。**修复：** HashSet 去重 + 按文件增量 flush 追加写。
- **`StringCache` 静态字典资产图全程去重永不清理**（medium）。`AssetMap.cs:10-25`。仅资产图模式触发。

### D. Classes 反序列化层（medium）

- **`TypeTreeHelper.ReadType` 构建装箱 DOM**（原 high→medium）。`TypeTreeHelper.cs:166-337`：每元素递归 `ReadValue` 返回 `object`（装箱）、`new OrderedDictionary`、`GetNodes` 每次 `new List` 复制子树。大型 MonoBehaviour 放大数倍。入口 `Object.ToType`（Dump/JSON）。**修复：** Dump 走 `ReadTypeString` 直写 StringBuilder；ToType 用 `JsonTextWriter` 流式；`GetNodes` 传 `(startIndex,count)` 不复制。
- **Mesh 顶点全量物化 + 双倍驻留**（原 high→medium）。`Mesh.cs:171/517-535/860-886`：顶点 byte[] 与展开 float 数组双份。
- **MiHoYoBinData getter 每次访问重复解密 + 重复 JSON 解析 + 整段复制**（medium）。`MiHoYoBinData.cs:34-81`。**修复：** lazy 缓存解密/解析结果。

### E. 导出路径（Convert/Dump，medium/low）

- **FBX IR 全量物化，与原始 Mesh、原生 FBX 三份共存**（原 high→medium）。`ModelConverter.cs:354-427`：整层级转纯托管引用对象图，每顶点引用类型 + 8 通道锯齿 UV/权重/骨骼子数组（`:379-424` 海量小对象 GC 压力）。
  - **存疑（验证者分歧）：** blendshape Morph 嵌套展开（`:508-564`）—— 一方认为大放大，另一方指出 frame 循环对应互不重叠的 shape（非笛卡尔积），量级被高估。结论：真实低效但非 critical OOM 单点。
- **纹理解码为 PNG + `ToArray` 双缓冲驻留 TextureList**（原 high→**low**）。`ModelConverter.cs:770-787`。**修复：** 转完一张即写盘/喂原生并释放；`GetBuffer`+长度替代 `ToArray`。
- **全量字符串缓冲未流式写盘（medium/low）：** AnimationClip YAML 对象树全量物化（`AnimationClipExtensions.cs:159-169`）+ `StringWriter→ToString→WriteAllText` 三重缓冲（`Exporter.cs:457-460`）；Mesh OBJ 单 `StringBuilder` 全量 + 双缓冲（`Exporter.cs:273-345`）；Shader megashader 全平台/段解压 subprogram 同时物化（`ShaderConverter.cs:48-83`）+ 十余处 helper 返回 string 逐层 Append（`:86-244`）；header 拼接 + WriteAllText（`Exporter.cs:69-77`）。**修复：** 一律改 `StreamWriter`/`JsonTextWriter` 流式写 `File.OpenWrite`。
- **共享 SpriteAtlas 对每个 Sprite 重复全量解码**（原 high→medium）。`SpriteHelper.cs:16-37`：无原图缓存，N 个 sprite 解码 N 次 4K 图。**修复：** 按 atlas 缓存解码结果。

### F. 原生互操作（low，但应修）

- **`AsFbxAnimContext` 泄漏 FbxAnimCurveFilterUnroll**（**high/leak**，唯一保留 high）。`AnimeStudio.FBXNative/asfbx_anim_context.cpp:6-9` `new FbxAnimCurveFilterUnroll`，但 `.h:30` 析构 `=default`、`api.cpp:813` 只 delete 默认析构 —— 每次导出含动画 FBX 泄漏一个原生 filter，批量累积。**修复：** `~AsFbxAnimContext(){ delete lFilter; }`。
- **ACL/DBACL 漏调 Dispose**（原 high→**low**）。`ACL.cs:35-42/144-157`：`DisposeNative` 无 try/finally 兜底；仅持 IntPtr 无 SafeHandle。glibc 16 字节对齐使相关越界在 Debian 不触发，但健壮性仍应补。
- **AudioClip 每 clip 重建 FMOD System**（low）。`AudioClipConverter.cs:26-65`。**修复：** 复用单个 System。
- **正面：** DllLoader、Texture2DDecoder、FbxExporterContext/FBX 原生 Dispose 链、ImageSharp WriteToStream 流式编码均无泄漏（验证确认）。

### G. 运行时/GC 配置 —— 详见下方路线图第 1 节（config）


## 优化路线图（建议，本次不实施）

按"投入产出比"排序。所有内存预算估算基于 16GB 物理内存、cgroup v2、批处理大 bundle 的画像。

### 第 1 优先级 —— 纯配置/环境，零业务代码改动，最高杠杆

这一层不碰任何 C# 逻辑，只加配置和环境变量，却直接决定进程会不会被 OOM killer 突杀。

1. **设堆硬上限（防 OOM killer 突杀）。** 新增 `AnimeStudio.CLI/runtimeconfig.template.json`，设 `System.GC.HeapHardLimitPercent`（保守 `0x46`=70%，约 11GB 封顶）。或在 deb 包装脚本/systemd unit 设 `DOTNET_GCHeapHardLimitPercent=70`。**效果：** 默认无上限时峰值 RSS 可逼近 12-15GB 才回收；封顶后提前触发回收，把"硬性 OOM 被 SIGKILL（数据丢失）"转为"可捕获的 OutOfMemoryException / 可控回收压力"。
2. **GC 模式显式锁定。** 该负载几乎单线程顺序处理（仅 `AssetsHelper.cs:114` 一处 AsParallel、`:651` 一处 Task.Run），**不建议盲开 Server GC** —— Server GC 按核预留多堆 + 更高触发阈值会抬高 16GB 机的峰值 RSS。建议显式 `ServerGarbageCollection=false` + `ConcurrentGarbageCollection=true`，并配 `RetainVMGarbageCollection` 视实测取舍。
3. **限制 glibc malloc arena（原生库 RSS）。** deb 包装脚本 exec 前 `export MALLOC_ARENA_MAX=2`（甚至 1），可选叠加 `export MALLOC_TRIM_THRESHOLD_=131072`。Oodle/FBX/ACL/FMOD 原生分配走 glibc malloc，多线程下 arena 膨胀可额外吃数百 MB~2GB 且不回收；限制后通常压低 50-70%。纯脚本改动。
4. **LOH 碎片对策。** 长跑批处理设 `DOTNET_GCConserveMemory=5~9`（用 CPU 换更低 RSS、更积极归还）。配合下方第 2 优先级第 1 条恢复 `Clear()` 尾的周期性 LOH 压缩。
5. **（部署侧）systemd unit 设 `MemoryMax`**，让 .NET 通过 cgroup v2 感知上限并提前回收，与第 1 条互补。

### 第 2 优先级 —— 小改动、收益明确

1. **恢复 `Clear()` 的周期性 LOH 压缩。** `AssetsManager.cs:625-626` 已注释掉的 `GC.Collect()` 改为：每处理 N 个文件后 `GCSettings.LargeObjectHeapCompactionMode = CompactOnce; GC.Collect();`（不必每文件，避免吞吐损失）。直接缓解大解压缓冲释放后 LOH 不归还。
2. **修 MhyFile 死代码 bug。** `MhyFile.cs:162` 的 `(int)Sum(...)` 改为 `long uncompressedSizeSum = m_BlocksInfo.Sum(...)`，与 `BundleFile.cs:264` 对齐，让 2GB+ 容器的临时文件兜底真正生效。Blb/Hyg/VFS 同型一并修。
3. **修原生 filter 泄漏。** `asfbx_anim_context.cpp` 加 `~AsFbxAnimContext(){ delete lFilter; }`（或析构非 `=default`）。批量导出动画 FBX 时消除每次一个原生对象的累积。
4. **补 diff 加固残留。** `NapAssetBundleIndexAsset.cs`/`Avatar.cs` 的 `new List<T>(ReadInt32())` 改 `ReadArrayLength(...)` 或不预设容量。
5. **静态累积按文件 flush。** `scrapeMonos` 三列表（`Studio.cs:34-36`）改 HashSet + 增量追加写；`StringCache`（`AssetMap.cs`）在资产图运行末尾显式清理。

### 第 3 优先级 —— 结构性流式化（收益大、改动大，需评估）

1. **导出路径全面流式写盘。** 把 YAML/OBJ/Shader/header 的 `StringBuilder→ToString→WriteAllText` 改为 `StreamWriter`/`JsonTextWriter` 直写 `File.OpenWrite`（`Exporter.cs:69-77/273-345/457-460`、`AnimationClipExtensions.cs:159-169`、`ShaderConverter.cs:48-244`）。消除全量字符串双三重缓冲。
2. **资产图构建增量序列化。** `AssetsHelper.BuildAssetMap/BuildBoth` 改为每文件增量写出（MessagePack 分段 / JSON streaming），不在内存累积全量 `List<AssetEntry>`。百万级条目时收益最大。
3. **容器解压减少双缓冲。** `BundleFile.ReadFiles` 评估让 StreamFile 直接切片引用 `blocksStream`（OffsetStream 包装）而非各自 `new MemoryStream` 复制一份；Zstd/UnityWeb 分支消除 `ToArray()` 中间拷贝（直接 `blocksStream.Write(span)`）。
4. **FBX/纹理导出流式化。** ModelConverter 纹理转换完一张即写盘/喂原生并释放，勿全部驻留 TextureList；TypeTreeHelper Dump 走 `ReadTypeString` 直写不建装箱 DOM。
5. **重复解码缓存。** SpriteHelper 按 atlas 缓存解码图，避免 N sprite 解码 N 次。

## 验证方式（本次不执行，供后续实施时参考）

研究阶段不需要验证，但实施任一优化后应：

1. **构建：** `dotnet build AnimeStudio.CLI -c Release`（已迁 net10，可在 Debian 直接构建）。
2. **内存基线对比：** Linux 上跑同一批大 bundle，`/usr/bin/time -v dotnet AnimeStudio.CLI.dll ...` 读 `Maximum resident set size`（峰值 RSS），优化前后对比。重点看处理单个最大 .blk/.mhy 容器时的峰值。
3. **配置类验证（第 1 优先级）：** 设 `DOTNET_gcServer`/`DOTNET_GCHeapHardLimitPercent`/`MALLOC_ARENA_MAX` 后用 `dotnet-counters monitor -p <pid>` 观察 `gc-heap-size`/`gen-2-gc-count`/`loh-size`，确认堆封顶生效、LOH 不再单调增长。
4. **泄漏类验证（FBX filter、Clear 完整性）：** `dotnet-gcdump` 在批处理中段和多轮后各抓一次堆快照对比；原生侧用 `valgrind --leak-check=full` 或 `heaptrack` 跑含动画 FBX 批量导出，确认 `FbxAnimCurveFilterUnroll` 不再累积。
5. **回归：** 用 `scripts/AnimeStudio.Core.Smoke/` 冒烟项目确认解析正确性不被流式化破坏。

---

# 第二部分：下一步开发计划（指引实施）

> 上方为研究报告，以下把路线图转成可执行分阶段任务。按"投入产出比 + 风险"排序，每阶段独立可交付/验证/回滚，建议每阶段一个 PR。**待批准后实施。**

## 实施前置准备（所有阶段共用）

1. **建立内存基线。** 实施前在 Debian 目标机跑代表性负载（最大 ZZZ .blk + 一批含动画 FBX 导出），`/usr/bin/time -v` 记 `Maximum resident set size`，`dotnet-counters monitor` 记 `gc-heap-size`/`loh-size`/`gen-2-gc-count`。后续每阶段对比锚点。
2. **冒烟回归基线。** 跑 `scripts/AnimeStudio.Core.Smoke/` 记录当前通过状态，作为功能不退化判据。
3. **分支策略。** 当前 `master` 有未提交加固 diff（正向，见研究报告 A 节）。建议先单独提交它，再从干净基线起每阶段开新分支。

## 阶段一：运行时/GC 配置加固（纯配置，零业务代码，最高杠杆）

**风险：低**（不碰 C# 逻辑，可随时回滚）。**目标：** 加堆硬上限 + 控原生 RSS + 锁 GC 模式，把"被 OOM killer 静默 SIGKILL"转为"可控回收/可捕获异常"。

### 1.1 新增 `AnimeStudio.CLI/runtimeconfig.template.json`（当前不存在，已核实）

- `System.GC.Server=false` —— 负载几乎单线程（仅 `AssetsHelper.cs:114` AsParallel、`:651` Task.Run），Server GC 多堆+按核预留+高阈值会抬高 16GB 机峰值 RSS，不利。显式锁 Workstation。
- `System.GC.Concurrent=true` —— 后台并发回收降暂停。
- `System.GC.HeapHardLimitPercent`≈70（约 11GB 封顶）—— **关键防 OOM 阀**：超限抛可捕获 `OutOfMemoryException`（被 `Program.cs:196` per-file try/catch 接住、跳过该文件继续），而非进程被内核 SIGKILL 致整批中断、数据不落盘。
- `System.GC.RetainVM=true` —— 保留已归还段减少反复申请释放（视实测）。
- **决策点（需实测）：** HeapHardLimitPercent 太低会让本可完成的大容器提前 OOM，太高失去保护。从 75 起观察是否有正常容器触发 OOM 再下调。

### 1.2 修改 `scripts/package-deb.sh:86-89` 包装脚本注入环境变量

当前是裸 `exec dotnet ... "$@"` 无任何环境变量。在 exec 前导出（用 `${VAR:-default}` 形式允许外部覆盖）：

- `MALLOC_ARENA_MAX=2`（甚至 1）—— Oodle/FBX/ACL/FMOD 原生分配走 glibc malloc，多线程 arena 膨胀可额外吃数百 MB~2GB 不回收，限制后压低 50-70%。
- `MALLOC_TRIM_THRESHOLD_=131072` —— 促空闲堆归还内核降 RSS。
- 可选 `DOTNET_GCConserveMemory=5`（0-9）—— 用 CPU 换更低 RSS、更积极 LOH 归还。
- 附带 systemd service 模板示例设 `MemoryMax=14G`，让 .NET 经 cgroup v2 感知上限。

### 1.3 验证（阶段一）

构建确认 runtimeconfig.json 含 `System.GC.*`；`dotnet-counters` 跑最大容器确认堆封顶、loh-size 不无限涨；喂超大/损坏 bundle 确认触发可捕获 `OutOfMemoryException` 而非进程消失；RSS 对比基线确认 `MALLOC_ARENA_MAX` 生效。

## 阶段二：局部 bug 与泄漏修复（小改动、低-中风险）

修审计确认的真实 bug 和原生泄漏，均几行级、不改架构。

### 2.1 修 MhyFile 临时文件兜底死代码 bug
`MhyFile.cs:162` `(int)m_BlocksInfo.Sum(...)` 先 long→int 强转致 `:164 >= int.MaxValue` 永假，2GB+ 容器恒走 `new MemoryStream` 必 OOM。改 `long uncompressedSizeSum = m_BlocksInfo.Sum(x => (long)x.uncompressedSize);`，与正确的 `BundleFile.cs:264` 对齐。**Blb/Hyg/VFS 的 CreateBlocksStream 同型一并核查。**

### 2.2 修原生 FBX filter 泄漏
`asfbx_anim_context.cpp:8` `new FbxAnimCurveFilterUnroll()`，但 `.h:30 ~AsFbxAnimContext()=default`、`api.cpp:813 delete` 只跑默认析构 → 每次导出含动画 FBX 泄漏一个原生 filter。改：`.h` 声明 `~AsFbxAnimContext();`，`.cpp` 实现 `{ delete lFilter; }`（构造已初始化 nullptr，安全）。需重构 FBXNative 原生库（cmake）。

### 2.3 补 ACL/DBACL Dispose 健壮性
`ACL.cs:35-41`（ACL/SRACL）`DisposeNative` 在 `Marshal.Copy` 后调用、无 try/finally，Copy 抛异常则原生缓冲泄漏；`DBACL:144-157` 同。改：`values/times` 赋值 + Dispose 包 try/finally，Dispose 放 finally；`ValuesCount/TimesCount` 加非负+上界校验。**对齐位移段（:109/:113）不必改**（验证者确认 glibc 16 字节对齐使越界不触发）。

### 2.4 跨轮静态累积按文件 flush
- `scrapeMonos` 三列表（`Studio.cs:34-36`，唯一真 leak 类，仅该模式触发）：HashSet 去重 + 每文件增量追加写，处理完清空内存列表（消除整轮累积 + 末尾 `.Distinct().OrderBy()` 二次复制）。
- `StringCache`（`AssetMap.cs:10-25`）：资产图模式末尾显式清理。

### 2.5 补 diff 加固残留
`NapAssetBundleIndexAsset.cs:22-43`（4 处）、`Avatar.cs:203/362/368` 的 `new List<T>(reader.ReadInt32())` 绕过 ValidateLength。改 `reader.ReadArrayLength(elementWireSize,...)` 先校验，或 `new List<T>()` 不预设容量。

### 2.6 验证（阶段二）
构造 >2GB Mhy 头确认走临时文件；`heaptrack`/`valgrind` 跑动画 FBX 多轮确认 filter 不累积；喂坏 ValuesCount 确认异常时不泄漏；冒烟全绿。

## 阶段三：结构性流式化（大改动、收益大、需回归保障）

**风险：中-高**（改解析/导出热路径，必须有冒烟回归 + 字节级输出对比兜底）。建议拆成多个独立 PR，每个 PR 只动一类导出器，单独验证输出一致性。

### 3.1 恢复 Clear() 周期性 LOH 压缩（先做，最简单）
`AssetsManager.cs:625-626` 已注释的 `GC.Collect()` 改为：每处理 N 个文件后 `GCSettings.LargeObjectHeapCompactionMode=CompactOnce; GC.Collect();`（用计数器，不必每文件，避免吞吐损失）。直接缓解大解压缓冲释放后 LOH 不归还。属阶段三里风险最低的一项，可提前并入阶段二。

### 3.2 导出路径全面流式写盘
把 `StringBuilder→ToString→WriteAllText` 三重缓冲改为 `StreamWriter`/`JsonTextWriter` 直写 `File.OpenWrite`：
- YAML AnimationClip：`AnimationClipExtensions.cs:159-169` + `Exporter.cs:457-460`
- Mesh OBJ：`Exporter.cs:273-345`
- Shader：`ShaderConverter.cs:48-83`（megashader 全平台/段物化）+ `:86-244`（十余处 helper 返回 string 逐层 Append）
- header：`Exporter.cs:69-77`
**回归判据：** 流式化前后输出文件字节级一致（或语义一致），用基线导出结果 diff。

### 3.3 资产图构建增量序列化
`AssetsHelper.BuildAssetMap/BuildBoth`（`:322-344/521-527/649-716`）改为每文件增量写出（MessagePack 分段 / JSON streaming），不在内存累积全量 `List<AssetEntry>`。百万级条目时收益最大。注意 MessagePack 分段格式需与 `ParseAssetMap` 的读取端兼容。

### 3.4 容器解压减少双缓冲
`BundleFile.ReadFiles`（`:311-339`）评估让 StreamFile 用 `OffsetStream` 切片引用 `blocksStream` 而非各自 `new MemoryStream` 复制；Zstd（`:808`）/UnityWeb（`:279-294`）分支消除 `ToArray()` 中间拷贝，直接 `blocksStream.Write(span)`。**风险点：** StreamFile 生命周期若改为引用 blocksStream，需确保 blocksStream 不在 using 块结束时被释放 —— 这会改变现有所有权模型，需仔细评估，可能不值得。优先做 Zstd/UnityWeb 的 ToArray 消除（低风险）。

### 3.5 FBX/纹理导出与 TypeTree Dump 流式化
- ModelConverter 纹理（`:770-787`）转完一张即写盘/喂原生并释放，勿全部驻留 TextureList；`GetBuffer`+长度替代 `ToArray`。
- TypeTreeHelper Dump（`:166-337`）走 `ReadTypeString` 直写 StringBuilder 不建装箱 DOM；`GetNodes` 传 `(startIndex,count)` 区间不复制。
- SpriteHelper（`:16-37`）按 atlas 缓存解码图，避免 N sprite 解码 N 次 4K 图。

### 3.6 验证（阶段三）
每个 PR：① 基线导出结果与改后字节级/语义 diff，确认输出不变；② RSS 对比确认峰值下降；③ 冒烟全绿。流式化最易引入"输出截断/编码差异"回归，输出对比是硬性门禁。

## 阶段排序与依赖

```
前置准备（基线 + 冒烟）
   │
阶段一（配置）──── 可立即上线，独立收益，无代码依赖
   │
阶段二（bug/泄漏）── 独立，2.1/2.2 是真实正确性 bug，优先级高于阶段三
   │
阶段三（流式化）──── 3.1 可并入阶段二；3.2~3.6 拆多 PR，依赖冒烟回归
```

**建议节奏：** 阶段一 + 阶段二（含 3.1）合并为第一批交付（风险低、收益明确、防 OOM 立竿见影）；阶段三 3.2~3.6 作为后续迭代，逐 PR 推进。

## 风险与回滚

- **阶段一**：配置错误最坏导致 HeapHardLimit 过低、正常容器提前 OOM —— 回滚=删 runtimeconfig 键/还原脚本，零代码风险。
- **阶段二**：2.2 改原生库需重新构建分发；2.1 改 long 比较需确认下游 `(int)` 使用处不受影响。均可单测覆盖。
- **阶段三**：流式化改输出热路径，回归风险最高 —— 强制每 PR 做基线输出 diff，发现不一致立即回滚该 PR。

## 不做的事（验证者驳回，避免误工）

- **不要**因"裸 ReadInt32 作循环计数"去全量改 Classes/（验证者驳回：stream-bounded 读取受切片硬限流，非预分配，无实际风险）。
- **不要**为"依赖闭包递归"做特殊处理（验证者驳回：仅对裸 .assets 文件生效，blk/bundle 工作负载根本不触发该路径）。
- **不要**改 ACL 的对齐位移逻辑（验证者驳回：glibc x64 恒 16 字节对齐，位移恒为 0，越界不触发）。
- **不要**盲目开 Server GC（会抬高该单线程负载的峰值 RSS）。
