// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Views/SampleDocument.cs
//  说明：内置示例文档。用来验证渲染器的各类元素，也方便第一次打开时看到效果。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

namespace MD.Views;

internal static class SampleDocument
{
    public const string FileName = "MD 示例文档.md";

    public const string Text = """
---
title: MD 示例文档
author: MD
tags: [markdown, reader, dotnet]
---

# MD 示例文档

> MD 是一个用 **.NET 10 + MAUI** 写的原生 Markdown 阅读器。
> 它不内嵌浏览器内核，每个 Markdown 元素都被渲染为操作系统原生控件。
> 这份文档覆盖了所有支持的语法，滚动到底部可以看到表格与代码块。

## 一、为什么不用 WebView

Typora 采用 Electron 架构，好处是排版能力强、样式统一，代价是：

1. 安装包动辄 100 MB 以上（自带 Chromium）；
2. 内存占用高，长文档滚动容易掉帧；
3. 在移动端只能靠 WebView 移植，触感与系统割裂。

MD 的选择是**原生渲染**：

- [x] 块级元素直接映射为原生控件，滚动交给系统
- [x] 只渲染可视区域的块（虚拟化），十万行文档也稳
- [x] 主题只是 JSON，改配色不需要重新编译
- [ ] 富文本编辑（阅读器定位，暂不做）

## 二、排版对照

### 2.1 行内样式

普通文本、**粗体**、*斜体*、***粗斜体***、~~删除线~~、`行内代码`、
==高亮==、++下划线++、H~2~O、E=mc^2^，以及一条[外部链接](https://learn.microsoft.com/dotnet/maui)。

### 2.2 引用嵌套

> 第一层引用。
>
> > 第二层引用，用来验证竖线的层级关系。
> > 继续第二层的内容。
>
> 回到第一层。

### 2.3 列表

无序列表：

- 一级项
- 一级项
  - 二级项
  - 二级项
    - 三级项

有序列表：

1. 打开文件（Ctrl+O）
2. 切换主题（工具栏 ◐ 或设置面板）
3. 调整字号（A− / A＋）

## 三、代码块

C# 示例：

```csharp
public sealed class BlockHost : ContentView
{
    private readonly RenderContext _ctx;

    public BlockHost(RenderContext ctx) => _ctx = ctx;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        // 只有滚动到可视区域时，这一行才会被执行
        Content = BlockRenderer.Render(BindingContext as MdBlock, _ctx);
    }
}
```

SQL 示例：

```sql
SELECT  w.WorkShopName,
        COUNT(p.LinePlanId) AS PlanCount
FROM    Prod_LinePlan p WITH (NOLOCK)
        INNER JOIN Basal_WorkShop w ON w.WorkShopId = p.WorkShopId
WHERE   p.PlanDate BETWEEN @StartDate AND @EndDate
GROUP BY w.WorkShopName
ORDER BY PlanCount DESC;
```

Shell 示例：

```bash
dotnet publish src/MD/MD.csproj -c Release -f net10.0-windows10.0.19041.0
ls -lh bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/MD.exe
```

JSON 示例：

```json
{
  "themeId": "github-light",
  "window": { "width": 1180, "height": 840, "maximized": false },
  "reading": { "fontSizeDelta": 0, "lineHeightDelta": 0 }
}
```

## 四、表格

| 平台 | 框架目标 | 单文件发布 | 备注 |
| --- | --- | :---: | --- |
| Windows | net10.0-windows10.0.19041.0 | 支持 | 自包含单 exe，双击即用 |
| Android | net10.0-android | APK | 24 以上 |
| macOS | net10.0-maccatalyst | 支持 | 需在 macOS 上构建 |
| Linux | 暂不支持 | - | 见 PLATFORMS 说明 |

## 五、分割线与长段落

---

阅读器的性能瓶颈通常不在解析，而在**控件数量**。解析一份 10 万行的 Markdown，
Markdig 通常只需要几十毫秒；真正的挑战是如何让它滚动起来不掉帧。
本项目的做法是把文档压平成一维的块序列，交给平台的虚拟化列表按需实例化，
因此内存占用与文档长度基本无关，只与可视区域高度有关。

## 六、图片

相对路径、绝对路径、`http(s)` 与 `data:` 形式的图片都会被正确解析；
关闭「加载图片」后只显示占位说明，适合边看文字边省流量。

![示例图片](images/example.png)

## 七、脚注与缩写

Markdown 的扩展语法同样支持，例如脚注[^1]。

[^1]: 这是脚注内容，会被展开在文末。

---

**快捷键一览**

| 操作 | 快捷键 |
| --- | --- |
| 打开文件 | Ctrl + O |
| 显示 / 隐藏侧栏 | Ctrl + B |
| 增大 / 减小字号 | Ctrl + = / Ctrl + - |
| 专注模式 | Ctrl + F |
| 重新加载 | F5 |
""";
}
