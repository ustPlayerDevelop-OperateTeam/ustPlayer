using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UstPlayer.Views.Controls;

/// <summary>
/// 页面进入动画：把页面的**直接子元素**逐张错峰上浮淡入。
/// </summary>
/// <remarks>
/// <para>
/// 做法与参数对齐 ClassIsland 的
/// <c>StackPanelIntroAnimationBehavior</c>（其 <c>animated-intro</c> 样式使用）：
/// 子元素初始 <c>Opacity=0</c> 且下移 50px，随后按 <b>25 毫秒</b>的节奏逐个加上
/// <c>intro-play</c> 类，由样式里的动画在 <b>0.6 秒</b>内上浮到原位并淡入。
/// </para>
/// <para>
/// 与 ClassIsland 的差异：它用 <c>0.75</c> 秒与 <c>(0, 1, 0, 1)</c> 缓动；
/// 这里略短一些（0.6 秒），因为本应用一次切换只展示 6–8 张卡片，
/// 太长会让「点一下要等一下」的感觉盖过利落感。
/// </para>
/// <para>
/// 只处理**直接子元素**：页面的版式就是「若干张卡片依次纵向排列」，
/// 直接子元素即卡片本身；再往下是卡片内部（图标、文字、控件），
/// 让它们也参与会显得琐碎且与页面切换动画叠加过头。
/// </para>
/// <para>
/// 已有 <see cref="Visual.RenderTransform"/> 的子元素会被跳过——与 ClassIsland 一致
/// （它的判断是 <c>RenderTransform == null</c>）：那些元素自带变换，
/// 再叠一层平移动画会互相打架。
/// </para>
/// </remarks>
internal static class PageIntroAnimation
{
    /// <summary>播放节奏：相邻两张卡片的启动间隔。</summary>
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(25);

    /// <summary>动画类名（加在参与动画的子元素上触发样式里的动画）。</summary>
    internal const string PlayClass = "intro-play";

    /// <summary>初始偏移类名（加在页面根上，把子元素置为「未入场」状态）。</summary>
    internal const string ArmedClass = "intro-armed";

    /// <summary>
    /// 是否对指定页面启用进入动画（附加属性，在 XAML 上设置）。
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Panel, bool>("IsEnabled", typeof(PageIntroAnimation));

    /// <summary>设置是否启用进入动画。</summary>
    /// <param name="panel">页面根面板。</param>
    /// <param name="value">是否启用。</param>
    public static void SetIsEnabled(Panel panel, bool value) => panel.SetValue(IsEnabledProperty, value);

    /// <summary>读取是否启用进入动画。</summary>
    /// <param name="panel">页面根面板。</param>
    /// <returns>是否启用。</returns>
    public static bool GetIsEnabled(Panel panel) => panel.GetValue(IsEnabledProperty);

    static PageIntroAnimation()
    {
        IsEnabledProperty.Changed.AddClassHandler<Panel>(OnIsEnabledChanged);
    }

    /// <summary>启用时挂上一次性播放逻辑。</summary>
    /// <param name="panel">页面根面板。</param>
    /// <param name="args">变更参数。</param>
    private static void OnIsEnabledChanged(Panel panel, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            panel.Loaded += OnPanelLoaded;
        }
        else
        {
            panel.Loaded -= OnPanelLoaded;
        }
    }

    /// <summary>页面首次进入可视树时播放。</summary>
    /// <param name="sender">页面根面板。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 每次 <c>Loaded</c> 都重播：页面切换时旧页面被移出可视树、新页面被放入，
    /// 再次切回会重新触发 <c>Loaded</c>，于是「每次切页都有入场动画」。
    /// 收起类名放在播放开始时加、播放结束后移除（见 <see cref="Play"/>），
    /// 因此重复进入不会停留在「全透明」状态。
    /// </remarks>
    private static void OnPanelLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Panel panel)
        {
            return;
        }

        Play(panel);
    }

    /// <summary>播放一次入场动画。</summary>
    /// <param name="panel">页面根面板。</param>
    internal static void Play(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        // 已经播过就别重入（Loaded 在某些布局变化下可能多次触发）
        if (panel.Classes.Contains(ArmedClass))
        {
            return;
        }

        var targets = panel.Children
            .OfType<Control>()
            .Where(child => child.IsVisible && child.RenderTransform is null)
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        // 先置为「未入场」（透明 + 下移），否则第一帧会闪一下完整内容
        panel.Classes.Add(ArmedClass);

        var index = 0;

        var timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = Stagger,
        };

        timer.Tick += (_, _) =>
        {
            if (index >= targets.Count)
            {
                timer.Stop();

                // 动画交给样式里的 Animation（FillMode=Both 会保持终止值），
                // 因此这里只需撤掉「未入场」状态，不必清理每个子元素的类：
                // 保留 intro-play 会让下次进入时样式命中却不再触发动画。
                panel.Classes.Remove(ArmedClass);

                foreach (var target in targets)
                {
                    target.Classes.Remove(PlayClass);
                }

                return;
            }

            targets[index].Classes.Add(PlayClass);
            index++;
        };

        timer.Start();
    }
}
