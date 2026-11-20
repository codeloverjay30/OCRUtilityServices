#!/usr/bin/env python3
"""Deterministic process fixture; never performs OCR or Appium actions."""
import argparse
import json
from PIL import Image

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--image",required=True)
    args=parser.parse_args()
    with Image.open(args.image) as image:
        width,height=image.size
    if (width,height)!=(2048,945):
        raise ValueError("Fixture requires 2048x945 input")
    envelope=dict(schemaVersion="1.0.0",imageWidth=width,imageHeight=height,
        coordinateSpace="original_image_pixels",
        resolvedTextInstances=[dict(text="限時禮包",confidence=0.9992184638977051,
            bounds=dict(left=1820,top=244,right=1921,bottom=271),
            source="adaptive_rescan",localizationStatus="corroborated",
            corroborationCount=2)],
        unresolvedMergedBoxes=[dict(text="外觀基金特惠禮包祈福祭壇零元基金活動日曆玉虛奪寶限時禮包",
            confidence=0.9,bounds=dict(left=1163,top=243,right=1919,bottom=270),
            source="full_screen",localizationStatus="ambiguous_merged_text",
            corroborationCount=0)])
    print(json.dumps(envelope,ensure_ascii=False,separators=(",",":")))

if __name__=="__main__":
    main()
