# uPlRender API 文档（ustplayer_renderer.dll）

>[!WARNING]
>
>本文档使用AI进行辅助编写，可能有些地方是说胡话，对不起——

> 本文档描述 uPlRender（Rust 渲染库，仓库：`ustPlayerDevelop-OperateTeam/uPlRender`）对外暴露的 C ABI，
> 供 ustPlayer / 二次开发项目对接参考。权威定义见源仓库 `bindings/ustplayer_renderer.h` 与 `lib/ffi.rs`，本文件与它们保持同步。

## 1. 概述

- 实现语言：Rust（cdylib），输出 `ustplayer_renderer.dll`（64 位，Windows）。
- 交互方式：纯 C 函数接口（`#[no_mangle] extern "C"`），无 C++ 运行时依赖；字符串一律 **UTF-8**。
- 数据交换：配置与 UST 内容均以 **JSON 文本**传入；所有跨 FFI 字符串由库分配、库释放，宿主绝不 free。
- 上下文句柄：`u64`（`up_create_context` 分配，`up_destroy_context` 释放；一次创建可多次复用，内部缓存字体/LRC/时序模型）。

## 2. C ABI 函数

### 2.1 生命周期

| 函数 | 签名 | 说明 |
|---|---|---|
| `up_create_context` | `uint64_t up_create_context(void)` | 创建渲染上下文，失败返回 `0` |
| `up_destroy_context` | `void up_destroy_context(uint64_t ctx)` | 销毁上下文，释放全部内部资源（幂等） |
| `up_set_config` | `int32_t up_set_config(ctx, const char* json_config)` | 设置渲染配置（`RenderConfig` JSON，UTF-8）；同时重建时序模型与字体上下文 |
| `up_set_ust_text` | `int32_t up_set_ust_text(ctx, const char* ust_json)` | 直接传入 UST JSON，替换 `config.ust` 并重建时序模型 |
| `up_set_lrc_text` | `int32_t up_set_lrc_text(ctx, const char* lrc_text)` | 直接传入 LRC 文本（内部多编码探测：utf-8-sig → utf-8 → gbk → gb2312 → shift-jis），替换 `lrc_path` 读取结果 |

### 2.2 视频导出

| 函数 | 签名 | 说明 |
|---|---|---|
| `up_begin_export` | `int32_t up_begin_export(ctx)` | 按当前配置（width/height/fps/output_path）初始化编码器 |
| `up_render_frame` | `int32_t up_render_frame(ctx, double elapsed_sec)` | 渲染并送入一帧。**帧完全由宿主驱动，库不维护内部时钟**——`elapsed_sec` 为该帧的播放时间（秒），请传合法时间；负值/超范围时间按当前时序模型处理 |
| `up_end_export` | `int32_t up_end_export(ctx)` | flush 编码器、写尾部、关闭文件 |
| `up_set_progress_callback` | `void up_set_progress_callback(ctx, UstPlayerProgressFn cb)` | 设置编码进度回调（千分比 0~1000）；回调类型 `extern "C" fn(i32)` |

### 2.3 单帧离屏渲染（不编码，供预览）

| 函数 | 签名 | 说明 |
|---|---|---|
| `up_render_to_buffer` | `int32_t up_render_to_buffer(ctx, double elapsed_sec, uint8_t* buf, int32_t buf_len, int32_t* out_w, int32_t* out_h)` | 渲染单帧到宿主提供的 **RGBA8888（premultiplied）** 缓冲；`buf_len >= width*height*4`；`out_w/out_h` 回写实际尺寸。内部以 `u64` 计算并检查上限（`w*h*4 > i32::MAX` 拒绝），防回绕越界 |

### 2.4 错误消息

| 函数 | 签名 | 说明 |
|---|---|---|
| `up_last_error` | `const char* up_last_error(ctx)` | 最近一次错误消息（UTF-8，静态缓冲）；无错误返回 `NULL`；下次调用任何 `up_*` 前内容仍有效 |

## 3. 错误码

| 常量 | 值 | 含义 |
|---|---|---|
| `UP_OK` | 0 | 成功 |
| `UP_ERR_INVALID_ARG` | -1 | 空指针 / 非法枚举 / 长度越界 |
| `UP_ERR_PARSE` | -2 | UST / LRC / JSON 解析失败 |
| `UP_ERR_IO` | -3 | 文件读写失败 |
| `UP_ERR_FONT` | -4 | 字体加载失败 |
| `UP_ERR_RENDER` | -5 | 渲染阶段失败 |
| `UP_ERR_ENCODE` | -6 | 编码器初始化 / 写入失败 |
| `UP_ERR_INTERNAL` | -99 | 未分类内部错误（已捕获的 panic） |

> 所有导出函数内部以 `catch_unwind` 包裹，**绝不 panic 跨越 FFI 边界**；非法参数一律返回负错误码。

## 4. 生命周期 / 线程 / 内存约定

- 句柄表：`RwLock<HashMap<u64, Arc<Mutex<ContextInner>>>>`；
  **不同 ctx 可并发调用**（各自独立锁），**同一 ctx 的所有调用必须串行**。
- 除 `up_render_to_buffer` 的 `buf` 由宿主提供外，其余字符串/句柄由库分配、库释放；宿主不得 free 库返回的任何指针。
- 所有跨 FFI 字符串统一 UTF-8。

## 5. RenderConfig JSON 结构

`up_set_config` 的 JSON 反序列化结构（serde，**顶层及各段均 `#[serde(default)]`，未知字段默认忽略**）：

```jsonc
{
  "ust": {                      // UstInfo
    "version": "",              // 版本号（仅记录）
    "tempo": 120.0,             // BPM（默认 120）
    "tracks": 1,                // 轨道数
    "notes": [                  // NoteInfo
      { "index": "0000", "length": 480, "lyric": "あ",
        "note_num": 60, "phoneme": "a", "pitch_bend": [0, 64, 128] }
    ]
  },
  "show": {                     // ShowConfig 显示开关
    "bpm": true, "play_time": true, "song_name": true,
    "song_author": true, "ust_author": true,
    "lyric": true, "curve_show": false,
    "note_name": true, "ust_lyric": true, "copyright": true,
    "font_note": "", "font_ust_lyric": "", "font_lrc": "", "font_other": "",
    "custom_font_paths": []
  },
  "project": {                  // ProjectInfo
    "project_name": "", "song_name": "", "song_author": "", "ust_author": ""
  },
  "style": {                    // PlayerStyle
    "bg_color": "#000000",      // 背景色
    "note_color": "#6c6c6c",    // 音名色
    "lyric_color": "#FFFFFF",   // 歌字色
    "lyric_text_color": "#FFFFFF", // LRC 歌词色
    "other_text_color": "#FFFFFF", // 其他文字色
    "lyric_pos": "top",         // "top" | "bottom"
    "fullscreen": true,         // （播放器用，渲染器不消费）
    "lrc_path": "",             // LRC 文件路径（被 set_lrc_text 覆盖）
    "music_path": "",           // 伴奏路径（渲染器不消费）
    "silent_display": "r",      // "r"|"dash"|"custom"|"none"
    "silent_custom_text": "",
    "end_display": "end",       // "end"|"dash"|"custom"|"none"
    "end_custom_text": "",
    "pitch_placeholder": "none",// "none"|"dash"|"custom"
    "pitch_custom_text": "",
    "pitch_curve_color": "#FFFFFF", // 音高线颜色
    "app_version": "1.1.0 Beta 2"   // 版权行版本号（宿主显式传入；模型默认值已过时，勿依赖）
  },
  "width": 1920,                // 输出画面宽
  "height": 1080,               // 输出画面高
  "fps": 60,                    // 帧率
  "output_path": "out.mp4"      // 输出路径（非 .mp4 时使用空编码器，仅走流程不产文件）
}
```

### 5.1 与 ustPlayer 的字段映射

| RenderConfig 字段 | ustPlayer 侧来源 |
|---|---|
| `ust` | `UstFileReader.parse()` 结果（`UstInfo`） |
| `show` | `SettingsManager.build_ust_info()` 的 `ShowConfig` |
| `project` | `ProjectInfo`（项目名/曲名/作者/调音师） |
| `style` | `PlayerStyle`（颜色/枚举/自定义文本；`app_version` 由 `video_exporter` 补 `APP_VERSION`） |
| `width/height/fps` | `VideoExporter.render()` 参数 |
| `output_path` | 导出对话框选择的 MP4 路径 |

### 5.2 已写入但渲染器**暂未消费**的字段（serde 默认忽略）

ustPlayer 端（`_settings_to_uprd_info`）为打通「播放器设置 → 导出视频」通道，已写入以下字段；
渲染器当前版本**忽略**它们（兼容），实现后可对照消费：

- `display.show_note_name` / `show_ust_lyric` / `show_copyright`
- `display.font_note` / `font_ust_lyric` / `font_lrc` / `font_other`（字体族名，空 = 默认）
- `display.custom_font_paths`（自定义字体文件路径数组，注册后可经 fontdb/cosmic-text 使用）
- `display.fullscreen`、`style.music_path` 等仅播放器语义字段

（注：这些字段位于 `.uprd` 的 `Info.json` display 段；`.uprd` 的 RenderConfig 结构以渲染器 `model.rs` 为准。）

## 6. 时序与帧约定（与 ustPlayer 播放器一致）

- 每拍 = **480 tick**；`tick_per_second = tempo * 480 / 60`。
- **帧由宿主驱动**：总帧 = `ceil(结束边界秒 × fps) + fps`（末尾 +1 秒为结束画面），与播放器「播完停 1 秒关闭」一致。
- 结束边界：有伴奏时以「音频播完」为准（宿主经 ffprobe 取得时长并**补一个尾部休止音符 `R`** 覆盖 [内容结束, 音频结束] 的静默区间）；无伴奏时按音符 tick 总长。
- `lyric == "R"` → 空拍/静默文字（`silent_display` 规则）；`lyric == "-"` → 延音（保留上个有效歌词）；其余正常显示。
- 播放时间文本与 BPM 显示与播放器**逐字一致**（百分秒截断；`BPM=120.0` 保留一位小数；超过 1 小时带小时位）。
- 字号为**点值×96/72 像素换算**（与播放器 QPainter 一致），随分辨率等比缩放；`up_render_to_buffer` 与 `up_render_frame` 共用同一套显示状态计算。

## 7. 字体回退

- 逐字歌词/信息文字回退族指向 **Windows 常见字体**（无「等线」的环境如 CI 也能渲染）。
- 字体加载失败返回 `UP_ERR_FONT`，错误消息可用 `up_last_error` 读取。

## 8. 构建与放置（ustPlayer 侧）

```powershell
# 1. 编译（windows-latest；本机需 Rust 工具链）
cargo build --release --manifest-path <uPlRender 仓库>\Cargo.toml

# 2. 产物
<uPlRender 仓库>\target\release\ustplayer_renderer.dll

# 3. 放入 ustPlayer
#    - 开发/本地：仓库根 renderer\ustplayer_renderer.dll（或者程序根目录）
#    - CI（build.yml）：Nuitka include-data-files → 产物 renderer\ 子目录
```

- 加载顺序（`renderer_ffi.RendererLoader`）：`<程序根>/ustplayer_renderer.dll` → `<程序根>/renderer/ustplayer_renderer.dll`。
- 版本追踪：uPlRender 自身无版本号，以源仓库 commit 为准；建议每次替换 DLL 时在 ChangeLog 记录对应 commit。

## 9. 近期行为校正（2026-08 未提交工作区 / 已提交改动）

| 变更 | 影响 |
|---|---|
| 字号改「点 × 96/72」像素换算 + 小字基线修正 | 导出视频与播放器画面字号一致（此前小 25%） |
| `format_play_time` 百分秒截断、`MM:SS:CC` / 小时位补零 | 时间显示与播放器逐字一致 |
| `format_tempo`（`120.0` → `BPM=120.0`） | BPM 显示与播放器一致 |
| Mutex `poison` 兜底（`unwrap_or_else(into_inner)`） | 任一线程 panic 后其余调用不再连锁崩溃 |
| `up_render_to_buffer` 溢出安全检查（u64 计算） | 防超大分辨率下 `w*h*4` 回绕导致越界写 |
| `up_render_frame` 注释澄清「宿主驱动帧」 | 与 ustPlayer 驱动方式对齐（库不维护内部时钟） |
