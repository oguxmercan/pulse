"""Pulse logo generator: rounded dark tile + neon pulse line. Run to regenerate assets."""
from PIL import Image, ImageDraw, ImageFilter

S = 512
ACCENT = (84, 230, 168)
BG = (11, 17, 24)
BORDER = (30, 42, 56)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle([0, 0, S - 1, S - 1], radius=112, fill=BG, outline=BORDER, width=6)

# heartbeat polyline across the tile
pts = [(56, 268), (150, 268), (186, 268), (214, 196), (250, 336), (286, 232), (312, 268), (456, 268)]
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.line(pts, fill=ACCENT + (110,), width=40, joint="curve")
glow = glow.filter(ImageFilter.GaussianBlur(16))
img.alpha_composite(glow)
d.line(pts, fill=ACCENT, width=22, joint="curve")
d.ellipse([456 - 11, 268 - 11, 456 + 11, 268 + 11], fill=ACCENT)

img.save("logo.png")

# multi-size icon (PNG frames are valid ICO payloads since Vista)
sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [img.resize((s, s), Image.LANCZOS) for s in sizes]
frames[-1].save("pulse.ico", format="ICO", sizes=[(s, s) for s in sizes])
print("wrote logo.png + pulse.ico")
