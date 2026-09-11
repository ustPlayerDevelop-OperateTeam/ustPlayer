# ADR 0002：窗口镶边（标题栏）策略

- 状态：**已采纳**
- 日期：Phase 1
- 相关：`docs/adr-0001-renderer-strategy.md`（渲染器出帧策略，Phase 2 落地）

## 背景

1.1.x（PySide6）使用 `qfluentwidgets.FluentWindow`，自带 Mica/亚克力窗口效果、侧边导航与
系统强调色跟随。迁移到 Avalonia 后需要决定窗口标题栏形态。

要求：

1. 三平台（Windows / macOS / Linux）都能用，且 Android 头接入时不需重构；
2. 观感接近现有 Fluent 风格；
3. 不引入额外的大块工作量（Phase 5 已含 5 个设置页 + 对话框 + 主题）。

## 关键实证：ClassIsland 的做法

参考了本地 ClassIsland 源码（同类跨平台 Avalonia + FluentAvalonia 应用，发布 Windows / macOS / Linux）：

- `ClassIsland.Core/Controls/MyWindow.cs`：`public partial class MyWindow : AppWindow`
  —— **窗口基类直接继承 FluentAvalonia 的 `AppWindow`**，全应用 20+ 个窗口都基于它。
- `ClassIsland/Views/SettingsWindowNew.axaml.cs` 的标题栏配置：

  ```csharp
  TitleBar.ExtendsContentIntoTitleBar = true;
  TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;
  TitleBar.Height = 48;

  // 只有 macOS 才额外做客户区扩展
  if (OperatingSystem.IsMacOS())
  {
      ExtendClientAreaToDecorationsHint = true;
      ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
      ExtendClientAreaTitleBarHeightHint = -1;
      SystemDecorations = SystemDecorations.Full;
  }
  ```

- `platforms/ClassIsland.Platforms.Windows/Patches/` 下两个 **Harmony 补丁**
  （`Win32WindowManagerConstructorPatcher`、`AppWindowInitializeAppWindowPatcher`）只在
  Windows 平台工程注册，用于在特定策略下关掉 Win32 标题栏管理器。

**结论**：`AppWindow` 并非编译期 Windows 限定，FluentAvalonia 内部按运行时平台降级，
因此可以安全地作为跨平台窗口基类。

## 补充实证：headless 下会崩，但那只影响测试

在 Avalonia 的 headless 平台上实例化 `AppWindow` 会抛：

```
System.ArgumentException : An item with the same key has already been added.
    Key: FluentAvalonia.Interop.Win32.HWND
   at FluentAvalonia.UI.Windowing.Win32WindowManager..ctor(AppWindow window)
   at FluentAvalonia.UI.Windowing.AppWindow.InitializeAppWindow()
```

`AppWindow` 构造函数无条件创建 `Win32WindowManager`，与 headless 平台已注册的
Win32 属性冲突。**这只影响 headless 单元测试，不影响真实桌面运行**（ClassIsland 的
Windows 补丁正是用来接管这一步的）。

因此本项目的测试策略是：

- 窗口的**类型关系**在单元测试中断言（不实例化）；
- 窗口的**实例化与启动**由 `build/verify-app-launch.ps1` 以真实进程启动验证。

## 候选方案与取舍

### 方案一：`AppWindow` 基类 + `TitleBar` 配置（**已采纳**）

- 优点：Windows 上获得完整 WinUI 观感（标题栏配色、拖拽区命中测试、Mica、Win11 圆角），
  与 ClassIsland 同路线，久经验证；macOS 只需补三行客户区扩展。
- 缺点：headless 下无法实例化（见上），需要进程级启动验证补充覆盖。

### 方案二：裸 `Window` + `ExtendClientArea*`（未采纳）

- 曾作为初版方案。缺点：拿不到 `AppWindowTitleBar` 的三态配色与
  `SetDragRectangles` 精确拖拽区；Windows 观感弱于方案一。

### 方案三：`SystemDecorations=None` 全自绘（未采纳）

- 需自绘按钮、处理双击最大化与系统菜单、**自己 P/Invoke 补贴边吸附**、阴影与命中区，
  且按钮位置平台相关（Windows 在右、macOS 在左）。成本吃掉 Phase 5 预算，收益有限。

## 决定

1. `UstPlayer.Views.ShellWindow` 继承 `FluentAvalonia.UI.Windowing.AppWindow`，
   在构造函数中设置：

   ```csharp
   TitleBar.ExtendsContentIntoTitleBar = true;
   TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;
   ```

2. **仅在 macOS** 额外设置 `ExtendClientAreaToDecorationsHint` /
   `PreferSystemChrome` / `ExtendClientAreaTitleBarHeightHint = -1` /
   `SystemDecorations.Full`（与 ClassIsland 一致；Windows 由其内部 Win32 管理器负责，
   重复设置会互相打架）。

3. 所有应用窗口继承 `ShellWindow`。播放器窗口 `Views/PlayerWindow` **不**继承它：
   以 `WindowState=FullScreen` + `SystemDecorations=None` + `Topmost` 呈现，
   本就不需要标题栏（对应 1.1.x `NotePlayerLauncher` 中「窗口标志必须在
   show/showFullScreen 之前统一设置，否则全屏与置顶标志互相冲突导致边角漏出」的教训）。

4. 版本号展示统一由 `Views/AppInfo.Version` 提供：取自
   `AssemblyInformationalVersion`，并剥掉 SDK 自动附加的 `+{git-sha}` 后缀。

## 实现注记

- `Window.ExtendClientAreaChromeHints` 属性与其枚举类型同名：直接写
  `ExtendClientAreaChromeHints.PreferSystemChrome` 会被解析为「实例成员访问」而报
  CS0176，写全限定名又易搞错命名空间。本项目改用
  `SetValue(Window.ExtendClientAreaChromeHintsProperty, Enum.Parse(..., "PreferSystemChrome"))`
  绕开该问题（仅 macOS 分支使用）。同理 `SystemDecorations` 右侧需写
  `Avalonia.Controls.SystemDecorations.Full`。

## 后果

- Phase 5 实现导航壳时直接复用 `ShellWindow`，不为窗口镶边单独排期。
- 窗口实例化不进 headless 测试；CI 需运行 `build/verify-app-launch.ps1` 覆盖启动路径。
- 若将来需要品牌化标题栏，可在 `ShellWindow` 内调整 `TitleBar` 属性，调用方无需改动。
