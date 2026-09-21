// -----------------------------------------------------------------------------
//  MD - 跨平台 Markdown 阅读器
//  文件：Rendering/BlockHost.cs
//  说明：虚拟化列表的宿主控件。
//        CollectionView 只会在「滚动到可视区域」时才创建对应项，所以这里
//        等到 BindingContext 被赋值（也就是该项被实现出来）时才真正构建控件树。
//        这是万行文档依然流畅的关键：屏幕上永远只存在几十个控件。
//  作者：MD
//  创建：2026-09-21
//  修改：2026-09-21  初版
// -----------------------------------------------------------------------------

using MD.Models;

namespace MD.Rendering;

public sealed class BlockHost : ContentView
{
    private RenderContext _ctx;

    public BlockHost(RenderContext ctx)
    {
        _ctx = ctx;
        Padding = 0;
        BackgroundColor = Colors.Transparent;
    }

    public void UpdateContext(RenderContext ctx) => _ctx = ctx;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        Rebuild();
    }

    /// <summary>主题 / 排版变化时原地重建（保留滚动位置）。</summary>
    public void Rebuild()
    {
        if (BindingContext is MdBlock block)
        {
            _ctx = _ctx ?? throw new InvalidOperationException("RenderContext 未初始化");
            Content = BlockRenderer.Render(block, _ctx);
        }
        else
        {
            Content = null;
        }
    }
}
