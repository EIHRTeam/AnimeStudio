# AnimeStudio 导出性能调查报告

> 2026-06-18 – 2026-06-19，基于 Debian 13（4 核 / 15 GiB RAM）实机 profiling 与代码审查。
> 服务器 `1.14.226.195`，用户 `u202f`，ArknightsEndfield VFS 包（57 GB / 767 `.chk` / 792 文件）。

---

## 1. 背景

上一轮调查确定 Debian 上 AssetMap 仅用到 ~65% CPU / ~55% 内存的根因是
`WorkerBudget.GetMemoryStableWorkerCount`（`WorkerBudget.cs:14-16`）把 `--workers 4` 砍半为 2，
是为守 6,957,772 KiB RSS 门槛的**故意取舍**。

基于此实现了性能配置文件 + 模式：
- `~/.anime/config.json` + `--mode default|limit|fast`
- `default`：保守兼容，砍半 ON，守 RSS 门槛（无配置/无标志逐项 = 今天）
- `limit`/`fast`：预算内优化——**解除砍半、填满预算**（"优化"而非"节流"）
- RAM 为软预算（推导 worker 数/容器阈值），**不动 GC**（`HeapHardLimitPercent=75` 仍是硬天花板）
- 已通过三-RID package smoke + Debian 实机 AssetMap 对比验证

随后用户要求研究**全量 VFS 逐资产导出（Convert dump）的吞吐优化**。由此开启本次深入研究。

---

## 2. AssetMap 实机对比验证

**命令**：`--map_op AssetMap --map_type XML`，固定基线文件 `VFS/7064D8E2/68B3B9B8EB82E88FBFE6A313E6B18FB6.chk`（1,812,594,931 字节 / 283,596 资产）。

| 指标 | `--mode default` | `--mode fast` |
|---|---|---|
| CPU | 131% | **204%** (+73pp) |
| 墙钟 | 10:05 | **7:41** (−24%) |
| 峰值 RSS | 6,811,548 KiB | 6,896,620 KiB |
| Loading | 340,097 ms | 240,888 ms (−29%) |
| Object scanning | 247,384 ms | 203,215 ms (−18%) |
| 退出码 / oom_kill | 0 / 0 | 0 / 0 |
| temp 清空 | ✓ | ✓ |
| XML 规范化 SHA256 | `c563e85f…` | `c563e85f…` (一致) |
| 资产数 | 283,596 | 283,596 |

**结论**：fast 模式按设计工作——CPU +56%、墙钟 −24%、输出不变、RSS 仍在门槛内。
**局限**：单个 VFS 容器下串行 Loading 仍限制上限。

---

## 3. 全量 dump 吞吐验证

**命令**：`--game ArknightsEndfield --mode fast`，输入全 VFS 包（767 chk），输出 `~/dump-test`，600s 后停止。

| 指标 | 值 |
|---|---|
| 运行 | 609s，信号停止 |
| 扫描 | 792 文件 / 767 chk |
| 进度 | **仍停在第 1 个容器**（其含 5,459 资产，约 1,246/5,459 即 23% 完成，0 个 chk 完整结束）|
| 输出 | 1,240 文件 / 6.2 MiB |
| 输出类型 | MonoBehaviour / Sprite / Texture2D / Material / Mesh |
| 峰值 RSS | 1,174,060 KiB（~1.12 GiB，极低，远低于任何门槛）|
| oom_kill | 0 |

**初步观察**：~2 资产/秒 —— **严重偏低**。但在约第 1,244 个资产后进度停止。

---

## 4. 根因定位：并行导出死锁（头号瓶颈）★★★★★

### 4.1 诊断方法

1. `dotnet-trace collect`：在 `--workers 4`（fast）下采 120s 样本，speedscope 总权重 = 0（极少样本），120s 内导出计数增长 = 0。
   → 进程**卡死（阻塞、非匀慢）**。
2. `dotnet-stack report`：在检测到停顿（5 次 5s 无计数增长）时抓全线程栈，立刻看到 **3 个 worker 在 `ExportPathCoordinator.WaitForOrder` -> `Monitor.Wait` 等待**，1 个在 `Exporter.ExportConvertFile` -> `Monitor.Enter_Slowpath` 锁竞争。
3. `--workers 1` 对照：同容器 **2 分钟跑完全部 5,459 个资产**（~45/秒），零停顿。
   → 死锁是**并发专属**（worker ≥ 2 即触发）。

### 4.2 死锁机制（精确代码路径）

**锁序反转死锁**：类型锁先取、再等序号，导致两线程互相等。

```
Exporter.ExportConvertFile (Exporter.cs:696-742)
  ├─ case MonoBehaviour: lock(MonoBehaviourExportSync) {        // (1) 先取 TYPE 锁
  │      ExportMonoBehaviour → ToType/GetRawData → ...         //    读对象数据
  │      → TryExportFile                                        //    预留输出路径
  │        → ExportReservationScope.TryReserveFile
  │          → ExportPathCoordinator.TryReserveFile             //    (2) 再取 COORDINATOR 锁
  │             lock(sync) { WaitForOrder(scope) }              //    (3) WAIT: scope.Ordinal != nextOrdinal
  │  }                                                          //    → 线程在 Monitor.Wait 阻塞，但 STILL HOLDING type lock!
  └─ ...

ExportPathCoordinator.WaitForOrder (ExportPathCoordinator.cs:153-164)
  while (scope.Ordinal != nextOrdinal) { Monitor.Wait(sync); }  // 序号不对 → 等待
  // nextOrdinal 只在 ReleaseOrderAfterInitialReservation / AdvanceOrder(scope)
  // 中推进，需要当前序号持有者完成预留然后 PulseAll
```

**死锁场景（本容器 ~第 1,244 个资产）**：
1. 资产 #1245（MonoBehaviour，高序号）被 worker A 调度到，**先取了 `MonoBehaviourExportSync`**，再进 `WaitForOrder` 等 #1243（当前序号）。
2. 资产 #1243（MonoBehaviour，低序号 = `nextOrdinal`）被 worker B 调度到，**需要 `MonoBehaviourExportSync` 才能预留路径并推进序号**，但锁被 #1245 持有 → `Monitor.Enter` 阻塞。
3. Worker A 等在 `WaitForOrder` 等 #1243 推进 ←→ Worker B 等在 `Monitor.Enter` 等 #1245 释放锁 → **死锁**。
4. 其余 worker 也卡在不同的 `WaitForOrder`（等各自序号），整个 Parallel.For 停止。

**触发条件**：同一类型连续 ≥2 个资产被不同 worker 调度（对于占比 70% 的 MonoBehaviours，几乎必然触发）。

### 4.3 修复建议

**选项 A（推荐）—— 先预留路径、再取类型锁**：
在 `ExportConvertFile` 里把 `TryExportFile(path)` 移到 `lock(typeSync)` 之前，
让每个 worker 拿到同序号锁（`ExportPathCoordinator` 的 `sync`）的 `lock` 下确定自己的序号/路径后才去抢类型锁。
此顺序下类型锁**不持有同时等待序号**，不会反转。

需要稍微改动调用接口——把预留路径（或至少目标目录/扩展名计算）与类型锁保护的转换工作分离。
对 GameObject/Animator（FBX）保留有序导出（`holdOrderUntilDispose`），其余类型仅路径预留受序号约束。

**选项 B（防御性）—— 完全去掉有序语义**：
把 `ExportPathCoordinator` 的 `WaitForOrder`/`holdOrderUntilDispose` 用于纯路径去重的碰撞等待
（无全局序号）——多类型/跨类型不再互锁。实现面更大但彻底消除此问题族。

> 无论选哪个，**当前修复必须在小规模验证前完成**，因为 `--workers 4`（即默认）在全量导出中不可用。
> 临时变通：`--workers 1` 无死锁风险、且对于当前单容器工作负载反而更快（约 120s vs 永远卡住）。

---

## 5. 单 Worker CPU 剖析（死锁排除后的真实转换成本）

在 `--workers 1` 下 `dotnet-trace collect` 120s，`dotnet-trace report topN` 取 exclusive 自耗占比。

### 5.1 单 Worker 类型分布（整容器，5459 资产）

| 类型 | 数量 | 占比 |
|---|---|---|
| **MonoBehaviour** | **3,835** | **70.2%** |
| Texture2D | 670 | 12.3% |
| Sprite | 619 | 11.3% |
| Mesh | 132 | 2.4% |
| Material | 70 | 1.3% |
| AnimationClip | 62 | 1.1% |
| TextAsset | 34 | 0.6% |
| Animator | 31 | 0.6% |
| Font | 6 | 0.1% |

### 5.2 CPU 开销分解（方法级 Exclusive %）

| 方法 | % | 类别 |
|---|---|---|
| `LowLevelLifoSemaphore.WaitNative` | 34.3% | **线程池空闲线程 parked**（非工作：追踪/诊断线程、已完成 worker 的等待）|
| `WaitHandle.WaitOneNoCheck` | 28.6% | **同上**，线程 idle |
| `EventSource.DefineEventPipeEvents` | 15.2% | **追踪开销**（dotnet-trace 自身 = 观察者效应）|
| `KeyValuePair<TKey,TValue>>.GetEnumerator()` | 5.1% | TypeTree DOM 字典遍历（JSON/MonoBehaviour）|
| `RandomAccess.ReadAtOffset` | 4.7% | 文件定位读 I/O |
| `DeflaterEngine.FindLongestMatch` | 3.0% | **PNG Deflate 压缩** |
| `Monitor.Enter_Slowpath` | 2.6% | 锁竞争（`MonoBehaviourExportSync`）|
| `OSFileStreamStrategy.get_SafeFileHandle` | 1.9% | 文件打开/句柄获取 |
| `DeflaterEngine.Deflate` (inclusive) | 3.7%† | **PNG 压缩总开销** |
| `DeflaterHuffman.CompressBlock` | 0.1% | PNG Huffman 压缩 |
| `DeflaterEngine.SlideWindow` | 0.1% | PNG LZ77 滑动窗口 |
| `Texture2DConverter.DecodeTexture2D` | **0.17%** | **原生纹理解码（极低！）** |
| `LZ4.Decompress` | **0.05%** | **容器 LZ4 解压（极低！）** |
| `YAMLMappingNode.Emit` + `ExportYAML` + `Number.FormatFloat` | ~1% | YAML 序列化 |
| `CoreLib!Interop+Sys.Stat` + `Interop+Sys.Open` + `RandomAccess.Write` | ~0.2% | 文件系统 syscall |
| `Buffer.MemmoveInternal` | 0.5% | 内存复制 |
| `GC.AllocateUninitializedArray` | 0.2% | GC 分配 |

† inclusive = 自身 + 子调用总和。

### 5.3 结论

- **逐资产转换本身很轻，真实工作密度约 20%（压缩 + TypeTree + I/O）**，其余是空闲线程 + 追踪开销。
- **原生解码不是瓶颈**（0.17%），管道容器解压不是瓶颈（0.05%）——这与静态分析预期相反。
- **PNG Deflate 压缩是最大的单线程 CPU 耗能**（含 Huffman 约 3.7% incl，占"真正工作"的约 20%）。
- **70% 的资产是 MonoBehaviour**，被 `MonoBehaviourExportSync` 串行化——这对于单 worker 无影响，但对修死锁后的并行是收益天花板。

---

## 6. MonoBehaviour 串行锁分析

### 6.1 为什么需要这把锁

`ExportMonoBehaviour`（`Exporter.cs:122-220`）调用 `m_MonoBehaviour.ToType()` →
`Object.ToType`（`Classes/Object.cs:85-101`）→ `TypeTreeHelper.ReadType`（`TypeTreeHelper.cs:166-183`），
后者执行 `reader.Reset()`（设 `BaseStream.Position = byteStart`，`ObjectReader.cs:142-148`）然后顺序读取。

所有 `Object` 实例共享同一个 `SerializedFile` 的 `reader.BaseStream`。
**两个线程同时写同一个 `Stream.Position` 会导致数据损坏**——这就是 `MonoBehaviourExportSync` 存在的原因。

### 6.2 现成的解决方案（仓库已有）

AssetMap 扫描路径已经解决了完全相同的共享流问题：
- `ObjectReader.SupportsIndependentReading`（`ObjectReader.cs:69-112`）判断是否可创建独立 reader。
- `CreateIndependentReader` 为每个 worker 创建一个独立的流视图（`ReadOnlyRandomAccessStream`/内存副本）。
- `AssetMapObjectWorkerState.GetReader`（`AssetsHelper.cs:891-916`）缓存每个 `(worker, SerializedFile)` 一个 reader。
- 对不可切分的流（非 FileStream/MemoryStream/ReadOnlySliceStream），回退为 `lock(assetsFile.reader)`（`AssetsHelper.cs:600-622`）。

**扩展此模式到导出路径**即可消除 `MonoBehaviourExportSync`（以及潜在的 Texture2D/Sprite 同文件并发冲突），
让 70%（以此容器例 3,835 个）MonoBehaviour 在 `--workers 4` 下可并行导出。

### 6.3 其他共享状态

- `scrapeMonos` 分支写入 `Studio.PathStrings/VOStrings/EventStrings`——`HashSet.Add` 非线程安全。
  改为 per-worker 积累 + Merge，或用 `ConcurrentDictionary`。
- `FbxExportSync`（GameObject/Animator，本容器共 31+？个）有原生库重入理由，
  保持串行但可缩小临界区（仅原生调用）。

---

## 7. 优化建议排序

| # | 优化 | 影响 | 前提 | 风险 | 工作量 |
|---|---|---|---|---|---|
| **1** | **修复并行导出死锁**（先预留路径、再取类型锁）| **消除 600s 全卡死**，使 `--workers 4` > `--workers 1` | 无 | 低（锁序单向变更）| 小 |
| 2 | **MonoBehaviour 导出并行化**（扩展独立 reader 到导出路径）| 70% 资产不再串行 → 多核有效 | 1 已修 | 中（需每对象正确构造 reader）| 中 |
| 3 | **scrapeMonos Set 并发安全**（per-worker 积累 + Merge 或锁）| 避免数据丢失/Set 内部损坏 | 2 进行中 | 低 | 小 |
| 4 | **PNG 编码加速**（降低压缩等级、或换编码器）| 纹理/精灵吞吐提升（~120 Texture2D+110 Sprite/分钟）| 1 已修 | 低（仅尺寸/质量 tradeoff）| 小 |
| 5 | **减少 TypeTree DOM 分配**（boxing 消除，参考 `PERF_ANALYZE_REPO` 高优#4）| 降 `KeyValuePair` 5% / GC 频率 | 1 已修 | 中（需改 TypeTree 模型）| 中 |
| 6 | **Texture2D/Sprite/Material/Mesh 同文件读也改为独立 reader** | 消除理论上同文件跨类型并发冲突 | 1 已修 | 低（同 MB 模式的复用）| 小 |
| 7 | **LOH/GC 常驻回收调优**（`ConserveMemory`、`GCHeapCount`）| 降 GC 停顿（当前总计约 19s）| 门禁达标后 | 中（需 Debian 对照）| 小 |
| 8 | **MonoBehaviour 多文件并行**（当前 Parallel.For 全局抢占，对多 chk 输入有效）| 多容器 dump 时提升 | 1+6 已修 | 低（已在一处）| 小 |

> **注意**：上述排名的核心逻辑是——只有修死锁后多核才有用；只有 MB 并行化后 70% 资产才能"看到"多核；
> 其余优化是对剩余的纹理/精灵/材质/网格的单核 per-asset 微调，有收益但不在死锁修复前追加收益。

---

## 8. 验证环境

| 属性 | 值 |
|---|---|
| 服务器 | `1.14.226.195`（Debian 13, 6.12.90 kernel）|
| CPU | 4 核 x86_64 |
| RAM | 15,615 MiB |
| .NET | 10.0.301 |
| 仓库分支 | `feat/performance-profile`（HEAD `8546725`）|
| 测试数据 | `~/ArknightsEndfield/Endfield_Data/StreamingAssets/VFS`（57 GB / 767 chk / 792 文件）|
| GC 配置 | Workstation GC, Concurrent=true, HeapHardLimitPercent=75, RetainVM=true |
| 工具栈 | `dotnet-trace` 8.0.0（.NET 10 兼容）, `dotnet-stack`, `dotnet-publish linux-x64 --self-contained false`, `/usr/bin/time -v` |

---

## 9. 后续步骤建议

1. **立即修：死锁（#1）**。修复后 `--workers 4` 应可在死锁前的 1,244 个资产后继续推进。
2. **立即验：用同基线 5459-资产容器对比 `--workers 1/2/4` post-fix**，确认修复版并行 > 串行。
3. **中优先：MonoBehaviour 并行化（#2）**。修完后 #6（同文件其他类型）免费可得。
4. **可选：PNG 编码加速（#4）**。若纹理导出量大，降压缩等级或检查当前编码器开销值得做。
5. **延后：TypeTree DOM 去分配（#5）**。MB 计数大但 per-MB 重新构造 DOM 成本随 MB 并行化而分散——单核时不必做；并行后若仍然构成墙钟瓶颈再做。

---

## A. 附录：实机命令与验证脚本

```bash
# AssetMap 对比 (~/perf-profile-run/publish, HEAD 76b5f7d)
for mode in default fast; do
  ANIMESTUDIO_TEMP_DIR=~/perf-profile-run/temp-$mode /usr/bin/time -v \
    ./AnimeStudio.CLI "$IN" ~/perf-profile-run/out-$mode \
    --game ArknightsEndfield --map_op AssetMap --map_type XML \
    --map_name perf-$mode --mode $mode
done

# 死锁检测 (运行时栈, HEAD 8546725)
dotnet-stack report -p $PID > stack.txt
# → WaitForOrder / Monitor.Enter_Slowpath 死锁确认

# 单 worker 干净剖析
ANIMESTUDIO_TEMP_DIR=~/dump-run/t1w ./AnimeStudio.CLI "$VFS" ~/dump-run/out1w \
  --game ArknightsEndfield --workers 1 > log &
dotnet-trace collect -p $PID --duration 00:00:02:00 -o trace.nettrace
dotnet-trace report trace.nettrace topN -n 25
```
