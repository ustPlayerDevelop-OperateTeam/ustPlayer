using System;
using System.IO;

using UstPlayer.Projects;
using UstPlayer.Settings;
using UstPlayer.Settings.Domains;

namespace UstPlayer.ViewModels;

/// <summary>
/// 基础页 ViewModel — 项目信息、显示选项与工程导入/导出。
/// </summary>
/// <remarks>
/// <para>
/// <b>这里没有「同步界面」的方法</b>：<see cref="Project"/> / <see cref="Display"/> 等
/// 子域自身会通知变更，XAML 直接双向绑定，导入工程后界面自动更新。
/// </para>
/// <para>
/// 文件选择与提示条属于交互，留在 View（code-behind）：ViewModel 只接受「已选好的路径」，
/// 因此这里是可单测的纯逻辑，不需要为对话框引入服务抽象。
/// </para>
/// </remarks>
internal sealed class BasicPageViewModel : ViewModelBase
{
    private readonly AppServices _services;

    /// <summary>创建基础页 ViewModel。</summary>
    /// <param name="services">组合根。</param>
    internal BasicPageViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>项目信息子域（项目名 / 曲名 / 作者 / 伴奏路径）。</summary>
    internal ProjectSettings Project => _services.Settings.Project;

    /// <summary>显示开关子域。</summary>
    internal DisplaySettings Display => _services.Settings.Display;

    /// <summary>文件子域（UST 路径与编码）。</summary>
    internal FileSettings File => _services.Settings.File;

    /// <summary>上次打开工程所在目录（供打开对话框定位）。</summary>
    internal string LastOpenDirectory => _services.Settings.LastOpenDirectory;

    /// <summary>上次导出工程所在目录（供保存对话框定位）。</summary>
    internal string LastExportDirectory => _services.Settings.LastExportDirectory;

    /// <summary>导入 <c>.uplr</c> / <c>.uprd</c> 工程。</summary>
    /// <param name="uplrPath">工程文件路径。</param>
    /// <exception cref="ProjectFormatException">工程文件损坏或格式不受支持。</exception>
    /// <exception cref="IOException">读写失败。</exception>
    /// <remarks>
    /// 只做「导入 + 记住目录」。界面刷新由设置通知驱动，无需在此回填控件。
    /// </remarks>
    internal void ImportProject(string uplrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uplrPath);

        _services.ProjectIo.ImportUplr(uplrPath);
        RememberDirectory(uplrPath, isExport: false);
    }

    /// <summary>把当前设置导出为 <c>.uplr</c> 工程。</summary>
    /// <param name="uplrPath">输出路径（应以 <c>.uplr</c> 结尾，调用方保证）。</param>
    internal void ExportProject(string uplrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uplrPath);

        _services.ProjectIo.ExportUplr(uplrPath);
        RememberDirectory(uplrPath, isExport: true);
    }

    /// <summary>
    /// 为「保存工程」对话框生成默认文件名。
    /// </summary>
    /// <returns>不含扩展名的建议文件名。</returns>
    /// <remarks>
    /// 项目名为空时用「未命名」，避免对话框出现空文件名（1.1.x 即如此）。
    /// </remarks>
    internal string SuggestProjectFileName()
    {
        var name = Project.ProjectName.Trim();
        return name.Length > 0 ? name : "未命名";
    }

    /// <summary>记住最近使用的目录（供下次对话框定位）。</summary>
    /// <param name="path">文件路径。</param>
    /// <param name="isExport">是否为导出方向。</param>
    private void RememberDirectory(string path, bool isExport)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        if (isExport)
        {
            _services.Settings.LastExportDirectory = directory;
        }
        else
        {
            _services.Settings.LastOpenDirectory = directory;
        }
    }
}
