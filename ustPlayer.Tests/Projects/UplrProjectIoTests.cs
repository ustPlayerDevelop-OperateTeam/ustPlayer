using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using UstPlayer.Projects;
using UstPlayer.Settings;

using Xunit;

namespace UstPlayer.Tests.Projects;

/// <summary>
/// 工程文件导出的端到端往返测试。
/// </summary>
/// <remarks>
/// 前面已分别验证过 <c>Info.json</c> 映射与路径安全；这里验证**整条链路**：
/// 导出成 ZIP → 再导入 → 设置与资源都回到原位。这正是用户会做的事
/// （「另存工程 → 换台机器打开」），也是「格式双向兼容」承诺的最终验收。
/// </remarks>
public class UplrProjectIoTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public UplrProjectIoTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"uplr-io-{Guid.NewGuid():N}");
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

    /// <summary>导出为 <c>.uplr</c> 后导入，设置与资源都应回到原位。</summary>
    [Fact]
    public void 导出再导入应还原设置与资源()
    {
        var (io, settings) = CreateProjectIO("source");
        var ustPath = CreateResource("source", "song.ust", "UST 内容");
        var lrcPath = CreateResource("source", "song.lrc", "[00:01.00]歌词");
        var musicPath = CreateResource("source", "song.wav", "音频占位");

        settings.Project.ProjectName = "往返工程";
        settings.Project.SongName = "往返曲";
        settings.File.UstPath = ustPath;
        settings.Player.LrcPath = lrcPath;
        settings.Project.MusicPath = musicPath;
        settings.File.Encoding = "UTF-8";
        settings.File.CurveShow = true;
        settings.Display.ShowBpm = false;
        settings.Display.ShowLyric = true;
        settings.Color.BackgroundColor = "#112233";
        settings.Player.LyricPosition = "bottom";

        var archivePath = Path.Combine(_tempDirectory, "roundtrip.uplr");
        io.ExportUplr(archivePath);

        // 导出后应是一个 ZIP，且含 Info.json 与三个资源
        Assert.True(File.Exists(archivePath));
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var names = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray();
            Assert.Contains("Info.json", names);
            Assert.Contains("song.ust", names);
            Assert.Contains("song.lrc", names);
            Assert.Contains("song.wav", names);
        }

        // 换一份全新设置导入
        var (targetIo, targetSettings) = CreateProjectIO("target");
        targetIo.ImportUplr(archivePath);

        Assert.Equal("往返工程", targetSettings.Project.ProjectName);
        Assert.Equal("往返曲", targetSettings.Project.SongName);
        Assert.Equal("UTF-8", targetSettings.File.Encoding);
        Assert.True(targetSettings.File.CurveShow);
        Assert.False(targetSettings.Display.ShowBpm);
        Assert.True(targetSettings.Display.ShowLyric);
        Assert.Equal("#112233", targetSettings.Color.BackgroundColor);
        Assert.Equal("bottom", targetSettings.Player.LyricPosition);

        // 资源被解压到缓存目录，且内容一致
        Assert.True(File.Exists(targetSettings.File.UstPath));
        Assert.Equal("UST 内容", File.ReadAllText(targetSettings.File.UstPath));
        Assert.True(File.Exists(targetSettings.Player.LrcPath));
        Assert.Equal("[00:01.00]歌词", File.ReadAllText(targetSettings.Player.LrcPath));
        Assert.True(File.Exists(targetSettings.Project.MusicPath));
        Assert.Equal("音频占位", File.ReadAllText(targetSettings.Project.MusicPath));

        // 解压结果落在缓存目录内
        var cacheBase = Path.GetFullPath(targetIo.CacheBase());
        Assert.StartsWith(cacheBase, Path.GetFullPath(targetSettings.File.UstPath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>同名资源应自动去重为 <c>_2</c>。</summary>
    [Fact]
    public void 同名资源自动去重()
    {
        var (io, settings) = CreateProjectIO("dup");

        var directoryA = Path.Combine(_tempDirectory, "dup-a");
        var directoryB = Path.Combine(_tempDirectory, "dup-b");
        Directory.CreateDirectory(directoryA);
        Directory.CreateDirectory(directoryB);

        // 两个不同目录下的同名文件：包内名冲突，第二个应落成 same_2.ust
        var first = Path.Combine(directoryA, "same.ust");
        var second = Path.Combine(directoryB, "same.ust");
        File.WriteAllText(first, "A");
        File.WriteAllText(second, "B");

        settings.File.UstPath = first;
        settings.Player.LrcPath = second;

        var archivePath = Path.Combine(_tempDirectory, "dup.uplr");
        io.ExportUplr(archivePath);

        using var archive = ZipFile.OpenRead(archivePath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("same.ust", names);
        Assert.Contains("same_2.ust", names);
    }

    /// <summary>不存在的资源不写入包，对应字段写成 <c>null</c>。</summary>
    [Fact]
    public void 不存在的资源被跳过()
    {
        var (io, settings) = CreateProjectIO("missing");
        settings.File.UstPath = Path.Combine(_tempDirectory, "不存在.ust");

        var archivePath = Path.Combine(_tempDirectory, "missing.uplr");
        io.ExportUplr(archivePath);

        using var archive = ZipFile.OpenRead(archivePath);

        Assert.Single(archive.Entries);
        Assert.Equal("Info.json", archive.Entries[0].FullName);
    }

    /// <summary>导出 <c>.uprd</c> 应含 <c>video</c> 段并能被当作工程导入。</summary>
    [Fact]
    public void 导出_uprd_含视频段且可导入()
    {
        var (io, settings) = CreateProjectIO("uprd");
        settings.Project.ProjectName = "视频工程";
        settings.File.CurveShow = true;

        var archivePath = Path.Combine(_tempDirectory, "video.uprd");
        io.ExportUprd(archivePath, width: 1280, height: 720, fps: 30);

        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var infoEntry = archive.GetEntry("Info.json");
            Assert.NotNull(infoEntry);

            using var reader = new StreamReader(infoEntry!.Open(), Encoding.UTF8);
            var parsed = System.Text.Json.Nodes.JsonNode.Parse(reader.ReadToEnd())!.AsObject();

            // .uprd：video 段存在且数值正确
            var video = parsed["video"]!.AsObject();
            Assert.Equal(1280, video["width"]!.GetValue<int>());
            Assert.Equal(720, video["height"]!.GetValue<int>());
            Assert.Equal(30, video["fps"]!.GetValue<int>());

            // .uprd：curve_show 在 else 段（不在 display），且 display 含渲染器不消费的三个开关
            var display = parsed["display"]!.AsObject();
            var elseSection = parsed["else"]!.AsObject();

            Assert.False(display.ContainsKey("curve_show"));
            Assert.True(elseSection.ContainsKey("curve_show"));
            Assert.True(display.ContainsKey("show_phoneme"));
            Assert.True(display.ContainsKey("show_midinote"));
            Assert.True(display.ContainsKey("show_waveform"));
        }

        // 再导入（归一化后按 .uplr 语义处理）
        var (targetIo, targetSettings) = CreateProjectIO("uprd-target");
        targetIo.ImportUplr(archivePath);

        Assert.Equal("视频工程", targetSettings.Project.ProjectName);
        Assert.True(targetSettings.File.CurveShow);
    }

    /// <summary>导入不存在的文件抛出 <see cref="FileNotFoundException"/>。</summary>
    [Fact]
    public void 导入不存在的文件抛异常()
    {
        var (io, _) = CreateProjectIO("nonexistent");

        Assert.Throws<FileNotFoundException>(
            () => io.ImportUplr(Path.Combine(_tempDirectory, "没有这个.uplr")));
    }

    /// <summary>旧版纯文本格式仍可导入。</summary>
    [Fact]
    public void 旧版文本格式仍可导入()
    {
        var (io, settings) = CreateProjectIO("legacy");

        var legacyPath = Path.Combine(_tempDirectory, "legacy.uplr");
        File.WriteAllText(
            legacyPath,
            string.Join('\n',
            [
                "# 旧版文本 .uplr",
                "project_name=旧版工程",
                "song_name=旧版曲",
                "encoding=UTF-8",
                "bg_color=#445566",
                "lyric_pos=上",
                "silent_display=R",
                "show_bpm=1",
                "show_lyric=0",
                "curve_show=1",
            ]),
            Encoding.UTF8);

        io.ImportUplr(legacyPath);

        Assert.Equal("旧版工程", settings.Project.ProjectName);
        Assert.Equal("旧版曲", settings.Project.SongName);
        Assert.Equal("UTF-8", settings.File.Encoding);
        Assert.Equal("#445566", settings.Color.BackgroundColor);
        // 旧中文枚举值经 setter 迁移为英文 key
        Assert.Equal("top", settings.Player.LyricPosition);
        Assert.Equal("r", settings.Player.SilentDisplay);
        Assert.True(settings.Display.ShowBpm);
        Assert.False(settings.Display.ShowLyric);
        Assert.True(settings.File.CurveShow);
    }

    /// <summary>导入前会把「会被触碰的字段」重置：旧文件里没有的字段不残留上个工程的值。</summary>
    [Fact]
    public void 导入前重置未出现的字段()
    {
        var (io, settings) = CreateProjectIO("reset");

        settings.Project.SongName = "上一个工程的曲名";
        settings.Color.BackgroundColor = "#999999";

        var legacyPath = Path.Combine(_tempDirectory, "only-name.uplr");
        File.WriteAllText(legacyPath, "project_name=新工程", Encoding.UTF8);

        io.ImportUplr(legacyPath);

        Assert.Equal("新工程", settings.Project.ProjectName);
        // 旧文件里没有 song_name / bg_color → 应回到默认，而不是残留
        Assert.Equal(string.Empty, settings.Project.SongName);
        Assert.Equal("#000000", settings.Color.BackgroundColor);
    }

    // ===================== 辅助 =====================

    private (UplrProjectIO Io, SettingsManager Settings) CreateProjectIO(string name)
    {
        var root = Path.Combine(_tempDirectory, name);
        Directory.CreateDirectory(root);

        var settings = new SettingsManager(Path.Combine(root, "Settings.json"));
        var io = new UplrProjectIO(settings, cacheBaseOverride: Path.Combine(root, "cache"));

        return (io, settings);
    }

    private string CreateResource(string folder, string name, string content)
    {
        var directory = Path.Combine(_tempDirectory, folder);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }
}
