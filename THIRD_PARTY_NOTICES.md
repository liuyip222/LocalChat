# 第三方依赖

LocalChat 自身使用 MIT 许可，不改变第三方依赖的许可。下列为直接依赖及主要组件概览；完整依赖树可执行 `dotnet list src/LocalChat package --include-transitive` 查看。

| 组件 | 用途 | 许可来源 |
| --- | --- | --- |
| Microsoft.Data.Sqlite | SQLite .NET 数据访问 | [MIT，dotnet/efcore](https://github.com/dotnet/efcore/blob/main/LICENSE.txt) |
| SQLitePCLRaw | 原生 SQLite 绑定及初始化 | [Apache-2.0，SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) |
| SQLite | 原生数据库引擎 | [Public domain](https://www.sqlite.org/copyright.html)；同时保留 NuGet 包自带的 LICENSE |
| .NET / WPF | 桌面框架和自包含运行时 | [dotnet/runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)、[dotnet/wpf](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)，及各自第三方声明 |

源码通过 NuGet 引用组件，没有把它们的源代码复制到项目。分发自包含二进制时，应附带对应版本原始包/运行时要求的许可证和第三方声明；本表是索引，不能替代原文。

项目图标为自制；`tools/create_icon.py` 的 Pillow 依赖仅用于开发时重建资源，不随应用运行。
