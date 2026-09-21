# MD · 跨平台 Markdown 阅读器

> 用 **.NET 10 + .NET MAUI** 重写的「Typora 式」阅读体验：原生渲染、不内嵌浏览器内核、
> 十万行文档流畅滚动、9 套内置主题、所有个性化设置落到一个 JSON 文件。

> **当前交付目标只有 Windows**，产物是一个 71.7 MB 的单文件 `MD.exe`。
> Android 目标已停用（手机端 Markdown 阅读器已足够多），代码与脚本保留，一行配置即可恢复；
> 详见 [`PLATFORMS.md`](PLATFORMS.md)。

---

## 一、它解决什么问题

Typora 用 Electron：排版能力强，但代价是自带一整个 Chromium（安装包 100 MB+、内存占用高）。

| | Typora（Electron） | **MD（.NET 10 + MAUI）** |
| --- | --- | --- |
| 渲染方式 | Chromium 内嵌浏览器 | **操作系统原生控件 + 原生滚动** |
| 安装形态 | 安装包 / AppImage | **Windows 单文件 exe（71.7 MB），双击即用** |
| 长文档滚动 | 依赖浏览器合成层，易掉帧 | 原生虚拟化列表，**只实例化可视区域的块** |
| 主题 | CSS | **JSON（改配色不用重新编译）** |
| 内存 | 数百 MB 起 | 与文档长度基本无关 |

## 二、功能

**阅读**

- CommonMark + GFM 全套：标题、段落、有序 / 无序 / 任务列表、引用（可嵌套）、围栏代码块、
  表格（含对齐）、分割线、图片、链接、脚注、定义列表、缩写、YAML Front Matter
- 行内样式：**粗体**、*斜体*、~~删除线~~、==高亮==、++下划线++、`行内代码`、下标、上标
- **代码块语法着色**：自带轻量着色器，覆盖 C 系 / Python / Shell / SQL / JSON / YAML / XML 等，
  可选行号，一键复制
- 图片：相对路径、绝对路径、`http(s)`、`data:` URI 全部支持，可整体关闭图片加载

**导航**

- 左侧「大纲」：标题树，点击跳转；滚动正文时自动高亮并滚动到当前章节
- 左侧「文件」：打开一个文件夹当工作区，树形列出其中的 `.md`，点一下就打开
- 左侧「最近」：最近打开的文件，可单独移除
- **标签页**：打开的文档以标签排列，可切换 / 逐个关闭；启动时恢复上次的标签与文件夹
- 顶部 2px 阅读进度条，底部状态栏显示编码 / 换行 / 字数 / 行数 / 当前章节 / 进度 / 当前主题

**编辑**

- 轻量编辑：`Ctrl + E` 在阅读与编辑之间切换，编辑区显示路径 · 编码 · 换行 · 行数 · 字符数
- **保存严格保持原编码**：编码（UTF-8 / UTF-16 / GB18030 / GBK / Big5）、BOM 有无、
  换行风格（CRLF / LF / CR）全部按原样写回，字节级无损；编辑器新输入的内容会按原换行风格补行
- 原子写入（临时文件 + `File.Replace`），内容与磁盘一致时跳过写入

**系统集成（仅 Windows）**

- **单实例运行**：已在运行时，双击第二个 `.md` 会把文件送到当前窗口的新标签里，
  并把窗口提到前台；不会再起第二个进程（`--new-instance` 可强制开新进程）
- 右键「用 MD 打开」/「用 MD 打开文件夹」
- 出现在右键「打开方式」里，也能在系统「默认应用」列表中找到 MD
- 拖拽文件到窗口打开（Windows）
- 命令行：`MD.exe D:\docs\readme.md`、`MD.exe --folder D:\docs`、`MD.exe --new-instance`，
  以及可脚本化的 `--register-shell` / `--unregister-shell` / `--default-apps`

**外观**

- 9 套内置主题：GitHub 亮色 / Whitey / Pixyll / Newsprint 新闻纸 / Solarized 日光 /
  GitHub 暗色 / Night 深夜 / Vue 暗色 / Dracula 紫夜
- 可「跟随系统明暗」自动切换；工具栏 ◐ 一键亮暗互换
- 字号、行高、正文宽度可调（设置面板实时预览）
- **标题栏也跟随主题**：系统绘制的窗口标题栏底色取自主题的 `toolbarBg`，
  亮暗主题下都与顶部工具条连成一片
- 专注模式：隐藏工具栏 / 侧栏 / 状态栏

**交互**

- 鼠标滚轮统一路由：指针停在代码块上时滚轮照常翻页（代码块横向滚动不会吃掉纵向滚轮）
- `Ctrl + 鼠标滚轮` 调字号（连续滚动会合并成一次重绘，不会卡）
- 功能全部可达：工具栏按钮 + 快捷键（标题栏菜单栏在 Windows 上不渲染，见「十、已知限制」）

## 三、快捷键

| 操作 | 快捷键 |
| --- | --- |
| 打开文件 | `Ctrl + O` |
| 打开文件夹 | `Ctrl + Shift + O` |
| 打开示例文档 | `Ctrl + D` |
| 保存 | `Ctrl + S` |
| 关闭当前标签 | `Ctrl + W` |
| 重新加载 | `F5` |
| 编辑 / 退出编辑 | `Ctrl + E` |
| 显示 / 隐藏侧栏 | `Ctrl + B` |
| 专注模式 | `Ctrl + F` |
| 亮色 / 暗色互换 | `Ctrl + T` |
| 增大 / 减小字号 | `Ctrl + =` / `Ctrl + -` |
| 重置字号 | `Ctrl + 0` |
| 下一个 / 上一个标签 | `Ctrl + Tab` / `Ctrl + Shift + Tab` |
| 在资源管理器中显示当前文件 | `Ctrl + Shift + E` |
| 调字号（鼠标） | `Ctrl + 鼠标滚轮` |

## 四、技术选型

原始需求是「至少 Windows + Android，Linux / macOS 可选，高性能高颜值，尽量单 exe」。三个候选：

| | .NET MAUI | Avalonia UI | Uno Platform |
| --- | --- | --- | --- |
| Windows | ✅ WinUI 3 | ✅ | ✅ WinUI 3 |
| Android | ✅ | ⚠️ 实验性（Avalonia.Android 非稳定线） | ✅ |
| macOS | ✅ Mac Catalyst | ✅ | ✅ |
| Linux | ❌ | ✅ | ✅（Skia 后端） |
| 官方支持 | ✅ 微软官方 | 社区 | 第三方 |
| Windows 单文件 exe | ✅ **71.7 MB** | ✅（明显更小） | ⚠️ 依赖较多 |

**当时的结论是选 .NET MAUI**：需求里 Windows 与 Android 都是「必须有」，而 Avalonia 的
Android 支持不在稳定线上、Uno 在桌面端仍是套壳 WinUI，只有 MAUI 对这两个平台都是一等公民。

**后续变更**：Android 目标已停用（手机端 Markdown 阅读器已足够多）。这使选型的理由明显变弱 ——
如果**同时**在意 Linux 支持或 exe 体积，Avalonia 更划算：它的 Windows 单文件可以做到
二十几 MB，而 MAUI 必须打包 Windows App SDK 的原生依赖，自包含后是 71.7 MB。

仍留在 MAUI 的理由只有两条，取舍如下：

| 留在 MAUI | 迁到 Avalonia |
| --- | --- |
| WinUI 3 原生观感，系统字体 / 滚动条 / 拖拽天然一致 | 自绘一套，观感需自己对齐 |
| 将来恢复移动端不用重写 UI 层 | 移动端不是 Avalonia 的强项 |
| — | 单文件体积可降到 1/3 左右，且顺带支持 Linux |

**如果 Linux 不是需求、也不打算恢复移动端，迁移到 Avalonia 是更优解。**
业务逻辑（`Models/` + `Markdown/` + 大部分 `Services/`）约 60% 可直接复用，
需要重写的是 `Rendering/` 与 `Views/` —— 详见 [`PLATFORMS.md`](PLATFORMS.md) 第五节。

## 五、目录结构

```
MD/
├─ MD.slnx                          解决方案（.NET 10 新的 XML 解决方案格式）
├─ src/MD/
│  ├─ MD.csproj                     单目标 net10.0-windows（改回复数即恢复 Android / macOS）
│  ├─ MauiProgram.cs                入口：先跑外壳指令，再建 MAUI 应用
│  ├─ App.cs                        主窗口（窗口几何信息恢复 + 首个窗口上色）
│  ├─ Models/                       领域模型（零 UI 依赖）
│  │  ├─ MarkdownModel.cs           Block / Inline 领域模型（与渲染层解耦）
│  │  ├─ ThemeDefinition.cs         主题模型（配色 + 排版）+ ThemeJson 序列化选项
│  │  ├─ AppSettings.cs             个性化设置模型（含 Workspace 段）
│  │  ├─ DocumentTab.cs             标签页模型（路径 / 是否已改动 / 编码摘要）
│  │  ├─ FolderModel.cs             文件夹树模型（文件节点 / 目录节点）
│  │  └─ FiniteDoubleConverter.cs   NaN / Infinity 降级为 null，保证配置一定写得下去
│  ├─ Markdown/                     解析与编码层（零 UI 依赖）
│  │  ├─ MarkdownParser.cs          Markdig AST → 领域模型，产出大纲与统计
│  │  ├─ SyntaxHighlighter.cs       零依赖语法着色器
│  │  ├─ TextEncodingDetector.cs    UTF-8 / UTF-16 / GB18030 编码嗅探 + 往返校验
│  │  └─ TextFileFormat.cs          「编码 + BOM + 换行」三元组的读取与照原样写回
│  ├─ Rendering/
│  │  ├─ RenderContext.cs           主题 → 颜色 / 字号 / 间距 的唯一换算点
│  │  ├─ InlineRenderer.cs          行内元素 → FormattedString
│  │  ├─ BlockRenderer.cs           块级元素 → 原生控件
│  │  └─ BlockHost.cs               虚拟化宿主（按需构建控件树）
│  ├─ Services/
│  │  ├─ SettingsService.cs         settings.json 原子写入 + 防抖
│  │  ├─ DiagnosticsLog.cs          运行日志（GUI 程序没有控制台，出问题只看这里）
│  │  ├─ ThemeCatalog.cs            内置 + 用户主题加载（模板覆盖机制）
│  │  ├─ BuiltinThemes.cs           内置主题（JSON 常量，保证单 exe 自包含）
│  │  ├─ DocumentService.cs         异步加载 + 解析缓存
│  │  ├─ FolderService.cs           文件夹扫描（递归/非递归、扩展名过滤、排序）
│  │  ├─ ShellService.cs            Windows 右键菜单 / 默认应用注册（只写 HKCU）
│  │  ├─ ShellCommandLine.cs        --register-shell / --unregister-shell / --default-apps
│  │  ├─ SingleInstance.cs          单实例：命名互斥体 + 命名管道转交参数
│  │  ├─ AppPaths.cs                各平台配置目录
│  │  ├─ AppHost.cs                 服务定位（静态容器）
│  │  ├─ PlatformLauncher.cs        打开链接 / 目录 / 定位文件
│  │  └─ WindowHelper.cs            窗口几何信息读写（含坐标可信性校验与屏幕内保护）
│  ├─ Controls/                     UiKit / MdSwitch 等自绘控件
│  ├─ Views/
│  │  ├─ ReaderPage.cs              主阅读页（工具栏 / 侧栏 / 阅读区 / 状态栏 / 设置层）
│  │  ├─ TabStrip.cs                多标签栏（文件夹模式下的页签管理）
│  │  ├─ EditorView.cs              轻量编辑器（编码/换行保持的保存路径）
│  │  ├─ SidebarView.cs             大纲 / 文件 / 最近 三页签
│  │  ├─ SettingsPanel.cs           设置覆盖层
│  │  ├─ WelcomeView.cs             空状态首屏
│  │  ├─ Toast.cs                   轻提示
│  │  └─ SampleDocument.cs          内置示例文档
│  ├─ Platforms/
│  │  ├─ Windows/                   Windows 差异代码
│  │  │  ├─ WindowsInterop.cs       窗口发现与矩形 / 定位的公共 Win32 互操作
│  │  │  ├─ WindowsTitleBar.cs      标题栏跟随主题（DWM + 去 Mica + 根面板底色）
│  │  │  ├─ WindowsActivation.cs    把已有窗口提到前台（单实例打开文件时用）
│  │  │  ├─ WindowsScreen.cs        窗口跑到屏幕外时搬回主显示器中央
│  │  │  ├─ WindowsWheelRouter.cs   滚轮路由（子滚动容器吃不下时转交祖先）
│  │  │  ├─ WindowsShellIntegration.cs  注册表读写实现
│  │  │  ├─ WindowsFolderPicker.cs  文件夹选择对话框
│  │  │  ├─ WindowsFileSaver.cs     另存为对话框
│  │  │  ├─ WindowsDropTarget.cs    文件拖入
│  │  │  ├─ App.xaml(.cs)           平台入口（唯一保留的 XAML，模板自带）
│  │  │  ├─ app.manifest / Package.appxmanifest
│  │  ├─ Android/  MacCatalyst/     清单与入口（当前不编译，保留以便恢复）
│  └─ Resources/                    矢量图标与启动图（SVG）
├─ build/
│  ├─ publish-windows.ps1           单文件 exe 发布（默认）
│  └─ publish-android.ps1           APK 发布（需先恢复 Android 目标）
├─ tests/
│  └─ MD.EncodingSelfCheck/         编码 / BOM / 换行 往返自检（纯控制台，73 用例）
├─ dist/                            发布产物（dist/windows-win-x64-single/MD.exe）
└─ docs/
   ├─ README.md  ARCHITECTURE.md  THEMES.md  PLATFORMS.md
```

> 界面全部用 **C# 构建，不使用 XAML**。原因：主题热切换需要按颜色重建整棵可视树，
> 命令式代码比 XAML 绑定更直接可靠；同时单文件发布不需要额外打包 XAML 资源。
> 平台清单（`Platforms/`）仍沿用模板的 XAML。

## 六、构建与运行

```bash
# 环境
dotnet --version                    # 需要 10.x
dotnet workload install maui-windows

# 编译（单目标，不需要 -f）
dotnet build src/MD/MD.csproj

# 运行
dotnet run --project src/MD/MD.csproj
```

### 编码自检（回归测试）

需求里有一条硬指标：**编辑保存后文件编码不能变**。这条不靠肉眼开文件对比，而是有一个
自动化自检工程 —— `tests/MD.EncodingSelfCheck`。它用 `<Compile Include>` **直接链接**
MD 里真实的 `Markdown/TextEncodingDetector.cs` 与 `Markdown/TextFileFormat.cs`
（这两个文件零 UI 依赖，所以普通控制台工程能编进来），跑的就是线上那套实现：

```bash
dotnet run --project tests/MD.EncodingSelfCheck -c Release
# 期望末行：结果：通过 73，失败 0（失败时退出码为 1）
```

断言两条核心不变量：

1. 读进来再原样写回去，**字节必须完全一致**；
2. 在编辑器里改一行后保存，**编码 / BOM / 换行三者都不能变**，且内容能无损读回。

用例矩阵是 {UTF-8 有 BOM、UTF-8 无 BOM、UTF-16 LE、UTF-16 BE、GBK、GB18030、Big5}
× {CRLF、LF}，另外单测换行归一与「有损编码必须被识别出来」（例如 Big5 遇到 emoji
必须报 `lossy=true`，**不能悄悄把文件写坏**）。

> 改了编码相关代码就顺手跑一遍，比手工验 80 个文件可靠得多。

## 七、发布为单文件 exe

```powershell
powershell -ExecutionPolicy Bypass -File build\publish-windows.ps1
# 产物：dist\windows-win-x64-single\MD.exe（71.7 MB，目录下只有这一个文件）
```

等价的原始命令：

```bash
dotnet publish src/MD/MD.csproj -c Release -o dist/windows
```

> ⚠️ 不要加命令行 `-r win-x64` 或 `--self-contained`：它们是**全局** MSBuild 属性。
> 现在项目只有 Windows 一个目标所以看不出问题，但一旦恢复 Android 目标，
> 它们会让 Android 去还原 `Mono.win-x64` 而报 `NU1102`。
> RID / 自包含 / 单文件等属性统一写在 `MD.csproj` 的 Release 条件块里，
> 换架构请用自定义属性 `-p:MdWindowsRuntime=win-arm64`。

体积换便携：`-Mode framework` 得到更小的包，但需要用户预装
[.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0) 与 Windows App SDK 运行时。

Android（需先恢复 Android 目标，见 [`PLATFORMS.md`](PLATFORMS.md) 第四节）：

```powershell
powershell -ExecutionPolicy Bypass -File build\publish-android.ps1
```

## 八、个性化设置文件

首次运行自动生成，路径：

| 平台 | 路径 |
| --- | --- |
| **Windows（当前）** | `%APPDATA%\MD\settings.json` |
| macOS（恢复后） | `~/Library/Application Support/MD/settings.json` |
| Android（恢复后） | 应用私有目录 `files/MD/settings.json` |

同目录下还有 `md.log`（运行日志）与 `themes/`（用户自定义主题）。

内容示例（窗口尺寸 / 位置 / 最大化、主题、排版、最近文件、工作区、每个文件的阅读进度）：

```json
{
  "version": 1,
  "appearance": {
    "themeId": "github-dark",
    "darkThemeId": "github-dark",
    "followSystemTheme": false,
    "language": "zh-CN"
  },
  "window": {
    "x": 60, "y": 60, "width": 1500, "height": 940, "maximized": false,
    "sidebarVisible": true, "sidebarWidth": 300
  },
  "reading": {
    "fontSizeDelta": 0, "lineHeightDelta": 0, "contentWidthMode": "auto",
    "focusMode": false, "statusBarVisible": true, "syncOutline": true,
    "codeLineNumbers": false, "loadImages": true,
    "scrollPositions": { "D:\\docs\\readme.md": 0.4231 },
    "lastSeen": { "D:\\docs\\readme.md": 639020000000000000 }
  },
  "files": {
    "lastFile": "D:\\docs\\readme.md",
    "recent": [ { "path": "D:\\docs\\readme.md", "name": "readme.md", "openedAt": "2026-09-21T19:40:00+08:00" } ],
    "maxRecent": 20
  },
  "workspace": {
    "folderPath": "D:\\docs",
    "folderRecursive": true,
    "sidebarTab": "outline",
    "openTabs": [ "D:\\docs\\readme.md", "D:\\docs\\guide.md" ],
    "activeTab": "D:\\docs\\guide.md"
  }
}
```

几个字段的说明：

| 字段 | 说明 |
| --- | --- |
| `reading.contentWidthMode` | 正文列宽：`auto`（跟随主题舒适宽度并按窗口自适应放宽）/ `narrow`(700) / `standard`(900) / `wide`(1150) / `full`（铺满）。**不是**百分比数值 |
| `reading.scrollPositions` | 文件路径 → 滚动进度（0~1），用于「记住阅读进度」 |
| `workspace.sidebarTab` | 侧栏当前页签：`outline` / `recent` / `folder` |
| `workspace.openTabs` | 打开文件夹后已打开的标签页（按显示顺序），下次启动原样恢复 |
| `workspace.activeTab` | 当前激活的标签（未打开文件时也为单个文件路径，用于启动时自动重开） |
| `window.x` / `window.y` | `double?`，`null` = 尚无记录。首次运行且从未关闭过窗口时**整个键被省略**，正常关闭一次后即出现 |

> `window.x` / `window.y` 在**首次运行且从未关闭过窗口**时会被省略
> （模型里是 `double?`，`null` 表示「尚无记录，由系统决定位置」，序列化时跳过）。
> 正常关闭一次窗口后它们就会出现。

写入策略：临时文件 + 原子替换，防止断电写坏配置；变更后 500ms 防抖落盘，
退出前强制 flush。

写入失败不会静默：`Services/SettingsService.cs` 的任何序列化/写盘异常都会记进
`md.log`（见 [`PLATFORMS.md`](PLATFORMS.md) 第三节）。

## 九、自定义主题

把 JSON 丢进 `<配置目录>\themes\` 即可，文件名随意，同 `id` 会覆盖内置主题：

```json
{
  "id": "my-theme",
  "name": "我的主题",
  "isDark": false,
  "colors": {
    "editorBg": "#FFFDFCF8",
    "text": "#FF2B2A27",
    "link": "#FF3B6E9E",
    "accent": "#FF3B6E9E"
  },
  "typography": { "baseFontSize": 17, "lineHeight": 1.9, "contentMaxWidth": 760 }
}
```

**只需要写想改的字段**，其余自动继承同明暗的模板。完整字段表见 [`THEMES.md`](THEMES.md)。

## 十、已知限制

平台与体积：

- Windows 单文件体积 71.7 MB（WinUI / Windows App SDK 的原生依赖决定），
  这是平台约束而非实现问题；在意体积的话 Avalonia 可做到二十几 MB
- 只交付 Windows：Android 目标已停用（可一行配置恢复），Linux 不在 .NET MAUI 支持范围内

功能边界：

- 定位是**阅读器 + 轻量编辑**，不含导出 / 多文件批量替换等重编辑能力
- 编辑保存**只保证编码 / BOM / 换行三元组不变**，不负责 Markdown 语法规范化
- 标题内的行内代码会沿用标题色（MAUI 的 `Span` 不支持背景色，无法画出行内代码底色）
- Windows 上**不能由程序自行把本程序设成 `.md` 的默认打开方式**：`UserChoice` 有哈希保护，
  这是系统安全设计。本程序的做法是注册干净的 ProgId + 右键菜单，然后**引导去系统设置页**
- 窗口位置只在**正常关闭**时记录；被任务管理器强制结束则会丢失（尺寸/主题不受影响）
- 程序默认单实例。要用两个窗口并排对比文档，得加 `--new-instance`
  —— 此时两个进程共用同一份 `settings.json`，窗口几何信息与会话状态会互相覆盖
  （文档内容不受影响，但「恢复上次标签」的结果取决于哪个窗口最后关闭）
- 窗口位置做过防呆：不可能出现「窗口跑到屏幕外、看得见进程却摸不到窗口」的状态。
  相关机制（拒绝 Win32 的 `-32000` 哨兵、按真实矩形确认可达性）见
  [`PLATFORMS.md`](PLATFORMS.md) 的「窗口为什么会跑到屏幕外」

### 已定位、尚未修的两个缺陷（均为框架层，非本轮改动引入）

1. **标题栏左侧没有菜单栏**——`AttachMenu()` 确实把 3 项挂到了 `Page.MenuBarItems`
   （日志有 `菜单已挂载 3 项`），但 MAUI 10 的 Windows 端**不渲染** `Page.MenuBarItems`，
   而 `Window.MenuBarItems` 这个 API 已经从 MAUI 里移除了。目前标题栏只有窗口标题。
   替代路径：所有功能都有快捷键与工具栏按钮，不受影响。

2. **滚动进度不生效**——`CollectionView.Scrolled` 事件在当前 MAUI 10 / Windows 组合下
   **完全不触发**（`md.log` 里一条 `[滚动]` 都没有）。后果：
   状态栏百分比、进度条、大纲自动高亮都不更新，`settings.json` 的
   `reading.scrollPositions` 始终为空 →「记住阅读进度」实际失效。
   已加限流日志便于后续排查（见 [`PLATFORMS.md`](PLATFORMS.md)）。

> 这两条都已写进 `docs/PLATFORMS.md` 的「已知缺陷」一节，并在 `md.log` 里留有痕迹。
