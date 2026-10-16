"""图标在不同背景下的可读性检查。

图标本身是透明底白色卡片，在「白色界面」上会不会糊掉、
在「深色界面」上会不会太亮，需要放在真实底色的板子上看。
顺便对比一个「带圆角方块底」的变体，决定要不要换。
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent / "icon"

BG_CASES = [
    ("深色任务栏", (32, 36, 44, 255)),
    ("浅灰工具栏", (243, 244, 246, 255)),
    ("纯白界面", (255, 255, 255, 255)),
    ("品牌蓝底", (47, 111, 235, 255)),
]

SIZES = [16, 24, 32, 48]


def with_tile(master: Image.Image, radius_ratio: float = 0.22) -> Image.Image:
    """给图标加一个圆角方块底（渐变），让小尺寸在任意背景上都立得住。"""
    size = master.size[0]
    tile = Image.new("RGBA", master.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(tile)

    # 竖向渐变：上浅下深，避免大面积纯色显得平
    top = (86, 132, 240)
    bottom = (36, 92, 214)
    for y in range(size):
        t = y / max(1, size - 1)
        d.line(
            [(0, y), (size, y)],
            fill=(
                int(top[0] + (bottom[0] - top[0]) * t),
                int(top[1] + (bottom[1] - top[1]) * t),
                int(top[2] + (bottom[2] - top[2]) * t),
                255,
            ),
        )

    # 圆角遮罩
    mask = Image.new("L", master.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=int(size * radius_ratio), fill=255
    )
    tile.putalpha(mask)

    # 把白卡缩到 72% 居中盖上去
    inner = master.resize((int(size * 0.72), int(size * 0.72)), Image.LANCZOS)
    tile.alpha_composite(inner, ((size - inner.size[0]) // 2, (size - inner.size[1]) // 2))
    return tile


def main() -> None:
    plain = Image.open(HERE / "icon-256.png").convert("RGBA")
    tiled = with_tile(plain)

    col_w = 150
    row_h = 92
    variants = [("透明底（当前）", plain), ("圆角底（备选）", tiled)]

    sheet = Image.new(
        "RGBA", (110 + col_w * len(SIZES), row_h * len(BG_CASES) * len(variants) + 30),
        (250, 250, 252, 255),
    )
    d = ImageDraw.Draw(sheet)

    y = 8
    for vname, master in variants:
        d.text((8, y + 30), vname, fill=(30, 34, 42))
        for i, s in enumerate(SIZES):
            d.text((110 + i * col_w + 30, y), f"{s}px", fill=(120, 126, 138))
        y += 22

        for bg_name, bg in BG_CASES:
            d.text((8, y + 20), bg_name, fill=(90, 96, 108))
            for i, s in enumerate(SIZES):
                cell = Image.new("RGBA", (col_w - 20, 64), bg)
                thumb = master.resize((s, s), Image.LANCZOS)
                cell.alpha_composite(
                    thumb, ((col_w - 20 - s) // 2, (64 - s) // 2)
                )
                sheet.alpha_composite(cell, (110 + i * col_w, y))
            y += 64
        y += 16

    target = HERE / "_preview-backgrounds.png"
    sheet.save(target)
    print(target)


if __name__ == "__main__":
    main()
