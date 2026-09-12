using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UstPlayer.Diagnostics;
using UstPlayer.Settings.Domains;

namespace UstPlayer.ViewModels;

/// <summary>
/// 文件页 ViewModel（对应 1.1.x <c>file_page.py</c>）：UST 选择、编码与内容预览。
/// </summary>
/// <remarks>
/// <para>
/// 预览与编码校验是纯逻辑（读文件 + 解码），放在这里以便单测；
/// 提示条与文件选择框属于交互，留在 View。
/// </para>
/// <para>
/// <b>编码为什么要规范化</b>：<see cref="FileSettings.Encoding"/> 是个自由字符串，
/// 可以来自 <c>Settings.json</c>、<c>.uplr</c> 或旧版本（例如 <c>utf-8</c> 小写、<c>Shift_JIS</c> 带下划线）。
/// 若把它直接绑到 <c>ComboBox.SelectedItem</c>，取值不在候选列表里时 SelectedItem 会变成
/// <see langword="null"/>，而双向绑定会把这个 <see langword="null"/> **写回设置**——
/// 编码被清空，之后解析 UST 必然失败。因此这里统一映射到候选之一。
/// </para>
/// </remarks>
internal sealed class FilePageViewModel : ViewModelBase
{
    /// <summary>下拉框里的编码候选（与 1.1.x 的 ComboBox 一致）。</summary>
    private static readonly IReadOnlyList<string> EncodingNames = ["UTF-8", "GBK", "Shift-JIS"];

    private readonly AppServices _services;

    private string _previewText = string.Empty;

    /// <summary>创建文件页 ViewModel。</summary>
    /// <param name="services">组合根。</param>
    internal FilePageViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;

        NormalizeEncoding();
        RefreshPreview();
    }

    /// <summary>文件子域（UST 路径与音高线开关直接绑定到它）。</summary>
    internal FileSettings File => _services.Settings.File;

    /// <summary>
    /// 下拉框候选。
    /// </summary>
    /// <remarks>
    /// 必须是**实例属性**：XAML 用的是编译期绑定，解析不了静态字段
    /// （写成静态字段会直接报 AVLN2000）。
    /// </remarks>
    internal IReadOnlyList<string> SupportedEncodings => EncodingNames;

    /// <summary>
    /// 编码（读写均经过规范化，保证始终是 <see cref="SupportedEncodings"/> 之一）。
    /// </summary>
    internal string Encoding
    {
        get => Canonical(File.Encoding);
        set => File.Encoding = Canonical(value);
    }

    /// <summary>预览文本（只读展示）。</summary>
    internal string PreviewText
    {
        get => _previewText;
        private set => SetProperty(ref _previewText, value);
    }

    /// <summary>
    /// 把任意编码写法映射到候选之一。
    /// </summary>
    /// <param name="encoding">原始写法（可为空）。</param>
    /// <returns>候选之一。</returns>
    internal static string Canonical(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return FileSettings.DefaultEncoding;
        }

        // 忽略大小写与 -/_ 差异：utf-8 / utf8 / UTF_8 等价
        var key = encoding.Trim().Replace("-", string.Empty).Replace("_", string.Empty);

        if (key.Equals("utf8", StringComparison.OrdinalIgnoreCase))
        {
            return "UTF-8";
        }

        if (key.Equals("gbk", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("gb2312", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("gb18030", StringComparison.OrdinalIgnoreCase))
        {
            return "GBK";
        }

        if (key.Equals("shiftjis", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("sjis", StringComparison.OrdinalIgnoreCase))
        {
            return "Shift-JIS";
        }

        AppLogger.Warning($"无法识别的编码「{encoding}」，已回退 {FileSettings.DefaultEncoding}");
        return FileSettings.DefaultEncoding;
    }

    /// <summary>把设置里的编码规范化为候选之一（启动时调用一次）。</summary>
    internal void NormalizeEncoding() => File.Encoding = Canonical(File.Encoding);

    /// <summary>
    /// 按当前编码宽松读取 UST 并刷新预览。
    /// </summary>
    /// <remarks>
    /// 宽松 = 坏字节显示为替换符而不是报错：预览的目的就是让人**看到**内容，
    /// 而不是先判定编码是否正确（那由「编码检查」负责）。
    /// 路径为空或文件不存在时清空预览——展示旧内容会误导用户。
    /// </remarks>
    internal void RefreshPreview()
    {
        var path = File.UstPath.Trim();

        if (path.Length == 0 || !System.IO.File.Exists(path))
        {
            PreviewText = string.Empty;
            return;
        }

        try
        {
            PreviewText = System.IO.File.ReadAllText(path, CreateEncoding(throwOnInvalid: false));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"预览 UST 失败：{path}（{exception.Message}）");
            PreviewText = string.Empty;
        }
    }

    /// <summary>
    /// 以当前编码严格试读，判断编码是否正确。
    /// </summary>
    /// <returns>读取成功返回 <see langword="true"/>；失败返回 <see langword="false"/>。</returns>
    /// <remarks>
    /// 严格模式用于「编码检查」：坏字节直接抛 <see cref="DecoderFallbackException"/>，
    /// 从而给出「请换一种编码」的明确结论。
    /// </remarks>
    internal bool CheckEncoding()
    {
        var path = File.UstPath.Trim();

        if (path.Length == 0 || !System.IO.File.Exists(path))
        {
            return false;
        }

        try
        {
            _ = System.IO.File.ReadAllText(path, CreateEncoding(throwOnInvalid: true));
            return true;
        }
        catch (DecoderFallbackException)
        {
            // 编码不对：这是「检查」要报告的结论，不是异常情况
            return false;
        }
        catch (Exception exception)
        {
            AppLogger.Error($"编码检查失败：{path}", exception);
            return false;
        }
    }

    /// <summary>创建用于读文件的编码。</summary>
    /// <param name="throwOnInvalid">遇到非法字节时是否抛异常。</param>
    /// <returns>编码。</returns>
    /// <remarks>
    /// 注意本类有一个名为 <see cref="Encoding"/> 的属性，会遮蔽 <c>System.Text.Encoding</c> 类型名，
    /// 因此这里的类型与静态调用都写成完全限定名。
    /// </remarks>
    private Encoding CreateEncoding(bool throwOnInvalid)
    {
        var fallback = throwOnInvalid ? DecoderFallback.ExceptionFallback : DecoderFallback.ReplacementFallback;

        try
        {
            return System.Text.Encoding.GetEncoding(Encoding, EncoderFallback.ReplacementFallback, fallback);
        }
        catch (ArgumentException)
        {
            // 代码页不可用（理论上不会：候选都是内置或 CodePages 提供的）
            AppLogger.Warning($"编码不可用，回退 UTF-8：{Encoding}");
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: throwOnInvalid);
        }
    }
}
