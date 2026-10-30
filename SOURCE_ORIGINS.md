# 来源与致谢 / Sources and acknowledgements

LibrarySearch 的搜索、元件获取和文件转换链路使用或参考了以下项目。下面区分直接随仓库提供的源码、格式参考，以及在线数据服务。

| 项目 / 服务 | 链接 | 使用或参考范围 | 基线与声明 |
| --- | --- | --- | --- |
| easyeda2altium | https://github.com/korbin/easyeda2altium | C 编号元件获取、EasyEDA CAD 数据解析、转换为 Altium SchLib/PcbLib 的基础；本仓库提供经过修改的完整源码 | `ad21515ef0a7dd65cf0462458dc1ba8e8f47ac6d`；MIT OR Apache-2.0 |
| altiumrs（实际采用的 fork） | https://github.com/korbin/altiumrs | Altium 文件读写基础，用于转换输出和本地 SchLib/PcbLib/IntLib 元件扫描；本仓库提供修改版 | `2b45c1f91278680255d1f8dac067b95e0917f143`；MIT OR Apache-2.0 |
| altiumrs（上游元数据） | https://github.com/issus/altiumrs | vendored Cargo.toml 保留此 repository 地址；实际使用的源码快照来自上面列出的 korbin fork | 保留原始许可证与元数据，不混淆 fork 和上游 |
| easyeda2kicad.py | https://github.com/uPesy/easyeda2kicad.py | easyeda2altium 解析器注释明确列出的 EasyEDA shape / pin 数据格式参考，路径为 `easyeda2kicad/easyeda/easyeda_importer.py` | 格式参考；该 Python 项目未作为依赖随仓库打包，项目原始许可证见其仓库 |
| 嘉立创EDA / EasyEDA | https://lceda.cn/ · https://easyeda.com/ | 在线型号搜索和元件 CAD 数据服务；型号搜索请求实现在 `plugin/src/ModelSearch.cs` | 数据服务来源，区别于上述开源代码来源 |

## 可核对的来源记录

- 原始 `LibrarySearch-0.6.0-beta.1-complete.zip` 中的源码交接 `SOURCE-ORIGINS.md` 记录了上述 korbin 源码仓库及两个基线 revision。
- `converter/easyeda2altium-*/src/easyeda/parser.rs` 顶部记录 easyeda2kicad 的格式参考。
- `converter/dependencies/altiumrs-*/Cargo.toml` 保留 issus/altiumrs 的上游 repository 元数据。
- 原始版权和许可证保留在两个 Rust 源码目录、`plugin/licenses/`；Rust 其他依赖告知保留在 `plugin/licenses/rust-dependencies/`。

本项目后续增加了 AD 面板、多版本兼容、在线型号搜索交互、本地库分段索引、缓存和诊断，并对转换链路做了引脚、图形、封装、STEP 及接口一致性修复。参考和依赖关系不代表上游维护者为这些本地改动或发行物背书。

## English summary

The modified **korbin/easyeda2altium** and **korbin/altiumrs** snapshots are included in this repository. Their original MIT OR Apache-2.0 notices are retained. The EasyEDA parser explicitly credits **uPesy/easyeda2kicad.py** as a shape/pin format reference. The JLCEDA/EasyEDA service supplies online search results and CAD data; it is a data source, separate from software provenance. The altiumrs workspace still records **issus/altiumrs** as its upstream metadata; the actual snapshot origin is the korbin fork.
