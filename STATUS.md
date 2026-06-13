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
