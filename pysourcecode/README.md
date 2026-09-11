# ustPlayer（Python 1.1.x 存档）

本目录是 **ustPlayer 1.1.x 的 Python / PySide6 实现**，已冻结归档，仅用于：

- 保留 1.1.x 的可发布状态（只修致命 bug，不再加功能）；
- 作为 C# / Avalonia 2.0 迁移的**行为与格式参照**（`.uplr` / `.uprd` / `Settings.json` 的兼容性基准）。

> **当前主线开发已迁往仓库根目录的 C# / Avalonia 版本。**
> 仓库根目录 = 2.0 主工程（`ustPlayer/`、`ustPlayer.Desktop/` 等），
> 项目总览、使用说明与更新日志见仓库根目录的 [README.md](../README.md) 与 [CHANGELOG.md](../CHANGELOG.md)。

## 本目录内开发（仅在需要维护 1.1.x 时）

```bash
uv sync          # 创建 .venv（Python 3.13.12）
uv run main.py   # 运行
uv run pytest    # 运行测试
```

依赖唯一事实源是本目录的 `pyproject.toml`，不要另建 `requirements.txt`。
