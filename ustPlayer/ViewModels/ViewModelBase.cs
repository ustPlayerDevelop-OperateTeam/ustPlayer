using CommunityToolkit.Mvvm.ComponentModel;

namespace UstPlayer.ViewModels;

/// <summary>
/// ViewModel 基类 — 复用 CommunityToolkit.Mvvm 的 <see cref="ObservableObject"/>。
/// </summary>
/// <remarks>
/// <para>
/// 2.0 的 ViewModel 刻意很薄。1.1.x 每个页面都要手写「控件 → 设置」与
/// 「设置 → 控件」两向连接（<c>_connect_signals</c>），再加上
/// <c>_sync_ui_from_settings</c> / <c>sync_all_from_settings</c> 用于工程导入后回填，
/// 一处漏写就会出现「设置不生效」或「导入后界面没刷新」。
/// </para>
/// <para>
/// C# 侧不需要这些：设置子域本身实现了 <see cref="System.ComponentModel.INotifyPropertyChanged"/>，
/// 且 setter 有「值未变化则不通知」的守卫，因此 XAML 可以**直接双向绑定**
/// <c>Settings.Display.ShowBpm</c> 这类路径。导入工程改了设置 → 绑定自动刷新界面，
/// 手工同步这一整类 bug 从根上消失。
/// </para>
/// </remarks>
internal abstract class ViewModelBase : ObservableObject;
