using System;
using System.IO;

using Xunit;

namespace UstPlayer.Tests;

/// <summary>
/// <c>build/</c> 下 PowerShell 脚本的约定检查。
/// </summary>
/// <remarks>
/// <para>
/// Windows PowerShell 5.1 读取 <c>.ps1</c> 时**不假定 UTF-8**：没有 BOM 就按系统
/// ANSI 代码页（中文 Windows 上是 GBK）解码。脚本里全是中文注释与中文输出，
/// 按 GBK 解码会把多字节序列切错，产生诸如
/// <c>The string is missing the terminator</c> / <c>Unexpected token</c> 的语法错误——
/// 报错位置指向看起来完全正常的行，很难一眼看出根因。
/// </para>
/// <para>
/// 这个坑已经踩过两次（其中一次是「用编辑器改脚本时 BOM 被静默丢掉」），
/// 因此用测试守住：CI 用的是 <c>pwsh</c>（UTF-8 默认，不受影响），
/// 但开发机上直接双击 / 用 5.1 运行会立刻炸，且只有中文环境才复现。
/// </para>
/// </remarks>
public class BuildScriptsTests
{
    /// <summary>每个 <c>build/*.ps1</c> 都必须以 UTF-8 BOM 开头。</summary>
    [Fact]
    public void 构建脚本必须带_UTF8_BOM()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "build");

        Assert.True(Directory.Exists(directory), $"未找到 build 目录：{directory}");

        var scripts = Directory.GetFiles(directory, "*.ps1");
        Assert.NotEmpty(scripts);

        foreach (var script in scripts)
        {
            var bytes = File.ReadAllBytes(script);
            var hasBom = bytes.Length >= 3 &&
                         bytes[0] == 0xEF &&
                         bytes[1] == 0xBB &&
                         bytes[2] == 0xBF;

            Assert.True(
                hasBom,
                $"{Path.GetFileName(script)} 缺少 UTF-8 BOM："
                + "Windows PowerShell 5.1 会按 GBK 读取，中文会引发语法错误。");
        }
    }

    /// <summary>
    /// 由测试程序集位置向上定位仓库根目录。
    /// </summary>
    /// <returns>仓库根目录的绝对路径。</returns>
    private static string FindRepositoryRoot()
    {
        // 测试程序集位于 <repo>/ustPlayer.Tests/bin/<配置>/net10.0/
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UstPlayer.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"未能从 {AppContext.BaseDirectory} 向上定位仓库根目录");
    }
}
