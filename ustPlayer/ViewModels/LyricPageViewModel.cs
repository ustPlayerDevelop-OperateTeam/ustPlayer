using System;
using System.IO;

using UstPlayer.Settings.Domains;

namespace UstPlayer.ViewModels;

/// <summary>
/// 歌词页 ViewModel（对应 1.1.x <c>lyric_page.py</c>）：歌词文件与显示开关。
/// </summary>
/// <remarks>
/// 这一页几乎全是绑定，ViewModel 只承担一件事：算出选择文件对话框的起始目录
/// （优先当前歌词所在目录，其次上次打开工程的目录）。把这点逻辑放在这里而不是
/// code-behind，是为了它能被单测覆盖——它的分支在手工点击时很容易漏测。
/// </remarks>
internal sealed class LyricPageViewModel : ViewModelBase
{
    private readonly AppServices _services;

    /// <summary>创建歌词页 ViewModel。</summary>
    /// <param name="services">组合根。</param>
    internal LyricPageViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>显示开关子域（<c>show_lyric</c>）。</summary>
    internal DisplaySettings Display => _services.Settings.Display;

    /// <summary>播放器子域（<c>lrc_path</c>）。</summary>
    internal PlayerSettings Player => _services.Settings.Player;

    /// <summary>
    /// 选择歌词文件时的起始目录。
    /// </summary>
    /// <returns>目录路径；都无法使用时返回 <see langword="null"/>（对话框用系统默认位置）。</returns>
    internal string? ResolveStartDirectory()
    {
        var lrcPath = Player.LrcPath.Trim();

        if (lrcPath.Length > 0)
        {
            var directory = Path.GetDirectoryName(lrcPath);

            // 目录可能已被删除或改名，此时不能把它交给对话框
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        var lastOpen = _services.Settings.LastOpenDirectory;

        return !string.IsNullOrEmpty(lastOpen) && Directory.Exists(lastOpen) ? lastOpen : null;
    }
}
