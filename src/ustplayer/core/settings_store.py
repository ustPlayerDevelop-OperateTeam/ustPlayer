# settings_store.py — Settings.json 文件存取
"""设置持久化服务：新版 Settings.json（结构为「分组 → 键值」字典），
旧版 Settings.ini 在首次启动时自动迁移（迁移成功后删除旧文件）。

仅负责文件 IO 与迁移，不含任何设置业务逻辑。
"""

import configparser
import json
import os

from ustplayer.core.contracts import resolve_program_root
from ustplayer.core.log import logger


class SettingsStore:
    """设置存取服务（JSON）。"""

    FILE_NAME = "Settings.json"
    LEGACY_FILE_NAME = "Settings.ini"

    def __init__(self):
        self.settings_path = self._resolve_settings_path()

    @staticmethod
    def _resolve_settings_path() -> str:
        """解析 Settings.json 路径：程序根目录优先，只读时回退用户数据目录。"""
        root = resolve_program_root()
        if os.access(root, os.W_OK):
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
                with open(self.settings_path, "r", encoding="utf-8") as f:
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

    def save(self, config: dict):
        """把配置写入 Settings.json。"""
        with open(self.settings_path, "w", encoding="utf-8") as f:
            json.dump(config, f, ensure_ascii=False, indent=2)

    def _legacy_path(self) -> str:
        """旧版 Settings.ini 路径（与新版同目录逻辑）。"""
        root = resolve_program_root()
        if os.access(root, os.W_OK):
            return os.path.join(root, self.LEGACY_FILE_NAME)
        fallback_dir = os.path.join(
            os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "ustPlayer"
        )
        return os.path.join(fallback_dir, self.LEGACY_FILE_NAME)

    def _migrate_legacy(self, legacy_path: str) -> dict:
        """旧版 Settings.ini → 分组字典，写为新版 Settings.json 并删除旧文件。"""
        parser = configparser.ConfigParser()
        try:
            parser.read(legacy_path, encoding="utf-8")
        except Exception as e:
            logger.exception(f"读取旧版设置文件失败: {e}")
            return {}
        # 与旧 ini 段一一对应：{"分组名": {"键": "值"}}
        config = {section: dict(parser[section]) for section in parser.sections()}
        try:
            self.save(config)
            os.remove(legacy_path)
            logger.info(f"已从旧版 {self.LEGACY_FILE_NAME} 迁移设置至 {self.FILE_NAME}")
        except Exception as e:
            logger.exception(f"旧版设置迁移失败（旧文件保留）: {e}")
        return config
