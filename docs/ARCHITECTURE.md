# 架构与性能设计

## 一、总体管线

```
   磁盘字节
      │  ① 编码嗅探（BOM → 严格 UTF-8 → GB18030 → Latin-1）
      ▼
   Markdown 文本
      │  ② Markdig 解析（CommonMark + GFM 扩展，带精确源码位置）
      ▼
   Markdig AST
      │  ③ 转换：AST → 与 UI 无关的领域模型（Block / Inline）
      │     同时产出：大纲树、锚点索引、字数/行数统计
      ▼
   MdDocument（扁平数组 MdBlock[] + OutlineNode[]）
      │  ④ 虚拟化渲染：列表按需把「可视区域的块」交给 BlockHost
      ▼
   原生控件树（Label / Grid / Border / Image …）
```

四步之间是严格单向的。这个划分带来两个直接好处：

- **主题切换不需要重新解析**。主题只影响 ④，改主题时把已实例化的 `BlockHost` 原地重建即可，
  滚动位置不动。
- **解析发生在后台线程**。③ 之前的所有工作在 `Task.Run` 里完成，UI 线程只做最后一步。

## 二、为什么不用 WebView

WebView 方案（Typora / VS Code 的 Markdown 预览）把排版交给 HTML/CSS：开发成本低，但：

1. 滚动、选字、缩放都发生在浏览器合成层里，长文档下与宿主窗口的滚动条、触控惯性不统一；
2. 移动端等于把「套壳浏览器」再套一层壳，内存与启动时间的开销叠加两次；
3. 想要「跟随系统明暗」，要写一遍 CSS 再写一遍宿主主题，两套颜色容易不一致。

本项目把每个 Markdown 元素直接映射为原生控件：H1 是一个 `Label`，代码块是
`Border + ScrollView + Label(FormattedString)`，表格是 `Grid`。滚动完全交给平台，
触控惯性、DPI 缩放、滚动条样式都是系统原生的。

## 三、性能关键点

### 1. 虚拟化：只实例化可视区域的块

`CollectionView` + `DataTemplate`，模板工厂返回一个 `BlockHost`（`ContentView` 子类）。
真正的控件树在 `OnBindingContextChanged` 里才构建：

```csharp
public sealed class BlockHost : ContentView
{
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        // 只有滚动到可视区域、被列表实现出来的项，才会走到这里
        Content = BlockRenderer.Render(BindingContext as MdBlock, _ctx);
    }
}
```

屏幕高度通常只会同时存在 20~40 个块，所以**内存占用与文档长度基本无关**：
10 万行与 1 千行的常驻控件数一样多。

### 2. 解析在后台线程 + 结果缓存

`DocumentService` 用 `Task.Run` 把解码与解析移出 UI 线程；解析结果按
`(路径, 最后写入时间, 文件长度)` 缓存，重复打开同一文件直接命中（状态栏会显示「缓存」）。
缓存最多保留 24 份，超出即整体清空，避免内存无限增长。

### 3. 颜色解析缓存

主题里的颜色是十六进制字符串。`Palette.Get` 带字典缓存，一个主题下几十个颜色
只会被 `Color.FromArgb` 解析一次。渲染一个块可能需要几十次取色，这一步省下的开销很可观。

### 4. 语法着色不引入依赖

`SyntaxHighlighter` 是一个单遍扫描的小型词法分析器：
按语言族（C 系 / Hash 系 / SQL / Markup / JSON）共用一套扫描逻辑，
用「语言配置」区分注释符、关键字表、是否支持模板字符串。
超过 40 万字符的代码块直接降级为纯文本，保证不会因为一个超大代码块卡住滚动。

**着色失败永远降级为纯文本** —— 阅读器的核心职责是「把字显示出来」，
装饰性功能不应该有能力破坏它。同理，`BlockRenderer.Render` 也整体包了 try/catch，
单个块渲染异常只会在原位显示一行错误提示，而不是让整篇文档打不开。

### 5. 高频交互不触发全量重建

- 滚动：只更新进度条、状态栏文案、大纲高亮，不重建任何块。
- 大纲高亮：`OutlineRow` 自己持有「是否当前章节」，滚动时只改颜色，不重建列表。
- 字号 / 行高拖动：`Slider.ValueChanged` 只更新设置与数字标签，
  `Slider.DragCompleted` 才触发重新测量 —— 拖动过程不会卡。

### 6. 设置落盘防抖

拖动窗口会高频触发尺寸变化。`SettingsService` 用 500ms 防抖 + 临时文件原子替换写入，
既不会把磁盘打满，也不会因为中途崩溃留下半个 JSON。

## 四、主题机制：模板 + 差异覆盖

主题 JSON 支持部分字段覆盖。实现方式是**在 JSON 层面合并**：

```
亮色主题 → 以 LightTemplate 为底 ─┐
                                  ├─► JsonNode 逐键覆盖 ─► 反序列化 ─► ThemeDefinition
暗色主题 → 以 DarkTemplate  为底 ─┘
```

这样带来两个好处：

1. 写一套主题通常只需要十几个字段，而不是把四十多个颜色全抄一遍；
2. 新增颜色字段时，所有既有主题自动获得合理默认值，不会漏配。

合并前会把 `#RRGGBB` / `#RGB` 统一补成 `#AARRGGBB`，所以主题作者可以按 CSS 习惯写简写。

## 五、窗口几何信息

`Window.X / Y / Width / Height` 由 MAUI 跨平台提供，直接读写即可。

「是否最大化」MAUI 没有暴露，在 Windows 上通过 WinAppSDK 读取：

```csharp
if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
{
    var presenter = native.AppWindow?.Presenter as OverlappedPresenter;
    return presenter?.State == OverlappedPresenterState.Maximized;
}
```

最大化时**不覆盖**记录下来的宽高，这样「取消最大化」后还能回到原来的窗口尺寸。

**位置必须校验后才可用。** Win32 在窗口最小化时给的是 `(-32000, -32000)` 这个哨兵值，
只做「是不是有限数」检查会把它当成真实位置存下来，于是下次启动窗口落在屏幕外 ——
而且关闭时又写回去，形成永远不会自愈的循环。所以：

1. `WindowHelper` 在**读和写两侧**都拒绝不可信坐标，写侧遇到坏值要**清空**而不是保留；
2. 窗口创建后再按真实 Win32 矩形确认一次可达性（`WindowsScreen.cs`），
   这一道还能兜住「保存位置所在的那台显示器已经被拔掉」。

判定标准是「标题栏那一条」是否至少有一部分落在某个显示器的可用区域（`rcWork`）里 ——
只要标题栏够得到，用户就能把窗口拖回来。

## 六、平台差异层：Windows 独有能力落在哪里

分层原则是「上层不认识平台，平台差异只出现在两处」：

| 位置 | 放什么 |
| --- | --- |
| `Platforms/Windows/` | 只能靠系统 API 做的事：窗口外壳、滚轮路由、Shell 注册、系统对话框 |
| `Services/` 里的 `partial`/接口实现 | 需要跨平台保留调用点、但实现方式不同的服务（文件夹选择、另存为、定位文件） |

`Models/` 与 `Markdown/` **零 UI 依赖**，`Rendering/` 与 `Views/` 只调用平台服务的抽象，
不写 `#if WINDOWS`；需要分支时集中在平台文件内部。

### 已实现的 Windows 独有能力

| 文件 | 职责 | 核心手法 |
| --- | --- | --- |
| `WindowsTitleBar.cs` | 系统标题栏颜色跟随主题 | 去掉 `SystemBackdrop`（Mica）+ 给窗口根 `Panel` 上工具栏底色 + DWM 切亮暗。**不要用 `AppWindowTitleBar`**，默认标题栏下它对颜色完全无效 |
| `WindowsWheelRouter.cs` | 滚轮翻页不被内部横向滚动容器吃掉 | `AddHandler(PointerWheelChangedEvent, handledEventsToo: true)`，内层不能纵向滚则把滚动转交最近的祖先，并还原被抢走的横向偏移 |
| `WindowsShellIntegration.cs` + `Services/ShellService.cs` | 右键菜单 / 「打开方式」/ 默认应用列表 | 只写 HKCU；`UserChoice` 有哈希保护，**程序无权自行改默认关联**，只能注册干净后引导用户去系统设置页 |
| `Services/SingleInstance.cs` | 单实例运行：第二个进程把参数转交后退出 | 命名互斥体 + 命名管道；第二个实例退出前 `AllowSetForegroundWindow` 让出前台权限，主实例才拉得动前台 |
| `WindowsActivation.cs` | 把已有窗口提到前台 | `SetForegroundWindow` 会被静默拒绝 → 最小化时先 `SW_RESTORE`，再 topmost 抬一下制造 Z 序变化；**不用** SW_MINIMIZE 组合，避免每次双击都闪一下 |
| `WindowsScreen.cs` | 窗口跑到屏幕外时搬回来 | `GetWindowRect` + `EnumDisplayMonitors` 判断标题栏是否可达，够不到就用 `SetWindowPos` 居中到主显示器 `rcWork`（全程设备像素，不涉及 DPI 换算） |
| `WindowsFolderPicker.cs` / `WindowsFileSaver.cs` / `WindowsDropTarget.cs` | 系统对话框与拖放 | 用 WinAppSDK 的 `FileOpenPicker` / `FileSavePicker`，需先用窗口句柄 `InitializeWithWindow` |
| `WindowsInterop.cs` | 上述几个共用的窗口发现与矩形 / 定位互操作 | 单一来源，避免每个文件各写一份 `GetWindowRect` / `SetWindowPos` 声明 |

还有两个容易被忽略的点：

1. **外壳集成类命令行开关必须放在 `CreateMauiApp()` 最前面**
   （`Services/ShellCommandLine.cs`），执行完直接 `Environment.Exit`。
   放到页面逻辑里会先建窗口再执行，批处理调用时会闪出一片黑框。
   单实例判断紧跟其后，同样必须在建窗口之前。

2. **窗口位置有两侧防御。** `WindowHelper` 做纯数值校验（拒绝 Win32 的 `-32000`
   最小化哨兵，并且在读到坏值时**清空**而不是保留），平台层再做一次真实矩形可达性检查。
   注意「清空」这一步很关键：只跳过更新会把坏坐标永久留在配置里。

## 七、读写路径（编码三元组的落点）

读和写共用同一个 `TextFileFormat`（编码 + BOM + 换行），保证「读出来什么样，写回去还什么样」：

```
读取：磁盘字节 ─► TextEncodingDetector.Decode ─► TextFileFormat.From ─► 文本 + 格式三元组
保存：编辑后的文本 ─► TextFileFormat.Encode ─► 原子替换写回（tmp + File.Replace）
```

- **编码嗅探的候选集是打分制**：BOM 优先，其次是严格 UTF-8 往返校验，
  再往后是 GB18030 / GBK / Big5 等按「可疑字符」扣分排序。
- **往返校验必须用严格编码器**（`EncoderFallback.ExceptionFallback`）。
  普通编码器遇到表示不了的字符会悄悄替换成 `'?'` 且不报错，校验会假通过。
- 这个三元组有独立的自动化回归测试：`tests/MD.EncodingSelfCheck`（见 `README.md` 第六节）。

## 八、已知取舍

| 取舍 | 原因 |
| --- | --- |
| 行内代码没有底色 | MAUI 的 `Span` 不支持背景色，只能用专属前景色表达 |
| 单个代码块作为一个列表项 | 极端长的代码块无法继续切分，属于虚拟化的固有边界 |
| 界面用 C# 而非 XAML | 主题热切换需要重建整棵树，命令式代码更直接，也免去打包 XAML 资源 |
| 不引入 MVVM 框架 | 单窗口阅读器，状态量小，直接操作控件比搭一层绑定更省事也更快 |
| 正则着色而非完整词法分析 | 目标是「看起来专业」，不是编译前端；换取零依赖与微秒级耗时 |
| 不做 Markdown 语法规范化 | 保存只保证编码无损，不做「顺手美化」，避免用户文件被静默改写 |
| 默认单实例运行 | 双击多个文档应汇入**同一个窗口的多个标签**，而不是开一堆进程抢同一份配置。要并排对比得显式加 `--new-instance`，此时两个进程共用 `settings.json`，会话状态会互相覆盖 |
| 滚动进度依赖 `CollectionView.Scrolled` | 目前该事件在 MAUI 10 / Windows 上不触发，属已知缺陷（见 `PLATFORMS.md`） |
