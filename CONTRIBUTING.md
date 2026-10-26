# 贡献指南

## 环境

- Windows 桌面环境、.NET 10 SDK。
- 任意支持 C# 的编辑器；也可打开 `LocalChat.slnx`。
- SQLite 等 NuGet 依赖由 `dotnet restore` 获取，无需单独安装数据库服务。

```powershell
dotnet restore LocalChat.slnx
dotnet build LocalChat.slnx -c Release
dotnet run --project tests/LocalChat.Tests -c Release --no-build
```

测试项目是控制台断言运行器，不是测试适配器，**不能用 `dotnet test` 的空结果代替运行测试**。非零退出码表示失败。

`build.ps1` 默认使用 PATH 中的 `dotnet`。SDK 在其他目录时可显式传入：

```powershell
.\build.ps1 -Test -DotNetPath 'D:\SDKs\dotnet\dotnet.exe'
.\build.ps1 -Publish -OutputDirectory artifacts/LocalChat
```

## 修改约定

- 保持现有的离线存储方式，不引入隐式联网或遥测。
- 数据库结构调整需要考虑旧数据兼容；不能直接丢弃历史数据库。
- 文件导入保持流式复制，避免将大文件全部读入内存；不在 UI 线程执行长耗时工作。
- 删除和卸载必须校验路径；不要递归删除用户指定的数据根目录。
- 遵循 `.editorconfig`。格式检查可使用 `dotnet format whitespace LocalChat.slnx --verify-no-changes --no-restore`。
- 新行为应补充集成测试或在 PR 中说明手工验收步骤。UI 与真实卸载仍需人工验证。

## 提交问题或 PR

说明复现步骤、预期与实际行为、Windows 版本及 LocalChat 版本。日志和截图先移除聊天内容、个人路径等信息，不要上传数据库、附件、密钥或证书。

## 图标

`src/LocalChat/Assets/` 包含 SVG、PNG 和多分辨率 ICO。图标为项目自制，正常构建直接使用提交的资源；如需重新生成 PNG/ICO：

```powershell
python -m pip install Pillow
python tools/create_icon.py
```

SVG 与 Python 绘制代码描述同一视觉标识，修改时保持一致。
