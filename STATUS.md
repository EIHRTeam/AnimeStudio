# Development Status

## Phase 0 - Parser and Memory Safety Baseline

- Status: completed
- Branch: `master`
- Completion commit: `71fdbf9`
- Acceptance: Endfield controller export and memory safety smoke checks passed.

## Phase 1 - CLI-only .NET 10 and Container Streaming

- Status: completed
- Branch: `feat/container-streaming`
- Baseline commit: `71fdbf9`
- Implementation commit: `cf1ad70`
- Acceptance: memory, cleanup, parser, build, package, and normalized output
  compatibility gates passed. Deterministic files match by relative path and
  SHA256; random fallback outputs match by content-hash multiset; FBX payloads
  match after known volatile metadata normalization. Strict deterministic
  fallback naming and FBX metadata are deferred to Phase 5.

### Session 2026-06-13-01

- 本次目标：建立开发账本，移除 GUI/旧 TFM，并实现共享容器流式存储。
- 完成内容：已建立三文档开发流程；已移除 GUI、Patcher、旧 TFM 和旧 Windows-only 构建入口；共享容器存储实现与验证进行中。
- 修改文件/接口：已更新根目录开发文档、解决方案和项目目标框架；下一步新增 `ContainerStorageOptions`、共享后备存储和有界切片接口。
- 验证及指标：CLI-only `.NET 10` 解决方案 Release 构建已通过；流式存储指标待补充。
- 问题与决策：内存稳定优先；256 MiB 作为所有存活容器共享的累计内存预算，超出预算的新后备使用磁盘；输出要求字节一致；容器切片必须独立位置并通过引用计数维持后备生命周期；阶段完成后或实现风险需要时允许在授权 Debian 13 服务器 `1.14.226.195` 实机验证。
- 未完成事项：五类容器接入、临时目录配置、自动测试、三 RID 发布和 Debian 验收。
- 后续注意事项：不得使用 Debian `/tmp` 作为默认后备目录；磁盘不可用时不得回退到内存；同步维护 `CLAUDE.md`。
- 起止提交：`71fdbf9` -> `3831a82`。

### 会话 2026-06-14-01

- 本次目标：完成五类容器流式接入、自动测试、三 RID 发布验证和 Debian 13 实机验收。
- 完成内容：新增共享内存/磁盘后备、严格有界且独立位置的只读切片、引用计数清理和累计 256 MiB 内存预算；Bundle、Mhy、Blb、Hyg、VFS 改为顺序解压到单一后备并以切片交付内部文件；加入临时目录优先级、锁文件、过期目录清理、磁盘余量检查和 CLI 配置；补齐异常、取消、模拟 OOM、并发读取、五类容器哈希和临时文件清理 smoke。
- 修改文件/接口：新增 `ContainerStorageOptions`、`ContainerStorageManager`、`SharedBackingStore`、`ReadOnlySliceStream`、`ContainerFileStreams`；新增 `AssetsManager.ContainerStorageOptions` 和 `AssetsHelper.SetContainerStorageOptions`；更新 CLI 配置、容器读取器、打包脚本及 Core/CLI smoke。
- 验证及指标：`dotnet build AnimeStudio.sln -c Release`、Core smoke、`win-x64`/`linux-x64`/`osx-arm64` 发布及 smoke、`git diff --check` 均通过；用户已在 Debian 安装 `time`、`sysstat`。`68B3...chk` 退出码 0，耗时 692 秒，峰值 RSS 8,559,464 KiB（采样 8,560,240 KiB），临时磁盘峰值 5,283,871,523 字节，读 1,644,511,232 字节、写 5,555,204,096 字节，无内存跳过/OOM。31 个 `.chk` 全目录退出码 0，耗时 1,980 秒，GNU time 峰值 RSS 10,197,212 KiB，临时磁盘峰值 5,283,871,523 字节，读 31,693,094,912 字节、写 49,722,859,520 字节，`oom_kill` 0 -> 0，临时目录清空。触发文件 SHA256 `65b72bfe12149339d716919b8379f7e9346b8c7501250bb4a14328d23277df99` 导出 36 个 AnimatorController，内容哈希集合与基线一致，峰值 RSS 1,952,720 KiB。原参数 fresh 导出退出码 0，耗时 1,021 秒，峰值 RSS 5,498,928 KiB，完成 77,234 个资产/77,146 个文件，无内存跳过、OOM 或 AnimatorController 失败。
- 问题与决策：最初按单容器阈值选择内存时峰值约 11.50 GiB；改为所有存活容器共享累计预算后仍因 109,231 个磁盘流各自的 64 KiB 用户缓冲达到约 11.50 GiB；将临时 `FileStream` 用户缓冲设为 1 后通过硬门槛。全目录仍有 62 个已知 Shader/Mesh/缺失资源加载错误，不属于本阶段。原参数第一次后台运行被外部 `SIGTERM` 中止，续跑仅用于诊断；fresh 单次运行完成。严格树比较中，77,020 个确定性文件路径和 SHA256 完全一致；双方各有 88 个由 `Path.GetRandomFileName()` 产生不同名称但内容哈希集合一致的 JSON；38 个 FBX 因内嵌创建时间和文件标识不同。该结果不能表述为严格字节一致。
- 未完成事项：明确 strict byte identity 与 normalized compatibility 的验收政策；在政策关闭前 Phase 1 不切换到 AssetMap streaming。
- 后续注意事项：10 GiB 硬门槛已通过，但 8 GiB 目标未达到；全目录仅余约 282 MiB 余量，后续改动必须继续实机回归。`MessagePack` 3.1.4 的 NU1903 仍为已知警告。`Unknown ClassIDType 1186182244` 继续按 Roadmap 排除。
- 起止提交：`3831a82` -> `cf1ad70`（另含本会话文档收尾提交）。

### 会话 2026-06-14-02

- 本次目标：修复原参数批量导出中的优化 Animator 缺失 Avatar、无 RID publish 缺少 Linux FBXNative，以及半构造 FBX 上下文终结器崩溃。
- 完成内容：已实现优化 Animator 缺失 Avatar 的导出前置跳过、Linux FBXNative 发布能力探测、半构造 FBX 上下文和导出器的幂等清理、FBX 失败时当前目录恢复；缺失 Avatar 默认日志改为首条 Warning、后续 Verbose；新增 Linux x64 正式发布构建脚本及对应 smoke。代码经 GitHub 推送并由 Debian 服务器拉取精确提交完成最终验证。
- 修改文件/接口：更新 `Exporter`、`PlatformCapabilities`、`ModelConverter`、`FbxExporterContext`、`FbxExporter`、`Fbx` 和 CLI smoke；新增根目录 `build-linux-release.sh`；更新 `AGENTS.md`、`ROADMAP.md`、`CLAUDE.md`、`README.md`，要求远程验证优先通过 GitHub 精确提交交付，并将全量 Convert 内存优化列入 Phase 4。
- 验证及指标：本地 Release 构建、Core smoke、三 RID 发布与静态 smoke、macOS ARM64 动态原生 smoke、脚本语法和 `git diff --check` 均通过。Debian 从 GitHub 拉取 `b1db294304f2ed254202ba51b425e0946edcdebb` 后，根脚本构建、Core smoke、Linux 动态原生 smoke 均通过；最终归档 SHA256 `f6d08e82ee512418e9bfdcc0a74e5d45d57f7fd9b3508e63d5bec071b187ef7b`。最终定向回归退出码 0，耗时 109.96 秒，峰值 RSS 3,222,448 KiB，`oom_kill` 0 -> 0，临时目录清空；三个缺失 Avatar 名称仅产生一条 Warning，正常 Animator 生成两个 FBX，无 FBXNative、Animator 导出错误、Unhandled exception 或 Aborted。
- 问题与决策：缺失 Avatar 属于单资产数据不完整，应受控跳过；仓库已有 Linux FBXNative，问题是无 RID publish 未复制；原生加载失败不得由终结器二次异常终止进程。新增 GitHub 优先规则前的首轮服务器验证通过临时归档传输，仅用于诊断，不作为发布溯源。GitHub 拉取版本的用户原命令完整处理 31/31 个 `.chk`，退出码 0，耗时 7:39:51，生成 1,161,481 个文件、57,205,453,928 字节，临时目录清空，`oom_kill` 0 -> 0，无本次目标崩溃；该运行峰值 RSS 11,474,792 KiB，超过 10 GiB，但属于全量 Convert 导出路径，容器-only 31 文件门槛仍为已通过的 10,197,212 KiB。全量 Convert 峰值转入 Phase 4，不与容器-only 指标混写。
- 未完成事项：Phase 1 仍需明确 strict byte identity 与 normalized compatibility 验收政策；Phase 4 需降低全量 Convert 导出峰值。
- 后续注意事项：Linux 正式版必须使用 RID 发布包或根目录 `build-linux-release.sh`，不得部署无 RID 的 `bin/Publish/net10.0`；保持单对象失败继续批处理；不得吞掉所有 native 异常；远程代码交付默认走 GitHub，直传例外必须记录原因。完整回归仍有 2,690 条既有资产级 MonoBehaviour/Shader/Mesh/缺失资源错误，不属于本次 FBX 崩溃修复。
- 起止提交：`4f0ca6b` -> `b1db294`（另含本会话文档收尾提交）。

### 会话 2026-06-14-03

- 本次目标：在 CLI 成功完成后输出英文运行总结，包括总耗时、输出目录、最终输出文件数与大小，以及解包前输入总大小。
- 完成内容：新增英文 `Run summary`，成功路径最后输出总耗时、绝对输出目录、输入文件数与解包前总大小、最终输出文件数与总大小；时间采用总小时且不按 24 小时回绕的 `hh:mm:ss (12345s)` 格式，大小同时显示 IEC 可读值和精确字节数。
- 修改文件/接口：新增内部 `FileTreeStatistics` 和 `RunSummary`；`Program.Run` 在原始输入扫描后记录输入统计，并在所有导出和 scraped strings 收尾后流式统计输出目录；CLI package smoke 新增时间、大小、嵌套目录统计和真实命令尾部输出断言。
- 验证及指标：`dotnet build AnimeStudio.sln -c Release`、Core smoke、macOS ARM64 动态 package/runtime smoke、Linux x64 与 Windows x64 跨平台 package smoke、`git diff --check` 和活跃文件旧 TFM/GUI 引用扫描均通过。最小实际命令确认最后输出输入 1 文件/3 bytes、输出 0 文件/0 bytes，退出码 0。
- 问题与决策：输出统计按命令结束时输出目录的最终文件树计算，包含预先存在的文件；输入大小按开始处理前扫描到的原始输入文件总大小计算；目录统计使用 `Directory.EnumerateFiles`，不保存完整路径列表。统计不可用时总结显示 `unavailable`，不得把已成功的资产导出改判为失败。
- 未完成事项：无。
- 后续注意事项：超大输出目录的最终统计需要额外完整遍历，其耗时计入总耗时；总结必须保持为成功命令的最后一组输出。
- 起止提交：`2100e8b` -> `05a0426`（另含本会话文档收尾提交）。

### 会话 2026-06-14-04

- 本次目标：从 `PERF_ANALYZE_REPO.md` 中筛选与当前下一步工作直接相关的性能优化方向，形成独立参考文档。
- 完成内容：新增下一步性能优化参考，确定 Phase 2 优先移除全量 `List<AssetEntry>`、使用磁盘后备条目 spool 并增量读写 XML/JSON/MessagePack；补充实测基线、清理、兼容性和 Debian 验收要求；将 MMF、GC、并行化、SIMD、原生库与系统调优降为有 profiling 触发条件的后续实验。
- 修改文件/接口：新增 `PERF_NEXT_STEP.md`；不修改运行时接口。
- 验证及指标：已逐节核对性能报告、当前代码和 Roadmap 阶段边界；`git diff --check` 通过。纯文档变更，未运行构建。
- 问题与决策：Phase 1 仍须先关闭输出兼容策略；后续最近的性能工作包仍是 Phase 2 AssetMap 流式化。明确拒绝当前直接把所有 file-backed `FileStream` 缓冲从 1 字节放大到 64 KiB，也不在缺少运行时证据时切换 Server GC、引入 MMF 或并行解析。
- 未完成事项：无；Phase 1 关闭后应以本文为输入整体替换 `PLAN.md`，再开始 Phase 2 实现。
- 后续注意事项：本文不替代三份权威开发文档；`PERF_ANALYZE_REPO.md` 的倍率均为静态估算，后续只能引用实测收益；MessagePack 流式化必须先固定旧格式 fixture 和兼容政策。
- 起止提交：`55eba6e` -> `9fce159`（另含本会话文档收尾提交）。

### 会话 2026-06-14-05

- 本次目标：明确并关闭 Phase 1 输出兼容策略，完成最终门禁。
- 完成内容：正式采用 normalized compatibility；同步 `ROADMAP.md`、`PLAN.md` 和本状态，将 Phase 1 标记完成并建立 Phase 2 AssetMap streaming 的详细计划。
- 修改文件/接口：仅更新权威开发文档，不修改运行时接口。
- 验证及指标：`dotnet build AnimeStudio.sln -c Release`、Core smoke、`win-x64`/`linux-x64`/`osx-arm64` 发布与 package smoke、`git diff --check` 和活跃文件旧 TFM/桌面项目引用扫描均通过；本次隔离发布临时目录已删除，默认 AnimeStudio 临时根目录不存在，无残留。构建仍有已知 `MessagePack` 3.1.4 NU1903。
- 问题与决策：采用与 Roadmap 阶段边界一致的 normalized compatibility；确定性输出要求相对路径和 SHA256 一致，随机 fallback 输出按内容哈希多重集比较，FBX 按移除已知易变元数据后的 payload 比较；稳定 fallback 命名和 FBX 元数据确定性留在 Phase 5。不得将该结果表述为严格树字节一致。
- 未完成事项：无；下一会话在 `feat/asset-map-streaming` 开始 Phase 2。
- 后续注意事项：Phase 2 MessagePack 变更前必须固定旧 writer fixture 和兼容行为；继续保留现有容器与全量 Convert 内存基线。
- 起止提交：`1a24296` -> `a0a7176`（另含本会话文档收尾提交）。

## Phase 2 - Asset Map Streaming

- Status: active
- Branch: `feat/asset-map-streaming`
- Baseline commit: `c846a7b`
- Acceptance: 见 `PLAN.md`；尚未开始。

### 会话 2026-06-14-06

- 本次目标：开始 Phase 2，固定旧 AssetMap 格式兼容 fixture，并实现可重复有界枚举的磁盘后备条目 spool 基础。
- 完成内容：抽取通用 `TemporaryFileWorkspace`，使容器后备和 AssetMap spool 共用临时根目录解析、进程锁、过期目录清理、1 GiB 磁盘余量和显式失败行为；新增版本 1 的 `AssetMapEntrySpool`，支持长度分隔记录、64 位计数、封口、重复有界枚举和自动清理；由旧 writer 直接生成 XML/JSON/MessagePack fixture 并锁定 SHA256。
- 修改文件/接口：新增内部 `TemporaryFileWorkspace` 和 `AssetMapEntrySpool`；`ContainerStorageManager` 委托通用工作区但保留容器文件 1 字节缓冲、累计内存预算和引用计数；`AssetsHelper.ExportAssetsMap` 放宽为程序集内部测试入口；Core smoke 新增 fixture 更新/验证、spool 重复枚举、空字段、尾随损坏和清理覆盖。
- 验证及指标：`dotnet build AnimeStudio.sln -c Release`、Core smoke、`win-x64`/`linux-x64`/`osx-arm64` 发布与 package smoke、`git diff --check` 和活跃文件旧 TFM/桌面项目引用扫描均通过；默认 AnimeStudio 临时根目录无残留。fixture SHA256：XML `f4679e2c46a3fa7a0979c2193ccd293619eca7ad8db92009876fc9519bad92b2`，JSON `307cbb53304b209c5f90c5a2cf377d8bed28821cb18cdac13029bc2a2cbbf260`，MessagePack `0d63e476db696fb74a23804052ba8ce43d037502f155dbf77bc6dbf834eeeba5`。
- 问题与决策：第一批改动不替换生产 writer，也不接入 `BuildAssetMap`；JSON 和 MessagePack 新生成结果必须与 fixture 逐字节一致，XML 因旧格式包含输出路径和 UTC 创建时间，仅在明确规范化这两个属性后比较结构。spool 对单字符串设 16 MiB、单记录设 64 MiB 硬上限，并按 64 MiB 写入窗口复查磁盘余量。
- 未完成事项：增加阶段计时并建立 Debian AssetMap 基线；将构建路径接入 spool；实现容器二次解析和 XML/JSON/MessagePack 流式 writer/reader；补齐取消、模拟 OOM 和磁盘失败验收。
- 后续注意事项：`StringCache` 仍可能在枚举大量唯一字符串时形成全局保留，接入生产路径时必须限定每次 pass 的缓存生命周期；MessagePack 3.1.4 仍有已知 NU1903；在 fixture 原型通过前不得替换当前 LZ4 block-array writer。
- 起止提交：`c846a7b` -> `3b4603f`（另含本会话文档收尾提交）。

### 会话 2026-06-14-07

- 本次目标：为 Phase 2 AssetMap 基线增加阶段级计时，覆盖加载、对象扫描、容器解析、过滤/写入 spool 预留阶段及各格式 writer，且不改变 map 文件语义。
- 完成内容：新增单调时钟驱动的 AssetMap 阶段指标，累计加载、对象扫描、容器解析、过滤/spool 预留阶段及 XML、JSON、MessagePack writer 耗时和 pass 数；未选择或因失败未执行的阶段明确显示 `not run`；`BuildAssetMap` 和 `BuildBoth` 均在结束或失败时输出摘要。修复内部静默加载抛异常后未恢复全局日志/进度状态的问题，使 parse failure 的警告和计时可见。
- 修改文件/接口：新增内部 `AssetMapBuildMetrics` 和 `AssetMapBuildStage`；`AssetsHelper.LoadFiles`、`BuildAssetMap`、`BuildBoth`、`ExportAssetsMap` 接收并记录指标；`AssetsManager.LoadFiles`/`LoadFolder` 用 `finally` 恢复调用前 silent 状态；Core/CLI smoke 增加累计计时、真实 writer、失败状态恢复和最终摘要顺序覆盖。
- 验证及指标：`dotnet build AnimeStudio.sln -c Release`、Core smoke、`osx-arm64` 动态 package/runtime smoke、`linux-x64`/`win-x64` 跨平台 package smoke、`git diff --check` 和活跃文件旧 TFM/桌面项目引用扫描均通过。legacy fixture 哈希保持 XML `f4679e2c46a3fa7a0979c2193ccd293619eca7ad8db92009876fc9519bad92b2`、JSON `307cbb53304b209c5f90c5a2cf377d8bed28821cb18cdac13029bc2a2cbbf260`、MessagePack `0d63e476db696fb74a23804052ba8ce43d037502f155dbf77bc6dbf834eeeba5`；默认临时根目录无残留，隔离发布目录已删除。
- 问题与决策：计时仅增加 Info 级诊断，不修改 XML、JSON、MessagePack schema、序列化选项或输出字节；加载 pass 数包含一次 split 预处理和每个输入文件一次加载，便于固定命令横向比较。无效输入 smoke 暴露的 silent 状态泄漏必须修复，否则失败路径会吞掉基线诊断。
- 未完成事项：提交并推送精确验证版本后，在 Debian 13 运行固定 AssetMap 命令，记录 wall time、峰值 RSS、GC、临时磁盘、最终 map 和 `iostat` 基线；随后把生产构建路径接入 spool，并实现流式 writer/reader。
- 后续注意事项：当前仍保留全量 `List<AssetEntry>`，本次只建立测量边界；`StringCache` 的 pass 生命周期约束仍需在 spool 接入时落实；MessagePack 3.1.4 的 NU1903 仍为已知警告。
- 起止提交：`93685d6` -> `d6cd765`。

### 会话 2026-06-14-08

- 本次目标：持续推进并完整关闭 Phase 2，逐项实现和验证生产 AssetMap 有界构建、三格式流式读写、异常清理以及 Debian 13 基线与回归验收。
- 完成内容：会话 07 的计时改动已以 `d6cd7652ffa8f1ac8d797119270f7142b33994f5` 提交并推送，Debian 13 从 GitHub 检出该精确提交并完成固定 AssetMap 基线。生产构建现使用 unresolved/resolved 两阶段版本化磁盘 spool；每个顶层输入结束或异常时释放 `AssetsManager`、对象字典和 `StringCache`。XML、JSON、MessagePack writer 均按条目流式输出；JSON/XML reader 按条目解析，MessagePack reader 将 LZ4 block-array 有界解压到临时工作区后流式筛选。旧 GUI Asset Browser 专用的 `ResourceMap` 和 CLI 中未使用的重复 writer 已删除。
- 修改文件/接口：新增内部 `AssetMapEntryRecord` 和 `AssetMapStreamingIO`；扩展 `AssetMapEntrySpool` 为不经过全局字符串缓存的可重复记录枚举并加入测试故障注入；更新 `AssetsHelper`、`AssetsManager.Clear`、Core smoke 和 CLI 遗留代码。MessagePack writer 固定匹配 3.1.4 的 sequence 分段和 LZ4 block-array 字节；格式依赖版本不满足时显式失败，不静默改变格式。
- 验证及指标：Debian 基线固定命令为 `ANIMESTUDIO_TEMP_DIR=<run>/temp ./AnimeStudio.CLI <68B3B9B8EB82E88FBFE6A313E6B18FB6.chk> <run>/output --game ArknightsEndfield --map_op AssetMap --map_type MessagePack,XML,JSON --map_name phase2-baseline`。精确输入 1,812,594,931 字节，退出码 0，wall 11:22.47，峰值 RSS 6,957,772 KiB；283,596 条资产，Loading 456,177.784 ms、Object scanning 217,843.369 ms、Container resolution 0.438 ms、Filtering/spooling 41.651 ms、XML 943.054 ms、JSON 1,119.385 ms、MessagePack 478.134 ms。`System.Runtime` 每秒 counters 汇总分配 40,195,711,968 字节，Gen0/1/2 GC 3573/1194/12 次，GC pause 19.018868 秒，最大 committed managed memory 6,877,892,608 字节，最大 working set 7,011,201,024 字节。临时磁盘峰值 5,283,871,523 字节；`vda` 684 个样本平均读 4,457.81 KiB/s、写 9,612.96 KiB/s、read await 0.814 ms、write await 1.816 ms、queue 0.847、util 16.291%，最大 util 95.6%。输出 JSON 122,482,551 字节/SHA256 `1c57f69e2e956eb111751ed83ca7d1a4d865e5a3268001cae74bbab50eff82e2`，MessagePack 15,932,259 字节/SHA256 `76a4fa163604ef4e0771366803c816eaee7f7d4ad20fe5a30cb68a6d7ce0a003`，XML 113,161,179 字节，规范化 `filename`/`createdAt` 后 SHA256 `cfb169161a7e3d9495eccbe80b65f196fb9a9874851ad29befeaed1eba9b6d41`；临时目录清空，`oom_kill` 0 -> 0。本地 `dotnet build AnimeStudio.sln -c Release`、Core smoke、macOS ARM64 动态 package/runtime smoke、Linux x64 与 Windows x64 跨平台 package smoke、`git diff --check` 和活跃项目旧 TFM/桌面引用扫描均通过。Core smoke 覆盖旧 fixture 哈希、4,096 条含 40 KiB 字符串的 JSON/MessagePack 字节差分、XML 规范化等价、20,000 条唯一字符串 synthetic map、重复 spool pass、三格式 reader/filter/source 顺序、大小写兼容、取消、解析失败、模拟磁盘失败和模拟 OOM 清理；默认临时根目录无残留。
- 问题与决策：完成判定必须逐项具备自动化和 Debian 实机证据；基线与回归均须通过 GitHub 上的精确提交交付，不以未提交工作树或直接传输作为发布溯源。MessagePack 3.1.4 的 LZ4 block-array 字节受 sequence 分段影响，因此实现和大尺寸差分测试同时固定该依赖；现有 `NU1903` 仍为已知警告。`ResourceMap` 的历史调用者仅存在于已移除 GUI，保留它会继续提供进程级完整 `List<AssetEntry>`。
- 未完成事项：提交并推送最终精确验证版本；按同一 Debian 固定命令复测内存、GC、磁盘、输出哈希和清理；重跑 container-only/full Convert 内存门禁；所有实机标准通过后关闭 Phase 2 文档。
- 后续注意事项：不得修改或提交用户的未跟踪 `PERF_ANALYZE_REPO.md`；Debian 最终输出必须与基线 JSON/MessagePack 哈希一致，XML 仅规范化 `filename`/`createdAt` 后比较。
- 起止提交：`d6cd765` -> 当前工作树（进行中）。
