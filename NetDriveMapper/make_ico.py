#!/usr/bin/env python3
from PIL import Image, ImageDraw, ImageFilter
import struct, os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "network-drive.ico")
PREV = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon-preview.png")
ORANGE = (247, 99, 27, 255)
ORANGE_D = (200, 70, 12, 255)


def lerp(a, b, t):
    return int(a + (b - a) * t)


def draw_icon(S=512):
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    def u(x, y):
        return int(x * S), int(y * S)

    # soft drop shadow
    sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle([u(0.16, 0.30), u(0.90, 0.78)], radius=S // 14, fill=(0, 0, 0, 70))
    sh = sh.filter(ImageFilter.GaussianBlur(S // 28))
    im = Image.alpha_composite(im, sh)
    d = ImageDraw.Draw(im)

    # drive body
    body = [u(0.14, 0.22), u(0.88, 0.70)]
    d.rounded_rectangle(body, radius=S // 12, fill=(236, 240, 244, 255), outline=(160, 170, 180, 255), width=max(2, S // 90))
    # top gloss
    d.rounded_rectangle([u(0.18, 0.25), u(0.84, 0.40)], radius=S // 18, fill=(255, 255, 255, 230))
    # front face
    d.rounded_rectangle([u(0.18, 0.46), u(0.84, 0.66)], radius=S // 22, fill=(210, 216, 222, 255))
    # slot
    d.rounded_rectangle([u(0.24, 0.51), u(0.70, 0.58)], radius=S // 40, fill=(40, 46, 52, 255))
    # LED
    lx, ly = u(0.77, 0.545)
    r = max(3, S // 36)
    d.ellipse([lx - r - 2, ly - r - 2, lx + r + 2, ly + r + 2], fill=(180, 255, 160, 90))
    d.ellipse([lx - r, ly - r, lx + r, ly + r], fill=(50, 210, 80, 255))

    # CSL orange network jack
    jack = [u(0.10, 0.62), u(0.38, 0.86)]
    d.rounded_rectangle(jack, radius=S // 28, fill=ORANGE, outline=ORANGE_D, width=max(2, S // 100))
    # inner socket
    d.rounded_rectangle([u(0.14, 0.68), u(0.28, 0.80)], radius=S // 50, fill=(40, 22, 10, 255))
    # pins
    for i in range(4):
        px = 0.16 + i * 0.025
        d.rectangle([u(px, 0.70), u(px + 0.012, 0.78)], fill=(230, 190, 80, 255))
    # cable stub
    d.rounded_rectangle([u(0.08, 0.70), u(0.14, 0.78)], radius=S // 50, fill=ORANGE_D)

    return im


def rgba_to_ico_bmp(im):
    w, h = im.size
    pixels = im.load()
    xor = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = pixels[x, y]
            xor += struct.pack("<BBBB", b, g, r, a)
    row = ((w + 31) // 32) * 4
    and_mask = bytearray()
    for y in range(h - 1, -1, -1):
        bits = count = 0
        line = bytearray()
        for x in range(w):
            a = pixels[x, y][3]
            bits = (bits << 1) | (1 if a < 128 else 0)
            count += 1
            if count == 8:
                line.append(bits)
                bits = count = 0
        if count:
            bits <<= (8 - count)
            line.append(bits)
        while len(line) < row:
            line.append(0)
        and_mask += line
    header = struct.pack("<IIIHHIIIIII", 40, w, h * 2, 1, 32, 0, len(xor), 0, 0, 0, 0)
    return header + xor + and_mask


def save_ico(path, master):
    sizes = (16, 32, 48, 64, 128, 256)
    images = [master.resize((s, s), Image.Resampling.LANCZOS) for s in sizes]
    payloads = [rgba_to_ico_bmp(im) for im in images]
    offset = 6 + 16 * len(images)
    data = struct.pack("<HHH", 0, 1, len(images))
    for im, payload in zip(images, payloads):
        w, h = im.size
        data += struct.pack("<BBBBHHII", w if w < 256 else 0, h if h < 256 else 0, 0, 0, 1, 32, len(payload), offset)
        offset += len(payload)
    for payload in payloads:
        data += payload
    open(path, "wb").write(data)


if __name__ == "__main__":
    master = draw_icon(512)
    master.save(PREV)
    save_ico(OUT, master)
    print("ok", os.path.getsize(OUT))
