using System;
using System.IO;
using System.Linq;

using UstPlayer.I18n;

using Xunit;

namespace UstPlayer.Tests.I18n;

/// <summary>
/// 翻译资产的**部署**测试：确认 .ts 真的被复制到程序目录，且运行时能加载。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要这个类：<see cref="TranslationCatalogLoader.LoadForLocale"/> 只按
/// 「程序目录 → i18n/ 子目录 → 用户数据目录」查找 .ts。这些文件在仓库里位于
/// 冻结产品线 <c>pysourcecode/i18n/</c>，如果工程忘了复制它们到输出目录，
/// <b>语言设置会完全失效</b>——任何语言都显示中文，而且**不报任何错**，
/// 单元测试也不会失败（既有测试直接按仓库路径加载，绕过了部署这一环）。
/// </para>
/// <para>
/// 本类按**测试输出目录**（即运行时的程序目录）加载，正好覆盖那一环。
/// </para>
/// </remarks>
public class TranslationAssetsTests
{
    /// <summary>运行时的程序目录（= 测试程序集所在目录）。</summary>
    private static string ProgramDirectory => AppContext.BaseDirectory;

    /// <summary>部署目录里应包含 i18n/ 下的全部语言文件。</summary>
    [Fact]
    public void 语言目录已复制到程序目录()
    {
        var i18nDirectory = Path.Combine(ProgramDirectory, "i18n");

        Assert.True(
            Directory.Exists(i18nDirectory),
            $"未找到 {i18nDirectory}：工程没有把 .ts 复制到输出目录，"
            + "运行时的语言设置会完全失效（见 ustPlayer.Desktop.csproj / ustPlayer.Tests.csproj）。");

        var locales = TranslationCatalogLoader.DiscoverLocales(i18nDirectory).ToList();

        Assert.Contains("zh_CN", locales);
        Assert.Contains("en_US", locales);
        Assert.Contains("zh_classic", locales);
    }

    /// <summary>按程序目录加载英文目录，并验证译文真的可用。</summary>
    [Fact]
    public void 可按程序目录加载英文译文()
    {
        var catalog = TranslationCatalogLoader.LoadForLocale(ProgramDirectory, "en_US");

        Assert.NotNull(catalog);
        Assert.Equal("en_US", catalog!.Locale);

        // 抽样既有译文（来自 1.1.x）与 2.0 新增的译文，确认加载到的不是空目录
        Assert.Equal("Project", catalog.Translate("项目"));
        Assert.Equal("Fonts", catalog.Translate("字体"));
    }

    /// <summary>文言目录同样可加载。</summary>
    [Fact]
    public void 可按程序目录加载文言译文()
    {
        var catalog = TranslationCatalogLoader.LoadForLocale(ProgramDirectory, "zh_classic");

        Assert.NotNull(catalog);
        Assert.Equal("字體", catalog!.Translate("字体"));
    }

    /// <summary>
    /// 设置页按钮要打开的随程序分发的文本文件，必须真的在程序目录里。
    /// </summary>
    /// <remarks>
    /// 与 .ts 是同一类故障：文件只在仓库根目录，忘了复制到输出目录时**不会报错**，
    /// 只是用户点「ERcodes纠错」/「开源协议」永远打不开。因此用测试守住这一环。
    /// </remarks>
    [Theory]
    [InlineData("ERcode.txt")]
    [InlineData("Terms.txt")]
    public void 随程序分发的文本资源已复制到程序目录(string fileName)
    {
        var path = Path.Combine(ProgramDirectory, fileName);

        Assert.True(
            File.Exists(path),
            $"未找到 {path}：工程没有把它复制到输出目录，设置页对应按钮会永远失败。");
    }

    /// <summary>
    /// 三份目录的条目数应一致（源语言文件里的条目是 unfinished 空译文）。
    /// </summary>
    /// <remarks>
    /// 新增界面文案时必须三份一起补：只补 en_US 会让文言界面回退成中文，
    /// 而回退是静默的，不会有人发现。
    /// </remarks>
    [Fact]
    public void 三份目录条目数一致()
    {
        var i18nDirectory = Path.Combine(ProgramDirectory, "i18n");
        var counts = new[] { "zh_CN", "en_US", "zh_classic" }
            .Select(locale =>
            {
                var path = Path.Combine(i18nDirectory, $"ustplayer_{locale}.ts");
                var document = System.Xml.Linq.XDocument.Load(path);

                return document.Descendants("message").Count();
            })
            .Distinct()
            .ToList();

        Assert.True(
            counts.Count == 1,
            $"三份 .ts 的 <message> 条目数不一致：{string.Join(" / ", counts)}——新增文案时请三份一起补。");
    }
}
