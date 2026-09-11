# test_audio_backend.py — 音频后端封装与播放器音频状态机（Layer 2，需 offscreen qapp）
"""覆盖：

- 播放器经 create_audio_backend 注入 fake 后端后的音频状态机：
  媒体就绪 → 只 play() 一次；播完 → 记录时长锚点；出错 / 看门狗 → 降级且时间轴不跳变。
- 无伴奏路径时保持纯可视化。

此前音频逻辑直接依赖 QtMultimedia，无音频设备环境完全不可测；
封装到 audio_backend.py 后可用可编程 fake 后端驱动全部分支。
"""

import time

import pytest

import ustplayer.core.player as player_mod
from ustplayer.core.audio_backend import AudioBackend
from ustplayer.core.contracts import PlayerLaunchParams
from ustplayer.core.player import NoteLyricDisplay


class FakeAudioBackend(AudioBackend):
    """可编程的假后端：测试手动驱动状态字段与信号。"""

    def __init__(self):
        super().__init__()
        self.loaded_path = None
        self.play_calls = 0
        self.stop_calls = 0
        self._pos = 0.0
        self._dur = 0.0
        self._playing = False
        self._loaded = False
        self._loading = False
        self._invalid = False
        self._finished = False

    def load(self, music_path: str):
        self.loaded_path = music_path
        self._loading = True

    def play(self):
        self.play_calls += 1
        self._playing = True

    def stop(self):
        self.stop_calls += 1
        self._playing = False

    def position_seconds(self):
        return self._pos

    def duration_seconds(self):
        return self._dur

    def is_playing(self):
        return self._playing

    def is_loaded(self):
        return self._loaded

    def is_loading(self):
        return self._loading

    def is_invalid(self):
        return self._invalid

    def is_finished(self):
        return self._finished

    # ---- 测试辅助：模拟 Qt 状态流转 ----
    def emit_ready(self):
        self._loading = False
        self._loaded = True
        self.media_ready.emit()

    def emit_ended(self):
        self.media_ended.emit()

    def emit_error(self, message: str = "模拟错误"):
        self.media_error.emit(message)


@pytest.fixture
def fake_backend(monkeypatch, qapp, tmp_path):
    """注入 fake 后端并创建播放器；music_path 指向一个存在的假文件。"""
    fake = FakeAudioBackend()
    music = tmp_path / "music.wav"
    music.write_bytes(b"WAV")
    monkeypatch.setattr(player_mod, "create_audio_backend", lambda parent=None: fake)
    params = PlayerLaunchParams()
    params.style.music_path = str(music)
    display = NoteLyricDisplay(params)
    return fake, display


class TestPlayerAudioStateMachine:
    def test_ready_triggers_single_play(self, fake_backend):
        fake, d = fake_backend
        assert d._audio is fake
        assert d._audio_ok is True
        assert fake.loaded_path.endswith("music.wav")
        assert fake.play_calls == 0

        fake.emit_ready()
        assert fake.play_calls == 1
        assert d._play_issued is True

        # 再次就绪不重播（播完后不再自动 play 的守卫）
        fake.emit_ready()
        assert fake.play_calls == 1

    def test_ready_after_ended_does_not_replay(self, fake_backend):
        fake, d = fake_backend
        fake.emit_ended()
        fake.emit_ready()
        assert fake.play_calls == 0

    def test_ended_records_duration_anchor(self, fake_backend):
        fake, d = fake_backend
        fake._dur = 90.0
        fake._pos = 90.0
        fake.emit_ended()
        assert d._media_finished is True
        assert d._media_duration_s == 90.0
        assert d._media_finish_real > 0

    def test_ended_falls_back_to_position_when_duration_unknown(self, fake_backend):
        fake, d = fake_backend
        fake._dur = 0.0
        fake._pos = 42.0
        fake.emit_ended()
        assert d._media_duration_s == 42.0

    def test_error_degrades_and_keeps_timeline_continuous(self, fake_backend):
        """音频播到 30 秒出错降级：时间轴应从 30 秒附近继续，而非跳回 0（回归测试）。"""
        fake, d = fake_backend
        d.total_tick = 10**9  # 避免 _tick 直接进入结束画面
        d._play_elapsed = 30.0
        fake._pos = 30.0
        fake.emit_error("模拟错误")
        assert d._audio_ok is False
        assert d._audio_degraded_real > 0

        d._tick()
        assert abs(d._play_elapsed - 30.0) < 1.0

    def test_watchdog_degrades_when_loaded_but_not_playing(self, fake_backend):
        fake, d = fake_backend
        fake._loaded = True
        fake._playing = False
        d._check_audio_ready()
        assert d._audio_ok is False

    def test_watchdog_degrades_immediately_on_invalid(self, fake_backend):
        fake, d = fake_backend
        fake._invalid = True
        d._check_audio_ready()
        assert d._audio_ok is False

    def test_watchdog_retries_then_degrades_while_loading(self, fake_backend, monkeypatch):
        fake, d = fake_backend
        fake._loading = True
        scheduled = []
        monkeypatch.setattr(player_mod.QTimer, "singleShot", lambda ms, cb: scheduled.append(cb))

        d._check_audio_ready()
        assert d._audio_ok is True
        assert len(scheduled) == 1

        # 第 2、3 次仍加载中 → 第 3 次超限强制降级
        d._check_audio_ready()
        d._check_audio_ready()
        assert d._audio_ok is False

    def test_watchdog_not_degrade_after_ended_signal(self, fake_backend):
        """EndOfMedia 信号已发（_media_finished=True）后，Qt FFmpeg 后端把
        mediaStatus 回落为 LoadedMedia 属正常现象——看门狗不得降级（回归测试）。"""
        fake, d = fake_backend
        fake.emit_ended()
        assert d._media_finished is True
        # 模拟播完后 status 回落为 LoadedMedia + Stopped
        fake._loaded = True
        fake._playing = False
        d._check_audio_ready()
        assert d._audio_ok is True

    def test_watchdog_treats_finished_status_as_ended(self, fake_backend):
        """后端停留在 EndOfMedia 状态但信号未发出：看门狗补记播完，不降级。

        注意 EndOfMedia 与 LoadedMedia/BufferedMedia 是互斥状态（mediaStatus 单枚举），
        因此模拟时 is_loaded() 必须为 False——否则走不进真实 Qt 后端会经过的分支。
        """
        fake, d = fake_backend
        fake._loaded = False  # EndOfMedia 状态下 is_loaded() 为 False
        fake._playing = False
        fake._finished = True
        d._check_audio_ready()
        assert d._audio_ok is True
        assert d._media_finished is True

    def test_no_music_path_keeps_visual_only(self, qapp):
        d = NoteLyricDisplay(PlayerLaunchParams())
        assert d._audio is None
        assert d._audio_ok is False

    def test_watchdog_degrades_when_stuck_in_nomedia(self, fake_backend, monkeypatch):
        """后端缺失（已 setSource 但 mediaStatus 停在 NoMedia）：不能永久卡死，
        3 次未就绪后强制降级为纯可视化（回归测试：打包缺媒体后端资源的场景）。"""
        fake, d = fake_backend
        scheduled = []
        monkeypatch.setattr(player_mod.QTimer, "singleShot", lambda ms, cb: scheduled.append(cb))

        d._check_audio_ready()
        assert d._audio_ok is True
        assert len(scheduled) == 1  # 首次：登记 3 秒后复查

        d._check_audio_ready()
        d._check_audio_ready()
        assert d._audio_ok is False  # 第 3 次仍未就绪 → 强制降级


# ===================== 回归：同步信号 / 重复结束 / 看门狗异常 =====================

class SyncSignalBackend(AudioBackend):
    """在 load() 内同步发信号的后端，验证初始化顺序。"""

    def __init__(self, ready=False, error=False):
        super().__init__()
        self._ready = ready
        self._error = error
        self.play_calls = 0
        self.stop_calls = 0

    def load(self, music_path: str):
        if self._error:
            self.media_error.emit("同步错误")
        if self._ready:
            self.media_ready.emit()

    def play(self):
        self.play_calls += 1

    def stop(self):
        self.stop_calls += 1


def test_sync_media_ready_during_load_starts_playback(qapp, tmp_path, monkeypatch):
    music = tmp_path / "music.wav"
    music.write_bytes(b"WAV")
    backend = SyncSignalBackend(ready=True)
    monkeypatch.setattr(player_mod, "create_audio_backend", lambda parent=None: backend)
    params = PlayerLaunchParams()
    params.style.music_path = str(music)
    display = NoteLyricDisplay(params)
    assert display._play_issued is True
    assert backend.play_calls == 1


def test_sync_media_error_during_load_not_overridden(qapp, tmp_path, monkeypatch):
    music = tmp_path / "music.wav"
    music.write_bytes(b"WAV")
    backend = SyncSignalBackend(error=True)
    monkeypatch.setattr(player_mod, "create_audio_backend", lambda parent=None: backend)
    params = PlayerLaunchParams()
    params.style.music_path = str(music)
    display = NoteLyricDisplay(params)
    assert display._audio_ok is False
    assert display._audio_degraded_real > 0


def test_repeated_media_ended_keeps_first_anchor(fake_backend):
    fake, d = fake_backend
    fake._dur = 10.0
    fake._pos = 10.0
    fake.emit_ended()
    first_anchor = d._media_finish_real
    import time as _time

    _time.sleep(0.01)
    fake.emit_ended()
    assert d._media_finish_real == first_anchor


def test_watchdog_query_exception_degrades(fake_backend, monkeypatch):
    fake, d = fake_backend

    def _boom():
        raise RuntimeError("后端查询异常")

    fake.is_loaded = _boom
    scheduled = []
    monkeypatch.setattr(player_mod.QTimer, "singleShot", lambda ms, cb: scheduled.append(cb))
    d._check_audio_ready()
    assert d._audio_ok is False
    assert scheduled == []
