using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using UstPlayer.Diagnostics;

namespace UstPlayer.I18n;

/// <summary>
/// 一个语言的翻译目录（<c>.ts</c> 解析结果）。
/// </summary>
/// <remarks>
/// 1.1.x 的 <c>.ts</c> 使用**空 context 名**（<c>&lt;name&gt;&lt;/name&gt;</c>），
/// 因此按「源文本 → 译文」的单一字典即可表达，无需按 context 分组。
/// 若将来出现多个非空 context，再改为 <c>(context, source)</c> 复合键。
/// </remarks>
internal sealed class TranslationCatalog
{
    private readonly Dictionary<string, string> _messages;

    /// <summary>创建目录。</summary>
    /// <param name="locale">语言代码（如 <c>en_US</c>）。</param>
    /// <param name="messages">源文本 → 译文。</param>
    internal TranslationCatalog(string locale, Dictionary<string, string> messages)
    {
        Locale = locale;
        _messages = messages;
    }

    /// <summary>语言代码。</summary>
    internal string Locale { get; }

    /// <summary>条目数。</summary>
    internal int Count => _messages.Count;

    /// <summary>按源文本取译文。</summary>
    /// <param name="source">源文本（中文原文）。</param>
    /// <returns>译文；未收录时返回 <see langword="null"/>。</returns>
    internal string? Translate(string source) =>
        _messages.TryGetValue(source, out var translated) ? translated : null;

    /// <summary>是否收录某条源文本。</summary>
    /// <param name="source">源文本。</param>
    /// <returns>收录则返回 <see langword="true"/>。</returns>
    internal bool Contains(string source) => _messages.ContainsKey(source);
}

/// <summary>
/// Qt <c>.ts</c> 翻译文件的解析器。
/// </summary>
/// <remarks>
/// <para>
/// 直接复用 1.1.x 的翻译资产：<c>pysourcecode/i18n/ustplayer_*.ts</c> 就是 XML，
/// 无需转换即可在 C# 侧读取。因此 <b>三语共 161 条译文零重译</b>，
/// 且新增中文串仍可继续用 <c>pyside6-lupdate</c> 抽取。
/// </para>
/// <para>
/// 只采用**非 empty 的译文**：<c>zh_CN</c> 作为源语言其条目全部是
/// <c>type="unfinished"</c> 的空译文（源语言不需要翻译），跳过它们即可。
/// </para>
/// </remarks>
internal static class TranslationCatalogLoader
{
    /// <summary>从文件加载翻译目录。</summary>
    /// <param name="path">.ts 文件路径。</param>
    /// <returns>翻译目录。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="System.Xml.XmlException">文件不是合法 XML。</exception>
    internal static TranslationCatalog Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"翻译文件不存在：{path}", path);
        }

        var document = System.Xml.Linq.XDocument.Load(path);

        var locale =
            document.Root?.Attribute("language")?.Value
            ?? Path.GetFileNameWithoutExtension(path);

        var messages = new Dictionary<string, string>(StringComparer.Ordinal);

        // 遍历 <context>/<message>；1.1.x 的 context 名为空，但仍按标准结构解析
        foreach (var message in document.Descendants("message"))
        {
            var source = message.Element("source")?.Value;
            var translationElement = message.Element("translation");

            if (string.IsNullOrEmpty(source) || translationElement is null)
            {
                continue;
            }

            // 空译文 = 未翻译（zh_CN 全部如此），跳过
            var translation = translationElement.Value;
            if (string.IsNullOrEmpty(translation))
            {
                continue;
            }

            // 同一源文本出现多次时保留首个（lupdate 可能在不同 context 产生重复）
            messages.TryAdd(source, translation);
        }

        return new TranslationCatalog(locale, messages);
    }

    /// <summary>
    /// 从程序目录下查找并加载翻译目录。
    /// </summary>
    /// <param name="programRoot">程序根目录。</param>
    /// <param name="locale">语言代码。</param>
    /// <returns>翻译目录；未找到返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 查找顺序沿用 1.1.x：<c>i18n/</c> 子目录 → 程序根目录 → 用户数据目录下的 <c>i18n/</c>。
    /// </remarks>
    internal static TranslationCatalog? LoadForLocale(string programRoot, string locale)
    {
        var fileName = $"ustplayer_{locale}.ts";

        var candidates = new[]
        {
            Path.Combine(programRoot, "i18n", fileName),
            Path.Combine(programRoot, fileName),
            Path.Combine(ProgramPaths.UserDataDirectory, "i18n", fileName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                AppLogger.Debug($"已加载翻译：{candidate}");
                return Load(candidate);
            }
        }

        AppLogger.Warning($"翻译文件不存在，回退源语言：{fileName}");
        return null;
    }

    /// <summary>返回指定目录下可用的语言代码（按 <c>ustplayer_*.ts</c> 的文件名推断）。</summary>
    /// <param name="i18nDirectory">含 .ts 文件的目录。</param>
    /// <returns>语言代码序列。</returns>
    internal static IEnumerable<string> DiscoverLocales(string i18nDirectory)
    {
        if (!Directory.Exists(i18nDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(i18nDirectory, "ustplayer_*.ts")
            .Select(path => Path.GetFileNameWithoutExtension(path)["ustplayer_".Length..])
            .OrderBy(locale => locale, StringComparer.Ordinal);
    }

    /// <summary>语言代码 → 显示名（与 1.1.x 的 <c>SUPPORTED_LANGUAGES</c> 一致）。</summary>
    internal static IReadOnlyDictionary<string, string> SupportedLanguages { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["zh_CN"] = "简体中文",
            ["zh_classic"] = "文言（华夏）",
            ["en_US"] = "English",
        };
}

/// <summary>
/// 多语言支持 — 从 1.1.x <c>core/i18n.py</c> 移植。
/// </summary>
/// <remarks>
/// <para>
/// <b>中文是源语言</b>：<c>tr()</c> 收到中文原文，返回当前语言的译文；
/// 未安装翻译或未收录该串时原样返回中文，因此调用方无需判空。
/// </para>
/// <para>
/// 与 1.1.x 的差异：不再依赖 Qt 的 <c>QTranslator</c>，而是直接解析 <c>.ts</c>（XML），
/// 省去 <c>lrelease</c> 编译 <c>.qm</c> 的步骤，同时保留 <c>lupdate</c> 的抽取能力。
/// </para>
/// </remarks>
internal static class Translator
{
    /// <summary>默认语言（源语言）。</summary>
    internal const string DefaultLanguage = "zh_CN";

    /// <summary>「跟随系统」的存储值（与 <c>Settings.json</c> 的 <c>[LanguageSettings]</c> 一致）。</summary>
    internal const string SystemLanguage = "system";

    private static readonly Lock SyncRoot = new();
    private static readonly Dictionary<string, TranslationCatalog> CatalogCache = new(StringComparer.Ordinal);

    private static TranslationCatalog? _current;
    private static string _currentLanguage = DefaultLanguage;

    /// <summary>当前生效的语言代码。</summary>
    internal static string CurrentLanguage
    {
        get
        {
            lock (SyncRoot)
            {
                return _currentLanguage;
            }
        }
    }

    /// <summary>
    /// 装载指定语言。
    /// </summary>
    /// <param name="language">
    /// 语言代码，或 <see cref="SystemLanguage"/> 表示跟随系统。
    /// </param>
    /// <param name="programRoot">程序根目录（用于查找 <c>i18n/</c>）。</param>
    /// <remarks>
    /// 源语言（<see cref="DefaultLanguage"/>）不需要目录文件——直接原样返回中文。
    /// 找不到文件时回退源语言并记录警告，不抛异常（与 1.1.x 一致）。
    /// </remarks>
    internal static void Install(string language, string programRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programRoot);

        var effective = string.IsNullOrWhiteSpace(language) || language == SystemLanguage
            ? SystemLocale()
            : language;

        lock (SyncRoot)
        {
            if (effective == DefaultLanguage)
            {
                _current = null;
                _currentLanguage = DefaultLanguage;
                return;
            }

            if (!CatalogCache.TryGetValue(effective, out var catalog))
            {
                catalog = TranslationCatalogLoader.LoadForLocale(programRoot, effective);
                if (catalog is null)
                {
                    _current = null;
                    _currentLanguage = DefaultLanguage;
                    return;
                }

                CatalogCache[effective] = catalog;
            }

            _current = catalog;
            _currentLanguage = effective;
        }
    }

    /// <summary>
    /// 翻译源文本（自由函数风格的唯一入口，对应 1.1.x 的 <c>tr()</c>）。
    /// </summary>
    /// <param name="sourceText">中文原文。</param>
    /// <returns>译文；未装载翻译或未收录时原样返回 <paramref name="sourceText"/>。</returns>
    internal static string Tr(string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return sourceText;
        }

        TranslationCatalog? catalog;
        lock (SyncRoot)
        {
            catalog = _current;
        }

        return catalog?.Translate(sourceText) ?? sourceText;
    }

    /// <summary>
    /// 跟随系统时解析出具体语言。
    /// </summary>
    /// <returns>与系统显示语言匹配的语言代码；无法确定时返回 <see cref="DefaultLanguage"/>。</returns>
    /// <remarks>
    /// 文言（<c>zh_classic</c>）任何系统都不会使用，因此不参与前缀匹配。
    /// </remarks>
    internal static string SystemLocale()
    {
        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name.Replace('-', '_');

        if (TranslationCatalogLoader.SupportedLanguages.ContainsKey(name))
        {
            return name;
        }

        // 按语言前缀匹配（zh_TW → zh_CN）
        var language = culture.TwoLetterISOLanguageName;
        foreach (var code in TranslationCatalogLoader.SupportedLanguages.Keys)
        {
            if (code != "zh_classic" &&
                code.StartsWith(language, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return DefaultLanguage;
    }

    /// <summary>判断语言代码是否受支持（不含「跟随系统」）。</summary>
    /// <param name="language">语言代码。</param>
    /// <returns>受支持返回 <see langword="true"/>。</returns>
    internal static bool IsSupportedLanguage(string? language) =>
        !string.IsNullOrEmpty(language) &&
        TranslationCatalogLoader.SupportedLanguages.ContainsKey(language);

    /// <summary>清空缓存（测试用；语言切换本身不需要清缓存）。</summary>
    internal static void Reset()
    {
        lock (SyncRoot)
        {
            _current = null;
            _currentLanguage = DefaultLanguage;
            CatalogCache.Clear();
        }
    }
}
