from __future__ import annotations

import argparse
import json
from datetime import datetime
from pathlib import Path
from typing import Any


MARKET_CATEGORY_KEYS = {
    "naver": "naver",
    "coupang": "coupang",
    "esm": "esm",
    "11st": "11st",
    "elevenst": "11st",
    "lotteon": "lotte_standard",
    "lotte_standard": "lotte_standard",
    "lotte_display": "lotte_display",
    "lotte_item": "lotte_item",
}


def text(value: Any) -> str:
    return "" if value is None else str(value).strip()


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def dump_json(path: Path, payload: Any) -> None:
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def normalize_override_payload(payload: Any) -> dict[str, dict[str, dict[str, str]]]:
    if not isinstance(payload, dict):
        return {}
    out: dict[str, dict[str, dict[str, str]]] = {}
    for gs, markets in payload.items():
        if not isinstance(markets, dict):
            continue
        gs_key = text(gs).upper()
        for market, item in markets.items():
            if not isinstance(item, dict):
                continue
            code = text(item.get("code") or item.get("categoryId") or item.get("target_category_id"))
            path = text(item.get("path") or item.get("categoryPath") or item.get("target_path"))
            if not code and not path:
                continue
            out.setdefault(gs_key, {})[text(market).lower()] = {
                "code": code,
                "path": path,
                "source": "manual_override",
                "savedAt": text(item.get("savedAt")),
            }
    return out


def best_candidate(row: dict[str, Any], market: str) -> dict[str, Any] | None:
    candidates = row.get("candidateMappings", {}).get(market)
    if isinstance(candidates, list) and candidates:
        for candidate in candidates:
            if isinstance(candidate, dict) and text(candidate.get("target_category_id")):
                return candidate
    mappings = row.get("mappings", {})
    candidate = mappings.get(market) if isinstance(mappings, dict) else None
    return candidate if isinstance(candidate, dict) else None


def candidate_status(candidate: dict[str, Any] | None) -> str:
    if not candidate:
        return "no_match"
    score = float(candidate.get("score") or candidate.get("confidence_score") or 0)
    method = text(candidate.get("match_method"))
    if score >= 0.75 and method not in {"manual_review", "no_match_after_exclusion_filter"}:
        return "auto"
    if score >= 0.55:
        return "review"
    return "review"


def apply_mapping_to_product(
    product: dict[str, Any],
    report_row: dict[str, Any],
    overrides: dict[str, dict[str, dict[str, str]]],
) -> dict[str, Any]:
    gs = text(product.get("gs") or report_row.get("gs")).upper()
    categories = product.get("categories") if isinstance(product.get("categories"), dict) else {}
    categories = dict(categories)
    labels = categories.get("labels") if isinstance(categories.get("labels"), dict) else {}
    labels = dict(labels)
    category_paths = product.get("categoryPaths") if isinstance(product.get("categoryPaths"), dict) else {}
    category_paths = dict(category_paths)
    review = product.get("marketCategoryReview") if isinstance(product.get("marketCategoryReview"), dict) else {}
    review = dict(review)
    override_for_gs = overrides.get(gs, {})
    changed: dict[str, Any] = {}
    needs_review: list[str] = []

    naver = report_row.get("naver") if isinstance(report_row.get("naver"), dict) else {}
    naver_code = text(naver.get("id"))
    if naver_code:
        categories["naver"] = naver_code
        naver_path = text(naver.get("path"))
        if naver_path:
            labels["naver"] = naver_path
            categories["naverPath"] = naver_path
            category_paths["naver"] = naver_path
        review["naver"] = {
            "code": naver_code,
            "path": naver_path,
            "method": text(naver.get("method")),
            "confidence": naver.get("confidence"),
            "firstRank": naver.get("firstRank"),
            "needsReview": bool(naver.get("needsReview")),
        }
        if naver.get("needsReview"):
            needs_review.append("naver")

    for market in ("coupang", "esm", "11st", "lotteon"):
        override = override_for_gs.get(market)
        candidate = best_candidate(report_row, market)
        category_key = MARKET_CATEGORY_KEYS[market]
        if override:
            code = override["code"]
            path = override["path"]
            categories[category_key] = code
            if path:
                labels[category_key] = path
                categories[f"{category_key}Path"] = path
                category_paths[category_key] = path
            review[market] = {
                "code": code,
                "path": path,
                "method": "manual_override",
                "needsReview": False,
                "savedAt": override.get("savedAt", ""),
            }
            changed[market] = "manual_override"
            continue
        if not candidate:
            needs_review.append(market)
            continue
        code = text(candidate.get("target_category_id"))
        path = text(candidate.get("target_path"))
        if code:
            categories[category_key] = code
        if path:
            labels[category_key] = path
            categories[f"{category_key}Path"] = path
            category_paths[category_key] = path
        status = candidate_status(candidate)
        review[market] = {
            "code": code,
            "path": path,
            "method": text(candidate.get("match_method")),
            "score": candidate.get("score", candidate.get("confidence_score")),
            "source": text(candidate.get("source")),
            "needsReview": status != "auto",
        }
        changed[market] = status
        if status != "auto":
            needs_review.append(market)

    if labels:
        categories["labels"] = labels
    product["categories"] = categories
    if category_paths:
        product["categoryPaths"] = category_paths
    product["marketCategoryReview"] = {
        **review,
        "updatedAt": datetime.now().isoformat(timespec="seconds"),
        "needsReviewMarkets": sorted(set(needs_review)),
    }
    return changed


def main() -> int:
    parser = argparse.ArgumentParser(description="Apply category review candidates/overrides to a WEBOCR seed file.")
    parser.add_argument("--seed", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--overrides", type=Path, help="category_review_overrides.json downloaded from the review HTML")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()

    seed = load_json(args.seed)
    report_rows = load_json(args.report)
    if not isinstance(seed, dict) or not isinstance(seed.get("products"), list):
        raise SystemExit("seed products not found")
    if not isinstance(report_rows, list):
        raise SystemExit("report rows must be a list")

    overrides = normalize_override_payload(load_json(args.overrides)) if args.overrides and args.overrides.is_file() else {}
    report_by_gs = {text(row.get("gs")).upper(): row for row in report_rows if isinstance(row, dict)}
    applied: dict[str, Any] = {}
    for product in seed["products"]:
        if not isinstance(product, dict):
            continue
        gs = text(product.get("gs")).upper()
        row = report_by_gs.get(gs)
        if not row:
            continue
        applied[gs] = apply_mapping_to_product(product, row, overrides)

    seed["categoryMatchingPipeline"] = {
        "schema": "webocr.categoryMatching.v1",
        "updatedAt": datetime.now().isoformat(timespec="seconds"),
        "report": str(args.report),
        "overrides": str(args.overrides) if args.overrides else "",
        "rule": "Naver rank-weighted anchor -> market top candidates -> manual override first -> low score remains review-gated",
        "excludedCategories": ["children", "baby", "brand", "overseas", "book"],
        "appliedProducts": len(applied),
    }
    out = args.out or args.seed.with_name(args.seed.stem + "_category_applied.webseed.json")
    dump_json(out, seed)
    print(out)
    print(json.dumps({"appliedProducts": len(applied), "overrides": sum(len(v) for v in overrides.values())}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
