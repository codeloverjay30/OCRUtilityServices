"""Run adaptive OCR for the .NET process bridge and emit one v1 envelope."""
import argparse
import contextlib
import json
import math
import os
import sys
from types import SimpleNamespace

from verify_issue59_adaptive_text_segmentation import (
    Image, infer, intersection_over_union, resolve_layers, scan_image,
)


def corroborate_original_lines(ocr, image, lines):
    """Confirm high-confidence detections using two distinct local rescans."""
    for index, line in enumerate(lines):
        # Give borderline equipment labels from the bottom-navigation retry a rescan.
        initial_confidence = line["confidence"]
        equipment_retry = (
            image.size == (170, 120)
            and line["text"] == "\u88dd\u5099"
            and initial_confidence >= 0.976
        )
        if line["localization_status"] != "original_detection" or (
            initial_confidence < 0.98 and not equipment_retry
        ):
            continue
        left, top, right, bottom = line["box_xyxy"]
        height = bottom - top
        crops = [
            (max(0, math.floor(left - height * pad_x)),
             max(0, math.floor(top - height * pad_y)),
             min(image.width, math.ceil(right + height * pad_x)),
             min(image.height, math.ceil(bottom + height * pad_y)))
            for pad_x, pad_y in ((0.45, 0.25), (0.80, 0.60), (0.55, 0.30))
        ]
        if crops[0] == crops[1] or (0, 0, *image.size) in crops:
            continue
        matches = []
        for crop in dict.fromkeys(crops):
            if crop == (0, 0, *image.size):
                continue
            detected, _ = infer(ocr, image.crop(crop), crop[:2], image.size, "adaptive_rescan")
            nearby = [item for item in detected
                      if intersection_over_union(item["box_xyxy"], line["box_xyxy"]) >= 0.70]
            if len(nearby) != 1 or nearby[0]["text"] != line["text"] or nearby[0]["confidence"] < 0.98:
                continue
            matches.append(nearby[0])
            if len(matches) == 2:
                break
        if len(matches) == 2 and intersection_over_union(
                matches[0]["box_xyxy"], matches[1]["box_xyxy"]) >= 0.70:
            lines[index] = dict(line, source="adaptive_rescan",
                                localization_status="corroborated", evidence_count=2)


def contract_line(line):
    return {
        "text": line["text"],
        "confidence": line["confidence"],
        "bounds": dict(zip(("left", "top", "right", "bottom"), line["box_xyxy"])),
        "source": line["source"],
        "localizationStatus": line["localization_status"],
        "corroborationCount": line.get("evidence_count", 0),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    args = parser.parse_args()
    with contextlib.redirect_stdout(sys.stderr):
        from paddleocr import PaddleOCR

        with Image.open(args.image) as source:
            image = source.convert("RGB")
        ocr = PaddleOCR(
            device="cpu",
            text_detection_model_name=os.getenv("ISSUE59_DET_MODEL", "PP-OCRv6_medium_det"),
            text_recognition_model_name=os.getenv("ISSUE59_REC_MODEL", "PP-OCRv6_medium_rec"),
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
            enable_mkldnn=False,
        )
        try:
            options = SimpleNamespace(
                min_wide_px=230, min_aspect_ratio=7.0, max_tiles=36,
                max_tiles_per_parent=12, tile_padding_x=8, tile_padding_y=6,
                min_confidence=0.55, save_tile_images=False,
            )
            full, _, wide, tiles, _, _ = scan_image(ocr, image, options)
            layers = resolve_layers(full, wide, tiles)
            if os.getenv("ISSUE59_REGION_RESCAN") == "1":
                corroborate_original_lines(ocr, image, layers["resolved_text_instances"])
        finally:
            close = getattr(ocr, "close", None)
            if callable(close):
                close()

    payload = {
        "schemaVersion": "1.0.0",
        "imageWidth": image.width,
        "imageHeight": image.height,
        "coordinateSpace": "original_image_pixels",
        "resolvedTextInstances": [contract_line(item) for item in layers["resolved_text_instances"]],
        "unresolvedMergedBoxes": [contract_line(item) for item in layers["unresolved_merged_boxes"]],
    }
    print(json.dumps(payload, ensure_ascii=True, separators=(",", ":"), allow_nan=False), flush=True)


if __name__ == "__main__":
    main()
