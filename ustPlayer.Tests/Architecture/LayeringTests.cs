using System;
using System.Linq;
using System.Reflection;

using NetArchTest.Rules;

using Xunit;

namespace UstPlayer.Tests.Architecture;

/// <summary>
/// 分层约束测试：保证「逻辑层不依赖 UI 框架」这条架构红线不被悄悄破坏。
///
/// 背景：2.0 采用单解决方案多工程 + 单共享工程的组织方式，逻辑层与 UI 层同处
/// 一个程序集，编译器无法替我们强制分层，因此由测试守住。
/// 详见 docs/ 中的迁移计划「分层约束」一节。
/// </summary>
public class LayeringTests
{
    /// <summary>共享工程程序集（逻辑层与 UI 层都在其中）。</summary>
    private static readonly Assembly SharedAssembly = typeof(UstPlayer.App).Assembly;

    /// <summary>
    /// 逻辑层命名空间不得引用 Avalonia 与 FluentAvalonia。
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
    /// 逻辑层也不得反向依赖 UI 层（Views / ViewModels / Controls / Styles / Converters）。
    /// </summary>
    [Fact]
    public void 逻辑层不得依赖_UI_层命名空间()
    {
        var uiNamespaces = new[]
        {
            "UstPlayer.Views",
            "UstPlayer.ViewModels",
            "UstPlayer.Controls",
            "UstPlayer.Styles",
            "UstPlayer.Converters",
        };

        var logicNamespaces = new[]
        {
            "UstPlayer.Models",
            "UstPlayer.Ust",
            "UstPlayer.Settings",
            "UstPlayer.Projects",
            "UstPlayer.Video",
            "UstPlayer.Timing",
            "UstPlayer.I18n",
            "UstPlayer.Diagnostics",
            "UstPlayer.Interop",
        };

        foreach (var logic in logicNamespaces)
        {
            var result = Types.InAssembly(SharedAssembly)
                .That().ResideInNamespaceStartingWith(logic)
                .ShouldNot().HaveDependencyOnAny(uiNamespaces)
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"{logic} 不得依赖 UI 层命名空间，违规类型：" +
                $"{string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>())}");
        }
    }

    /// <summary>
    /// 元测试：确认上面那条分层断言不是「空过」。
    /// 若某个逻辑命名空间下没有任何类型（例如占位类型被删、代码被整体移走），
    /// NetArchTest 会返回成功，从而掩盖真实问题；这里显式要求各命名空间非空。
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
    public void 逻辑层命名空间应存在类型(string logicNamespace)
    {
        var count = SharedAssembly.GetTypes()
            .Count(t => t.Namespace is not null &&
                        t.Namespace.StartsWith(logicNamespace, StringComparison.Ordinal));

        Assert.True(count > 0, $"{logicNamespace} 下未发现任何类型，分层约束测试会空过");
    }

    /// <summary>
    /// 共享工程中不允许出现平台条件编译（Android 头尚未加入）。
    /// 平台差异必须走 Platform/ 接口的运行时装配，否则 Android 接入时需要重构。
    /// </summary>
    [Fact]
    public void 共享工程暂不引入平台条件编译()
    {
        // 说明：这里只做「App 类型可被加载且来自共享程序集」的轻量断言，
        // 真正的 #if 扫描由 CI 脚本负责（源码级检查，见 Phase 6）。
        Assert.Equal("ustPlayer.Core", SharedAssembly.GetName().Name);
        Assert.NotNull(typeof(UstPlayer.App));
        Assert.NotNull(typeof(UstPlayer.Views.MainWindow));
    }
}
