from __future__ import annotations

import os
import re
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
import requests

from .env_loader import ensure_env_loaded, get_env, key_file_candidates


def _app_root() -> str:
    here = os.path.abspath(os.path.dirname(__file__))
    return os.path.abspath(os.path.join(here, '..', '..'))


def load_shopping_keys(path: str = "naver_shopping_api_key.txt") -> dict:
    keys = {
        "CLIENT_ID": "",
        "CLIENT_SECRET": "",
    }

    ensure_env_loaded()
    keys["CLIENT_ID"] = get_env("NAVER_SHOPPING_CLIENT_ID", "NAVER_CLIENT_ID")
    keys["CLIENT_SECRET"] = get_env("NAVER_SHOPPING_CLIENT_SECRET", "NAVER_CLIENT_SECRET")

    candidates = key_file_candidates(path)
    file_path = next((p for p in candidates if os.path.isfile(p)), None)
    if not file_path:
        return keys

    with open(file_path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if "=" in line:
                k, v = line.split("=", 1)
                k = k.strip().upper()
                v = v.strip().strip('"').strip("'")
                if k in keys and not keys[k]:
                    keys[k] = v
    return keys


def _strip_html(text: str) -> str:
    text = re.sub(r"<[^>]+>", " ", text or "")
    text = re.sub(r"\s+", " ", text).strip()
    return text


def _compact(text: str) -> str:
    return re.sub(r"\s+", "", text or "").lower()


def _category_path(item: dict) -> str:
    return " > ".join(
        str(item.get(f"category{i}") or "").strip()
        for i in range(1, 5)
        if str(item.get(f"category{i}") or "").strip()
    )


def _category_reference_path() -> Path:
    return Path(_app_root()).parent / "data" / "category_reference" / "naver_categories.csv"


def _load_naver_category_ids() -> dict[str, str]:
    path = _category_reference_path()
    if not path.is_file():
        return {}
    out: dict[str, str] = {}
    with path.open("r", encoding="utf-8-sig") as f:
        header = f.readline().strip().split(",")
        try:
            code_idx = header.index("category_code")
            path_idx = header.index("full_path")
        except ValueError:
            return {}
        for line in f:
            cols = line.rstrip("\n").split(",")
            if len(cols) <= max(code_idx, path_idx):
                continue
            full_path = cols[path_idx].replace(">", " > ").strip()
            full_path = re.sub(r"\s*>\s*", " > ", full_path)
            code = cols[code_idx].strip()
            if full_path and code:
                out[full_path] = code
    return out


@dataclass(frozen=True)
class NaverCategoryAnchor:
    category_id: str
    path: str
    weighted_score: float
    count: int
    count_ratio: float
    first_rank: int
    confidence: float
    method: str
    needs_review: bool
    reason: str


NAVER_CATEGORY_INTENT_TERMS = (
    "홀커버", "홀마감", "홀마개", "구멍마개", "구멍커버", "마감캡", "마감재", "점검구",
    "커버", "마개", "캡", "덮개", "브라켓", "손잡이", "경첩", "받침", "다리",
    "앙카", "앵커", "나사", "피스", "볼트", "너트", "스티커", "클립", "후크",
)


def _title_intent_boost(query: str, title: str, path: str) -> float:
    compact_query = _compact(query)
    compact_title = _compact(title)
    compact_path = _compact(path)
    boost = 1.0

    matched_terms = [
        term for term in NAVER_CATEGORY_INTENT_TERMS
        if term in compact_query and term in compact_title
    ]
    if matched_terms:
        boost += min(0.8, 0.22 * len(matched_terms))

    if "홀" in compact_query and any(term in compact_title for term in ("홀커버", "홀마감", "구멍마개", "마감캡", "점검구")):
        boost += 0.55

    if any(term in compact_query for term in ("홀마감", "홀커버", "구멍마개", "마감캡", "점검구")):
        if "조명" in compact_path and any(term in compact_title for term in ("홀커버", "구멍마개", "마감캡", "점검구", "마개", "커버")):
            boost *= 0.28

    return max(0.05, boost)


def naver_shopping_items(query: str,
                         client_id: str,
                         client_secret: str,
                         display: int = 100,
                         timeout: int = 10) -> list[dict]:
    if not query:
        return []
    if not client_id or not client_secret:
        raise RuntimeError("네이버 쇼핑 API 키가 없습니다. .env(NAVER_SHOPPING_CLIENT_ID/NAVER_SHOPPING_CLIENT_SECRET) 또는 naver_shopping_api_key.txt를 확인하세요.")

    headers = {
        "X-Naver-Client-Id": client_id,
        "X-Naver-Client-Secret": client_secret,
    }
    params = {
        "query": query,
        "display": max(1, min(100, int(display))),
        "start": 1,
        "sort": "sim",
    }
    r = requests.get("https://openapi.naver.com/v1/search/shop.json",
                     headers=headers, params=params, timeout=timeout)
    r.raise_for_status()
    return list(r.json().get("items", []))


def select_naver_category_anchor(query: str,
                                 items: list[dict],
                                 category_ids: dict[str, str] | None = None) -> NaverCategoryAnchor | None:
    if not items:
        return None

    category_ids = category_ids if category_ids is not None else _load_naver_category_ids()
    scores: dict[str, float] = defaultdict(float)
    counts: dict[str, int] = defaultdict(int)
    first_rank: dict[str, int] = {}

    for rank, item in enumerate(items, 1):
        path = _category_path(item)
        if not path:
            continue
        title = _strip_html(str(item.get("title") or ""))
        first_rank.setdefault(path, rank)
        counts[path] += 1

        rank_weight = 1.0 / (rank ** 0.65)
        scores[path] += rank_weight * _title_intent_boost(query, title, path)

    if not scores:
        return None

    ranked = sorted(scores.items(), key=lambda pair: (-pair[1], first_rank.get(pair[0], 9999), pair[0]))
    best_path, best_score = ranked[0]
    second_score = ranked[1][1] if len(ranked) > 1 else 0.0
    total_score = sum(scores.values()) or 1.0
    confidence = best_score / total_score
    margin = best_score - second_score
    count = counts.get(best_path, 0)
    count_ratio = count / max(1, len(items))
    first = first_rank.get(best_path, 9999)

    needs_review = first > 20 or confidence < 0.30 or margin < 0.80
    reason = "rank_weighted"
    if needs_review:
        reason = f"review:first_rank={first}, confidence={confidence:.3f}, margin={margin:.3f}"

    return NaverCategoryAnchor(
        category_id=category_ids.get(best_path, ""),
        path=best_path,
        weighted_score=round(best_score, 6),
        count=count,
        count_ratio=round(count_ratio, 6),
        first_rank=first,
        confidence=round(confidence, 6),
        method="rank_weighted_title_intent",
        needs_review=needs_review,
        reason=reason,
    )


def naver_shopping_titles(query: str,
                          client_id: str,
                          client_secret: str,
                          pages: int = 3,
                          display: int = 40,
                          timeout: int = 10) -> list[str]:
    if not query:
        return []
    if not client_id or not client_secret:
        raise RuntimeError("네이버 쇼핑 API 키가 없습니다. .env(NAVER_SHOPPING_CLIENT_ID/NAVER_SHOPPING_CLIENT_SECRET) 또는 naver_shopping_api_key.txt를 확인하세요.")

    headers = {
        "X-Naver-Client-Id": client_id,
        "X-Naver-Client-Secret": client_secret,
    }

    titles: list[str] = []
    pages = max(1, min(5, int(pages)))
    display = max(1, min(100, int(display)))

    for p in range(pages):
        start = 1 + p * display
        if start > 1000:
            break
        params = {
            "query": query,
            "display": display,
            "start": start,
            "sort": "sim",
        }
        r = requests.get("https://openapi.naver.com/v1/search/shopping.json",
                         headers=headers, params=params, timeout=timeout)
        r.raise_for_status()
        data = r.json()
        for item in data.get("items", []):
            title = _strip_html(item.get("title", ""))
            if title:
                titles.append(title)
    return titles

