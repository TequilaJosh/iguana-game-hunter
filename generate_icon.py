"""One-shot icon generator: iguana-eye.ico (16/24/32/48/64/128/256)."""
import math
from PIL import Image, ImageDraw

OUT = "iguana-eye.ico"
SIZES = [16, 24, 32, 48, 64, 128, 256]
MASTER = 1024  # render large, downsample for clean edges


def render_eye(s: int) -> Image.Image:
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = cy = s / 2
    R = s / 2 - max(1, s // 256)

    # --- Outer scaly ring (dark moss) with chartreuse rim ---
    d.ellipse((cx - R, cy - R, cx + R, cy + R),
              fill=(20, 40, 25, 255),
              outline=(74, 124, 58, 255),
              width=max(2, s // 64))

    # Inner eyelid ring
    r1 = R * 0.93
    d.ellipse((cx - r1, cy - r1, cx + r1, cy + r1),
              fill=(10, 20, 16, 255),
              outline=(124, 196, 74, 255),
              width=max(1, s // 128))

    # --- Iris radial gradient (deep brown -> amber -> warm gold core) ---
    iris_r = R * 0.86
    iris_stops = [
        (88, 38, 10),
        (132, 64, 18),
        (176, 96, 28),
        (212, 144, 44),
        (228, 172, 56),
        (244, 198, 78),
        (252, 222, 116),
    ]
    n = len(iris_stops)
    for i, color in enumerate(iris_stops):
        rr = iris_r * (1 - i / n)
        d.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), fill=color + (255,))

    # --- Radial striations (iguana iris fibers) ---
    striation = (96, 44, 8, 220)
    n_lines = 48
    line_w = max(1, s // 220)
    for i in range(n_lines):
        a = (i / n_lines) * 2 * math.pi
        x0 = cx + math.cos(a) * iris_r * 0.18
        y0 = cy + math.sin(a) * iris_r * 0.18
        x1 = cx + math.cos(a) * iris_r * 0.96
        y1 = cy + math.sin(a) * iris_r * 0.96
        d.line((x0, y0, x1, y1), fill=striation, width=line_w)

    # --- Pupil (iguanas have round pupils) with soft dark halo ---
    pupil_r = iris_r * 0.30
    halo_r = pupil_r * 1.18
    d.ellipse((cx - halo_r, cy - halo_r, cx + halo_r, cy + halo_r),
              fill=(20, 10, 0, 210))
    d.ellipse((cx - pupil_r, cy - pupil_r, cx + pupil_r, cy + pupil_r),
              fill=(0, 0, 0, 255))

    # --- Catch-lights ---
    hl_r = pupil_r * 0.42
    hx, hy = cx - pupil_r * 0.32, cy - pupil_r * 0.40
    d.ellipse((hx - hl_r, hy - hl_r, hx + hl_r, hy + hl_r),
              fill=(255, 252, 230, 245))
    hl2_r = pupil_r * 0.18
    hx2, hy2 = cx + pupil_r * 0.30, cy + pupil_r * 0.32
    d.ellipse((hx2 - hl2_r, hy2 - hl2_r, hx2 + hl2_r, hy2 + hl2_r),
              fill=(255, 240, 200, 190))

    return img


master = render_eye(MASTER).resize((256, 256), Image.LANCZOS)
master.save(OUT, format="ICO",
            sizes=[(s, s) for s in SIZES])
print(f"wrote {OUT} ({SIZES})")
