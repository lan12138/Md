# MD · 平台支持与部署

> 当前交付目标**只有 Windows**。本文说明怎么构建/发布、产物长什么样，
> 以及 Android / macOS / Linux 若要恢复需要做什么。

---

## 一、交付目标

| 平台 | 状态 | 框架 | 产物 |
| --- | --- | --- | --- |
| **Windows 10 1809+ / 11** | ✅ **当前唯一交付目标** | .NET MAUI（WinUI 3 / Windows App SDK） | 单文件 `MD.exe`（71.7 MB），双击即用 |
| Android | ⏸️ **已停用**（代码保留） | .NET MAUI | 恢复方式见第四节 |
| macOS | ⏸️ 未启用 | .NET MAUI（Mac Catalyst） | 需 macOS + Xcode |
| Linux | ❌ 官方不支持 | 需另建 Avalonia 前端 | 见第五节 |

**Android 为什么停用**：手机端支持 Markdown 的应用已经足够多，自研没有收益。
`Platforms/Android/` 清单与 `build/publish-android.ps1` 都原样保留，
`MD.csproj` 里改成多目标即可恢复 —— 见第四节。

### 关于「只有 Windows 了，为什么还用 MAUI」

Android 退出后，选型的理由确实变弱了：如果**同时**想要 Linux，或者在意 exe 体积，
Avalonia 会是更划算的选择（单文件可以做到二十几 MB，而 MAUI 因为要打包
Windows App SDK 原生依赖，自包含后是 71.7 MB —— 这是平台约束，不是实现问题）。

仍然留在 MAUI 的理由只有两条：一是 WinUI 3 的原生观感与系统字体/滚动条天然一致；
二是将来若要恢复移动端，不需要重写 UI 层。**如果这两条都不重要，迁移到 Avalonia 更优。**

---

## 二、Windows

### 前置

```bash
dotnet --version                    # 需要 10.x
dotnet workload install maui-windows
```

不需要额外 SDK：Windows App SDK 由 NuGet 自动还原。

### 调试运行

```bash
dotnet build src/MD/MD.csproj -c Debug
dotnet run   --project src/MD/MD.csproj
```

项目是**单目标**（`<TargetFramework>` 单数），所以不需要 `-f`。

也可以直接运行产物：
`src/MD/bin/Debug/net10.0-windows10.0.19041.0/win-x64/MD.exe`

### 发布为单文件 exe

```powershell
powershell -ExecutionPolicy Bypass -File build\publish-windows.ps1
# 产物：dist\windows-win-x64-single\MD.exe     （实测 71.7 MB，目录下只有这一个文件）
```

两种模式：

| 模式 | 参数 | 体积 | 对用户的要求 |
| --- | --- | --- | --- |
| **single**（默认） | `-Mode single` | 实测 **71.7 MB** | **无**，双击即用 |
| framework | `-Mode framework` | 约 5~20 MB | 需预装 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0) + Windows App SDK 运行时 |

体积来自 Windows App SDK 的原生依赖（`Microsoft.WindowsAppRuntime.*.dll`、WebView2 loader、
DirectX 相关），不是托管代码膨胀 —— 单文件自包含模式把整套运行时一起打包了。

换架构：`-p:MdWindowsRuntime=win-arm64`。

涉及的关键 MSBuild 属性（见 `MD.csproj` 的 Release 条件块）：

```xml
<WindowsPackageType>None</WindowsPackageType>          <!-- 非打包模式，直接 exe 运行 -->
<SelfContained>true</SelfContained>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

> ⚠️ `IncludeAllContentForSelfExtract=true` 会让启动时把内容解压到临时目录，
> 首次启动稍慢但兼容性最好。去掉它可加快启动（代价是部分资源需与 exe 同目录分发）。

### Windows 独有能力

- **拖拽文件到窗口打开**：`Platforms/Windows/WindowsDropTarget.cs` 直接接 WinUI 的
  `DragOver` / `Drop`，这样才能拿到 `StorageFile.Path`。
  MAUI 的 `DropGestureRecognizer` 拿不到本地路径，所以没用它。
  拖拽悬停时工具栏下沿变成 2px 强调色作为反馈。
- **标题栏跟随主题**：`Platforms/Windows/WindowsTitleBar.cs`。
  见下面「标题栏为什么必须绕过 AppWindowTitleBar」。
- **鼠标滚轮统一路由**：`Platforms/Windows/WindowsWheelRouter.cs`。
  代码块为了横向滚动套了一层 `ScrollViewer`，它会吃掉纵向滚轮；
  路由把「指针所在容器不能纵向滚动」时的滚动转交给最近的祖先容器，
  并把被抢走的横向偏移还原回去。顺带承接 Ctrl + 滚轮调字号。
- **系统集成（右键菜单 / 打开方式 / 默认应用）**：
  `Platforms/Windows/WindowsShellIntegration.cs`，只写 HKCU，不需要管理员权限。
- **单实例运行**：`Services/SingleInstance.cs`（命名互斥体 + 命名管道）。
  已在运行时，第二个进程把命令行参数转交给第一个进程后**立刻退出**（此时还没建窗口，
  所以不会闪窗）；第一个进程把文件在新标签里打开，并把窗口提到前台。
- **窗口可见性保护**：`Platforms/Windows/WindowsScreen.cs`。
  窗口被放到所有显示器之外时搬回主显示器中央，见下面「窗口为什么会跑到屏幕外」。
- **窗口几何信息恢复**：位置、尺寸、最大化状态（走 `OverlappedPresenter`，MAUI 自身取不到最大化标志）。
- **命令行参数**：

  | 参数 | 作用 |
  | --- | --- |
  | `<文件路径>` | 打开该文件（资源管理器双击 / 右键打开走这条） |
  | `--folder <目录>` | 打开该目录为工作区 |
  | `--new-instance` | 强制开一个新进程，跳过单实例检查（并排对比两份文档时用） |
  | `--register-shell` | 注册右键「打开方式」与默认应用条目，然后退出（退出码 0/1） |
  | `--unregister-shell` | 移除上面注册的全部内容，然后退出 |
  | `--default-apps` | 打开系统「默认应用」设置页，然后退出 |

  三个 `--*-shell` 开关在 `Services/ShellCommandLine.cs` 里处理，位置在
  `MauiProgram.CreateMauiApp` 的**最前面** —— 那时还没建窗口，所以脚本化调用不会闪窗。
  单实例判断紧跟在它们之后，同样在建窗口之前。

### 单实例运行：怎么做的、为什么这么做

程序注册了 `.md` 的关联，所以在资源管理器里连点三篇文档，理应得到**一个窗口里的三个标签**，
而不是三个进程、三个抢同一份 `settings.json` 的写入者、三个各记各的阅读位置的窗口。

实现（`Services/SingleInstance.cs`）：

```
第二个实例 ──① 抢命名互斥体失败──► ② 用命名管道把参数发给主实例
                                        │
                                        └─► ③ 立刻 Environment.Exit(0)（还没建窗口 → 不闪窗）
主实例 ── 后台循环等管道 ──► 收到参数 ──► 在 UI 线程上开成新标签 + 窗口提到前台
```

几个刻意的选择：

| 选择 | 原因 |
| --- | --- |
| 命名管道，不用 `WM_COPYDATA` | `WM_COPYDATA` 得先拿到对方窗口句柄，而句柄只能靠 `FindWindow` 猜窗口标题 —— 改标题就失效。管道不用先找到对方，还能直接传结构化数据 |
| 互斥体前缀 `Local\`，管道名带会话号 | 互斥体 `Local\` 本身就是会话级；但命名管道在 `\\.\pipe\` 下是**全机器**可见的，不带会话号会被同一台机器上另一个登录用户的 MD 抢走消息 |
| 界面就绪**之后**才注册参数接收者 | 启动期间到达的参数由 `SingleInstance` 排队，注册时一次性补投，不会丢 |
| 转交失败时**降级为独立启动** | 对方可能正好在退出（互斥体还在、管道已关）。宁可短暂出现两个窗口，也不能让用户双击文档之后什么都没发生 |

**前台权限**这一步容易漏：`SetForegroundWindow` 在调用方不是前台进程时会被系统**静默拒绝**。
第二个实例是刚被资源管理器启动的，**它**才有前台权限 —— 所以它在退出前调用
`AllowSetForegroundWindow(ASFW_ANY)` 把这个权限让出来，主实例才拉得动前台。

> 逃生阀 `--new-instance`：跳过单实例检查，真的开一个新进程。
> ⚠ 此时两个进程**共用同一份 `settings.json`**，窗口几何信息与会话状态（标签列表）
> 会互相覆盖 —— 文档内容不会丢，但「记住上次标签」的结果取决于哪个窗口最后关闭。
> 并排对比两份文档时可用，日常不需要。

### 窗口为什么会跑到屏幕外

这一条用户实际遇到过，值得单独写。

**症状**：双击文档，程序确实启动了（任务管理器里有进程），但窗口看不到；
点任务栏图标也拉不回来。

**根因**：Win32 用 `(-32000, -32000)` 表示「最小化窗口的位置」。
如果窗口在最小化状态下被读到几何信息，这个哨兵值就会被写进 `settings.json`；
而恢复时只检查了「是不是有限数」，于是照单全收 —— 窗口被建在屏幕外。
更糟的是关闭时会把它再写回去，**形成自我循环，永远不会恢复**。

**修法**（两侧都要拦，外加一道兜底）：

| 位置 | 措施 |
| --- | --- |
| `WindowHelper.Capture` | 写入前拒绝不可信坐标，并且**清空**旧值（保留旧值会让坏坐标永远留在配置里） |
| `WindowHelper.Restore` | 套用前拒绝不可信坐标，改为交给系统决定位置 |
| `WindowsScreen.EnsureOnScreen` | 窗口创建后按**真实 Win32 矩形**再确认一次能否被用户够到，够不到就搬回主显示器中央 |

第三道还能兜住「保存位置所在的那台显示器已经被拔掉」—— 数值上完全合法、实际却看不见。
它全程用设备像素（`GetWindowRect` / `SetWindowPos`），不与 MAUI 的设备无关单位混用，
因此不需要任何 DPI 换算。

> 判定标准是「标题栏那一条」是否至少有一部分落在某个显示器的**可用区域**（`rcWork`，已排除任务栏）里。
> 只要标题栏够得到，用户就能把窗口拖回来。

### 系统集成：为什么不能「一键设为默认」

Windows 8 起，文件类型的默认关联由 `UserChoice` 保护，其值带哈希校验，
**微软明确禁止程序自行改写**（改了会被系统重置）。所以：

| 做什么 | 做法 |
| --- | --- |
| 出现在右键「打开方式」里 | 注册 ProgId `MD.Markdown` + 各扩展名的 `OpenWithProgids` + `Applications\MD.exe\SupportedTypes` |
| 出现在「默认应用」列表里 | 注册 `Software\MD\Capabilities` 并登记到 `RegisteredApplications` |
| 加右键菜单项 | 写 `*\shell\MD`（"用 MD 打开"）与 `Directory\shell\MD`（"用 MD 打开文件夹"） |
| **设为默认** | **只能引导用户**去系统设置页确认一次，程序无权代改 |

### 标题栏为什么必须绕过 AppWindowTitleBar

这一条踩了三次，记下来免得再试。

| 方案 | 结果 |
| --- | --- |
| `AppWindowTitleBar.BackgroundColor` 等 | ❌ 只在「扩展内容到标题栏」时生效。默认标题栏下设了完全没效果，实测颜色纹丝不动 |
| MAUI 的 `Window.TitleBar`（自绘标题栏） | ❌ 颜色是对的，但会把 `MenuBarItems` 挤掉，菜单栏直接消失 |
| `DwmSetWindowAttribute` 单独用 | ❌ 能切亮暗（按钮会跟着变），但 `DWMWA_CAPTION_COLOR` 被 Mica 背景顶掉 |
| **去掉 `SystemBackdrop` + 给窗口根 `Panel` 上底色** | ✅ 保留系统标题栏（与菜单栏同一条），颜色精确等于主题的 `toolbarBg` |

结论：MAUI 把内容扩展进了标题栏，那一条实际是**客户端区域**，底色来自窗口根元素的
`Background`。所以正确做法是清掉 Mica 材质（半透明、会压不住壁纸色），
再把根 `Panel` 的 `Background` 设成 `toolbarBg` —— 页面正文会盖住其余部分，
只有标题栏那一条露出来，于是标题栏与工具条连成一片。

### 已知缺陷（已定位，尚未修）

| 现象 | 根因 | 影响 |
| --- | --- | --- |
| 标题栏那一条**没有菜单栏**（只有窗口标题） | MAUI 10 的 Windows 端实测不渲染 `Page.MenuBarItems`；`Window.MenuBarItems` API 已移除 | 文件 / 视图 / 工具 三个菜单看不见。命令目前靠工具栏按钮、设置面板与键盘快捷键触达（`AttachMenu` 里有一行启动日志记录菜单项数量） |
| 滚动时进度条 / 状态栏百分比 / 大纲高亮不更新，`settings.json` 里 `reading.scrollPositions` 始终为空 | MAUI 的 `CollectionView.Scrolled` 事件不触发（滚轮被转交给平台 `ScrollViewer` 后同样没有回调）。`ReaderPage.OnScrolled` 里加了限流的 `[滚动]` 日志，实测**一条都不出现** | 「每个文件记住阅读进度」实际失效；进度条停在 0% |


---

## 三、运行现场：配置与日志

出问题时先看这两个文件，都在配置目录下。

| 文件 | 路径 | 说明 |
| --- | --- | --- |
| 配置 | `%APPDATA%\MD\settings.json` | 主题、窗口几何、排版、最近文件、每文件阅读进度 |
| 日志 | `%APPDATA%\MD\md.log` | 启动节点、命令行参数、渲染结果、未捕获异常 |
| 主题 | `%APPDATA%\MD\themes\*.json` | 用户自定义主题 |

`md.log` 的存在意义：这是 GUI 程序，没有控制台，启动期异常本来是完全不可见的
（表现为「窗口一闪就没了」）。日志由 `Services/DiagnosticsLog.cs` 写入，
每次启动追加，超过 512 KB 自动轮转为 `md.log.1`。

配置写入策略：临时文件 + 原子替换，防断电写坏；变更后防抖 500ms 落盘，
退出时强制 flush。

> ⚠️ 有一个曾经踩到的坑：`System.Text.Json` **拒绝**序列化 NaN / ±Infinity 并抛异常。
> 窗口位置这类「尚未确定」的值天然可能是 NaN（MAUI 在窗口未定位时 `Window.X` 就是 NaN），
> 一旦有一个字段是 NaN，整份 settings.json 就一次都写不成功。
> 现在做了两件事：窗口 X/Y 改用 `double?`（null = 未记录，JSON 里省略），
> 并注册了 `FiniteDoubleConverter`（`Models/FiniteDoubleConverter.cs`）
> 把任何漏网的非有限值降级成 `null`。

---

## 四、按需恢复：Android 与 macOS

### Android

停用只是把目标框架收窄了，代码、清单、脚本都在。

```xml
<!-- MD.csproj -->
<TargetFramework>net10.0-windows10.0.19041.0;net10.0-android</TargetFramework>  <!-- 改成复数的 TargetFrameworks -->
```

恢复后构建：

```bash
export ANDROID_HOME="/c/Program Files (x86)/Android/android-sdk"
dotnet build src/MD/MD.csproj -c Release -f net10.0-android
# 或
powershell -ExecutionPolicy Bypass -File build\publish-android.ps1
adb install -r src/MD/bin/Release/net10.0-android/cn.md.reader-Signed.apk
```

所需环境：

| 依赖 | 要求 |
| --- | --- |
| .NET 工作负载 | `maui-android` |
| JDK | **17 ~ 21**（通过 `JAVA_HOME` 指定；JDK 24 偏新，可能出现 `Unsupported class file major version`） |
| Android SDK | 含 `platforms/android-36` 与任一 `build-tools`（.NET 10 默认面向 **API 36**） |
| `ANDROID_HOME` | 指向 SDK 根目录 |

缺 API 平台时会报 `error XA5207: 找不到适用于 API 级别 36 的 android.jar`，两种解法：

```bash
# 1) 让 MSBuild 自己补装（会自动接受许可）
dotnet build src/MD/MD.csproj -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory="C:\Program Files (x86)\Android\android-sdk" \
  -p:AcceptAndroidSDKLicenses=True

# 2) 或降级到已有的 API 级别
#    <AndroidSdkTargetVersion>35</AndroidSdkTargetVersion>
```

Android 端已有的差异处理（恢复后即生效）：

- 菜单栏/快捷键代码在 `#if WINDOWS || MACCATALYST` 内，Android 编译不到
- 拖拽代码整文件在 `#if WINDOWS` 内
- Android 15 起强制 edge-to-edge，`ReaderPage` 给根 Grid 加了 `Padding(0, 26, 0, 4)` 让出状态栏
- 工具栏左内边距收窄到 6px（桌面 12px）
- 配置与日志在应用私有目录 `/data/data/cn.md.reader/files/MD/`

### macOS

```xml
<TargetFramework>net10.0-windows10.0.19041.0;net10.0-maccatalyst</TargetFramework>
```

**必须在 macOS 上构建**（Mac Catalyst 需要 Xcode 工具链）：

```bash
dotnet workload install maui
dotnet build src/MD/MD.csproj -c Release -f net10.0-maccatalyst
```

Windows 端已有的菜单栏代码（`#if WINDOWS || MACCATALYST`）在 macOS 上同样生效。

---

## 五、Linux

.NET MAUI **不支持** Linux 桌面。`AppPaths.cs` 里保留了 `~/.config/MD` 分支，
是为将来的非 MAUI 前端预留的，MAUI 本身走不到。

### 路线 A：另建 Avalonia 前端（推荐）

Avalonia 支持 Linux / Windows / macOS，API 风格与 WPF 接近。**约 60% 的代码可直接复用**：

| 目录 | 能否复用 | 说明 |
| --- | --- | --- |
| `Models/` | ✅ 100% | 纯 POCO，无框架依赖 |
| `Markdown/` | ✅ 100% | Markdig + 自写着色器 / 编码嗅探，零 UI 依赖 |
| `Services/` | ✅ 约 90% | 只有 `WindowHelper`、`PlatformLauncher`、`AppPaths` 需要换平台实现 |
| `Rendering/` | ⚠️ 需重写 | 映射目标是 MAUI 控件（`Label`/`Span`/`Border`），Avalonia 用 `TextBlock`/`Run`/`Border` |
| `Views/` | ⚠️ 需重写 | 布局 DSL 不同，但结构（工具栏 / 侧栏 / 虚拟化列表 / 状态栏）可照搬 |

当前的目录划分（业务逻辑与渲染层严格分离）就是为这件事预留的余地。

### 路线 B：WSL + Windows 版

让 WSL 调宿主的 `MD.exe`（需 WSLg 或 X Server）。只适合「文件在 Linux 侧、人坐在 Windows 前」，
本质不是 Linux 原生应用。

### 路线 C：GTK / Qt 壳

工作量与路线 A 相当，但社区活跃度和 .NET 10 兼容性都不如 Avalonia，不推荐。

---

## 六、跨平台能力对照

| 能力 | Windows | Android（恢复后） | macOS（恢复后） |
| --- | --- | --- | --- |
| 打开文件对话框 | ✅ | ✅ | ✅ |
| 最近文件 | ✅ | ✅ | ✅ |
| 拖拽打开 | ✅ | ❌ | ⚪ |
| 原生菜单栏 | ✅ | ❌ | ✅ |
| 键盘快捷键 | ✅ | ❌ | ✅ |
| 窗口几何恢复 | ✅ | ❌（单窗口全屏） | ✅ |
| 最大化状态恢复 | ✅ | — | ⚪ |
| 打开链接 / 定位文件 | ✅ | ✅（浏览器） | ✅ |
| 自定义主题目录 | ✅ | ✅ | ✅ |
| 单文件分发 | ✅（71.7 MB） | ✅（APK） | ⚪ |

---

## 七、当前开发环境与踩坑

实测情况，供排错参考：

| 项 | 值 |
| --- | --- |
| .NET SDK | 10.0.401（另有 9.0.318 并存） |
| 工作负载 | `maui-windows` 10.0.20（`maui-android` / `android` 也已安装，恢复 Android 时可直接用） |
| JDK | Oracle OpenJDK 24.0.1 |
| Android SDK | `C:\Program Files (x86)\Android\android-sdk`（build-tools 35.0.0、platforms 34/35/36，**已补装 36**） |
| `ANDROID_HOME` | 未设置 —— 构建 Android 前必须手动指定 |

### 三个真的会卡住人的坑

1. **`dotnet publish` 报 `NU1102: Microsoft.NETCore.App.Runtime.Mono.win-x64`**
   原因：命令行 `-r win-x64` 与 `--self-contained` 是**全局** MSBuild 属性，
   会一并作用到 Android 目标上，使它去找 Mono 的 Windows 运行时包。
   `MD.csproj` 现在只保留 Windows 一个目标，这个问题不会再出现；
   将来恢复多目标时，Windows 的 RID 请走自定义属性 `-p:MdWindowsRuntime=win-arm64`，
   不要用命令行 `-r`。

2. **csproj 里 XML 注释不能含双横线**：写「`--self-contained`」会直接 `MSB4025: 未能加载项目文件`。

3. **`dotnet workload install` 报 `0x00000652`**：Windows Installer 被别的进程占用。
   等安装程序退出后重跑即可，不会损坏已有工作负载。
