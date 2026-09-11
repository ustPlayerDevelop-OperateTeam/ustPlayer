# 1.1.x 冻结基线记录

> 记录时间：本次仓库重构（Python 源码归档至 `pysourcecode/`）之后。
> 目的：为 C# / Avalonia 2.0 迁移提供**行为与格式的比对基准**，并确保 1.1.x 仍处于可发布状态。

## 1. 归档内容

`pysourcecode/` 现为自洽的 Python 1.1.x 项目：

| 内容 | 路径 |
|---|---|
| 依赖唯一事实源 | `pysourcecode/pyproject.toml` |
| 锁文件 | `pysourcecode/uv.lock` |
| Python 版本 | `pysourcecode/.python-version`（3.13.12） |
| 源码 | `pysourcecode/src/ustplayer/`（`core/` + `ui/`） |
| 测试 | `pysourcecode/tests/`（18 个文件） |
| 翻译源与产物 | `pysourcecode/i18n/*.ts` + `*.qm` |
| 旧格式转换器 | `pysourcecode/tools/uplr_converter/`（C++17） |
| 入口 | `pysourcecode/main.py`（→ `ustplayer.app:main`） |
| 该目录独立忽略规则 | `pysourcecode/.gitignore`、`pysourcecode/.gitattributes` |
| 归档的旧 CI（GitHub 不执行子目录 workflow，仅存档） | `pysourcecode/.github/workflows/build.yml` |

根目录保留的**共享资源**（两条产品线共用）：`README.md`、`CHANGELOG.md`、`LICENSE`、`ERcode.txt`、`Terms.txt`、`icon.ico`、`icon-128.ico`、`icon.png`、`CODE_OF_CONDUCT.md`。

## 2. 基线验证结果（本机实测）

```
cd pysourcecode
uv sync
uv run pytest -q
```

结果：**248 passed, 1 skipped in 8.06s**（合计 249 个用例）。

> 说明：此前 `AGENTS.md` 记载的「249 个用例」**属实**。早期静态清点得到的 241 只统计了 `def test_` 函数定义，遗漏了参数化展开的用例，不构成文档错误，无需更正。

环境实测版本：Python 3.13.12、pytest 9.1.1、PySide6 6.11.1、pyside6-fluent-widgets 1.11.3。

## 3. 归档期间的行为修复

`pysourcecode/pyproject.toml` 的 `readme = "README.md"` 原本指向仓库根目录的 README；归档后该文件不再同目录，导致 `uv sync` 构建失败（`failed to open file ... README.md`）。已在本目录补一份 `README.md`（说明这是冻结存档并指回根目录文档），`pyproject.toml` 未改动，依赖事实源保持单一。

## 4. 1.1.x 维护策略

- **只修致命 bug，不再加功能**；`version` / `contracts.APP_VERSION` / `CHANGELOG.md` 保持一致。
- 发版仍由标签推送触发；`1.1.*` 标签走 Python 产物，`2.0.*` 标签走 C# 产物（见根 `.github/workflows/`）。
- 旧 CI 已归档到 `pysourcecode/.github/workflows/build.yml` 并补上 `pysourcecode/` 路径前缀，**GitHub 不会执行子目录中的 workflow**；如需继续为 1.1.x 出包，须把它移回根 `.github/workflows/` 并改回路径。

## 5. 作为 2.0 迁移基准的用途

下列文件是 2.0 必须**逐字兼容**的格式基准（详见迁移计划的契约章节）：

- `.uplr` / `.uprd`：ZIP 容器（`Info.json` + 资源），枚举存英文稳定 key。
- `Settings.json`：分组 → 键值字典，布尔存 `0/1`。
- `i18n/*.ts`：三语（`zh_CN` / `zh_classic` / `en_US`），共 161 条。

迁移完成后需用 1.1 实际产出的这三类文件做**双向读写往返测试**（2.0 读得进、1.1 读得回）。
