# AnimeStudio CLI 跨平台迁移计划

## 总体目标

将 CLI 迁移到 .NET 10，并发布以下 framework-dependent 产物：

- `win-x64`
- `linux-x64`，基线 Debian 13
- `osx-arm64`，基线 macOS 15

GUI 与 Patcher 暂不迁移。共享项目保留 `net8.0;net9.0`，新增 `net10.0`，避免破坏现有 GUI。

## 阶段记录规范

创建 `CLI_CROSS_PLATFORM_MIGRATION_PLAN.md`。每次实施会话只能归属一个阶段，并在结束前记录：

- 本次目标
- 阶段完成状态（未开始、进行中、已完成或阻塞）
- 已完成修改
- 验证命令和结果
- 未解决问题
- 后续工作的注意事项

每次会话开始时将对应阶段标记为“进行中”，结束前根据验收结果更新为“已完成”或“阻塞”。
未达到当前阶段验收条件，不进入下一阶段。

## 阶段 0：基线与跟踪

**阶段完成状态：已完成**

**当前会话已完成**

- 审查兼容性报告、项目引用链、原生依赖和 CI。
- 验证 macOS FBX SDK 2020.3.9 同时支持 `x86_64/arm64`。
- 验证 Ooz 可在 macOS 15 ARM64 编译。
- 确认 Texture2DDecoder 已提供 Linux/macOS 原生包，无需替换解码器。
- 确认 `.NET 10` 编译当前阻塞于 `FairGuardUtils` 的两个 Span 类型错误。
- 确认 ACL 定制源码缺失，`HLSLDecompiler` 实际被 CLI shader 路径使用。

**后续注意事项**

- 报告中 Newtonsoft.Json 13.0.4 不存在、纹理解码器仅支持 Windows、HLSLDecompiler 仅 GUI 使用等结论不正确。
- FBX SDK 安装包不得提交；只提交按许可证构建的最终原生库及声明。
- 仓库没有可用测试资产，需创建合成测试数据。

## 阶段 1：建立 .NET 10 托管基线

**阶段完成状态：已完成**

- 添加 `global.json`，固定 SDK `10.0.301` 并允许 latest patch。
- CLI 改为单目标 `net10.0`。
- Core、Utility、PInvoke、FBXWrapper 改为 `net10.0;net9.0;net8.0`。
- GUI 和 Patcher TFM 保持不变。
- 显式使用 `seedInts.AsSpan()` 修复 `FairGuardUtils` 的 .NET 10 编译错误。
- 保留有效的 Newtonsoft.Json 13.0.4，不做无关序列化重构。
- 验收：CLI `net10.0` 编译成功，原 GUI 的 net8/net9 引用仍可恢复和编译。

## 阶段 2：CLI 与配置现代化

**阶段完成状态：已完成**

- System.CommandLine 升级到稳定版 `2.0.9`。
- 删除 `BinderBase`/`SetHandler`，使用 `SetAction(ParseResult)`、`CustomParser`、`Validators` 和 `GetValue`。
- 保持现有参数名、默认值、过滤器语义和帮助行为。
- `Program.Main` 返回退出码；解析或启动失败返回非零，单个资源失败继续批处理。
- 用 `appsettings.json` 和 `System.Text.Json` 替代 App.config/ConfigurationManager。
- `types`、`uvs`、`texs` 改为结构化 JSON，同时保留现有默认值。
- 统一使用 `Path.Combine`；配置从 `AppContext.BaseDirectory` 读取，输入输出相对路径仍基于工作目录。

## 阶段 3：原生依赖和 RID 布局

**阶段完成状态：已完成**

- 原生文件迁移到 `AnimeStudio.Libraries/runtimes/<rid>/native/`，发布时只复制当前 RID。
- 使用 `NativeLibrary` 和逻辑库名统一解析 `.dll/.so/.dylib`，移除 `libdl`、`RTLD_GLOBAL` 和 `x86/x64` 目录推断。
- Texture2DDecoder 升级为 wrapper `0.17.1`，平台包使用 Windows/Linux/macOS `0.2.0`；移除 Utility 中重复引用。
- 调整 Ooz CMake 输出名称为 `AnimeStudio.Ooz`，为三个 RID 提交预编译库。
- 为 FBXNative 添加 CMake，使用 C++17、`FBX_SDK_ROOT` 和静态 FBX SDK：
  - Windows 保留现有 x64 库，后续可重建。
  - Linux x64 使用提供的 2020.3.9 SDK。
  - macOS ARM64 使用已安装的 2020.3.9 SDK。
- 增加原生资产清单，记录 SHA256、编译器、SDK 版本和目标 RID，并附 Autodesk 要求的版权声明。

## 阶段 4：平台能力与降级行为

**阶段完成状态：已完成**

| 功能 | Windows x64 | Linux x64 | macOS ARM64 |
|---|---|---|---|
| 纹理解码 | 完整 | 完整 | 完整 |
| Ooz 解压 | 完整 | 完整 | 完整 |
| FBX 导出 | 完整 | 完整 | 完整 |
| ACL 动画解压 | 完整 | 首版禁用 | 首版禁用 |
| DirectX shader 反编译 | 完整 | 首版降级 | 首版降级 |

- 增加统一的平台能力检查，禁止暴露 `DllNotFoundException`。
- Linux/macOS 遇到 ACL 动画时记录资源和游戏类型，跳过该资源并继续批处理。
- 非 Windows 的 DXBC 子程序写入明确的“不支持”注释和 hash；Metal/SPIR-V shader 路径保持工作。
- Windows CLI 补齐 HLSLDecompiler.dll 的发布，移除运行时不需要的 BinaryDecompiler.lib。

## 阶段 5：构建、发布与 CI

**阶段完成状态：已完成**

- 提供 Windows PowerShell 和 POSIX shell 发布脚本。
- 使用 `dotnet publish -f net10.0 -r <rid> --self-contained false`。
- 输出：
  - `AnimeStudio.CLI-<version>-win-x64.zip`
  - `AnimeStudio.CLI-<version>-linux-x64.tar.gz`
  - `AnimeStudio.CLI-<version>-osx-arm64.tar.gz`
- CI 使用 .NET 10：
  - Windows x64 构建与 smoke test。
  - Debian 13 容器验证 Linux x64。
  - `macos-15` Apple Silicon 验证 macOS ARM64。
- 保留现有 GUI 工作流；CLI 使用独立矩阵，避免 AppHost Patcher 和 GUI 产物混合。

## 阶段 6：测试与验收

**阶段完成状态：未开始**

- 新建基于 .NET 10 SDK MSTest 模板的 CLI 测试项目。
- 测试参数解析、默认值、自定义 regex/key parser、无效路径和帮助输出。
- 测试配置缺失、部分配置、无效 JSON 和结构化类型映射。
- 使用合成数据测试 DXT/ASTC 纹理解码、Ooz 解压和最小 FBX 场景导出。
- 验证三个发布包均可执行 `--help`，并只包含当前 RID 的原生库。
- Linux/macOS 验证 ACL 和 DXBC 降级不会终止整个导出任务。
- 发布前使用维护者合法持有的 GI/SR/ZZZ 资产执行手工回归；只记录输入 hash、输出数量和错误摘要，不提交游戏资产。

## 假设与依据

- 用户机器已安装 .NET 10；实际只需要对应架构的 .NET 10 Runtime。
- 首版不发布 Linux ARM64、Windows x86/ARM64 或 macOS x64。
- 首版不承诺非 Windows ACL 与 DXBC 完全等价。
- 不包含 macOS Developer ID 签名和 notarization。
- 依据：[.NET 发布文档](https://learn.microsoft.com/dotnet/core/deploying/)、[System.CommandLine 2.0.9](https://github.com/dotnet/command-line-api/tree/v2.0.9)、[Texture2DDecoder](https://github.com/KiruyaMomochi/Texture2DDecoder)、[GitHub Hosted Runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)。
