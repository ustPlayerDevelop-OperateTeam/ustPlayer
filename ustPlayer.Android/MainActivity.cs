using Android.App;
using Android.Content.PM;

using Avalonia;
using Avalonia.Android;

namespace UstPlayer.AndroidHead;

/// <summary>
/// Android 入口 Activity — 与桌面头 <c>Program.cs</c> 等价，只负责构建 AppBuilder。
/// </summary>
/// <remarks>
/// 命名空间刻意**不叫** <c>UstPlayer.Android</c>：那样会让 <c>using Android.App;</c>
/// 里的 <c>Android</c> 先按外层命名空间解析（<c>UstPlayer.Android</c>），
/// 与 Android SDK 的 <c>Android</c> 命名空间撞名，报错信息很难看懂。
/// 程序集名仍是 <c>ustPlayer.Android</c>（见 csproj），两者互不影响。
/// </remarks>
[Activity(
    Label = "ustPlayer",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    /// <summary>构建 Avalonia 应用（与桌面头保持同一套配置）。</summary>
    /// <param name="builder">Avalonia 提供的 builder。</param>
    /// <returns>已配置的 builder。</returns>
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
