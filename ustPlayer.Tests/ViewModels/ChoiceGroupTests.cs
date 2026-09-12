using System;
using System.Collections.Generic;
using System.Linq;

using UstPlayer.ViewModels;

using Xunit;

namespace UstPlayer.Tests.ViewModels;

/// <summary>
/// 下拉选项组（存储层稳定 key ⇄ 显示文案）的测试。
/// </summary>
/// <remarks>
/// 这里刻意**不安装任何语言**：断言只看 <see cref="ChoiceOption.Key"/> 与
/// <see cref="ChoiceOption.SourceText"/>（与语言无关），因为 <c>Translator</c> 是全局状态，
/// 装上 en_US 会和并行运行的翻译测试互相干扰。
/// </remarks>
public class ChoiceGroupTests
{
    /// <summary>写入的必须是稳定 key，而不是显示文案。</summary>
    [Fact]
    public void 选中项写回稳定_key()
    {
        var store = "top";
        var group = CreateGroup(() => store, value => store = value, ("top", "上"), ("bottom", "下"));

        group.Selected = group.Options[1];

        Assert.Equal("bottom", store);
    }

    /// <summary>设置里的 key 决定选中项（导入工程后界面靠这条自动跟上）。</summary>
    [Fact]
    public void 选中项由设置投影而来()
    {
        var store = "dash";
        var group = CreateGroup(
            () => store,
            value => store = value,
            ("r", "R"),
            ("dash", "-"),
            ("custom", "自定义文字"));

        Assert.Equal("dash", group.Selected.Key);

        store = "r";

        Assert.Equal("r", group.Selected.Key);
    }

    /// <summary>key 不在候选里（旧文件、被手改过）时回退第一项，而不是抛异常或返回 null。</summary>
    [Fact]
    public void 未知_key_回退第一项()
    {
        var group = CreateGroup(() => "unknown", _ => { }, ("top", "上"), ("bottom", "下"));

        Assert.Equal("top", group.Selected.Key);
    }

    /// <summary>只有 custom 才算「自定义」，界面据此显示配套文本框。</summary>
    /// <param name="key">当前 key。</param>
    /// <param name="expected">期望值。</param>
    [Theory]
    [InlineData("custom", true)]
    [InlineData("r", false)]
    [InlineData("none", false)]
    [InlineData("dash", false)]
    public void 是否自定义(string key, bool expected)
    {
        var group = CreateGroup(
            () => key,
            _ => { },
            ("r", "R"),
            ("dash", "-"),
            ("custom", "自定义文字"),
            ("none", "什么都不显示"));

        Assert.Equal(expected, group.IsCustom);
    }

    /// <summary>选项顺序与中文原文即 1.1.x 的映射表，不能走样（存储层是契约）。</summary>
    [Fact]
    public void 选项与中文原文一致()
    {
        var group = CreateGroup(
            () => "none",
            _ => { },
            ("none", "无"),
            ("dash", "-"),
            ("custom", "自定义文字"));

        Assert.Equal(
            new[] { ("none", "无"), ("dash", "-"), ("custom", "自定义文字") },
            group.Options.Select(option => (option.Key, option.SourceText)));
    }

    /// <summary>设置被外部改动时需要显式通知（下拉项是对象，不会自动重选）。</summary>
    [Fact]
    public void 通知选中变化()
    {
        var group = CreateGroup(() => "top", _ => { }, ("top", "上"), ("bottom", "下"));
        var raised = new List<string?>();

        group.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        group.NotifySelectionChanged();

        Assert.Contains(nameof(ChoiceGroup.Selected), raised);
        Assert.Contains(nameof(ChoiceGroup.IsCustom), raised);
    }

    /// <summary>构造参数必填。</summary>
    [Fact]
    public void 参数为空时抛异常()
    {
        Assert.Throws<ArgumentNullException>(() => new ChoiceGroup(null!, _ => { }, ("top", "上")));
        Assert.Throws<ArgumentNullException>(() => new ChoiceGroup(() => "top", null!, ("top", "上")));
        Assert.Throws<ArgumentNullException>(() => new ChoiceGroup(() => "top", _ => { }, null!));
    }

    private static ChoiceGroup CreateGroup(
        Func<string> read,
        Action<string> write,
        params (string Key, string SourceText)[] options) =>
        new(read, write, options);
}
