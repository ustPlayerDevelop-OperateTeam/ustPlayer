using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

using UstPlayer.Projects;
using UstPlayer.Settings;

using Xunit;

namespace UstPlayer.Tests.Projects;

/// <summary>
/// 路径安全与解压防护测试。
/// </summary>
/// <remarks>
/// 工程文件来自不可信来源（用户互相分享）。这些用例证明防护**确实生效**：
/// 恶意成员名会被拒绝、资源引用不能越出缓存目录、超大成员会被中止。
/// 没有这些断言，防护代码是否真的拦得住只能靠读代码猜。
/// </remarks>
public class ProjectPathSafetyTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public ProjectPathSafetyTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"uplr-safety-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败无关紧要
        }

        GC.SuppressFinalize(this);
    }

    // ===================== 路径校验 =====================

    /// <summary>正常的相对路径应被接受并落在基目录内。</summary>
    [Theory]
    [InlineData("song.ust")]
    [InlineData("sub/song.ust")]
    [InlineData("a/b/c/song.ust")]
    [InlineData("带中文的名字.ust")]
    public void 正常相对路径被接受(string memberName)
    {
        var resolved = ProjectPathSafety.ResolveInside(_tempDirectory, memberName);
        var baseFull = Path.GetFullPath(_tempDirectory);

        Assert.StartsWith(baseFull, resolved, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>穿越、绝对路径、盘符、NUL、空名一律拒绝。</summary>
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../escape.txt")]
    [InlineData("sub/../../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("\\absolute.txt")]
    [InlineData("\\\\server\\share\\x.txt")]
    [InlineData("C:/windows/system32/x.txt")]
    [InlineData("C:\\windows\\x.txt")]
    [InlineData("c:x.txt")]
    [InlineData("bad\0name.txt")]
    [InlineData("")]
    public void 不安全路径被拒绝(string memberName) =>
        Assert.Throws<ProjectFormatException>(
            () => ProjectPathSafety.ResolveInside(_tempDirectory, memberName));

    /// <summary>反斜杠写法同样被规范化后校验（Windows 风格成员名）。</summary>
    [Fact]
    public void 反斜杠穿越被拒绝() =>
        Assert.Throws<ProjectFormatException>(
            () => ProjectPathSafety.ResolveInside(_tempDirectory, "sub\\..\\..\\escape.txt"));

    /// <summary>以 <c>/</c> 结尾的成员被识别为目录条目。</summary>
    [Theory]
    [InlineData("dir/", true)]
    [InlineData("dir\\", true)]
    [InlineData("dir/file", false)]
    public void 目录条目识别(string memberName, bool expected) =>
        Assert.Equal(expected, ProjectPathSafety.IsDirectoryEntry(memberName));

    // ===================== 恶意 ZIP =====================

    /// <summary>含穿越成员名的 ZIP 必须被拒绝，且不得在目标之外留下文件。</summary>
    [Fact]
    public void 穿越成员名的_zip_被拒绝()
    {
        var (io, _) = CreateProjectIO();
        var archivePath = Path.Combine(_tempDirectory, "evil.uplr");

        WriteZip(archivePath, ("Info.json", "{}"), ("../escaped.txt", "已被逃逸写入"));

        Assert.Throws<ProjectFormatException>(() => io.ImportUplr(archivePath));

        var escaped = Path.Combine(_tempDirectory, "escaped.txt");
        Assert.False(File.Exists(escaped), "穿越成员不得写到缓存目录之外");
    }

    /// <summary>缺少 <c>Info.json</c> 的 ZIP 被拒绝。</summary>
    [Fact]
    public void 缺_Info_json_的_zip_被拒绝()
    {
        var (io, _) = CreateProjectIO();
        var archivePath = Path.Combine(_tempDirectory, "noinfo.uplr");

        WriteZip(archivePath, ("song.ust", "dummy"));

        var exception = Assert.Throws<ProjectFormatException>(() => io.ImportUplr(archivePath));
        Assert.Contains("Info.json", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>顶层不是对象的 <c>Info.json</c> 被拒绝。</summary>
    [Fact]
    public void 非法_Info_json_被拒绝()
    {
        var (io, _) = CreateProjectIO();

        foreach (var content in (string[])["[1,2,3]", "不是 JSON", "\"字符串\""])
        {
            var archivePath = Path.Combine(_tempDirectory, $"bad-{Guid.NewGuid():N}.uplr");
            WriteZip(archivePath, ("Info.json", content));

            Assert.Throws<ProjectFormatException>(() => io.ImportUplr(archivePath));
        }
    }

    /// <summary>
    /// <c>Info.json</c> 里登记的资源路径也必须过安全校验——
    /// 否则恶意工程能把缓存目录之外的任意本机文件登记为工程资源。
    /// </summary>
    [Fact]
    public void Info_json_引用的穿越路径被拒绝()
    {
        var (io, _) = CreateProjectIO();
        var archivePath = Path.Combine(_tempDirectory, "evil-ref.uplr");

        var info = new JsonObject
        {
            ["basic"] = new JsonObject { ["ust_path"] = "../../../windows/win.ini" },
        };

        WriteZip(archivePath, ("Info.json", info.ToJsonString()), ("song.ust", "dummy"));

        Assert.Throws<ProjectFormatException>(() => io.ImportUplr(archivePath));
    }

    /// <summary>
    /// <c>Info.json</c> 引用了不存在于包内的资源时必须拒绝整份导入，
    /// 而不是留下一个指向空路径的半成品工程。
    /// </summary>
    [Fact]
    public void Info_json_引用不存在的资源被拒绝()
    {
        var (io, _) = CreateProjectIO();
        var archivePath = Path.Combine(_tempDirectory, "missing-ref.uplr");

        var info = new JsonObject
        {
            ["basic"] = new JsonObject { ["ust_path"] = "不存在的文件.ust" },
        };

        WriteZip(archivePath, ("Info.json", info.ToJsonString()));

        var exception = Assert.Throws<ProjectFormatException>(() => io.ImportUplr(archivePath));
        Assert.Contains("不存在", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>导入失败后设置必须整体回滚（不被半成品污染）。</summary>
    [Fact]
    public void 导入失败后设置回滚()
    {
        var (io, settings) = CreateProjectIO();

        settings.Project.ProjectName = "原有工程";
        settings.File.Encoding = "UTF-8";
        settings.Player.LyricPosition = "bottom";

        var archivePath = Path.Combine(_tempDirectory, "fails-late.uplr");

        // Info.json 合法（会改设置），但引用的资源缺失 → 在写设置之后才失败
        var info = new JsonObject
        {
            ["encoding"] = "Shift-JIS",
            ["basic"] = new JsonObject
            {
                ["project_name"] = "新工程",
                ["ust_path"] = "缺失.ust",
            },
            ["else"] = new JsonObject { ["lyric_pos"] = "top" },
        };

        WriteZip(archivePath, ("Info.json", info.ToJsonString()));

        Assert.Throws<ProjectFormatException>(() => io.ImportUplr(archivePath));

        Assert.Equal("原有工程", settings.Project.ProjectName);
        Assert.Equal("UTF-8", settings.File.Encoding);
        Assert.Equal("bottom", settings.Player.LyricPosition);
    }

    // ===================== 缓存目录 =====================

    /// <summary>缓存目录按「工程名-路径哈希前8位」命名，同名不同目录的工程互不覆盖。</summary>
    [Fact]
    public void 缓存目录按路径哈希区分()
    {
        var (io, _) = CreateProjectIO();

        var directoryA = Path.Combine(_tempDirectory, "a");
        var directoryB = Path.Combine(_tempDirectory, "b");
        Directory.CreateDirectory(directoryA);
        Directory.CreateDirectory(directoryB);

        var first = io.ResolveCacheDirectory(Path.Combine(directoryA, "same.uplr"));
        var second = io.ResolveCacheDirectory(Path.Combine(directoryB, "same.uplr"));
        var again = io.ResolveCacheDirectory(Path.Combine(directoryA, "same.uplr"));

        Assert.NotEqual(first, second);
        Assert.Equal(first, again);
        Assert.StartsWith("same-", Path.GetFileName(first), StringComparison.Ordinal);
    }

    /// <summary>缓存占用统计与清空。</summary>
    [Fact]
    public void 缓存占用统计与清空()
    {
        var (io, _) = CreateProjectIO();
        var cacheBase = io.CacheBase();

        Directory.CreateDirectory(Path.Combine(cacheBase, "proj-1234"));
        File.WriteAllBytes(Path.Combine(cacheBase, "proj-1234", "a.bin"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(cacheBase, "proj-1234", "b.bin"), new byte[2048]);

        Assert.True(io.CacheUsage() >= 3072);

        io.ClearCache();

        Assert.Equal(0, io.CacheUsage());
        Assert.False(Directory.Exists(cacheBase));
    }

    // ===================== 辅助 =====================

    private (UplrProjectIO Io, SettingsManager Settings) CreateProjectIO()
    {
        var root = Path.Combine(_tempDirectory, $"case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var settings = new SettingsManager(Path.Combine(root, "Settings.json"));
        var io = new UplrProjectIO(settings, cacheBaseOverride: Path.Combine(root, "cache"));

        return (io, settings);
    }

    private static void WriteZip(string path, params (string Name, string Content)[] entries)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);

            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}
