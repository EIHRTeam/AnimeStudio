# Development Status

## Phase 0 - Parser and Memory Safety Baseline

- Status: completed
- Branch: `master`
- Completion commit: `71fdbf9`
- Acceptance: Endfield controller export and memory safety smoke checks passed.

## Phase 1 - CLI-only .NET 10 and Container Streaming

- Status: implementation complete; acceptance closure in progress
- Branch: `feat/container-streaming`
- Baseline commit: `71fdbf9`
- Implementation commit: `cf1ad70`
- Acceptance: memory, cleanup, parser, build, and package gates passed; strict
  cross-run output identity remains unresolved because of pre-existing random
  fallback names and volatile FBX metadata.

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
