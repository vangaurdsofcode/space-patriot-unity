"""Split the six orthographic views into stable, individual source images."""
from pathlib import Path
from PIL import Image

PROJECT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT / "ArtDirection/Modules/kestrel-hull-orthographic-turnaround-v1.png"
OUT = PROJECT / "ArtDirection/Modules/kestrel-hull-ortho-v1"

# Divider pixels are deliberately excluded. Cell edges were measured from the
# generated 1536x1024 sheet; views are row-major in pairs, not a 3x2 grid.
PANELS = {
    "top": (0, 0, 766, 431),
    "bottom": (770, 0, 1536, 431),
    "port": (0, 435, 766, 707),
    "starboard": (770, 435, 1536, 707),
    "nose": (0, 712, 766, 1024),
    "aft": (770, 712, 1536, 1024),
}

if not SOURCE.is_file():
    raise SystemExit(f"Missing source turnaround: {SOURCE}")
OUT.mkdir(parents=True, exist_ok=True)
sheet = Image.open(SOURCE).convert("RGB")
if sheet.size != (1536, 1024):
    raise SystemExit(f"Expected 1536x1024 turnaround, got {sheet.size}")
for name, bounds in PANELS.items():
    target = OUT / f"{name}.png"
    sheet.crop(bounds).save(target, optimize=True)
    print(f"{name}: {target} {Image.open(target).size}")
