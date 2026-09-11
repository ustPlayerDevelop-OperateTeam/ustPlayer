using System;

using Avalonia.Controls;

using FluentAvalonia.UI.Windowing;

namespace UstPlayer.Views;

/// <summary>
/// 应用窗口基类：统一窗口镶边（标题栏）策略。
///
/// 继承 FluentAvalonia 的 <see cref="AppWindow"/>（WinUI 风格窗口）。做法与 ClassIsland
/// 一致（其 <c>MyWindow</c> 同样继承 <c>AppWindow</c>，并跨 Windows / macOS / Linux 发布）：
///
/// <list type="bullet">
///   <item>Windows：标题栏由 <c>AppWindow</c> 内部的 Win32 窗口管理器处理，
///   只设置 <c>TitleBar</c> 相关属性即可；</item>
///   <item>macOS：<c>AppWindow</c> 不自动扩展客户区，需显式设置
///   <c>ExtendClientArea*</c>。FluentAvalonia 内部按运行时平台降级，无需条件编译。</item>
/// </list>
///
/// 决策依据与备选方案见 <c>docs/adr-0002-window-chrome.md</c>。
/// </summary>
public class ShellWindow : AppWindow
{
    /// <summary>初始化窗口基类的默认标题栏设置。</summary>
    protected ShellWindow()
    {
        Title = "ustPlayer";

        // 内容延伸进标题栏，由基类处理命中测试与拖拽区
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;

        ApplyPlatformChrome();
    }

    /// <summary>应用版本号（语义化，如 <c>2.0.0</c>）。</summary>
    public static string AppVersion { get; } = AppInfo.Version;

    /// <summary>
    /// 平台相关的客户区扩展设置：仅在 macOS 上显式扩展。
    /// </summary>
    /// <remarks>
    /// 与 ClassIsland 的做法一致。Windows 上由 <c>AppWindow</c> 的 Win32 管理器负责，
    /// 重复设置会互相打架。<c>ExtendClientAreaChromeHints</c> 属性与其同名类型冲突，
    /// 这里改走 <c>SetValue</c> + <c>Enum.Parse</c>，避免书写枚举类型名。
    /// </remarks>
    private void ApplyPlatformChrome()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;

        var hintProperty = Window.ExtendClientAreaChromeHintsProperty;
        SetValue(hintProperty, Enum.Parse(hintProperty.PropertyType, "PreferSystemChrome"));

        // 同理：SystemDecorations 属性与 Avalonia.Controls.SystemDecorations 枚举同名，
        // 右侧需用完全限定名。
        SystemDecorations = Avalonia.Controls.SystemDecorations.Full;
    }
}
