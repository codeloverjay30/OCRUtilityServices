#!/usr/bin/env python3
"""Run full-screen OCR and automatically rescan unusually wide text detections.

The recognition algorithm never reads the expected text or manually selected ROI.
An optional fixture is used only for post-inference acceptance evaluation.
"""

from __future__ import annotations

import argparse
import contextlib
import tempfile
import json
import math
import platform
import sys
import time
from datetime import datetime
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageDraw


def parse_args() -> argparse.Namespace:
    """Parse diagnostic options without accepting any manual recognition ROI."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", type=Path, default=None)
    parser.add_argument("--replay-report", type=Path, default=None,
                        help="Replay fusion from a saved report without PaddleOCR.")
    parser.add_argument("--output-dir", type=Path, default=None)
    parser.add_argument("--fixture", type=Path, default=None,
                        help="Optional expected text and ROI used ONLY after inference.")
    parser.add_argument("--det-model", default=None)
    parser.add_argument("--rec-model", default=None)
    parser.add_argument("--min-wide-px", type=int, default=230)
    parser.add_argument("--min-aspect-ratio", type=float, default=7.0)
    parser.add_argument("--tile-height-factor", type=float, default=1.0)
    parser.add_argument("--tile-width-factor", type=float, default=5.8)
    parser.add_argument("--tile-overlap", type=float, default=0.45)
    parser.add_argument("--max-tiles", type=int, default=48)
    parser.add_argument("--min-confidence", type=float, default=0.55)
    parser.add_argument("--tile-padding-x", type=int, default=8,
                        help="Horizontal context pixels added to each proposed tile.")
    parser.add_argument("--tile-padding-y", type=int, default=6,
                        help="Vertical context pixels added to each proposed tile.")
    parser.add_argument("--max-tiles-per-parent", type=int, default=12,
                        help="Maximum rescan calls for each wide parent.")
    parser.add_argument("--save-tile-images", action="store_true",
                        help="Save actual rescan crops for inspection.")
    return parser.parse_args()


def serializable(value: Any) -> Any:
    """Convert PaddleOCR output to JSON-compatible values."""
    if isinstance(value, dict):
        return {str(k): serializable(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [serializable(v) for v in value]
    if hasattr(value, "tolist"):
        return serializable(value.tolist())
    if hasattr(value, "item"):
        return serializable(value.item())
    if isinstance(value, float):
        return value if math.isfinite(value) else None
    if isinstance(value, (str, int, bool)) or value is None:
        return value
    return str(value)


def get_payload(result: Any) -> dict[str, Any]:
    """Read the public PaddleOCR result JSON structure."""
    raw = getattr(result, "json", None)
    if callable(raw):
        raw = raw()
    raw = serializable(raw)
    if not isinstance(raw, dict):
        raise ValueError("PaddleOCR result does not expose a JSON object.")
    payload = raw.get("res", raw)
    if not isinstance(payload, dict):
        raise ValueError("Invalid PaddleOCR result payload.")
    return payload


def parse_lines(results: Any, offset: tuple[int, int],
                image_size: tuple[int, int], source: str) -> list[dict[str, Any]]:
    """Validate OCR results and convert all coordinates to the original image."""
    lines: list[dict[str, Any]] = []
    for page, result in enumerate(results):
        payload = get_payload(result)
        texts = payload.get("rec_texts", [])
        scores = payload.get("rec_scores", [])
        polys = payload.get("rec_polys", payload.get("dt_polys", []))
        boxes = payload.get("rec_boxes", [])
        if not isinstance(texts, list):
            raise ValueError("rec_texts must be an array.")
        for i, text in enumerate(texts):
            if not isinstance(text, str):
                raise ValueError("OCR text must be a string.")
            score = float(scores[i]) if i < len(scores) else None
            if score is not None and (not math.isfinite(score) or not 0 <= score <= 1):
                raise ValueError("OCR confidence must be finite and between 0 and 1.")
            poly = polys[i] if i < len(polys) else None
            if poly is None and i < len(boxes):
                b = boxes[i]
                if len(b) == 4:
                    poly = [[b[0], b[1]], [b[2], b[1]], [b[2], b[3]], [b[0], b[3]]]
            if poly is None:
                continue
            poly = serializable(poly)
            if not isinstance(poly, list) or len(poly) < 3:
                raise ValueError("Invalid OCR polygon.")
            pts = []
            for point in poly:
                if not isinstance(point, list) or len(point) < 2:
                    raise ValueError("Invalid polygon point.")
                x, y = float(point[0]) + offset[0], float(point[1]) + offset[1]
                if not math.isfinite(x) or not math.isfinite(y):
                    raise ValueError("Non-finite polygon coordinate.")
                pts.append([round(x, 2), round(y, 2)])
            xs, ys = zip(*pts)
            bbox = [min(xs), min(ys), max(xs), max(ys)]
            w, h = image_size
            if not (0 <= bbox[0] <= bbox[2] <= w and
                    0 <= bbox[1] <= bbox[3] <= h):
                raise ValueError(f"OCR polygon exceeds image bounds: {bbox}")
            lines.append({
                "text": text, "confidence": score,
                "polygon_original_xy": pts, "box_xyxy": bbox,
                "source": source, "page": page, "index": i,
            })
    return lines


def infer(ocr: Any, image: Image.Image, offset: tuple[int, int],
          original_size: tuple[int, int], source: str) -> tuple[list[dict[str, Any]], float]:
    """Run the official PaddleOCR pipeline on one image."""
    start = time.perf_counter()
    with contextlib.redirect_stdout(sys.stderr):
        with tempfile.TemporaryDirectory(prefix="issue59-ocr-") as temp_dir:
            image_path = Path(temp_dir) / "input.png"
            image.save(image_path, format="PNG")
            results = list(ocr.predict(input=str(image_path)))
    elapsed = round((time.perf_counter() - start) * 1000, 2)
    return parse_lines(results, offset, original_size, source), elapsed


def candidates(lines: list[dict[str, Any]], min_width: int,
               min_ratio: float) -> list[dict[str, Any]]:
    """Find unusually long horizontal text regions without knowing their content."""
    found = []
    for line in lines:
        x1, y1, x2, y2 = line["box_xyxy"]
        width, height = x2 - x1, y2 - y1
        if width >= min_width and height > 0 and width / height >= min_ratio:
            found.append(line)
    return sorted(found, key=lambda line: line["box_xyxy"][0])


def tiles_for_box(box: list[float], image_size: tuple[int, int],
                  width_factor: float, overlap: float,
                  height_factor: float) -> list[tuple[int, int, int, int]]:
    """Generate overlapping local rescans around a wide text region."""
    iw, ih = image_size
    x1, y1, x2, y2 = box
    height = max(1.0, y2 - y1)
    tile_width = max(64, round(height * width_factor))
    pad_y = max(4, round(height * height_factor * 0.5))
    top = max(0, math.floor(y1) - pad_y)
    bottom = min(ih, math.ceil(y2) + pad_y)
    left, right = max(0, math.floor(x1) - 8), min(iw, math.ceil(x2) + 8)
    if bottom <= top or right <= left:
        return []
    if right - left <= tile_width:
        return [(left, top, right, bottom)]
    stride = max(1, round(tile_width * (1.0 - overlap)))
    starts = list(range(left, max(left, right - tile_width) + 1, stride))
    last = max(left, right - tile_width)
    if not starts or starts[-1] != last:
        starts.append(last)
    return [(start, top, min(right, start + tile_width), bottom) for start in starts]


def intersection_over_union(a: list[float], b: list[float]) -> float:
    """Compute intersection-over-union of two axis-aligned bounding boxes."""
    x1, y1 = max(a[0], b[0]), max(a[1], b[1])
    x2, y2 = min(a[2], b[2]), min(a[3], b[3])
    intersection = max(0.0, x2 - x1) * max(0.0, y2 - y1)
    area_a = max(0.0, a[2] - a[0]) * max(0.0, a[3] - a[1])
    area_b = max(0.0, b[2] - b[0]) * max(0.0, b[3] - b[1])
    union = area_a + area_b - intersection
    return intersection / union if union > 0 else 0.0


def same_text_nearby(a: dict[str, Any], b: dict[str, Any]) -> bool:
    """Check if two observations are likely the same text instance."""
    if a["text"] != b["text"]:
        return False
    ax1, ay1, ax2, ay2 = a["box_xyxy"]
    bx1, by1, bx2, by2 = b["box_xyxy"]
    cx_a, cy_a = (ax1 + ax2) / 2, (ay1 + ay2) / 2
    cx_b, cy_b = (bx1 + bx2) / 2, (by1 + by2) / 2
    h = max(1.0, ay2 - ay1, by2 - by1)
    return (intersection_over_union(a["box_xyxy"], b["box_xyxy"]) >= 0.35
            or (abs(cx_a - cx_b) <= h * 0.7 and abs(cy_a - cy_b) <= h * 0.7))


def deduplicate(lines: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Keep highest-confidence nearby copies without merging distinct texts."""
    ordered = sorted(lines, key=lambda item: item["confidence"] or 0, reverse=True)
    kept: list[dict[str, Any]] = []
    for line in ordered:
        if not line["text"].strip():
            continue
        if any(same_text_nearby(line, other) for other in kept):
            continue
        kept.append(line)
    return sorted(kept, key=lambda item: (item["box_xyxy"][1], item["box_xyxy"][0]))


def covered_fraction(parent: list[float], children: list[dict[str, Any]]) -> float:
    """Estimate horizontal coverage by independently recognized child regions."""
    spans = []
    for child in children:
        b = child["box_xyxy"]
        if b[3] < parent[1] or b[1] > parent[3]:
            continue
        left, right = max(parent[0], b[0]), min(parent[2], b[2])
        if right > left:
            spans.append((left, right))
    spans.sort()
    merged: list[list[float]] = []
    for left, right in spans:
        if merged and left <= merged[-1][1]:
            merged[-1][1] = max(merged[-1][1], right)
        else:
            merged.append([left, right])
    return sum(right - left for left, right in merged) / max(1.0, parent[2] - parent[0])


def annotate(image: Image.Image, lines: list[dict[str, Any]], destination: Path) -> None:
    """Draw polygons and source markers on a copy of the full screenshot."""
    canvas = image.convert("RGB").copy()
    draw = ImageDraw.Draw(canvas)
    for idx, line in enumerate(lines):
        pts = [tuple(map(round, point)) for point in line["polygon_original_xy"]]
        color = (0, 255, 0) if line["source"] == "rescan" else (255, 180, 0)
        draw.line(pts + [pts[0]], fill=color, width=2)
        draw.text(pts[0], str(idx), fill=color)
    canvas.save(destination)


def evaluate(lines: list[dict[str, Any]], fixture: Path | None,
             image_size: tuple[int, int]) -> dict[str, Any] | None:
    """Evaluate Issue59 expectations only after all generic inference has completed."""
    if fixture is None:
        return None
    data = json.loads(fixture.read_text(encoding="utf-8"))
    if list(image_size) != data["image_size"]:
        raise ValueError("Fixture image size does not match screenshot.")
    expected = data["expected_text"]
    roi = data["roi_xyxy"]
    exact = [line for line in lines if line["text"] == expected]
    inside = [
        line for line in exact
        if line["box_xyxy"][0] >= roi[0] and line["box_xyxy"][1] >= roi[1]
        and line["box_xyxy"][2] <= roi[2] and line["box_xyxy"][3] <= roi[3]
    ]
    merged = [line for line in lines if expected in line["text"] and line["text"] != expected]
    return {
        "case_id": data["case_id"],
        "expected_text": expected,
        "exact_instances": len(exact),
        "exact_instances_inside_reference_roi": len(inside),
        "merged_instances_containing_target": len(merged),
        "passed": len(inside) == 1 and not merged,
        "note": "Fixture expected text and ROI were used ONLY for evaluation, never to choose tiles.",
    }



def ink_projection(image: Image.Image, box: list[float], pad_y: int = 4) -> tuple[int, int, int, int, np.ndarray]:
    """Estimate vertical foreground density using local luminance contrast."""
    width, height = image.size
    x1 = max(0, int(math.floor(box[0])))
    x2 = min(width, int(math.ceil(box[2])))
    y1 = max(0, int(math.floor(box[1])) - pad_y)
    y2 = min(height, int(math.ceil(box[3])) + pad_y)
    pixels = np.asarray(image.crop((x1, y1, x2, y2)).convert("L"), dtype=np.float32)
    if pixels.size == 0 or pixels.shape[1] < 4:
        return x1, y1, x2, y2, np.zeros(max(0, x2 - x1))
    # A robust per-column contrast proxy works for both light and dark glyphs.
    # It is a proposal generator, not a reliable semantic UI boundary detector.
    median = float(np.median(pixels))
    contrast = np.abs(pixels - median)
    threshold = max(24.0, float(np.percentile(contrast, 70)) * 0.65)
    mask = contrast >= threshold
    projection = mask.mean(axis=0).astype(np.float64)
    projection = np.convolve(projection, np.ones(3) / 3, mode="same")
    return x1, y1, x2, y2, projection


def candidate_cuts(image: Image.Image, box: list[float],
                   min_gap: int = 3, max_cuts: int = 80) -> list[int]:
    """Propose cut positions from low-ink valleys without using OCR text."""
    x1, _, x2, _, density = ink_projection(image, box)
    if len(density) < 8:
        return [x1, x2]
    local_minima = []
    for i in range(2, len(density) - 2):
        if density[i] <= density[i - 1] and density[i] <= density[i + 1]:
            local_minima.append((float(density[i]), x1 + i))
    # Preserve well-separated valleys and supplement with width-aware positions.
    selected = [x1, x2]
    for _, pos in sorted(local_minima):
        if min(pos - x1, x2 - pos) < min_gap:
            continue
        if all(abs(pos - existing) >= min_gap for existing in selected):
            selected.append(pos)
        if len(selected) >= max_cuts:
            break
    return sorted(selected)


def adaptive_tiles(image: Image.Image, parent: dict[str, Any],
                   max_per_parent: int, min_chars: float = 2.5,
                   max_chars: float = 5.5) -> list[tuple[int, int, int, int]]:
    """Create bounded, gap-aligned crops of plausible short text labels."""
    x1, y1, x2, y2 = parent["box_xyxy"]
    iw, ih = image.size
    h = max(8, y2 - y1)
    min_width = max(40, round(h * min_chars))
    max_width = max(min_width + 1, round(h * max_chars))
    cuts = candidate_cuts(image, parent["box_xyxy"])
    # Generate crops from all cut pairs, favoring low-ink boundaries.
    # Add small exterior padding to retain edge strokes without absorbing
    # neighboring labels. The crop geometry is independent of expected text.
    top = max(0, math.floor(y1) - max(5, round(h * 0.18)))
    bottom = min(ih, math.ceil(y2) + max(5, round(h * 0.18)))
    proposals = []
    for i, left in enumerate(cuts[:-1]):
        for right in cuts[i + 1:]:
            span = right - left
            if span < min_width:
                continue
            if span > max_width:
                break
            edge_score = abs(span - 3.7 * h) / h
            proposals.append((edge_score, (max(0, left - 2), top,
                                           min(iw, right + 2), bottom)))
    # Include alternate widths around plausible character counts, useful when
    # antialiased backgrounds produce no clear gaps.
    for target_chars in (3.0, 4.0, 5.0):
        step = max(8, round(h * 0.65))
        width = round(h * target_chars)
        for left in range(math.floor(x1), math.ceil(x2) - width + 1, step):
            proposals.append((2.0, (max(0, left), top,
                                    min(iw, left + width), bottom)))
    # Reserve up to two widths at each endpoint before ranking interior crops.
    # The outer padding applied by scan_image retains strokes beyond the parent box.
    left_edge, right_edge = math.floor(x1), math.ceil(x2)
    endpoint_tiles = []
    for character_count in (3.7, 4.0):
        width = round(h * character_count)
        if left_edge + width < right_edge:
            endpoint_tiles.extend((
                (left_edge, top, left_edge + width, bottom),
                (right_edge - width, top, right_edge, bottom),
            ))

    # Preserve a slot for the original ranking when the budget is tight.
    reserved = list(dict.fromkeys(endpoint_tiles))[:max(0, max_per_parent - 1)]
    seen = set(reserved)
    unique = list(reserved)
    for _, tile in sorted(proposals, key=lambda item: item[0]):
        if tile in seen or tile[2] - tile[0] < 20:
            continue
        seen.add(tile)
        unique.append(tile)
        if len(unique) >= max_per_parent:
            break
    return unique


def line_inside_tile(line: dict[str, Any], tile: tuple[int, int, int, int],
                     tolerance: int = 2) -> bool:
    """Reject detections clipped by a crop edge."""
    a, b, c, d = line["box_xyxy"]
    return (a >= tile[0] + tolerance and b >= tile[1] + tolerance
            and c <= tile[2] - tolerance and d <= tile[3] - tolerance)


def choose_children(parent: dict[str, Any],
                    observations: list[dict[str, Any]],
                    min_confidence: float) -> tuple[list[dict[str, Any]], str]:
    """Conservatively replace a merged parent only with coherent child labels."""
    box = parent["box_xyxy"]
    pool = [
        item for item in deduplicate(observations)
        if item["confidence"] is not None
        and item["confidence"] >= min_confidence
        and item["text"] != parent["text"]
        and item["box_xyxy"][0] >= box[0] - 12
        and item["box_xyxy"][2] <= box[2] + 12
        and abs((item["box_xyxy"][1] + item["box_xyxy"][3]) / 2
                - (box[1] + box[3]) / 2) <= max(12, box[3] - box[1])
    ]
    # Weighted interval scheduling: prefer high-confidence nonoverlapping
    # child observations. A split is valid only if concatenated text equals
    # the original detection; coverage alone is not sufficient.
    pool.sort(key=lambda item: (item["box_xyxy"][2], item["box_xyxy"][0]))
    if len(pool) > 150:
        pool = sorted(pool, key=lambda item: item["confidence"], reverse=True)[:150]
        pool.sort(key=lambda item: item["box_xyxy"][2])
    n = len(pool)
    dp: list[tuple[float, list[int]]] = [(0.0, [])]
    for i, item in enumerate(pool):
        left = item["box_xyxy"][0]
        prev = -1
        for j in range(i - 1, -1, -1):
            if pool[j]["box_xyxy"][2] <= left + 2:
                prev = j
                break
        width = item["box_xyxy"][2] - left
        reward = width * float(item["confidence"])
        include = (dp[prev + 1][0] + reward, dp[prev + 1][1] + [i])
        exclude = dp[-1]
        dp.append(include if include[0] > exclude[0] else exclude)
    chosen = [pool[i] for i in dp[-1][1]]
    chosen.sort(key=lambda item: item["box_xyxy"][0])
    if len(chosen) < 2:
        return [], "fewer_than_two_nonoverlapping_children"
    parent_text = "".join(parent["text"].split())
    child_text = "".join("".join(item["text"].split()) for item in chosen)
    if child_text != parent_text:
        return [], "child_text_does_not_reconstruct_parent"
    if covered_fraction(box, chosen) < 0.75:
        return [], "insufficient_spatial_coverage"
    return chosen, "reconstructed"



def _center_inside(line: dict[str, Any], parent: dict[str, Any]) -> bool:
    """Check whether a child center belongs to a parent detection region."""
    x1, y1, x2, y2 = line["box_xyxy"]
    a, b, c, d = parent["box_xyxy"]
    return a - 12 <= (x1 + x2) / 2 <= c + 12 and b - 12 <= (y1 + y2) / 2 <= d + 12


def _supporting_observations(
    candidate: dict[str, Any], observations: list[dict[str, Any]]
) -> list[dict[str, Any]]:
    """Find spatially consistent independent OCR observations for a label."""
    return [
        item for item in observations
        if item["text"] == candidate["text"]
        and intersection_over_union(item["box_xyxy"], candidate["box_xyxy"]) >= 0.70
    ]


def resolve_layers(
    full_lines: list[dict[str, Any]],
    wide_lines: list[dict[str, Any]],
    tile_records: list[dict[str, Any]],
    min_confidence: float = 0.98,
) -> dict[str, Any]:
    """Resolve corroborated text instances while retaining ambiguous parents."""
    if not 0 <= min_confidence <= 1:
        raise ValueError("Fusion confidence threshold must be within [0, 1].")
    observations = [
        line for tile in tile_records for line in tile.get("lines", [])
        if isinstance(line, dict) and line.get("box_xyxy") is not None
        and isinstance(line.get("text"), str) and line.get("text", "").strip()
        and isinstance(line.get("confidence"), (int, float))
    ]
    unique = deduplicate(observations)
    trusted = []
    evidence = []
    for candidate in unique:
        parent_matches = [
            parent for parent in wide_lines
            if candidate["text"] != parent["text"]
            and candidate["text"] in parent["text"]
            and _center_inside(candidate, parent)
        ]
        if not parent_matches:
            continue
        supporting = _supporting_observations(candidate, observations)
        strong = [line for line in supporting if line["confidence"] >= min_confidence]
        # Require two distinct tiles; no expected target text or fixture coordinates.
        tile_ids = set()
        for index, tile in enumerate(tile_records):
            if any(
                item["text"] == candidate["text"]
                and item["confidence"] >= min_confidence
                and intersection_over_union(item["box_xyxy"], candidate["box_xyxy"]) >= 0.70
                for item in tile.get("lines", [])
            ):
                tile_ids.add(index)
        if len(tile_ids) < 2 or len(strong) < 2:
            continue
        best = max(strong, key=lambda item: item["confidence"])
        trusted.append(dict(best, evidence_count=len(tile_ids), localization_status="corroborated"))
        evidence.append({"text": best["text"], "box_xyxy": best["box_xyxy"],
                         "supporting_tile_count": len(tile_ids)})
    trusted = deduplicate(trusted)
    # Avoid promoting two different labels that claim the same physical region.
    filtered = []
    for item in sorted(trusted, key=lambda x: x["confidence"], reverse=True):
        if any(
            other["text"] != item["text"]
            and intersection_over_union(other["box_xyxy"], item["box_xyxy"]) > 0.35
            for other in filtered
        ):
            continue
        filtered.append(item)
    trusted = sorted(filtered, key=lambda x: (x["box_xyxy"][1], x["box_xyxy"][0]))
    ambiguous = [
        dict(parent, localization_status="ambiguous_merged_text")
        for parent in wide_lines
    ]
    normal = [
        dict(line, localization_status="original_detection")
        for line in full_lines if line not in wide_lines
    ]
    return {
        "resolved_text_instances": deduplicate(normal + trusted),
        "corroborated_rescan_instances": trusted,
        "unresolved_merged_boxes": ambiguous,
        "fusion_evidence": evidence,
        "policy": {
            "min_confidence": min_confidence,
            "min_supporting_tiles": 2,
            "min_spatial_iou": 0.70,
            "ambiguous_boxes_are_not_actionable": True,
            "text_boxes_are_not_verified_click_targets": True,
        },
    }


def replay_fusion(report: dict[str, Any], fixture: Path | None) -> dict[str, Any]:
    """Recompute the two-layer output exclusively from saved OCR observations."""
    for key in ("image_size", "full_screen_lines", "wide_candidates", "tiles"):
        if key not in report:
            raise ValueError(f"Replay report missing required field: {key}")
    layers = resolve_layers(
        report["full_screen_lines"], report["wide_candidates"], report["tiles"]
    )
    assessment = evaluate(
        layers["resolved_text_instances"], fixture, tuple(report["image_size"])
    )
    if assessment is not None:
        # Merged parents are retained separately as diagnostic evidence.
        assessment["unresolved_merged_boxes"] = len(layers["unresolved_merged_boxes"])
        assessment["scope"] = "resolved_text_instances_only"
    return {**layers, "acceptance": assessment, "replay_source": "saved_ocr_report"}


def run_replay(report_path: Path, fixture: Path | None, output_dir: Path | None) -> int:
    """Run deterministic fusion replay without importing or initializing PaddleOCR."""
    if not report_path.is_file():
        raise FileNotFoundError(report_path)
    report = json.loads(report_path.read_text(encoding="utf-8"))
    result = replay_fusion(report, fixture)
    output = output_dir or report_path.parent / "fusion-replay"
    output.mkdir(parents=True, exist_ok=True)
    destination = output / "fusion-replay-report.json"
    destination.write_text(
        json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False),
        encoding="utf-8",
    )
    print(f"Corroborated instances: {len(result['corroborated_rescan_instances'])}")
    print(f"Unresolved merged boxes: {len(result['unresolved_merged_boxes'])}")
    print("Replay acceptance:", json.dumps(result["acceptance"], ensure_ascii=False))
    print(f"Report: {destination}")
    assessment = result["acceptance"]
    return 0 if assessment is None or assessment["passed"] else 2


def scan_image(ocr, image, args, output=None):
    full, full_ms = infer(ocr, image, (0, 0), image.size, "full_screen")
    wide = candidates(full, args.min_wide_px, args.min_aspect_ratio)
    tile_records = []
    observations = []
    # Prioritize the longest candidate while preserving a bounded budget.
    # No expected text or fixture coordinates are consulted here.
    prioritized = sorted(
        enumerate(wide),
        key=lambda entry: entry[1]["box_xyxy"][2] - entry[1]["box_xyxy"][0],
        reverse=True,
    )
    per_parent = min(args.max_tiles_per_parent,
                     max(1, args.max_tiles // max(1, len(wide))))
    rejection_totals = {
        "raw_lines": 0, "accepted_lines": 0,
        "rejected_by_confidence": 0, "rejected_by_bounds": 0,
        "missing_geometry": 0,
    }
    def scan_tiles():
        seen = set()
        for parent_index, parent in prioritized:
            for proposal in adaptive_tiles(image, parent, per_parent):
                if len(tile_records) >= args.max_tiles:
                    break
                tile = (
                    max(0, proposal[0] - args.tile_padding_x),
                    max(0, proposal[1] - args.tile_padding_y),
                    min(image.width, proposal[2] + args.tile_padding_x),
                    min(image.height, proposal[3] + args.tile_padding_y),
                )
                if tile not in seen:
                    seen.add(tile)
                    yield parent_index, proposal, tile, "initial"

        initial_records = tuple(tile_records)
        for parent_index, parent in prioritized:
            ranked = sorted(
                ((line, record["tile_xyxy"])
                 for record in initial_records if record["parent_index"] == parent_index
                 for line in record["lines"]),
                key=lambda pair: pair[0]["confidence"], reverse=True,
            )
            rescans = 0
            for line, tile in ranked:
                text = line["text"]
                if (line["confidence"] < 0.98 or not text or text == parent["text"]
                        or text not in parent["text"] or not _center_inside(line, parent)):
                    continue
                x1, y1, x2, y2 = line["box_xyxy"]
                support = sum(
                    any(other["text"] == text and other["confidence"] >= 0.98
                        and intersection_over_union(other["box_xyxy"], line["box_xyxy"]) >= 0.70
                        for other in record["lines"])
                    for record in tile_records
                )
                if support != 1:
                    continue
                # Remove some left context while leaving room before the text.
                left = max(tile[0], int(x1) - max(2, round((y2 - y1) / 4)))
                refined = (left, tile[1], tile[2], tile[3])
                if left == tile[0] or refined in seen:
                    continue
                seen.add(refined)
                yield parent_index, tile, refined, "left_trim"
                rescans += 1
                if rescans == 3:
                    break

    for parent_index, proposal, tile, scan_kind in scan_tiles():
        crop = image.crop(tile)
        if output is not None and args.save_tile_images:
            tile_dir = output / "tiles"
            tile_dir.mkdir(exist_ok=True)
            crop.save(tile_dir / f"parent-{parent_index}-tile-{len(tile_records):03d}.png")
        raw, elapsed = infer(
            ocr, crop, (tile[0], tile[1]),
            image.size, "adaptive_rescan"
        )
        accepted = []
        reasons = []
        for item in raw:
            confidence = item["confidence"]
            if confidence is None or confidence < args.min_confidence:
                reasons.append({"text": item["text"], "reason": "confidence"})
                rejection_totals["rejected_by_confidence"] += 1
            elif not line_inside_tile(item, tile, tolerance=0):
                reasons.append({"text": item["text"], "reason": "bounds"})
                rejection_totals["rejected_by_bounds"] += 1
            else:
                accepted.append(item)
        rejection_totals["raw_lines"] += len(raw)
        rejection_totals["accepted_lines"] += len(accepted)
        observations.extend(accepted)
        tile_records.append({
            "parent_index": parent_index,
            "scan_kind": scan_kind,
            "proposal_xyxy": list(proposal),
            "tile_xyxy": list(tile),
            "elapsed_ms": elapsed,
            "raw_line_count": len(raw),
            "accepted_line_count": len(accepted),
            "raw_lines": raw,
            "rejections": reasons,
            "lines": accepted,
        })
    return full, full_ms, wide, tile_records, observations, rejection_totals


def main() -> int:
    """Evaluate adaptive full-screen segmentation and write diagnostic evidence."""
    args = parse_args()
    if args.replay_report is not None:
        return run_replay(args.replay_report, args.fixture, args.output_dir)
    if args.image is None:
        raise ValueError("--image is required unless --replay-report is provided.")
    if not args.image.is_file():
        raise FileNotFoundError(args.image)
    if (args.tile_padding_x < 0 or args.tile_padding_y < 0 or
            args.max_tiles_per_parent < 1):
        raise ValueError("Tile padding must be nonnegative and per-parent budget positive.")
    if args.max_tiles < 1 or not 0 <= args.min_confidence <= 1:
        raise ValueError("Invalid max-tiles or min-confidence.")
    if args.min_wide_px <= 0 or args.min_aspect_ratio <= 0:
        raise ValueError("Wide candidate thresholds must be positive.")
    with Image.open(args.image) as opened:
        image = opened.convert("RGB")
    output = args.output_dir or (
        args.image.parent / "adaptive-segmentation-reports" /
        datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    )
    output.mkdir(parents=True, exist_ok=False)
    from paddleocr import PaddleOCR
    import paddleocr
    import paddle

    options = {
        "lang": "ch", "device": "cpu", "enable_mkldnn": False,
        "use_doc_orientation_classify": False,
        "use_doc_unwarping": False, "use_textline_orientation": False,
    }
    if args.det_model:
        options["text_detection_model_name"] = args.det_model
    if args.rec_model:
        options["text_recognition_model_name"] = args.rec_model
    started = time.perf_counter()
    with contextlib.redirect_stdout(sys.stderr):
        ocr = PaddleOCR(**options)
    init_ms = round((time.perf_counter() - started) * 1000, 2)
    try:
        full, full_ms, wide, tile_records, observations, rejection_totals = scan_image(ocr, image, args, output)
        decisions = []
        final = list(full)
        for parent in wide:
            children, reason = choose_children(
                parent, observations, args.min_confidence
            )
            decisions.append({
                "parent_text": parent["text"],
                "parent_box": parent["box_xyxy"],
                "replaced": bool(children),
                "reason": reason,
                "children": children,
            })
            if children:
                final.remove(parent)
                final.extend(children)
        final = deduplicate(final)
        layers = resolve_layers(full, wide, tile_records)
        assessment = evaluate(layers["resolved_text_instances"], args.fixture, image.size)
        if assessment is not None:
            assessment["unresolved_merged_boxes"] = len(layers["unresolved_merged_boxes"])
            assessment["scope"] = "resolved_text_instances_only"
        report = {
            "image": str(args.image.resolve()), "image_size": list(image.size),
            "python_version": platform.python_version(),
            "paddleocr_version": getattr(paddleocr, "__version__", "unknown"),
            "paddle_version": getattr(paddle, "__version__", "unknown"),
            "model_options": options,
            "initialization_ms": init_ms,
            "full_screen_ms": full_ms,
            "full_screen_lines": full,
            "wide_candidates": wide,
            "tile_count": len(tile_records),
            "rescan_diagnostics": rejection_totals,
            "tiles": tile_records,
            "rescan_lines_deduplicated": deduplicate(observations),
            "replacement_decisions": decisions,
            "final_lines": final,
            **layers,
            "acceptance": assessment,
            "warning": (
                "Heuristic projection valleys do not guarantee UI boundaries. "
                "No OCR text bounding box is a verified click target."
            ),
        }
        annotate(image, full, output / "full-screen-annotated.png")
        annotate(image, deduplicate(observations),
                 output / "rescanned-annotated.png")
        annotate(image, layers["resolved_text_instances"], output / "final-annotated.png")
        (output / "adaptive-segmentation-report.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False),
            encoding="utf-8"
        )
        print(f"Full-screen lines: {len(full)}")
        print(f"Wide candidates: {len(wide)}")
        print(f"Adaptive rescan tiles: {len(tile_records)}")
        print(f"Rescan diagnostics: {json.dumps(rejection_totals)}")
        print(f"Rescanned unique lines: {len(deduplicate(observations))}")
        print(f"Resolved instances: {len(layers['resolved_text_instances'])}")
        print(f"Corroborated rescan instances: {len(layers['corroborated_rescan_instances'])}")
        for decision in decisions:
            print(f"  Parent: {decision['parent_text']!r} "
                  f"replaced={decision['replaced']} reason={decision['reason']}")
        if assessment is not None:
            print("Issue59 acceptance:",
                  json.dumps(assessment, ensure_ascii=False))
        print(f"Report: {output / 'adaptive-segmentation-report.json'}")
        return 0 if assessment is None or assessment["passed"] else 2
    finally:
        close = getattr(ocr, "close", None)
        if callable(close):
            close()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERROR: {type(exc).__name__}: {exc}", file=sys.stderr)
        raise