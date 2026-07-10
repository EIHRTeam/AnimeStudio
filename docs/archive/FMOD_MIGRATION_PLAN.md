> Historical document. Current sources of truth are `/ROADMAP.md`, `/PLAN.md`,
> and `/STATUS.md`.

# FMOD 1.07 → 2.03 升级与跨平台音频支持计划

## Context

当前项目使用 FMOD Core API 1.07.16 解码 Unity AudioClip 为 WAV。原生库仅 `win-x64` 有 `fmod.dll`，Linux/macOS 缺失，导致"音频导出"成为 Phase 4 能力矩阵的盲区。

用户已下载 FMOD 2.03.14 SDK：
- macOS: `/Users/null/Downloads/fmod-core-api-darwin/lib/libfmod.dylib`（Universal: x86_64+arm64）
- Linux: `/Users/null/Downloads/fmodstudioapi20314linux/api/core/lib/x86_64/libfmod.so.14` + 软链接

2.03 与 1.07 的 C API 高度兼容，但 SDK 自带的 C# 封装有架构性变化：
P/Invoke 从 `FMOD_*` 重命名为 `FMOD5_*`，Handle 模型从 class 改为
struct。项目不能直接分发 SDK C# 源文件，因此采用项目自有的最小兼容层：
保留 GUI 依赖的 class/null 语义，只声明实际使用的 2.03 C ABI。

## 2026-06-11 实施状态

**状态：三平台 runtime 已迁移，macOS/Linux 已执行验证，Windows 原生执行与发布许可待处理。**

- 已用项目自有兼容层替换旧 FMOD SDK 包装，删除未使用的 DSP 包装。
- Windows/Linux/macOS 统一使用 FMOD 2.03.14 `FMOD5_*` ABI。
- `AudioClipConverter` 使用 `NOSOUND` 输出并在所有失败路径释放 system/sound。
- 平台能力检查会把缺库转换为 `PlatformNotSupportedException`。
- macOS 冒烟已验证版本 `0x00020314`、内存 WAV、PCM lock/unlock 和缺库降级。
- Debian 13 x86_64 冒烟已验证 `linux-x64` 发布、版本 `0x00020314`、
  内存 WAV、PCM lock/unlock。
- Windows `win-x64` 已换用 2.03.14 x64 DLL，CLI 交叉发布、x64 架构、SHA256
  和全部 27 个 P/Invoke 导出已验证；原生执行待 Windows runner。
- FMOD EULA 对工具类产品的 runtime 分发有限制。Linux/macOS runtime 当前仅用于
  本地迁移和 CI 验证，确认许可前不得发布对应安装包。

## 目标

- 将 FMOD 从 1.07.16 升级到 2.03.14，三个 RID 均提供原生库
- 在 PlatformCapabilities 中加入 FMOD 能力检查，与 ACL/DXBC 降级模式一致
- AudioClipConverter 使用者代码零改动或接近零改动

## 修改清单

### Step 1: 替换 `fmod.cs`、`fmod_dsp.cs`、`fmod_errors.cs`（已完成）

实现为项目自有兼容层，不复制 SDK wrapper。兼容层仅覆盖
`AudioClipConverter` 与 GUI 播放器实际使用的方法，并由 `Factory` 注册
`DllImportResolver`。Windows 和 Unix ABI 在同一公开 API 后分流。

### Step 2: 放置原生库（已完成）

| RID | 源文件 | 目标路径 |
|-----|--------|---------|
| `win-x64` | `fmodstudioapi20314win-installer/api/core/lib/x64/fmod.dll` | `runtimes/win-x64/native/fmod.dll` |
| `linux-x64` | `fmodstudioapi20314linux/api/core/lib/x86_64/libfmod.so.14` | `runtimes/linux-x64/native/libfmod.so` |
| `osx-arm64` | `fmod-core-api-darwin/lib/libfmod.dylib` | `runtimes/osx-arm64/native/libfmod.dylib` |

macOS 的 `.dylib` 已是 universal（含 x86_64 + arm64），无需额外处理。

Linux 目标文件使用实际二进制内容而不是软链接，避免归档或 publish 复制时丢失链接目标。

### Step 3: 在 `PlatformCapabilities` 中新增 FMOD 检查（已完成）

在 `PlatformCapabilities.cs` 中增加：

```csharp
public static bool SupportsFmodAudioConversion => true; // 全平台支持，仅依赖原生库加载

public static bool TryGetFmodAudioConversionSupport(out string reason)
{
    if (!TryGetNativeLibrarySupport("fmod", out reason))
    {
        return false;
    }
    reason = string.Empty;
    return true;
}
```

在 `AudioClipConverter.ConvertToWav()` 入口处调用 `PlatformCapabilities.EnsureNativeLibraryAvailable("fmod")`，确保 `DllNotFoundException` 不泄漏到用户面。与 ACL (`ACL.cs` 中 `EnsureNativeLibraryAvailable`) 和 HLSLDecompiler 的守卫模式一致。

### Step 4: 更新计划文档的能力矩阵（已完成）

`PLAN.md` 阶段 4 的表格中新增一行：

```
| 音频转码 (FMOD) | 完整 | 完整 | 完整 |
```

### Step 5: 更新 `NATIVE_ASSETS.md` 和 `THIRD_PARTY_NOTICES.md`（已完成）

- `NATIVE_ASSETS.md`: 为三个 RID 添加 `fmod.dll` / `libfmod.so.14` / `libfmod.dylib` 的 SHA256 和编译来源信息
- `THIRD_PARTY_NOTICES.md`: 添加 FMOD 版权声明（按 FMOD 许可证要求），格式参照已有的 Autodesk 条目

### Step 6: 验证 AudioClipConverter 兼容性（macOS/Linux 已完成）

读取新的 `fmod.cs` 中以下方法的公开签名，逐一对比 `AudioClipConverter.cs` 中的调用：

| 调用方代码 | 需匹配的方法签名 |
|-----------|-----------------|
| `Factory.System_Create(out var system)` | `Factory.System_Create(out System system)` |
| `system.init(1, INITFLAGS.NORMAL, IntPtr.Zero)` | `System.init(int, INITFLAGS, IntPtr)` |
| `system.createSound(data, MODE.OPENMEMORY, ref exinfo, out var sound)` | `System.createSound(byte[], MODE, ref CREATESOUNDEXINFO, out Sound)` |
| `sound.getNumSubSounds(out int num)` | `Sound.getNumSubSounds(out int)` |
| `sound.getSubSound(0, out var sub)` | `Sound.getSubSound(int, out Sound)` |
| `sound.getFormat(out _, out _, out int ch, out int bits)` | `Sound.getFormat(out SOUND_TYPE, out SOUND_FORMAT, out int, out int)` |
| `sound.getDefaults(out var freq, out _)` | `Sound.getDefaults(out float, out int)` |
| `sound.getLength(out var len, TIMEUNIT.PCMBYTES)` | `Sound.getLength(out uint, TIMEUNIT)` |
| `sound.@lock(0, length, out ptr1, out ptr2, out len1, out len2)` | `Sound.@lock(uint, uint, out IntPtr, out IntPtr, out uint, out uint)` |
| `sound.unlock(ptr1, ptr2, len1, len2)` | `Sound.unlock(IntPtr, IntPtr, uint, uint)` |
| `sound.release()` / `system.release()` | `Sound.release()` / `System.release()` |

预期全部匹配——新 SDK 仅变更内部 P/Invoke 名称 (`FMOD5_*`) 和 handle 存储方式，公开方法签名保持不变。

### Step 7: 为 linux-x64 发布配置处理版本化 SO（不需要）

仓库保存为普通文件 `libfmod.so`，现有 RID wildcard 发布规则会直接复制。

## 验证

1. **编译**: `dotnet build AnimeStudio.CLI -c Release -f net10.0` 零错误
2. **macOS publish 与 smoke test**:
   ```bash
   dotnet publish AnimeStudio.CLI -c Release -f net10.0 -r osx-arm64 --self-contained false
   # 确认 libfmod.dylib 存在于 publish 输出中
   # 编写一个快速测试：调用 Factory.System_Create + system.init → 应返回 OK
   ```
3. **Linux publish**: 同上，验证 `libfmod.so.14` 和 `libfmod.so` 均在 publish 输出中
4. **Windows publish**: 验证 2.03.14 DLL、`FMOD5_*` 导出和 runtime 版本
5. **AudioClipConverter 功能测试**: 用合成/测试 AudioClip 数据验证 `ConvertToWav()` 返回有效 WAV 缓冲
6. **降级测试**: 故意移除 `libfmod.dylib`，验证 `PlatformCapabilities.EnsureNativeLibraryAvailable("fmod")` 抛出 `PlatformNotSupportedException` 而非 `DllNotFoundException`
7. **GUI 兼容**: `dotnet build AnimeStudio.GUI -c Release -f net9.0-windows -p:EnableWindowsTargeting=true` 通过

### 已执行验证

- `dotnet build AnimeStudio.CLI -c Release -f net10.0`：通过。
- GUI `net9.0-windows` 交叉编译：通过。
- macOS ARM64 publish + 仓库 smoke：通过，FMOD `0x00020314`。
- macOS 缺库降级：通过，抛出 `PlatformNotSupportedException`。
- Debian 13 `linux-x64` publish + 仓库 smoke：通过，FMOD `0x00020314`。
- Windows x64 CLI publish：通过，打包的 2.03.14 DLL 哈希为
  `b07035752ed88be7a492c31fc45a7a33e935d003610677d2495beca0aca61514`。
- Windows x64 GUI `net9.0-windows` publish：通过，打包同一 2.03.14 x64 DLL。
- Windows DLL 导出检查：兼容层声明的 27 个 `FMOD5_*` 入口全部存在。
- Windows 原生 smoke：当前环境无 Windows/Wine，待 Windows CI runner。

## 风险

- **发布许可未确认**: 当前 EULA 文本限制 FMOD Engine 作为工具集的一部分分发；
  公共发布前必须取得适用许可或移除 runtime
- **新 fmod_dsp.cs 大幅重构**: 确认项目中无任何代码直接引用 `fmod_dsp.cs` 中的类型（当前确认仅 `AudioClipConverter` 使用 FMOD 命名空间，且不涉及 DSP 类型）
- **VERSION.partial 的 Unity 条件编译**: 新 SDK 中 `VERSION` 是 `partial class` 且有 `#if !UNITY_2021_3_OR_NEWER`——非 Unity 环境下正常，但需确认编译不触发意外路径
