using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UstPlayer.Platform;

using Xunit;

namespace UstPlayer.Tests.Platform;

/// <summary>
/// 伴奏音频后端的**真实集成**测试（加载原生 libvlc，解析真实音频文件）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么必须真实加载：后端的价值全在「libvlc 能不能认出这个文件、拿到正确时长、
/// 按事件汇报状态」。用假件替掉 libvlc 就只能测到自己写的那点胶水，
/// 而胶水出错的方式（原生库缺失、路径传错、解析状态判断错）恰恰都发生在真实调用上。
/// </para>
/// <para>
/// <b>刻意不测「真的放出声音」</b>：那需要音频设备，会让测试在无声卡环境（CI）下失败，
/// 而且「有没有声音」本来就无法断言。这里只断言到「媒体可解析、时长正确、
/// 就绪与失败事件按约定汇报」——**播放本身由真实进程验证**（见 build/README.md）。
/// </para>
/// </remarks>
public class AudioBackendIntegrationTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>建立临时目录。</summary>
    public AudioBackendIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"audiobackend-{Guid.NewGuid():N}");
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

    /// <summary>未配置伴奏时不创建后端（播放器应直接走墙钟计时）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 未配置伴奏时不创建后端(string? musicPath) =>
        Assert.Null(AudioBackendFactory.Create(musicPath));

    /// <summary>文件不存在时返回空并留下日志，而不是拉起 libvlc。</summary>
    [Fact]
    public void 文件不存在时不创建后端() =>
        Assert.Null(AudioBackendFactory.Create(Path.Combine(_tempDirectory, "不存在.mp3")));

    /// <summary>
    /// 真实 WAV：后端能被创建，解析完成后报就绪，且时长与文件一致。
    /// </summary>
    /// <remarks>
    /// 1 秒 8kHz 单声道 16 位静音。时长是这里最关键的一项——
    /// 播放器的结束边界依赖它，读错了整条时间轴就偏了。
    /// </remarks>
    [Fact]
    public async Task 能解析真实音频并取到时长()
    {
        var wavPath = WriteSilentWav(_tempDirectory, seconds: 1);

        var backend = AudioBackendFactory.Create(wavPath);

        Assert.NotNull(backend);

        using (backend)
        {
            // 解析是异步的：等就绪或失败，谁先到算谁
            var ready = await WaitForAsync(backend!, TimeSpan.FromSeconds(20));

            Assert.True(
                ready,
                $"媒体既未就绪也未报失败（IsLoading={backend!.IsLoading} IsInvalid={backend.IsInvalid}）");

            Assert.True(backend.IsLoaded, "就绪后 IsLoaded 应为真");
            Assert.False(backend.IsLoading, "就绪后不应仍在加载");

            // 允许少量偏差：容器时长由解码器换算得出
            Assert.InRange(backend.DurationSeconds, 0.9, 1.1);
        }
    }

    /// <summary>损坏的文件应报「失败」并标记为无效，而不是抛异常或永远停在加载中。</summary>
    [Fact]
    public async Task 损坏文件报失败()
    {
        var path = Path.Combine(_tempDirectory, "broken.mp3");
        await File.WriteAllTextAsync(path, "这不是音频，只是一段文本", Encoding.UTF8);

        var backend = AudioBackendFactory.Create(path);
        Assert.NotNull(backend);

        using (backend)
        {
            var failed = false;
            backend!.Failed += (_, _) => failed = true;

            // 等失败事件（解析可能先报 done 但时长为 0，也会走失败路径）
            var ready = await WaitForAsync(backend, TimeSpan.FromSeconds(20));

            Assert.False(ready, "损坏文件不应报就绪");
            Assert.True(backend.IsInvalid || failed, "损坏文件应标记为无效并汇报失败");
        }
    }

    /// <summary>等待就绪或失败事件，任一先到即返回（就绪为真）。</summary>
    /// <param name="backend">后端。</param>
    /// <param name="timeout">超时。</param>
    /// <returns>就绪返回 <see langword="true"/>；失败或超时返回 <see langword="false"/>。</returns>
    private static async Task<bool> WaitForAsync(IAudioBackend backend, TimeSpan timeout)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnReady(object? sender, EventArgs e) => completed.TrySetResult(true);
        void OnFailed(object? sender, string message) => completed.TrySetResult(false);

        backend.Ready += OnReady;
        backend.Failed += OnFailed;

        try
        {
            var finished = await Task.WhenAny(completed.Task, Task.Delay(timeout)).ConfigureAwait(false);

            return finished == completed.Task && await completed.Task.ConfigureAwait(false);
        }
        finally
        {
            backend.Ready -= OnReady;
            backend.Failed -= OnFailed;
        }
    }

    /// <summary>写一个 8kHz 单声道 16 位静音 WAV。</summary>
    /// <param name="directory">目录。</param>
    /// <param name="seconds">时长（秒）。</param>
    /// <returns>文件路径。</returns>
    private static string WriteSilentWav(string directory, int seconds)
    {
        const int SampleRate = 8000;
        const short Channels = 1;
        const short BitsPerSample = 16;

        var dataLength = SampleRate * seconds * Channels * (BitsPerSample / 8);
        var path = Path.Combine(directory, "silence.wav");

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);                 // PCM
        writer.Write(Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * (BitsPerSample / 8));  // byte rate
        writer.Write((short)(Channels * (BitsPerSample / 8)));      // block align
        writer.Write(BitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);

        return path;
    }
}
