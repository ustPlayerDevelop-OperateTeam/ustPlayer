using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using UstPlayer.Models;

namespace UstPlayer.Video;

/// <summary>
/// 渲染器配置（<c>up_set_config</c> 的入参）——对应 uPlRender 的 <c>RenderConfig</c>。
/// </summary>
/// <remarks>
/// 由 <see cref="PlayerLaunchParams"/> 三部分（<c>ust</c> / <c>show</c> / <c>project</c> /
/// <c>style</c>）加上输出参数（<c>width</c> / <c>height</c> / <c>fps</c> / <c>output_path</c>）组成。
/// 字段契约见根 <c>API_Docs.md</c>；字段名错误会被渲染器的 serde **静默忽略**
/// （表现为画面缺内容），因此 <c>UstPlayer.Tests/Models/JsonContractTests</c> 逐字段钉死了名字。
/// </remarks>
internal static class RenderConfig
{
    /// <summary>
    /// JSON 序列化选项。
    /// </summary>
    /// <remarks>
    /// <b>必须保持默认的 PascalCase 策略配合模型上的显式 <c>JsonPropertyName</c></b>：
    /// 不设置 <c>PropertyNamingPolicy</c>，让模型自己声明 snake_case 键名。
    /// 这样新增属性若忘记标注会以 PascalCase 出现——由 JsonContractTests 立即发现。
    /// 若改用 camelCase 策略，忘记标注会静默变成 camelCase，反而更难发现。
    /// </remarks>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // 不缩进：减少传给原生侧的数据量（配置走 UTF-8 字符串跨 FFI）
        WriteIndented = false,
        // null 值照常写出：渲染器各段均为 #[serde(default)]，显式 null 与缺省等价
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>组装渲染器配置 JSON。</summary>
    /// <param name="parameters">工程与样式参数。</param>
    /// <param name="width">输出宽（像素）。</param>
    /// <param name="height">输出高（像素）。</param>
    /// <param name="fps">帧率。</param>
    /// <param name="outputPath">
    /// 输出 MP4 路径。预览（<c>up_render_to_buffer</c>）时传空串——
    /// 渲染器按约定走「空编码器」分支，不产生文件。
    /// </param>
    /// <returns>配置 JSON 文本。</returns>
    /// <exception cref="ArgumentOutOfRangeException">宽/高/帧率非正。</exception>
    internal static string Build(
        PlayerLaunchParams parameters,
        int width,
        int height,
        int fps,
        string outputPath = "")
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);

        // 用 JsonNode 组装：模型撑起四个段，其余四个键在此追加，
        // 避免为四个标量再建一个 DTO（那样又会多一份需要同步的字段契约）。
        var node = JsonSerializer.SerializeToNode(parameters, SerializerOptions)!.AsObject();

        node["width"] = width;
        node["height"] = height;
        node["fps"] = fps;
        node["output_path"] = outputPath;

        return node.ToJsonString(SerializerOptions);
    }

    /// <summary>组装仅含 <c>ust</c> 段的 JSON（用于 <c>up_set_ust_text</c>）。</summary>
    /// <param name="ust">UST 解析结果。</param>
    /// <returns>UST JSON 文本。</returns>
    internal static string BuildUstJson(UstInfo ust)
    {
        ArgumentNullException.ThrowIfNull(ust);
        return JsonSerializer.Serialize(ust, SerializerOptions);
    }
}
