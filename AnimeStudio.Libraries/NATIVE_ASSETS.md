# Native Asset Manifest

All hashes are SHA256. Runtime files live under `runtimes/<rid>/native` and are
copied to the application root only for the RID selected by `dotnet publish -r`.

## win-x64

Except for FMOD, these binaries were moved without modification from the
repository's previous `AnimeStudio.Libraries` x64 layout. Their original
compiler and SDK build metadata are not available. FBXNative is retained as the
existing Windows x64 binary until it can be rebuilt on Windows from the new
CMake project. FMOD comes from the FMOD 2.03.14 Windows Core API x64 SDK.

| File | SHA256 |
|---|---|
| `AnimeStudio.FBXNative.dll` | `a138e600a1f344bd0bf4d288b9a0248aeb8c781de5f42d969af94ad45b00d525` |
| `AnimeStudio.Ooz.dll` | `aa007fa0fdd62370876fa368e6262e8c8de425f5c3a2967d0edc9a76e9e7f470` |
| `HLSLDecompiler.dll` | `b6364b4c1f55b0e13509304924563a4101138e31b7b4f3aeaea0dd7065fe20bb` |
| `acl.dll` | `2982fbcf80062c2ad20ea7bcc5b15b601de68e4eb9f71e11689c3d7694cfe9c9` |
| `acldb.dll` | `fc89d4fc47303ce6980d7532e3c1e7d5a65f7ec9e45a54bd60a607cb2fb141e1` |
| `acldb_zzz.dll` | `9780d0fb6890b295c12225c72fa959c185de838ef17eefb311b0c591cdbb181a` |
| `fmod.dll` (FMOD 2.03.14) | `b07035752ed88be7a492c31fc45a7a33e935d003610677d2495beca0aca61514` |
| `sracl.dll` | `1d08c848da0d3e4e1c0db2c69fa05e1132d2432a08e39785a2e8cf8bf35acbe3` |

`link/win-x64/BinaryDecompiler.lib` is a link-time asset and is not published.
Its SHA256 is
`64e8ec7f4159fecb75cee055b3f1a350bed45fa5fca8d1940d037e4c4dff7a91`.

## linux-x64

- Build host: Debian 13.5 x86_64
- Compiler: GCC 14.2.0
- CMake: 3.31.6
- FBX SDK: Autodesk FBX SDK 2020.3.9, static `libfbxsdk.a`
- SDK archive SHA256:
  `25d3cfd72a8a02070630a8f939bc2a41d48b9a8d87905488c352e949fa5c8635`

| File | SHA256 |
|---|---|
| `libAnimeStudio.FBXNative.so` | `789fb1a776a3ecb79b624c7434fab2d5c94ce93a4ae9634a83b801d2639b533d` |
| `libAnimeStudio.Ooz.so` | `ac769100e5b0313e5177902f94d0d42e8765047ce42a7dd6db643653811eb300` |
| `libfmod.so` (FMOD 2.03.14) | `3ae8c9eca9a28ee5ec85b56b12d682ff6bbc20de5cd8f693c8eba8fe02dec3bc` |

## osx-arm64

- Build host: macOS 15.7.7 ARM64
- Compiler: Apple Clang 17.0.0
- CMake: 4.3.3
- Deployment target: macOS 15.0
- FBX SDK: Autodesk FBX SDK 2020.3.9, static universal
  `lib/clang/release/libfbxsdk.a`, linked as ARM64

| File | SHA256 |
|---|---|
| `libAnimeStudio.FBXNative.dylib` | `971d68ad8d962553aeefbe5f4eeb586e29dccb36a2b608d78f5beb4810d6c741` |
| `libAnimeStudio.Ooz.dylib` | `9eb5f47089d0a2cad9c9faa3965981e81fd09ae3de12c8fd6d946ae234cf70a1` |
| `libfmod.dylib` (FMOD 2.03.14, universal x86_64/ARM64) | `b111ec81ad626808dfa5aafa348eb3a2d66104f8055773f6b15bea554ec3a02e` |

## FMOD distribution status

The FMOD 2.03.14 Windows, Linux, and macOS runtime files are present for local
migration and CI validation. Do not publish packages containing these files
until the project has confirmed that its FMOD license permits redistribution in
an asset extraction tool. The SDK EULA permits runtime redistribution only in
specified products and explicitly restricts distribution as part of a tool set.

## Rebuilding

Use `scripts/build-native.sh osx-arm64` on Apple Silicon macOS 15+ or
`FBX_SDK_ROOT=/path/to/fbxsdk scripts/build-native.sh linux-x64` on Debian 13
x86_64. The SDK itself and its installer archive must not be committed.
