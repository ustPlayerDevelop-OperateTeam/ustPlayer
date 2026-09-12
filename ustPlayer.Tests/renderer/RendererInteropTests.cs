using System;
using System.Collections.Generic;
using System.IO;

using UstPlayer.Interop;

using Xunit;

namespace UstPlayer.Tests.Renderer;

/// <summary>
/// uPlRender 渲染器 P/Invoke 的集成测试。
/// </summary>
/// <remarks>
/// <para>
/// 原生库由 <c>build/sync-native-assets.ps1</c> 在构建前同步到
/// <c>renderer/</c> 并随输出复制（见测试工程 csproj 的 <c>SyncNativeAssets</c> 目标），
/// 因此这里是**显式依赖**而非可选跳过——渲染器没就位时用例会失败并给出明确指引，
/// 避免 CI 一路绿灯却没真正验证过任何原生调用。
/// </para>
/// <para>
/// <b>CI 可见性</b>：渲染器目前只有 Windows 构建产物（Spike 0c 待补
/// macOS / Linux 目标）。CI 的非 Windows 作业用 <c>--filter "RequiresNative!=true"</c>
/// 跳过本类；Windows 作业必须全跑。
/// </para>
/// <para>
/// 判定依据与实测数据见 <c>docs/adr-0001-renderer-strategy.md</c>。
/// </para>
/// </remarks>
[Collection(NativeRendererCollection.Name)]
public class RendererInteropTests
{
    /// <summary>原生库应由同步脚本就位；缺失时给出可操作提示。</summary>
    private static void RequireNativeRenderer()
    {
        Assert.True(
            UplRenderLoader.IsAvailable(),
            "未找到渲染器原生库。请运行 build/sync-native-assets.ps1，"
            + "或从 GitHub Release 的包中取 renderer/ 目录后重试。");
    }

    /// <summary>冒烟：能创建上下文并正常销毁。</summary>
    [Fact]
    public void 可创建并销毁渲染上下文()
    {
        RequireNativeRenderer();

        using var context = UplRenderContext.Create();
        Assert.NotNull(context);
    }

    /// <summary>冒烟：set_config + set_ust_text + set_lrc_text 全部接受。</summary>
    [Fact]
    public void 可注入配置与UST及LRC()
    {
        RequireNativeRenderer();

        using var context = UplRenderContext.Create();

        context.SetConfig(BuildMinimalConfig(192, 108));
        context.SetUstText(BuildMinimalUstJson());
        context.SetLrcText("[00:01.00]第一行\n[00:02.50]第二行");
    }

    /// <summary>
    /// 单帧渲染到缓冲：尺寸回写正确、缓冲确实被写入内容。
    /// 这是 Spike 0b 的原生侧前提。
    /// </summary>
    [Fact]
    public void 单帧渲染回写正确尺寸并写出像素()
    {
        RequireNativeRenderer();

        const int Width = 192;
        const int Height = 108;

        using var context = UplRenderContext.Create();
        context.SetConfig(BuildMinimalConfig(Width, Height));
        context.SetUstText(BuildMinimalUstJson());

        var buffer = new byte[Width * Height * 4];
        var (outWidth, outHeight) = context.RenderToBuffer(1.0, buffer, Width, Height);

        Assert.Equal(Width, outWidth);
        Assert.Equal(Height, outHeight);
        Assert.Contains(buffer, b => b != 0);
    }

    /// <summary>缓冲过小时应抛出可读的托管异常，而不是越界写内存。</summary>
    [Fact]
    public void 缓冲过小时拒绝渲染()
    {
        RequireNativeRenderer();

        using var context = UplRenderContext.Create();
        context.SetConfig(BuildMinimalConfig(192, 108));
        context.SetUstText(BuildMinimalUstJson());

        var tooSmall = new byte[16];

        Assert.Throws<ArgumentException>(() => context.RenderToBuffer(1.0, tooSmall, 192, 108));
    }

    /// <summary>非法 JSON 应返回错误码 -2（UP_ERR_PARSE）并附带可读消息。</summary>
    [Fact]
    public void 非法配置返回解析错误()
    {
        RequireNativeRenderer();

        using var context = UplRenderContext.Create();

        var error = Assert.Throws<RendererException>(() => context.SetConfig("{not json"));

        Assert.Equal(NativeMethods.UpErrParse, error.Code);
        Assert.NotEmpty(error.Message);
    }

    /// <summary>加载器应能定位到原生库文件。</summary>
    [Fact]
    public void 加载器可定位原生库()
    {
        RequireNativeRenderer();

        var path = UplRenderLoader.FindLibraryPath();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    // ===================== 测试数据 =====================

    private static string BuildMinimalConfig(int width, int height) =>
        $$"""
        {
          "ust": {
            "version": "UST Version1.2",
            "tempo": 120.0,
            "tracks": 1,
            "notes": [
              { "index": "0000", "length": 480, "lyric": "A", "note_num": 69,
                "phoneme": "", "pitch_bend": [0, 60, -60] }
            ]
          },
          "show": { "bpm": true, "play_time": true, "ust_lyric": true },
          "project": { "song_name": "test", "song_author": "a", "ust_author": "b" },
          "style": { "bg_color": "#000000", "lyric_pos": "top" },
          "width": {{width}},
          "height": {{height}},
          "fps": 60,
          "output_path": ""
        }
        """;

    private static string BuildMinimalUstJson() =>
        """
        {
          "version": "UST Version1.2",
          "tempo": 120.0,
          "tracks": 1,
          "notes": [
            { "index": "0000", "length": 480, "lyric": "A", "note_num": 69,
              "phoneme": "", "pitch_bend": [0, 60, -60] }
          ]
        }
        """;
}
