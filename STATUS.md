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
- 起止提交：`1a24296` -> 待本次文档提交。

## Phase 2 - Asset Map Streaming

- Status: active
- Branch: `feat/asset-map-streaming`
- Baseline commit: 待 Phase 1 收尾提交
- Acceptance: 见 `PLAN.md`；尚未开始。
