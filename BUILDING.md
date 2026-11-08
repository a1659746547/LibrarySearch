# 构建与验证

## 环境

Windows x64、Visual Studio 2022 C++ Build Tools 和 Windows SDK、Rust MSVC 工具链（发布构建使用 Rust 1.89.0）、.NET Framework 4.8 开发环境及 .NET 8 SDK。现代 .NET 引用包首次构建时需要 NuGet 连接。

## Rust 转换器

在 Visual Studio 的 Developer PowerShell / x64 Native Tools 环境中执行：

```powershell
./tools/Build-Converter.ps1 -Toolchain stable-x86_64-pc-windows-msvc
./tools/Build-Converter.ps1 -Toolchain stable-x86_64-pc-windows-msvc -Check
```

两个 EXE 输出到 `plugin/online/bin`，同时记录源码及二进制 SHA-256。`-Offline` 仅适用于依赖已经缓存的环境。

1.1.4-rc.1 验证包使用 Rust 1.99.1 Windows GNU 工具链构建。也可使用 `-Toolchain stable-x86_64-pc-windows-gnu -NativeTools <w64devkit/bin>`；构建脚本会选择配套链接器。该构建的两个 EXE 不依赖额外的 MinGW DLL，Windows 系统 DLL 仍需由系统提供。

## 共享核心

```powershell
./tools/Build-PublicCores.ps1
```

此脚本构建 .NET Framework、.NET 6、.NET 8 共享核心。可通过 `-DotNet` 指定 dotnet.exe。既有适配器的程序集绑定版本为 `1.1.0-beta.2`，属于兼容标识；产品版本由 `plugin/Release.psd1` 和 `ReleaseInfo.Version` 共同决定。

## 沿用已验证宿主

仓库中保留既有宿主适配 DLL 和原始核心作为兼容性夹具（均为本项目产物，不是 Altium SDK）。它们与对应源码、注册文件及 SHA-256 一起用于检验接口是否一致。日常改动搜索、转换或面板时，可重新构建共享核心并验证这些宿主：

```powershell
python -m pip install --target .artifacts/metadata dnfile==0.18.0
./tools/Complete-CompatibleMatrix.ps1 -MetadataLibraries .artifacts/metadata -Python python
```

记下脚本输出的构建目录，运行对应回归和打包命令：

```powershell
./tools/Test-Regression.ps1 -BuildDirectory <构建目录>
./tools/Test-Regression.ps1 -BuildDirectory <构建目录> -ManagedRuntime net6windows -DotNet dotnet
./tools/Test-Regression.ps1 -BuildDirectory <构建目录> -ManagedRuntime net8windows -DotNet dotnet
./tools/Package-Matrix.ps1 -BuildDirectory <构建目录>
```

## 重新编译 AD 宿主

总库的离线集成测试可通过 `$env:LIBRARYSEARCH_UNIFIED_FIXTURE` 指定夹具目录，再执行上述三种运行时的 Test-Regression.ps1。目录需包含 `C15127-paired.SchLib` 和 `C15127-paired.PcbLib`（一对含嵌入 STEP 的测试下载库）；测试只复制到隔离目录，不修改原始夹具，不连接网络或启动 AD。Rust 的 `-Check` 同时执行自建库的合并测试。

修改 AD 接口调用时，需要自己安装相应版本 AD 或合法取得开发接口。复制 `targets.example.json` 为 `targets.local.json`，填入实际 System 目录，运行 `tools/Build-Matrix.ps1 -Config targets.local.json`。SDK 不随仓库提供。

## 验证边界

NativeRefreshTests 会在独立测试输出目录编译一个 SDK 形状夹具，验证共享核心调用 AD 刷新 API 的顺序和线程。该夹具不是 Altium SDK，不随安装包分发；真实 AD 中的缓存刷新、连续放置和 ECO 仍需实机确认。

独立检查 PcbLib 名称映射可安装开发工具 `olefile` 后执行 `python tools/check_pcblib_section_keys.py <PcbLib路径>`，与 Rust 读写实现分开验证。`LIBRARYSEARCH_RECOVERY_FIXTURE` 可指定一份 rc.3 下载目录的只读副本（Index / Components / Unified），启用现场恢复回归；测试先复制到自身临时目录，不修改该副本。

代码回归、二进制接口匹配及隔离安装检查不能替代实际 AD 工程验收。发版前检查面板显示、元件放置、连线、3D、ECO 以及保存重开。测试夹具使用独立临时目录，不应指向真实用户工程或元件目录。

## 后台验证

Test-Regression.ps1 默认使用 Headless 模式，不显示测试窗体，不抢焦点。Default3dTests 只创建隐藏控件并将异常作为进程错误输出，避免弹出 .NET 异常窗口。可见界面测试需要显式传入 `-Headless:$false`；本项目当前交互测试由用户执行。
