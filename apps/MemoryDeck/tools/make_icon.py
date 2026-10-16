"""
生成 MemoryDeck 的图标（定稿版）。

设计方案：一叠记忆卡片（Deck）

为什么是这个方案（对比过 4 个变体后的结论）：
- 单卡 + 折角：和「文档 / 笔记」类图标撞脸，没有辨识度。
- 牌组 + 折角：折角在 16px 下糊成噪点，是负担而非信息。
- 牌组不带折角：**轮廓在 16px 下最抓眼**，这是选它的唯一理由。
- 折角画在背面卡上：那个尺寸下几乎看不见，白做。

细节决定：
- 背面卡在「上 / 右 / 下」三个方向均匀露出，读起来像「一叠」；
  只往右下偏移会读成「投影」，那是另一种语义。
- 保留前卡左上角不动，让视觉重心落在左上方（符合阅读起点）。
- 内容条用界面同款蓝，第一行实心、后两行浅色，做出层次但不啰嗦。
- 配色沿用程序界面的灰蓝体系，不引入新色。

为什么用脚本生成而不是手绘：
- 需要在 16 / 24 / 32 / 48 / 64 / 128 / 256 七个尺寸下逐一核对
- 几何一致性靠代码保证，改一个参数就能全套重出
- 同时产出 SVG 源，以后要改直接改参数

输出：
  icon/icon.svg          矢量源（可编辑）
  icon/icon-{n}.png      各尺寸 PNG
  icon/icon.png          256px，给 README 用
  icon/icon.ico          多尺寸 ICO，嵌进 exe
  icon/_preview-sizes.png  各尺寸横向拼版，用于自检
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

# ---------------------------------------------------------------- 画布

# 先在 1024 画布上按 256 网格的比例绘制，再降采样。
# 直接在小画布上画，圆角和描边会糊。
MASTER = 1024
S = MASTER / 256.0

def px(v: float) -> float:
    return v * S


# ---------------------------------------------------------------- 配色

BACK_FILL = (203, 211, 223, 255)
BACK_EDGE = (166, 176, 192, 255)
FRONT_FILL = (255, 255, 255, 255)
FRONT_EDGE = (86, 98, 116, 255)
ACCENT = (47, 111, 235, 255)
ACCENT_SOFT = (137, 174, 245, 255)

# 圆角底的渐变端点：上浅下深，避免大面积纯色显平
TILE_TOP = (86, 132, 240)
TILE_BOTTOM = (36, 92, 214)

# ---------------------------------------------------------------- 几何

CARD = (26, 20, 204, 230)      # 前卡
BAND = 11                      # 背面卡露出的宽度（上/右/下三边）
BACK = (CARD[0], CARD[1] - BAND, CARD[2] + BAND, CARD[3] + BAND)

FRONT_RADIUS = 22
BACK_RADIUS = 24

FRONT_EDGE_W = 3.4
BACK_EDGE_W = 2.8

BARS = [
    (54, 60, 120, 18, ACCENT),
    (54, 106, 104, 18, ACCENT_SOFT),
    (54, 152, 78, 18, ACCENT_SOFT),
]


def draw_rounded(draw: ImageDraw.ImageDraw, box, radius, fill, edge, width):
    draw.rounded_rectangle(
        [px(box[0]), px(box[1]), px(box[2]), px(box[3])],
        radius=px(radius),
        fill=fill,
        outline=edge,
        width=max(1, int(round(px(width)))),
    )


def build_master(with_tile: bool) -> Image.Image:
    """
    with_tile=False：透明底，只有白卡（干净，但小尺寸在白色界面上会消失）
    with_tile=True ：加圆角蓝底（在深色任务栏 / 白色界面 / 品牌底色上都立得住）
    """
    card = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    d = ImageDraw.Draw(card)

    # ① 背面那张卡（先画，露出的三边形成「一叠」）
    draw_rounded(d, BACK, BACK_RADIUS, BACK_FILL, BACK_EDGE, BACK_EDGE_W)

    # ② 前面那张卡
    draw_rounded(d, CARD, FRONT_RADIUS, FRONT_FILL, FRONT_EDGE, FRONT_EDGE_W)

    # ③ 内容条
    for x, y, w, h, color in BARS:
        d.rounded_rectangle(
            [px(x), px(y), px(x + w), px(y + h)],
            radius=px(h / 2),
            fill=color,
        )

    if not with_tile:
        return card

    return apply_tile(card)


def apply_tile(card: Image.Image) -> Image.Image:
    """给卡片铺一层带渐变的圆角底，让它脱离背景也能看清。"""
    tile = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    d = ImageDraw.Draw(tile)

    for y in range(MASTER):
        t = y / (MASTER - 1)
        d.line(
            [(0, y), (MASTER, y)],
            fill=(
                int(TILE_TOP[0] + (TILE_BOTTOM[0] - TILE_TOP[0]) * t),
                int(TILE_TOP[1] + (TILE_BOTTOM[1] - TILE_TOP[1]) * t),
                int(TILE_TOP[2] + (TILE_BOTTOM[2] - TILE_TOP[2]) * t),
                255,
            ),
        )

    mask = Image.new("L", (MASTER, MASTER), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, MASTER - 1, MASTER - 1], radius=int(MASTER * 0.225), fill=255
    )
    tile.putalpha(mask)

    # 卡片按「较长边」缩放到占满画布的 78%，再居中。
    # 占比太小的话，16px 下白卡会缩成一小块，反而认不出是什么。
    target = MASTER * 0.78
    ratio = target / max(card.size)
    inner_size = (max(1, int(card.size[0] * ratio)), max(1, int(card.size[1] * ratio)))
    inner = card.resize(inner_size, Image.LANCZOS)
    tile.alpha_composite(inner, ((MASTER - inner.size[0]) // 2, (MASTER - inner.size[1]) // 2))
    return tile


def build_svg() -> str:
    """同一份几何的矢量版本（带圆角底，与主图标一致）。"""
    def hexa(c) -> str:
        return "#%02X%02X%02X" % c[:3]

    def rect(box, radius, fill, stroke, w) -> str:
        x1, y1, x2, y2 = box
        return (
            f'  <rect x="{x1}" y="{y1}" width="{x2 - x1}" height="{y2 - y1}" '
            f'rx="{radius}" fill="{fill}" stroke="{stroke}" stroke-width="{w}" />'
        )

    # 把卡片几何缩到 70% 并居中，配合圆角底
    scale = 0.70
    offset = 256 * (1 - scale) / 2

    def shrink(box):
        return tuple(offset + v * scale for v in box)

    lines = [
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" '
        'width="256" height="256" role="img" aria-label="MemoryDeck">',
        '  <title>MemoryDeck</title>',
        '  <defs>',
        '    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">',
        f'      <stop offset="0" stop-color="{hexa(TILE_TOP)}" />',
        f'      <stop offset="1" stop-color="{hexa(TILE_BOTTOM)}" />',
        '    </linearGradient>',
        '  </defs>',
        '  <rect width="256" height="256" rx="58" fill="url(#tile)" />',
        '  <g>',
    ]

    back = shrink(BACK)
    front = shrink(CARD)
    lines.append(
        f'    <rect x="{back[0]:.1f}" y="{back[1]:.1f}" width="{back[2] - back[0]:.1f}" '
        f'height="{back[3] - back[1]:.1f}" rx="{BACK_RADIUS * scale:.1f}" '
        f'fill="{hexa(BACK_FILL)}" stroke="{hexa(BACK_EDGE)}" stroke-width="{BACK_EDGE_W * scale:.2f}" />'
    )
    lines.append(
        f'    <rect x="{front[0]:.1f}" y="{front[1]:.1f}" width="{front[2] - front[0]:.1f}" '
        f'height="{front[3] - front[1]:.1f}" rx="{FRONT_RADIUS * scale:.1f}" '
        f'fill="{hexa(FRONT_FILL)}" stroke="{hexa(FRONT_EDGE)}" stroke-width="{FRONT_EDGE_W * scale:.2f}" />'
    )

    for x, y, w, h, color in BARS:
        lines.append(
            f'    <rect x="{offset + x * scale:.1f}" y="{offset + y * scale:.1f}" '
            f'width="{w * scale:.1f}" height="{h * scale:.1f}" '
            f'rx="{h * scale / 2:.1f}" fill="{hexa(color)}" />'
        )

    lines.append("  </g>")
    lines.append("</svg>")
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------- 输出

def main() -> None:
    out_dir = Path(__file__).resolve().parent / "icon"
    out_dir.mkdir(parents=True, exist_ok=True)

    master = build_master(with_tile=True)
    plain = build_master(with_tile=False)

    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = []

    for size in sizes:
        thumb = master.resize((size, size), Image.LANCZOS)
        thumb.save(out_dir / f"icon-{size}.png")
        images.append(thumb)

    master.resize((256, 256), Image.LANCZOS).save(out_dir / "icon.png")

    # 透明底备选：网页 / 证件照那种需要叠在自有底色上的场合
    plain.resize((256, 256), Image.LANCZOS).save(out_dir / "icon-transparent.png")

    # ICO 里塞多尺寸，Windows 会按场合挑最合适的一档
    master.resize((256, 256), Image.LANCZOS).save(
        out_dir / "icon.ico",
        format="ICO",
        sizes=[(s, s) for s in sizes],
        append_images=images,
    )

    (out_dir / "icon.svg").write_text(build_svg(), encoding="utf-8")

    # 各尺寸拼版 + 小尺寸放大预览，用于人工核对
    gap = 14
    sheet_w = 20 + sum(s + gap for s in sizes)
    sheet = Image.new("RGBA", (sheet_w, 300), (244, 245, 248, 255))
    x = 20
    for size, thumb in zip(sizes, images):
        sheet.alpha_composite(thumb, (x, 20))
        x += size + gap

    # 16 和 32 放大 6 倍，方便看小尺寸是否糊
    x = 20
    for size, thumb in zip(sizes[:2], images[:2]):
        big = thumb.resize((size * 6, size * 6), Image.NEAREST)
        sheet.alpha_composite(big, (x, 300 - size * 6 - 20))
        x += size * 6 + gap

    sheet.save(out_dir / "_preview-sizes.png")

    print("已生成：")
    for name in ["icon.ico", "icon.svg", "icon.png", "icon-transparent.png", "_preview-sizes.png"]:
        print("  " + name)
    for size in sizes:
        print(f"  icon-{size}.png")
    print(f"\n目录：{out_dir}")


if __name__ == "__main__":
    main()
