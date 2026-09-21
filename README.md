# MD · 跨平台 Markdown 阅读器

> 用 **.NET 10 + .NET MAUI** 重写的「Typora 式」阅读体验：
> 原生控件渲染、不内嵌浏览器内核、长文档流畅滚动、9 套内置主题，
> 所有个性化设置落到一个 JSON 文件。

**当前交付目标只有 Windows**，产物是一个 **71.8 MB 的单文件 `MD.exe`** ——双击即用，
目标机器不需要预装 .NET 运行时，也不需要 Windows App SDK。

Android 目标已停用（手机端 Markdown 阅读器已经足够多），代码与脚本保留，一行配置即可恢复。

---

## 为什么不用 Electron

|                        | Typora（Electron）        | **MD（.NET 10 + MAUI）**                       |
| ---------------------- | ------------------------- | ---------------------------------------------- |
| 渲染方式               | 内嵌 Chromium             | **操作系统原生控件 + 原生滚动**                |
| 安装形态               | 安装包 / AppImage         | **Windows 单文件 exe，双击即用**               |
| 长文档滚动             | 依赖浏览器合成层，易掉帧  | 原生虚拟化列表，**只实例化可视区域的块**       |
| 主题                   | CSS                       | **JSON —— 改配色不用重新编译**                 |
| 内存                   | 数百 MB 起                | 与文档长度基本无关                             |

## 功能

**阅读**

- CommonMark + GFM 全套：标题、段落、有序 / 无序 / 任务列表、引用（可嵌套）、围栏代码块、
  表格（含对齐）、分割线、图片、链接、脚注、定义列表、缩写、YAML Front Matter
- 行内样式：**粗体**、*斜体*、~~删除线~~、高亮、下划线、`行内代码`、下标、上标
- 代码块语法着色：自带轻量着色器，覆盖 C 系 / Python / Shell / SQL / JSON / YAML / XML 等，
  可选行号，一键复制
- 图片：相对路径、绝对路径、`http(s)`、`data:` URI 全部支持，可整体关闭图片加载

**导航**

- 大纲：标题树，点击跳转，滚动正文时自动高亮当前章节
- 文件夹工作区：打开一个目录，树形列出其中的 `.md`
- 最近打开的文件
- **标签页**：可切换 / 逐个关闭，启动时恢复上次的标签与文件夹
- 顶部阅读进度条，底部状态栏显示编码 / 换行 / 字数 / 行数 / 当前章节 / 进度 / 当前主题

**编辑**

- `Ctrl + E` 在阅读与编辑之间切换
- **保存严格保持原编码**：编码（UTF-8 / UTF-16 / GB18030 / GBK / Big5）、BOM 有无、
  换行风格（CRLF / LF / CR）全部按原样写回，字节级无损
- 原子写入（临时文件 + `File.Replace`），内容与磁盘一致时跳过写入

**Windows 系统集成**

- **单实例运行**：双击第二个 `.md` 会把文件送到当前窗口的新标签并提到前台，不再起第二个进程
- 右键「用 MD 打开」/「用 MD 打开文件夹」，并出现在右键「打开方式」与系统「默认应用」列表
- 拖拽文件到窗口打开
- 命令行：`MD.exe <文件>`、`MD.exe --folder <目录>`、`MD.exe --new-instance`，
  以及可脚本化的 `--register-shell` / `--unregister-shell` / `--default-apps`
- **系统标题栏跟随主题**：底色取自主题的 `toolbarBg`，亮暗主题下都与工具条连成一片

**外观**

- 9 套内置主题：GitHub 亮色 / Whitey / Pixyll / Newsprint 新闻纸 / Solarized 日光 /
  GitHub 暗色 / Night 深夜 / Vue 暗色 / Dracula 紫夜
- 可「跟随系统明暗」自动切换，工具栏一键亮暗互换
- 字号、行高、正文宽度可调；专注模式隐藏工具栏 / 侧栏 / 状态栏

**交互**

- 滚轮统一路由：指针停在代码块上时滚轮照常翻页（代码块横向滚动不会吃掉纵向滚轮）
- `Ctrl + 鼠标滚轮` 调字号

## 快捷键

| 操作                     | 快捷键                          |
| ------------------------ | ------------------------------- |
| 打开文件                 | `Ctrl + O`                      |
| 打开文件夹               | `Ctrl + Shift + O`              |
| 打开示例文档             | `Ctrl + D`                      |
| 保存                     | `Ctrl + S`                      |
| 关闭当前标签             | `Ctrl + W`                      |
| 重新加载                 | `F5`                            |
| 编辑 / 退出编辑          | `Ctrl + E`                      |
| 显示 / 隐藏侧栏          | `Ctrl + B`                      |
| 专注模式                 | `Ctrl + F`                      |
| 亮色 / 暗色互换          | `Ctrl + T`                      |
| 增大 / 减小 / 重置字号   | `Ctrl + =` / `Ctrl + -` / `Ctrl + 0` |
| 下一个 / 上一个标签      | `Ctrl + Tab` / `Ctrl + Shift + Tab` |
| 在资源管理器中显示       | `Ctrl + Shift + E`              |

## 构建与运行

前置：**.NET 10 SDK**（含 `maui-windows` workload）。

```bash
# 还原 + 编译（要求 0 警告 0 错误）
dotnet build MD.csproj

# 直接运行
dotnet run --project MD.csproj

# 发布为单文件自包含 exe（产物落到 dist/windows-win-x64-single/MD.exe）
dotnet publish MD.csproj -c Release -o dist/windows-win-x64-single

# 或走脚本（能额外指定 RID 与「依赖框架」模式）
powershell -ExecutionPolicy Bypass -File build/publish-windows.ps1
```

> ⚠ **不要给 `dotnet publish` 传 `-r` / `--self-contained`。**
> 这两个是全局 MSBuild 属性，会一并作用到 Android 目标上并引发 `NU1102`。
> RID、自包含、单文件等属性都写在 `MD.csproj` 的 Release 条件块里。

### 编码往返自检（回归测试）

「保存后编码必须不变」这条是靠测试守住的。工程只链接 `Markdown/` 下两个零 UI 依赖的文件，
所以能用普通控制台工程直接跑到真实实现：

```bash
dotnet run --project tests/MD.EncodingSelfCheck -c Release
# 期望：通过 73，失败 0（失败时进程退出码为 1）
```

覆盖 UTF-8 有 / 无 BOM、UTF-16 LE / BE、GBK、GB18030、Big5 × CRLF / LF，
断言「读取 → 原样写回字节完全一致」与「编辑一行后三元组不变」。

### 一个容易踩的坑

`.csproj` / `.slnx` 的 **XML 注释里不能出现两个连续的短横线**。
把带参数的命令行原样写进注释（就比如发布时用的那些选项）会让 MSBuild 直接报
`MSB4025：未能加载项目文件`，而且报错位置指向注释那一行，不容易一眼看出原因。
本项目已经在这上面栽过两次。

## 仓库结构

```
.
├─ MD.csproj                     项目文件（单目标 net10.0-windows10.0.19041.0）
├─ MD.slnx                       解决方案（.NET 10 的 XML 解决方案格式）
├─ MauiProgram.cs  App.cs        入口与主窗口（含单实例判断、窗口几何恢复）
├─ Models/                       领域模型 + 设置模型（零 UI 依赖）
├─ Markdown/                     解析、语法着色、编码嗅探（零 UI 依赖）
├─ Rendering/                    主题 → 颜色/字号/间距的换算与控件构建
├─ Services/                     设置落盘、主题目录、文档/文件夹服务、单实例、诊断日志
├─ Controls/  Views/             自绘控件与页面（界面全部用 C# 构建，不用 XAML）
├─ Platforms/Windows/            Windows 差异代码（标题栏、滚轮路由、Shell 集成、屏幕校验）
├─ Resources/                    矢量图标与启动图
├─ docs/                         用法 / 架构 / 主题 / 平台四份文档
├─ tests/MD.EncodingSelfCheck/   编码往返回归测试
└─ build/                        发布脚本
```

> **关于布局**：本仓库是**扁平布局** —— `MD.csproj` 就在仓库根。
> 本项目在本地的开发目录是 `src/MD/`，因此 `docs/` 里描述的路径
> 去掉 `src/MD/` 前缀后才是本仓库的路径（例如 `src/MD/Services/` → `Services/`）。
>
> **两种布局共用同一份工程文件，不需要维护两个版本**：
>
> - `MD.csproj` 里显式 `Remove` 掉了 `tests/**`。扁平布局下默认的 `**/*.cs` 通配会把
>   测试工程的源码以及它自己的 `obj/` 生成文件（`MD.EncodingSelfCheck.AssemblyInfo.cs`）
>   一并吞进来，与本工程生成的 `MD.AssemblyInfo.cs` 撞车，报 `CS0579「特性重复」`；
>   标准布局（`src/MD/`）下这条不匹配任何文件，是空操作。
> - `tests/MD.EncodingSelfCheck/MD.EncodingSelfCheck.csproj` 用 `Exists` 条件
>   自动选择要链接的源文件路径（`../../src/MD/Markdown/` 或 `../../Markdown/`）。
> - `build/publish-*.ps1` 自动探测 `src/MD/MD.csproj`，不存在则回落到 `MD.csproj`。
>
> 唯一要区分的是 **`MD.slnx`**（这种解决方案格式没有条件语法），本仓库里的版本指向 `MD.csproj`。

## 文档

| 文档 | 内容 |
| --- | --- |
| [`docs/README.md`](docs/README.md) | 功能全集、快捷键、目录结构、设置文件 JSON、已知限制 |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | 分层与渲染管线、虚拟化机制、平台差异层、读写路径 |
| [`docs/THEMES.md`](docs/THEMES.md) | 主题字段全表、派生色规则、自定义主题写法 |
| [`docs/PLATFORMS.md`](docs/PLATFORMS.md) | 平台支持现状、发布方式、Windows 定制细节、踩坑记录 |

## 平台支持

| 平台 | 状态 |
| --- | --- |
| Windows 10 1809+ / 11 | ✅ 当前交付目标，单文件 exe |
| Android | ⏸ 代码与脚本保留，改回多目标即可恢复 |
| macOS / macOS Catalyst | ⏸ 保留入口文件，未验证 |
| Linux | ❌ 不在 .NET MAUI 支持范围内 |

## 已知限制

- **标题栏没有菜单栏**：MAUI 10 的 Windows 端不渲染 `Page.MenuBarItems`，
  功能靠工具栏按钮 + 快捷键触达
- **滚动进度不生效**：`CollectionView.Scrolled` 在 MAUI 10 / Windows 上不触发，
  导致进度条与大纲高亮不实时更新，「记住阅读进度」失效
- 单文件体积 71.8 MB —— WinUI / Windows App SDK 的原生依赖决定，属平台约束
- 标题内的行内代码会沿用标题色（MAUI 的 `Span` 不支持背景色）
- 窗口位置只在**正常关闭**时记录；被强制结束则丢失（尺寸 / 主题不受影响）
- Windows 上**不能由程序自行把本程序设成 `.md` 的默认打开方式**：
  `UserChoice` 带哈希保护，这是系统安全设计。程序会注册干净的 ProgId 与右键菜单，
  并引导用户去系统设置页

## 说明

- 界面全部用 **C# 构建，不使用 XAML**（`Platforms/` 的平台清单除外）：
  主题热切换需要按颜色重建整棵可视树，命令式代码更直接，也免去打包 XAML 资源。
- 个性化设置（主题、窗口尺寸 / 位置、最近文件、阅读进度）在运行时写到
  `%APPDATA%\MD\settings.json`；诊断日志写在 `%APPDATA%\MD\md.log`。
  程序没有控制台窗口，出问题看日志。
