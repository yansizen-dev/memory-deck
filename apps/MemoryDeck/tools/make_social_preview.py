"""
生成分享用的社交预览图（GitHub social preview）。

尺寸 1280x640 —— GitHub 建议的比例，也是各社交平台裁切时最安全的横版。
内容刻意克制：图标 + 名字 + 一句话，不放功能列表。
缩略图尺寸下放越多字越没人看。
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent / "icon"
OUT = Path(__file__).resolve().parent.parent / "docs" / "social-preview.png"

W, H = 1280, 640

BG_TOP = (34, 92, 214)
BG_BOTTOM = (18, 48, 122)

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\msyhbd.ttc",   # 微软雅黑 粗
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\simhei.ttf",
    r"C:\Windows\Fonts\segoeui.ttf",
]


def load_font(size: int):
    for path in FONT_CANDIDATES:
        try:
            return ImageFont.truetype(path, size)
        except Exception:
            continue
    return ImageFont.load_default()


def main() -> None:
    img = Image.new("RGB", (W, H), BG_BOTTOM)
    d = ImageDraw.Draw(img)

    # 竖向渐变底
    for y in range(H):
        t = y / (H - 1)
        d.line(
            [(0, y), (W, y)],
            fill=(
                int(BG_TOP[0] + (BG_BOTTOM[0] - BG_TOP[0]) * t),
                int(BG_TOP[1] + (BG_BOTTOM[1] - BG_TOP[1]) * t),
                int(BG_TOP[2] + (BG_BOTTOM[2] - BG_TOP[2]) * t),
            ),
        )

    icon = Image.open(HERE / "icon-256.png").convert("RGBA")

    icon_size = 200
    icon_big = icon.resize((icon_size, icon_size), Image.LANCZOS)
    icon_x = 110
    icon_y = 150
    img.paste(icon_big, (icon_x, icon_y), icon_big)

    text_x = icon_x + icon_size + 64

    title_font = load_font(84)
    tag_font = load_font(32)
    sub_font = load_font(26)

    d.text((text_x, 148), "MemoryDeck", font=title_font, fill=(255, 255, 255))
    d.text((text_x, 258), "记忆卡片管理器", font=tag_font, fill=(178, 208, 255))

    # 一句话定位：只讲「是什么」，不讲功能列表
    d.text((text_x, 320), "以 Markdown 为数据源，", font=sub_font, fill=(214, 228, 255))
    d.text((text_x, 358), "把长期记忆变成 AI 每一轮都能读到的上下文", font=sub_font, fill=(214, 228, 255))

    # 底部装饰条与文字块左对齐，宽度递减排出「一堆卡片」的暗示
    bar_y = 452
    for i, (w, level) in enumerate([(188, 255), (140, 190), (92, 140)]):
        tint = int(120 + 100 * (level / 255))
        d.rounded_rectangle(
            [text_x, bar_y + i * 22, text_x + w, bar_y + i * 22 + 10],
            radius=5,
            fill=(tint // 2 + 90, tint // 2 + 130, 255),
        )

    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT)
    print(OUT)
    print(f"{W}x{H}")


if __name__ == "__main__":
    main()
