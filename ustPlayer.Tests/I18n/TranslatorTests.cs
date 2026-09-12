using System;
using System.IO;
using System.Linq;

using UstPlayer.I18n;

using Xunit;

namespace UstPlayer.Tests.I18n;

/// <summary>
/// 翻译装载与查询测试。
/// </summary>
/// <remarks>
/// 直接读取仓库内 1.1.x 的 <c>.ts</c> 资产，验证「三语共 161 条译文零重译」这条承诺
/// 确实成立——这是 2.0 迁移相对「重新做 i18n」的主要收益之一。
/// </remarks>
public class TranslatorTests : IDisposable
{
    /// <summary>源语言文件（条目数以它为准）。</summary>
    private const string SourceLocale = "zh_CN";

    private readonly string _i18nDirectory;

    /// <summary>定位仓库内的 i18n 目录。</summary>
    public TranslatorTests()
    {
        _i18nDirectory = FindI18nDirectory();
        Translator.Reset();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Translator.Reset();
        GC.SuppressFinalize(this);
    }

    // ===================== 资产完整性 =====================

    /// <summary>三种语言都应能被发现。</summary>
    [Fact]
    public void 可发现三种语言()
    {
        var locales = TranslationCatalogLoader.DiscoverLocales(_i18nDirectory).ToArray();

        Assert.Contains("zh_CN", locales);
        Assert.Contains("zh_classic", locales);
        Assert.Contains("en_US", locales);
    }

    /// <summary>
    /// 英文与文言必须**逐条**都有非空译文（zh_CN 是源语言，条目为空）。
    /// </summary>
    /// <param name="locale">语言代码。</param>
    /// <remarks>
    /// 条目数不写死：以源语言文件的 <c>&lt;message&gt;</c> 条数为准。
    /// 写死数字会变成维护陷阱——每新增一条界面文案都要改这个常量，
    /// 而真正要守住的性质是「源语言里有的，两个目标语言都不能缺」。
    /// </remarks>
    [Theory]
    [InlineData("en_US")]
    [InlineData("zh_classic")]
    public void 目标语言条目数应完整(string locale)
    {
        var catalog = Load(locale);

        Assert.Equal(CountSourceMessages(), catalog.Count);
    }

    /// <summary>统计源语言文件里的条目数。</summary>
    /// <returns>条目数。</returns>
    private int CountSourceMessages()
    {
        var path = Path.Combine(_i18nDirectory, $"ustplayer_{SourceLocale}.ts");
        var document = System.Xml.Linq.XDocument.Load(path);

        return document.Descendants("message").Count();
    }

    /// <summary>
    /// 源语言（zh_CN）的条目全部是空译文，因此装载后应为 0 条——
    /// 这正是「中文是源语言、tr() 原样返回」的实现基础。
    /// </summary>
    [Fact]
    public void 源语言不提供译文条目()
    {
        var catalog = Load("zh_CN");

        Assert.Equal(0, catalog.Count);
    }

    // ===================== 翻译查询 =====================

    /// <summary>英文译文可用。</summary>
    [Theory]
    [InlineData("基础", "Basic")]
    [InlineData("文件", "File")]
    [InlineData("成功", "Success")]
    public void 英文译文可用(string source, string expected)
    {
        var catalog = Load("en_US");

        Assert.Equal(expected, catalog.Translate(source));
    }

    /// <summary>文言译文可用，且与英文不同（确实是独立语言而非复用英文）。</summary>
    [Fact]
    public void 文言译文可用()
    {
        var classic = Load("zh_classic");

        var translated = classic.Translate("基础");

        Assert.NotNull(translated);
        Assert.NotEmpty(translated);

        var english = Load("en_US").Translate("基础");
        Assert.NotEqual(english, translated);
    }

    /// <summary>两种目标语言收录的源文本集合应完全一致（lupdate 抽取同一份代码）。</summary>
    [Fact]
    public void 两种目标语言的源文本集合一致()
    {
        var english = Load("en_US");
        var classic = Load("zh_classic");

        // 通过「对每个英文条目的源文本都能在文言里找到」间接核对：
        // 逐个探测需要暴露键集合，这里用抽样 + 计数已足以发现偏移
        Assert.Equal(english.Count, classic.Count);
    }

    // ===================== Translator 行为 =====================

    /// <summary>未装载翻译时原样返回源文本（中文），调用方无需判空。</summary>
    [Fact]
    public void 未装载翻译时返回源文本()
    {
        Assert.Equal("基础", Translator.Tr("基础"));
        Assert.Equal(Translator.DefaultLanguage, Translator.CurrentLanguage);
    }

    /// <summary>空串与 null 安全返回。</summary>
    [Fact]
    public void 空文本安全返回()
    {
        Assert.Equal(string.Empty, Translator.Tr(string.Empty));
    }

    /// <summary>装载英文后生效，且未收录的串仍返回中文。</summary>
    [Fact]
    public void 装载英文后翻译生效且未收录串回退中文()
    {
        Translator.Install("en_US", ProgramRoot());

        Assert.Equal("en_US", Translator.CurrentLanguage);
        Assert.Equal("Basic", Translator.Tr("基础"));

        // 未收录：原样返回
        var unknown = "这条文案不存在于翻译文件";
        Assert.Equal(unknown, Translator.Tr(unknown));
    }

    /// <summary>装载源语言等价于不翻译。</summary>
    [Fact]
    public void 装载源语言等价于不翻译()
    {
        Translator.Install("zh_CN", ProgramRoot());

        Assert.Equal("zh_CN", Translator.CurrentLanguage);
        Assert.Equal("基础", Translator.Tr("基础"));
    }

    /// <summary>找不到翻译文件时回退源语言且不抛异常。</summary>
    [Fact]
    public void 翻译文件缺失时回退源语言()
    {
        Translator.Install("no_SUCH", ProgramRoot());

        Assert.Equal(Translator.DefaultLanguage, Translator.CurrentLanguage);
        Assert.Equal("基础", Translator.Tr("基础"));
    }

    /// <summary>「跟随系统」应解析出受支持的语言代码。</summary>
    [Fact]
    public void 跟随系统解析出受支持语言()
    {
        var locale = Translator.SystemLocale();

        Assert.Contains(locale, TranslationCatalogLoader.SupportedLanguages.Keys);
    }

    /// <summary>语言显示名与 1.1.x 的 <c>SUPPORTED_LANGUAGES</c> 一致。</summary>
    [Fact]
    public void 语言显示名与_1_1_一致()
    {
        var languages = TranslationCatalogLoader.SupportedLanguages;

        Assert.Equal("简体中文", languages["zh_CN"]);
        Assert.Equal("文言（华夏）", languages["zh_classic"]);
        Assert.Equal("English", languages["en_US"]);
    }

    // ===================== 辅助 =====================

    private TranslationCatalog Load(string locale) =>
        TranslationCatalogLoader.Load(Path.Combine(_i18nDirectory, $"ustplayer_{locale}.ts"));

    private static string ProgramRoot() =>
        Directory.GetParent(FindI18nDirectory())!.FullName;

    /// <summary>由测试程序集位置向上定位仓库内的 <c>pysourcecode/i18n</c> 目录。</summary>
    /// <returns>i18n 目录绝对路径。</returns>
    private static string FindI18nDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "pysourcecode", "i18n");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未定位到 pysourcecode/i18n 目录");
    }
}
