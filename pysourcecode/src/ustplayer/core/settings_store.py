# settings_store.py — Settings.json 文件存取
"""设置持久化服务：新版 Settings.json（结构为「分组 → 键值」字典），
旧版 Settings.ini 在首次启动时自动迁移（迁移成功后删除旧文件）。

仅负责文件 IO 与迁移，不含任何设置业务逻辑。
"""

import configparser
import json
import os

from ustplayer.core.contracts import ensure_writable_dir, resolve_program_root
from ustplayer.core.log import logger


class SettingsStore:
    """设置存取服务（JSON）。"""

    FILE_NAME = "Settings.json"
    LEGACY_FILE_NAME = "Settings.ini"

    def __init__(self):
        self.settings_path = self._resolve_settings_path()

    @staticmethod
    def _resolve_settings_path() -> str:
        """解析 Settings.json 路径：程序根目录优先，实际不可写时回退用户数据目录。"""
        root = resolve_program_root()
        if ensure_writable_dir(root):
            return os.path.join(root, SettingsStore.FILE_NAME)
        fallback_dir = os.path.join(
            os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ustPlayer"
        )
        try:
            os.makedirs(fallback_dir, exist_ok=True)
        except OSError:
            pass
        return os.path.join(fallback_dir, SettingsStore.FILE_NAME)

    def load(self) -> dict:
        """读取设置；JSON 不存在时尝试从旧版 Settings.ini 迁移；都没有则返回空配置。"""
        if os.path.exists(self.settings_path):
            try:
                # utf-8-sig 兼容 Windows 记事本等编辑器写入的 UTF-8 BOM
                with open(self.settings_path, "r", encoding="utf-8-sig") as f:
                    data = json.load(f)
                if isinstance(data, dict):
                    return data
                logger.warning(f"设置文件格式异常，按空配置处理: {self.settings_path}")
                return {}
            except Exception as e:
                logger.exception(f"读取设置文件失败，按空配置处理: {e}")
                return {}

        legacy = self._legacy_path()
        if legacy and os.path.exists(legacy):
            return self._migrate_legacy(legacy)
        return {}

    @staticmethod
    def _fallback_path() -> str:
        """用户数据目录回退路径（与 _resolve_settings_path 保持一致）。"""
        fallback_dir = os.path.join(
            os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ustPlayer"
        )
        return os.path.join(fallback_dir, SettingsStore.FILE_NAME)

    def save(self, config: dict):
        """把配置写入 Settings.json（临时文件 + 原子替换，避免写一半损坏配置）。

        首选路径的目录虽可写、但目标文件本身可能只读 / 被 ACL 拒绝替换
        （Windows 常见），此时自动改写到 %LOCALAPPDATA%\\ustPlayer 并切换
        后续使用的 settings_path，避免设置从此静默丢失。
        """
        if self._try_save(self.settings_path, config):
            return

        fallback = self._fallback_path()
        logger.warning(f"首选设置文件不可替换，回退到: {fallback}")
        if self._try_save(fallback, config):
            self.settings_path = fallback

    @staticmethod
    def _try_save(target_path: str, config: dict) -> bool:
        """写临时文件并原子替换到 target_path；任何一步失败都清理临时文件。"""
        tmp_path = target_path + ".tmp"
        try:
            os.makedirs(os.path.dirname(target_path), exist_ok=True)
            with open(tmp_path, "w", encoding="utf-8") as f:
                json.dump(config, f, ensure_ascii=False, indent=2)
            os.replace(tmp_path, target_path)
            return True
        except OSError as e:
            logger.warning(f"写入设置失败: {target_path} ({e})")
            return False
        finally:
            try:
                if os.path.exists(tmp_path):
                    os.remove(tmp_path)
            except OSError:
                pass

    def _legacy_path(self) -> str:
        """旧版 Settings.ini 路径（与新版同目录逻辑）。"""
        root = resolve_program_root()
        if ensure_writable_dir(root):
            return os.path.join(root, self.LEGACY_FILE_NAME)
        fallback_dir = os.path.join(
            os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ustPlayer"
        )
        return os.path.join(fallback_dir, self.LEGACY_FILE_NAME)

    def _migrate_legacy(self, legacy_path: str) -> dict:
        """旧版 Settings.ini → 分组字典，写为新版 Settings.json 并删除旧文件。

        旧版值允许包含未转义的 %（如项目名 100% Pure），因此必须关闭
        ConfigParser 的插值，否则迁移会抛 InterpolationSyntaxError。
        """
        parser = configparser.ConfigParser(interpolation=None)
        try:
            parser.read(legacy_path, encoding="utf-8-sig")
        except Exception as e:
            logger.exception(f"读取旧版设置文件失败: {e}")
            return {}
        try:
            config = {section: dict(parser[section]) for section in parser.sections()}
        except Exception as e:
            logger.exception(f"解析旧版设置文件失败: {e}")
            return {}
        try:
            self.save(config)
            os.remove(legacy_path)
            logger.info(f"已从旧版 {self.LEGACY_FILE_NAME} 迁移设置至 {self.FILE_NAME}")
        except Exception as e:
            logger.exception(f"旧版设置迁移失败（旧文件保留）: {e}")
        return config
