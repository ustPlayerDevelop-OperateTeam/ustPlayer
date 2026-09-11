namespace UstPlayer.Views;

/// <summary>
/// 主窗口。窗口镶边策略继承自 <see cref="ShellWindow"/>（见
/// <c>docs/adr-0002-window-chrome.md</c>）。
///
/// Phase 1 为占位实现，Phase 5 替换为 FluentAvalonia 的 NavigationView 外壳
/// （基础 / 文件 / 播放器 / 歌词 / 其他 五个页面）。
/// </summary>
public partial class MainWindow : ShellWindow
{
    /// <summary>构造主窗口。</summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}
