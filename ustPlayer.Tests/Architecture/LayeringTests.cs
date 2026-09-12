using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using NetArchTest.Rules;

using Xunit;

namespace UstPlayer.Tests.Architecture;

/// <summary>
/// 分层约束测试：保证「逻辑层不依赖 UI 框架」「共享工程无平台条件编译」这两条架构红线
/// 不被悄悄破坏。
/// </summary>
/// <remarks>
/// 2.0 采用「共享工程 + 平台头」的组织方式，逻辑层与 UI 层同处一个程序集，
/// 编译器无法替我们强制分层，因此由测试守住。详见
/// <c>docs/adr-0002-window-chrome.md</c> 与迁移计划的分层约束一节。
/// </remarks>
public class LayeringTests
{
    /// <summary>共享工程程序集（逻辑层与 UI 层都在其中）。</summary>
    private static readonly Assembly SharedAssembly = typeof(UstPlayer.App).Assembly;

    /// <summary>
    /// 无框架依赖的逻辑层命名空间：不得引用 Avalonia / FluentAvalonia。
    /// </summary>
    /// <remarks>
    /// 只在这里列一次——此前同一份清单在三个测试里各写一遍，新增一层要改三处。
    /// <c>UstPlayer.Platform</c> 是特例：它**允许**引用 Avalonia（窗口效果需要），
    /// 因此单独由 <see cref="平台层不得依赖_UI_层命名空间"/> 覆盖。
    /// </remarks>
    private static readonly string[] FrameworkFreeNamespaces =
    [
        "UstPlayer.Models",
        "UstPlayer.Ust",
        "UstPlayer.Settings",
        "UstPlayer.Projects",
        "UstPlayer.Video",
        "UstPlayer.Timing",
        "UstPlayer.I18n",
        "UstPlayer.Diagnostics",
        "UstPlayer.Interop",
    ];

    /// <summary>UI 层命名空间：逻辑层与平台层都不得依赖它们。</summary>
    private static readonly string[] UiNamespaces =
    [
        "UstPlayer.Views",
        "UstPlayer.ViewModels",
        "UstPlayer.Controls",
        "UstPlayer.Styles",
        "UstPlayer.Converters",
    ];

    /// <summary>
    /// 无框架依赖的逻辑层不得引用 Avalonia 与 FluentAvalonia。
    /// 这些命名空间下的代码必须能在无 UI 框架的环境（含单元测试）中运行。
    /// </summary>
    /// <param name="logicNamespace">待检查的逻辑层命名空间前缀。</param>
    [Theory]
    [InlineData("UstPlayer.Models")]
    [InlineData("UstPlayer.Ust")]
    [InlineData("UstPlayer.Settings")]
    [InlineData("UstPlayer.Projects")]
    [InlineData("UstPlayer.Video")]
    [InlineData("UstPlayer.Timing")]
    [InlineData("UstPlayer.I18n")]
    [InlineData("UstPlayer.Diagnostics")]
    [InlineData("UstPlayer.Interop")]
    public void 逻辑层不得依赖_UI_框架(string logicNamespace)
    {
        var result = Types.InAssembly(SharedAssembly)
            .That().ResideInNamespaceStartingWith(logicNamespace)
            .ShouldNot().HaveDependencyOnAny("Avalonia", "FluentAvalonia")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{logicNamespace} 下的类型不应依赖 Avalonia/FluentAvalonia，违规类型：" +
            $"{string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}");
    }

    /// <summary>
    /// 逻辑层不得反向依赖 UI 层（Views / ViewModels / Controls / Styles / Converters）。
    /// </summary>
    [Fact]
    public void 逻辑层不得依赖_UI_层命名空间()
    {
        foreach (var logicNamespace in FrameworkFreeNamespaces)
        {
            AssertNoUiDependency(logicNamespace);
        }
    }

    /// <summary>
    /// 平台层不得反向依赖 UI 层。
    /// </summary>
    /// <remarks>
    /// 平台层允许引用 Avalonia（窗口效果），所以不能纳入上面的「无框架依赖」清单；
    /// 但「不得依赖 Views/ViewModels」同样适用——否则平台实现会被 UI 类型绑死，
    /// Android 头接入时就要返工。
    /// </remarks>
    [Fact]
    public void 平台层不得依赖_UI_层命名空间() => AssertNoUiDependency("UstPlayer.Platform");

    /// <summary>
    /// 元测试：确认上面那些分层断言不是「空过」。
    /// 若某个命名空间下没有任何类型（例如占位类型被删、代码被整体移走），
    /// NetArchTest 会返回成功，从而掩盖真实问题；这里显式要求各命名空间非空。
    /// </summary>
    /// <param name="logicNamespace">待检查的命名空间前缀。</param>
    [Theory]
    [InlineData("UstPlayer.Models")]
    [InlineData("UstPlayer.Ust")]
    [InlineData("UstPlayer.Settings")]
    [InlineData("UstPlayer.Projects")]
    [InlineData("UstPlayer.Video")]
    [InlineData("UstPlayer.Timing")]
    [InlineData("UstPlayer.I18n")]
    [InlineData("UstPlayer.Diagnostics")]
    [InlineData("UstPlayer.Interop")]
    [InlineData("UstPlayer.Platform")]
    public void 命名空间应存在类型(string logicNamespace)
    {
        var count = SharedAssembly.GetTypes()
            .Count(t => t.Namespace is not null &&
                        t.Namespace.StartsWith(logicNamespace, StringComparison.Ordinal));

        Assert.True(count > 0, $"{logicNamespace} 下未发现任何类型，分层约束测试会空过");
    }

    /// <summary>
    /// 共享工程内不得出现平台条件编译（<c>#if WINDOWS</c> 等）。
    /// </summary>
    /// <remarks>
    /// 平台差异必须走 <c>Platform/</c> 的运行时装配（<c>OperatingSystem.IsWindows()</c> 等），
    /// 否则 Android 头接入时需要大规模重构。这是**源码级**检查——编译产物已看不出 <c>#if</c>。
    /// </remarks>
    [Fact]
    public void 共享工程不得出现平台条件编译()
    {
        var sharedProjectDirectory = FindSharedProjectDirectory();
        Assert.True(
            Directory.Exists(sharedProjectDirectory),
            $"未定位到共享工程源码目录：{sharedProjectDirectory}");

        // 只针对平台符号；#if DEBUG 之类的常规条件编译不在此列
        string[] platformSymbols = ["WINDOWS", "MACOS", "LINUX", "ANDROID", "IOS"];
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     sharedProjectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutputPath(file))
            {
                continue;
            }

            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("#if", StringComparison.Ordinal))
                {
                    continue;
                }

                if (platformSymbols.Any(symbol =>
                        trimmed.Contains(symbol, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {trimmed}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "共享工程不得使用平台条件编译，请改为 Platform/ 的运行时装配。违规行：" +
            string.Join(" | ", offenders));
    }

    private static bool IsBuildOutputPath(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    private static void AssertNoUiDependency(string logicNamespace)
    {
        var result = Types.InAssembly(SharedAssembly)
            .That().ResideInNamespaceStartingWith(logicNamespace)
            .ShouldNot().HaveDependencyOnAny(UiNamespaces)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{logicNamespace} 不得依赖 UI 层命名空间，违规类型：" +
            $"{string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}");
    }

    /// <summary>
    /// 由测试程序集位置向上定位共享工程源码目录。
    /// </summary>
    /// <returns>共享工程目录的绝对路径；未找到返回空串。</returns>
    private static string FindSharedProjectDirectory()
    {
        // 测试程序集位于 <repo>/ustPlayer.Tests/bin/<配置>/net10.0/
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var projectFile = Path.Combine(
                directory.FullName, "ustPlayer", "ustPlayer.csproj");

            if (File.Exists(projectFile))
            {
                return Path.Combine(directory.FullName, "ustPlayer");
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }
}
