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

修改 AD 接口调用时，需要自己安装相应版本 AD 或合法取得开发接口。复制 `targets.example.json` 为 `targets.local.json`，填入实际 System 目录，运行 `tools/Build-Matrix.ps1 -Config targets.local.json`。SDK 不随仓库提供。

## 验证边界

代码回归、二进制接口匹配及隔离安装检查不能替代实际 AD 工程验收。发版前检查面板显示、元件放置、连线、3D、ECO 以及保存重开。测试夹具使用独立临时目录，不应指向真实用户工程或元件目录。
