"""Run adaptive OCR for the .NET process bridge and emit one v1 envelope."""
import argparse
import contextlib
import json
import os
import sys
from types import SimpleNamespace

from verify_issue59_adaptive_text_segmentation import Image, resolve_layers, scan_image


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
