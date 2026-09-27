"""Verify reopened Kestrel six-view bake files, UV0 and packed texture pixels.

Run in Blender, for example:
  blender --background --python-exit-code 1 --python verify_kestrel_ortho_saved.py \
    -- --directory ABSOLUTE_CANDIDATE_DIR --baseline ABSOLUTE_SOURCE_BLEND

The default checks 01-top.blend through 06-aft.blend. --through permits checking
an in-progress prefix. No bake report booleans are trusted. This script only
reads blends/textures; its sole output is a JSON validation report.
"""

import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy
import numpy as np


VIEWS = ("top", "bottom", "port", "starboard", "nose", "aft")
SIZE = 4096
HALF = SIZE // 2
PADDING = 12
CHANNELS = {
    "base_color": ("Kestrel_BaseColor_4096", "Kestrel_BaseColor.png", None),
    "normal": ("Kestrel_Normal_4096", "Kestrel_Normal.png", (.5, .5, 1., 1.)),
    "metallic_smoothness": (
        "Kestrel_Metal_4096", "Kestrel_MetallicSmoothness.png", (.35, .35, .35, .38)
    ),
    "roughness": (
        "Kestrel_Roughness_4096", "Kestrel_Roughness_source.png", (.62, .62, .62, 1.)
    ),
}
NEUTRAL_ALBEDO = np.rint(np.array((.34, .33, .29)) * 255).astype(np.int16)


def digest(data):
    if isinstance(data, np.ndarray):
        data = np.ascontiguousarray(data).tobytes()
    return hashlib.sha256(data).hexdigest()


def image_bytes(image):
    if tuple(image.size) != (SIZE, SIZE):
        raise ValueError(f"{image.name}: expected {SIZE}x{SIZE}, found {tuple(image.size)}")
    pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    if not np.isfinite(pixels).all():
        raise ValueError(f"{image.name}: non-finite image pixels")
    # PNG byte images quantize constants on save. Compare decoded 8-bit values
    # instead of failing on harmless floating-point representation differences.
    np.clip(pixels, 0., 1., out=pixels)
    pixels *= 255.
    np.rint(pixels, out=pixels)
    return pixels.astype(np.uint8).reshape(SIZE, SIZE, 4)


def tile_bounds(index):
    col, row = index % 2, index // 2
    return (col * 1024, SIZE - round((row + 1) * HALF / 3),
            (col + 1) * 1024, SIZE - round(row * HALF / 3))


def chart(array, index):
    x0, y0, x1, y1 = tile_bounds(index)
    return array[y0:y1, x0:x1]


def pixel_changes(left, right):
    return int(np.count_nonzero(np.any(left != right, axis=2)))


def region_hashes(array):
    return {
        "full_rgba8_sha256": digest(array),
        "charts": {view: digest(chart(array, i)) for i, view in enumerate(VIEWS)},
        "non_hull": {
            "v_lower_half": digest(array[:HALF]),
            "v_upper_right": digest(array[HALF:, HALF:]),
        },
    }


def changes(left, right):
    return {
        "full_image": pixel_changes(left, right),
        "charts": {view: pixel_changes(chart(left, i), chart(right, i))
                   for i, view in enumerate(VIEWS)},
        "non_hull": (pixel_changes(left[:HALF], right[:HALF]) +
                     pixel_changes(left[HALF:, HALF:], right[HALF:, HALF:])),
    }


def load_channel(key, blend_path, baseline=False):
    image_name, filename, _ = CHANNELS[key]
    image = bpy.data.images.get(image_name)
    if image is None:
        stem = image_name.rsplit("_", 1)[0]
        matches = [i for i in bpy.data.images if i.name.startswith(stem)]
        if len(matches) == 1:
            image = matches[0]
    origin = "blend_datablock"
    # Older source files drop their unreferenced roughness datablock on save.
    # The original external texture is still useful as the baseline only.
    if image is None and baseline:
        path = blend_path.parent / "Textures" / filename
        if path.is_file():
            image = bpy.data.images.load(str(path), check_existing=False)
            origin = "baseline_external_texture"
    if image is None:
        raise ValueError(f"Missing retained {key} image ({image_name})")
    return image, origin


def channel_record(image, origin, key, expected_path=None):
    pixels = image_bytes(image)
    record = {"image": image.name, "source": origin,
              "colorspace": image.colorspace_settings.name,
              "resolved_filepath": str(Path(bpy.path.abspath(image.filepath)).resolve()),
              **region_hashes(pixels)}
    problems = []
    if expected_path is not None:
        expected_path = expected_path.resolve()
        record["expected_external_file"] = str(expected_path)
        if Path(record["resolved_filepath"]) != expected_path:
            problems.append(f"Image filepath does not identify this pass's {expected_path}")
        packed = bytes(image.packed_file.data) if image.packed_file else None
        record["packed_png_sha256"] = digest(packed) if packed is not None else None
        if packed is None:
            problems.append("Saved blend does not retain packed image bytes")
        if not expected_path.is_file():
            problems.append(f"Missing pass texture {expected_path}")
        else:
            external = expected_path.read_bytes()
            record["external_png_sha256"] = digest(external)
            record["packed_equals_external"] = packed == external
            if packed != external:
                problems.append("Packed PNG bytes differ from the saved pass PNG")
        constant = CHANNELS[key][2]
        if constant is not None:
            wanted = np.rint(np.asarray(constant) * 255).astype(np.int16)
            delta = np.abs(pixels[HALF:, :HALF].astype(np.int16) - wanted)
            record["hull_max_constant_error_codes"] = int(delta.max())
            record["hull_nonconstant_texels"] = int(np.any(delta > 1, axis=2).sum())
            if record["hull_nonconstant_texels"]:
                problems.append("Hull PBR channel still contains non-neutral old-UV details")
            if image.colorspace_settings.name != "Non-Color":
                problems.append("PBR data channel is not marked Non-Color")
        elif image.colorspace_settings.name != "sRGB":
            problems.append("Base-color image is not marked sRGB")
    return pixels, record, problems


def uv_records(expected_layer):
    objects = sorted((o for o in bpy.context.scene.objects if o.type == "MESH"
                      and o.name.startswith("Hull_")
                      and any(o.name.endswith(f"_LOD{i}") for i in range(3))),
                     key=lambda o: o.name)
    problems, result = [], {}
    for lod in range(3):
        if not any(o.name.endswith(f"_LOD{lod}") for o in objects):
            problems.append(f"Missing Hull LOD{lod}")
    for obj in objects:
        mesh = obj.data
        layers = mesh.uv_layers
        record = {"layers": [layer.name for layer in layers],
                  "active_index": layers.active_index,
                  "polygon_count": len(mesh.polygons), "loop_count": len(mesh.loops)}
        result[obj.name] = record
        if len(layers) != 1 or layers[0].name != expected_layer:
            problems.append(f"{obj.name}: expected sole UV0 layer {expected_layer}")
            continue
        layer = layers[0]
        if layers.active_index != 0 or not layer.active_render:
            problems.append(f"{obj.name}: UV0 is not active for editing/rendering")
        uvs = np.empty(len(mesh.loops) * 2, dtype=np.float32)
        layer.data.foreach_get("uv", uvs)
        uvs = uvs.reshape(-1, 2)
        record["uv0_sha256"] = digest(uvs)
        if not len(uvs) or not np.isfinite(uvs).all():
            problems.append(f"{obj.name}: empty or non-finite UV0")
            continue
        points = uvs * SIZE
        counts = {view: 0 for view in VIEWS}
        bad = 0
        # Classification must be by the hull's canonical normal. The prototype
        # stores model dimensions in vertex coordinates and instances unrotated.
        normal_matrix = obj.matrix_world.to_3x3().inverted().transposed()
        for poly in mesh.polygons:
            normal = normal_matrix @ poly.normal
            axis = max(range(3), key=lambda i: abs(normal[i]))
            sign = 1 if normal[axis] >= 0 else -1
            view = {(0, 1): "nose", (0, -1): "aft", (1, 1): "top",
                    (1, -1): "bottom", (2, 1): "starboard", (2, -1): "port"}[(axis, sign)]
            counts[view] += 1
            x0, y0, x1, y1 = tile_bounds(VIEWS.index(view))
            coords = points[list(poly.loop_indices)]
            # Float32 UVs can differ by subpixel rounding. Bounds tolerate
            # <.02 px but still detect wrong tiles or missing 12px gutters.
            low = np.array((x0 + PADDING, y0 + PADDING)) - .02
            high = np.array((x1 - PADDING, y1 - PADDING)) + .02
            if np.any(coords < low) or np.any(coords > high):
                bad += 1
        record["polygons_by_direction"] = counts
        record["wrong_chart_or_gutter_polygons"] = bad
        if bad:
            problems.append(f"{obj.name}: {bad} polygons violate directional chart/gutter bounds")
        if any(count == 0 for count in counts.values()):
            problems.append(f"{obj.name}: at least one directional chart contains no polygons")
    return result, problems


def run(args):
    directory = args.directory.resolve()
    report_path = (args.report or directory / "saved-bake-validation.json").resolve()
    count = VIEWS.index(args.through) + 1
    report = {"directory": str(directory), "baseline": None, "uv_layer": args.uv_layer,
              "requested_passes": list(VIEWS[:count]), "pixel_comparison": "decoded RGBA8",
              "first_pass_non_hull_baseline_verified": False,
              "passes": [], "failures": [], "passed": False}
    previous, first_uvs = None, None
    try:
        if args.baseline:
            baseline = args.baseline.resolve()
            bpy.ops.wm.open_mainfile(filepath=str(baseline))
            previous, records = {}, {}
            for key in CHANNELS:
                image, origin = load_channel(key, baseline, baseline=True)
                previous[key], records[key], _ = channel_record(image, origin, key)
            report["baseline"] = {"blend": str(baseline), "channels": records}
        for index, view in enumerate(VIEWS[:count]):
            stem = f"{index + 1:02d}-{view}"
            blend = directory / f"{stem}.blend"
            item = {"view": view, "blend": str(blend), "channels": {}, "failures": []}
            report["passes"].append(item)
            if not blend.is_file():
                item["failures"].append("Missing saved blend")
                report["failures"].append(f"{stem}: missing saved blend")
                break
            bpy.ops.wm.open_mainfile(filepath=str(blend))
            current = {}
            for key, (_, filename, _) in CHANNELS.items():
                try:
                    image, origin = load_channel(key, blend)
                    current[key], record, errors = channel_record(
                        image, origin, key, directory / "Textures" / stem / filename)
                    item["channels"][key] = record
                    item["failures"].extend(f"{key}: {error}" for error in errors)
                    if previous is not None and key in previous:
                        delta = changes(current[key], previous[key])
                        record["changed_pixels_from_previous"] = delta
                        if delta["non_hull"]:
                            item["failures"].append(f"{key}: changed {delta['non_hull']} non-hull texels")
                        if index == 0 and args.baseline and delta["non_hull"] == 0:
                            record["baseline_non_hull_preserved"] = True
                        if index > 0:
                            if key == "base_color":
                                if delta["charts"][view] == 0:
                                    item["failures"].append("Base color intended chart did not change")
                                for other in VIEWS:
                                    if other != view and delta["charts"][other]:
                                        item["failures"].append(f"Base color overwrote {other} chart")
                            elif delta["full_image"]:
                                item["failures"].append(f"{key}: reset PBR pixels changed again after first pass")
                        elif key == "base_color" and delta["charts"][view] == 0:
                            item["failures"].append("Top chart did not change from baseline")
                    if key == "base_color":
                        rgb = chart(current[key], index)[:, :, :3].astype(np.int16)
                        nonneutral = int(np.any(np.abs(rgb - NEUTRAL_ALBEDO) > 2, axis=2).sum())
                        record["intended_chart_non_neutral_texels"] = nonneutral
                        if nonneutral < 1000:
                            item["failures"].append("Intended chart contains too few painted texels")
                except Exception as exc:
                    item["failures"].append(f"{key}: {type(exc).__name__}: {exc}")
            item["hull_uvs"], uv_errors = uv_records(args.uv_layer)
            item["failures"].extend(uv_errors)
            uv_hashes = {name: record.get("uv0_sha256") for name, record in item["hull_uvs"].items()}
            if first_uvs is None:
                first_uvs = uv_hashes
            elif uv_hashes != first_uvs:
                item["failures"].append("Hull UV0 differs from first saved pass")
            if index == 0 and args.baseline:
                report["first_pass_non_hull_baseline_verified"] = all(
                    item["channels"].get(key, {}).get("baseline_non_hull_preserved", False)
                    for key in CHANNELS)
            report["failures"].extend(f"{stem}: {failure}" for failure in item["failures"])
            previous = current
            print(json.dumps({"view": view, "failures": item["failures"],
                              "base_color_hash": item["channels"].get("base_color", {}).get("full_rgba8_sha256")}), flush=True)
        report["passed"] = not report["failures"] and len(report["passes"]) == count
    except Exception as exc:
        report["failures"].append(f"{type(exc).__name__}: {exc}")
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"passed": report["passed"], "report": str(report_path),
                      "failure_count": len(report["failures"])}), flush=True)
    if not report["passed"]:
        raise RuntimeError(f"Saved bake validation failed; inspect {report_path}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--baseline", type=Path)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--through", choices=VIEWS, default="aft")
    parser.add_argument("--uv-layer", default="AtlasUV_SixView_v2")
    tail = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    run(parser.parse_args(tail))
