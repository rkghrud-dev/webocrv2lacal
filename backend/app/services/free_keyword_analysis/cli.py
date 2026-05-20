from __future__ import annotations

import argparse
import json
import sys

from .analyzer import AnalysisConfig, analyze_image_root
from .result_writer import write_keyword_result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Create a free local OCR keyword analysis Excel from downloaded image folders."
    )
    parser.add_argument("--image-root", required=True, help="Export root or _ocr_tmp folder.")
    parser.add_argument("--output-dir", default="", help="Output directory. Defaults to export_root/keyword_analysis.")
    parser.add_argument("--tesseract-path", default="", help="tesseract.exe path or Tesseract install folder.")
    parser.add_argument("--lang", default="kor+eng")
    parser.add_argument("--psm", type=int, default=11)
    parser.add_argument("--oem", type=int, default=3)
    parser.add_argument("--max-detail-images", type=int, default=999)
    parser.add_argument("--ocr-listing-images", action="store_true")
    parser.add_argument("--upload-file", default="", help="Optional upload workbook to fill with V4 local keywords.")
    parser.add_argument("--keyword-output-dir", default="", help="Optional directory for the filled keyword workbook.")
    args = parser.parse_args(argv)

    cfg = AnalysisConfig(
        image_root=args.image_root,
        output_dir=args.output_dir,
        tesseract_path=args.tesseract_path,
        lang=args.lang,
        psm=args.psm,
        oem=args.oem,
        max_detail_images=args.max_detail_images,
        ocr_listing_images=bool(args.ocr_listing_images),
    )
    out_path = analyze_image_root(cfg, status_cb=lambda message: print(message, flush=True))
    keyword_result = ""
    if args.upload_file:
        print("V4 로컬 키워드 초안 엑셀 생성 중...", flush=True)
        keyword_result = write_keyword_result(
            upload_file=args.upload_file,
            analysis_excel=out_path,
            output_dir=args.keyword_output_dir,
        )
        print(f"V4 로컬 키워드 초안 저장: {keyword_result}", flush=True)
    print(json.dumps({"output_file": out_path, "keyword_result_file": keyword_result}, ensure_ascii=False), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
