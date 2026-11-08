# LibrarySearch 1.1.3 搜索依赖修复

对应反馈：LibrarySearch 1.1.2、Altium Designer 25.0.2、Windows 10 22H2；搜索 ESP32 时显示无法加载 System.Web.Extensions 4.0.0.0。

## 修复内容

1.1.2 的旧运行时核心通过 JavaScriptSerializer 引用 ASP.NET 的 System.Web.Extensions。面板可先加载，但第一次执行型号搜索的 JSON 解析时才会触发该程序集的加载。现代运行时不提供这个程序集。用发布包中的原版核心在 .NET 6.0.30 中调用 ModelSearchClient.Parse，已复现与截图一致的 FileNotFoundException。

这证明缺少依赖的故障路径可以复现，但尚未取得反馈电脑的安装状态和运行时日志，不能认定其安装器选错了宿主。1.1.3 移除了旧核心对该程序集的依赖：使用 System.Runtime.Serialization 中的 JSON 读取器，保留字典/数组、公共字段、属性、构造默认值和既有配置格式。现代核心继续使用 System.Text.Json。各版本均使用 LibrarySearch 自己的序列化命名空间，避免类型解析混淆。

修复版旧核心在 .NET 6.0.30 和 .NET 8.0.15 中运行相同搜索及设置读写检查均通过；.NET Framework 下 16 项 JSON 兼容检查通过。原有经典外观缓存键仍为 9f76e98439de2e7af4374a531d6816ceee8529dc30561ff278a1a60f09c6c795。

## 安装与验收

1. 保存工程，完全退出所有 AD 实例。
2. 完整解压 LibrarySearch-1.1.3.zip，运行 Install.cmd，选择实际使用的 AD 25.0.2 的 X2.exe。
3. 重启 AD，打开 Panels → LibrarySearch，搜索 ESP32，再搜索一个 C 编号。
4. 检查切换分类、已下载元件和设置保存；确认搜索不再出现 System.Web.Extensions 错误。
5. 如仍异常，使用设置中的诊断导出功能，保留完整报错和安装输出；不要手动向 AD 目录复制网上的 System.Web.Extensions.dll。

安装器沿用升级备份流程，元件库、工程库和用户设置保留。程序集文件名中的 1.1.0-beta.2 是旧适配器的绑定标识，实际产品版本为 1.1.3。

## 验证边界

- 三个运行时核心均已重新编译；7 个原有 AD 适配器共 133 个成员引用校验通过。
- Rust 源码与两个原生程序哈希校验通过；本次未改动或重新构建转换器。
- .NET 8 全套回归到达完成标记；.NET 6 的最后一项渲染测试单独重跑后完成（69 项），其余测试均有完成记录。两种现代运行时各记录 251 条 PASS。
- .NET Framework 的版本、更新和 JSON 共 68 项通过，后续界面测试程序在本机未成功运行；不能计为完整界面回归通过。
- AD24 24.3.1.35、AD25 25.7.1.20、AD26 26.1.1.7 的隔离安装、哈希、卸载检查通过，仅使用复制的 SDK/启动器夹具，不改动真实安装。
- 未在反馈用户的 Windows 10 22H2 / AD 25.0.2 中执行实际验收；本机 AD25 安装夹具是 25.7.1.20，不能替代 25.0.2 的界面验证。
- 本包尚未上传官网或 GitHub Release。

## 开发复现

先按 BUILDING.md 构建三个核心，然后运行 tools/Test-RuntimeDependency.ps1，分别指定原版和修复版旧核心，以及便携 .NET 的 dotnet.exe。脚本使用独立临时目录，无需启动 AD。

完整回归使用 tools/Test-Regression.ps1。JsonCompatibilityTests 增加依赖检查、历史配置默认值、大小写字段名、中文与转义、非 XML 字段名、匿名请求对象、深度/大小/尾部内容校验及 ESP32 搜索结果夹具。
