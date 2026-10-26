# GitHub 发布指南

## 首次上传源码

此目录尚未初始化 Git 时，在仓库根目录执行以下命令。先检查暂存区，再提交：

```powershell
git init -b main
git add .
git status --short
git diff --cached --stat
git commit -m "Initial LocalChat source"
```

在 GitHub 创建空仓库，然后用 GitHub 提供的真实仓库地址添加 `origin` 并推送。不要把示例用户名或占位地址当成你的仓库。

`.gitignore` 已排除 `bin/`、`obj/`、`dist/`、本地 NuGet 缓存、数据库、配置及常见证书文件。它不会清理已经被 Git 跟踪的文件，也不能识别所有个人资料；发布前仍应检查暂存区。不要把整个工作目录直接拖到网页上传——网页上传不等同于按 `.gitignore` 筛选。

## Release 检查表

- [ ] 更新项目版本和 `CHANGELOG.md`，保持 tag 与版本一致。
- [ ] 在 Windows 上运行 `./build.ps1 -Test -Publish`，确认断言通过。
- [ ] 完成 `TESTING.md` 中的 UI 回归；卸载仅在可丢弃环境测试。
- [ ] 确认发布包无聊天数据、配置、个人路径截图和调试日志。
- [ ] 随分发内容保留 `LICENSE` 及所用第三方依赖要求的许可证/声明；自包含版本还包含 .NET 运行时。
- [ ] 必要时完成组织认可的代码签名，不把 PFX 或密码提交到仓库。
- [ ] 创建版本 tag 与 GitHub Release，将压缩后的 Windows x64 发布目录作为附件；不把 EXE 和依赖缓存提交到源码历史。

## CI

`.github/workflows/build.yml` 在 push、PR 或手动触发时构建、运行集成测试、打包并上传 Actions artifact。工作流仅需仓库读取权限，不会自动创建 Release 或推送代码。将 artifact 下载链接与正式 Release 区分，artifact 有保留期限。
