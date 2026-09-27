"""Measure bilateral symmetry in fixed-camera saved hull albedo renders.

Python (NumPy + Pillow) compare_symmetry_views.py --directory CANDIDATE

Reads Renders/After/{port,starboard,nose,aft,top,bottom}.png without changing
those images or any model. Writes a report and an actual-render comparison
board under SymmetryComparison. No registration, crop fitting, or recoloring
is used in measurements. Board thumbnails alone are uniformly reduced.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


COMPARISONS = (
    ('port_starboard', 'Port / reflected starboard', 'port', 'starboard', 'horizontal'),
    ('nose', 'Nose / reflected nose', 'nose', 'nose', 'horizontal'),
    ('aft', 'Aft / reflected aft', 'aft', 'aft', 'horizontal'),
    ('top', 'Top / reflected top', 'top', 'top', 'vertical'),
    ('bottom', 'Bottom / reflected bottom', 'bottom', 'bottom', 'vertical'),
)
BACKGROUND = (18, 24, 31)
CYAN = (35, 218, 242)
MAGENTA = (244, 71, 177)


def load_render(path):
    with Image.open(path) as image:
        if 'A' not in image.getbands():
            raise ValueError(f'Render has no alpha channel; transparent silhouette required: {path}')
        pixels = np.array(image.convert('RGBA'))
    alpha = pixels[:, :, 3]
    if not np.any(alpha < 128) or not np.any(alpha >= 250):
        raise ValueError(f'Render must contain transparent background and opaque hull: {path}')
    return pixels


def reflect(pixels, direction):
    if direction == 'horizontal':
        return pixels[:, ::-1].copy()
    if direction == 'vertical':
        return pixels[::-1].copy()
    raise ValueError(f'Unknown reflection: {direction}')


def boundary(mask):
    """One-pixel inner boundary, including any foreground at the frame edge."""
    padded = np.pad(mask, 1, constant_values=False)
    interior = (mask & padded[:-2, 1:-1] & padded[2:, 1:-1]
                & padded[1:-1, :-2] & padded[1:-1, 2:])
    return mask & ~interior


def measure(a, b, min_iou=.995, max_rgb_error=.03):
    if a.shape != b.shape:
        raise ValueError(f'Fixed-camera render dimensions differ: {a.shape} / {b.shape}')
    ma, mb = a[:, :, 3] >= 128, b[:, :, 3] >= 128
    common, union = ma & mb, ma | mb
    common_opaque = (a[:, :, 3] >= 250) & (b[:, :, 3] >= 250)
    if not union.any() or not common_opaque.any():
        raise ValueError('Empty silhouette or no common opaque pixels; comparison is invalid')
    difference = np.abs(a[:, :, :3].astype(np.float32) - b[:, :, :3].astype(np.float32)) / 255.0
    rgb_error = float(difference[common_opaque].mean())
    iou = float(common.sum() / union.sum())
    frame_clipped = bool(ma[0].any() or ma[-1].any() or ma[:, 0].any() or ma[:, -1].any()
                         or mb[0].any() or mb[-1].any() or mb[:, 0].any() or mb[:, -1].any())
    report = {
        'width': a.shape[1], 'height': a.shape[0],
        'silhouette_iou': iou,
        'original_only_pixels': int((ma & ~mb).sum()),
        'reflected_only_pixels': int((mb & ~ma).sum()),
        'common_silhouette_pixels': int(common.sum()),
        'common_opaque_pixels': int(common_opaque.sum()),
        'mean_rgb_error_common_opaque_0_to_1': rgb_error,
        'mean_rgb_error_common_opaque_0_to_255': rgb_error * 255,
        'p95_pixel_mean_rgb_error_common_opaque_0_to_1': float(np.quantile(difference[common_opaque].mean(1), .95)),
        'foreground_touches_frame': frame_clipped,
        'within_symmetry_review_tolerances': bool(iou >= min_iou and rgb_error <= max_rgb_error and not frame_clipped),
    }
    overlay = np.empty((*ma.shape, 3), np.uint8)
    overlay[:] = BACKGROUND
    overlay[common] = (43, 57, 65)
    overlay[ma & ~mb] = CYAN
    overlay[mb & ~ma] = MAGENTA
    overlay[boundary(ma) & boundary(mb)] = (246, 248, 250)
    return report, Image.fromarray(overlay)


def font(size):
    for name in ('C:/Windows/Fonts/segoeui.ttf', 'DejaVuSans.ttf', 'arial.ttf'):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def composite(pixels):
    foreground = Image.fromarray(pixels)
    background = Image.new('RGBA', foreground.size, BACKGROUND + (255,))
    return Image.alpha_composite(background, foreground).convert('RGB')


def overlay_thumbnail(image, size):
    # Preserve the visibility of every full-resolution contour/discrepancy in
    # the small board. Metrics and standalone overlap maps remain untouched.
    pixels = np.asarray(image)
    thumbnail = np.array(image.resize(size, Image.Resampling.NEAREST))
    for color in ((246, 248, 250), CYAN, MAGENTA):
        mask = np.all(pixels == color, axis=2)
        occupied = np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).resize(size, Image.Resampling.BOX)) > 0
        thumbnail[occupied] = color
    return Image.fromarray(thumbnail)


def create_board(rows, output):
    panel_width, gap, header = 320, 14, 133
    aspect = rows[0]['images'][0].height / rows[0]['images'][0].width
    panel_height = round(panel_width * aspect)
    row_height = panel_height + 64 + gap
    board = Image.new('RGB', (panel_width * 3 + gap * 4, header + row_height * len(rows) + 12), BACKGROUND)
    draw = ImageDraw.Draw(board)
    draw.text((gap, 12), 'Saved hull symmetry comparison', fill=(237, 242, 247), font=font(25))
    draw.text((gap, 49), 'Original renders and prescribed reflections. No resizing, alignment, or cropping in metrics.',
              fill=(177, 191, 202), font=font(16))
    draw.text((gap, 73), 'Symmetry checks do not certify geometry quality, concept fidelity, or correct UV placement.',
              fill=(177, 191, 202), font=font(16))
    for x, color, label in ((gap, CYAN, 'Original only'), (220, MAGENTA, 'Reflection only'),
                            (444, (246, 248, 250), 'Aligned contour')):
        draw.rectangle((x, 105, x + 12, 117), fill=color)
        draw.text((x + 19, 101), label, fill=(204, 213, 220), font=font(15))
    for row_index, row in enumerate(rows):
        y = header + row_index * row_height
        metrics = row['metrics']
        status = 'within tolerances' if metrics['within_symmetry_review_tolerances'] else 'review differences'
        draw.text((gap, y), row['title'], fill=(238, 242, 246), font=font(18))
        summary = (f"IoU {metrics['silhouette_iou']:.5f} | mean RGB {metrics['mean_rgb_error_common_opaque_0_to_255']:.2f}/255"
                   f" | {status}")
        draw.text((gap + 388, y + 2), summary, fill=(187, 201, 211), font=font(14))
        for column, (label, image) in enumerate(zip(('Actual render', 'Prescribed reflection', 'Silhouette overlap'), row['images'])):
            x = gap + column * (panel_width + gap)
            draw.text((x, y + 30), label, fill=(159, 177, 190), font=font(14))
            thumbnail = (image.resize((panel_width, panel_height), Image.Resampling.LANCZOS) if column < 2
                         else overlay_thumbnail(image, (panel_width, panel_height)))
            board.paste(thumbnail, (x, y + 57))
    board.save(output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', required=True, type=Path, help='Final saved candidate directory')
    parser.add_argument('--min-iou', type=float, default=.995, help='Review threshold, not a model-quality grade')
    parser.add_argument('--max-rgb-error', type=float, default=.03, help='Mean encoded RGB error, range 0..1')
    args = parser.parse_args()
    if not 0 <= args.min_iou <= 1 or not 0 <= args.max_rgb_error <= 1:
        parser.error('Thresholds must be in the range 0..1')
    directory = args.directory.resolve()
    render_directory = directory / 'Renders' / 'After'
    names = sorted({name for _, _, a, b, _ in COMPARISONS for name in (a, b)})
    pixels = {name: load_render(render_directory / f'{name}.png') for name in names}
    # The verifier renders all these views on the same fixed-size canvas.
    if len({image.shape for image in pixels.values()}) != 1:
        raise ValueError('All six saved views must share the same image dimensions')
    report = {
        'candidate': str(directory), 'render_directory': str(render_directory),
        'measurement_space': 'Unmodified encoded PNG RGB; alpha excluded from RGB error',
        'registration': 'None: no translation, crop, resize, rotation, or color fitting',
        'silhouette_alpha_threshold_byte': 128, 'common_opaque_alpha_threshold_byte': 250,
        'review_thresholds': {'minimum_silhouette_iou': args.min_iou, 'maximum_mean_rgb_error_0_to_1': args.max_rgb_error,
                              'foreground_must_not_touch_frame': True},
        'limitations': [
            'Image-space symmetry does not prove UV correctness, art quality, watertightness, normals, or attachment contact.',
            'A symmetrically incorrect mesh or paint layout can satisfy these checks.',
            'Opaque-pixel RGB excludes antialiased edges; silhouette errors remain separately measured.',
            'Self-reflection views compare both halves twice and are not independent samples.',
            'Inputs are hull-only fixed-camera albedo renders; no full-assembly or lit-runtime approval is implied.',
            'Board contour/discrepancy pixels are preserved when downsampling for visibility; standalone overlap maps retain original pixel dimensions.',
        ],
        'input_sha256': {name: hashlib.sha256((render_directory / f'{name}.png').read_bytes()).hexdigest() for name in names},
        'comparisons': {},
    }
    rows = []
    for key, title, a, b, direction in COMPARISONS:
        mirrored = reflect(pixels[b], direction)
        metrics, overlay = measure(pixels[a], mirrored, args.min_iou, args.max_rgb_error)
        metrics.update({'original_file': f'{a}.png', 'reflected_file': f'{b}.png', 'reflection': direction})
        report['comparisons'][key] = metrics
        rows.append({'key': key, 'title': title, 'metrics': metrics,
                     'images': (composite(pixels[a]), composite(mirrored), overlay)})
    report['all_within_symmetry_review_tolerances'] = all(row['metrics']['within_symmetry_review_tolerances'] for row in rows)
    output = directory / 'SymmetryComparison'
    output.mkdir(exist_ok=True)
    for row in rows:
        row['images'][2].save(output / f"{row['key']}-silhouette-overlap.png")
    create_board(rows, output / 'symmetry-comparison.png')
    (output / 'symmetry-view-metrics.json').write_text(json.dumps(report, indent=2), encoding='utf8')
    print(json.dumps({'output': str(output), 'all_within_symmetry_review_tolerances': report['all_within_symmetry_review_tolerances'],
                      'comparisons': report['comparisons']}, indent=2))


if __name__ == '__main__':
    main()
