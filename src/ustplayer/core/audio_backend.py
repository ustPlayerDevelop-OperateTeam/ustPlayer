# audio_backend.py — 伴奏音频后端封装
"""把 QtMultimedia 的加载 / 播放 / 状态机隔离在本模块，播放器只依赖 AudioBackend 窄接口。

- 降级导入模式（try/except + None 占位）从 player.py 迁移至此：QtMultimedia 缺失时
  `_HAS_AUDIO = False`，工厂返回 None，播放器走纯可视化计时；
- 后端通过 Qt 信号向播放器汇报「就绪 / 结束 / 错误」，播放器不接触 QMediaPlayer 细节；
- 状态查询（位置 / 时长 / 是否在播 / 媒体阶段）收敛为布尔窄接口，便于 mock 与无音频环境测试。
"""

from typing import Optional

from PySide6.QtCore import QObject, QUrl, Signal

try:
    from PySide6.QtMultimedia import QAudioOutput, QMediaPlayer
    _HAS_AUDIO = True
except Exception:  # QtMultimedia 缺失时降级为纯可视化
    QAudioOutput = None  # type: ignore[assignment]
    QMediaPlayer = None  # type: ignore[assignment]
    _HAS_AUDIO = False

from ustplayer.core.log import logger


class AudioBackend(QObject):
    """伴奏音频后端基类 — 播放器依赖的唯一音频接口。

    子类实现加载/播放/查询；就绪、结束、错误经信号上报。
    播放器收到 media_error 或看门狗判定异常后应降级为纯可视化计时。
    """

    # 媒体加载完成（可以开始播放）
    media_ready = Signal()
    # 播放到结尾
    media_ended = Signal()
    # 出错（携带可读错误描述）
    media_error = Signal(str)

    def load(self, music_path: str) -> None:
        """开始加载伴奏；之后异步经 media_ready / media_ended / media_error 汇报。"""
        raise NotImplementedError

    def play(self) -> None:
        """开始播放（仅当已就绪时有效）。"""
        raise NotImplementedError

    def stop(self) -> None:
        """停止播放并释放资源。"""
        raise NotImplementedError

    def position_seconds(self) -> float:
        """当前播放位置（秒）；未播放返回 0.0。"""
        return 0.0

    def duration_seconds(self) -> float:
        """媒体总时长（秒）；未知返回 0.0。"""
        return 0.0

    def is_playing(self) -> bool:
        """是否正处于播放状态。"""
        return False

    def is_loaded(self) -> bool:
        """媒体已加载完成（LoadedMedia / BufferedMedia）。"""
        return False

    def is_loading(self) -> bool:
        """媒体仍在加载中（LoadingMedia / StalledMedia）。"""
        return False

    def is_invalid(self) -> bool:
        """媒体无效（InvalidMedia）。"""
        return False


class QtAudioBackend(AudioBackend):
    """QtMultimedia 实现：包装 QMediaPlayer + QAudioOutput。"""

    def __init__(self, parent: Optional[QObject] = None):
        super().__init__(parent)
        self._player: Optional[QMediaPlayer] = None  # type: ignore[assignment]
        self._output: Optional[QAudioOutput] = None  # type: ignore[assignment]

    def load(self, music_path: str) -> None:
        if QAudioOutput is None or QMediaPlayer is None:
            logger.warning("QtMultimedia 不可用，忽略音频加载")
            return
        output = QAudioOutput(self)
        output.setVolume(1.0)
        player = QMediaPlayer(self)
        player.setAudioOutput(output)
        player.mediaStatusChanged.connect(self._on_media_status)
        player.errorOccurred.connect(self._on_error)
        player.setSource(QUrl.fromLocalFile(music_path))
        self._output = output
        self._player = player

    def _on_media_status(self, status) -> None:
        """媒体状态变化：就绪后发 media_ready；播完发 media_ended。"""
        try:
            if QMediaPlayer is None:
                return
            if status == QMediaPlayer.MediaStatus.LoadedMedia:  # pyright: ignore[reportOptionalMemberAccess]
                self.media_ready.emit()
            elif status == QMediaPlayer.MediaStatus.EndOfMedia:  # pyright: ignore[reportOptionalMemberAccess]
                self.media_ended.emit()
        except Exception:
            logger.exception("媒体状态处理异常")

    def _on_error(self, error, error_string) -> None:
        self.media_error.emit(error_string or f"错误码 {error}")

    def play(self) -> None:
        if self._player is not None:
            self._player.play()

    def stop(self) -> None:
        if self._player is not None:
            self._player.stop()

    def position_seconds(self) -> float:
        if self._player is None:
            return 0.0
        return self._player.position() / 1000.0

    def duration_seconds(self) -> float:
        if self._player is None:
            return 0.0
        ms = self._player.duration()
        return ms / 1000.0 if ms > 0 else 0.0

    def is_playing(self) -> bool:
        if self._player is None or QMediaPlayer is None:
            return False
        return self._player.playbackState() == QMediaPlayer.PlaybackState.PlayingState  # pyright: ignore[reportOptionalMemberAccess]

    def is_loaded(self) -> bool:
        if self._player is None or QMediaPlayer is None:
            return False
        mp = QMediaPlayer
        return self._player.mediaStatus() in (  # pyright: ignore[reportOptionalMemberAccess]
            mp.MediaStatus.LoadedMedia,
            mp.MediaStatus.BufferedMedia,
        )

    def is_loading(self) -> bool:
        if self._player is None or QMediaPlayer is None:
            return False
        mp = QMediaPlayer
        return self._player.mediaStatus() in (  # pyright: ignore[reportOptionalMemberAccess]
            mp.MediaStatus.LoadingMedia,
            mp.MediaStatus.StalledMedia,
        )

    def is_invalid(self) -> bool:
        if self._player is None or QMediaPlayer is None:
            return False
        return self._player.mediaStatus() == QMediaPlayer.MediaStatus.InvalidMedia  # pyright: ignore[reportOptionalMemberAccess]


def create_audio_backend(parent: Optional[QObject] = None) -> Optional[AudioBackend]:
    """按环境创建音频后端；QtMultimedia 不可用时返回 None（播放器走纯可视化）。

    返回实例已以 parent 为父对象，生命周期由 Qt 父子关系管理。
    """
    if not _HAS_AUDIO or QAudioOutput is None or QMediaPlayer is None:
        return None
    return QtAudioBackend(parent)
