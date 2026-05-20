from __future__ import annotations

import os
import re
from dataclasses import asdict, dataclass
from datetime import datetime
from pathlib import Path

import pandas as pd
import pytesseract
from PIL import Image, ImageFilter, ImageOps, UnidentifiedImageError

from .text_rules import (
    ProductFields,
    clean_ocr_text,
    generate_keyword_variants,
    infer_product_fields,
    is_noise_only_text,
)


IMAGE_EXTS = {".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff"}


@dataclass
class AnalysisConfig:
    image_root: str
    output_dir: str = ""
    tesseract_path: str = ""
    lang: str = "kor+eng"
    psm: int = 11
    oem: int = 3
    max_detail_images: int = 999
    ocr_listing_images: bool = False


def _noop_status(_message: str) -> None:
    return None


@dataclass
class ImageRecord:
    gs_code: str
    file_name: str
    image_type: str
    index: int
    ocr_text: str
    extracted_features: str
    image_role: str
    main_candidate_score: int
    exclude_reason: str
    path: str


def analyze_image_root(cfg: AnalysisConfig, status_cb=None) -> str:
    status = status_cb or _noop_status
    root = _resolve_image_root(cfg.image_root)
    output_dir = Path(cfg.output_dir).resolve() if cfg.output_dir else root.parent / "keyword_analysis"
    output_dir.mkdir(parents=True, exist_ok=True)

    _setup_tesseract(cfg.tesseract_path)

    summary_rows: list[dict] = []
    image_rows: list[dict] = []

    folders = [p for p in root.iterdir() if p.is_dir()]
    folders.sort(key=lambda p: p.name.lower())

    for folder_i, folder in enumerate(folders, start=1):
        gs_code = _extract_gs_code(folder.name) or folder.name
        detail_files, listing_files = _split_folder_images(folder)
        status(f"[{folder_i}/{len(folders)}] {gs_code} 분석 시작 - 상세 {len(detail_files)}장, 대표/추가 {len(listing_files)}장")
        if cfg.max_detail_images > 0:
            detail_files = detail_files[: cfg.max_detail_images]

        ocr_parts: list[str] = []
        for path in detail_files:
            text = _ocr_image(path, cfg)
            clean = clean_ocr_text(text)
            exclude_reason = "공통 안내/배송 이미지" if is_noise_only_text(clean) else ""
            if clean and not exclude_reason:
                ocr_parts.append(clean)
            image_rows.append(
                asdict(
                    ImageRecord(
                        gs_code=gs_code,
                        file_name=path.name,
                        image_type="detail",
                        index=_image_index(path),
                        ocr_text=clean,
                        extracted_features=_short_features(clean),
                        image_role="상세 OCR 대상",
                        main_candidate_score=0,
                        exclude_reason=exclude_reason,
                        path=str(path),
                    )
                )
            )

        if cfg.ocr_listing_images:
            for path in listing_files:
                text = clean_ocr_text(_ocr_image(path, cfg))
                image_rows.append(
                    asdict(
                        ImageRecord(
                            gs_code=gs_code,
                            file_name=path.name,
                            image_type="listing",
                            index=_image_index(path),
                            ocr_text=text,
                            extracted_features=_short_features(text),
                            image_role="대표/추가 이미지 후보",
                            main_candidate_score=_listing_candidate_score(path),
                            exclude_reason="",
                            path=str(path),
                        )
                    )
                )
        else:
            for path in listing_files:
                image_rows.append(
                    asdict(
                        ImageRecord(
                            gs_code=gs_code,
                            file_name=path.name,
                            image_type="listing",
                            index=_image_index(path),
                            ocr_text="",
                            extracted_features="대표/추가 후보 이미지",
                            image_role="대표/추가 이미지 후보",
                            main_candidate_score=_listing_candidate_score(path),
                            exclude_reason="",
                            path=str(path),
                        )
                    )
                )

        fields = infer_product_fields(
            gs_code=gs_code,
            ocr_text=" ".join(ocr_parts),
            detail_count=len(detail_files),
            listing_count=len(listing_files),
        )
        variants = generate_keyword_variants(fields, count=5)
        summary_rows.append(_summary_row(fields, len(detail_files), len(listing_files), variants))
        status(f"[{folder_i}/{len(folders)}] {gs_code} 분석 완료 - 신뢰도 {fields.confidence}, 상품정체성: {fields.product_identity or '미확정'}")

    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_path = output_dir / f"무료키워드분석_{root.name}_{ts}.xlsx"
    _write_excel(summary_rows, image_rows, out_path)
    status(f"V4 분석 엑셀 저장: {out_path}")
    return str(out_path)


def _resolve_image_root(path: str) -> Path:
    root = Path(path).resolve()
    if not root.exists():
        raise FileNotFoundError(f"image root not found: {root}")
    if (root / "_ocr_tmp").is_dir():
        return root / "_ocr_tmp"
    return root


def _setup_tesseract(custom_path: str = "") -> str:
    candidates = []
    if custom_path:
        p = Path(custom_path)
        candidates.append(p / "tesseract.exe" if p.is_dir() else p)
    candidates.extend(
        [
            Path(r"C:\Program Files\Tesseract-OCR\tesseract.exe"),
            Path(r"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"),
        ]
    )
    for path in candidates:
        if path.is_file():
            pytesseract.pytesseract.tesseract_cmd = str(path)
            return str(path)
    return ""


def _split_folder_images(folder: Path) -> tuple[list[Path], list[Path]]:
    files = [p for p in folder.iterdir() if p.is_file() and p.suffix.lower() in IMAGE_EXTS]
    detail = [p for p in files if p.stem.isdigit()]
    listing = [p for p in files if _is_listing_image(p)]
    detail.sort(key=_image_index)
    listing.sort(key=_image_index)
    return detail, listing


def _is_listing_image(path: Path) -> bool:
    return bool(re.search(r"GS\d{7}[A-Z]?\d+$", path.stem, flags=re.IGNORECASE))


def _image_index(path: Path) -> int:
    stem = path.stem
    if stem.isdigit():
        return int(stem)
    m = re.search(r"(\d+)$", stem)
    return int(m.group(1)) if m else 0


def _extract_gs_code(value: str) -> str:
    m = re.search(r"(GS\d{7}[A-Z]?)", str(value or ""), flags=re.IGNORECASE)
    return m.group(1).upper() if m else ""


def _ocr_image(path: Path, cfg: AnalysisConfig) -> str:
    try:
        with Image.open(path) as img:
            g = ImageOps.grayscale(img.convert("RGB"))
            w, h = g.size
            if w < 1200:
                scale = 2
                g = g.resize((w * scale, h * scale), Image.Resampling.LANCZOS)
            elif w > 2200:
                scale = 2200 / float(w)
                g = g.resize((int(w * scale), int(h * scale)), Image.Resampling.LANCZOS)
            g = ImageOps.autocontrast(g, cutoff=1)
            g = g.filter(ImageFilter.UnsharpMask(radius=1.2, percent=140, threshold=3))
            config = f"--psm {int(cfg.psm)} --oem {int(cfg.oem)}"
            return pytesseract.image_to_string(g, lang=cfg.lang, config=config)
    except (UnidentifiedImageError, OSError, RuntimeError, ValueError):
        return ""


def _short_features(text: str) -> str:
    text = clean_ocr_text(text)
    features = []
    for term in ["슈링클", "A4", "열수축", "반투명", "투명", "키링", "네임택", "굿즈", "오븐", "펀칭", "채색"]:
        if term in text:
            features.append(term)
    return ", ".join(features)


def _listing_candidate_score(path: Path) -> int:
    idx = _image_index(path)
    if idx <= 0:
        return 50
    return max(50, 90 - ((idx - 1) * 6))


def _summary_row(fields: ProductFields, detail_count: int, listing_count: int, variants: list[str]) -> dict:
    row = {
        "GS코드": fields.gs_code,
        "상품정체성": fields.product_identity,
        "대표검색어": ", ".join(fields.representative_terms),
        "다른명칭": ", ".join(fields.aliases),
        "규격": ", ".join(fields.specs),
        "옵션": ", ".join(fields.options),
        "소재": ", ".join(fields.material),
        "핵심특징": ", ".join(fields.features),
        "사용처": ", ".join(fields.use_cases),
        "사용자대상": ", ".join(fields.target_users),
        "제작도구": ", ".join(fields.tools),
        "제외문구": ", ".join(fields.exclusions),
        "OCR정제문": fields.ocr_clean_text[:1500],
        "이미지관찰요약": fields.image_summary,
        "키워드소스문장": fields.keyword_source,
        "상세이미지수": detail_count,
        "대표이미지수": listing_count,
        "분석신뢰도": fields.confidence,
    }
    for i in range(5):
        row[f"추천키워드{i + 1}"] = variants[i] if i < len(variants) else ""
    return row


def _write_excel(summary_rows: list[dict], image_rows: list[dict], out_path: Path) -> None:
    with pd.ExcelWriter(out_path, engine="openpyxl") as writer:
        pd.DataFrame(summary_rows).to_excel(writer, sheet_name="상품요약", index=False)
        pd.DataFrame(image_rows).to_excel(writer, sheet_name="이미지상세", index=False)

        meta = pd.DataFrame(
            [
                {"항목": "생성방식", "값": "로컬 Tesseract OCR + 규칙 기반 키워드 분석"},
                {"항목": "GoogleOCR사용", "값": "아니오"},
                {"항목": "생성일시", "값": datetime.now().strftime("%Y-%m-%d %H:%M:%S")},
            ]
        )
        meta.to_excel(writer, sheet_name="메타데이터", index=False)
