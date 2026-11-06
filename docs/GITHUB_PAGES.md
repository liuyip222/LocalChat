# GitHub Pages 展示页

展示页是纯静态 HTML/CSS，无需 Node.js、.NET、npm 或打包工具。它只展示 LocalChat 并链接到 GitHub Releases，不运行桌面程序，不访问本地聊天记录，也不包含统计脚本或第三方字体。

## 文件

```text
docs/
├── .nojekyll                # 按原样发布静态文件，不使用 Jekyll
├── index.html              # 展示页入口与文案
├── assets/
│   ├── localchat.svg        # 从应用资源复制的自制图标
│   └── site.css            # 响应式样式
└── GITHUB_PAGES.md          # 本指南；其他开发文档继续保留
```

网页聊天窗口是 HTML/CSS 静态布局示意，已经明确标注，不是真实应用截图，也不能发消息。以后可以用脱敏的真实截图替换这部分内容。

## 1. 提交网页

在仓库根目录检查并提交：

```powershell
git diff --check
git add docs README.md
git diff --cached --stat
git commit -m "docs: add LocalChat GitHub Pages showcase"
git push origin main
```

先确认暂存区没有不想提交的其他改动。当前仓库默认分支为 `main`；如果之后更名，推送命令和 Pages 分支设置也需同步更换。

## 2. 启用 Pages（只需一次）

1. 打开仓库 <https://github.com/liuyip222/LocalChat>。
2. 进入 **Settings → Pages**。
3. 在 **Build and deployment** 下，将 **Source** 选择为 **Deploy from a branch**。
4. **Branch** 选择 `main`，右侧目录选择 **`/docs`**，点击 **Save**。
5. 在 **Actions** 中查看自动生成的 **pages build and deployment** 任务，等待构建和部署成功。
6. 回到 **Settings → Pages**，点击 **Visit site**。如果提供 **Enforce HTTPS** 选项，可启用它。

这里不选 `/(root)`，也不选自定义 **GitHub Actions** 源；现有桌面程序构建工作流不需要修改。本方案由 GitHub 自动完成 Pages 部署。

预期访问地址：

<https://liuyip222.github.io/LocalChat/>

这是项目站点，不需要新建 `liuyip222.github.io` 仓库，也不需要购买域名。页面启用前或部署未完成时，该地址可能返回 404。以 Pages 设置中的最终地址为准。

GitHub Free 可用于公开仓库的 Pages；私有仓库支持取决于账号套餐。需要有仓库维护或管理权限才能配置发布源。

## 3. 在仓库首页展示网站地址

仓库首页右侧 **About → 齿轮 → Website** 填入站点地址，或勾选使用 GitHub Pages 地址，再保存。

## 后续更新

- 修改页面文字：编辑 `docs/index.html`。
- 修改颜色、间距与手机布局：编辑 `docs/assets/site.css`。
- 替换网站 Logo：更新 `docs/assets/localchat.svg`；它是独立副本，不会随应用图标自动同步。
- 下载按钮指向 `/releases`，能同时展示稳定版和预览版，不硬编码版本号。新版本发布后无需改网页。发布二进制附件请继续按 [发布指南](RELEASING.md) 操作。
- 仓库改名或转移后，更新 HTML 内的 GitHub 链接、本指南与 README 中的站点地址。站内资源使用相对路径，兼容 `/LocalChat/` 子路径。
- 推送到 `main` 后，Pages 会自动重新部署，无需每次重新配置。

## 本地预览

直接用浏览器打开 `docs/index.html` 即可，不需要安装任何依赖。也可使用本机已有的 Python，在仓库根目录运行：

```powershell
python -m http.server 8080 --bind 127.0.0.1 --directory docs
```

浏览器访问 <http://127.0.0.1:8080>。退出预览时在终端按 `Ctrl+C`。核对桌面与窄屏布局、导航、FAQ 展开以及下载按钮。

## 排查

- **404**：先确认网页已推送，发布源为 `main` + `/docs`，目录中存在 `index.html`；检查 Actions 中 Pages 部署是否成功。
- **样式或图标丢失**：确认 `assets` 已提交；保留 `assets/site.css` 这类相对路径，不要改为 `/assets/site.css`。
- **更新未显示**：等待新部署成功，核对部署的 commit，然后强制刷新浏览器。
- **下载列表没有程序**：网页不会自动生成安装包。需要先创建 Release 并上传发布后的 Windows ZIP 附件；Source code 不是可执行程序包。
- **开发 Markdown 文档没有渲染为网页**：本方案关闭 Jekyll，这是预期行为。展示页中的文档链接指向 GitHub 上的 Markdown 阅读页。

`docs` 是公开发布目录，只放展示素材和公开文档，不放数据库、私人截图、个人配置或程序包。

官方说明：[配置发布源](https://docs.github.com/en/pages/getting-started-with-github-pages/configuring-a-publishing-source-for-your-github-pages-site)、[创建 GitHub Pages 站点](https://docs.github.com/en/pages/getting-started-with-github-pages/creating-a-github-pages-site)。
