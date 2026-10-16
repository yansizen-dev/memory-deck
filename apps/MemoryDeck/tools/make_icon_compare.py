"""图标方案对比：一次性渲染几个变体，拼成一张对比图，挑完再定稿。"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

MASTER = 1024
S = MASTER / 256.0

FRONT_EDGE = (86, 98, 116, 255)
ACCENT = (47, 111, 235, 255)
ACCENT_SOFT = (137, 174, 245, 255)


def px(v: float) -> float:
    return v * S


def draw_card(draw, box, radius, fill, edge, width):
    draw.rounded_rectangle(
        [px(box[0]), px(box[1]), px(box[2]), px(box[3])],
        radius=px(radius), fill=fill, outline=edge,
        width=max(1, int(round(px(width)))),
    )


def bars(draw, items):
    for x, y, w, h, c in items:
        draw.rounded_rectangle(
            [px(x), px(y), px(x + w), px(y + h)], radius=px(h / 2), fill=c)


# ---------------------------------------------------------------- 变体

def variant_single_fold():
    """① 单卡 + 折角（早期方案）"""
    img = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    box = (34, 26, 190, 214)
    draw_card(d, box, 20, (255, 255, 255, 255), FRONT_EDGE, 3.2)

    fx2, fy2 = box[2], box[3]
    fx1, fy1 = fx2 - 62, fy2 - 62
    d.polygon([(px(fx1), px(fy2)), (px(fx2), px(fy2)), (px(fx2), px(fy1))],
              fill=(226, 233, 245, 255), outline=FRONT_EDGE)
    bars(d, [(60, 62, 108, 16, ACCENT),
             (60, 100, 92, 16, ACCENT_SOFT),
             (60, 138, 70, 16, ACCENT_SOFT)])
    return img


def variant_deck_symmetric():
    """② 对称偏移的牌组（背面四周均匀露出）+ 折角"""
    img = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # 背面：四周各露出一圈，视觉上干净
    o = 9
    back = (28 + o, 22 + o, 206 - o + 26, 232 - o + 6)
    draw_card(d, back, 24, (203, 211, 223, 255), (168, 178, 194, 255), 3.0)

    front = (28, 22, 206, 232)
    draw_card(d, front, 20, (255, 255, 255, 255), FRONT_EDGE, 3.4)

    fx2, fy2 = front[2], front[3]
    fx1, fy1 = fx2 - 66, fy2 - 66
    d.polygon([(px(fx1), px(fy2)), (px(fx2), px(fy2)), (px(fx2), px(fy1))],
              fill=(232, 238, 248, 255), outline=FRONT_EDGE)
    bars(d, [(58, 66, 112, 17, ACCENT),
             (58, 108, 96, 17, ACCENT_SOFT),
             (58, 150, 74, 17, ACCENT_SOFT)])
    return img


def variant_deck_no_fold():
    """③ 牌组，不带折角 —— 看极简方向"""
    img = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    o = 10
    back = (26 + o, 20 + o, 204 - o + 28, 230 - o + 8)
    draw_card(d, back, 24, (203, 211, 223, 255), (168, 178, 194, 255), 3.0)

    front = (26, 20, 204, 230)
    draw_card(d, front, 22, (255, 255, 255, 255), FRONT_EDGE, 3.4)
    bars(d, [(54, 60, 120, 18, ACCENT),
             (54, 106, 104, 18, ACCENT_SOFT),
             (54, 152, 78, 18, ACCENT_SOFT)])
    return img


def variant_deck_corner_fold():
    """④ 牌组 + 折角放在「背面卡」上（折角不撞前卡轮廓）"""
    img = Image.new("RGBA", (MASTER, MASTER), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    o = 10
    back = (26 + o, 20 + o, 204 - o + 28, 230 - o + 8)

    # 背面先当纯色块画，再把右下角切成折角
    draw_card(d, back, 24, (203, 211, 223, 255), (168, 178, 194, 255), 3.0)
    bx2, by2 = back[2], back[3]
    bx1, by1 = bx2 - 58, by2 - 58
    d.polygon([(px(bx1), px(by2)), (px(bx2), px(by2)), (px(bx2), px(by1))],
              fill=(178, 188, 205, 255), outline=(150, 161, 178, 255))

    front = (26, 20, 204, 230)
    draw_card(d, front, 22, (255, 255, 255, 255), FRONT_EDGE, 3.4)
    bars(d, [(54, 60, 116, 18, ACCENT),
             (54, 106, 100, 18, ACCENT_SOFT),
             (54, 152, 76, 18, ACCENT_SOFT)])
    return img


# ---------------------------------------------------------------- 拼版

def main() -> None:
    variants = [
        ("1 single+fold", variant_single_fold()),
        ("2 deck sym+fold", variant_deck_symmetric()),
        ("3 deck no fold", variant_deck_no_fold()),
        ("4 deck+back fold", variant_deck_corner_fold()),
    ]

    sizes = [16, 32, 64, 128]
    cell_h = 190
    sheet = Image.new("RGBA", (120 + 150 * len(sizes), cell_h * len(variants) + 20),
                      (244, 245, 248, 255))

    d = ImageDraw.Draw(sheet)
    x0 = 120
    for i, s in enumerate(sizes):
        d.text((x0 + i * 150 + 40, 6), f"{s}px", fill=(70, 78, 92))

    for row, (name, master) in enumerate(variants):
        y = 24 + row * cell_h
        d.text((8, y + 40), name, fill=(40, 46, 56))
        for i, s in enumerate(sizes):
            small = master.resize((s, s), Image.LANCZOS)
            # 放大到 128 便于肉眼观察（最近邻，保留像素真实感）
            shown = small.resize((128, 128), Image.NEAREST) if s < 128 else small
            sheet.alpha_composite(shown, (x0 + i * 150, y))

    out = Path(__file__).resolve().parent / "icon"
    out.mkdir(parents=True, exist_ok=True)
    target = out / "_compare-variants.png"
    sheet.save(target)
    print(target)


if __name__ == "__main__":
    main()
