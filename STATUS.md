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
- 完成内容：会话 07 的计时改动已以 `d6cd7652ffa8f1ac8d797119270f7142b33994f5` 提交并推送，Debian 13 从 GitHub 检出该精确提交并完成固定 AssetMap 基线。生产构建现使用 unresolved/resolved 两阶段版本化磁盘 spool；每个顶层输入结束或异常时释放 `AssetsManager`、对象字典和进程级 `StringCache`。XML、JSON、MessagePack writer 均按条目流式输出；JSON/XML reader 按条目解析，MessagePack reader 将 LZ4 block-array 有界解压到临时工作区后流式筛选。旧 GUI Asset Browser 专用的 `ResourceMap` 和 CLI 中未使用的重复 writer 已删除。首次最终候选实机比较发现旧格式还依赖 `StringCache` 的跨字段 32 位 CRC 首值复用语义；现已增加作用域内磁盘后备 CRC 缓存，在不恢复进程级字符串保留的前提下复现该兼容行为。第二次候选进一步暴露 MessagePack 3.1.4 在大序列中受 `ConfigurableArrayPool` 每 bucket 100 个未归还租赁影响；流式 writer 现模拟相同的 32 KiB -> 64 KiB -> 未池化 32 KiB 段序列，同时实际只保留一个可复用缓冲区。
- 修改文件/接口：新增内部 `AssetMapEntryRecord`、`AssetMapStreamingIO` 和 `AssetMapStringCache`；扩展 `AssetMapEntrySpool` 为不经过全局字符串缓存的可重复记录枚举并加入测试故障注入；更新 `AssetsHelper`、`AssetsManager.Clear`、Core smoke 和 CLI 遗留代码。磁盘字符串缓存使用固定 1,048,576 bucket 的 8 MiB 头表、磁盘索引/值文件及 32 MiB/65,536 项上限的 LRU，扫描和容器解析结束后立即释放。MessagePack writer 固定匹配 3.1.4 的 sequence 分段和 LZ4 block-array 字节；格式依赖版本不满足时显式失败，不静默改变格式。
- 验证及指标：Debian 基线固定命令为 `ANIMESTUDIO_TEMP_DIR=<run>/temp ./AnimeStudio.CLI <68B3B9B8EB82E88FBFE6A313E6B18FB6.chk> <run>/output --game ArknightsEndfield --map_op AssetMap --map_type MessagePack,XML,JSON --map_name phase2-baseline`。精确输入 1,812,594,931 字节，退出码 0，wall 11:22.47，峰值 RSS 6,957,772 KiB；283,596 条资产，Loading 456,177.784 ms、Object scanning 217,843.369 ms、Container resolution 0.438 ms、Filtering/spooling 41.651 ms、XML 943.054 ms、JSON 1,119.385 ms、MessagePack 478.134 ms。`System.Runtime` 每秒 counters 汇总分配 40,195,711,968 字节，Gen0/1/2 GC 3573/1194/12 次，GC pause 19.018868 秒，最大 committed managed memory 6,877,892,608 字节，最大 working set 7,011,201,024 字节。临时磁盘峰值 5,283,871,523 字节；`vda` 684 个样本平均读 4,457.81 KiB/s、写 9,612.96 KiB/s、read await 0.814 ms、write await 1.816 ms、queue 0.847、util 16.291%，最大 util 95.6%。输出 JSON 122,482,551 字节/SHA256 `1c57f69e2e956eb111751ed83ca7d1a4d865e5a3268001cae74bbab50eff82e2`，MessagePack 15,932,259 字节/SHA256 `76a4fa163604ef4e0771366803c816eaee7f7d4ad20fe5a30cb68a6d7ce0a003`，XML 113,161,179 字节，规范化 `filename`/`createdAt` 后 SHA256 `cfb169161a7e3d9495eccbe80b65f196fb9a9874851ad29befeaed1eba9b6d41`；临时目录清空，`oom_kill` 0 -> 0。本地 `dotnet build AnimeStudio.sln -c Release`、Core smoke、macOS ARM64 动态 package/runtime smoke、Linux x64 与 Windows x64 跨平台 package smoke、`git diff --check` 和活跃项目旧 TFM/桌面引用扫描均通过。Core smoke 覆盖旧 fixture 哈希、4,096 条含 40 KiB 字符串的 JSON/MessagePack 字节差分、超过 10 MiB canonical payload 且跨越 32/64 KiB pool exhaustion 的 MessagePack 字节差分、XML 规范化等价、20,000 条唯一字符串 synthetic map、重复 spool pass、三格式 reader/filter/source 顺序、大小写兼容、取消、解析失败、模拟磁盘失败和模拟 OOM 清理；默认临时根目录无残留。
- 问题与决策：`beb212b4628d73ed7a93ef4e16706cddccbc784c` 已通过 Debian 构建、Core smoke、Linux 动态 package smoke和固定 AssetMap 内存门槛，但不是合格最终版本：wall 11:26.91、峰值 RSS 6,745,384 KiB（比基线低 212,388 KiB）、临时峰值 5,347,346,518 字节、临时目录清空、`oom_kill` 0 -> 0；然而固定输入中 96 条记录受旧 CRC 碰撞影响，导致 JSON/MessagePack/XML 大小和哈希均不兼容。`3b0d7a15ce2308181c946c8305edbefa247261f2` 修复 CRC 语义后，wall 11:24.51、峰值 RSS 6,884,672 KiB（仍比基线低 73,100 KiB）、临时峰值 5,376,502,926 字节、临时目录清空、`oom_kill` 0 -> 0，JSON 与基线逐字节一致且 XML 规范化一致；但 MessagePack 因旧 writer 的 pool exhaustion 形成 2,254 个 compression blocks、流式 writer 形成 2,355 个 blocks，仍不兼容。两个候选均明确判定失败，不能用部分门槛通过覆盖格式门槛。完成判定必须逐项具备自动化和 Debian 实机证据；基线与回归均须通过 GitHub 上的精确提交交付，不以未提交工作树或直接传输作为发布溯源。MessagePack 3.1.4 的 LZ4 block-array 字节受 sequence 分段影响，因此实现和大尺寸差分测试同时固定该依赖；现有 `NU1903` 仍为已知警告。`ResourceMap` 的历史调用者仅存在于已移除 GUI，保留它会继续提供进程级完整 `List<AssetEntry>`。
- 未完成事项：提交并推送 MessagePack pool exhaustion 分段兼容修正版；按同一 Debian 固定命令重新验证内存、GC、磁盘、基线输出哈希和清理；重跑 container-only/full Convert 内存门禁；所有实机标准通过后关闭 Phase 2 文档。
- 后续注意事项：不得修改或提交用户的未跟踪 `PERF_ANALYZE_REPO.md`；Debian 最终输出必须与基线 JSON/MessagePack 哈希一致，XML 仅规范化 `filename`/`createdAt` 后比较。
- 起止提交：`d6cd765` -> 当前工作树（进行中）。

### 会话 2026-06-15-01

- 本次目标：在关闭 Phase 2 前修复 CLI 长时间仅使用单个 CPU 核心的问题，使容器流式读取/解压、对象解析和非 FBX 导出默认使用多核，同时保留输出兼容、内存门槛和异常隔离，并形成后续阶段复用的有界调度基础。
- 完成内容：已停止旧串行实现的 Debian 全量 Convert 验收重跑；确认根因是 `Program.Run`、`AssetsManager.ReadAssets`、`BuildAssetMapFile` 和 `Studio.ExportAssets` 的嵌套串行循环，原 `Task.Run(...).Wait()` 仅移动串行工作。新增默认取进程可见逻辑 CPU 数的 `--workers`，同步提高线程池最小 worker 数；普通对象解析和 AssetMap 扫描先按独立 `SerializedFile` 分配 worker，文件数少于 worker 时再为同一文件创建独立对象范围 reader，并由显式有界线程执行器分配真实 worker，避免嵌套 `Parallel` 被调度器内联成单线程。结果始终按原文件/对象顺序合并后再执行 CRC 字符串缓存、关系解析、过滤与 spool，保持格式兼容顺序。实机确认逐对象创建随机访问流造成额外分配后，调度器进一步暴露稳定 worker 槽位；每个对象 worker 现在只创建一个覆盖完整文件、内存段或容器切片的独立 reader，并在该 worker 内复用，流位置仍完全隔离但独立流数量由对象数降到活跃 worker 数。非 FBX 导出使用有界 worker 和按资产序号协调的输出路径预留；相同路径等待较早资产结果，失败可按串行语义复用，既有文件和 duplicate 后缀不依赖调度顺序。共享容器文件改用 `RandomAccess.Read` 定位读取，内存后备直接复制；共享资源 reader 的缓存和 seek/read 全部同步。VFS、UnityFS/ENCR、Mhy、BLB 和 HYG 的独立容器块已接入共用有界 producer/worker 解码管线：输入保持顺序读取，解码结果按预计算偏移并发写入共享后备，排队与活动暂存合计受 256 MiB 预算限制；HNACB1 因后续块依赖首块内容保留显式串行路径。针对大量单块容器串接文件，新增轻量 VFS/普通 UnityFS/ENCR 布局探测和独立随机读取范围；范围 worker 活跃时容器内部固定 1 worker，解析结果先进入按 offset 索引的槽，再按原 offset/inner-file 顺序注册资产和资源。UnityCN、Azur、BH3、Naraka 等共享或变换头状态尚未隔离的布局明确回退串行范围发现，仍可使用块内并行。该调度约束也写入后续阶段开发规则。FBX 与 MonoBehaviour 共享状态保留串行临界区，异常仍按单资产隔离，OOM 继续上抛。Server GC 实机候选超出 RSS 门槛后已撤销，继续使用 Workstation GC，并保留 concurrent GC、75% heap hard limit 和 RetainVM。
- 修改文件/接口：新增 `CommandOptions.Workers`、`Options.Workers`、`AssetsManager.WorkerCount`、`AssetsHelper.SetWorkerCount`、内部 `BoundedParallel`、`ReadOnlyRandomAccessStream`、`ExportPathCoordinator`、`ContainerBlockPipeline` 和 `BlockFileRangeDiscovery`；`BoundedParallel` 可给命名线程指定阶段前缀；`ObjectReader` 可基于文件句柄、内存段或容器切片创建保留原始对齐语义的对象范围 reader；`Studio.ExportAssets` 接收 worker 数；`SharedBackingStore.ReadAt` 支持并发定位读取，`PrepareForPositionedWrites`/`WriteAt` 支持容器块固定偏移写入；VFS 布局读取与 payload 解压分离，VFS/Bundle/Mhy/BLB/HYG 构造路径接收同一 worker 数和取消令牌；`AssetsManager` 的容器解析结果可延迟原序合并；`ResourceReader` 严格读满并同步共享流；更新 `README.md`、`CLAUDE.md`、`PLAN.md` 及 Core/CLI smoke。
- 验证及指标：第一候选精确提交 `8d5127dba53f0ebd89c83582aedfc69ff8a75d0c` 已推送，Debian 从 GitHub 检出后 Release 构建、Core smoke、Linux 动态 package smoke 均通过，发布归档 SHA256 `36cead45901a68d503b487967ee88fce5c7187f71956d497ee3b0b14a5b74d77`。固定 1,812,594,931 字节输入上，`--workers 1` 退出码 0、wall 13:11.32、CPU 96%、峰值 RSS 7,235,520 KiB、Object scanning 209,789.928 ms；`--workers 4` 退出码 0、wall 12:17.29、CPU 103%、峰值 RSS 7,255,676 KiB、Object scanning 161,456.186 ms。两次 JSON/MessagePack 与基线逐字节一致，XML 规范化 SHA256 均为 `08309b9ce48e344e5dd465f4b7a8504a2387aa1eb9e3343caa9ee73e346a6b14`，临时目录均清空且 `oom_kill` 未增加；但该输入只含一个大型 `SerializedFile`，按文件并行仍接近单核，且 Server GC 峰值超过 6,957,772 KiB 门槛，因此 `8d5127d` 明确判定失败。第二候选精确提交 `468799dcb951c5bcfd85b6e5b4804b87a450a14f` 已推送并由 Debian 从 GitHub 检出；Release 构建、Core smoke 和 Linux 动态 package smoke 通过。固定输入 `--workers 4` 退出码 0、wall 12:19.27、CPU 100%、GNU time 峰值 RSS 7,040,988 KiB、采样峰值 7,041,700 KiB、临时文件峰值 5,349,871,049 字节、Loading 575,620.995 ms、Object scanning 151,730.517 ms，`oom_kill` 0 -> 0 且临时目录清空。JSON 和 MessagePack SHA256 与基线逐字节一致，XML 规范化 SHA256 为同一 `08309b9ce48e344e5dd465f4b7a8504a2387aa1eb9e3343caa9ee73e346a6b14`。线程采样确认三个 `AnimeStudio worker` 同时运行在不同 CPU，23:15:22 单秒合计约 195% CPU，证明对象范围调度生效；但约 576 秒 Loading 仍由主线程接近 100% CPU 串行执行，且 RSS 略超 6,957,772 KiB 门槛，所以 `468799d` 仍不是最终候选。容器范围修复后的精确提交 `597460408d1a02e8ebdc6e0fd26278b32beadde1` 已推送并由 Debian 从 GitHub 检出，Release/Core/Linux 动态 package runtime smoke 通过，发布归档 SHA256 为 `b9c44b21cfcfb529db02e252ce5e479bc8d8ed2a72ef420bb9e9598178fc5831`。同一固定输入 `--workers 4` 完整退出码 0，wall 7:11.46、平均 CPU 216.8%、峰值 368%、431 个 CPU 样本中 266 个超过 100%、222 个超过 200%、210 个超过 300%；Loading 237,641.078 ms、Object scanning 181,240.131 ms，三个命名 `AnimeStudio container` 线程持续分布于不同 CPU。JSON/MessagePack 与基线逐字节一致，XML 规范化 SHA256 为 `08309b9ce48e344e5dd465f4b7a8504a2387aa1eb9e3343caa9ee73e346a6b14`，临时峰值 5,402,732,332 字节且退出后清零，`oom_kill` 0 -> 0；但 GNU time/采样峰值 RSS 分别为 7,095,324/7,095,864 KiB，仍高于 6,957,772 KiB 门槛，故该候选只证明调度和兼容正确，不能作为最终版本。其 runtime counters 相比 `2f7e87b` 串行兼容版本多分配约 3.27 GiB，最大 committed memory 增加约 244 MiB，Gen2/LOH 峰值分别增加约 136/97 MiB。worker reader 复用候选 `2900a00524099a2b688d33246b9f0aa100593fcf` 已推送并由 Debian 精确检出，Release/Core/Linux 动态 package runtime smoke 通过，归档 SHA256 `b9b92d47e5b6f44edacc27e99171bb4b2dc80e729421dab1546bf2690becb33a`。固定运行退出码 0，wall 7:17.72、平均 CPU 213.8%、峰值 364%、437 个样本中 258 个超过 100%、219 个超过 200%、211 个超过 300%，Loading 236,115.684 ms、Object scanning 188,647.780 ms；输出三格式继续兼容，临时目录清零，`oom_kill` 0 -> 0。但 GNU time/采样峰值 RSS 仍为 7,091,508/7,092,100 KiB，总分配仅由 44,553,481,552 降至 44,438,823,872 字节，说明逐对象 reader 只占次要部分；主要增量来自整文件 `AssetMapObjectScan[]`、未缓存重复字符串和派生列表在全部对象完成前同时保留。批次扫描候选 `0d39970ef0a59b96e5a338cadc91e522e037e964` 已推送并由 Debian 精确检出，Release/Core/Linux 动态 package runtime smoke 通过，归档 SHA256 `e37619ea938dd7a2e398c298d32f70b0aec5a15c478c52e911e677519c08827d`。固定运行退出码 0，wall 7:33、平均 CPU 206.9%、峰值 368%、452 个样本中 260 个超过 100%、217 个超过 200%、206 个超过 300%；JSON/MessagePack 与基线逐字节一致，XML 规范化 SHA256 `08309b9ce48e344e5dd465f4b7a8504a2387aa1eb9e3343caa9ee73e346a6b14`，临时目录清零，`oom_kill` 0 -> 0。但采样峰值 RSS 7,128,748 KiB，仍高于 6,957,772 KiB 门槛，因此 `0d39970` 也不是最终候选；该结果说明单纯切小扫描窗口不能抵消对象并行阶段的堆峰值。被停止的旧实现全量尝试由外部 `SIGTERM` 在 7:09:44 终止，非 OOM，已处理 27/31 个 `.chk`，GNU time 峰值 RSS 11,409,832 KiB、采样峰值 11,410,128 KiB、`oom_kill` 0 -> 0，记录保留在服务器 `attempt-sigterm-1`。
- 本次补充完成：新增共享 memory-stable worker 预算，高分配的容器范围、容器块、对象解析和批量 AssetMap 扫描在 `--workers 4` 时内部使用 3 个 worker，给 4 核 Debian 保留一个核心的内存/GC 余量，同时 `--workers 1` 和 `--workers 2` 保持原请求并行度。
- 本次补充验证：本地 `dotnet build AnimeStudio.sln -c Release`、Core smoke、`osx-arm64` 动态 package/runtime smoke、`linux-x64`/`win-x64` package smoke、`git diff --check` 和活跃项目旧 TFM/桌面引用扫描均通过；仅有既有 `MessagePack` NU1903、nullable 和 TODO 警告。精确提交 `fafe68e57d57486ca9b8e2a75c3201d951aeec65` 已推送并由 Debian 精确检出，Release/Core/Linux 动态 package runtime smoke 通过，Linux 归档 SHA256 `3930193a1fd7858fc18a9a65fd49e82f136ec14cf8a3afbc830d27995dbd5ceb`。固定 AssetMap `--workers 4` 退出码 0，wall 7:36.65、平均 CPU 237.0%、峰值 CPU 271.0%、Loading/Object scanning 阶段明确超过单核；JSON/MessagePack 与基线逐字节一致，XML 以 `filename`/`createdAt` 规范化后 SHA256 `08309b9ce48e344e5dd465f4b7a8504a2387aa1eb9e3343caa9ee73e346a6b14`。但 GNU time/采样峰值 RSS 为 7,116,136/7,116,900 KiB，高于 6,957,772 KiB 门槛，故 `fafe68e` 失败；后续后台 suite 已停止，未继续浪费时间跑 container-only/full Convert。
- 当前工作树补充：高保留对象图阶段的 memory-stable 预算已改为 `requested <= 2 ? requested : max(2, requested / 2)`，因此 Debian 默认 `--workers 4` 内部使用 2 个高保留 worker，仍保持多核执行但比 `fafe68e` 少一个对象图并发保留集；Core smoke 增加 `4 -> 2`、`8 -> 4` 断言。本地 `dotnet build AnimeStudio.sln -c Release`、Core smoke、三 RID package smoke、`git diff --check` 和活跃项目旧 TFM/桌面引用扫描均通过，等待提交推送后进行 Debian 精确验证。
- 后续实测：精确提交 `87a646ba40d534a89ef341961b030d74f3d2dd88` 已推送并由 Debian 精确检出，Release/Core/Linux 动态 package runtime smoke 通过，Linux 归档 SHA256 `c23ad58d56616515913dbb9c5727bde552990cbcee2289562d2645e30a7de69c`。固定 AssetMap `--workers 4` 在输出 writer 开始前已采到 RSS 7,067,432 KiB，超过 6,957,772 KiB 门槛；该 run 当场终止，未继续跑全量回归。下一候选改为在每个 AssetMap 输入加载完成、对象扫描开始前做一次受控 compacting full GC，释放容器范围和解压阶段临时保留，避免加载临时图与对象扫描图叠峰。
- 当前工作树补充 2：已在 AssetMap 每个输入加载完成、对象扫描开始前加入受控 compacting full GC。本地 `dotnet build AnimeStudio.sln -c Release`、Core smoke、三 RID package smoke、`git diff --check` 和活跃项目旧 TFM/桌面引用扫描均通过，等待提交推送后进行 Debian 精确验证。
- 后续实测 2：精确提交 `b983ccee60f34db36fbc15c11ef45c77b79a51ec` 已推送并由 Debian 精确检出，Release/Core/Linux 动态 package runtime smoke 通过，Linux 归档 SHA256 `ed0ae9fd4f2ddae9838bca040db21c324dd1c2b302a8ac5394e9df1b8d8ea8cf`。固定 AssetMap `--workers 4` 在输出 writer 开始前已采到 RSS 7,027,464 KiB，仍高于 6,957,772 KiB 门槛；该 run 当场终止，未继续跑全量回归。受控加载后 GC 有一定收益但不足，下一步必须减少对象扫描阶段的保留图，而不是继续依赖 GC 或仅降 worker。
- 新增目标记录：用户新增完整 systemd 集成目标，包括但不限于 `journalctl` 与 `systemctl` 管理。已按仓库 Context7 规则查询 `/systemd/systemd` 当前文档：service stdout/stderr 可由 journal 收集，`systemctl status` 展示服务状态和近期 journal，`journalctl -u`/`journalctl -b` 用于日志查询；`EnvironmentFile=` 可加载外部环境配置，`Restart=on-failure` 是常用失败重启策略，硬化项需显式保留输入、输出和临时目录写入能力。由于 `PLAN.md` 仅承载当前 Phase 2 计划，已把 systemd 集成加入 Phase 3 Roadmap，并同步 `CLAUDE.md` 的后续实现约束；Phase 2 仍需先过 AssetMap/container/full Convert 门槛后才能切换 Phase 3 并展开详细实现计划。
- 当前工作树补充 3：对象扫描批次窗口由 16,384 降至 4,096，减少同一批次合并前同时保留的 `AssetMapObjectScan`/解析对象图；跨批次顺序由现有 smoke 继续覆盖，输出顺序和格式不变。该候选保留 4 核下 2 个高保留 worker 与加载后 compacting GC，目标是消除 `b983cce` 距离 RSS 门槛约 70 MiB 的剩余峰值。
- 后续实测 3：精确提交 `d4e57b279efd100739c449f2433fe0056c34ebee` 已推送并由 Debian 精确检出，Release/Core/Linux 动态 package runtime smoke 通过，Linux 归档 SHA256 `a2fd9812375fad3478d53c1e11d8578f42dece0973daac9358e346f1a1095afe`。固定 AssetMap `--workers 4` 在输出 writer 开始前采到 RSS 6,988,900 KiB，仍高于 6,957,772 KiB 门槛；该 run 当场终止，未继续跑全量回归。4,096 批次窗口有效但还差约 31 MiB，下一候选继续将批次窗口降到 1,024。
- 问题与决策：并行度默认不得为 1；单核主机仍可正确运行，但不为其牺牲多核默认值。严禁多个 worker 共享可变 reader 位置；同一 `SerializedFile` 仅在文件句柄、可公开内存段或引用计数容器切片能创建独立对象范围时才对象级并行，否则受控回退串行。共享资源流必须串行 seek/read，读取完成后的转换可并行。PPtr 的外部文件索引缓存必须使用大小写不敏感的原子 `GetOrAdd`，不得在对象 worker 间执行分离的 `TryGetValue`/`Add`。FBX 原生库会切换进程当前目录且尚未证明可重入，因此暂不并行；其资产保持原序直到完整导出结束。`PERF_NEXT_STEP.md` 是历史规划输入，其中暂缓 Phase 2 并行化的建议已被本次用户明确要求覆盖。Server GC 已由实机数据否决，不再作为 Phase 2 条件。
- 未完成事项：下一步需要在不降低默认多核调度的前提下继续降低对象并行阶段峰值；`fafe68e` 已证明 3 个高保留 worker 仍超线，下一候选将把高保留对象图阶段限制为最多 2 个 worker，以 4 核 Debian 上约双核以上 CPU 占用换取 RSS 余量。若仍不足，再评估对象阶段结束后的受控压缩/收集或减少 `matches`/PPtr 关系保留图。最终候选 RSS 必须不高于 6,957,772 KiB；随后跑同提交 `--workers 1` 对照、container-only 和全量 Convert，分别核对多核调度、10,197,212 KiB 与 11,474,792 KiB 门槛。
- 后续注意事项：并行化不得改变 AssetMap JSON/MessagePack 字节和 XML 规范化结果；实机必须满足 AssetMap 6,957,772 KiB、container-only 10,197,212 KiB、全量 Convert 11,474,792 KiB 的既有上限；不得修改或提交用户的未跟踪 `PERF_ANALYZE_REPO.md`。
- 起止提交：`2f7e87b` -> 当前工作树（进行中）。
