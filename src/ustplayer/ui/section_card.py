# section_card.py — 卡片式分区组件 + 可滚动页面基类
"""1. SectionCard：带「主题色竖线 + 分区名」标题的卡片分区，替代 "/ XXX" 纯文本标题。
   2. ScrollPage：可滚动页面基类——内容超高时窗口大小不变，滚动查看；
      背景透明，露出窗口的 Mica/亚克力模糊效果。
"""

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QPainter
from PySide6.QtWidgets import (
    QFrame, QGraphicsDropShadowEffect, QHBoxLayout,
    QVBoxLayout, QWidget,
)

from qfluentwidgets import HeaderCardWidget, ScrollArea, themeColor
from qfluentwidgets.common.style_sheet import isDarkTheme


class AccentBar(QWidget):
    """主题色竖线（绘制时实时取当前主题色）。"""

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setFixedSize(4, 18)

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.RenderHint.Antialiasing)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(themeColor())
        painter.drawRoundedRect(self.rect(), 2, 2)


class SectionCard(HeaderCardWidget):
    """卡片式分区：主题色竖线 + 分区名 + 内容区。

    卡片用纯色实底（亮色=白、暗色=半透明白）+ 柔和投影，保证在窗口背景上清晰可辨。
    """

    # HeaderCardWidget.__init__ 是 singledispatchmethod，这里改为统一 (title, parent) 签名
    def __init__(self, title: str, parent: QWidget | None = None):  # type: ignore[reportIncompatibleVariableOverride]
        super().__init__(parent)
        self.setTitle(title)

        # 标题行最前面插入主题色竖线 + 间距
        self._bar = AccentBar(self.headerView)
        self.headerLayout.insertWidget(0, self._bar)
        self.headerLayout.insertSpacing(1, 10)

        # 内容区：在 HeaderCardWidget 的横向 viewLayout 内放一个纵向布局（收紧留白）
        self.content_layout = QVBoxLayout()
        self.content_layout.setContentsMargins(0, 8, 0, 8)
        self.content_layout.setSpacing(8)
        self.viewLayout.addLayout(self.content_layout)
        self.viewLayout.setContentsMargins(20, 16, 20, 16)

        # 柔和投影，让卡片从背景中"浮"出来
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(28)
        shadow.setOffset(0, 6)
        shadow.setColor(QColor(0, 0, 0, 45))
        self.setGraphicsEffect(shadow)

    # ---- 卡片底色：纯色实底，明显区分于窗口背景 ----

    def _normalBackgroundColor(self):
        return QColor(255, 255, 255, 255) if not isDarkTheme() else QColor(255, 255, 255, 16)

    def _hoverBackgroundColor(self):
        return QColor(245, 245, 245, 255) if not isDarkTheme() else QColor(255, 255, 255, 22)

    def addWidget(self, widget: QWidget, stretch: int = 0):
        self.content_layout.addWidget(widget, stretch)

    def addLayout(self, layout: QHBoxLayout | QVBoxLayout, stretch: int = 0):
        self.content_layout.addLayout(layout, stretch)


class ScrollPage(QWidget):
    """可滚动页面基类：内容超高时窗口大小不变，滚动查看；背景透明露出窗口模糊效果。"""

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)

        # 用 qfluentwidgets.ScrollArea：自带 Fluent 风格滚动条 + 平滑滚动
        self._scroll = ScrollArea(self)
        self._scroll.setWidgetResizable(True)
        self._scroll.setFrameShape(QFrame.Shape.NoFrame)
        self._content = QWidget()
        self._content.setStyleSheet("background: transparent;")
        self._scroll.setWidget(self._content)
        # 视口与内容背景透明，让窗口的 Mica/亚克力效果透出来
        self._scroll.enableTransparentBackground()

        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)
        outer.addWidget(self._scroll)

        # 页面内容布局（各页面在此添加控件）
        self.page_layout = QVBoxLayout(self._content)
        self.page_layout.setContentsMargins(24, 20, 24, 20)
        self.page_layout.setSpacing(12)
