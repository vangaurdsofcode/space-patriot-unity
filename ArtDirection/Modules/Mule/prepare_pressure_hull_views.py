"""Deterministic crop and metric registration for the Mule concept/mesh pipeline.

The original generated sheet is immutable. Registered images are working bake
references, not evidence that six independently generated drawings agree in 3D.
Requires Pillow, numpy and scipy (already available in the local mesh runtime).
"""
from pathlib import Path
import hashlib
import json

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "pressure-hull-turnaround-v1.png"
# Exclude labels explicitly; coordinates are Pillow left/top/right/bottom.
VIEWS = {
    "top": ([8, 48, 760, 325], [26.0, 9.6]),
    "bottom": ([776, 48, 1528, 325], [26.0, 9.6]),
    "port": ([8, 384, 760, 635], [26.0, 7.1]),
    "starboard": ([776, 384, 1528, 635], [26.0, 7.1]),
    "nose": ([195, 703, 550, 991], [9.6, 7.1]),
    "aft": ([976, 703, 1330, 991], [9.6, 7.1]),
}
PIXELS_PER_METRE = 40
CANVAS = (1536, 768)


def main():
    source = Image.open(SOURCE).convert("RGB")
    assert source.size == (1536, 1024), "Source dimensions changed: review crop windows."
    records = {}
    for name, (window, dimensions) in VIEWS.items():
        raw = source.crop(window)
        pixels = np.asarray(raw).astype(np.int16)
        # The flat grey background has no chroma; dark edge seams and the warm
        # painted metal form one connected component. Hole filling retains the
        # pale interior paint rather than turning it into transparent holes.
        seed = (pixels.max(2) - pixels.min(2) > 13) | (pixels.mean(2) < 140)
        seed = ndimage.binary_closing(seed, iterations=2)
        labels, count = ndimage.label(seed)
        assert count > 0
        sizes = np.bincount(labels.ravel())
        sizes[0] = 0
        mask = ndimage.binary_fill_holes(labels == sizes.argmax())
        ys, xs = np.where(mask)
        box = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
        assert box[0] > 0 and box[1] > 0 and box[2] < raw.width and box[3] < raw.height
        raw_dir = ROOT / "views-raw"
        raw_dir.mkdir(exist_ok=True)
        raw.save(raw_dir / (name + ".png"))
        rgba = np.dstack([pixels.astype(np.uint8), mask.astype(np.uint8) * 255])
        subject = Image.fromarray(rgba).crop(box)
        target = tuple(round(v * PIXELS_PER_METRE) for v in dimensions)
        resized = subject.resize(target, Image.Resampling.LANCZOS)
        left, top = ((CANVAS[0] - target[0]) // 2, (CANVAS[1] - target[1]) // 2)
        registered = Image.new("RGBA", CANVAS)
        registered.paste(resized, (left, top))
        reg_dir = ROOT / "views-registered"
        reg_dir.mkdir(exist_ok=True)
        registered.save(reg_dir / (name + ".png"))
        records[name] = {
            "sheet_crop_ltrb": window,
            "raw_subject_bbox_ltrb": list(box),
            "raw_subject_size_pixels": list(subject.size),
            "raw_pixels_per_metre_u_v": [subject.width / dimensions[0], subject.height / dimensions[1]],
            "raw_anisotropy_ratio_u_over_v": (subject.width / dimensions[0]) / (subject.height / dimensions[1]),
            "registered_subject_bbox_ltrb": [left, top, left + target[0], top + target[1]],
            "registered_metres_u_v": dimensions,
            "registered_pixels_per_metre": PIXELS_PER_METRE,
            "rescale_x_y": [target[0] / subject.width, target[1] / subject.height],
            "raw_file": f"views-raw/{name}.png",
            "registered_file": f"views-registered/{name}.png",
        }
    report = {
        "schema": "space-patriot-orthographic-art-registration-v1",
        "source_sha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        "canvas_pixels": list(CANVAS),
        "normalization": "Independent affine bounds registration to the proposed physical component envelope; the raw concept drawings are retained unchanged.",
        "three_dimensional_consistency_verified": False,
        "mesh_overlay_verified": False,
        "bake_approved": False,
        "views": records,
    }
    (ROOT / "view-registration.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v["raw_subject_size_pixels"] for k, v in records.items()}))


if __name__ == "__main__":
    main()
