"""Rebuild the code-native LocalChat mark as PNG and multi-resolution Windows ICO."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent.parent / "src" / "LocalChat" / "Assets"
root.mkdir(parents=True, exist_ok=True)
scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale), (0, 0, 0, 0))
d = ImageDraw.Draw(image)
def box(coords):
    return tuple(int(n * scale) for n in coords)
green = "#39745C"
cream = "#F6FAF4"
d.rounded_rectangle(box((8, 8, 248, 248)), radius=60 * scale, fill=green)
d.polygon([box((68, 162)), box((138, 162)), box((68, 210))], fill=cream)
d.rounded_rectangle(box((52, 54, 204, 182)), radius=24 * scale, fill=cream)
d.line([box((99, 86)), box((99, 148)), box((158, 148))], fill=green, width=18 * scale, joint="curve")
for x, y in ((99, 86), (99, 148), (158, 148)):
    d.ellipse(box((x-9, y-9, x+9, y+9)), fill=green)
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(root / "localchat.png")
image.save(root / "localchat.ico", sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print(root / "localchat.ico")
