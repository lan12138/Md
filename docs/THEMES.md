# 主题定制

MD 的主题就是一个 JSON 文件。把文件放进配置目录的 `themes/` 子目录，
重启应用即可在设置面板里看到（同 `id` 会覆盖内置主题）。

| 平台 | 主题目录 |
| --- | --- |
| Windows | `%APPDATA%\MD\themes\` |
| macOS | `~/Library/Application Support/MD/themes/` |
| Linux | `~/.config/MD/themes/` |
| Android | 应用私有目录 `files/MD/themes/` |

设置面板里的「打开主题目录」按钮可以直接跳过去。

---

## 一、最小可用的主题文件

```json
{
  "id": "my-theme",
  "name": "我的主题",
  "author": "你",
  "isDark": false,
  "colors": {
    "editorBg": "#FFFDFCF8",
    "text": "#FF2B2A27",
    "link": "#FF3B6E9E",
    "accent": "#FF3B6E9E"
  },
  "typography": {
    "baseFontSize": 17,
    "lineHeight": 1.9,
    "contentMaxWidth": 760
  }
}
```

**关键点：只写想改的字段。** 未写的字段会自动继承：

- `isDark: false` → 继承「亮色模板」（即 GitHub 亮色）
- `isDark: true` → 继承「暗色模板」（即 GitHub 暗色）

所以想做一套暗色主题，通常只需要改十来个颜色。

## 二、颜色写法

支持三种写法，内部统一补成 `#AARRGGBB`：

| 写法 | 含义 |
| --- | --- |
| `#RRGGBB` | 不透明，自动补 `FF` |
| `#AARRGGBB` | 带透明度 |
| `#RRGGBB00` | 完全透明（用于「不要底色」） |

`quoteBg` 与 `tableStripeBg` 默认是全透明；想给引用加淡底色就写个带 alpha 的值，例如 `#0D0969DA`。

## 三、颜色字段全表

### 界面外壳

| 字段 | 说明 |
| --- | --- |
| `windowBg` | 窗口底色（专注模式下的浮层也用） |
| `toolbarBg` / `toolbarBorder` | 顶部工具栏底色与下边线 |
| `toolbarText` / `toolbarTextHover` | 工具栏图标常态 / 悬停色 |
| `accent` | 强调色：主按钮、开关打开、进度条、选中态 |
| `sidebarBg` / `sidebarBorder` | 侧栏底色与右侧分隔线 |
| `sidebarHeaderBg` | 侧栏顶部「页签条」底色。**留空自动派生**（见下） |
| `sidebarActiveBg` | 侧栏「当前项」底色：选中的页签、当前大纲章节、文件夹树的当前文件。**留空自动派生** |
| `sidebarText` / `sidebarTextActive` | 大纲条目常态 / 当前章节 |
| `sidebarHoverBg` | 列表项悬停底色 |
| `statusBarBg` / `statusBarText` | 底部状态栏 |
| `scrim` | 设置面板背后的遮罩（含 alpha，如 `#66000000`） |
| `panelBg` / `panelBorder` | 设置面板本体 |
| `controlBg` / `controlBorder` | 主题卡片、开关轨道等控件底色 |

### 正文

| 字段 | 说明 |
| --- | --- |
| `editorBg` | 阅读区底色 |
| `text` | 正文 |
| `textSecondary` | 次要文字：副标题、列表符号、行号 |
| `heading` | 标题 |
| `headingBorder` | H1 / H2 下方细线 |
| `link` | 链接 |
| `rule` | 分割线 |
| `selection` | 预留（平台选择高亮） |

### 代码

| 字段 | 说明 |
| --- | --- |
| `inlineCodeText` | 行内代码前景色（MAUI 的 `Span` 不支持背景色，故用专属前景色表达） |
| `codeBg` / `codeBorder` | 代码块容器 |
| `codeText` | 代码默认文字色 |
| `codeHeaderText` | 代码块左上角语言名、行号 |

### 引用与表格

| 字段 | 说明 |
| --- | --- |
| `quoteBg` | 引用底色（默认透明） |
| `quoteText` / `quoteBorder` | 引用文字与左侧竖条 |
| `tableBorder` | 表格网格线 |
| `tableHeaderBg` / `tableHeaderText` | 表头 |
| `tableStripeBg` | 偶数行斑马纹（默认透明） |

### 语法着色

| 字段 | 对应 token |
| --- | --- |
| `syntaxKeyword` | 关键字（`if` / `class` / `SELECT` …） |
| `syntaxType` | 类型名、首字母大写的驼峰标识符 |
| `syntaxString` | 字符串字面量 |
| `syntaxNumber` | 数字 |
| `syntaxComment` | 注释（行注释与块注释） |
| `syntaxFunction` | 函数调用（标识符后紧跟 `(`） |
| `syntaxOperator` | 运算符 |
| `syntaxPunctuation` | 括号、逗号、分号等 |
| `syntaxVariable` | 预留 |
| `syntaxBuiltin` | 内置名（`Console` / `println` …） |

### 侧栏底色的自动派生

`sidebarHeaderBg` 与 `sidebarActiveBg` 默认留空，由 `ThemeCatalog.DeriveShades()` 按
`sidebarBg` 推出来，所以**自写主题通常什么都不用管**：

| 字段 | 亮色主题 | 暗色主题 |
| --- | --- | --- |
| `sidebarHeaderBg` | `sidebarBg` 与 `windowBg` 按 **1 : 3** 混合（75% 取窗口底色，整体往亮走） | `sidebarBg` 朝白推进 **4.5%** |
| `sidebarActiveBg` | `sidebarBg` 朝黑推进 **10%** | `sidebarBg` 朝白推进 **11%** |

**为什么要派生而不是写死**：早期版本里侧栏「当前项」直接用 `accent`（强调色）当底色，
在 GitHub 主题下就是一片 `#0969DA` 的蓝矩形，压在浅灰侧栏上非常刺眼。
改成「同色系、只差几个色号」之后，深浅主题都能自然过渡。

想手动接管就显式写上，例如：

```json
"colors": {
  "sidebarBg": "#FFF6F8FA",
  "sidebarHeaderBg": "#FFEDEFF2",
  "sidebarActiveBg": "#FFE3E7EC"
}
```

## 四、排版字段

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `baseFontSize` | 16.5 | 正文字号（pt / dp） |
| `lineHeight` | 1.75 | 行高倍数，可被用户设置面板叠加微调 |
| `contentMaxWidth` | 780 | 正文**舒适宽度**；`0` 表示不限制。实际宽度还会被设置面板的「正文宽度」模式叠加（见下） |
| `contentPadding` | 28 | 阅读区左右内边距 |
| `fontFamily` | 空 | 正文字体族；留空用平台默认（Windows `Segoe UI`、Android `sans-serif`） |
| `monoFontFamily` | 空 | 等宽字体族；留空用平台默认（`Consolas` / `monospace`） |
| `headingScale` | `[1.92, 1.55, 1.28, 1.12, 1.0, 0.94]` | H1~H6 相对正文字号的倍数，必须给 6 个 |
| `headingMarginTop` | 1.6 | 标题上间距倍数 |
| `headingMarginBottom` | 0.5 | 标题下间距倍数 |
| `paragraphSpacing` | 0.72 | 段落间距倍数 |
| `tableCellPadding` | 7 | 表格单元格内边距 |

> 字号单位是设备无关单位（MAUI 的 `FontSize`）。Windows 上约等于 pt，
> Android 上约等于 sp，无需按平台分别适配。

### 正文宽度：主题给「舒适值」，用户给「模式」

最终宽度由 `RenderContext.ContentWidth` 计算，规则是**模式优先于主题**：

| 设置项 `reading.contentWidthMode` | 结果 |
| --- | --- |
| `auto`（默认） | 由主题的 `contentMaxWidth` 出发，按窗口可用宽度做 1.0 ~ 1.55 倍自适应放大 |
| `narrow` | 固定 700 |
| `standard` | 固定 900 |
| `wide` | 固定 1150 |
| `full` | 不限宽，铺满阅读区 |

**为什么要自适应**：早期版本直接吃 `contentMaxWidth`（780），在 1920px 全屏下正文只占中间一小条，
两侧大片留白，看着像没做完。改成自适应后，宽窗口会自动放宽到 1.3~1.5 倍，
窄窗口则回落到主题的舒适值，不再出现「全屏反而更难读」。

## 五、内置主题一览

| id | 名称 | 明暗 | 特点 |
| --- | --- | --- | --- |
| `github-light` | GitHub 亮色 | 亮 | 中性、克制，默认主题 |
| `whitey` | Whitey 极简白 | 亮 | 纯白、低对比，界面几乎消失 |
| `pixyll` | Pixyll 留白 | 亮 | 大留白、宽行高，适合长文 |
| `newsprint` | Newsprint 新闻纸 | 亮 | 暖纸底色 + 衬线字体 |
| `solarized-light` | Solarized 日光 | 亮 | 低刺激的米黄底 |
| `github-dark` | GitHub 暗色 | 暗 | 默认暗色主题 |
| `night` | Night 深夜 | 暗 | 蓝灰调，长时间夜间阅读 |
| `vue-dark` | Vue 暗色 | 暗 | 深蓝底 + 青绿强调 |
| `dracula` | Dracula 紫夜 | 暗 | 高饱和紫青配色 |

## 六、调试技巧

1. 改完主题文件后不需要重启：设置面板里点一下另一套主题再点回来，
   `ThemeCatalog.Reload()` 会重新扫描目录（点「打开主题目录」后重新打开设置面板亦可）。
2. JSON 解析失败不会导致崩溃，会退回内置主题；出错信息写进
   `%APPDATA%\MD\md.log`（GUI 程序没有控制台，**不要靠调试输出定位问题**）。
3. `id` 重复时后加载的（用户主题）覆盖先加载的（内置），可用来「改内置主题」。
4. 主题是「模板 + 差异覆盖」：`isDark` 决定用哪套模板，其余字段逐键覆盖。
   **覆盖必须发生在原始 JSON 节点上** —— 若先反序列化成 `ThemeDefinition` 再回序列化，
   其 `Colors` 属性携带的全套亮色默认值会把暗色模板整个盖掉，
   结果就是「暗色主题全变亮色」。这是曾经踩过的坑，改动 `ApplyTemplate` 时注意。
