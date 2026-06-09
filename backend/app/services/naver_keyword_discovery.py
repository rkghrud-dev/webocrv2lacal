from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import re
import time
from pathlib import Path
from urllib.parse import urlencode

import requests

from .env_loader import ensure_env_loaded, get_env, key_file_candidates


SEARCHAD_BASE_URL = "https://api.searchad.naver.com"
SEARCHAD_TIMEOUT = 8
NAVER_AUTOCOMPLETE_URL = "https://ac.search.naver.com/nx/ac"
AUTOCOMPLETE_TIMEOUT = 5

STOPWORDS = {
    "무료배송", "국내배송", "해외배송", "당일배송", "오늘출발", "정품", "신상", "특가",
    "세일", "할인", "추천", "인기", "베스트", "최저가", "리뷰", "후기", "문의",
    "상담", "옵션", "선택", "랜덤", "상품", "제품", "세트", "1개", "1p", "1P",
}
BROAD_HINT_TERMS = {
    "pp", "PP", "코튼", "면", "스텐", "스테인리스", "스테인레스", "실리콘", "원형",
    "사각", "대형", "소형", "중형", "미니", "투명", "고급", "교체", "정리", "고정",
}


def _text(value: object) -> str:
    return str(value or "").strip()


def _compact_keyword(value: object) -> str:
    text = re.sub(r"<[^>]+>", " ", _text(value))
    text = re.sub(r"[\[\]{}()\"'`~!@#$%^&*_+=|\\:;,.?/<>]", " ", text)
    text = re.sub(r"\s+", " ", text).strip()
    return text


def _tag(value: object) -> str:
    return re.sub(r"\s+", "", _compact_keyword(value))


def _parse_count(value: object) -> int:
    raw = _text(value).replace(",", "")
    if not raw:
        return 0
    if raw.startswith("<"):
        return 5
    try:
        return int(float(raw))
    except Exception:
        return 0


def _parse_float(value: object) -> float:
    try:
        return float(_text(value).replace(",", ""))
    except Exception:
        return 0.0


def _tokenize(value: object) -> list[str]:
    clean = re.sub(r"GS\d{4,}[A-Z0-9]*", " ", _text(value), flags=re.IGNORECASE)
    clean = re.sub(r"[^0-9A-Za-z가-힣\s]", " ", clean)
    tokens: list[str] = []
    for token in re.split(r"\s+", clean):
        token = token.strip()
        if len(token) < 2 or token in STOPWORDS or token.isdigit():
            continue
        if token not in tokens:
            tokens.append(token)
    return tokens[:24]


def build_hint_keywords(base_text: str, *extra_texts: str) -> list[str]:
    hints: list[str] = []
    for source in (base_text, *extra_texts):
        for token in _tokenize(source):
            compact = _tag(token)
            if not (2 <= len(compact) <= 20):
                continue
            if compact not in hints:
                hints.append(compact)
            if len(hints) >= 5:
                return hints
    return hints


def _keyword_variants(keyword: str) -> list[str]:
    variants = [_tag(keyword)]
    if "라이타" in variants[0]:
        variants.append(variants[0].replace("라이타", "라이터"))
    if "라이터" in variants[0]:
        variants.append(variants[0].replace("라이터", "라이타"))
    for suffix in ("솜", "용품", "부품", "장치"):
        for item in list(variants):
            if item.endswith(suffix) and len(item) > len(suffix) + 1:
                variants.append(item[: -len(suffix)])
    return [item for item in dict.fromkeys(variants) if item]


def _matched_hints(keyword: object, hints: list[str]) -> list[str]:
    target_variants = _keyword_variants(_text(keyword))
    hint_variants: list[str] = []
    for hint in hints:
        hint_variants.extend(_keyword_variants(hint))
    hint_variants = [item for item in dict.fromkeys(hint_variants) if len(item) >= 2]
    if not target_variants or not hint_variants:
        return []
    matched: list[str] = []
    for target in target_variants:
        if len(target) < 2 or target in STOPWORDS:
            continue
        for hint in hint_variants:
            if target == hint or target in hint or hint in target:
                matched.append(hint)
            if len(hint) >= 3 and len(target) >= 3 and hint[:3] == target[:3]:
                matched.append(hint)
    return list(dict.fromkeys(matched))


def _is_related(keyword: object, hints: list[str]) -> bool:
    matched = _matched_hints(keyword, hints)
    if not matched:
        return False
    non_broad = [hint for hint in matched if hint not in BROAD_HINT_TERMS and hint.lower() not in BROAD_HINT_TERMS]
    return bool(non_broad) or len(matched) >= 2


def _read_json_file(path: Path) -> dict:
    try:
        if path.is_file():
            return json.loads(path.read_text(encoding="utf-8-sig"))
    except Exception:
        return {}
    return {}


def _read_pairs_file(path: Path) -> dict[str, str]:
    out: dict[str, str] = {}
    try:
        if not path.is_file():
            return out
        for raw in path.read_text(encoding="utf-8-sig").splitlines():
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            match = re.match(r"^([^:=\s]+)\s*[:=]\s*(.+)$", line)
            if not match:
                continue
            out[match.group(1).strip().lower()] = match.group(2).strip().strip("\"'")
    except Exception:
        return out
    return out


def load_searchad_keys() -> dict[str, str]:
    ensure_env_loaded()
    keys = {
        "apiKey": get_env("NAVER_SEARCHAD_API_KEY", "SEARCHAD_API_KEY"),
        "secretKey": get_env("NAVER_SEARCHAD_SECRET_KEY", "SEARCHAD_SECRET_KEY"),
        "customerId": get_env("NAVER_SEARCHAD_CUSTOMER_ID", "SEARCHAD_CUSTOMER_ID"),
    }

    for path_text in key_file_candidates("navertagv2.keys.json"):
        data = _read_json_file(Path(path_text))
        search_ad = data.get("searchAd") if isinstance(data.get("searchAd"), dict) else {}
        keys["apiKey"] = keys["apiKey"] or _text(search_ad.get("apiKey"))
        keys["secretKey"] = keys["secretKey"] or _text(search_ad.get("secretKey"))
        keys["customerId"] = keys["customerId"] or _text(search_ad.get("customerId"))

    for path_text in key_file_candidates("naver_api_key.txt"):
        pairs = _read_pairs_file(Path(path_text))
        keys["apiKey"] = keys["apiKey"] or pairs.get("naver_searchad_api_key") or pairs.get("searchad_api_key") or pairs.get("api_key") or pairs.get("access_license") or ""
        keys["secretKey"] = keys["secretKey"] or pairs.get("naver_searchad_secret_key") or pairs.get("searchad_secret_key") or pairs.get("secret_key") or ""
        keys["customerId"] = keys["customerId"] or pairs.get("naver_searchad_customer_id") or pairs.get("searchad_customer_id") or pairs.get("customer_id") or ""

    return keys


def _searchad_headers(keys: dict[str, str], method: str, path: str) -> dict[str, str]:
    timestamp = str(int(time.time() * 1000))
    message = f"{timestamp}.{method}.{path}"
    signature = base64.b64encode(
        hmac.new(keys["secretKey"].encode("utf-8"), message.encode("utf-8"), hashlib.sha256).digest()
    ).decode("utf-8")
    return {
        "X-Timestamp": timestamp,
        "X-API-KEY": keys["apiKey"],
        "X-Customer": keys["customerId"],
        "X-Signature": signature,
    }


def searchad_keyword_tool(hints: list[str]) -> dict[str, object]:
    keys = load_searchad_keys()
    if not (keys.get("apiKey") and keys.get("secretKey") and keys.get("customerId")):
        return {"ok": False, "reason": "searchad_key_missing", "keywords": []}
    hints = [hint for hint in hints if 2 <= len(_tag(hint)) <= 20][:5]
    if not hints:
        return {"ok": False, "reason": "hint_empty", "keywords": []}
    method = "GET"
    path = "/keywordstool"
    params = urlencode({"hintKeywords": ",".join(hints), "showDetail": "1"})
    url = f"{SEARCHAD_BASE_URL}{path}?{params}"
    try:
        response = requests.get(url, headers=_searchad_headers(keys, method, path), timeout=SEARCHAD_TIMEOUT)
        if response.status_code >= 400:
            return {"ok": False, "reason": f"searchad_http_{response.status_code}", "keywords": []}
        items = response.json().get("keywordList") or []
    except Exception as exc:
        return {"ok": False, "reason": _text(exc)[:200], "keywords": []}

    out: list[dict[str, object]] = []
    for item in items:
        keyword = _tag(item.get("relKeyword") or item.get("keyword"))
        if len(keyword) < 2 or keyword in STOPWORDS:
            continue
        pc = _parse_count(item.get("monthlyPcQcCnt"))
        mobile = _parse_count(item.get("monthlyMobileQcCnt"))
        clicks = _parse_float(item.get("monthlyAvePcClkCnt")) + _parse_float(item.get("monthlyAveMobileClkCnt"))
        comp = _text(item.get("compIdx"))
        comp_score = 10 if comp == "low" else 6 if comp == "mid" else 1 if comp == "high" else 4
        score = min(60.0, (pc + mobile) ** 0.35) + min(20.0, clicks ** 0.5) + comp_score
        out.append({
            "keyword": keyword,
            "monthlyPcQcCnt": pc,
            "monthlyMobileQcCnt": mobile,
            "monthlyTotalQcCnt": pc + mobile,
            "monthlyAvePcClkCnt": _parse_float(item.get("monthlyAvePcClkCnt")),
            "monthlyAveMobileClkCnt": _parse_float(item.get("monthlyAveMobileClkCnt")),
            "compIdx": comp,
            "score": round(score, 4),
            "source": "naver_searchad_keywordstool",
        })
    out.sort(key=lambda x: (-int(x.get("monthlyTotalQcCnt") or 0), -float(x.get("score") or 0), _text(x.get("keyword"))))
    return {"ok": True, "reason": "", "keywords": out}


def naver_autocomplete(query: str, max_results: int = 12) -> list[str]:
    query = _compact_keyword(query)
    if not query:
        return []
    params = {
        "q": query,
        "con": "1",
        "frm": "nv",
        "ans": "2",
        "r_format": "json",
        "r_enc": "UTF-8",
        "q_enc": "UTF-8",
        "st": "100",
    }
    try:
        response = requests.get(
            NAVER_AUTOCOMPLETE_URL,
            params=params,
            headers={"User-Agent": "Mozilla/5.0"},
            timeout=AUTOCOMPLETE_TIMEOUT,
        )
        if response.status_code != 200:
            return []
        data = response.json()
    except Exception:
        return []
    out: list[str] = []
    for group in data.get("items") or []:
        if not isinstance(group, list):
            continue
        for entry in group:
            keyword = ""
            if isinstance(entry, list) and entry:
                keyword = _tag(entry[0])
            elif isinstance(entry, str):
                keyword = _tag(entry)
            if len(keyword) < 2 or keyword in STOPWORDS:
                continue
            if keyword not in out:
                out.append(keyword)
            if len(out) >= max_results:
                return out
    return out


def discover_naver_keywords(query: str, seed_text: str = "", limit: int = 500) -> dict[str, object]:
    hints = build_hint_keywords(query, seed_text)
    searchad = searchad_keyword_tool(hints)
    autocomplete: list[str] = []
    for hint in hints[:5]:
        for keyword in naver_autocomplete(hint, max_results=12):
            if keyword not in autocomplete:
                autocomplete.append(keyword)
        time.sleep(0.15)

    merged: dict[str, dict[str, object]] = {}
    filtered_searchad = [
        item for item in (searchad.get("keywords") or [])
        if _is_related(item.get("keyword"), hints)
    ]
    filtered_autocomplete = [
        keyword for keyword in autocomplete
        if _is_related(keyword, hints)
    ]

    for item in filtered_searchad:
        keyword = _text(item.get("keyword"))
        if keyword:
            merged[keyword] = dict(item)
    for index, keyword in enumerate(filtered_autocomplete):
        current = merged.setdefault(keyword, {
            "keyword": keyword,
            "monthlyPcQcCnt": 0,
            "monthlyMobileQcCnt": 0,
            "monthlyTotalQcCnt": 0,
            "compIdx": "",
            "score": 0,
            "source": "naver_autocomplete",
        })
        current["autocompleteRank"] = index + 1
        current["source"] = "naver_searchad_keywordstool+autocomplete" if current.get("source") != "naver_autocomplete" else "naver_autocomplete"
        current["score"] = round(float(current.get("score") or 0) + max(1.0, 14.0 - index * 0.2), 4)

    expanded = sorted(
        merged.values(),
        key=lambda item: (-int(item.get("monthlyTotalQcCnt") or 0), -float(item.get("score") or 0), _text(item.get("keyword"))),
    )[:limit]
    return {
        "status": "ok" if expanded else "empty",
        "query": query,
        "hints": hints,
        "searchAdStatus": "ok" if searchad.get("ok") else _text(searchad.get("reason")),
        "searchAdKeywords": filtered_searchad[:120],
        "autocompleteKeywords": filtered_autocomplete[:120],
        "expandedKeywords": expanded,
    }
