# renderer_ffi.py — uPlRender 渲染器 DLL 的 ctypes 封装
"""加载 `ustplayer_renderer.dll`，通过 C ABI 驱动视频渲染。

对应 uPlRender 仓库的 `bindings/ustplayer_renderer.h`：所有 `up_*` 函数均以
UTF-8 交换字符串、以 `u64` 句柄标识上下文，且同一上下文的所有调用必须串行
（不同上下文可并发）。错误通过返回值（负数错误码）+ `up_last_error` 上报。
"""

import ctypes
import os
from ctypes import (
    CFUNCTYPE,
    c_char_p,
    c_double,
    c_int32,
    c_uint8,
    c_uint64,
    POINTER,
    byref,
)

from ustplayer.core.log import logger

# ---------- 错误码 ----------
UP_OK = 0
UP_ERR_INVALID_ARG = -1
UP_ERR_PARSE = -2
UP_ERR_IO = -3
UP_ERR_FONT = -4
UP_ERR_RENDER = -5
UP_ERR_ENCODE = -6
UP_ERR_INTERNAL = -99

# 进度回调类型：extern "C" fn(i32)
_ProgressFn = CFUNCTYPE(None, c_int32)


class RendererError(RuntimeError):
    """渲染器调用失败（携带错误码与可读消息）。"""

    def __init__(self, code: int, message: str):
        super().__init__(message)
        self.code = code


class RendererLibrary:
    """持有一个 Dll 句柄，绑定各函数签名；本模块内首个加载成功后复用单例。"""

    def __init__(self, dll_path: str):
        self._lib = ctypes.CDLL(dll_path)
        self._bind()

    @staticmethod
    def _bind_ret(lib, name, restype, argtypes):
        fn = getattr(lib, name)
        fn.restype = restype
        fn.argtypes = argtypes
        return fn

    def _bind(self):
        lib = self._lib
        self.create_context = self._bind_ret(lib, "up_create_context", c_uint64, [])
        self.destroy_context = self._bind_ret(lib, "up_destroy_context", None, [c_uint64])
        self.set_config = self._bind_ret(lib, "up_set_config", c_int32, [c_uint64, c_char_p])
        self.set_ust_text = self._bind_ret(lib, "up_set_ust_text", c_int32, [c_uint64, c_char_p])
        self.set_lrc_text = self._bind_ret(lib, "up_set_lrc_text", c_int32, [c_uint64, c_char_p])
        self.begin_export = self._bind_ret(lib, "up_begin_export", c_int32, [c_uint64])
        self.render_frame = self._bind_ret(lib, "up_render_frame", c_int32, [c_uint64, c_double])
        self.end_export = self._bind_ret(lib, "up_end_export", c_int32, [c_uint64])
        self.set_progress_callback = self._bind_ret(
            lib, "up_set_progress_callback", None, [c_uint64, _ProgressFn]
        )
        self.last_error = self._bind_ret(lib, "up_last_error", c_char_p, [c_uint64])
        self.render_to_buffer = self._bind_ret(
            lib,
            "up_render_to_buffer",
            c_int32,
            [c_uint64, c_double, POINTER(c_uint8), c_int32, POINTER(c_int32), POINTER(c_int32)],
        )

    @staticmethod
    def _err_message(lib, ctx: int) -> str:
        try:
            ptr = lib.last_error(ctx)
            if not ptr:
                return ""
            return ptr.decode("utf-8", errors="replace")
        except Exception:
            return ""


class RendererContext:
    """一个渲染上下文句柄的生命周期管理（with 语句自动销毁）。"""

    def __init__(self, lib: RendererLibrary):
        self._lib = lib
        self._ctx = int(lib.create_context())
        if not self._ctx:
            raise RendererError(UP_ERR_INTERNAL, "创建渲染上下文失败")
        self._progress_cb_ref = None  # 保持回调存活，避免被 GC

    @property
    def handle(self) -> int:
        return self._ctx

    def _raise_if_error(self, code: int, stage: str):
        if code != UP_OK:
            raise RendererError(code, f"{stage} 失败：{self._err()}")

    def _err(self) -> str:
        return RendererLibrary._err_message(self._lib._lib, self._ctx)

    # ---------- 配置 ----------
    def set_config(self, json_text: str):
        rc = self._lib.set_config(self._ctx, json_text.encode("utf-8"))
        self._raise_if_error(rc, "set_config")

    def set_ust_text(self, ust_json: str):
        rc = self._lib.set_ust_text(self._ctx, ust_json.encode("utf-8"))
        self._raise_if_error(rc, "set_ust_text")

    def set_lrc_text(self, lrc_text: str):
        # LRC 可能是 UTF-8/GBK，一律按 UTF-8 传入（渲染器内部另有编码探测）。
        rc = self._lib.set_lrc_text(self._ctx, lrc_text.encode("utf-8"))
        self._raise_if_error(rc, "set_lrc_text")

    def set_progress_callback(self, callback):
        """设置进度回调（千分比 0..1000）。返回回调引用以延长生命周期。"""
        if callback is None:
            return None
        cb = _ProgressFn(callback)
        self._progress_cb_ref = cb
        self._lib.set_progress_callback(self._ctx, cb)
        return cb

    # ---------- 导出 ----------
    def begin_export(self):
        rc = self._lib.begin_export(self._ctx)
        self._raise_if_error(rc, "begin_export")

    def render_frame(self, elapsed_sec: float):
        rc = self._lib.render_frame(self._ctx, c_double(elapsed_sec))
        self._raise_if_error(rc, "render_frame")

    def end_export(self):
        rc = self._lib.end_export(self._ctx)
        self._raise_if_error(rc, "end_export")

    # ---------- 单帧到缓冲（可选） ----------
    def render_to_buffer(self, elapsed_sec: float, width: int, height: int) -> bytes:
        buf_len = width * height * 4
        buf = (c_uint8 * buf_len)()
        out_w = c_int32(0)
        out_h = c_int32(0)
        rc = self._lib.render_to_buffer(
            self._ctx, c_double(elapsed_sec), buf, buf_len, byref(out_w), byref(out_h)
        )
        self._raise_if_error(rc, "render_to_buffer")
        return bytes(buf)

    def close(self):
        if self._ctx:
            self._lib.destroy_context(self._ctx)
            self._ctx = 0

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()
        return False


# ---------- 软件单例加载（按固定目录查找） ----------

_RENDERER_DIR_NAME = "renderer"


def dll_search_dirs(program_root: str):
    """返回渲染器 DLL 的候选路径（固定目录，按优先级排序）。"""
    candidates = [
        os.path.join(program_root, "ustplayer_renderer.dll"),
        os.path.join(program_root, _RENDERER_DIR_NAME, "ustplayer_renderer.dll"),
    ]
    # 兜底：exe 旁（打包结构可能与开发目录不同）
    return candidates


class RendererLoader:
    """按固定目录查找并加载渲染器 DLL。"""

    def __init__(self, program_root: str):
        self.program_root = program_root
        self._lib: "RendererLibrary | None" = None

    def load(self) -> RendererLibrary:
        if self._lib is not None:
            return self._lib
        last_err: str = ""
        for path in dll_search_dirs(self.program_root):
            if not os.path.isfile(path):
                continue
            try:
                lib = RendererLibrary(path)
                logger.info(f"已加载渲染器 DLL: {path}")
                self._lib = lib
                return lib
            except OSError as e:
                last_err = f"{path}: {e}"
                continue
        raise RuntimeError(
            f"未找到 ustplayer_renderer.dll（请在 {_RENDERER_DIR_NAME}\\ "
            f"目录或程序根目录放置）。最后错误: {last_err or '无候选文件'}"
        )
